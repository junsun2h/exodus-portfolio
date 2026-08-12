# F-18. 인게임 전투 실시간 측정 시스템

> 소스 발췌: `src/` — 10개 파일
>
> 런타임 기록기 + 에디터 검증 모듈 전체 + **시뮬레이션 이론값 vs 인게임 실측 대조 리포트**.

**구간** Phase 2 (2025.12.30 ~ 2026.01) | **포지션** 툴·TD | **AI** 협업

- **문제**: 시뮬레이터(F-13)가 산출하는 것은 **이론값**이다. 실제 인게임 전투에서는 이동·쿨타임·타겟팅·사거리 때문에 이론 DPS가 그대로 나오지 않는다. **"시뮬레이터가 맞는가"를 검증할 방법이 없었다.**
- **해결**: 런타임 → 에디터로 이어지는 **측정 파이프라인**을 만들었다.
  - 런타임의 `DamageEventRecorder`가 실제 전투의 모든 피해 이벤트를 기록
  - 에디터의 스테이지 전투 분석기가 이를 받아 **시뮬레이션 이론값과 실측값을 자동 대조**
  - **원클릭 피해 검증**에 **허용오차 3등급**을 두어, 오차가 어느 등급인지로 판정한다. "다르다/같다"가 아니라 "얼마나 다른가"를 등급으로 관리한다.
- **기술**: 런타임 이벤트 레코더, 에디터 분석기, 이론값-실측값 자동 대조, 허용오차 등급 판정
- **정량**: 허용오차 **3등급** 원클릭 검증 / 인게임 전투 전 피해 이벤트 기록
- **근거**:
  - `Assets/Source/Logic/Common/DamageEventRecorder.cs` — 런타임 기록기
  - `Docs/배틀/MOD밸런스/13. 인게임 전투 실시간 측정 시스템/26.01.01_작업명세서.md`
  - `Docs/배틀/MOD밸런스/12. 인게임 피해와 다른 문제/26.01.01_6. 마나생명력_재생흡수.md` — 이론-실측 괴리 사례
- **면접 포인트**: **"시뮬레이터를 만들었으면 시뮬레이터를 검증해야 한다."** F-15에서 탐색기의 재현성을 검증한 것과 같은 사고방식이 한 단계 위로 적용된 사례다. 디렉토리 이름이 `12. 인게임 피해와 다른 문제` → `13. 인게임 전투 실시간 측정 시스템`으로 이어지는 것이 서사 그 자체 — **괴리를 발견하고, 괴리를 상시 측정하는 시스템으로 대응했다.** 허용오차를 3등급으로 나눈 것은 "완전 일치는 불가능하다"는 현실 인식.
- **슬라이드 자료**: 이론값 vs 실측값 대조 리포트 — **캡처 필요** / 런타임→에디터 파이프라인 다이어그램 — **다이어그램 필요**


<!-- EVIDENCE:START -->
## 실행 결과

> 아래는 이 도구가 실제로 출력한 리포트의 **발췌**입니다. 원본 전체는 `src/` 에 그대로 들어 있습니다.

<details>
<summary>6개 도메인 전부 이론값 = 실측값 통과 (7ms) &nbsp;·&nbsp; <code>src/SimVsInGameDps_2025-12-30_20-13-04.txt</code></summary>

```text
════════════════════════════════════════════════════════════════
📋 검증 결과 요약
════════════════════════════════════════════════════════════════
🔮 Spell: ✅ 통과
🌀 Aura: ✅ 통과
💥 Ailment: ✅ 통과
☠️ DoT: ✅ 통과
🛡️ Defense: ✅ 통과
👹 Monster→Player: ✅ 통과

📊 전체: ✅ 모든 검증 통과
```

</details>

<details>
<summary>Spell 피해 항목별 대조 — 11단계 계산 중간값을 전부 맞춰본다 &nbsp;·&nbsp; <code>src/SimVsInGameDps_2025-12-30_20-13-04.txt</code></summary>

```text
════════════════════════════════════════════════════════════════
🔮 SPELL DAMAGE 검증 (skillspell_poisonarrow)
════════════════════════════════════════════════════════════════

📊 스킬 강화도 정보:
  • 스킬 티어: mythic
  • 피해 속성: skilltag_poison
  • Core 강화도: 65
  • 강화도 모드 증가: +10
  • 최종 강화도: 75
  • Skill Effectiveness: 5.3277x (532.77%)

┌──────────────────────┬──────────────┬──────────────┬───────┬─────┐
│ 항목                 │ 시뮬레이션   │ 인게임       │ 오차% │상태 │
├──────────────────────┼──────────────┼──────────────┼───────┼─────┤
│ Flat Damage          │   27,824,364 │   27,824,364 │ 0.00% │ ✅  │
│ Skill Effectiveness  │      5.3277x │      5.3277x │ 0.00% │ ✅  │
│ Base Damage          │  148,240,015 │  148,240,015 │ 0.00% │ ✅  │
│ 크리티컬 전 피해            │ 6,680,187,966 │ 6,680,187,966 │ 0.00% │ ✅  │
│ 크리티컬 확률              │       56.44% │       56.44% │ 0.00% │ ✅  │
│ 크리티컬 배율              │     1168.20% │     1168.20% │ 0.00% │ ✅  │
│ 치명타 일격 확률            │       20.00% │       20.00% │ 0.00% │ ✅  │
│ 치명타 일격 배율            │      375.00% │      375.00% │ 0.00% │ ✅  │
│ 평균 크리티컬 배율           │     10.6552x │     10.6552x │ 0.00% │ ✅  │
│ 시전 속도                │            2 │            2 │ 0.00% │ ✅  │
│ Spell DPS            │ 121,096,835,788 │ 121,096,835,788 │ 0.00% │ ✅  │
└──────────────────────┴──────────────┴──────────────┴───────┴─────┘
```

</details>

<!-- EVIDENCE:END -->

## 수록 파일

- `Assets/Editor/BattleSimulator/Reports/SimVsInGameDps/SimVsInGameDps_2025-12-30_20-13-04.txt`
- `Assets/Editor/BattleSimulator/Scripts/Verification/DamageVerificationSystem.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/DamageVerificationWindow.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/LiveDamageCollector.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/LiveDamageEvent.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/LiveVerificationResult.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/ModPercentageReportSystem.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/StatisticalAnalyzer.cs`
- `Assets/Editor/BattleSimulator/Scripts/Verification/VerificationResult.cs`
- `Assets/Source/Logic/Common/DamageEventRecorder.cs`
