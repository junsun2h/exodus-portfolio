# F-33. 서버 테스트 커버리지 확보

> **코드 발췌 없음.** 이 항목은 전역 정책 변경·조사·설계 문서 형태의 작업이라
> 특정 파일로 잘라낼 수 없다. 아래 카드의 *근거* 항목이 실제 산출물이다.

**구간** Phase 3 (2026.04.10 ~ 04.13) | **포지션** 서버 | **AI** 협업

- **문제**: 서버 테스트가 **1408 pass / 47 fail** 상태였다. fail이 47개 쌓여 있으면 새 실패가 묻히고, 테스트 스위트 전체가 신호를 잃는다. 커버리지도 라인 80.34%에서 정체.
- **해결**: 47개 fail을 개별 수정하지 않고 **근본원인 5종으로 분류**한 뒤 유형별로 해결해 **0 fail**을 만들었다. 신호가 살아난 뒤에야 저커버리지 파일을 골라 테스트를 증설했다.
- **기술**: mocha 테스트 스위트, 실패 유형 분류, 커버리지 계측(nyc/c8), Firebase 에뮬레이터 테스트 환경
- **정량**: (지표 두 종을 구분해 표기 — 전체 집계는 istanbul 총계, 파일별 평균은 97개 파일 Lines% 의 산술평균)
  - **1408 pass / 47 fail → 1640 pass / 0 fail** (04.10, 근본원인 정리 당일)
  - **최종 1793 pass / 6 fail** (04.13) — 테스트 **+385개**. 잔여 6건은 커버리지 확장 중 새로 추가한 테스트에서 발생한 것으로, 원래의 47건과는 별개
  - 전체 집계 Lines 커버리지 **80.34% → 82.06%** (Statements 80.06% → 81.77%)
  - 파일별 평균 Lines 커버리지 **83.35%**(04.12) **→ 89.53%**(04.13)
  - 서버 테스트 **39,199줄** / 수기 구현 29,003줄 = **테스트:구현 비율 135%**
- **근거**:
  - `Docs/plans/테스트/26.04.10_전체-코드-검수-및-테스트-보강-작업-추적.md`
  - `FirebaseCLI/functions/Docs/테스트/2026-04-13_coverage-report.md`, `FirebaseCLI/functions/Docs/테스트/2026-04-13_test-report.md`, `FirebaseCLI/functions/Docs/테스트/2026-04-13_full-pipeline-report.md`
  - `FirebaseCLI/functions/Docs/테스트/2026-04-12_coverage-report.md` — 중간 경과
  - `FirebaseCLI/functions/Docs/테스트/26.02.22_테스트-커버리지-측정-환경-구성.md` — 측정 환경 선행 구축(Phase 2)
- **면접 포인트**: **"47개 실패를 47번 고치지 않았다."** 근본원인 5종 분류가 요점이다 — `CoreDataFactory._datasOriginal` 미설정 28건, Stage `CurrentStage_Main` 불일치 13건, Product VIP 체크 7건, CombatPower 리네이밍 4건, 기타 5건. 개별 수정은 같은 실패를 다시 만들지만, 유형별 해결은 재발을 막는다. 또한 **테스트 코드가 구현 코드보다 많은(135%) 상태**를 1인 개발에서 유지하고 있다는 점, 그리고 커버리지 측정 환경을 2월에 미리 구축(`26.02.22`)한 뒤 4월에 개선에 들어간 **준비 → 실행 순서**가 특징.
- **슬라이드 자료**: 커버리지 리포트 (파일별 평균 Lines 83.35% → 89.53%) — **캡처 필요**

<!-- EVIDENCE:START -->
## 실행 결과

> 원본 리포트는 이 리포에 포함하지 않았다(사내 문서 경로). 아래는 그 리포트에서 옮긴 수치다.

<details>
<summary>47건의 근본원인 5종 분류 &nbsp;·&nbsp; <code>26.04.10_전체-코드-검수-및-테스트-보강-작업-추적.md</code></summary>

| 근본원인 | 건수 |
|---|---:|
| `CoreDataFactory._datasOriginal` 미설정 | 28 |
| Stage `CurrentStage_Main` 불일치 | 13 |
| Product VIP 체크 | 7 |
| CombatPower 리네이밍 | 4 |
| 기타 | 5 |
| **합계** | **47** |

수정 범위는 `src` 4파일 + `test` 9파일. 47개 테스트를 개별로 손대지 않았다.

</details>

<details>
<summary>커버리지 전후 (전체 집계, istanbul 총계) &nbsp;·&nbsp; <code>26.04.10_전체-코드-검수-및-테스트-보강-작업-추적.md</code></summary>

| 지표 | 시작 | 최종 | 변화 |
|---|---|---|---|
| Statements | 80.06% (10758/13437) | **81.77%** | +1.71%p |
| Branches | 63.92% (2201/3443) | **66.23%** | +2.31%p |
| Functions | 79.32% (1891/2384) | **81.38%** | +2.06%p |
| Lines | 80.34% (10467/13028) | **82.06%** | +1.72%p |

</details>

<details>
<summary>테스트 증설 후 파일별 Lines% 변화 &nbsp;·&nbsp; <code>2026-04-13_coverage-report.md</code></summary>

| 파일 | 개선 전 | 개선 후 | 변화 |
|---|---|---|---|
| `GenerateDummy.ts` | 2.04% | 22.90% | +20.86%p |
| `CoreDataDump.ts` | 16.26% | 59.35% | +43.09%p |
| `Contents.ts` | 67.65% | 79.41% | +11.76%p |
| `UtilityUserDocument.ts` | 63.97% | 80.97% | +17.00%p |
| `UtilityGame.ts` | 76.09% | **100.00%** | +23.91%p |
| `UtilityJson.ts` | 77.42% | **100.00%** | +22.58%p |
| `UtilityTypeScript.ts` | 78.26% | **100.00%** | +21.74%p |
| `PRNGServer.ts` | 77.89% | 77.89% | 0 (테스트 실패) |

**총 생성 케이스 58개** (신규 41 + 기존 보강 17). 대상 선정 기준은 "Lines 50% 미만이면서
테스트가 있는데 30% 미만" 2개 + "Lines 50~80% 인데 테스트 파일 자체가 없음" 6개.

</details>

<details>
<summary>최종 상태 &nbsp;·&nbsp; <code>2026-04-13_full-pipeline-report.md</code></summary>

| 지표 | 값 |
|---|---|
| 전체 파일 수 | 97개 |
| 파일별 평균 Lines 커버리지 | **89.53%** |
| 테스트 결과 | 1793 passing / 6 failing |
| 50% 미만 파일 | 3개 (전부 타입·enum·re-export 전용, 실행 라인 없음) |

잔여 6 failing 은 커버리지 확장 중 새로 추가한 테스트에서 발생했다. 원인은
`sinon` stub 이 `fs.existsSync` 에 적용되지 않는 문제(non-configurable) 4건,
`handleRequest` 흐름의 응답 검증 2건. 원래의 47건과는 성격이 다른 신규 항목이라
별도 트랙으로 분리했다.

</details>
<!-- EVIDENCE:END -->
