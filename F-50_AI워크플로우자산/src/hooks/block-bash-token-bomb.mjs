#!/usr/bin/env node
// CLAUDE.md '파일 읽기 & 토큰 절약 규칙' 강제 (Bash 편)
// Bash 명령 중 출력 제한이 없으면 컨텍스트를 폭발시키는 패턴을 차단한다.
// 차단 대상:
//   1. git log         — --oneline / -n N / | head / | tail 등 제한이 없을 때
//   2. find / dir /s   — 재귀 탐색 + 제한 없을 때 (Glob 도구로 유도)
//   3. cat/type        — Unity 직렬화 파일(.unity, .prefab, .asset, .meta) 또는
//                        generated/*.cs 를 출력 제한 없이 읽으려 할 때

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (c) => (buf += c));
process.stdin.on('end', () => {
  let input;
  try { input = JSON.parse(buf); } catch { process.exit(0); }

  if (input?.tool_name !== 'Bash') process.exit(0);

  const cmd = String(input.tool_input?.command ?? '');
  if (!cmd.trim()) process.exit(0);

  // 따옴표 안 텍스트는 제외 (커밋 메시지에 'find' 들어간 경우 등 오탐 방지)
  const stripped = cmd
    .replace(/<<-?\s*["']?(\w+)["']?[\s\S]*?\n\s*\1/g, '')
    .replace(/'[^']*'/g, "''")
    .replace(/"[^"]*"/g, '""');

  // 출력 제한자 — 이게 있으면 안전하다고 본다
  const hasLimiter = /\|\s*(head|tail|wc|sed\s+-n|awk|rg|grep)\b/i.test(stripped) ||
                     /\s>\s/.test(stripped) || /\s>>\s/.test(stripped);

  // ─── 1) git log 제한 없는 호출 ───────────────────────────────────
  if (/(^|\s|&&|\|\||;)git\s+log(\s|$)/i.test(stripped)) {
    const hasGitLimit =
      /--oneline\b/.test(stripped) ||
      /\s-(n\s*)?\d+\b/.test(stripped) ||      // -n 20, -20
      /--max-count[=\s]\d+/.test(stripped) ||
      /--since[=\s]/.test(stripped) ||         // 시간 제한
      /--until[=\s]/.test(stripped) ||
      hasLimiter;
    if (!hasGitLimit) {
      blockWith([
        '[CLAUDE.md 토큰 절약 규칙] git log 출력 제한 없음 → 차단.',
        `명령: ${cmd}`,
        '',
        '이유: 페이지 제한 없는 git log는 수천 줄 출력이 가능 → 컨텍스트 폭탄.',
        '',
        '대안:',
        '  git log --oneline -n 20',
        '  git log --oneline --since=1.week',
        '  git log -10 --stat   (간단 통계만)',
        '  git log | head -30   (강제 절단)',
      ]);
    }
  }

  // ─── 2) 재귀 탐색 (find / ls -R / dir /s) ────────────────────────
  const isFind   = /(^|\s|&&|\|\||;)find\s+/i.test(stripped);
  const isLsR    = /(^|\s|&&|\|\||;)ls\s+(-\w*R\w*|-R)/i.test(stripped);
  const isDirS   = /(^|\s|&&|\|\||;)dir\s+(\S+\s+)*\/s\b/i.test(stripped);
  if (isFind || isLsR || isDirS) {
    const hasFindLimit =
      hasLimiter ||
      /-maxdepth\s+[1-3]\b/.test(stripped) ||  // 최대 3단계까지만 OK
      /\s-quit\b/.test(stripped);
    if (!hasFindLimit) {
      blockWith([
        '[CLAUDE.md 토큰 절약 규칙] 재귀 탐색 출력 제한 없음 → 차단.',
        `명령: ${cmd}`,
        '',
        '이유: find / ls -R / dir /s 는 거대 디렉토리에서 수만 줄 출력 가능.',
        '',
        '대안:',
        '  Glob 도구 사용 (예: pattern="Assets/**/*.cs")',
        '  find . -name "*.cs" | head -50',
        '  find . -maxdepth 2 -name "*.cs"',
        '  Grep 도구로 내용 검색 (파일 목록만 필요하면 output_mode=files_with_matches)',
      ]);
    }
  }

  // ─── 3) cat/type 으로 큰 Unity 직렬화 파일 ───────────────────────
  // 매치 예: cat Assets/foo.prefab, type Assets\\bar.unity
  const catMatch = stripped.match(
    /(?:^|\s|&&|\|\||;)(?:cat|type|Get-Content|gc)\s+([^\s|;&<>]+\.(?:unity|prefab|asset|meta|uxml))(\s|$)/i,
  );
  const generatedMatch = stripped.match(
    /(?:^|\s|&&|\|\||;)(?:cat|type|Get-Content|gc)\s+([^\s|;&<>]*[\\/]generated[\\/][^\s|;&<>]+\.cs)(\s|$)/i,
  );
  const m = catMatch || generatedMatch;
  if (m && !hasLimiter) {
    blockWith([
      '[CLAUDE.md 토큰 절약 규칙] 토큰 폭탄 파일 cat → 차단.',
      `파일: ${m[1]}`,
      `명령: ${cmd}`,
      '',
      '이유: Unity 직렬화 파일(.unity/.prefab/.asset/.meta/.uxml) 및 generated/*.cs',
      '      는 한 줄이 매우 길거나 줄 수가 많아 토큰 낭비가 크다.',
      '',
      '대안:',
      `  Read 도구 + offset/limit (예: limit=200)`,
      `  Grep 도구로 패턴만 추출 (예: 'm_Name|m_Component')`,
      `  cat ${m[1]} | head -50  (강제 절단이 필요한 경우만)`,
    ]);
  }

  process.exit(0);
});

function blockWith(lines) {
  process.stderr.write(lines.join('\n') + '\n');
  process.exit(2);
}
