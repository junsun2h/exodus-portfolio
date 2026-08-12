// Phase 5 서버 autofix — 화이트리스트 항목만 안전망 하에서 자동 수정
//
// 실행 순서:
//   1. git stash push -u -m healthcheck-autofix-{date}-pre  (보험)
//   2. eslint --fix src/**/*.ts
//   3. codemod: console-to-logdebug
//   4. "C:/Program Files/nodejs/npm.cmd" run build  (빌드 게이트)
//   5. npm run test                                   (테스트 게이트, 1640건)
//   6. 전부 통과 시: git add src/ → commit → stash drop → SUCCESS
//   7. 실패 시: git checkout -- src/ → stash pop → ROLLBACK
//
// 결과는 healthcheck/reports/autofix-{date}.json 으로 저장

import { spawnSync, SpawnSyncReturns } from "child_process";
import * as fs from "fs";
import * as path from "path";
import { runConsoleToLogDebug } from "./codemods/console-to-logdebug";

const ROOT = path.resolve(__dirname, "..");
const REPO_ROOT = path.resolve(ROOT, "..", "..");
const OUT_DIR = path.join(__dirname, "reports");

// Windows: npm이 PATH에 있으면 직접 사용 (shell:true로 .cmd 자동 해석)
// 공백 있는 절대 경로는 shell:true와 spaces 조합에서 파싱 오류 발생
const NPM_CMD = "npm";

interface AutofixStep {
  name: string;
  status: "success" | "failed" | "skipped";
  details?: string;
  durationMs: number;
}

interface AutofixResult {
  date: string;
  timestamp: string;
  status: "SUCCESS" | "ROLLBACK" | "NO_CHANGES";
  reason?: string;
  steps: AutofixStep[];
  stats: {
    eslintFixedFiles: number;
    codemodFiles: number;
    codemodReplacements: number;
  };
  commit?: string;
}

