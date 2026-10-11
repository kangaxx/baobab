/* ============================================================================
   AIWorkLog 低 Token 调用存储过程（daily-ai-worklog skill 扩展）
   用途：用 EXEC 短命令替代手写 INSERT/统计 SQL，时长与间隔由 SQL 计算
   幂等：可重复执行
   兼容：纯 INSERT (ReportDate,Title,Detail) 的旧调用方式不受影响，
         AssignedAt/CompletedAt 为可空列，不填则统计标注"无法计算"
   ========================================================================== */
USE AIWorkLog;
GO

-- 可选时间列：供 SQL 直接计算工作时长与任务间隔
IF COL_LENGTH('dbo.DailyTask', 'AssignedAt') IS NULL
    ALTER TABLE dbo.DailyTask ADD AssignedAt TIME NULL;
IF COL_LENGTH('dbo.DailyTask', 'CompletedAt') IS NULL
    ALTER TABLE dbo.DailyTask ADD CompletedAt TIME NULL;
GO

-- 1. 查询当日日报状态（替代手写 SELECT + 判断逻辑）
CREATE OR ALTER PROCEDURE dbo.sp_CheckToday
    @ReportDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ReportDate IS NULL SET @ReportDate = CAST(GETDATE() AS DATE);
    DECLARE @exists BIT = CASE WHEN EXISTS (SELECT 1 FROM dbo.DailyReport WHERE ReportDate = @ReportDate) THEN 1 ELSE 0 END;
    SELECT @ReportDate AS ReportDate,
           @exists AS [Exists],
           (SELECT CreateUser FROM dbo.DailyReport WHERE ReportDate = @ReportDate) AS CreateUser,
           (SELECT COUNT(*) FROM dbo.DailyTask WHERE ReportDate = @ReportDate) AS TaskCount,
           (SELECT COUNT(*) FROM dbo.DailyTask WHERE ReportDate = @ReportDate) + 1 AS NextTaskIndex,
           CASE WHEN @exists = 1 THEN 'add_tasks_only' ELSE 'create_new_report' END AS NextAction;
END
GO

-- 2. 创建当日主表（已存在则跳过，不覆盖 CreateUser/Context）
CREATE OR ALTER PROCEDURE dbo.sp_UpsertDailyReport
    @CreateUser NVARCHAR(100),
    @Context    NVARCHAR(MAX) = NULL,
    @ReportDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ReportDate IS NULL SET @ReportDate = CAST(GETDATE() AS DATE);
    IF NOT EXISTS (SELECT 1 FROM dbo.DailyReport WHERE ReportDate = @ReportDate)
        INSERT INTO dbo.DailyReport (ReportDate, CreateUser, Context)
        VALUES (@ReportDate, @CreateUser, @Context);
    SELECT @ReportDate AS ReportDate, @@ROWCOUNT AS Inserted;
END
GO

-- 3. 追加任务明细（主表不存在时报错提示先建主表）
CREATE OR ALTER PROCEDURE dbo.sp_AddDailyTask
    @Title       NVARCHAR(500),
    @Detail      NVARCHAR(MAX) = NULL,
    @AssignedAt  TIME = NULL,
    @CompletedAt TIME = NULL,
    @ReportDate  DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ReportDate IS NULL SET @ReportDate = CAST(GETDATE() AS DATE);
    IF NOT EXISTS (SELECT 1 FROM dbo.DailyReport WHERE ReportDate = @ReportDate)
    BEGIN
        RAISERROR(N'当日日报主表不存在，请先调用 sp_UpsertDailyReport', 16, 1);
        RETURN;
    END
    INSERT INTO dbo.DailyTask (ReportDate, Title, Detail, AssignedAt, CompletedAt)
    VALUES (@ReportDate, @Title, @Detail, @AssignedAt, @CompletedAt);
    SELECT CONVERT(INT, SCOPE_IDENTITY()) AS TaskId;
END
GO

-- 4. 当日汇总：任务列表 + 时长/间隔由 SQL 计算 + 缺口标注 + 总计
CREATE OR ALTER PROCEDURE dbo.sp_DailySummary
    @ReportDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @ReportDate IS NULL SET @ReportDate = CAST(GETDATE() AS DATE);

    ;WITH t AS (
        SELECT Id, Title, AssignedAt, CompletedAt,
               CASE WHEN AssignedAt IS NOT NULL AND CompletedAt IS NOT NULL
                    THEN DATEDIFF(SECOND, CAST(AssignedAt AS DATETIME), CAST(CompletedAt AS DATETIME))
               END AS DurationSec
        FROM dbo.DailyTask
        WHERE ReportDate = @ReportDate
    ),
    g AS (
        SELECT t.*,
               CASE WHEN t.AssignedAt IS NOT NULL
                     AND LAG(t.CompletedAt) OVER (ORDER BY t.Id) IS NOT NULL
                    THEN DATEDIFF(SECOND,
                                  CAST(LAG(t.CompletedAt) OVER (ORDER BY t.Id) AS DATETIME),
                                  CAST(t.AssignedAt AS DATETIME))
               END AS IntervalSec
        FROM t
    )
    SELECT g.Id,
           g.Title,
           CONVERT(VARCHAR(8), g.AssignedAt, 108)  AS AssignedAt,
           CONVERT(VARCHAR(8), g.CompletedAt, 108) AS CompletedAt,
           CASE WHEN g.DurationSec IS NULL THEN N'无法计算'
                ELSE CONCAT(g.DurationSec / 3600, 'h ', (g.DurationSec % 3600) / 60, 'm ', g.DurationSec % 60, 's')
           END AS Duration,
           CASE WHEN g.Id = (SELECT MIN(Id) FROM g) THEN N'—'
                WHEN g.IntervalSec IS NULL THEN N'无法计算'
                ELSE CONCAT(g.IntervalSec / 3600, 'h ', (g.IntervalSec % 3600) / 60, 'm ', g.IntervalSec % 60, 's')
           END AS IntervalFromPrev
    FROM g
    ORDER BY g.Id;

    SELECT COUNT(*) AS TotalTasks,
           SUM(CASE WHEN AssignedAt IS NOT NULL AND CompletedAt IS NOT NULL THEN 1 ELSE 0 END) AS ComputableTasks,
           CASE WHEN SUM(DATEDIFF(SECOND, CAST(AssignedAt AS DATETIME), CAST(CompletedAt AS DATETIME))) IS NULL THEN N'无法计算'
                ELSE CONCAT(SUM(DATEDIFF(SECOND, CAST(AssignedAt AS DATETIME), CAST(CompletedAt AS DATETIME))) / 3600, 'h ',
                            SUM(DATEDIFF(SECOND, CAST(AssignedAt AS DATETIME), CAST(CompletedAt AS DATETIME))) % 3600 / 60, 'm')
           END AS TotalDuration
    FROM dbo.DailyTask
    WHERE ReportDate = @ReportDate;
END
GO
