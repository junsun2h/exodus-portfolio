# F-03. CoreData 서버-클라 동기화 계층

> 소스 발췌: `src/` — 3개 파일

**구간** Phase 0 (수작업기) | **포지션** 클라·서버 | **AI** 미사용

- **문제**: 서버 권위 구조에서 유저 데이터는 Firestore에 있고 클라는 스냅샷으로 받는다. 그런데 (1) 스냅샷이 올 때마다 전체를 재파싱하면 필드 하나 바뀐 경우에도 전량 갱신이 일어나고, (2) 어느 데이터가 바뀌었는지를 UI가 알 방법이 없어 화면 갱신을 수동으로 호출해야 한다.
- **해결**: `ECoreDataChangeType` 델타 파싱 계층을 만들어 **변경된 필드만 부분 갱신**하고, 그 변경 사실을 **이벤트로 발행**해 UI와 레드닷이 자동으로 반응하게 했다. 데이터가 바뀌면 화면이 따라오는 것이 기본 동작이 되어, 화면 갱신 호출을 잊어서 생기는 버그 자체가 사라진다.
- **기술**: Firestore 스냅샷 리스너, 변경 타입 열거형 기반 델타 파싱, 이벤트 발행/구독, CoreData 추상 베이스 + 도메인별 구현체
- **정량**: CoreData 구현체 **24종** (클라 측 관련 파일 25개) / 변경 이벤트가 레드닷 시스템(`GameReddotManager.cs` 950줄)으로 자동 전파
- **근거**:
  - `Assets/Source/Logic/Common/CommonCoreData.cs` (113줄) — 공통 베이스
  - `Assets/Source/Repository/CommonDefine.cs` — `ECoreDataChangeType` 정의
  - `Assets/Source/Logic/Manager/GameAPIManager/CoreDataSnapshotManager.cs` (497줄) — 스냅샷 처리
- **면접 포인트**: **"서버 권위 구조에서 클라의 최신성을 어떻게 보장하는가"**에 대한 답. 폴링이 아니라 스냅샷 델타 + 이벤트 전파로 풀었고, 이 구조 덕분에 Phase 2의 자동 보상 산출(`CoreDataSnapshotManager.cs` — 처리 전후 스냅샷을 Diff해 보상 목록을 자동 산출, `src/` 에 수록)과 Phase 3의 마이그레이션 자동화(F-19)가 같은 스키마 위에서 성립했다.
- **슬라이드 자료**: 스냅샷 → 델타 파싱 → 이벤트 전파 → UI/레드닷 흐름도 — **다이어그램 필요**


## 수록 파일

- `Assets/Source/Logic/Common/CommonCoreData.cs`
- `Assets/Source/Logic/Manager/GameAPIManager/CoreDataSnapshotManager.cs`
- `Assets/Source/Repository/CommonDefine.cs`
