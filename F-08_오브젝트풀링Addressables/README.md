# F-08. 오브젝트 풀링 + Addressables 무중단 마이그레이션

> 소스 발췌: `src/` — 3개 파일

**구간** Phase 0 (수작업기) | **포지션** 클라 | **AI** 미사용

- **문제**: (1) 방치형 RPG 전투는 몬스터·투사체·이펙트·데미지 텍스트가 초당 수십~수백 개 생성·파괴된다. GC 스파이크가 곧 프레임 드랍이다. (2) 레거시 `AssetBundle` API가 코드 전역에 퍼져 있는 상태에서 Addressables로 갈아타려면 호출부를 전부 수정해야 한다.
- **해결**:
  - **풀링을 값 객체 레벨까지 내렸다.** 프리팹뿐 아니라 전투 계산에서 초당 수천 개 생성되는 `ModValue` 같은 값 객체까지 풀링 대상이다. 풀 컬렉션은 대상 성격별로 분리했다 — 애니메이터 컨트롤러 / 어태치먼트 / 캐릭터 / 프롭 / 투사체 액터 / 스킬 투사체 / 다이내믹 스펠.
  - **Addressables 전환은 API 시그니처를 유지한 채 내부만 교체**했다. 호출부는 한 줄도 바뀌지 않는다. 전환 과정에서 **in-flight 중복 로드 병합**(같은 에셋을 동시에 여러 곳이 요청하면 로드는 1회, 결과는 전원에게)을 추가해 전환 자체가 성능 개선이 되게 했다.
- **기술**: 타입별 풀 컬렉션, 값 객체 풀링, Addressables 어댑터 패턴, in-flight 요청 병합(request coalescing)
- **정량**: 풀 컬렉션 **7종** (`GameObjectPoolManager` 716줄) + 이펙트·데미지 텍스트 전용 풀 별도 / `GameAssetBundleManager` 275줄로 전환 완료
- **근거**:
  - `Assets/Source/Logic/Manager/GameManager/GameObjectPoolManager.cs` (716줄)
  - `Assets/Source/Logic/Manager/GameManager/GameAssetBundleManager.cs` (275줄)
  - `Assets/Source/System/ObjectPool.cs` — 범용 풀 유틸
- **면접 포인트**: **"레거시 API 시그니처를 유지한 채 내부만 갈아끼운다"**는 마이그레이션 전략. 호출부 수정 없이 전환하면 리스크가 호출부 개수에 비례하지 않는다. 같은 전략을 Phase 3의 UI Toolkit 전환(F-29, 어댑터로 두 시스템 공존)에서 재사용했다. 한편 이 풀링 구조는 Phase 3에서 **정합성 버그**(F-47: 풀 반환 시 transform 미복구)로 되돌아왔는데, 이는 풀링의 고전적 함정(반환 시 상태 초기화 누락)을 실제로 겪고 고친 사례다.
- **슬라이드 자료**: 풀링 전후 GC 할당 그래프 — **캡처 필요** (F-46의 `GC.GetTotalAllocatedBytes()` 프로파일러 활용)


## 수록 파일

- `Assets/Source/Logic/Manager/GameManager/GameAssetBundleManager.cs`
- `Assets/Source/Logic/Manager/GameManager/GameObjectPoolManager.cs`
- `Assets/Source/System/ObjectPool.cs`
