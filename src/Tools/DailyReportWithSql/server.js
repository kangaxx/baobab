import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { execFile } from "node:child_process";
import { writeFileSync, unlinkSync, appendFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { randomUUID } from "node:crypto";

const SQLCMD = "sqlcmd";
const SQLSERVER = process.env.WORKLOG_SQLSERVER ?? "localhost";
const DB = "AIWorkLog";
const DEFAULT_SIG = process.env.AI_SIGNATURE ?? "Unknown Agent";

// 本地日志：逐行记录 stdio 上收到的 JSON-RPC 原文；写日志失败不影响主流程
const LOG_DIR =
  process.env.WORKLOG_LOG_DIR ??
  join(process.env.USERPROFILE ?? tmpdir(), ".copilot", "daily-logs");
mkdirSync(LOG_DIR, { recursive: true });

let stdinBuf = "";
process.stdin.on("data", (chunk) => {
  stdinBuf += chunk.toString("utf8");
  let i;
  while ((i = stdinBuf.indexOf("\n")) >= 0) {
    const line = stdinBuf.slice(0, i).trim();
    stdinBuf = stdinBuf.slice(i + 1);
    if (!line) continue;
    const day = new Date().toISOString().slice(0, 10).replace(/-/g, "");
    try {
      appendFileSync(join(LOG_DIR, `mcp-${day}.log`), `[${new Date().toISOString()}] ${line}\n`, "utf8");
    } catch { /* 忽略日志写入失败 */ }
  }
});

// 生成 NVARCHAR 字面量；null/undefined -> NULL
function nq(v) {
  if (v === null || v === undefined || v === "") return "NULL";
  return "N'" + String(v).replace(/'/g, "''") + "'";
}

// 写临时 .sql 文件（UTF-8 BOM）再交给 sqlcmd 执行，彻底规避命令行引号/中文转义问题
function runSql(sql) {
  return new Promise((resolve, reject) => {
    const f = join(tmpdir(), `worklog-${randomUUID()}.sql`);
    writeFileSync(f, "﻿SET NOCOUNT ON;\n" + sql + "\n", "utf8");
    execFile(
      SQLCMD,
      ["-S", SQLSERVER, "-d", DB, "-E", "-b", "-f", "65001", "-y", "0", "-i", f],
      { timeout: 30000, maxBuffer: 8 * 1024 * 1024 },
      (err, stdout, stderr) => {
        try { unlinkSync(f); } catch {}
        if (err) reject(new Error(`${stderr ?? ""}\n${stdout ?? ""}`.trim()));
        else resolve(stdout.trim());
      }
    );
  });
}

async function wrap(fn) {
  try {
    const out = await fn();
    return { content: [{ type: "text", text: out || "(ok)" }] };
  } catch (e) {
    return { content: [{ type: "text", text: "ERROR: " + e.message }], isError: true };
  }
}

const server = new McpServer({ name: "daily-report", version: "1.0.0" });

server.registerTool(
  "check_today",
  {
    description: "查询当日 AI 工作日报状态：是否存在、创建者、任务数、下一任务编号、建议动作",
    inputSchema: { report_date: z.string().optional().describe("YYYY-MM-DD，默认当天") },
  },
  async ({ report_date }) =>
    wrap(() => runSql(`EXEC dbo.sp_CheckToday${report_date ? ` @ReportDate=${nq(report_date)}` : ""};`))
);

server.registerTool(
  "start_task",
  {
    description:
      "任务开始（机械记录）：写入用户原始 prompt，自动分配 T 编号并记录真实开始时间，返回 TaskId 供 finish_task 使用。主表不存在时自动创建。",
    inputSchema: {
      prompt: z.string().describe("用户原始 prompt 全文，原样传入，不要改写"),
      ai_signature: z.string().optional().describe("AI签名，如 'Copilot (kimi-k3)'；缺省取环境变量 AI_SIGNATURE"),
      git_commit: z.string().optional(),
      project_path: z.string().optional(),
      report_date: z.string().optional().describe("仅供测试，生产留空"),
    },
  },
  async (a) =>
    wrap(() =>
      runSql(
        `EXEC dbo.sp_StartTask @Prompt=${nq(a.prompt)}, @AiSignature=${nq(
          a.ai_signature ?? DEFAULT_SIG
        )}, @GitCommit=${nq(a.git_commit)}, @ProjectPath=${nq(a.project_path)}${
          a.report_date ? `, @ReportDate=${nq(a.report_date)}` : ""
        };`
      )
    )
);

server.registerTool(
  "finish_task",
  {
    description: "任务完成（机械记录）：无脑写入结论全文，自动记录真实结束时间；已完成的任务不允许覆盖",
    inputSchema: {
      task_id: z.number().describe("start_task 返回的 TaskId"),
      result: z.string().describe("任务完成结论/交付物说明全文"),
    },
  },
  async ({ task_id, result }) =>
    wrap(() => runSql(`EXEC dbo.sp_FinishTask @TaskId=${task_id}, @Result=${nq(result)};`))
);

server.registerTool(
  "daily_summary",
  {
    description: "当日汇总：任务列表、工作时长、任务间隔、总计均由 SQL 计算，无需读回明细手工统计",
    inputSchema: { report_date: z.string().optional().describe("YYYY-MM-DD，默认当天") },
  },
  async ({ report_date }) =>
    wrap(() => runSql(`EXEC dbo.sp_DailySummary${report_date ? ` @ReportDate=${nq(report_date)}` : ""};`))
);

await server.connect(new StdioServerTransport());
