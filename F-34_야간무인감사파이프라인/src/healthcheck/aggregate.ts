// Phase 5 통합 리포트 생성기
//
// 입력:
//   - healthcheck/raw/server-raw-{date}.json
//   - healthcheck/raw/parity-raw-{date}.json
//   - healthcheck/raw/client-raw-{date}.json
//   - healthcheck/reports/autofix-{date}.json
//   - healthcheck/state.json (전일 상태)
//
// 출력:
//   - Docs/plans/테스트/healthcheck/reports/{date}_audit-report.md (최종 사람 판독)
//   - healthcheck/state.json (갱신)
//   - healthcheck/reports/new-findings-{date}.md (신규만)

import * as fs from "fs";
import * as path from "path";
import { Finding, ScanReport, Severity } from "./types";
import { loadState, saveState, updateState, State, StateEntry, FindingStatus, ScanScope } from "./state-manager";

const ROOT = path.resolve(__dirname, "..");
const RAW_DIR = path.join(__dirname, "raw");
const LOCAL_REPORTS_DIR = path.join(__dirname, "reports");
const FINAL_REPORTS_DIR = path.resolve(
  __dirname,
  "..",
  "..",
  "..",
  "Docs",
  "plans",
  "테스트",
  "healthcheck",
  "reports",
);

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function loadRawReport(name: string): ScanReport | null {
  const filePath = path.join(RAW_DIR, name);
  if (!fs.existsSync(filePath)) return null;
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

interface AutofixJson {
  date: string;
  status: "SUCCESS" | "ROLLBACK" | "NO_CHANGES";
  reason?: string;
  commit?: string;
  steps: Array<{ name: string; status: string; details?: string }>;
  stats: { eslintFixedFiles: number; codemodFiles: number; codemodReplacements: number };
}

function loadAutofix(date: string): AutofixJson | null {
  const filePath = path.join(LOCAL_REPORTS_DIR, `autofix-${date}.json`);
  if (!fs.existsSync(filePath)) return null;
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch {
    return null;
  }
}

function ensureDirs(): void {
  for (const d of [RAW_DIR, LOCAL_REPORTS_DIR, FINAL_REPORTS_DIR]) {
    if (!fs.existsSync(d)) fs.mkdirSync(d, { recursive: true });
  }
}

function severityEmoji(s: Severity): string {
  return s === "S1" ? "🚨" : s === "S2" ? "⚠️" : s === "S3" ? "💡" : "🔸";
}

function aggregateAllFindings(
  server: ScanReport | null,
  parity: ScanReport | null,
  client: ScanReport | null,
): Finding[] {
  const all: Finding[] = [];
  if (server) all.push(...server.findings);
  if (parity) all.push(...parity.findings);
  if (client) all.push(...client.findings);
  return all;
}

function countBySeverity(list: Finding[]): Record<Severity, number> {
  const cnt: Record<Severity, number> = { S1: 0, S2: 0, S3: 0, S4: 0 };
  for (const f of list) cnt[f.severity]++;
  return cnt;
}

function main(): void {
  ensureDirs();
  const date = todayString();
  console.log(`[aggregate] 통합 리포트 생성 ${date}`);

  // 1. 각 트랙 결과 로드
  const server = loadRawReport(`server-raw-${date}.json`);
  const parity = loadRawReport(`parity-raw-${date}.json`);
  const client = loadRawReport(`client-raw-${date}.json`);
  const autofix = loadAutofix(date);

  if (!server) console.log("  ⚠ server-raw 없음");
  if (!parity) console.log("  ⚠ parity-raw 없음");
  if (!client) console.log("  ⚠ client-raw 없음");

  // 2. 전체 findings 집계 + state 갱신
  // 부분 스캔 보호: 이번에 실제로 돌아간 scanner만 activeScopes에 포함시켜
  // 미스캔 source의 과거 TRIAGED 항목이 FIXED로 잘못 전환되지 않게 한다.
  const activeScopes = new Set<ScanScope>();
  if (server) activeScopes.add("server");
  if (parity) activeScopes.add("parity");
  if (client) activeScopes.add("client");

  const allCurrent = aggregateAllFindings(server, parity, client);
  const state = loadState();
  const { newFindings, fixedFindings, stillOpen } = updateState(allCurrent, state, date, activeScopes);
  saveState(state);

  // 3. Markdown 리포트 작성
  const lines: string[] = [];
  lines.push(`# Phase 5 Audit Report — ${date}`);
  lines.push("");

  // --- Summary ---
  const newBySev = countBySeverity(newFindings);
  const openBySev: Record<Severity, number> = { S1: 0, S2: 0, S3: 0, S4: 0 };
  for (const entry of Object.values(state)) {
    if (entry.status !== "FIXED") openBySev[entry.severity]++;
  }

  lines.push(`## Summary`);
  lines.push(`- **신규 발견**: S1=${newBySev.S1} S2=${newBySev.S2} S3=${newBySev.S3} S4=${newBySev.S4}`);
  lines.push(`- **기존 OPEN**: ${Object.values(state).filter((e) => e.status === "OPEN").length}건`);
  lines.push(`- **TRIAGED**: ${Object.values(state).filter((e) => e.status === "TRIAGED").length}건`);
  lines.push(`- **금일 FIXED 전환**: ${fixedFindings.length}건`);
  lines.push("");

  // --- Autofix 섹션 ---
  lines.push(`## Autofix`);
  if (!autofix) {
    lines.push(`- **상태**: 실행 안 함`);
  } else {
    lines.push(`- **상태**: ${autofix.status}${autofix.reason ? " — " + autofix.reason : ""}`);
    lines.push(`- **ESLint 수정 파일**: ${autofix.stats.eslintFixedFiles}`);
    lines.push(`- **console→logDebug 교체**: ${autofix.stats.codemodReplacements}건 (${autofix.stats.codemodFiles}파일)`);
    if (autofix.commit) lines.push(`- **커밋**: \`${autofix.commit}\``);
    lines.push(`- **단계별**:`);
    for (const step of autofix.steps) {
      const icon = step.status === "success" ? "✅" : step.status === "failed" ? "❌" : "⏭";
      lines.push(`  - ${icon} ${step.name}${step.details ? " — " + step.details : ""}`);
    }
  }
  lines.push("");

  // --- S1 알림 ---
  const s1Open = Object.values(state).filter((e) => e.status !== "FIXED" && e.severity === "S1");
  if (s1Open.length > 0) {
    lines.push(`## 🚨 S1 — 즉시 확인 필요 (${s1Open.length})`);
    lines.push("");
    lines.push("| 파일 | 라인 | 룰 | 상태 | 최초 |");
    lines.push("|---|---:|---|---|---|");
    for (const e of s1Open.slice(0, 30)) {
      lines.push(`| ${e.file} | ${e.line} | ${e.ruleId} | ${e.status} | ${e.firstSeen} |`);
    }
    lines.push("");
  }

  // --- Track A 서버 ---
  lines.push(`## Track A: Server`);
  if (!server) {
    lines.push(`- 데이터 없음`);
  } else {
    const sev = server.summary.bySeverity;
    lines.push(`- 전체: ${server.summary.totalFindings}건 (S1=${sev.S1} S2=${sev.S2} S3=${sev.S3} S4=${sev.S4})`);
    lines.push(`- 상위 룰:`);
    const topRules = Object.entries(server.summary.byRule).sort((a, b) => b[1] - a[1]).slice(0, 10);
    for (const [rule, count] of topRules) {
      lines.push(`  - ${rule}: ${count}`);
    }
  }
  lines.push("");

  // --- Track C 동등성 ---
  lines.push(`## Track C: Parity (동등성)`);
  if (!parity) {
    lines.push(`- 데이터 없음`);
  } else {
    lines.push(`- 불일치: ${parity.summary.totalFindings}건`);
    if (parity.summary.errors.length > 0) {
      lines.push(`- 로딩 에러:`);
      for (const e of parity.summary.errors) lines.push(`  - ${e}`);
    }
  }
  lines.push("");

  // --- Track B 클라이언트 ---
  lines.push(`## Track B: Client`);
  if (!client) {
    lines.push(`- 데이터 없음`);
  } else {
    const sev = client.summary.bySeverity;
    lines.push(`- 전체: ${client.summary.totalFindings}건 (S1=${sev.S1} S2=${sev.S2} S3=${sev.S3} S4=${sev.S4})`);
    if (client.summary.errors.length > 0) {
      lines.push(`- 에러:`);
      for (const e of client.summary.errors) lines.push(`  - ${e}`);
    }
  }
  lines.push("");

  // --- 신규 발견 상세 ---
  if (newFindings.length > 0) {
    lines.push(`## 신규 발견 상세 (${newFindings.length}건)`);
    lines.push("");
    lines.push("| 파일 | 라인 | 룰 | 심각도 | 내용 |");
    lines.push("|---|---:|---|---|---|");
    for (const f of newFindings.slice(0, 100)) {
      const msg = (f.message || "").slice(0, 120).replace(/\|/g, "\\|");
      lines.push(`| ${f.file} | ${f.line} | ${f.ruleId} | ${severityEmoji(f.severity)}${f.severity} | ${msg} |`);
    }
    if (newFindings.length > 100) lines.push(`| ... | | | | (${newFindings.length - 100}건 생략) |`);
  }
  lines.push("");

  // --- Triage 큐 ---
  lines.push(`## Triage 큐`);
  lines.push(`- [ ] S1 신규 ${newBySev.S1}건${newBySev.S1 === 0 ? " ✅" : ""}`);
  lines.push(`- [ ] S2 신규 ${newBySev.S2}건`);
  lines.push(`- [ ] S3 신규 ${newBySev.S3}건`);
  lines.push(`- [ ] S4 신규 ${newBySev.S4}건`);
  lines.push("");

  // 4. 최종 리포트 작성
  const finalPath = path.join(FINAL_REPORTS_DIR, `${date}_audit-report.md`);
  fs.writeFileSync(finalPath, lines.join("\n"), "utf8");

  // 5. 신규 발견 단독 파일
  const newPath = path.join(LOCAL_REPORTS_DIR, `new-findings-${date}.md`);
  const newLines: string[] = [];
  newLines.push(`# 신규 발견 — ${date}`);
  newLines.push("");
  newLines.push(`- 총 ${newFindings.length}건 (S1=${newBySev.S1} S2=${newBySev.S2} S3=${newBySev.S3} S4=${newBySev.S4})`);
  newLines.push("");
  for (const f of newFindings) {
    newLines.push(`- **${f.severity}** \`${f.file}:${f.line}\` [${f.ruleId}] ${f.message}`);
  }
  fs.writeFileSync(newPath, newLines.join("\n"), "utf8");

  console.log(`[aggregate] 완료`);
  console.log(`  최종 리포트: ${finalPath}`);
  console.log(`  신규 발견: ${newPath} (${newFindings.length}건)`);
  console.log(`  state.json 갱신 완료 (${Object.keys(state).length} 항목)`);
}

if (require.main === module) {
  try {
    main();
  } catch (e: any) {
    console.error("[aggregate] 치명적 에러:", e);
    process.exit(1);
  }
}
