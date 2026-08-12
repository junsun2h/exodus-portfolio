# F-21. Unity UGUI AI 편집 자동화

> 소스 발췌: `src/` — 28개 파일

**구간** Phase 2 (2026.01.16 ~ 01.17) | **포지션** 툴 | **AI** 협업

- **문제**: AI에게 UI 수정을 시키려면 현재 UI 상태를 알려줘야 하는데, Unity 프리팹은 **AI가 읽을 수 없는 형식**(YAML + GUID 참조)이다. 게다가 프리팹만 봐서는 (1) 런타임에 동적으로 채워지는 컨테이너 내용과 (2) 실제 데이터가 들어갔을 때의 모습을 알 수 없다.
- **해결**: **런타임 UI 구조를 JSON으로 캡처**하는 시스템을 만들었다.
  - **동적 컨테이너 감지** — 런타임에 아이템이 채워지는 컨테이너를 자동 식별
  - **실데이터 포함** — 실제 표시 중인 값을 함께 캡처
  - **스크린샷 동반** — 구조 JSON + 시각 정보를 함께 제공
  - AI가 수정한 결과는 **컨테이너 프리팹과 아이템 프리팹으로 분리 적용**한다. 동적 리스트는 컨테이너와 셀의 책임이 다르므로 한 덩어리로 적용하면 깨진다.
- **기술**: 런타임 UI 트리 순회 + JSON 직렬화, 동적 컨테이너 휴리스틱 감지, 스크린샷 연동, 프리팹 분리 적용
- **정량**: `Assets/Editor/UIAutomation/` **15파일 6,352줄**
- **근거**:
  - `Assets/Editor/UIAutomation/` — 15파일 6,352줄
  - `Docs/인프라/유니티UI자동화/명세서/26.01.17_Unity_UI_자동화_시스템_명세서.md`
  - `Docs/인프라/유니티UI자동화/명세서/26.01.17_Unity_UI_HTML_시안_시스템_명세서.md`
  - `Docs/인프라/유니티UI자동화/명세서/26.01.17_스크린샷_HTML_변환.md`
- **면접 포인트**: **"AI가 못 하는 일이 아니라, AI가 볼 수 없는 상태가 문제다."** 프리팹 포맷을 AI가 못 읽는다면 AI를 탓할 게 아니라 **읽을 수 있는 표현을 만들어 주면 된다.** 이 판단이 Phase 3의 UGUI→UXML 변환 자동화(F-32)와 headless 렌더 캡처(F-31)로 직결된다. AI 협업을 "프롬프트 잘 쓰기"가 아니라 **인터페이스 설계 문제**로 다룬 첫 사례.
- **슬라이드 자료**: UI 구조 JSON + 스크린샷 캡처 예시 — **캡처 필요**


## 수록 파일

- `Assets/Editor/UIAutomation/Core/UIEvaluationSummary.cs`
- `Assets/Editor/UIAutomation/Core/UIJsonSchema.cs`
- `Assets/Editor/UIAutomation/Core/UIYamlParser.cs`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/EquipmentHUD_20260401.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/EquipmentHUD_20260402.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/EquipmentScreen_20260402.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_EquipmentScreen_20260403.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_EquipmentScreen_20260404.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Lobby_Hud_20260602_lite.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Lobby_Hud_20260602_lite_draft.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Lobby_Hud_20260602_lite_senior.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_PopupEquipmentReforging_20260407_lite.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Screen_Player_20260415_lite.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Screen_Player_20260415_lite_draft.md`
- `Assets/Editor/UIAutomation/Data/EvaluationReports/TK_Screen_Player_20260415_lite_senior.md`
- `Assets/Editor/UIAutomation/Data/criteria.md`
- `Assets/Editor/UIAutomation/PrefabEditor/PrefabPhantomDirtyCleaner.cs`
- `Assets/Editor/UIAutomation/PrefabEditor/UIPrefabBackup.cs`
- `Assets/Editor/UIAutomation/PrefabEditor/UIPrefabEditorWindow.cs`
- `Assets/Editor/UIAutomation/PrefabEditor/UIPrefabModifier.cs`
- `Assets/Editor/UIAutomation/PrefabEditor/UIPrefabScreenshotCapture.cs`
- `Assets/Editor/UIAutomation/Preview/UIPreviewRenderer.cs`
- `Assets/Editor/UIAutomation/RuntimeCapture/DynamicContainerDetector.cs`
- `Assets/Editor/UIAutomation/RuntimeCapture/UIOverlayManager.cs`
- `Assets/Editor/UIAutomation/RuntimeCapture/UIOverlayWindow.cs`
- `Assets/Editor/UIAutomation/RuntimeCapture/UIRuntimeCaptureWindow.cs`
- `Assets/Editor/UIAutomation/RuntimeCapture/UIRuntimeParser.cs`
- `Assets/Editor/UIAutomation/UxmlDesignPreviewCapture.cs`
