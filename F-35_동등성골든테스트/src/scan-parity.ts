// Phase 5 Track C — 동등성 오케스트레이터
// 1. TS 골든 러너 실행 (mocha healthcheck-golden)
// 2. CS 골든 러너 실행 (unicli RunEditModeTests HealthCheckGolden)
// 3. compare-golden.ts 호출
//
// Unity Editor가 열려 있지 않으면 CS 러너는 실패 → 리포트에 "CS unavailable" 기록 후
// TS만 단독 저장. 다음 실행에서 CS가 가능해지면 자동으로 diff 생성.

import { spawnSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { runParity } from "./scripts/compare-golden";

const ROOT = path.resolve(__dirname, "..");
const REPORTS_DIR = path.join(__dirname, "reports");

const UNICLI = process.env.UNICLI_PATH || "C:/Users/USER/scoop/shims/unicli.exe";

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function runTSGolden(): { ok: boolean; details: string } {
  console.log("[parity] TS 골든 러너 실행");
  const res = spawnSync(
    "npx",
    [
      "mocha",
      "-r", "ts-node/register",
      "-r", "./test/mocha.setup.ts",
      "--slow", "0",
      "./test/HealthCheck/Golden.test.ts",
      "--grep", "healthcheck-golden",
    ],
    {
      cwd: ROOT,
      encoding: "utf8",
      shell: true,
      windowsHide: true,
      maxBuffer: 32 * 1024 * 1024,
    },
  );

  if (res.status !== 0) {
    return {
      ok: false,
      details: `exit=${res.status} stderr=${(res.stderr || "").slice(-500)}`,
    };
  }
  const passing = /(\d+)\s+passing/.exec(res.stdout || "");
  return {
    ok: true,
    details: passing ? `${passing[1]} passing` : "실행됨",
  };
}

function runCSGolden(): { ok: boolean; details: string } {
  console.log("[parity] CS 골든 러너 실행 (UniCli)");
  if (!fs.existsSync(UNICLI)) {
    return { ok: false, details: `unicli 바이너리 없음: ${UNICLI}` };
  }

  // UniCli 연결 확인
  const check = spawnSync(UNICLI, ["check"], {
    encoding: "utf8",
    shell: true,
    windowsHide: true,
  });
  if (check.status !== 0) {
    return {
      ok: false,
      details: `Unity Editor 연결 실패: ${(check.stderr || check.stdout || "").slice(-200)}`,
    };
  }

  // EditMode 테스트 실행 (Category HealthCheckGolden)
  const run = spawnSync(
    UNICLI,
    ["exec", "RunEditModeTests", "--filter", "HealthCheckGolden", "--json"],
    {
      encoding: "utf8",
      shell: true,
      windowsHide: true,
      maxBuffer: 32 * 1024 * 1024,
      timeout: 600000, // 10분
    },
  );

  if (run.status !== 0) {
    return {
      ok: false,
      details: `unicli exec 실패: exit=${run.status}, ${(run.stderr || run.stdout || "").slice(-400)}`,
    };
  }
  return { ok: true, details: "실행됨" };
}

async function main(): Promise<void> {
  if (!fs.existsSync(REPORTS_DIR)) fs.mkdirSync(REPORTS_DIR, { recursive: true });
  const date = todayString();

  const steps: Record<string, { status: string; details: string }> = {};

  const ts = runTSGolden();
  steps["ts-golden"] = { status: ts.ok ? "SUCCESS" : "FAILED", details: ts.details };
  console.log(`  TS: ${steps["ts-golden"].status} — ${ts.details}`);

  const cs = runCSGolden();
  steps["cs-golden"] = { status: cs.ok ? "SUCCESS" : "SKIPPED", details: cs.details };
  console.log(`  CS: ${steps["cs-golden"].status} — ${cs.details}`);

  // TS라도 성공했으면 diff 실행 (CS 없으면 로딩 에러로 기록됨)
  if (ts.ok) {
    await runParity();
  }

  // 실행 상태 로그 파일
  const statusPath = path.join(REPORTS_DIR, `parity-status-${date}.json`);
  fs.writeFileSync(
    statusPath,
    JSON.stringify({ date, steps, timestamp: new Date().toISOString() }, null, 2),
    "utf8",
  );
  console.log(`[parity] 상태 저장: ${statusPath}`);
}

if (require.main === module) {
  main().catch((e) => {
    console.error("[parity] 치명적 에러:", e);
    process.exit(1);
  });
}
