// Enum 정의 검증 — Rule 07 (data-types)
// 검증 항목:
//   1. 첫 항목이 None: 0 인가
//   2. 같은 enum 내 중복된 값이 있는가
//   3. 값이 number 타입인가 (string enum 금지)

import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";
import { RuleDef, Finding, makeFingerprint } from "../types";

export async function run(
  rule: RuleDef,
  rootDir: string,
): Promise<Finding[]> {
  const findings: Finding[] = [];
  const files = await fg(["src/Data/Generated/**/*.ts", "src/Data/Types/**/*.ts"], {
    cwd: rootDir,
    absolute: false,
    onlyFiles: true,
  });

  for (const rel of files) {
    const abs = path.join(rootDir, rel);
    const content = fs.readFileSync(abs, "utf8");

    // export const E\w+ = { ... } as const; 블록 추출
    const enumBlockRe = /export\s+const\s+(E\w+)\s*=\s*\{([\s\S]*?)\}\s*as\s*const/g;
    let bm: RegExpExecArray | null;

    while ((bm = enumBlockRe.exec(content)) !== null) {
      const enumName = bm[1];
      const body = bm[2];
      const blockStart = bm.index;
      const blockStartLine = content.slice(0, blockStart).split(/\r?\n/).length;

      // 각 엔트리: key: value,
      const entryRe = /^\s*(\w+)\s*:\s*(-?\d+|"[^"]*"|'[^']*')\s*,?\s*$/gm;
      const entries: { key: string; raw: string; lineOffset: number }[] = [];
      let em: RegExpExecArray | null;
      while ((em = entryRe.exec(body)) !== null) {
        const before = body.slice(0, em.index);
        const lineOffset = before.split(/\r?\n/).length - 1;
        entries.push({ key: em[1], raw: em[2], lineOffset });
      }

      if (entries.length === 0) continue;

      // 1) 첫 항목 None: 0 체크
      if (entries[0].key !== "None" || entries[0].raw !== "0") {
        findings.push({
          ruleId: rule.id,
          ruleName: rule.name,
          severity: rule.severity,
          category: rule.category,
          file: rel.replace(/\\/g, "/"),
          line: blockStartLine + entries[0].lineOffset,
          message: `Enum ${enumName}: 첫 항목이 "None: 0"이 아님 (${entries[0].key}: ${entries[0].raw})`,
          fingerprint: makeFingerprint(rel, blockStartLine + entries[0].lineOffset, `${rule.id}:${enumName}:none`),
        });
      }

      // 2) string value 체크
      for (const e of entries) {
        if (e.raw.startsWith('"') || e.raw.startsWith("'")) {
          findings.push({
            ruleId: rule.id,
            ruleName: rule.name,
            severity: rule.severity,
            category: rule.category,
            file: rel.replace(/\\/g, "/"),
            line: blockStartLine + e.lineOffset,
            message: `Enum ${enumName}: ${e.key} 값이 string (${e.raw}) — number만 허용`,
            fingerprint: makeFingerprint(rel, blockStartLine + e.lineOffset, `${rule.id}:${enumName}:${e.key}:str`),
          });
        }
      }

      // 3) 중복 값 체크
      const valueMap = new Map<string, string>();
      for (const e of entries) {
        const v = e.raw;
        if (valueMap.has(v)) {
          findings.push({
            ruleId: rule.id,
            ruleName: rule.name,
            severity: rule.severity,
            category: rule.category,
            file: rel.replace(/\\/g, "/"),
            line: blockStartLine + e.lineOffset,
            message: `Enum ${enumName}: 중복 값 ${v} (${valueMap.get(v)} ↔ ${e.key})`,
            fingerprint: makeFingerprint(rel, blockStartLine + e.lineOffset, `${rule.id}:${enumName}:${e.key}:dup`),
          });
        } else {
          valueMap.set(v, e.key);
        }
      }
    }
  }

  return findings;
}
