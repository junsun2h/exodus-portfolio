---
description: Phase 5 야간 무인 로직/메커니즘 감사 파이프라인 (서버/클라 정적 감사 + 동등성 골든 + autofix)
---

# Phase 5 Code Healthcheck Skill

## 트리거

- `/code-healthcheck` — 전체 파이프라인 실행 (서버 → autofix → 재스캔 → 동등성 → 클라 → 통합)
- `/code-healthcheck --skip-autofix` — autofix 생략 (탐지만)
- `/code-healthcheck --skip-parity` — 동등성 생략 (Unity Editor 없을 때)
- `/code-healthcheck --skip-client` — 클라이언트 스캔 생략 (빠른 서버 점검)
- `/code-healthcheck report` — 마지막 리포트만 다시 보여주기

## 사전 조건

1. `FirebaseCLI/functions/` 에 `ts-prune`, `madge`, `eslint`, `@typescript-eslint/*`, `eslint-plugin-promise`, `ts-complex` 설치됨
2. Phase 5의 `healthcheck/` 디렉토리 구조 완비:
   - `healthcheck/patterns/server-patterns.json`
   - `healthcheck/patterns/client-patterns.json`
   - `healthcheck/scan-server.ts`
   - `healthcheck/autofix-server.ts`
   - `healthcheck/scan-parity.ts`
   - `healthcheck/scan-client.ts`
   - `healthcheck/aggregate.ts`
   - `healthcheck/run-all.ts`
3. `Docs/plans/테스트/healthcheck/golden/*.json` (5개 입력셋)
4. (선택) Unity Editor가 열려 있으면 CS 골든 + Unity Analyzer 경고 수집 가능. 없으면 해당 단계는 스킵됨 (실패 아님).

## 절차

### 0. Git clean 상태 확인

autofix 단계가 `git stash`로 기존 변경을 보호하지만, 혼란을 줄이기 위해 실행 전 commit 또는 stash 권장.
```bash
git status
```
dirty인 경우 사용자에게 확인 후 진행.

### 1. 전체 파이프라인 실행

```bash
cd FirebaseCLI/functions
"C:/Program Files/nodejs/npm.cmd" run healthcheck
```

또는 직접:
```bash
cd FirebaseCLI/functions
npx ts-node healthcheck/run-all.ts
```

옵션:
- `--skip-autofix`: 수정 시도 없이 탐지만
- `--skip-parity`: 동등성 스킵
- `--skip-client`: 클라이언트 스캔 스킵

### 2. 단계별 내부 실행 순서

`run-all.ts`가 순차적으로 실행:
1. `scan-server.ts` — 서버 정적 감사 (1차, 21개 룰)
2. `autofix-server.ts` — ESLint --fix + codemod + 빌드/테스트 게이트 + 자동 커밋 (안전망 실패 시 즉시 롤백)
3. `scan-server.ts` — autofix 이후 재스캔 (수정된 항목은 state에서 FIXED로 이동)
4. `scan-parity.ts` — TS 골든 실행 → CS 골든 실행 (MCP execute_code) → compare-golden
5. `scan-client.ts` — ripgrep 기반 Tier1 스캔 (7개 파일)
6. `aggregate.ts` — 3트랙 결과 통합 + state.json 갱신 + 최종 리포트 + new-findings 파일

### 3. 결과 확인

- **최종 리포트**: `Docs/plans/테스트/healthcheck/reports/{YYYY-MM-DD}_audit-report.md`
- **신규 발견만**: `FirebaseCLI/functions/healthcheck/reports/new-findings-{date}.md`
- **개별 트랙 리포트**:
  - `healthcheck/reports/server-{date}.md`
  - `healthcheck/reports/parity-{date}.md`
  - `healthcheck/reports/client-{date}.md`
- **autofix 결과**: `healthcheck/reports/autofix-{date}.json`
- **상태 추적**: `healthcheck/state.json` (fingerprint 기반)

### 4. Triage 워크플로우

다음 아침에 사용자가 확인해야 할 것:

1. `2026-XX-XX_audit-report.md` 의 **🚨 S1 섹션**부터 확인
2. 신규 S1이 있으면 → 즉시 수정 세션 시작
3. 신규 S2/S3 → 우선순위에 따라 트리아지
4. 이미 TRIAGED/OPEN인 항목에 `note` 추가하고 싶으면 `state.json` 직접 수정
5. 의도한 패턴(예: 테스트 의도적 try/catch)은 state.json에서 `status: "TRIAGED"`로 변경하여 신규 발견에서 제외

## 심각도 정의 (자동 분류)

