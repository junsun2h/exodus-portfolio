# F-34. 야간 무인 로직/메커니즘 감사 파이프라인

> 소스 발췌: `src/` — 29개 파일

**구간** Phase 3 (2026.04.11 ~) | **포지션** TD·서버 | **AI** 협업 (오케스트레이션)

- **문제**: 린터와 테스트는 **"코드가 규칙을 지키는가"**를 본다. 하지만 이 프로젝트에서 실제로 위험한 것은 **"로직 자체가 옳은가"**다 — 확률 합이 1.0인가, 가챠 테이블에 dead code가 있는가, 클라와 서버의 계산이 같은가. 이건 문법 검사로도 단위 테스트로도 잡히지 않는다. 그리고 1인 개발에서는 이걸 매일 볼 사람이 없다.
- **해결**: **야간에 무인으로 도는 로직 감사 파이프라인**을 구축했다. 3개 축으로 감사한다.
  - **서버 정적 감사** — 비즈니스 로직 정확성 검증
  - **CS↔TS 동등성 골든 테스트** (F-35)
  - **클라이언트 로직 검증**

  **운영 설계가 이 카드의 핵심이다.**
  - **전일 대비 신규 발견만 하이라이트** — 매일 같은 목록이 나오면 아무도 안 본다. 어제와 달라진 것만 강조한다.
  - **findings 상태머신** — 발견 항목에 상태를 부여해 "확인함 / 무시하기로 함 / 미해결"을 추적한다. 무시한 항목이 매일 다시 올라오지 않는다.
  - **자동수정은 화이트리스트 한정** — 안전하다고 확인된 유형만 자동 수정한다.
  - **자동수정 안전망**: `git stash` → 빌드 → 테스트 green → **실패 시 즉시 롤백**. 자동 수정이 코드를 망가뜨릴 수 없다.
- **기술**: 무인 배치 파이프라인, 정적 로직 감사, 골든 테스트 연동, 상태머신 기반 findings 추적, 화이트리스트 자동수정 + stash/빌드/테스트 게이트 롤백
- **정량**: `healthcheck/` **16파일 2,319줄** / 일일 자동 리포트 생성 / 자동수정 화이트리스트 방식
- **근거**:
  - `FirebaseCLI/functions/healthcheck/` — 16파일 2,319줄
  - `Docs/plans/테스트/26.04.11_Phase-5-야간-무인-로직-메커니즘-감사-시스템-구축.md`
  - `Docs/plans/테스트/healthcheck/reports/2026-04-11_audit-report.md`, `Docs/plans/테스트/healthcheck/reports/2026-04-12_audit-report.md` — 실제 산출 리포트
  - `Docs/리팩토링/완료플랜/26.04.12_서버-비즈니스-로직-정확성-검증.md`, `Docs/리팩토링/완료플랜/26.04.12_Unity-클라이언트-로직-검증-체계-구축.md`
  - `.claude/commands/code-healthcheck.md` — 파이프라인 실행 커맨드
- **면접 포인트**: **이 프로젝트에서 AI 활용의 정점.** 세 가지가 결합되어 있다 — (1) 감사 대상을 문법이 아닌 **로직의 옳음**으로 잡은 문제 정의, (2) 신규 발견 하이라이트와 상태머신이라는 **운영 가능성 설계**(매일 도는 것이 아니라 매일 **읽히는** 것을 만들었다), (3) 화이트리스트 + stash→빌드→테스트→롤백이라는 **자동화 안전망**. AI에게 자동 수정을 맡기면서 동시에 그것이 절대 코드를 망가뜨릴 수 없게 만든 구조가 요점이다. Phase 1의 첫 AI 감사(F-10)가 여기까지 왔다는 서사도 강하다.
- **슬라이드 자료**: 파이프라인 다이어그램(감사 3축 + 자동수정 안전망 게이트) — **다이어그램 필요** / 실제 audit-report — **캡처 필요** (이 프로젝트 대표 슬라이드 후보)


## 수록 파일

- `FirebaseCLI/functions/healthcheck/aggregate.ts`
- `FirebaseCLI/functions/healthcheck/autofix-server.ts`
- `FirebaseCLI/functions/healthcheck/codemods/console-to-logdebug.ts`
- `FirebaseCLI/functions/healthcheck/engines/eslint-engine.ts`
- `FirebaseCLI/functions/healthcheck/engines/ripgrep-engine.ts`
- `FirebaseCLI/functions/healthcheck/engines/tool-engines.ts`
- `FirebaseCLI/functions/healthcheck/patterns/client-patterns.json`
- `FirebaseCLI/functions/healthcheck/patterns/server-patterns.json`
- `FirebaseCLI/functions/healthcheck/reports/autofix-2026-04-11.json`
- `FirebaseCLI/functions/healthcheck/reports/autofix-2026-04-12.json`
- `FirebaseCLI/functions/healthcheck/reports/client-2026-04-11.md`
- `FirebaseCLI/functions/healthcheck/reports/client-2026-04-12.md`
- `FirebaseCLI/functions/healthcheck/reports/new-findings-2026-04-11.md`
- `FirebaseCLI/functions/healthcheck/reports/new-findings-2026-04-12.md`
- `FirebaseCLI/functions/healthcheck/reports/parity-2026-04-11.md`
- `FirebaseCLI/functions/healthcheck/reports/parity-2026-04-12.md`
- `FirebaseCLI/functions/healthcheck/reports/parity-status-2026-04-12.json`
- `FirebaseCLI/functions/healthcheck/reports/server-2026-04-11.md`
- `FirebaseCLI/functions/healthcheck/reports/server-2026-04-12.md`
- `FirebaseCLI/functions/healthcheck/run-all.ts`
- `FirebaseCLI/functions/healthcheck/scan-client.ts`
- `FirebaseCLI/functions/healthcheck/scan-parity.ts`
- `FirebaseCLI/functions/healthcheck/scan-server.ts`
- `FirebaseCLI/functions/healthcheck/scripts/baseline-triage.ts`
- `FirebaseCLI/functions/healthcheck/scripts/check-enum-offsets.ts`
- `FirebaseCLI/functions/healthcheck/scripts/compare-golden.ts`
- `FirebaseCLI/functions/healthcheck/state-manager.ts`
- `FirebaseCLI/functions/healthcheck/tsconfig.json`
- `FirebaseCLI/functions/healthcheck/types.ts`
