/* ============================================================================
   AIWorkLog MCP 机械记录扩展（daily-ai-worklog skill）
   用途：支持"无脑记录"模式——任务开始/结束由 MCP 服务端打真实时间戳，
         AI 不组织 SQL、不上报时间，token 消耗趋近于零
   幂等：可重复执行
   ========================================================================== */
USE AIWorkLog;
GO

-- 长文本字段：原始用户 Prompt 与任务完成结论
IF COL_LENGTH('dbo.DailyTask', 'Prompt') IS NULL
    ALTER TABLE dbo.DailyTask ADD Prompt NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.DailyTask', 'Result') IS NULL
    ALTER TABLE dbo.DailyTask ADD Result NVARCHAR(MAX) NULL;
GO

-- 1. 任务开始：主表不存在则自动创建；T 编号自动分配；开始时间取服务器真实时间
--    返回 TaskId，供 sp_FinishTask 回写
CREATE OR ALTER PROCEDURE dbo.sp_StartTask
    @Prompt      NVARCHAR(MAX),
    @AiSignature NVARCHAR(100),
    @GitCommit   NVARCHAR(64)  = NULL,
    @ProjectPath NVARCHAR(500) = NULL,
    @ReportDate  DATE = NULL          -- 仅供测试；生产环境留空取当天
AS
BEGIN
    SET NOCOUNT ON;
    IF @ReportDate IS NULL SET @ReportDate = CAST(GETDATE() AS DATE);

    IF NOT EXISTS (SELECT 1 FROM dbo.DailyReport WHERE ReportDate = @ReportDate)
        INSERT INTO dbo.DailyReport (ReportDate, CreateUser, Context)
        VALUES (@ReportDate, @AiSignature,
                CASE WHEN @GitCommit IS NOT NULL THEN N'git版本: ' + @GitCommit ELSE NULL END);

    DECLARE @idx INT = (SELECT COUNT(*) + 1 FROM dbo.DailyTask WHERE ReportDate = @ReportDate);

    -- Title：T 编号 + Prompt 首行截断 80 字符
    DECLARE @firstLine NVARCHAR(200) =
        LEFT(@Prompt, CASE WHEN CHARINDEX(CHAR(10), @Prompt) > 0
                           THEN CHARINDEX(CHAR(10), @Prompt) - 1 ELSE 200 END);
    SET @firstLine = LTRIM(RTRIM(REPLACE(@firstLine, CHAR(13), '')));
    DECLARE @title NVARCHAR(500) =
        N'T' + RIGHT('0' + CAST(@idx AS VARCHAR(3)), 2) + N' — ' + LEFT(@firstLine, 80);

    INSERT INTO dbo.DailyTask (ReportDate, Title, Prompt, AssignedAt)
    VALUES (@ReportDate, @title, @Prompt, CAST(GETDATE() AS TIME));

    SELECT CONVERT(INT, SCOPE_IDENTITY()) AS TaskId, @title AS Title;
END
GO

-- 2. 任务完成：回写结论与服务器真实结束时间；重复调用报错（防覆盖）
CREATE OR ALTER PROCEDURE dbo.sp_FinishTask
    @TaskId INT,
    @Result NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.DailyTask
    SET CompletedAt = CAST(GETDATE() AS TIME),
        Result      = @Result
    WHERE Id = @TaskId AND CompletedAt IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RAISERROR(N'TaskId 不存在或任务已完成（不允许覆盖）: %d', 16, 1, @TaskId);
        RETURN;
    END
    SELECT @TaskId AS TaskId, CONVERT(VARCHAR(8), GETDATE(), 108) AS CompletedAt;
END
GO