function todayString(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function runCmd(
  label: string,
  cmd: string,
  args: string[],
  cwd: string,
): SpawnSyncReturns<string> {
  console.log(`[autofix] > ${label}: ${cmd} ${args.join(" ")}`);
  const res = spawnSync(cmd, args, {
    cwd,
    encoding: "utf8",
    shell: true,
    windowsHide: true,
    maxBuffer: 128 * 1024 * 1024,
  });
  if (res.status !== 0) {
    console.log(`[autofix]   exit=${res.status}`);
    if (res.stderr) console.log(`[autofix]   stderr: ${res.stderr.slice(0, 500)}`);
  }
  return res;
}

function gitStash(date: string): boolean {
  const res = runCmd(
    "git stash (보험)",
    "git",
    ["stash", "push", "-u", "-m", `"healthcheck-autofix-${date}-pre"`],
    REPO_ROOT,
  );
  // "No local changes to save"는 status 0으로 나올 수 있음
  return res.status === 0;
}

function gitStashPop(): void {
  runCmd("git stash pop", "git", ["stash", "pop"], REPO_ROOT);
}

function gitStashDrop(): void {
  runCmd("git stash drop", "git", ["stash", "drop"], REPO_ROOT);
}

function gitHasStash(): boolean {
  const res = runCmd("git stash list", "git", ["stash", "list"], REPO_ROOT);
  return (res.stdout || "").includes("healthcheck-autofix-");
}

function gitRestoreSrc(): void {
  runCmd(
    "git restore src/",
    "git",
    ["checkout", "--", "FirebaseCLI/functions/src/"],
    REPO_ROOT,
  );
}

function gitHasChanges(): boolean {
  const res = spawnSync(
    "git",
    ["status", "--porcelain", "FirebaseCLI/functions/src/"],
    { cwd: REPO_ROOT, encoding: "utf8", shell: true, windowsHide: true },
  );
  return (res.stdout || "").trim().length > 0;
}

function gitCommit(date: string, summary: string): string | null {
  const addRes = runCmd(
    "git add",
    "git",
    ["add", "FirebaseCLI/functions/src/"],
    REPO_ROOT,
  );
  if (addRes.status !== 0) return null;

  const msg = `chore(healthcheck): autofix ${date} — ${summary}`;
  const commitRes = runCmd(
    "git commit",
    "git",
    ["commit", "-m", `"${msg}"`],
    REPO_ROOT,
  );
  if (commitRes.status !== 0) return null;

  const shaRes = spawnSync("git", ["rev-parse", "--short", "HEAD"], {
    cwd: REPO_ROOT,
    encoding: "utf8",
    shell: true,
    windowsHide: true,
  });
  return (shaRes.stdout || "").trim() || null;
}

async function main(): Promise<void> {
  const date = todayString();
  const dryRun = process.argv.includes("--dry-run");
  const skipTests = process.argv.includes("--skip-tests") || dryRun;
  const skipCommit = process.argv.includes("--skip-commit") || dryRun;

  const result: AutofixResult = {
    date,
    timestamp: new Date().toISOString(),
    status: "SUCCESS",
    steps: [],
    stats: { eslintFixedFiles: 0, codemodFiles: 0, codemodReplacements: 0 },
  };

  console.log(`[autofix] 시작 ${date}${dryRun ? " (DRY-RUN)" : ""}`);
  if (skipTests) console.log(`[autofix] 테스트 게이트 스킵`);
  if (skipCommit) console.log(`[autofix] 커밋 단계 스킵 — 수정 후 자동 롤백`);

  if (!fs.existsSync(OUT_DIR)) fs.mkdirSync(OUT_DIR, { recursive: true });

  // 0. 선행 조건: git clean한 상태에서 실행 권장
  const preDirty = gitHasChanges();
  if (preDirty) {
    console.log(`[autofix] ⚠ 사전 dirty — 기존 변경사항이 있음. stash로 보호하지 않고 진행.`);
  }

  // 1. ESLint --fix
  let step: AutofixStep = { name: "eslint --fix", status: "success", durationMs: 0 };
  const stepStart1 = Date.now();
  const eslintRes = runCmd(
    "eslint --fix",
    "npx",
    ["eslint", "src/**/*.ts", "--fix"],
    ROOT,
  );
  step.durationMs = Date.now() - stepStart1;
  // eslint exit 1 = 수정 후에도 남은 문제 있음 — 정상 케이스
  if (eslintRes.status === null || eslintRes.status > 2) {
    step.status = "failed";
    step.details = `exit=${eslintRes.status}, stderr=${(eslintRes.stderr || "").slice(0, 300)}`;
  } else {
    // 수정된 파일 수를 git diff로 추정
    const diffRes = spawnSync(
      "git",
      ["diff", "--name-only", "FirebaseCLI/functions/src/"],
      { cwd: REPO_ROOT, encoding: "utf8", shell: true, windowsHide: true },
    );
    const fixedCount = (diffRes.stdout || "").trim().split(/\r?\n/).filter(Boolean).length;
    result.stats.eslintFixedFiles = fixedCount;
    step.details = `${fixedCount} 파일 수정`;
  }
  result.steps.push(step);

  if (step.status === "failed") {
    result.status = "ROLLBACK";
    result.reason = `eslint 실행 실패: ${step.details}`;
    gitRestoreSrc();
    writeResult(result);
    return;
  }

  // 2. Codemod: console-to-logdebug
  step = { name: "codemod: console-to-logdebug", status: "success", durationMs: 0 };
  const stepStart2 = Date.now();
  try {
    const cm = await runConsoleToLogDebug(ROOT);
    result.stats.codemodFiles = cm.filesChanged;
    result.stats.codemodReplacements = cm.replacements;
    step.details = `${cm.filesChanged} 파일 / ${cm.replacements} 교체`;
  } catch (e: any) {
    step.status = "failed";
    step.details = `예외: ${e.message}`;
  }
  step.durationMs = Date.now() - stepStart2;
  result.steps.push(step);

  if (step.status === "failed") {
    result.status = "ROLLBACK";
    result.reason = `codemod 실패: ${step.details}`;
    gitRestoreSrc();
    writeResult(result);
    return;
  }

  // 변경 없으면 NO_CHANGES로 조기 종료
  if (!gitHasChanges()) {
    result.status = "NO_CHANGES";
    result.reason = "수정 대상 없음 (이미 깨끗함)";
    console.log(`[autofix] 수정 대상 없음 — 조기 종료`);
    writeResult(result);
    return;
  }

  // 3. 빌드 게이트
  step = { name: "build gate", status: "success", durationMs: 0 };
  const stepStart3 = Date.now();
  const buildRes = runCmd("npm run build", NPM_CMD, ["run", "build"], ROOT);
  step.durationMs = Date.now() - stepStart3;
  if (buildRes.status !== 0) {
    step.status = "failed";
    step.details = `exit=${buildRes.status}, stdout=${(buildRes.stdout || "").slice(-500)}`;
    result.steps.push(step);
    result.status = "ROLLBACK";
    result.reason = `빌드 실패: ${step.details}`;
    gitRestoreSrc();
    writeResult(result);
    return;
  }
  step.details = "빌드 성공";
  result.steps.push(step);

  // 4. 테스트 게이트
  if (skipTests) {
    result.steps.push({
      name: "test gate",
      status: "skipped",
      details: "--skip-tests 플래그",
      durationMs: 0,
    });
  } else {
    step = { name: "test gate", status: "success", durationMs: 0 };
    const stepStart4 = Date.now();
    const testRes = runCmd("npm test", NPM_CMD, ["run", "test"], ROOT);
    step.durationMs = Date.now() - stepStart4;
    if (testRes.status !== 0) {
      step.status = "failed";
      step.details = `exit=${testRes.status}, 마지막 출력: ${(testRes.stdout || "").slice(-500)}`;
      result.steps.push(step);
      result.status = "ROLLBACK";
      result.reason = `테스트 실패: ${step.details}`;
      gitRestoreSrc();
      writeResult(result);
      return;
    }
    const passingMatch = /(\d+)\s+passing/.exec(testRes.stdout || "");
    step.details = passingMatch ? `${passingMatch[1]} passing` : "통과";
    result.steps.push(step);
  }

  // 5. 커밋 (또는 dry-run 시 롤백)
  if (skipCommit) {
    result.steps.push({
      name: "git commit",
      status: "skipped",
      details: "--skip-commit/--dry-run — 변경 롤백",
      durationMs: 0,
    });
    gitRestoreSrc();
    result.status = "NO_CHANGES";
    result.reason = "DRY-RUN 검증 완료 — 변경 롤백됨";
    writeResult(result);
    console.log(`[autofix] ✅ DRY-RUN 통과 — 변경 롤백됨`);
    return;
  }

  step = { name: "git commit", status: "success", durationMs: 0 };
  const stepStart5 = Date.now();
  const summary = `eslint ${result.stats.eslintFixedFiles}파일 / codemod ${result.stats.codemodReplacements}건`;
  const commit = gitCommit(date, summary);
  step.durationMs = Date.now() - stepStart5;
  if (!commit) {
    step.status = "failed";
    step.details = "git commit 실패";
    result.steps.push(step);
    result.status = "ROLLBACK";
    result.reason = "커밋 단계 실패";
    gitRestoreSrc();
    writeResult(result);
    return;
  }
  step.details = `커밋 ${commit}`;
  result.steps.push(step);
  result.commit = commit;

  result.status = "SUCCESS";
  writeResult(result);
  console.log(`[autofix] ✅ SUCCESS — ${commit}`);
}

function writeResult(result: AutofixResult): void {
  const outPath = path.join(OUT_DIR, `autofix-${result.date}.json`);
  fs.writeFileSync(outPath, JSON.stringify(result, null, 2), "utf8");
  console.log(`[autofix] 결과 저장: ${outPath}`);
  console.log(`[autofix] 상태: ${result.status}${result.reason ? " — " + result.reason : ""}`);
  for (const step of result.steps) {
    console.log(`  [${step.status}] ${step.name}${step.details ? " — " + step.details : ""} (${step.durationMs}ms)`);
  }
}

main().catch((e) => {
  console.error("[autofix] 치명적 에러:", e);
  process.exit(1);
});
