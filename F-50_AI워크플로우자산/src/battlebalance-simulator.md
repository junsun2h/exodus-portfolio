---
description: 배틀 시뮬레이터 에디터 도구 파일 구조 탐색 전용
---

# 배틀 시뮬레이터 핵심 파일 인덱스

## 사용하지 말아야 할 때
- 일반 배틀/밸런스 질문(피해 계산 공식, 밸런싱 의사결정, 스테이지/콘텐츠 분석)에는 사용하지 않는다 — 그건 `battlebalance` 계열 스킬·분석 흐름의 영역이다.
- 이 인덱스는 **배틀 시뮬레이터 에디터 도구의 파일 구조를 탐색**해야 할 때(특정 윈도우/계산기/검증 스크립트의 위치를 찾을 때)만 참고한다.

## 기본 경로
`Assets/Editor/BattleSimulator/Scripts/`

## 핵심 파일별 용도

| 파일 | 용도 | 비고 |
|------|------|------|
| `BattleSimulatorWindow.cs` | 메인 UI 윈도우 | Odin Inspector 기반 |
| `SimulatorCalculator.cs` | DPS/피해 계산 | `CalculateRealDamage()` |
| `SimulatorAPIController.cs` | API 호출 자동화 | 장비/스킬 장착 |
| `SimulatorDefender.cs` | 방어자 스탯 관리 | 저항/방어력 |

## 파셜 클래스 구조 (BattleSimulatorWindow.*)

| 파일 | 기능 |
|------|------|
| `.ModManagement.cs` | MOD 관리 UI |
| `.Calculation.cs` | 계산 로직 |
| `.Report.cs` | 리포트 생성 |
| `.BuildOptimization.cs` | 빌드 최적화 |
| `.ModAnalysis.cs` | MOD 분석 |
| `.GrowthCurve.cs` | 성장 곡선 |
| `.FormulaReport.cs` | 공식 리포트 |
| `.TheoreticalBalance.cs` | 이론 밸런스 |
| `.BuildPresets.cs` | 빌드 프리셋 |
| `.SkillSpec.cs` | 스킬 스펙 |
| `.ActionButtons.cs` | 액션 버튼 |
| `.InlineModAssignment.cs` | 인라인 MOD 할당 |
| `.ModWeightReportUI.cs` | MOD 가중치 리포트 |
| `.FormulaReportUI.cs` | 공식 리포트 UI |
| `.DropdownProviders.cs` | 드롭다운 데이터 |

## 검증 시스템
**경로**: `Scripts/Verification/`

| 파일 | 용도 |
|------|------|
| `DamageVerificationSystem.cs` | 피해 검증 시스템 |
| `DamageVerificationWindow.cs` | 검증 UI 윈도우 |
| `LiveDamageCollector.cs` | 실시간 피해 수집 |
| `StatisticalAnalyzer.cs` | 통계 분석 |
| `VerificationResult.cs` | 검증 결과 데이터 |
| `LiveVerificationResult.cs` | 실시간 검증 결과 |
| `LiveDamageEvent.cs` | 실시간 피해 이벤트 |
| `ModPercentageReportSystem.cs` | MOD 퍼센티지 리포트 |

## 스킬 효율 디자이너
**경로**: `Scripts/SkillEfficiencyDesigner/`

| 파일 | 용도 |
|------|------|
| `SkillEfficiencyDesignerWindow.cs` | 스킬 효율 디자이너 UI |
| `EfficiencyCalculator.cs` | 효율 계산기 |
| `MechanismConfig.cs` | 메카니즘 설정 |

## 보조 도구

| 파일 | 용도 |
|------|------|
| `ModViewerWindow.cs` | MOD 뷰어 |
| `ConstellationInfoWindow.cs` | 성좌 정보 |
| `ModCodeScanner.cs` | MOD 코드 스캔 |
| `ModStageAutoMapper.cs` | 스테이지 자동 매핑 |
| `BuildPresets.cs` | 빌드 프리셋 데이터 |
| `SimulatorPresetData.cs` | 시뮬레이터 프리셋 |
| `ModWeightData.cs` | MOD 가중치 데이터 |
| `ModStageMetadata.cs` | 스테이지 메타데이터 |
| `SimulatorModSource.cs` | MOD 소스 추적 |
| `ModValueTypeChecker.cs` | MOD 값 타입 검사 |
| `FormulaCollector.cs` | 공식 수집기 |
| `ModChangeTracker.cs` | MOD 변경 추적 |
| `ModCsvDiffTool.cs` | CSV 비교 도구 |
| `ModImplementationHelper.cs` | MOD 구현 헬퍼 |
| `PlayerReinforceModWindow.cs` | 플레이어 강화 MOD |
| `EquipmentCommonModCalculatorWindow.cs` | 장비 공통 MOD 계산 |
| `EquipmentMythicModExtractorWindow.cs` | 신화 장비 MOD 추출 |

## 상세 문서

| 문서 | 경로 |
|------|------|
| 개발 명세서 | `Docs/배틀/시뮬레이터/25.11.25_시뮬레이터_개발_명세서.md` |
| MOD 할당 명세 | `Docs/배틀/시뮬레이터/25.11.16_MOD_할당_작업_명세서.md` |
| 작업 TODO | `Docs/배틀/시뮬레이터/25.11.16_작업_진행_TODO.md` |
