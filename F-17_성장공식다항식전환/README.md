# F-17. 성장 공식 지수 → 다항식(POLY) 전환

> 소스 발췌: `src/` — 4개 파일
>
> 성장 곡선 분석 모듈 + **곡선/공식 분석 리포트 2종**. 지수→다항식 전환 결과를 수치로 확인할 수 있다.

**구간** Phase 2 (2025.12.05 ~ 12.16) | **포지션** TD | **AI** 협업

- **문제**: 성장 수치가 **지수 공식**으로 되어 있어, 후반부에서 수치가 폭발했다. 지수는 초반 구간을 맞추면 후반이 터지고, 후반을 맞추면 초반이 밋밋해진다. 구간별 조절이 구조적으로 불가능하다.
- **해결**: 성장 공식을 **다항식(POLY)**으로 전환하고, 이를 사용하는 **전 영역을 일괄 재밸런싱**했다.
  - 영향 범위를 먼저 특정했다 — `FormulaData` 사용 클래스 목록을 전수 조사(`1. FormulaData_사용_클래스_목록.md`)한 뒤 전환에 착수.
  - 재밸런싱 대상: Awaken(각성) / AwakenGrade / PlayerGrowth / PlayerReinforce / Constellation(성좌) / CommonMod / 장비 보유 옵션 / 몬스터 계층·보스 배율
  - 공식 변경 결과를 확인할 **공식 리포트 기능**을 함께 만들었다.
- **기술**: 지수 → 다항식 공식 치환, 영향 범위 전수 조사, 도메인별 순차 재밸런싱, 리포트 도구
- **정량**: 관련 문서 **13종** (`11. 다항식으로 공식 변환` 디렉토리) / 재밸런싱 도메인 8개 영역
- **근거**:
  - `Docs/배틀/MOD밸런스/11. 다항식으로 공식 변환/25.12.05_1. FormulaData_사용_클래스_목록.md` — 영향 범위 조사
  - `Docs/배틀/MOD밸런스/11. 다항식으로 공식 변환/25.12.05_3. POLY_공식_변환_작업.md` — 전환 작업
  - `Docs/배틀/MOD밸런스/11. 다항식으로 공식 변환/25.12.06_3. 공식_리포트_기능_작업_명세서.md` — 검증 도구
  - `Docs/배틀/MOD밸런스/11. 다항식으로 공식 변환/25.12.14_Awaken_밸런싱.md`, `25.12.14_AwakenGrade_밸런싱.md`, `25.12.14_PlayerReinforce_밸런싱_조정.md`, `25.12.14_Constellation_밸런싱_조정.md`, `25.12.16_PlayerGrowth_밸런싱.md`
- **면접 포인트**: **"공식을 바꾸는 작업의 위험은 공식 자체가 아니라 영향 범위다."** 그래서 전환보다 먼저 `FormulaData` 사용 클래스를 전수 조사했고, 전환 후 확인을 위해 리포트 기능을 만들었다. **조사 → 전환 → 검증 도구 → 도메인별 순차 적용**의 순서가 문서 날짜로 그대로 남아 있다(12.05 조사 → 12.05 전환 → 12.06 리포트 → 12.14~16 도메인별).
- **슬라이드 자료**: 지수 vs 다항식 성장 곡선 비교 그래프 — **다이어그램 필요**


## 수록 파일

- `Assets/Editor/BattleSimulator/Reports/Formula/FormulaAnalysis_20251214_215904.txt`
- `Assets/Editor/BattleSimulator/Reports/GrowthCurve/GrowthCurveAnalysis_2026-01-10_01-03-34.txt`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.GrowthCurve.cs`
- `Assets/Editor/BattleSimulator/Scripts/BattleSimulatorWindow.TheoreticalBalance.cs`
