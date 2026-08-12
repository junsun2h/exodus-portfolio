#!/usr/bin/env node
// CLAUDE.md '파일 읽기 & 토큰 절약 규칙' 강제
// UXML 파일은 Grep-first 또는 offset/limit 없이 Read 금지.
// PreToolUse 훅으로 Read 호출을 검사하고, 조건 위반 시 exit 2(Block)로 차단.

const MAX_LIMIT = 200;

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (c) => (buf += c));
process.stdin.on('end', () => {
  let input;
  try {
    input = JSON.parse(buf);
  } catch {
    process.exit(0);
  }

  if (input?.tool_name !== 'Read') process.exit(0);

  const ti = input.tool_input || {};
  const fp = String(ti.file_path || '');
  if (!/\.uxml$/i.test(fp)) process.exit(0);

  const limit = ti.limit;
  const hasValidLimit = typeof limit === 'number' && limit > 0 && limit <= MAX_LIMIT;

  if (hasValidLimit) process.exit(0);

  const msg = [
    '[CLAUDE.md 토큰 절약 규칙 위반] UXML 풀 Read 차단됨.',
    `파일: ${fp}`,
    '',
    '이유: UXML은 한 줄에 매우 긴 URI(project://database/...?fileID=...)를 포함하여',
    '      풀 Read 시 토큰 낭비가 큼. 줄 수와 무관하게 풀 Read 금지.',
    '',
    '대안 (CLAUDE.md 규칙 따라가기):',
    `  1) 구조 파악 → Grep: 'ui:Button|ui:Label|name=|class='`,
    `  2) 특정 섹션 → Read(file_path, offset=N, limit<=${MAX_LIMIT})`,
    '  3) 넓은 탐색(3쿼리 이상) → Agent(subagent_type=Explore) 위임',
    '',
    `즉, limit 파라미터를 1~${MAX_LIMIT} 범위로 명시해서 다시 호출하거나,`,
    'Grep으로 검색 목적을 달성하세요.',
  ].join('\n');

  process.stderr.write(msg + '\n');
  process.exit(2);
});
