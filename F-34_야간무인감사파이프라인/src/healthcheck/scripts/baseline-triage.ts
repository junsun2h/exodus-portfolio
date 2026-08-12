// 최초 베이스라인 확정 — 현재 state.json의 모든 OPEN 항목을 TRIAGED로 일괄 전환
// 목적: "과거 부채" vs "신규 발견" 분리. 다음 실행부터 신규만 리포트 상단에 하이라이트
//
// 사용:
//   npx ts-node healthcheck/scripts/baseline-triage.ts
//
// 주의: 1회만 실행해야 함. 반복 실행은 새로 발견된 진짜 신규도 TRIAGED로 묻어버림.

import { loadState, saveState } from "../state-manager";

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function main(): void {
  const state = loadState();
  const today = todayString();

  let open = 0;
  let converted = 0;
  let already = 0;
  let fixed = 0;

  for (const [fp, entry] of Object.entries(state)) {
    if (entry.status === "OPEN") {
      entry.status = "TRIAGED";
      entry.note = entry.note || `baseline triage ${today}`;
      converted++;
      open++;
    } else if (entry.status === "TRIAGED") {
      already++;
    } else {
      fixed++;
    }
  }

  saveState(state);

  console.log(`[baseline-triage] 완료`);
  console.log(`  총 항목: ${Object.keys(state).length}`);
  console.log(`  OPEN → TRIAGED: ${converted}`);
  console.log(`  이미 TRIAGED: ${already}`);
  console.log(`  FIXED: ${fixed}`);
}

main();
