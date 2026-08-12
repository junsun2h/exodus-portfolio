# F-49. AI 워크플로우 엔지니어링 자산

> 소스 발췌: `src/` — 14개 파일
>
> 커스텀 커맨드 31종 중 **대표 9종** + 훅 3종 전부 + 스킬 3종. 훅(`block-bash-token-bomb.mjs` 등)이 규칙을 문서가 아닌 차단으로 강제하는 부분이다.
>
> 스킬의 `references/` 하위 문서(약 3,000줄)는 분량 때문에 제외했다. 그래서 `SKILL.md` 안의
> `references/tools-reference.md`, `references/workflows.md` 링크는 이 리포에서 열리지 않는다.

**구간** Phase 3 (2026.03 ~ 08) | **포지션** TD | **AI** 오케스트레이션

- **문제**: AI와 오래 일할수록 세 가지가 문제가 된다. (1) 프로젝트 지식이 대화마다 휘발되고, (2) 같은 실수를 반복하며, (3) **컨텍스트가 유한 자원**인데 무의미한 내용이 그것을 잡아먹는다.
- **해결**: AI 협업 자체를 **엔지니어링 대상**으로 다뤄 자산화했다.

| 자산 | 규모 | 역할 |
|---|---|---|
| **문서 지식베이스** | **765개** (Docs 698 + FirebaseCLI 67) | 프로젝트 지식의 외부 저장소 |
| **INDEX 색인** | `Docs/INDEX.md` | 키워드로 위키를 찾는 진입점. 갱신도 커맨드로 자동화 |
| **커스텀 커맨드** | **31종** | 반복 작업 규격화 |
| **스킬** | **8종** | 도메인 특화 워크플로우 |
| **훅** | **3종** | 규칙의 **자동 강제** |
| **MCP** | **4종** | Unity / Context7 / Firebase 등 외부 시스템 연결 |

  **훅 3종이 특히 이 프로젝트의 성격을 보여준다** — 규칙을 CLAUDE.md에 쓰는 것으로 끝내지 않고 **차단으로 강제**한다.
  - `block-bash-token-bomb.mjs` — 출력 제한 없는 재귀 탐색(`find` 등)을 **차단**. 수만 줄 출력으로 컨텍스트가 날아가는 것을 막는다.
  - `block-full-uxml-read.mjs` — UXML 전체 읽기 차단. UXML은 한 줄이 매우 길어 줄 수와 무관하게 토큰을 폭발시킨다.
  - `rename-plans.sh` — 플랜 문서에 완료 마커가 붙으면 파일명을 자동으로 `(완)` 접두사로 변경.

  **자동 메모리 쓰기 2-tier 설계** — 대화 내용을 자동으로 메모리에 반영하는 시스템에서, **Tier 1은 LLM 호출을 0회로** 설계했다. 트랜스크립트를 파싱해 `tool_result`를 **81% 제거**한 뒤 필요한 것만 상위 tier로 넘긴다. LLM으로 요약하면 비용도 지연도 발생하지만, 대부분의 노이즈는 **파싱만으로 제거 가능**하다는 판단.
- **기술**: Claude Code 커스텀 커맨드/스킬/훅 작성, MCP 서버 연동, 트랜스크립트 파싱 기반 컨텍스트 압축, 문서 색인 자동 갱신
- **정량**: 문서 **765개** / 커맨드 31 + 스킬 8 + 훅 3 + MCP 4 = **자산 46종** / Tier1 **LLM 호출 0**으로 `tool_result` **81% 제거**
- **근거**:
  - `.claude/commands/` — 31개
  - `.claude/skills/` — 8개
  - `.claude/hooks/block-bash-token-bomb.mjs`, `.claude/hooks/block-full-uxml-read.mjs`, `.claude/hooks/rename-plans.sh`
  - `Docs/INDEX.md` — 위키 색인
  - `Docs/plans/26.06.24_자동-메모리-쓰기-시스템(memorybox-memory-sync)-명세서.md`
  - `CLAUDE.md` — 프로젝트 작업 규약 (토큰 절약 규칙, 파일 읽기 규칙 등)
- **면접 포인트**: **"AI를 쓰는 사람"과 "AI 워크플로우를 만드는 사람"의 차이를 보여주는 카드.** 세 가지가 근거다 — (1) 규칙을 **훅으로 강제**했다(문서에 적어두면 지켜지지 않는다는 것을 인정하고 차단으로 바꿨다), (2) 컨텍스트를 **유한 자원으로 관리**했다(토큰 폭탄 차단, UXML 풀리드 차단), (3) 메모리 시스템 Tier1을 **LLM 호출 0**으로 설계했다(파싱으로 풀 수 있는 문제를 굳이 AI로 풀지 않는 판단 — F-32에서 기계 판정 가능한 검증을 자동 검출로 뺀 것과 같은 원칙). 765개 문서 지식베이스는 그 자체로 **AI가 읽을 수 있는 형태로 프로젝트를 외재화한 결과물**이다.
- **슬라이드 자료**: 자산 46종 구성도 + 훅 차단 실제 출력 — **다이어그램 필요** + **캡처 필요**


## 수록 파일

- `.claude/commands/battlebalance.md`
- `.claude/commands/bm-sangpum-design-discuss.md`
- `.claude/commands/code-healthcheck.md`
- `.claude/commands/data-coredata-creator.md`
- `.claude/commands/data-upload-gamedb.md`
- `.claude/commands/docs-addwiki.md`
- `.claude/commands/github-push.md`
- `.claude/commands/tool-htmlslide.md`
- `.claude/commands/ui-evaluate-lite.md`
- `.claude/hooks/block-bash-token-bomb.mjs`
- `.claude/hooks/block-full-uxml-read.mjs`
- `.claude/skills/systematic-debugging/skill.md`
- `.claude/skills/unity-mcp-skill/SKILL.md`
- `.claude/skills/verification-before-completion/skill.md`