| 레벨 | 이름 | 자동 탐지 조건 |
|---|---|---|
| **S1** | 데이터 손상/재무 | floating promise, 트랜잭션 없는 다중 write, GachaHash 미적용, 결정론 외 Random |
| **S2** | 게임플레이 버그 | BigInt 캐스팅, DB optional 필드, Update GetComponent, GC alloc 핫패스, console.log |
| **S3** | 성능/구조 | 복잡도 >15, 순환의존, 독립 await 연쇄, string concat, 에러 메시지 함수명 누락 |
| **S4** | 유지보수 | 데드코드(ts-prune), prefer-const, LOC >1000 |

## 자동 수정 화이트리스트 (autofix가 적용하는 것)

- ESLint `--fix` 기본 룰 (prefer-const, no-var, import 순서)
- `console.log` → `logDebug` codemod (logDebug import가 이미 있는 파일만)

**명시적 제외** (탐지만):
- 모든 S1/S2/S3 로직 수정
- ts-prune 데드코드 제거 (false positive)
- BigInt 캐스팅 교체
- Promise.all 병렬화
- 클라이언트 코드 (Unity 컴파일 필요)

## 실패 처리

autofix 단계가 실패하면 자동으로 `git checkout -- src/` 롤백하고 리포트에 "ROLLBACK" 상태 기록. 스캔 단계는 실패해도 다음 단계로 진행 (리포트에 에러 기록).

## 실행 방식

수동 실행 전용. 필요할 때 `/code-healthcheck` 또는 `npm run healthcheck`로 직접 돌린다.

## 관련 파일

- 실행 본체: `FirebaseCLI/functions/healthcheck/run-all.ts`
- 각 스캐너: `healthcheck/scan-{server,client,parity}.ts`
- 상태: `healthcheck/state.json` (gitignore)
- 리포트: `Docs/plans/테스트/healthcheck/reports/{date}_audit-report.md`
- 룰 정의: `healthcheck/patterns/{server,client}-patterns.json`
- 골든 입력: `Docs/plans/테스트/healthcheck/golden/*.json`

## 스킬 동작 시 주의

Claude가 이 스킬을 호출하면 아래 순서를 **끝까지** 따른다. 중간에 멈추지 않는다.

### Phase A: 파이프라인 실행
1. `git status` 확인 → dirty면 사용자에게 고지
2. `npm run healthcheck` 실행 (`run-all.ts` 경유)
3. 실행 완료 후 최종 리포트 파일을 Read하여 요약 표시

### Phase B: 리포트 분석 및 안내
4. **autofix 상태 확인** — 리포트의 Autofix 섹션을 읽고:
   - `COMMITTED` → "autofix 통과" 표시 후 Phase C로
   - `ROLLBACK` → 실패 원인 분석 후 사용자에게 보고. 원인이 autofix 변경과 무관한 기존 테스트 이슈인지, codemod/eslint 변경이 유발한 문제인지 구분하여 안내
   - `SKIPPED` → skip 사유 표시 후 Phase C로
5. **S1 신규** 있으면 강조 표시 (즉시 확인 필요)
6. **신규 발견 (new-findings)** 파일을 읽고 건수/심각도 요약

### Phase C: OPEN 항목 처리
7. state.json에서 OPEN 상태인 항목을 모두 나열
8. 각 OPEN 항목에 대해 코드를 읽고 분석 제시:
   - 수정 가능한 건 → 수정안 제안 (사용자 승인 후 수정)
   - 의도된 패턴 / false positive → TRIAGED 전환 제안
9. 수정한 건이 있으면 빌드 확인

### Phase D: autofix ROLLBACK 재시도 (해당 시에만)
10. Phase B에서 autofix가 ROLLBACK이었고, 이번 세션에서 원인이 되는 코드를 수정했다면:
    - 사용자에게 "autofix 재실행하여 통과 확인할까요?" 질문
    - 승인 시 healthcheck 재실행 (전체 또는 `--skip-parity --skip-client`로 빠르게)
    - 재실행 후 autofix 상태 다시 확인
11. autofix가 `COMMITTED`가 되면 → autofix 커밋 내용 요약 표시

### Phase E: 마무리
12. 최종 state 현황 요약 (TRIAGED / OPEN / FIXED 건수)
13. 이전 실행 대비 변화량 표시 (신규, FIXED 전환 등)
14. OPEN 0건 + autofix COMMITTED (또는 SKIPPED) 이면 "healthcheck 완료" 선언
15. 아직 OPEN이 남아있거나 autofix ROLLBACK이면 남은 작업 목록 안내
