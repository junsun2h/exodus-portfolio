# F-13. 배틀 시뮬레이터

> 소스 발췌: `src/` — 15개 파일
>
> 전체 54파일 44,782줄 중 **설계 핵심 12파일 + 실행 리포트 3종**을 발췌했다. 밸런스 데이터 JSON(32,842줄)과 보조 툴 윈도우는 제외. `SimulatorAPIController.cs`가 이 툴의 핵심 — 기존 서버 API를 자동 호출해 계정 상태를 세팅한다.

**구간** Phase 2 (2025.11~) | **포지션** 툴·TD | **AI** 협업

- **문제**: **서버 권위 구조에서는 밸런싱을 할 수가 없다.** 데미지 계산에 필요한 스탯·MOD·성장 수치가 전부 서버에 있고, 클라는 결과만 받는다. "각성 3단계를 올리면 DPS가 몇 % 오르는가"를 알려면 실제로 계정을 만들어 각성 3단계까지 플레이해야 한다. 밸런스 수치 하나 바꿀 때마다 이걸 반복하는 것은 불가능하다.
- **해결**: **게임 코드를 한 줄도 바꾸지 않고** 밸런싱을 가능하게 했다. 시뮬레이터가 에디터에서 **기존 서버 API를 그대로 자동 호출**해 실제 계정 상태를 원하는 조건으로 세팅하고, 클라 전투 로직을 그대로 태워 결과를 산출한다. 별도의 시뮬레이션 모델을 만들지 않았으므로 **시뮬레이터와 게임이 어긋날 수 없다.**
  - 규모가 커지면서 Odin 기반 에디터 창을 **partial class 9개 모듈로 분할**해 관리한다.
- **기술**: Unity Editor 확장(Odin Inspector), 기존 API 자동 호출 오케스트레이션, partial class 모듈 분할, F-02의 계산 브레이크다운 덤프 API 활용
- **정량**: **54파일 44,782줄** (프로젝트 전체 수기 코드의 약 15%) / **서버·클라 런타임 코드 변경 0%**
- **근거**:
  - `Assets/Editor/BattleSimulator/` — 54파일 44,782줄
  - `Docs/배틀/시뮬레이터/25.11.25_시뮬레이터_개발_명세서.md`
  - `Docs/배틀/시뮬레이터/완료/mod 최초 정리/25.11.16_UNITY_SIMULATOR_PRD.md`
  - `Docs/배틀/시뮬레이터/25.11.16_작업_진행_TODO.md`
- **면접 포인트**: **"제약을 우회하지 않고 제약 안에서 푼다."** 밸런싱을 위해 서버 로직을 클라에 복제하거나 치트 API를 뚫는 선택지도 있었지만, 둘 다 "시뮬레이터와 실제 게임이 다를 수 있다"는 근본 위험을 만든다. 기존 API를 자동 호출하는 방식은 느리지만 **정의상 실제와 같다.** 게임 코드 변경 0%는 이 판단의 결과다. 44,782줄짜리 툴을 위해 런타임 코드를 한 줄도 오염시키지 않았다는 점이 핵심.
- **슬라이드 자료**: 시뮬레이터 에디터 창 — **캡처 필요** / "일반 방식 vs 기존 API 호출 방식" 대비 다이어그램 — **다이어그램 필요**


## 수록 파일

- `Assets/Editor/BattleSimulator/Reports/CurrentBuildReport/CurrentBuildReport_2025-12-21_00-29-49.txt`
- `Assets/Editor/BattleSimulator/Reports/P1Balance/Build/P1Balance_Build_20260110_013000.txt`
- `Assets/Editor/BattleSimulator/Reports/P1Balance/Stage/P1Balance_Stage_20260110_020000.txt`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.ActionButtons.cs`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.BuildPresets.cs`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.Calculation.cs`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.DropdownProviders.cs`
- `Assets/Editor/BattleSimulator/Scripts/FormulaCollector.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorAPIController.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorCalculator.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorDefender.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorMath.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorModSource.cs`
- `Assets/Editor/BattleSimulator/Scripts/SimulatorPresetData.cs`
- `Assets/Editor/BattleSimulator/Scripts/SnapshotRegression.cs`
