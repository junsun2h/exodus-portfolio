// ts-prune, madge, ts-complex 도구 엔진 — npx 바이너리 호출 후 파싱
import { spawnSync } from "child_process";
import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";
import { RuleDef, Finding, makeFingerprint } from "../types";

function spawnCmd(
  cmd: string,
  args: string[],
  rootDir: string,
): { stdout: string; stderr: string; code: number } {
  const proc = spawnSync(cmd, args, {
    cwd: rootDir,
    encoding: "utf8",
    shell: true,
    maxBuffer: 64 * 1024 * 1024,
    windowsHide: true,
  });
  return {
    stdout: proc.stdout || "",
    stderr: proc.stderr || "",
    code: proc.status ?? -1,
  };
}

// --- ts-prune: 미사용 export 탐지 ---
export function runTsPrune(
  rule: RuleDef,
  rootDir: string,
): { findings: Finding[]; error: string | null } {
  const { stdout, stderr } = spawnCmd("npx", ["ts-prune"], rootDir);
  if (!stdout && stderr) {
    return { findings: [], error: `ts-prune 실행 실패: ${stderr.slice(0, 500)}` };
  }

  const excludes: string[] = rule.config.excludePatterns || [];
  const excludeRes = excludes.map((p) => new RegExp(p));
  const findings: Finding[] = [];

  // ts-prune 출력 형식: src/foo.ts:12 - exportName (used in module)
  const lineRe = /^(.+?):(\d+)\s*-\s*(.+?)(?:\s*\((.*?)\))?$/;
  for (const raw of stdout.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;

    if (excludeRes.some((r) => r.test(line))) continue;

    const m = lineRe.exec(line);
    if (!m) continue;

    const file = m[1].replace(/\\/g, "/");
    const lineNo = parseInt(m[2], 10);
    const symbol = m[3];

    findings.push({
      ruleId: rule.id,
      ruleName: rule.name,
      severity: rule.severity,
      category: rule.category,
      file,
      line: lineNo,
      message: `미사용 export: ${symbol}`,
      fingerprint: makeFingerprint(file, lineNo, rule.id),
    });
  }

  return { findings, error: null };
}

// --- madge: 순환 의존 탐지 ---
export function runMadge(
  rule: RuleDef,
  rootDir: string,
): { findings: Finding[]; error: string | null } {
  const entry = rule.config.entry || "src/index.ts";
  const exts = (rule.config.extensions || ["ts"]).join(",");
  const { stdout, stderr, code } = spawnCmd(
    "npx",
    ["madge", "--circular", "--extensions", exts, "--json", entry],
    rootDir,
  );

  // madge는 순환 발견 시 exit 1 — stdout은 유효
  if (!stdout) {
    return {
      findings: [],
      error: code !== 0 && code !== 1 ? `madge 실행 실패: ${stderr.slice(0, 500)}` : null,
    };
  }

  let cycles: string[][] = [];
  try {
    cycles = JSON.parse(stdout);
  } catch {
    // text 형식 폴백: "Found N circular dependencies!\n1) a -> b -> a"
    const textCycles = stdout
      .split(/\r?\n/)
      .filter((l) => /^\d+\)/.test(l))
      .map((l) => l.replace(/^\d+\)\s*/, "").split(/\s*>\s*/));
    cycles = textCycles;
  }

  const findings: Finding[] = [];
  let i = 0;
  for (const cycle of cycles) {
    i++;
    if (!Array.isArray(cycle) || cycle.length === 0) continue;
    const firstFile = (cycle[0] || "").replace(/\\/g, "/");
    findings.push({
      ruleId: rule.id,
      ruleName: rule.name,
      severity: rule.severity,
      category: rule.category,
      file: firstFile,
      line: 1,
      message: `순환 의존 #${i}: ${cycle.join(" → ")} → ${cycle[0]}`,
      fingerprint: makeFingerprint(firstFile, i, rule.id),
    });
  }

  return { findings, error: null };
}

// --- ts-complex: 사이클로매틱 복잡도 ---
export function runTsComplex(
  rule: RuleDef,
  rootDir: string,
): { findings: Finding[]; error: string | null } {
  const threshold: number = rule.config.threshold ?? 15;
  const sources: string[] = rule.config.sources || ["src"];
  const findings: Finding[] = [];
  const errors: string[] = [];

  // ts-complex는 단일 파일 분석 도구. glob으로 파일 수집 후 개별 실행 대신
  // 디렉토리 전체를 한 번에 처리하는 옵션이 없으므로 파일 단위 순회
  const files: string[] = [];
  for (const src of sources) {
    const absSrc = path.join(rootDir, src);
    if (!fs.existsSync(absSrc)) continue;
    const found = fg.sync(["**/*.ts"], {
      cwd: absSrc,
      ignore: ["**/*.test.ts", "**/*.d.ts", "**/Generated/**"],
      absolute: false,
    });
    for (const f of found) files.push(path.posix.join(src.replace(/\\/g, "/"), f));
  }

  for (const rel of files) {
    const { stdout } = spawnCmd("npx", ["ts-complex", rel], rootDir);
    if (!stdout) continue;

    // 출력 형식 (사람 가독): 함수명 옆에 복잡도 숫자
    // 예: "functionName: 17"
    // 버전에 따라 다르니 관대하게 파싱
    const re = /^\s*(\S+?)\s*[:|]\s*(\d+)\s*$/gm;
    let m: RegExpExecArray | null;
    while ((m = re.exec(stdout)) !== null) {
      const name = m[1];
      const complexity = parseInt(m[2], 10);
      if (complexity < threshold) continue;
      findings.push({
        ruleId: rule.id,
        ruleName: rule.name,
        severity: rule.severity,
        category: rule.category,
        file: rel,
        line: 1,
        message: `복잡도 ${complexity} (임계값 ${threshold}): ${name}`,
        fingerprint: makeFingerprint(rel, 0, `${rule.id}:${name}`),
      });
    }
  }

  return { findings, error: errors.length ? errors.join("; ") : null };
}
