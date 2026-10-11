# DailyReportWithSql

把 AI Agent 的任务机械地记录进 SQL Server `AIWorkLog` 数据库的 MCP server：
任务开始时原样写入用户 prompt（服务端打真实开始时间戳并自动分配 T 编号），
任务完成时无脑写入结论（服务端打真实结束时间戳）。AI 不组织 SQL、不上报时间，
除两次工具调用外几乎零额外 token。

## 工具

| 工具 | 时机 | 说明 |
|------|------|------|
| `check_today` | 会话开始 | 查询当日日报状态（是否存在 / 任务数 / 下一编号 / 建议动作） |
| `start_task` | 收到用户任务时 | 原样传入 prompt，返回 `TaskId`；主表不存在时自动创建 |
| `finish_task` | 任务交付时 | 传入 `TaskId` + 结论全文；已完成的任务拒绝覆盖 |
| `daily_summary` | 汇总时 | 时长 / 任务间隔 / 总计全部由 SQL 计算返回 |

## 部署

```sh
# 1. 初始化数据库（幂等，三个脚本按序执行；-f 65001 必须加）
sqlcmd -S localhost -E -f 65001 -i sql\create_aiworklog.sql
sqlcmd -S localhost -E -f 65001 -i sql\procs_aiworklog.sql
sqlcmd -S localhost -E -f 65001 -i sql\procs_mcp_aiworklog.sql

# 2. 安装依赖
npm install

# 3. 注册到 MCP 客户端（示例：Copilot CLI 的 ~/.copilot/mcp-config.json）
```

```json
{
  "mcpServers": {
    "daily-report": {
      "command": "node",
      "args": ["<绝对路径>\\src\\Tools\\DailyReportWithSql\\server.js"],
      "env": {
        "WORKLOG_SQLSERVER": "localhost",
        "AI_SIGNATURE": "Copilot (kimi-k3)"
      }
    }
  }
}
```

## 环境变量

| 变量 | 默认 | 说明 |
|------|------|------|
| `WORKLOG_SQLSERVER` | `localhost` | SQL Server 实例（Windows 集成认证） |
| `AI_SIGNATURE` | `Unknown Agent` | 调用方未传签名时的兜底值 |

## 测试

```sh
npm test
```

测试客户端走 stdio 握手并依次调用全部 4 个工具（测试数据写入 `2020-01-03`，
超过 60 天保护期，可正常清理）。
