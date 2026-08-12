# F-01. 계층형 싱글톤 컨텍스트 아키텍처

> 소스 발췌: `src/` — 5개 파일

**구간** Phase 0 (수작업기) | **포지션** 클라·TD | **AI** 미사용

- **문제**: 매니저가 수십 개로 늘어나면 (1) 각자 `MonoBehaviour`를 들고 Unity 생명주기를 개별 구독해 호출 순서가 비결정적이 되고, (2) "GameDB 로드 후에 초기화되어야 하는 매니저"와 "유저 데이터 수신 후에 초기화되어야 하는 매니저"가 뒤섞여 초기화 순서 버그가 상시 발생한다.
- **해결**: **씬에 존재하는 `MonoBehaviour`는 `ApplicationContext` 하나뿐**이고, 나머지 65개 매니저는 순수 C# 싱글톤으로 컨텍스트 딕셔너리에 등록된다. `ApplicationContext`가 Unity 이벤트를 받아 등록된 전체 매니저에 순차 전파한다.
  - 초기화 순서 문제는 **커스텀 생명주기 훅**으로 구조화했다. `InitData` → `InitAfterGameDB` → `InitAfterUserData` 순으로 단계가 나뉘어 있어, 각 매니저는 "나는 어느 단계에 초기화되어야 하는가"만 선언하면 된다. 호출 순서를 매니저끼리 알 필요가 없다.
  - `ShutdownFirebase` 훅은 **Firebase 네이티브 리소스 해제 직전**에 호출된다. 스냅샷 리스너를 먼저 끊지 않으면 네이티브 객체 파괴 후 Firebase 워커 스레드가 해제된 메모리를 참조해 에디터가 액세스 위반으로 죽는데, 이 훅이 그 시점을 보장한다.
  - 싱글톤 규약은 **런타임 리플렉션으로 강제**한다. `GetConstructors()`로 public 생성자 존재를 검사해, 하나라도 있으면 인스턴스 생성 시점에 `InvalidOperationException`을 던진다. 규약 위반이 컴파일이 아닌 **최초 접근 시점에 즉시 터진다.**
- **기술**: C# 제네릭 싱글톤(`Singleton<T>` / `SingletonDependency<T>`), 리플렉션 기반 생성자 검증, `Activator.CreateInstance(t, true)`로 private 생성자 호출, 타입명 키 딕셔너리 레지스트리, `DontDestroyOnLoad`
- **정량**:
  - 등록 매니저 **65개** — Game 25 / Firebase 4 / Security 1 / GameAPI 28 (`LogicContext`의 `AddSingleton` 58회) + Mono 7 (`MonoContext`)
  - 미러링 생명주기 이벤트 **20종** (`Awake`~`OnDestroy`, `OnRenderImage`·`OnPreCull` 등 렌더 이벤트 포함)
  - 커스텀 훅 **5종** — `InitData` / `ClearAllData` / `ShutdownFirebase` / `InitAfterGameDB` / `InitAfterUserData`
- **근거**:
  - `Assets/Source/Application/ApplicationContext.cs` (230줄) — 유일한 MonoBehaviour, 이벤트 전파 지점
  - `Assets/Source/Application/Singleton/Singleton.cs` (145줄) — `SingletonBase` 훅 정의 + 리플렉션 규약 강제
  - `Assets/Source/Application/Context/Context.cs` (28줄) — `MonoEvent` 20종 시그니처
  - `Assets/Source/Logic/LogicContext.cs` (212줄) — 매니저 58개 등록
  - `Assets/Source/Application/Singleton/MonoContext.cs` (26줄) — Mono 매니저 7개 등록
- **면접 포인트**: "싱글톤을 썼다"가 아니라 **"싱글톤의 알려진 단점 3가지(생명주기 비결정성 / 초기화 순서 의존 / 규약 미강제)를 각각 구조로 막았다"**. 특히 `ShutdownFirebase` 훅은 실제 크래시(네이티브 메모리 참조)를 겪고 나서 생명주기에 단계를 하나 추가해 해결한 사례로, 프레임워크 레벨의 문제 해결 경험을 보여준다.
- **슬라이드 자료**: 컨텍스트 계층 + 생명주기 훅 순서 다이어그램 — **다이어그램 필요**


## 수록 파일

- `Assets/Source/Application/ApplicationContext.cs`
- `Assets/Source/Application/Context/Context.cs`
- `Assets/Source/Application/Singleton/MonoContext.cs`
- `Assets/Source/Application/Singleton/Singleton.cs`
- `Assets/Source/Logic/LogicContext.cs`
