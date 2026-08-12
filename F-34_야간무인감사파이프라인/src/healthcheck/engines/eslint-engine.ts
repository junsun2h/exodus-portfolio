// ESLint 엔진 — eslint CLI를 1회 실행하여 결과를 cache 후 룰별 필터
import { spawnSync } from "child_process";
import { RuleDef, Finding, makeFingerprint } from "../types";

interface ESLintMessage {
  ruleId: string | null;
  severity: number;
  message: string;
  line: number;
  column: number;
  fatal?: boolean;
}

interface ESLintFileResult {
  filePath: string;
  messages: ESLintMessage[];
  errorCount: number;
  warningCount: number;
}

let _cache: ESLintFileResult[] | null = null;
let _cacheErr: string | null = null;

export function runESLintOnce(rootDir: string): {
  results: ESLintFileResult[];
  error: string | null;
} {
  if (_cache !== null || _cacheErr !== null) {
    return { results: _cache ?? [], error: _cacheErr };
  }

  // npx eslint src/**/*.ts -f json
  // Windows: shell:true 필요
  const proc = spawnSync(
    "npx",
    ["eslint", "src/**/*.ts", "-f", "json"],
    {
      cwd: rootDir,
      encoding: "utf8",
      shell: true,
      maxBuffer: 64 * 1024 * 1024,
      windowsHide: true,
    },
  );

  // ESLint는 에러 발견 시 exit 1 — stdout은 그래도 유효
  if (!proc.stdout) {
    _cacheErr = `ESLint 실행 실패: ${proc.stderr || "no output"}`;
    _cache = [];
    return { results: [], error: _cacheErr };
  }

  try {
    _cache = JSON.parse(proc.stdout);
    return { results: _cache ?? [], error: null };
  } catch (e: any) {
    _cacheErr = `ESLint JSON 파싱 실패: ${e.message}`;
    _cache = [];
    return { results: [], error: _cacheErr };
  }
}

export function runESLintRule(
  rule: RuleDef,
  rootDir: string,
): { findings: Finding[]; error: string | null } {
  const targetRuleId = rule.config.ruleId as string;
  const { results, error } = runESLintOnce(rootDir);
  if (error) return { findings: [], error };

  const findings: Finding[] = [];
  for (const fileResult of results) {
    const relPath = fileResult.filePath
      .replace(/\\/g, "/")
      .replace(rootDir.replace(/\\/g, "/") + "/", "");

    for (const m of fileResult.messages) {
      if (m.ruleId !== targetRuleId) continue;
      findings.push({
        ruleId: rule.id,
        ruleName: rule.name,
        severity: rule.severity,
        category: rule.category,
        file: relPath,
        line: m.line,
        column: m.column,
        message: `[${targetRuleId}] ${m.message}`,
        fingerprint: makeFingerprint(relPath, m.line, rule.id),
      });
    }
  }

  return { findings, error: null };
}

export function resetESLintCache(): void {
  _cache = null;
  _cacheErr = null;
}
