// codemod: src/*.ts (test 제외, Generated 제외)에서 console.log/error/warn → logDebug/logError/logDebug 교체
// Phase 2에서 Account.ts에 적용한 것과 같은 패턴
//
// 주의:
// - logDebug/logError import가 없는 파일은 자동 삭제 안 함 (false positive 우려)
// - 변환은 한 줄 단위로만. 다중 라인 template literal은 수동 처리 영역

import * as fs from "fs";
import * as path from "path";
import fg from "fast-glob";

interface CodemodResult {
  filesChanged: number;
  replacements: number;
  changedFiles: string[];
  skipped: { file: string; reason: string }[];
}

const LOG_IMPORT_MARKERS = ["logDebug", "logError", "logCategory"];

export async function runConsoleToLogDebug(rootDir: string): Promise<CodemodResult> {
  const result: CodemodResult = {
    filesChanged: 0,
    replacements: 0,
    changedFiles: [],
    skipped: [],
  };

  const files = await fg(["src/**/*.ts"], {
    cwd: rootDir,
    ignore: ["**/*.test.ts", "**/Generated/**", "**/*.d.ts", "**/UtilityLogging.ts"],
    absolute: false,
    onlyFiles: true,
  });

  for (const rel of files) {
    const abs = path.join(rootDir, rel);
    const original = fs.readFileSync(abs, "utf8");

    // console.log/warn/error 없으면 skip
    if (!/console\.(log|warn|error)\s*\(/.test(original)) continue;

    // logDebug/logError import가 없으면 skip (false positive 방지)
    const hasLogImport = LOG_IMPORT_MARKERS.some((m) => original.includes(m));
    if (!hasLogImport) {
      result.skipped.push({ file: rel, reason: "logDebug/logError import 없음" });
      continue;
    }

    let modified = original;
    let replacements = 0;

    // console.log → logDebug
    modified = modified.replace(/\bconsole\.log\s*\(/g, () => {
      replacements++;
      return "logDebug(";
    });
    // console.warn → logDebug (warn은 debug 카테고리로 통합)
    modified = modified.replace(/\bconsole\.warn\s*\(/g, () => {
      replacements++;
      return "logDebug(";
    });
    // console.error → logError
    modified = modified.replace(/\bconsole\.error\s*\(/g, () => {
      replacements++;
      return "logError(";
    });

    if (replacements > 0 && modified !== original) {
      fs.writeFileSync(abs, modified, "utf8");
      result.filesChanged++;
      result.replacements += replacements;
      result.changedFiles.push(rel);
    }
  }

  return result;
}

// 단독 실행 진입점
if (require.main === module) {
  const rootDir = path.resolve(__dirname, "..", "..");
  runConsoleToLogDebug(rootDir).then((result) => {
    console.log("[codemod:console-to-logdebug] 완료");
    console.log(`  수정 파일: ${result.filesChanged}`);
    console.log(`  교체 건수: ${result.replacements}`);
    if (result.skipped.length > 0) {
      console.log(`  스킵: ${result.skipped.length}건`);
      for (const s of result.skipped.slice(0, 10)) {
        console.log(`    ${s.file} — ${s.reason}`);
      }
    }
  });
}
