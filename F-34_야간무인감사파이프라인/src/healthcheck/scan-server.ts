// Phase 5 서버 스캐너 — 모든 룰을 실행하고 단일 JSON + Markdown 리포트 생성
import * as fs from "fs";
import * as path from "path";
import {
  PatternFile,
  Finding,
  ScanReport,
  emptyReport,
  addFinding,
  Severity,
} from "./types";
import { runRipgrep } from "./engines/ripgrep-engine";
import { runESLintRule, runESLintOnce, resetESLintCache } from "./engines/eslint-engine";
import { runTsPrune, runMadge, runTsComplex } from "./engines/tool-engines";

const ROOT = path.resolve(__dirname, "..");
const PATTERNS_PATH = path.join(__dirname, "patterns", "server-patterns.json");
const OUT_DIR = path.join(__dirname, "reports");
const RAW_DIR = path.join(__dirname, "raw");

async function runAll(): Promise<ScanReport> {
  const start = Date.now();
  const report = emptyReport("server");

  const patterns: PatternFile = JSON.parse(fs.readFileSync(PATTERNS_PATH, "utf8"));
  console.log(`[healthcheck:server] ${patterns.rules.length}개 룰 실행 시작`);

  // ESLint는 1회만 실행 후 cache
  resetESLintCache();
  let eslintPreloaded = false;

  for (const rule of patterns.rules) {
    const ruleStart = Date.now();
    let findings: Finding[] = [];
    let error: string | null = null;

    try {
      switch (rule.engine) {
        case "eslint": {
          if (!eslintPreloaded) {
            console.log(`  [eslint] 1회 preload 실행 중...`);
            runESLintOnce(ROOT);
            eslintPreloaded = true;
          }
          const res = runESLintRule(rule, ROOT);
          findings = res.findings;
          error = res.error;
          break;
        }
        case "ripgrep": {
          findings = await runRipgrep(rule, ROOT);
          break;
        }
        case "ts-prune": {
          const res = runTsPrune(rule, ROOT);
          findings = res.findings;
          error = res.error;
          break;
        }
        case "madge": {
          const res = runMadge(rule, ROOT);
          findings = res.findings;
          error = res.error;
          break;
        }
        case "ts-complex": {
          const res = runTsComplex(rule, ROOT);
          findings = res.findings;
          error = res.error;
          break;
        }
        case "custom": {
          const scriptPath = path.join(__dirname, rule.config.script);
          const mod = await import(scriptPath);
          findings = await mod.run(rule, ROOT);
          break;
        }
        default:
          error = `알 수 없는 engine: ${rule.engine}`;
      }
    } catch (e: any) {
      error = `[${rule.id}] 예외: ${e.message}`;
    }

    for (const f of findings) addFinding(report, f);
    if (error) report.summary.errors.push(error);

    const elapsed = Date.now() - ruleStart;
    console.log(
      `  [${rule.id}] ${rule.name} — ${findings.length}건 (${elapsed}ms)${error ? " ⚠ " + error : ""}`,
    );
  }

  report.durationMs = Date.now() - start;
  return report;
}

function todayString(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

function ensureDirs(): void {
  if (!fs.existsSync(OUT_DIR)) fs.mkdirSync(OUT_DIR, { recursive: true });
  if (!fs.existsSync(RAW_DIR)) fs.mkdirSync(RAW_DIR, { recursive: true });
}

function writeJsonReport(report: ScanReport, date: string): string {
  const filePath = path.join(RAW_DIR, `server-raw-${date}.json`);
  fs.writeFileSync(filePath, JSON.stringify(report, null, 2), "utf8");
  return filePath;
}

function writeMarkdownReport(report: ScanReport, date: string): string {
  const filePath = path.join(OUT_DIR, `server-${date}.md`);
  const lines: string[] = [];

  lines.push(`# 서버 감사 리포트 — ${date}`);
  lines.push("");
  lines.push(`- **스캔 시각**: ${report.timestamp}`);
  lines.push(`- **소요 시간**: ${(report.durationMs / 1000).toFixed(1)}s`);
  lines.push(`- **총 발견**: ${report.summary.totalFindings}건`);
  lines.push("");

  const sev = report.summary.bySeverity;
  lines.push(`## Summary`);
  lines.push(`- S1 (데이터 손상/재무): **${sev.S1}**${sev.S1 > 0 ? " 🚨" : ""}`);
  lines.push(`- S2 (게임플레이): **${sev.S2}**`);
  lines.push(`- S3 (성능/구조): **${sev.S3}**`);
  lines.push(`- S4 (유지보수): **${sev.S4}**`);
  lines.push("");

  if (report.summary.errors.length > 0) {
    lines.push(`## ⚠ 스캐너 에러 (${report.summary.errors.length})`);
    for (const err of report.summary.errors) {
      lines.push(`- ${err}`);
    }
    lines.push("");
  }

  const severityOrder: Severity[] = ["S1", "S2", "S3", "S4"];
  for (const sevKey of severityOrder) {
    const list = report.findings.filter((f) => f.severity === sevKey);
    if (list.length === 0) continue;

    lines.push(`## ${sevKey} — ${list.length}건`);
    lines.push("");
    lines.push("| 파일 | 라인 | 룰 | 내용 |");
    lines.push("|---|---:|---|---|");
    for (const f of list.slice(0, 200)) {
      const file = f.file.replace(/\|/g, "\\|");
      const msg = (f.message || "").replace(/\|/g, "\\|").slice(0, 200);
      lines.push(`| ${file} | ${f.line} | ${f.ruleId} | ${msg} |`);
    }
    if (list.length > 200) {
      lines.push(`| ... | | | (${list.length - 200}건 생략) |`);
    }
    lines.push("");
  }

  fs.writeFileSync(filePath, lines.join("\n"), "utf8");
  return filePath;
}

async function main(): Promise<void> {
  ensureDirs();
  const date = todayString();
  console.log(`[healthcheck:server] 스캔 시작 ${date}`);

  const report = await runAll();
  const jsonPath = writeJsonReport(report, date);
  const mdPath = writeMarkdownReport(report, date);

  console.log("");
  console.log(`[healthcheck:server] 완료`);
  console.log(`  총 발견: ${report.summary.totalFindings}건`);
  console.log(`  S1=${report.summary.bySeverity.S1} S2=${report.summary.bySeverity.S2} S3=${report.summary.bySeverity.S3} S4=${report.summary.bySeverity.S4}`);
  console.log(`  JSON: ${jsonPath}`);
  console.log(`  Markdown: ${mdPath}`);
  if (report.summary.errors.length > 0) {
    console.log(`  ⚠ 스캐너 에러: ${report.summary.errors.length}건`);
  }
}

main().catch((e) => {
  console.error("[healthcheck:server] 치명적 에러:", e);
  process.exit(1);
});
