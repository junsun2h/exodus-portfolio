// Phase 5 전체 파이프라인 — /code-healthcheck 스킬의 실행 본체
//
// 실행 순서:
//   1. scan-server.ts     → server-raw-{date}.json
//   2. autofix-server.ts  → (stash/build/test/rollback 안전망)
//   3. scan-server.ts     → autofix 이후 재스캔
//   4. scan-parity.ts     → TS/CS 골든 실행 + diff
//   5. scan-client.ts     → client-raw-{date}.json (탐지만)
//   6. aggregate.ts       → 통합 리포트 + state 갱신
//
// 옵션:
//   --skip-autofix        autofix 단계 생략
//   --skip-client         클라 스캔 생략 (빠른 테스트용)
//   --skip-parity         동등성 스킵 (Unity 없을 때)

import { spawnSync } from "child_process";
import * as path from "path";

const ROOT = path.resolve(__dirname, "..");

function runStep(label: string, script: string, args: string[] = []): boolean {
  console.log("");
  console.log(`━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);
  console.log(`[run-all] ${label}`);
  console.log(`━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);

  const res = spawnSync(
    "npx",
    ["ts-node", `healthcheck/${script}`, ...args],
    {
      cwd: ROOT,
      encoding: "utf8",
      shell: true,
      windowsHide: true,
      stdio: "inherit",
      maxBuffer: 64 * 1024 * 1024,
    },
  );

  if (res.status !== 0) {
    console.log(`[run-all] ⚠ ${label} 실패 (exit=${res.status}) — 계속 진행`);
    return false;
  }
  return true;
}

async function main(): Promise<void> {
  const skipAutofix = process.argv.includes("--skip-autofix");
  const skipClient = process.argv.includes("--skip-client");
  const skipParity = process.argv.includes("--skip-parity");

  console.log(`[run-all] Phase 5 healthcheck 파이프라인 시작`);
  console.log(`  skip-autofix=${skipAutofix}, skip-parity=${skipParity}, skip-client=${skipClient}`);
  const startAt = Date.now();

  // 1. 서버 스캔 (1차)
  runStep("1. 서버 스캔 (pre-autofix)", "scan-server.ts");

  // 2. autofix
  if (!skipAutofix) {
    runStep("2. 서버 autofix", "autofix-server.ts");
    // 3. 서버 재스캔
    runStep("3. 서버 스캔 (post-autofix)", "scan-server.ts");
  }

  // 4. 동등성
  if (!skipParity) {
    runStep("4. 동등성 (TS + CS 골든)", "scan-parity.ts");
  }

  // 5. 클라이언트
  if (!skipClient) {
    runStep("5. 클라이언트 스캔 (Tier1)", "scan-client.ts");
  }

  // 6. 통합 리포트
  runStep("6. 통합 리포트 + state 갱신", "aggregate.ts");

  const elapsed = ((Date.now() - startAt) / 1000).toFixed(1);
  console.log("");
  console.log(`[run-all] 전체 완료 (${elapsed}s)`);
}

main().catch((e) => {
  console.error("[run-all] 치명적 에러:", e);
  process.exit(1);
});
