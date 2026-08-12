// Phase 5 healthcheck 공통 타입 정의
// finding은 스캐너가 생성하는 단일 검출 항목 단위

export type Severity = "S1" | "S2" | "S3" | "S4";

export type Category = "data-corruption" | "gameplay" | "perf" | "maintenance";

export type Engine =
  | "eslint"
  | "ts-prune"
  | "madge"
  | "ts-complex"
  | "ripgrep"
  | "ripgrep-balance"
  | "loc-count"
  | "unicli"
  | "custom";

export interface RuleDef {
  id: string;
  name: string;
  severity: Severity;
  category: Category;
  engine: Engine;
  config: any;
}

export interface PatternFile {
  version: string;
  description?: string;
  rules: RuleDef[];
  tier1Paths?: string[];
  tier2Paths?: string[];
}

export interface Finding {
  ruleId: string;
  ruleName: string;
  severity: Severity;
  category: Category;
  file: string;
  line: number;
  column?: number;
  message: string;
  snippet?: string;
  fingerprint: string;
}

export interface ScanReport {
  timestamp: string;
  scanner: "server" | "client" | "parity";
  durationMs: number;
  summary: {
    totalFindings: number;
    bySeverity: Record<Severity, number>;
    byRule: Record<string, number>;
    errors: string[];
  };
  findings: Finding[];
}

export function makeFingerprint(file: string, line: number, ruleId: string): string {
  const normalized = file.replace(/\\/g, "/");
  return `${normalized}:${line}:${ruleId}`;
}

export function emptyReport(
  scanner: ScanReport["scanner"],
): ScanReport {
  return {
    timestamp: new Date().toISOString(),
    scanner,
    durationMs: 0,
    summary: {
      totalFindings: 0,
      bySeverity: { S1: 0, S2: 0, S3: 0, S4: 0 },
      byRule: {},
      errors: [],
    },
    findings: [],
  };
}

export function addFinding(report: ScanReport, f: Finding): void {
  report.findings.push(f);
  report.summary.totalFindings++;
  report.summary.bySeverity[f.severity]++;
  report.summary.byRule[f.ruleId] = (report.summary.byRule[f.ruleId] || 0) + 1;
}
