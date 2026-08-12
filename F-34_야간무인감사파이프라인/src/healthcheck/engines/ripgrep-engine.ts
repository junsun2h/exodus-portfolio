// ripgrep 엔진 — Node.js fast-glob + regex로 구현 (ripgrep 바이너리 불필요)
import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";
import { RuleDef, Finding, makeFingerprint } from "../types";

export interface RipgrepConfig {
  pattern: string;
  paths: string[];
  globs: string[];
  excludeGlobs?: string[];
  multiline?: boolean;
  negativeCheck?: {
    pattern: string;
    scope: "file" | "line";
  };
  message: string;
}

function toGlobPatterns(cfg: RipgrepConfig, rootDir: string): string[] {
  const patterns: string[] = [];
  for (const p of cfg.paths) {
    for (const g of cfg.globs) {
      // paths가 이미 파일이면 그대로 사용
      const abs = path.join(rootDir, p);
      try {
        const stat = fs.statSync(abs);
        if (stat.isFile()) {
          patterns.push(p.replace(/\\/g, "/"));
          continue;
        }
      } catch {
        // 경로 없으면 무시
      }
      patterns.push(path.posix.join(p.replace(/\\/g, "/"), "**", g));
    }
  }
  return patterns;
}

export async function runRipgrep(
  rule: RuleDef,
  rootDir: string,
): Promise<Finding[]> {
  const cfg = rule.config as RipgrepConfig;
  const findings: Finding[] = [];
  const globPatterns = toGlobPatterns(cfg, rootDir);

  const files = await fg(globPatterns, {
    cwd: rootDir,
    ignore: cfg.excludeGlobs ?? [],
    absolute: false,
    onlyFiles: true,
    followSymbolicLinks: false,
  });

  const flags = cfg.multiline ? "gms" : "gm";
  const patternRegex = new RegExp(cfg.pattern, flags);
  const negRegex = cfg.negativeCheck
    ? new RegExp(cfg.negativeCheck.pattern, "m")
    : null;

  for (const rel of files) {
    const abs = path.join(rootDir, rel);
    let content: string;
    try {
      content = fs.readFileSync(abs, "utf8");
    } catch {
      continue;
    }

    // negativeCheck scope=file: 파일 전체에 negPattern이 이미 있으면 skip
    if (negRegex && cfg.negativeCheck!.scope === "file" && negRegex.test(content)) {
      continue;
    }

    // 매치 수집
    const lines = content.split(/\r?\n/);
    const re = new RegExp(cfg.pattern, flags);
    let m: RegExpExecArray | null;
    const seen = new Set<number>();

    while ((m = re.exec(content)) !== null) {
      const idx = m.index;
      const before = content.slice(0, idx);
      const lineNo = before.split(/\r?\n/).length;
      const colNo = idx - before.lastIndexOf("\n");

      if (seen.has(lineNo)) {
        if (m[0].length === 0) re.lastIndex++;
        continue;
      }
      seen.add(lineNo);

      const snippet = (lines[lineNo - 1] || "").slice(0, 200).trim();

      findings.push({
        ruleId: rule.id,
        ruleName: rule.name,
        severity: rule.severity,
        category: rule.category,
        file: rel.replace(/\\/g, "/"),
        line: lineNo,
        column: colNo,
        message: cfg.message,
        snippet,
        fingerprint: makeFingerprint(rel, lineNo, rule.id),
      });

      if (m[0].length === 0) re.lastIndex++;
    }
  }

  return findings;
}

export interface RipgrepBalanceConfig {
  addPattern: string;
  removePattern: string;
  paths: string[];
  globs: string[];
  excludeGlobs?: string[];
  message: string;
}

// += 대비 -= 균형 체크 — 파일 단위
export async function runRipgrepBalance(
  rule: RuleDef,
  rootDir: string,
): Promise<Finding[]> {
  const cfg = rule.config as RipgrepBalanceConfig;
  const findings: Finding[] = [];
  const globPatterns: string[] = [];
  for (const p of cfg.paths) {
    const abs = path.join(rootDir, p);
    try {
      const stat = fs.statSync(abs);
      if (stat.isFile()) {
        globPatterns.push(p.replace(/\\/g, "/"));
        continue;
      }
    } catch {
      continue;
    }
    for (const g of cfg.globs) {
      globPatterns.push(path.posix.join(p.replace(/\\/g, "/"), "**", g));
    }
  }

  const files = await fg(globPatterns, {
    cwd: rootDir,
    ignore: cfg.excludeGlobs ?? [],
    absolute: false,
    onlyFiles: true,
  });

  const addRe = new RegExp(cfg.addPattern, "gm");
  const removeRe = new RegExp(cfg.removePattern, "gm");

  for (const rel of files) {
    const abs = path.join(rootDir, rel);
    let content: string;
    try {
      content = fs.readFileSync(abs, "utf8");
    } catch {
      continue;
    }

    const addCount = (content.match(addRe) || []).length;
    const removeCount = (content.match(removeRe) || []).length;

    if (addCount > removeCount + 1) {
      findings.push({
        ruleId: rule.id,
        ruleName: rule.name,
        severity: rule.severity,
        category: rule.category,
        file: rel.replace(/\\/g, "/"),
        line: 1,
        message: `${cfg.message} (+= ${addCount}, -= ${removeCount})`,
        fingerprint: makeFingerprint(rel, 0, rule.id),
      });
    }
  }

  return findings;
}
