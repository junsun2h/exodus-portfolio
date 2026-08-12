# F-09. 부가 자체 시스템군

> 소스 발췌: `src/` — 5개 파일

**구간** Phase 0 (수작업기) | **포지션** 클라 | **AI** 미사용

개별 카드로 분리할 규모는 아니지만, **외부 라이브러리 없이 직접 만든 시스템**들이다. "필요한 것을 그때그때 만들어 붙였다"가 아니라 각각 명확한 문제에 대응한다.

| 시스템 | 문제 | 해결 | 규모 |
|---|---|---|---|
| **네트워크 추상화** | 95종 API를 개별 호출 코드로 작성하면 중복 요청·응답 처리가 산발 | 열거형 기반 요청 디스패치 + 요청 객체 풀 + **중복 요청 차단** | `GameNetworkManager.cs` 976줄 |
| **구독형 레드닷** | "새 아이템 있음" 표시를 화면마다 수동 갱신하면 반드시 누락 발생 | CoreData 변경 이벤트(F-03)를 구독해 **데이터가 바뀌면 레드닷이 자동으로 켜진다** | `GameReddotManager.cs` 950줄 |
| **스냅샷 Diff 자동 보상 산출** | 오프라인/일괄 처리 후 "무엇이 얼마나 늘었는지"를 서버가 따로 계산해 내려주면 API마다 중복 | 처리 전후 CoreData 스냅샷을 **Diff해서 보상 목록을 자동 산출** | `CoreDataSnapshotManager.cs` 497줄 |
| **`PXBigInt` 무한 재화 타입** | 방치형은 재화가 `long` 범위를 넘어 인플레이션한다 | 방치형 표기(a, b, c… 단위) 대응 자체 큰수 타입 | `Assets/Source/Shared/Types/BigIntTypes.cs` |
| **자체 StringKey 로컬라이제이션** | 외부 로컬라이제이션 패키지는 GameDB 시트 파이프라인과 별도 관리가 됨 | GameDB 시트를 SSOT로 삼는 자체 StringKey 체계 (Phase 2에서 자동 생성까지 확장 → F-25) | `Assets/Source/Logic/Manager/GameDBManager/GameDB_StringKey.cs` |
| **사운드 보이스 스틸링** | 전투 중 동일 효과음이 수십 개 동시 재생되면 클리핑·성능 저하 | 채널 수 제한 + 우선순위 기반 **보이스 스틸링** | `GameSoundManager.cs` 1,544줄 |

- **정량**: 6개 시스템 합계 약 **4,000줄** (표기된 4개 매니저 합 3,967줄 + 타입·StringKey)
- **면접 포인트**: 개별로는 작지만 **공통점이 하나 있다 — 전부 "수동으로 하면 반드시 빠뜨리는 것"을 자동화했다.** 레드닷은 데이터 변경에 자동 반응하고, 보상은 Diff로 자동 산출되고, 중복 요청은 매니저가 자동 차단한다. 이 사고방식이 Phase 3의 무인 감사 파이프라인(F-34)까지 이어진다.
- **슬라이드 자료**: 레드닷 자동 전파 흐름 (CoreData 변경 → 레드닷) — **다이어그램 필요** / 나머지는 목록 슬라이드 1장


## 수록 파일

- `Assets/Source/Logic/Manager/GameManager/GameNetworkManager.cs`
- `Assets/Source/Logic/Manager/GameManager/GameReddotManager.cs`
- `Assets/Source/Logic/Manager/GameMonoManager/GameSoundManager.cs`
- `Assets/Source/Shared/Types/BigIntTypes.cs`
- `Assets/Source/System/ObjectDiffUtility.cs`
