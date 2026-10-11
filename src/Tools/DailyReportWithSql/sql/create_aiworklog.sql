/* ============================================================================
   AIWorkLog 数据库初始化脚本（daily-ai-worklog skill 依赖）
   用途：创建 AIWorkLog 数据库、DailyReport（主表）、DailyTask（明细表）
        及 60 天删除保护 INSTEAD OF DELETE 触发器
   认证：Windows 集成认证
   执行：sqlcmd -S localhost -E -i create_aiworklog.sql
   幂等：全部对象均带 IF NOT EXISTS 判断，可重复执行
   ========================================================================== */

-- 1. 创建数据库
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'AIWorkLog')
BEGIN
    CREATE DATABASE AIWorkLog;
END
GO

USE AIWorkLog;
GO

-- 2. 主表 DailyReport：每天一条记录，ReportDate 为主键
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DailyReport' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.DailyReport (
        ReportDate DATE           NOT NULL CONSTRAINT PK_DailyReport PRIMARY KEY,
        CreateUser NVARCHAR(100)  NOT NULL,           -- 首次写入的 AI 签名，之后不修改
        Context    NVARCHAR(MAX)  NULL                -- git版本等扩展信息，每行一个键值
    );
END
GO

-- 3. 明细表 DailyTask：每个任务一条记录，通过 ReportDate 外键关联主表
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DailyTask' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.DailyTask (
        Id         INT            NOT NULL IDENTITY(1,1) CONSTRAINT PK_DailyTask PRIMARY KEY,
        ReportDate DATE           NOT NULL,
        Title      NVARCHAR(500)  NOT NULL,           -- 形如 "T01 — 任务名称"
        Detail     NVARCHAR(MAX)  NULL,               -- 任务全部结构化信息（见 skill 模板）
        CONSTRAINT FK_DailyTask_DailyReport FOREIGN KEY (ReportDate)
            REFERENCES dbo.DailyReport (ReportDate)
    );
END
GO

-- 4. 60 天删除保护：日报日期距今 ≤ 60 天禁止删除（触发器报错回滚）
CREATE OR ALTER TRIGGER dbo.TR_DailyReport_InsteadOfDelete ON dbo.DailyReport
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE DATEDIFF(DAY, ReportDate, CAST(GETDATE() AS DATE)) <= 60)
    BEGIN
        RAISERROR(N'DailyReport 60 天删除保护：距今不超过 60 天的日报禁止删除', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
    DELETE r FROM dbo.DailyReport r
    INNER JOIN deleted d ON r.ReportDate = d.ReportDate;
END
GO

CREATE OR ALTER TRIGGER dbo.TR_DailyTask_InsteadOfDelete ON dbo.DailyTask
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE DATEDIFF(DAY, ReportDate, CAST(GETDATE() AS DATE)) <= 60)
    BEGIN
        RAISERROR(N'DailyTask 60 天删除保护：距今不超过 60 天的任务明细禁止删除', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
    DELETE t FROM dbo.DailyTask t
    INNER JOIN deleted d ON t.Id = d.Id;
END
GO

-- 5. 验证输出
SELECT name AS [table] FROM sys.tables WHERE name IN ('DailyReport','DailyTask');
SELECT name AS [trigger] FROM sys.triggers WHERE parent_class = 1;
GO
