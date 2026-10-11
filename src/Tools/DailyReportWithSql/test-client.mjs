import { spawn } from "node:child_process";
import { createInterface } from "node:readline";

const srv = spawn("node", ["server.js"], {
  cwd: process.cwd(),
  stdio: ["pipe", "pipe", "inherit"],
});

const rl = createInterface({ input: srv.stdout });
let id = 0;
const pending = new Map();
rl.on("line", (line) => {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  if (msg.id !== undefined && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  }
});

function call(method, params) {
  const myId = ++id;
  srv.stdin.write(JSON.stringify({ jsonrpc: "2.0", id: myId, method, params }) + "\n");
  return new Promise((res) => pending.set(myId, res));
}
function notify(method, params) {
  srv.stdin.write(JSON.stringify({ jsonrpc: "2.0", method, params }) + "\n");
}
const show = (label, r) => {
  const text = r?.result?.content?.[0]?.text ?? JSON.stringify(r);
  console.log(`\n===== ${label} =====\n${text}`);
};

const init = await call("initialize", {
  protocolVersion: "2024-11-05",
  capabilities: {},
  clientInfo: { name: "test-client", version: "0.1" },
});
console.log("initialize:", init?.result?.serverInfo?.name ?? JSON.stringify(init));
notify("notifications/initialized", {});

const tools = await call("tools/list", {});
console.log("tools:", tools.result.tools.map((t) => t.name).join(", "));

show("check_today(2020-01-03)", await call("tools/call", {
  name: "check_today", arguments: { report_date: "2020-01-03" },
}));

const start = await call("tools/call", {
  name: "start_task",
  arguments: {
    prompt: "帮我改造M05砂光机线程的校验逻辑\n第二行不应出现在标题里",
    ai_signature: "Copilot (kimi-k3)",
    git_commit: "abc1234",
    report_date: "2020-01-03",
  },
});
show("start_task", start);
const taskId = parseInt(/(\d+)/.exec(start.result.content[0].text)?.[1] ?? "0", 10);
console.log("parsed TaskId:", taskId);

show("finish_task", await call("tools/call", {
  name: "finish_task",
  arguments: { task_id: taskId, result: "改造完成：校验逻辑改为走 MES API，交付 3 个文件。" },
}));

show("finish_task 重复调用（应报错）", await call("tools/call", {
  name: "finish_task",
  arguments: { task_id: taskId, result: "试图覆盖" },
}));

show("daily_summary(2020-01-03)", await call("tools/call", {
  name: "daily_summary", arguments: { report_date: "2020-01-03" },
}));

srv.kill();
process.exit(0);
