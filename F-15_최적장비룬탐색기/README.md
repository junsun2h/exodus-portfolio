# F-15. 최적 장비/룬 조합 탐색기

> 소스 발췌: `src/` — 3개 파일
>
> 배틀 시뮬레이터(F-13) 내부 모듈. **장비편·룬편 최적화 리포트**가 실제 탐색 결과다.

**구간** Phase 2 (2025.11.21 ~ 11.27) | **포지션** 툴 | **AI** 협업

- **문제**: "이 빌드의 최적 장비 조합"은 조합 폭발 문제다. 장비 슬롯 × 등급 × 옵션 × 룬 조합을 전수 탐색하면 계산량이 감당되지 않고, 게다가 **제약조건**(보유 자원, 슬롯 제한)이 붙는다.
- **해결**: 제약조건 하에서 DPS를 최대화하는 조합 탐색기를 구현했다. 장비편과 룬편을 별도로 만들었다.
  - **중요한 것은 그 다음이다.** 탐색 결과가 실행마다 달라지는 **비일관성(재현성) 문제**를 발견하고, 이를 별도 분석 문서로 원인 규명했다. 최적화 도구는 결과가 재현되지 않으면 신뢰할 수 없으므로, 도구를 쓰기 전에 도구를 검증한 것이다.
- **기술**: 제약조건 하 조합 최적화 탐색, 재현성 디버깅
- **정량**: 탐색기 2종(장비/룬) + 검증 방법 문서 + 비일관성 원인 분석 문서
- **근거**:
  - `Docs/배틀/MOD밸런스/5. 최적장비찾기/25.11.21_최정장비찾기_TODO.md`
  - `Docs/배틀/MOD밸런스/5. 최적장비찾기/25.11.21_최적장비찾기_검증방법.md`
  - `Docs/배틀/MOD밸런스/6. 최적 룬 찾기/25.11.26_최적_룬_찾기_버그_수정.md`
  - `Docs/배틀/MOD밸런스/7. 최적 장비 찾기2/25.11.27_최적_장비_찾기_결과_비일관성_문제_분석.md`
- **면접 포인트**: **"도구의 결과를 믿기 전에 도구를 검증한다."** 최적화 결과가 재현되지 않는다는 것은 곧 탐색 로직에 비결정 요소가 있다는 뜻이고, 그 상태의 결과로 밸런싱을 하면 밸런싱 자체가 무의미해진다. `검증방법.md`를 탐색기와 **같은 날 함께 작성**한 것이 이 프로젝트의 작업 원칙("검증 가능한 성공 기준 먼저")을 그대로 보여준다.
- **슬라이드 자료**: 비일관성 문제 분석 문서 발췌 — **캡처 필요**


<!-- EVIDENCE:START -->
## 실행 결과

> 아래는 이 도구가 실제로 출력한 리포트의 **발췌**입니다. 원본 전체는 `src/` 에 그대로 들어 있습니다.

<details>
<summary>장비 최적화 실행 헤더 — 120개 조합 탐색, DPS 12.2M → 223.7B &nbsp;·&nbsp; <code>src/OptimizationReport_Equipment_20251227_135113.txt</code></summary>

```text
📊 최적화 상세 리포트
====================================
생성 일시: 2025-12-27 13:51:13
프리셋: preset_poison_poisoning
빌드 타입: Ailment 빌드 (상태이상 피해 우선)

이전 DPS: 12196376
최종 DPS: 223650195611
증가량: 223637999235 (+1833643.0%)
소요 시간: 145.2초
테스트 조합: 120개
====================================
```

</details>

<details>
<summary>룬 최적화 — 룬 1개가 아니라 룬 안의 MOD 1개 단위까지 기여도를 분해 &nbsp;·&nbsp; <code>src/OptimizationReport_Runes_20251227_135226.txt</code></summary>

```text
📊 최적화 상세 리포트
====================================
생성 일시: 2025-12-27 13:52:26
프리셋: preset_poison_poisoning
빌드 타입: Ailment 빌드 (상태이상 피해 우선)

이전 DPS: 126014072484
최종 DPS: 470503728362
증가량: 344489655878 (+273.4%)
소요 시간: 61.2초
테스트 조합: 24개
====================================

        …

🔮 룬 테스트 결과:
------------------------------------

총 24개 룬 테스트
  • skillrune_crit_chance
    DPS: 150273559806 | 증가량: +24259487322 (+19.3%)
    [RuneModValues]
      - mod_crit_chance_inc: 0.12 | 기여: +22393372912 (+17.8%)
      - mod_crit_chance: 0.01 | 기여: +1866114409 (+1.5%)
  • skillrune_rapid_decay
    DPS: 149014571148 | 증가량: +23000498664 (+18.3%)
    [RuneModValues]
      - mod_all_duration_inc: -0.10 | 기여: +11500249332 (+9.1%)
      - mod_all_ailment_damage_more: 0.10 | 기여: +11500249332 (+9.1%)
  • skillrune_chance_poisoning
    DPS: 145106537304 | 증가량: +19092464819 (+15.2%)
    [RuneModValues]
      - mod_chance_to_poisoning: 0.04 | 기여: +6364154940 (+5.1%)
      - mod_poison_damage_inc: 0.08 | 기여: +12728309879 (+10.1%)
  • skillrune_poisoning_damage
    DPS: 143245045014 | 증가량: +17230972529 (+13.7%)
    [RuneModValues]
      - mod_poisoning_damage_inc: 0.08 | 기여: +8615486265 (+6.8%)
      - mod_poisoning_damage_more: 0.08 | 기여: +8615486265 (+6.8%)
  • skillrune_crit_multiplier
    DPS: 141670397047 | 증가량: +15656324563 (+12.4%)
```

</details>

<!-- EVIDENCE:END -->

## 수록 파일

- `Assets/Editor/BattleSimulator/Reports/BuildOptimization/OptimizationReport_Equipment_20251227_135113.txt`
- `Assets/Editor/BattleSimulator/Reports/BuildOptimization/OptimizationReport_Runes_20251227_135226.txt`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.BuildOptimization.cs`
