// Track C diff — TS/CS golden 결과 쌍을 비교하여 불일치 건수를 수집한다.
//
// 입력: healthcheck/raw/golden-ts-*.json, golden-cs-*.json
// 출력: healthcheck/raw/parity-raw-{date}.json + healthcheck/reports/parity-{date}.md

import * as fs from "fs";
import * as path from "path";
import { Finding, ScanReport, emptyReport, addFinding, makeFingerprint } from "../types";

const RAW_DIR = path.resolve(__dirname, "..", "raw");
const REPORTS_DIR = path.resolve(__dirname, "..", "reports");

interface GoldenRow {
  id: string;
  input?: any;
  result?: string;
  error?: string;
  sequence?: string[];
  hash?: string;
}

interface GoldenFile {
  operation: string;
  iterations?: number;
  rows: GoldenRow[];
}

function loadGolden(name: string, side: "ts" | "cs"): GoldenFile | null {
  const filePath = path.join(RAW_DIR, `golden-${side}-${name}.json`);
  if (!fs.existsSync(filePath)) return null;
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch (e: any) {
    console.error(`[parity] ${filePath} 파싱 실패: ${e.message}`);
    return null;
  }
}

function makeFinding(
  suite: string,
  id: string,
  field: string,
  csResult: string,
  tsResult: string,
  firstSeen: string,
): Finding {
  const file = `golden/${suite}.json`;
  const message = `[${suite}:${id}] ${field}: CS=${csResult} / TS=${tsResult}`;
  return {
    ruleId: `PARITY-${suite.toUpperCase()}`,
    ruleName: `동등성 diff — ${suite}`,
    severity: suite === "bigint" ? "S2" : suite === "modvalue" ? "S2" : "S3",
    category: "gameplay",
    file,
    line: 0,
    message,
    snippet: `CS=${csResult} / TS=${tsResult}`,
    fingerprint: makeFingerprint(file, 0, `parity:${suite}:${id}`),
  };
}

// 숫자 문자열 정규화 — CS/TS 양쪽의 직렬화 포맷 차이를 제거
// 예:
//   "1E+100" ↔ "1e+100" → 같음
//   "5E-06"  ↔ "0.000005" → 같음
//   "824.46461182523478" ↔ "824.4646118252348" → 12자리 precision 통일
//   "1E+20"  ↔ "100000000000000000000" → 같음
//   "ERROR" / "NaN" / "Infinity" / "123n" (BigInt) → 그대로
//   "FLOAT_PER:0.5" → prefix 분리 후 0.5 정규화
const PRECISION = 12;
function normalizeNumericToken(raw: string): string {
  // 타입 prefix 분리: "FLOAT_PER:0.5" → ["FLOAT_PER", "0.5"]
  const typeMatch = /^([A-Z_]+):(.+)$/.exec(raw);
  const prefix = typeMatch ? typeMatch[1] + ":" : "";
  const rest = typeMatch ? typeMatch[2] : raw;

  // 특수값: 문자열 그대로 통과
  if (
    rest === "ERROR" ||
    rest === "NaN" ||
    rest === "Infinity" ||
    rest === "-Infinity" ||
    rest === "(missing)" ||
    rest === "exists" ||
    rest === "missing"
  ) {
    return prefix + rest;
  }

  // BigInt suffix: "9007199254740992n" 그대로
  if (/^-?\d+n$/.test(rest)) {
    return prefix + rest;
  }

  // 숫자 파싱 시도
  const n = Number(rest);
  if (!Number.isFinite(n) && n !== 0) {
    return prefix + rest;
  }

  // 0 특수 처리
  if (n === 0) return prefix + "0";

  // toPrecision(12) + 소문자 e + 불필요한 0 제거
  let normalized = n.toPrecision(PRECISION);
  // JS는 이미 소문자 e, 하지만 혹시 모를 경우 통일
  normalized = normalized.replace(/E/g, "e");
  // 불필요한 trailing 0 제거: "1.00000000000e+20" → "1e+20"
  normalized = normalized.replace(/(\.\d*?)0+(e|$)/, "$1$2").replace(/\.(e|$)/, "$1");
  return prefix + normalized;
}

// 두 결과 문자열이 의미상 같은지 — 정규화 후 비교
function resultsEqual(a: string | undefined, b: string | undefined): boolean {
  const aStr = a ?? "(missing)";
  const bStr = b ?? "(missing)";
  if (aStr === bStr) return true;
  return normalizeNumericToken(aStr) === normalizeNumericToken(bStr);
}

// 두 시퀀스 문자열 배열 비교 — 정규화 적용, 첫 불일치 위치 반환
function compareSequences(a: string[], b: string[]): { mismatched: boolean; firstDiffIdx?: number } {
  const len = Math.min(a.length, b.length);
  for (let i = 0; i < len; i++) {
    if (!resultsEqual(a[i], b[i])) return { mismatched: true, firstDiffIdx: i };
  }
  if (a.length !== b.length) return { mismatched: true, firstDiffIdx: len };
  return { mismatched: false };
}

