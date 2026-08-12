// Phase 5 Track B — 클라이언트 정적 감사 (탐지 전용, 자동 수정 없음)
//
// 실행:
//   - Tier1: 매일 야간 스캔 (패턴의 tier1Paths 사용, 약 7개 파일)
//   - Tier2: 주 1회 스캔 (--tier2 플래그, Assets/Source/Logic 전체)
//
// 엔진:
//   - ripgrep (Node 네이티브) — 대부분의 룰
//   - ripgrep-balance — 이벤트 += / -= 균형 체크
//   - loc-count — 파일당 LOC > 임계값
//   - unicli (선택) — Unity Analyzer 경고 수집

import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";
import { spawnSync } from "child_process";
import {
  PatternFile,
  RuleDef,
  Finding,
  ScanReport,
  emptyReport,
  addFinding,
  makeFingerprint,
} from "./types";
import { runRipgrep, runRipgrepBalance } from "./engines/ripgrep-engine";

const REPO_ROOT = path.resolve(__dirname, "..", "..", "..");
const PATTERNS_PATH = path.join(__dirname, "patterns", "client-patterns.json");
const OUT_DIR = path.join(__dirname, "reports");
const RAW_DIR = path.join(__dirname, "raw");

const UNICLI = process.env.UNICLI_PATH || "C:/Users/USER/scoop/shims/unicli.exe";

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

// LOC 체크 엔진 — tier1/tier2 경로에서 *.cs 파일 LOC 측정
async function runLocCount(
  rule: RuleDef,
  rootDir: string,
  tierPaths: string[],
): Promise<Finding[]> {
  const threshold: number = rule.config.threshold ?? 1000;
  const globs: string[] = rule.config.globs ?? ["*.cs"];
  const excludeGlobs: string[] = rule.config.excludeGlobs ?? [];

  const patterns: string[] = [];
  for (const p of tierPaths) {
    // 파일 경로면 그대로, 디렉토리면 glob 생성
    const abs = path.join(rootDir, p);
    try {
      const stat = fs.statSync(abs);
      if (stat.isFile()) {
        patterns.push(p.replace(/\\/g, "/"));
        continue;
      }
    } catch {
      continue;
    }
    for (const g of globs) {
      patterns.push(path.posix.join(p.replace(/\\/g, "/"), "**", g));
    }
  }

  const files = await fg(patterns, {
    cwd: rootDir,
    ignore: excludeGlobs,
    absolute: false,
    onlyFiles: true,
  });

  const findings: Finding[] = [];
  for (const rel of files) {
    const abs = path.join(rootDir, rel);
    try {
      const content = fs.readFileSync(abs, "utf8");
      const loc = content.split(/\r?\n/).length;
      if (loc > threshold) {
        findings.push({
          ruleId: rule.id,
          ruleName: rule.name,
          severity: rule.severity,
          category: rule.category,
          file: rel.replace(/\\/g, "/"),
          line: 1,
          message: `LOC ${loc} > ${threshold} — 리팩토링 후보`,
          fingerprint: makeFingerprint(rel, 0, rule.id),
        });
      }
    } catch {
      /* skip */
    }
  }
  return findings;
}

