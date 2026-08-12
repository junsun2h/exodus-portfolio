# F-16. MOD 체계 정립

> 소스 발췌: `src/` — 5개 파일
>
> 271종 MOD의 분류·매핑·검증을 담당하는 시뮬레이터 모듈 + **MOD 기여도 리포트**.

**구간** Phase 2 (2025.11 ~ 12) | **포지션** TD | **AI** 협업

- **문제**: MOD가 271종까지 늘어나면서 (1) 명명이 제각각이라 같은 개념이 다른 이름으로 존재하고, (2) 어떤 MOD가 어느 콘텐츠에 붙는지 파악이 안 되며, (3) `Increased`와 `More`의 구분이 코드에는 있는데 데이터에는 일관되게 반영되지 않았다.
- **해결**: MOD 체계를 문서로 확정했다.
  - **분류체계** — MOD를 카테고리로 정리
  - **명명규칙** — `EMod` 열거형 명명을 규칙화
  - **단계관리** — MOD 공식의 단계(레벨) 관리 시스템
  - **inc/more 승수 분리 개편** — 데이터의 `Increased`/`More` 구분을 코드 체계에 맞춰 전면 정리 (Phase 1-3 완료보고 → Phase 1-4 최종요약까지 단계 진행)
  - Ailment/DoT/흡수/폭발 등 특수 규칙도 별도 위키로 확정
- **기술**: 열거형 분류체계 설계, 명명 규칙 표준화, 데이터-코드 정합성 일괄 정리
- **정량**: `EMod` **271종** 전수 정리 / 관련 문서 다수 (분류체계·명명규칙·요약·검증보고서·개편 TODO)
- **근거**:
  - `Docs/배틀/시뮬레이터/완료/EMod분류/25.11.16_MOD분류체계.md`, `Docs/배틀/시뮬레이터/완료/EMod분류/25.11.16_EMod명명규칙.md`, `Docs/배틀/시뮬레이터/완료/EMod분류/25.11.16_mod_summary.md`
  - `Docs/배틀/시뮬레이터/완료/inc, more 개편/25.11.16_incmore개편_Phase1-3_완료보고.md`, `Docs/배틀/시뮬레이터/완료/inc, more 개편/25.11.16_incmore개편_Phase1-4_최종요약.md`
  - `Docs/배틀/시뮬레이터/완료/MOD_단계관리/25.11.16_MOD공식단계관리시스템_TODO.md`
  - `Docs/배틀/시뮬레이터/완료/mod 최초 정리/25.11.16_MOD_SOURCE_VALIDATION_REPORT.md`
  - `Docs/배틀/_기획/위키/25.11.16_위키_ailment.md`, `Docs/배틀/_기획/위키/25.11.16_위키_dot-damage-over-time.md`, `Docs/배틀/_기획/POE/26.01.01_POE_흡수.md`, `Docs/배틀/_기획/위키/26.01.01_위키_폭발(explode).md`
- **면접 포인트**: **271종 규모의 데이터 체계를 사후에 정리한 경험.** 처음부터 완벽한 명명 규칙을 세우는 것보다, 실제로 271종이 쌓인 뒤에 분류·명명·검증 리포트를 만들어 일괄 정리하는 쪽이 현실적이다. `MOD_SOURCE_VALIDATION_REPORT`처럼 **정리 결과를 검증 리포트로 확인**한 것이 요점 — 정리했다고 선언하지 않고 검증했다.
- **슬라이드 자료**: MOD 분류체계 표 — **캡처 필요**


<!-- EVIDENCE:START -->
## 실행 결과

> 아래는 이 도구가 실제로 출력한 리포트의 **발췌**입니다. 원본 전체는 `src/` 에 그대로 들어 있습니다.

<details>
<summary>MOD 백분율 처리 리포트 — 어떤 MOD가 어느 단계에서 어떤 규칙으로 곱해졌는지 전 단계 추적 &nbsp;·&nbsp; <code>src/ModPercentageReport_2025-12-21_01-18-55.txt</code></summary>

```text
[단계 1] Flat Damage (기본 피해)
  설명: 속성별 기본 피해 + 공통 피해를 합산
  공식: ∑(mod_xxx_damage)
  결과: 27824363.691596
  사용 MOD (1개):
    - mod_all_damage (FLOAT)
      저장값: 27824363.691596 | 표시값: 27824363.691596
      처리: Direct | 공식: total += value

════════════════════════════════════════════════════════════════════
[단계 2] Inc 배율 (Increased)
  설명: 증가 MOD들을 합산 후 (1 + 합계)로 배율 계산
  공식: 1 + ∑(mod_xxx_inc)
  결과: 37.515500
  사용 MOD (3개):
    - mod_elemental_damage_inc (FLOAT_PER)
      저장값: 0.250000 | 표시값: 25.00%
      처리: Direct | 공식: totalInc += value
    - mod_all_skill_damage_inc (FLOAT_PER)
      저장값: 4.856000 | 표시값: 485.60%
      처리: Direct | 공식: totalInc += value
    - mod_all_damage_inc (FLOAT_PER)
      저장값: 31.409500 | 표시값: 3140.95%
      처리: Direct | 공식: totalInc += value

════════════════════════════════════════════════════════════════════
[단계 3] More 배율 (More)
  설명: 증폭 MOD들을 개별적으로 (1 + value)를 곱셈
  공식: ∏(1 + mod_xxx_more)
  결과: 1.000000

════════════════════════════════════════════════════════════════════
```

</details>

<!-- EVIDENCE:END -->

## 수록 파일

- `Assets/Editor/BattleSimulator/Reports/ModPercentage/ModPercentageReport_2025-12-21_01-18-55.txt`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.ModManagement.cs`
- `Assets/Editor/BattleSimulator/Scripts/ModChangeTracker.cs`
- `Assets/Editor/BattleSimulator/Scripts/ModStageAutoMapper.cs`
- `Assets/Editor/BattleSimulator/Scripts/ModValueTypeChecker.cs`