function compareRowSets(
  suite: string,
  tsRows: GoldenRow[],
  csRows: GoldenRow[],
  report: ScanReport,
  today: string,
): { total: number; mismatches: number; missing: number } {
  const tsMap = new Map<string, GoldenRow>();
  for (const r of tsRows) tsMap.set(r.id, r);
  const csMap = new Map<string, GoldenRow>();
  for (const r of csRows) csMap.set(r.id, r);

  const allIds = new Set<string>([...tsMap.keys(), ...csMap.keys()]);
  let mismatches = 0;
  let missing = 0;

  for (const id of allIds) {
    const ts = tsMap.get(id);
    const cs = csMap.get(id);

    if (!ts || !cs) {
      missing++;
      const finding = makeFinding(
        suite,
        id,
        "존재성",
        cs ? "exists" : "missing",
        ts ? "exists" : "missing",
        today,
      );
      addFinding(report, finding);
      continue;
    }

    // result 필드 비교 (점 단위 값) — 숫자 정규화 적용
    if (ts.result !== undefined || cs.result !== undefined) {
      const tsR = ts.result ?? "(missing)";
      const csR = cs.result ?? "(missing)";
      if (!resultsEqual(tsR, csR)) {
        mismatches++;
        addFinding(report, makeFinding(suite, id, "result", csR, tsR, today));
      }
    }

    // sequence 필드 비교 (PRNG 시드)
    if (ts.sequence && cs.sequence) {
      const cmp = compareSequences(ts.sequence, cs.sequence);
      if (cmp.mismatched) {
        mismatches++;
        const idx = cmp.firstDiffIdx ?? 0;
        const tsV = ts.sequence[idx] ?? "(oob)";
        const csV = cs.sequence[idx] ?? "(oob)";
        addFinding(
          report,
          makeFinding(suite, id, `sequence[${idx}]`, csV, tsV, today),
        );
      }
    }
  }

  return { total: allIds.size, mismatches, missing };
}

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

interface SuiteStats {
  suite: string;
  total: number;
  mismatches: number;
  missing: number;
  tsLoaded: boolean;
  csLoaded: boolean;
}

async function main(): Promise<void> {
  if (!fs.existsSync(RAW_DIR)) fs.mkdirSync(RAW_DIR, { recursive: true });
  if (!fs.existsSync(REPORTS_DIR)) fs.mkdirSync(REPORTS_DIR, { recursive: true });

  const date = todayString();
  const report = emptyReport("parity");
  const start = Date.now();

  const suites = ["bigint", "formula", "modvalue", "gachahash", "randomhash", "binomial", "shared_enums"];
  const stats: SuiteStats[] = [];

  for (const suite of suites) {
    const ts = loadGolden(suite, "ts");
    const cs = loadGolden(suite, "cs");
    const tsLoaded = !!ts;
    const csLoaded = !!cs;

    if (!tsLoaded || !csLoaded) {
      report.summary.errors.push(
        `[${suite}] 로딩 실패: TS=${tsLoaded ? "OK" : "missing"}, CS=${csLoaded ? "OK" : "missing"}`,
      );
      stats.push({ suite, total: 0, mismatches: 0, missing: 0, tsLoaded, csLoaded });
      continue;
    }

    const r = compareRowSets(suite, ts!.rows, cs!.rows, report, date);
    stats.push({ suite, ...r, tsLoaded, csLoaded });
    console.log(
      `[parity] ${suite}: ${r.total}쌍 — 불일치 ${r.mismatches}, 누락 ${r.missing}`,
    );
  }

  report.durationMs = Date.now() - start;

  // JSON
  const jsonPath = path.join(RAW_DIR, `parity-raw-${date}.json`);
  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2), "utf8");

  // Markdown
  const mdPath = path.join(REPORTS_DIR, `parity-${date}.md`);
  const md: string[] = [];
  md.push(`# 동등성 리포트 (Track C) — ${date}`);
  md.push("");
  md.push(`- **총 불일치**: ${report.summary.totalFindings}건`);
  md.push(`- **소요**: ${(report.durationMs / 1000).toFixed(2)}s`);
  md.push("");
  md.push(`## Suite별 통계`);
  md.push("| Suite | 쌍 | 불일치 | 누락 | TS | CS |");
  md.push("|---|---:|---:|---:|:-:|:-:|");
  for (const s of stats) {
    md.push(`| ${s.suite} | ${s.total} | ${s.mismatches} | ${s.missing} | ${s.tsLoaded ? "✓" : "✗"} | ${s.csLoaded ? "✓" : "✗"} |`);
  }
  md.push("");

  if (report.summary.errors.length > 0) {
    md.push(`## ⚠ 로딩 에러`);
    for (const e of report.summary.errors) md.push(`- ${e}`);
    md.push("");
  }

  if (report.findings.length > 0) {
    md.push(`## 불일치 상세 (최대 200)`);
    md.push("| Suite / ID | 필드 | CS | TS |");
    md.push("|---|---|---|---|");
    for (const f of report.findings.slice(0, 200)) {
      const parts = (f.snippet || "").split(" / ");
      const cs = (parts[0] || "").replace(/^CS=/, "").slice(0, 60);
      const ts = (parts[1] || "").replace(/^TS=/, "").slice(0, 60);
      const msgMatch = /^\[(.+?):(.+?)\] (.+?):/.exec(f.message);
      const suite = msgMatch?.[1] ?? "?";
      const id = msgMatch?.[2] ?? "?";
      const field = msgMatch?.[3] ?? "?";
      md.push(`| ${suite}:${id} | ${field} | ${cs} | ${ts} |`);
    }
  }

  fs.writeFileSync(mdPath, md.join("\n"), "utf8");

  console.log("");
  console.log(`[parity] 완료`);
  console.log(`  총 불일치: ${report.summary.totalFindings}건`);
  console.log(`  JSON: ${jsonPath}`);
  console.log(`  Markdown: ${mdPath}`);
}

// 단독 실행 진입점
if (require.main === module) {
  main().catch((e) => {
    console.error("[parity] 치명적 에러:", e);
    process.exit(1);
  });
}

export { main as runParity };