// UniCli 엔진 — Unity 컴파일 경고 수집 (Editor 열려있어야 함)
function runUniCli(rule: RuleDef): { findings: Finding[]; error: string | null } {
  if (!fs.existsSync(UNICLI)) {
    return { findings: [], error: `unicli 바이너리 없음: ${UNICLI}` };
  }

  const check = spawnSync(UNICLI, ["check"], {
    encoding: "utf8",
    shell: true,
    windowsHide: true,
  });
  if (check.status !== 0) {
    return { findings: [], error: "Unity Editor 미연결 — 이번 스캔은 스킵" };
  }

  const cmd = rule.config.command || "CompileAndLog";
  const res = spawnSync(UNICLI, ["exec", cmd, "--json"], {
    encoding: "utf8",
    shell: true,
    windowsHide: true,
    maxBuffer: 32 * 1024 * 1024,
    timeout: 300000,
  });

  if (res.status !== 0) {
    return { findings: [], error: `unicli exec ${cmd} 실패` };
  }

  const findings: Finding[] = [];
  const filter = rule.config.filter as string | undefined;
  const filterRe = filter ? new RegExp(filter) : null;

  // 출력 포맷: json 배열 또는 라인 단위 텍스트
  try {
    const parsed = JSON.parse(res.stdout || "{}");
    const items = Array.isArray(parsed) ? parsed : parsed.items ?? parsed.warnings ?? [];
    for (const item of items) {
      const msg = (item.message || item.text || "").toString();
      if (filterRe && !filterRe.test(msg)) continue;
      const file = (item.file || item.path || "").replace(/\\/g, "/");
      const line = item.line ?? 0;
      findings.push({
        ruleId: rule.id,
        ruleName: rule.name,
        severity: rule.severity,
        category: rule.category,
        file,
        line,
        message: msg.slice(0, 200),
        fingerprint: makeFingerprint(file, line, rule.id),
      });
    }
  } catch {
    // 텍스트 파싱 폴백
    const lines = (res.stdout || "").split(/\r?\n/);
    for (const line of lines) {
      if (filterRe && !filterRe.test(line)) continue;
      const m = /^(.+?)\((\d+),\d+\):\s*(warning|error)\s+(\w+):\s*(.+)$/.exec(line);
      if (m) {
        findings.push({
          ruleId: rule.id,
          ruleName: rule.name,
          severity: rule.severity,
          category: rule.category,
          file: m[1].replace(/\\/g, "/"),
          line: parseInt(m[2], 10),
          message: `${m[4]}: ${m[5]}`.slice(0, 200),
          fingerprint: makeFingerprint(m[1], parseInt(m[2], 10), rule.id),
        });
      }
    }
  }

  return { findings, error: null };
}

// EditMode 테스트 전체 실행 — 실패 건을 Finding으로 변환
function runEditModeTests(report: ScanReport): void {
  console.log("[healthcheck:client] EditMode 테스트 실행");

  if (!fs.existsSync(UNICLI)) {
    report.summary.errors.push("EditMode 테스트 스킵: unicli 바이너리 없음");
    return;
  }

  const check = spawnSync(UNICLI, ["check"], {
    encoding: "utf8",
    shell: true,
    windowsHide: true,
  });
  if (check.status !== 0) {
    report.summary.errors.push("EditMode 테스트 스킵: Unity Editor 미연결");
    return;
  }

  const res = spawnSync(UNICLI, ["exec", "TestRunner.RunEditMode", "--json"], {
    encoding: "utf8",
    shell: true,
    windowsHide: true,
    maxBuffer: 32 * 1024 * 1024,
    timeout: 600000,
  });

  try {
    const parsed = JSON.parse(res.stdout || "{}");
    const data = parsed.data ?? parsed;
    const passed = data.passed ?? 0;
    const failed = data.failed ?? 0;
    const total = data.total ?? 0;

    console.log(`  EditMode 테스트: ${passed}/${total} 통과, ${failed} 실패`);

    if (failed > 0 && Array.isArray(data.results)) {
      for (const r of data.results) {
        if (r.status === "Failed" || r.result === "Failed") {
          addFinding(report, {
            ruleId: "EDITMODE-TEST-FAIL",
            ruleName: "EditMode 테스트 실패",
            severity: "S1",
            category: "gameplay",
            file: r.className || r.name || "unknown",
            line: 0,
            message: `${r.name || r.testName}: ${(r.message || "FAILED").slice(0, 200)}`,
            fingerprint: makeFingerprint(r.name || "test", 0, "EDITMODE-TEST-FAIL"),
          });
        }
      }
    }
  } catch {
    if (res.status !== 0) {
      report.summary.errors.push(`EditMode 테스트 실행 실패: exit=${res.status}`);
    }
  }
}

