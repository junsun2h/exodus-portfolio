# F-47. 전투 이펙트 풀링 정합성 복구

> 소스 발췌: `src/` — 2개 파일

**구간** Phase 3 (2026.07.31) | **포지션** 클라 | **AI** 협업

- **문제**: 전투 이펙트가 **2배속으로 재생**되는 현상과, 풀에서 꺼낸 이펙트가 엉뚱한 위치·크기로 나오는 현상이 있었다.
- **해결**: 두 가지 근본 원인을 찾았다.
  - **루트 파티클 2중 시뮬레이션** — 파티클 시스템이 자체 업데이트를 하는데 코드에서 `ParticleSystem.Simulate()`를 **수동으로 또 호출**하고 있었다. 두 번 진행하니 2배속.
  - **풀 반환 시 transform 미복구** — 풀링의 고전적 함정. 반환할 때 위치·회전·스케일을 초기화하지 않으면 다음에 꺼냈을 때 이전 상태가 남는다.
  - **수동 `Simulate()` 제거가 예상 밖의 이득을 낳았다.** 수동 호출을 하면 Unity가 파티클을 **Job System 배치 경로에서 빼버리고**, `cullingMode`도 무력화된다. 제거하자 **Job System 배치 처리로 복귀**하고 `cullingMode = Automatic`이 다시 살아났다 — 화면 밖 파티클이 다시 컬링된다. **버그 수정이 곧 성능 개선이었다.**
- **기술**: Unity ParticleSystem 내부 동작(Job System 배치, `cullingMode`), 오브젝트 풀 반환 시 상태 초기화, 이펙트 생명주기
- **정량**: 2배속 현상 해소 / Job System 배치 경로 복귀 / `cullingMode=Automatic` 복구
- **근거**:
  - `Docs/plans/완료/26.07.31_전투-이펙트-풀링-정합성-복구-및-최적화.md`
  - `Assets/Source/Logic/Manager/GameManager/GameObjectPoolManager.cs` (716줄) — 풀 반환 경로
- **면접 포인트**: **"엔진이 조용히 최적화를 끄고 있었다."** `ParticleSystem.Simulate()`를 수동 호출하면 Unity가 해당 파티클을 배치 처리에서 제외한다는 것은 문서에 크게 쓰여 있지 않다. 2배속 버그를 고치려고 수동 호출을 제거했더니 성능까지 좋아진 것은 **엔진 내부 동작을 이해했을 때만 설명 가능한 결과**다. 또한 F-08 풀링 설계가 Phase 3에서 정합성 함정으로 되돌아온 것 — 풀링은 만드는 것보다 **반환 시 상태 초기화를 빠뜨리지 않는 것**이 어렵다는 실전 교훈.
- **슬라이드 자료**: 수동 Simulate 제거 전후 (2배속 / Job System 경로) 다이어그램 — **다이어그램 필요**


## 수록 파일

- `Assets/Source/Logic/Effect/EffectData.cs`
- `Assets/Source/Logic/Manager/GameManager/GameEffectManager.cs`
