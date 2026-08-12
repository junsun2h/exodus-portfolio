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