async function runAll(tier2: boolean): Promise<ScanReport> {
  const report = emptyReport("client");
  const start = Date.now();

  // EditMode 테스트 먼저 실행
  runEditModeTests(report);

  const patterns: PatternFile = JSON.parse(fs.readFileSync(PATTERNS_PATH, "utf8"));
  const tierPaths = tier2 ? patterns.tier2Paths ?? [] : patterns.tier1Paths ?? [];
  console.log(
    `[healthcheck:client] ${tier2 ? "Tier2" : "Tier1"} 경로 ${tierPaths.length}개, ${patterns.rules.length}개 룰 실행`,
  );

  for (const rule of patterns.rules) {
    const ruleStart = Date.now();
    let findings: Finding[] = [];
    let error: string | null = null;

    // 룰의 paths가 "tier1"이면 tierPaths 주입
    const effectiveRule: RuleDef = { ...rule, config: { ...rule.config } };
    if (Array.isArray(rule.config.paths) && rule.config.paths.length > 0) {
      // 이미 절대 경로를 사용 중 — Tier1/2 범위를 제한하려면 교집합 필요
      // 단순화: tier1 모드에서만 Tier1 경로만 스캔하도록 교체
      if (!tier2) {
        effectiveRule.config.paths = tierPaths;
      }
    } else {
      effectiveRule.config.paths = tierPaths;
    }

    try {
      switch (rule.engine) {
        case "ripgrep":
          findings = await runRipgrep(effectiveRule, REPO_ROOT);
          break;
        case "ripgrep-balance":
          findings = await runRipgrepBalance(effectiveRule, REPO_ROOT);
          break;
        case "loc-count":
          findings = await runLocCount(effectiveRule, REPO_ROOT, tierPaths);
          break;
        case "unicli": {
          const res = runUniCli(effectiveRule);
          findings = res.findings;
          error = res.error;
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

    console.log(
      `  [${rule.id}] ${rule.name} — ${findings.length}건 (${Date.now() - ruleStart}ms)${error ? " ⚠ " + error : ""}`,
    );
  }

  report.durationMs = Date.now() - start;
  return report;
}

function writeReports(report: ScanReport, date: string, tier2: boolean): { json: string; md: string } {
  if (!fs.existsSync(RAW_DIR)) fs.mkdirSync(RAW_DIR, { recursive: true });
  if (!fs.existsSync(OUT_DIR)) fs.mkdirSync(OUT_DIR, { recursive: true });
  const suffix = tier2 ? "-tier2" : "";
  const jsonPath = path.join(RAW_DIR, `client-raw-${date}${suffix}.json`);
  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2), "utf8");

  const mdPath = path.join(OUT_DIR, `client-${date}${suffix}.md`);
  const lines: string[] = [];
  lines.push(`# 클라이언트 감사 리포트${tier2 ? " (Tier2)" : " (Tier1)"} — ${date}`);
  lines.push("");
  lines.push(`- **총 발견**: ${report.summary.totalFindings}건`);
  lines.push(`- **S1=${report.summary.bySeverity.S1} S2=${report.summary.bySeverity.S2} S3=${report.summary.bySeverity.S3} S4=${report.summary.bySeverity.S4}**`);
  lines.push(`- **소요**: ${(report.durationMs / 1000).toFixed(1)}s`);
  lines.push("");

  if (report.summary.errors.length > 0) {
    lines.push(`## ⚠ 스캐너 에러`);
    for (const e of report.summary.errors) lines.push(`- ${e}`);
    lines.push("");
  }

  const byRule: Record<string, Finding[]> = {};
  for (const f of report.findings) {
    (byRule[f.ruleId] ??= []).push(f);
  }

  for (const ruleId of Object.keys(byRule).sort()) {
    const list = byRule[ruleId];
    lines.push(`## ${ruleId} (${list.length}건) — ${list[0].severity}`);
    lines.push("");
    lines.push("| 파일 | 라인 | 내용 |");
    lines.push("|---|---:|---|");
    for (const f of list.slice(0, 50)) {
      lines.push(`| ${f.file} | ${f.line} | ${(f.message || "").slice(0, 150).replace(/\|/g, "\\|")} |`);
    }
    if (list.length > 50) lines.push(`| ... | | (${list.length - 50}건 생략) |`);
    lines.push("");
  }

  fs.writeFileSync(mdPath, lines.join("\n"), "utf8");
  return { json: jsonPath, md: mdPath };
}

async function main(): Promise<void> {
  const tier2 = process.argv.includes("--tier2");
  const date = todayString();
  console.log(`[healthcheck:client] 스캔 시작 ${date}${tier2 ? " (Tier2)" : " (Tier1)"}`);

  const report = await runAll(tier2);
  const { json, md } = writeReports(report, date, tier2);

  console.log("");
  console.log(`[healthcheck:client] 완료`);
  console.log(`  총 발견: ${report.summary.totalFindings}건`);
  console.log(`  JSON: ${json}`);
  console.log(`  Markdown: ${md}`);
}

if (require.main === module) {
  main().catch((e) => {
    console.error("[healthcheck:client] 치명적 에러:", e);
    process.exit(1);
  });
}
