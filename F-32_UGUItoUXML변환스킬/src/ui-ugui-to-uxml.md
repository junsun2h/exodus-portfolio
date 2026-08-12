---
description: "UGUI 프리팹/코드를 분석하여 UXML/USS로 자동 변환"
model: "opus"
---

# UGUI → UXML 변환 스킬

UGUI 프리팹과 C# 코드를 분석하여 UI Toolkit UXML/USS를 생성한다.
**기능·요소는 UGUI 코드에서, 레이아웃·디자인은 제공된 레퍼런스(시안 이미지)에서** 가져온다.

## ⚠️ 핵심 원칙: 충실 재현 (Faithful Reproduction)

변환의 목표는 **자유 창작이 아니라 충실 재현**이다. "자유롭게 개선/재구성"은 결과를 학습데이터 평균(= 경직된 'AI스러움'/슬롭)으로 회귀시키고 사용자 개입을 폭증시킨다 — 슬롭은 모델의 *재량*에서 나오므로, 재량을 없애면 슬롭이 사라진다.

**R0 — 충실 재현 (자유 디자인 금지)**: 레이아웃·디자인은 *임의로 재구성하지 않는다*. 제공된 레퍼런스 시안에 맞춘다. 시안 없이 원본 UGUI 레이아웃을 그대로 살리는 경우(예: Lobby형)엔 원본 좌표에 맞춘다. 어느 쪽이든 "현대적으로 개선/재구성"은 금지.

**R1 — 기능·요소 = UGUI 코드 (변경 금지)**:
- 표시되어야 하는 모든 데이터/정보 항목, 버튼 액션·이벤트 흐름, 탭/서브탭 구조, 데이터 바인딩 로직
- 데이터 표시·포맷은 반드시 기존 유틸리티 함수 호출 (`GameUtility.GetTierText` 등) — 임의 구현 금지 (상세: 아래 "핵심 규칙: 데이터/표시 로직을 임의로 만들지 않는다")

**R2 — 레이아웃 = 레퍼런스 시안**:
- 여백/정렬/비율/그룹핑은 레퍼런스 시안의 구성을 그대로 따른다. UGUI 배치를 임의로 "개선"하지 않는다.
- 시안 이미지는 정확한 px를 인코딩하지 못하므로 1차 패스는 근사치다. **정밀화는 렌더-비교 검증으로 수렴**시킨다 — 모델이 자기 UXML 렌더를 레퍼런스와 같은 1920×1080로 겹쳐 비교해 어긋남(찌그러짐/겹침/누락/비율)을 줄인다.

**R3 — Port / Polish 분리**:
- 변환의 기본 산출물은 레퍼런스와의 **일치(Port)**다. 미적 개선(Polish)은 일치 검증 후 별도의 의도적 단계로만 — 한 공정에 섞지 않는다.

**판단 기준**: "레퍼런스에 이렇게 있는가?" → 그대로 재현. **제공된 레퍼런스를 충실히 옮긴다**는 마음가짐으로 접근한다 (원본 레이아웃을 임의 개선하지 않는다).

> 활성화:
> - `/ui-ugui-to-uxml {대상}` — Step 1~6 전체 실행
>   - 예: `/ui-ugui-to-uxml EquipmentHUD.prefab`
>   - 예: `/ui-ugui-to-uxml Equipment`
>   - 대상에서 Feature명 추출: `PXPopup_EquipmentHUD.prefab` → `Equipment`, `SkillHUD` → `Skill`
>
> **참고**: 생성 후 **Step 7 레이아웃 검증 루프(R5a)**가 모델 스스로 레이아웃 결함을 검증·수정하고, 이어 **Step 8 픽셀/미적 검증 루프(R5b)**가 design-preview를 headless 렌더해 색·그라데이션·질감을 검증·수정한다(사용자는 최종 승인만). R5b 인프라는 **구현 완료** — `Assets/Editor/UIAutomation/UxmlDesignPreviewCapture.cs`(방향 A: 화면 밖 EditorWindow 호스트, 단일 동기 호출).

## 사전 조건 (실행 전 반드시 Read)

아래 파일을 순서대로 읽고 규칙을 숙지한 후 작업을 시작한다:
1. `.claude/commands/ui-toolkit-dev.md` — 최우선 규칙 체크리스트 + 참조 가이드
2. `.claude/references/ui-toolkit-dev/rules.md` — MANDATORY 규칙 상세
3. `Assets/GameAssets/UI/UIToolkit/Theme/` — 테마 변수(`PXColors.uss`), 컴포넌트 클래스(`Components/`) 직접 Read
4. `.claude/references/ui-toolkit-dev/ui-terminology.md` — UI 용어 정의 (ETier/EGrade/Mod Tier)

## UI 타입 네이밍 규칙

| UI 타입 | C# 접두사 | 상속 | 설명 |
|---------|-----------|------|------|
| **Screen** | `TKScreen_` | UIToolkitPanel | 풀스크린 메뉴/패널 |
| **Popup** | `TKPopup_` | UIToolkitPopup | 오버레이 팝업/다이얼로그 |
| **Widget** | `TKWidget_` | — | 재사용 서브 컴포넌트 |

### 파일 네이밍

| 종류 | 패턴 | 예시 |
|------|------|------|
| C# | `TK{Type}_{Feature}.cs` | `TKScreen_Equipment.cs` |
| UXML | `TK_{Type}_{Feature}.uxml` | `TK_Screen_Equipment.uxml` |
| USS | `TK_{Type}_{Feature}.uss` | `TK_Screen_Equipment.uss` |
| 템플릿 | `TK_Slot_{ItemName}.uxml` | `TK_Slot_Equip.uxml` |
| PopupKey | C# 클래스명과 동일 | `"TKScreen_Equipment"` |
| CSS 접두사 | Feature 2-3글자 + Type 머리글자 + `-` | `es-` (Equipment Screen) |

### 폴더 구조

C#과 프리팹의 폴더명도 UI 타입에 맞게 사용한다. UGUI의 `Popup/` `Widget/` 폴더 관행을 따르지 않는다.

| UI 타입 | C# 경로 | 프리팹 경로 |
|---------|---------|-------------|
| **Screen** | `Assets/Source/Hud/Content/{Feature}/Screen/` | `Assets/GameAssets/UI/Prefab/Content/{Feature}/Screen/` |
| **Popup** | `Assets/Source/Hud/Content/{Feature}/Popup/` | `Assets/GameAssets/UI/Prefab/Content/{Feature}/Popup/` |
| **Widget** | `Assets/Source/Hud/Content/{Feature}/Widget/` | `Assets/GameAssets/UI/Prefab/Content/{Feature}/Widget/` |

## 실행 워크플로우

`/ui-ugui-to-uxml {Feature}` 실행 시 아래 순서를 따른다. (예: `/ui-ugui-to-uxml Skill`)

### Step 1: 탐색 — UGUI 전체 구조 파악

**Explore 에이전트 3개를 병렬 실행**하여 UGUI 코드를 완전히 파악한다.

**에이전트 A — 메인 팝업 + 위젯 C# 코드:**
```
읽기 대상:
- Assets/Source/Hud/Content/{Feature}/Popup/PXPopup_{Feature}HUD.cs
- Assets/Source/Hud/Content/{Feature}/Widget/PXWidget_*.cs
- Assets/Source/Hud/Common/Popup/Base/PXPopup_Common.cs

추출 항목:
- 모든 SerializeField UI 바인딩
- 모든 버튼 액션 (onClickEvent)
- 탭/서브탭 구조와 데이터 흐름
- 동적으로 생성되는 요소 (Instantiate, AddListener)
```

**에이전트 B — 기존 TK 코드 + 인프라 확인:**
```
읽기 대상:
- Assets/Source/Hud/Content/{Feature}/Popup/TKPopup_{Feature}*.cs (있으면)
- Assets/Source/Hud/Base/UIToolkit/UIToolkitBridge.cs
- Assets/Source/Hud/PXUI/UIToolkit/UIToolkitExtensions.cs
- Assets/GameAssets/UI/UIToolkit/Theme/Components/*.uss (목록)

추출 항목:
- TK C# 존재 여부 및 Q() name 계약
- 사용 가능한 USS 컴포넌트 클래스 목록
- 확장 메서드 (SetSprite, SetTextKey, SetCountText 등)
```

**에이전트 C — 공용 위젯 + 데이터 표시 로직:**
```
읽기 대상:
- Assets/Source/Hud/Common/Widget/PXWidget_Common{ItemType}.cs (장비면 CommonEquipment)
- Assets/Source/Utility/GameUtility.cs (GetTierText, ConvertToAlphabetValue 등)
- Assets/Source/Shared/ColorCode.cs (GetTierColor 등)
- Assets/Source/Logic/Manager/GameManager/GameBattleUtilityManager.cs (아이콘 로딩)

추출 항목:
- 개별 슬롯의 시각 요소 (아이콘, 등급, 별, 강화, 진행바 등)
- 데이터 표시에 사용하는 유틸리티 함수 목록
- 아이콘/스프라이트 로딩 방식 (번들명, 에셋명 규칙)
- 사용되는 이미지 에셋 경로 (뒤로가기, 닫기, 별 아이콘 등)
```

### Step 2: 결정 — 네이밍 및 레이아웃 확정

탐색 결과를 바탕으로 결정:

1. **UI 타입 판별**: 풀스크린 → Screen, 오버레이 → Popup
2. **클래스명**: `TK{Type}_{Feature}` (예: `TKScreen_Skill`)
3. **CSS 접두사**: Feature 2-3글자 + Type 머리글자 (예: `ss-` = Skill Screen)
4. **레이아웃 패턴 선택**: Golden Reference에서 가장 유사한 화면 선택
   - 해당 스크린샷 + UXML을 이미 읽었으므로 구조 파악 완료
   - 유사 패턴 자동 적용 (기존 패턴과 크게 다를 때만 사용자에게 확인)
5. **TK C#이 이미 있으면**: 리네임만 (eh- → ss- 등). 없으면 새로 작성
6. **템플릿 UXML 필요 여부**: 반복 요소(슬롯, 카드 등)가 있으면 분리

### Step 3: C# 준비

**TK C#이 이미 존재하는 경우:**
1. 파일 리네임 (git mv)
2. 클래스명, PopupKey, 디버그 로그 일괄 치환
3. CSS 접두사 일괄 치환 (eh- → {새접두사}-)
4. UGUI에서 누락된 기능 추가 (서브탭, 아이콘 로딩 등)
5. 데이터 표시 로직을 **기존 유틸리티 함수 호출**로 교체 (임의 구현 금지)

**TK C#이 없는 경우:**
1. UGUI C# 코드를 기반으로 새 TK C# 작성
2. `UIToolkitPanel` (Screen) 또는 `UIToolkitPopup` (Popup) 상속
3. OnBindUI()에서 Q() name 바인딩
4. 모든 데이터 표시는 UGUI 코드의 유틸리티 함수를 그대로 호출

### Step 4: UXML/USS 생성

**4-1. 템플릿 UXML** (반복 요소가 있는 경우 먼저):
- 파일: `Assets/GameAssets/UI/UIToolkit/Content/{Feature}/TK_Slot_{ItemName}.uxml`
- `PXTheme.tss` + 커스텀 USS 참조 포함 (UI Builder 단독 열기 대응)
- 루트 요소에 name 필수 (CloneTree 후 Q 대상)
- 디폴트 이미지 설정 (실제 사용 이미지 경로를 UGUI에서 확인)

**4-2. 메인 UXML**:
- 파일: `Assets/GameAssets/UI/UIToolkit/Content/{Feature}/TK_{Type}_{Feature}.uxml`
- 선택한 Golden Reference 화면의 패널 비율, flex 구조, top-bar 패턴을 기준으로 생성
- 디자인(색상, 카드 내부, 효과)·레이아웃은 **레퍼런스 시안에 충실하게 재현** (자유 구성 금지 — R0/R2). 시안에 없는 부분만 기존 컴포넌트 기본값 사용
- 레터박스 래퍼 필수: `letterbox-bg` > `letterbox-content`
- 버튼 이미지: `btn-back` → `Btn_Back.png`, `btn-close` → `Close Icon.png` (inline style)
- C# Q() name 계약과 1:1 매칭

**4-3. 커스텀 USS**:
- 파일: `Assets/GameAssets/UI/UIToolkit/Content/{Feature}/TK_{Type}_{Feature}.uss`
- 기준 해상도 **1920x1080** (PanelSettings `ScaleWithScreenSize` + **Expand**). `.{prefix}-panel-root` 에 `width/height: 100%` 금지 — 테마 `.letterbox-content` 의 1920x1080 고정을 덮으면 기기 비율에 따라 px 기준이 흔들린다 (배치 속성만 둘 것)
- 기존 컴포넌트 USS 재사용, 커스텀만 `{접두사}-` 클래스로 정의
- 스크롤 영역에 `overflow: hidden` (상위 패널 침범 방지)
- 슬롯 크기는 고정 px (percentage padding-top 트릭은 UI Toolkit에서 동작 안 함)

### Step 5: 프리팹 + Addressables + 코드 참조

**5-1. MCP로 프리팹 생성** (Phase 6-1 템플릿 참조)
- 경로: `Assets/GameAssets/UI/Prefab/Content/{Feature}/{Type}/`
- 컴포넌트: RectTransform + Canvas + GraphicRaycaster + UIDocument + TK Script
- SerializeField에 VisualTreeAsset 템플릿 할당

**5-2. Addressables 등록** (Phase 6-2 템플릿 참조)
- 주소: `widget_popup/{classname_소문자}`
- `BuildPlayerContent()` 호출 금지

**5-3. 코드 참조 변경**
- Grep으로 기존 PopupKey (`"PXPopup_{Feature}HUD"`) 검색
- 모든 `OpenWidget()` / `CloseWidget()` 호출을 새 PopupKey로 변경

### Step 6: 정적 검증

코드 레벨 검증 (PlayMode 불필요):
- C# Q() name ↔ UXML name 1:1 매칭
- C# AddToClassList() ↔ USS class 정의
- 프리팹 UIDocument에 PanelSettings + UXML 할당됨
- Addressables 주소 형식 `widget_popup/...` 정확
- 데이터 표시 로직이 기존 유틸리티 함수를 호출하는지 (임의 하드코딩 없는지)

### Step 7: 레이아웃 검증 루프 (R5a) — 사용자 보고 전 모델이 자동 반복

목적: **사용자를 매 반복에서 제거**. 모델이 자기 UXML 레이아웃을 스스로 검증·수정한 뒤 **최종만 보고**한다. (사용자는 반복 엔진이 아니라 최종 승인자.)

**(a) 자체 결함 lint — 레퍼런스 불필요, 신뢰도 확실**
Edit 모드에서 UXML 로드 → 레이아웃 계산 → 모든 named element의 **계산된 rect**를 읽어 검출:
- **겹침** (rect 교차) / **화면 이탈** (1920×1080 또는 부모 컨테이너 초과) / **0크기·찌그러짐** (w 또는 h ≈ 0) / **누락** (C# `Q("name")` 바인딩엔 있는데 레이아웃에 없음)
> 이 단계는 시안조차 필요 없다 — 숫자(rect)만으로 판정한다. 추정 0. PoC로 Edit 모드 layout-calc 정확성 확인됨(픽셀 렌더 없이 좌표 정확).

**(b) 박스 오버레이 정렬 — 레퍼런스 시안 있을 때**
계산된 rect들을 라벨 박스로 그려 **시안 이미지 위에 겹친다** → vision으로 **정렬 판단**. 핵심: *시안의 좌표를 추출하는 게 아니라*, 내 박스가 시안 요소 위에 얹혔는지/어느 쪽으로 어긋났는지를 본다(어려운 방향 픽셀→숫자를 피한다). 밀집 구역(그리드 등)은 양쪽 공통 텍스트(헤더·수치·아이템명)를 앵커로 고정.
> 신뢰도: 시안이 래스터라 **근사** — 거친 오차(찌그러짐/퍼짐/위치 밀림) 교정 위주. 픽셀 정밀 일치는 시안 정밀도가 천장이라 불가.

**반복**: (a)(b)에서 검출된 어긋남 수정 → 다시 (a)(b) → 수렴까지 N회 → **최종만 사용자에게**.

> **R5a = 숫자(rect) 기반 레이아웃 검증.** R5a 수렴 후 픽셀/미적은 아래 Step 8(R5b)에서 다룬다.

### Step 8: 픽셀/미적 검증 루프 (R5b) — R5a 수렴 후 모델이 자동 반복

목적: R5a로 **레이아웃(rect)**이 수렴한 뒤, 색·그라데이션·질감·발광처럼 **숫자(rect)로 안 잡히는 미적 요소**를 실제 픽셀로 비교·수정한다. (역시 사용자는 반복 엔진이 아니라 최종 승인자.)

> **전제: R5b는 R5a 수렴 후에만 의미.** 레이아웃이 어긋난 상태의 미적 비교는 무의미하다. 순서는 **R5a(숫자/rect) → R5b(픽셀/미적)**. 두 단계의 입력이 다르다 — R5a는 시안 없이도 가능(자체 lint), R5b는 비교할 시안 픽셀이 필요.

**인프라**: `Assets/Editor/UIAutomation/UxmlDesignPreviewCapture.cs` — 화면 밖 임시 EditorWindow 호스트(방향 A)로 UXML의 `.design-preview`를 **headless PNG 렌더**. (`UIBuilderDesignPreviewToggle`의 화면 스크래핑[캡처]와 달리 UI Builder 창을 열 필요가 없어 MCP 무인 호출 가능. Unity 6000.3.14 검증.)
- 시그니처: `public static bool Capture(string uxmlPath, string outPng = null, int width = 1920, int height = 1080)`
- **단일 동기 호출** — `delayCall` 2-스텝/폴링 불필요(`UpdateForRepaint`가 레이아웃을 동기 처리). 반환 시점에 PNG가 디스크에 존재.
- design-preview(더미 데이터) **표시 상태 그대로** 캡처(StripDesignPreview 호출 안 함). **PanelSettings.themeStyleSheet(공통 클래스 USS — letterbox/top-bar 등)를 적용 + Linear RT**로 UI Builder 직접 캡처와 색·레이아웃이 1:1 일치(이 두 가지가 빠지면 색 바램·레이아웃 붕괴).

**흐름** (사용자 개입 0~1회):
1. (R5a 수렴 후) `execute_code`로 캡처 — 단일 호출, 반환 시 PNG 존재:
   ```csharp
   bool ok = PX.Editor.UIAutomation.UxmlDesignPreviewCapture.Capture(
       "<uxmlPath>",
       "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/UXML/<UXML이름>.png");
   return ok;
   ```
2. 그 PNG를 **Read(vision)** 로 읽어 시안과 **색·그라데이션·질감·발광·미적**을 비교.
3. 어긋남 수정(USS 변수 색 / 그라데이션 stop / `TKGradient`·`TKBoxShadow` 파라미터 등) → 다시 1~2 → 수렴까지 N회 → **최종만 사용자에게**.

> **R5a = 숫자(rect) 기반 레이아웃, R5b = 픽셀 기반 미적.** 충실도 검증 완료: 폰트/그라데이션/커스텀요소(`TKGradient`·`TKBoxShadow`)/색상이 기존 수동 [캡처] 대비 동등 이상(풀 1920×1080, 에디터 크롬 없음). 호출 패턴 상세는 `UxmlDesignPreviewCapture` 클래스 주석 참조.

---

## 상세 규칙

### Phase 1: Source Analysis

**TK C# 파일이 있는 경우** (우선):
```
Assets/Source/Hud/Content/{Feature}/Popup/TKScreen_{Feature}.cs
또는 TKPopup_{Feature}.cs
```
- 모든 `Q<T>("name")`, `Q("name")` 호출 추출 → **name 계약**
- 모든 `Q<T>(className: "class")` 호출 추출 → **class 계약**
- 모든 `AddToClassList("class")` / `RemoveFromClassList("class")` → **동적 CSS 클래스**
- `_xxxTemplate.CloneTree()` → **템플릿 UXML 필요**

**UGUI C# 파일** (보조 — 누락 요소 확인):
```
Assets/Source/Hud/Content/{Feature}/Popup/PXPopup_{Feature}HUD.cs
Assets/Source/Hud/Content/{Feature}/Widget/PXWidget_*.cs
Assets/Source/Hud/Common/Widget/PXWidget_Common*.cs
Assets/Source/Utility/GameUtility.cs
```
- `[SerializeField]` 바인딩 → 시각 요소 인벤토리
- 동적 Instantiate → 동적 요소 목록
- 탭/서브탭 구조
- 버튼 액션
- **데이터 표시 로직** → 티어 텍스트, 등급 별, 색상, 포맷 등
- **공용 유틸리티 함수** → `GameUtility`, `ColorCode`, `GameBattleUtilityManager` 등에서 호출하는 함수

### ⚠️ 핵심 규칙: 데이터/표시 로직을 임의로 만들지 않는다

UGUI에 이미 **모든 표시 로직이 구현되어 있다**. TK 코드에서 새로 정의하지 말고 반드시 기존 코드를 찾아서 재사용한다.

**금지**: 티어 텍스트, 등급 표시, 색상 코드, 아이콘 로딩, 수치 포맷 등을 임의로 하드코딩
**필수**: UGUI 위젯/유틸리티에서 사용하는 함수를 찾아서 동일하게 호출

| 항목 | 찾아야 할 위치 | 예시 |
|------|--------------|------|
| 티어 텍스트 (C/B/A/S/SS/SSS) | `GameUtility.GetTierText()` | 직접 switch문 작성 금지 |
| 티어 색상 | `ColorCode.GetTierColor()` | RGB 값 하드코딩 금지 |
| 장비 아이콘 | `GameBattleUtilityManager.GetEquipSprite()` | 경로 직접 조합 금지 |
| 등급 별 수 | UGUI 위젯의 gradeCount 계산 로직 참조 | |
| 수치 포맷 | `GameUtility.ConvertToAlphabetValue()` | |
| 비율/숫자 표시 헬퍼 | `GameUtility.FormatRate/FormatNumber/FormatCount`, `ModValue.GetValueTextForUI()` | UGUI의 `rate * 100`, `$"{x:F1}%"`, `x.ToString("0.##")+"%"` 패턴은 **반드시 헬퍼로 교체**. 중간 변수 저장 스케일도 0~1로 정정 (`>= 80f` → `>= 0.8f` 동반 수정). 자세한 규약은 `ui-toolkit-dev/skill.md` 규칙 #11 |
| 스프라이트 이미지 경로 | 기존 UGUI 프리팹/코드에서 사용하는 이미지 확인 | 별 아이콘, 뒤로가기, 닫기 등 |

**검증 방법**: TK 코드에 switch문이나 if-else로 데이터 변환 로직을 새로 작성했다면, UGUI에 동일한 로직이 이미 있는지 Grep으로 확인한다. 있으면 그 함수를 호출하도록 교체한다.

### Phase 2: Element Inventory

테이블 형식으로 정리:

| Element | Type | name | Classes | Dynamic | Template |
|---------|------|------|---------|---------|----------|
| 닫기 버튼 | Button | btn-close | btn-close | No | No |
| 장비 그리드 | ScrollView | scroll-equipment-grid | scroll-view-dark | No | No |
| 슬롯 컨테이너 | VisualElement | grid-container | grid-container | Yes (children) | TK_Slot_Equip |

### Phase 3: Component Mapping

기존 USS 컴포넌트에 매핑:

| UGUI 요소 | USS 컴포넌트 | 클래스 |
|-----------|-------------|--------|
| PXButton (Nav) | PXButton.uss | `.btn-cancel`, `.btn-positive` |
| PXButton (Action) | PXButton.uss | `.btn-primary`, `.btn-summon` |
| PXTabGroup | PXTabBar.uss | `.tab-bar`, `.tab-item`, `.tab-item-active` |
| PXProgress | PXProgress.uss | `.tk-progress`, `.tk-progress__fill` |
| PXListView | PXGridCard.uss | `.grid-container`, `.grid-card` |
| 상단바 | PXTopBar.uss | `.top-bar`, `.btn-close`, `.btn-back` |
| 재화 표시 | PXCurrencySlot.uss | `.currency-slot`, `.currency-slot-value` |
| 카드 패널 | PXCard.uss | `.card`, `.card-gradient-layer` |
| 레드닷 | PXMisc.uss | `.tk-reddot` |
| 티어 배지 | PXTierBadge.uss | `.tier-badge`, `.tier-{tier}` |
| 스크롤뷰 | PXMisc.uss | `.scroll-view-dark` |
| 배경 그라데이션 | PXGradient.uss | `.gradient-v`/`.gradient-h` + `.gradient-cyan/gold/dark/epic/red` |
| 셰도우/글로우 | PXShadow.uss | `.shadow-layer` + `.shadow-glow/glow-cyan/glow-gold/glow-epic/glow-legendary/glow-mythic` |
| 등급 글로우 (동적) | TKBoxShadow | `<PX.TKBoxShadow shadow-color="" />` |
| 텍스트 그라데이션 | TKGradientLabel | `<PX.TKGradientLabel text="" color-top="" color-bottom="" />` (타이틀 전용) |
| 임의 그라데이션 | TKGradient | `<PX.TKGradient color-top="" color-bottom="" />` |
| USS 애니메이션 | PXAnimation.uss | `.anim-pulse/slide-up/fade/highlight/bounce` + `PlayAnimation()` |
| DOTween 트윈 | TKTweenExtensions | `element.DOScale()`, `DOFade()`, `DOPopupEnter()` |

### Phase 4: UXML Generation

규칙:
1. **모든 요소에 name 속성** — UI Builder에서 구분 가능해야 함
2. name은 C# Q() 호출과 **정확히 일치**
3. 동적 콘텐츠 영역은 **빈 컨테이너**로 (C#에서 CloneTree/Add)
4. 템플릿 UXML은 별도 파일로 분리
5. 정적 텍스트는 플레이스홀더 (C#에서 바인딩)
6. **inline style에서 `var()` 사용 금지** (UI Builder NullReferenceException)
7. **16:9 레터박스 필수** — UXML 루트에 `letterbox-bg` > `letterbox-content` 래퍼 적용
8. **기준 해상도 1920x1080** — 모든 px 값은 이 해상도 기준 (PanelSettings `ScaleWithScreenSize` + **Expand**. `Shrink` 금지)
9. **`panel-root` 에 `width/height: 100%` 금지** — 어느 기기에서든 1920x1080 좌표계를 유지해야 시안 px 가 그대로 재현된다
10. **화면(Screen) 배경은 `letterbox-bg` 에** — `panel-root` 에 주면 좌우에 검은 바가 남는다 (팝업은 딤이 덮으므로 해당 없음)

### 16:9 레터박스 UXML 템플릿

모든 Screen/Popup의 UXML 루트 구조:

**Screen** — 배경을 `letterbox-bg` 에 두어 좌우 여백까지 덮는다:
```xml
<ui:VisualElement name="letterbox-bg" class="letterbox-bg screen-bg-default screen-bg-fill" style="background-image: url('...');">
<ui:VisualElement name="panel-root" class="letterbox-content {prefix}-panel-root">
    <!-- 실제 콘텐츠 -->
</ui:VisualElement>
</ui:VisualElement>
```

**Popup** — 딤(`popup-bg-dim`)이 화면 전체를 덮으므로 `letterbox-bg` 는 그대로:
```xml
<ui:VisualElement name="letterbox-bg" class="letterbox-bg">
<ui:VisualElement name="panel-root" class="letterbox-content {prefix}-panel-root">
    <!-- 실제 콘텐츠 -->
</ui:VisualElement>
</ui:VisualElement>
```

- `letterbox-bg`: absolute-fill, center 정렬. Screen 이면 여기에 배경 (`.letterbox-bg.screen-bg-fill` 이 `scale-and-crop` 이라 비율 왜곡 없이 화면 끝까지 덮음)
- `letterbox-content`: 1920x1080 고정 크기. **`width/height: 100%` 로 덮지 말 것**
- PanelSettings: `ScaleWithScreenSize` + **`Expand`** — `Shrink` 는 20:9 같은 긴 화면에서 패널을 1920x864 로 crop 해 1080px 콘텐츠의 상·하단을 잘라낸다
- 비율별: **20:9**(S21 2400x1080) → 좌우 240px 여백 / **16:9** → 정확히 일치 / **4:3** → 상하 여백. 배경을 `letterbox-bg` 에 두면 여백이 배경으로 채워져 검은 바가 보이지 않는다

### UXML element 네이밍 규칙

| 카테고리 | 패턴 | 예시 |
|----------|------|------|
| 컨테이너/섹션 | `{semantic}-section` | `top-bar-section`, `main-content-section` |
| 패널 | `{side}-panel` | `left-panel`, `right-panel` |
| 버튼 | `btn-{action}` | `btn-close`, `btn-equip`, `btn-reinforce` |
| 레이블 | `label-{content}` | `label-equip-name`, `label-tier` |
| 아이콘/이미지 | `icon-{name}` 또는 `{item}-icon` | `equip-icon`, `icon-diamond` |
| 탭 | `tab-{category}` / `sub-tab-{id}` | `tab-weapon`, `sub-tab-all` |
| 그리드/리스트 | `grid-{name}` / `scroll-{name}` | `grid-container`, `scroll-equipment-grid` |
| 행/항목 | `row-{name}` / `{name}-row` | `star-row`, `card-top-row` |

### Phase 5: USS Generation

규칙:
1. CSS 접두사: `{2-3글자}-` (Feature + Type 약어)
2. 기존 컴포넌트 USS 재사용 (중복 정의 금지)
3. `var(--color-*)` 참조 (PXColors.uss)
4. 커스텀 클래스만 새 USS 파일에
5. **`-unity-font-style: bold` 금지** — NotoSansKR-Bold 폰트 자체가 Bold이므로 이중 볼드 방지
6. **주요 컨테이너 `border-radius` 금지** — Screen/Popup 전체 프레임, Panel 등은 직각 유지. 소형 요소(뱃지, 프로그레스 바, 아이콘 등)에는 허용
7. **텍스트 그림자**: `text-shadow: <offset-x> <offset-y> <blur> <color>` 사용. 외곽선은 `-unity-text-outline-width` + `-unity-text-outline-color` 사용

### Phase 6: Prefab 생성 및 Addressables 등록

MCP execute_code로 프리팹을 생성하고 Addressables에 등록한다.

#### 6-1. 프리팹 생성 (MCP)

프리팹 경로 규칙: `Assets/GameAssets/UI/Prefab/Content/{Feature}/{Type}/{ClassName}.prefab`

프리팹에 필요한 컴포넌트:
1. **RectTransform** — anchor center, sizeDelta 100x100 (UIPopup 기반)
2. **Canvas** — RenderMode: ScreenSpaceCamera
3. **GraphicRaycaster** — 입력 처리용
4. **UIDocument** — PanelSettings + UXML VisualTreeAsset 할당
5. **TK C# Script** — `_equipSlotTemplate` 등 SerializeField 할당

```csharp
// MCP 프리팹 생성 템플릿
var go = new UnityEngine.GameObject("{ClassName}");
var rt = go.AddComponent<UnityEngine.RectTransform>();
rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
rt.sizeDelta = new UnityEngine.Vector2(100f, 100f);
rt.anchoredPosition = UnityEngine.Vector2.zero;

var canvas = go.AddComponent<UnityEngine.Canvas>();
canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceCamera;

go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

var uiDoc = go.AddComponent<UnityEngine.UIElements.UIDocument>();
uiDoc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(
    "Assets/GameAssets/UI/UIToolkit/PXPanelSettings.asset");
uiDoc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(
    "Assets/GameAssets/UI/UIToolkit/Content/{Feature}/TK_{Type}_{Feature}.uxml");

go.AddComponent<PX.{ClassName}>();

// SerializeField 할당 (템플릿 등)
var so = new UnityEditor.SerializedObject(go.GetComponent<PX.{ClassName}>());
var prop = so.FindProperty("_xxxTemplate");
if (prop != null) {
    prop.objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(
        "Assets/GameAssets/UI/UIToolkit/Content/{Feature}/TK_{TemplateName}.uxml");
    so.ApplyModifiedPropertiesWithoutUndo();
}

string prefabPath = "Assets/GameAssets/UI/Prefab/Content/{Feature}/{Type}/{ClassName}.prefab";
UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
UnityEngine.Object.DestroyImmediate(go);
UnityEditor.AssetDatabase.SaveAssets();
```

#### 6-2. Addressables 등록 (MCP)

**주소 형식**: `widget_popup/{popupkey_소문자}` (예: `widget_popup/tkscreen_equipment`)

이 형식은 `GameAssetBundleManager.GetAddressableAddress()`가 `"widget_popup"` + PopupKey를 소문자 결합하는 규칙에 맞춤.

```csharp
// MCP Addressables 등록 템플릿
string prefabPath = "Assets/GameAssets/UI/Prefab/Content/{Feature}/{Type}/{ClassName}.prefab";
string guid = UnityEditor.AssetDatabase.AssetPathToGUID(prefabPath);
var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;

// widget_popup 그룹 찾기
UnityEditor.AddressableAssets.Settings.AddressableAssetGroup targetGroup = null;
foreach (var group in settings.groups) {
    if (group.Name == "widget_popup") { targetGroup = group; break; }
}

var entry = settings.CreateOrMoveEntry(guid, targetGroup, false, false);
entry.address = "widget_popup/{classname_소문자}";

UnityEditor.EditorUtility.SetDirty(settings);
UnityEditor.AssetDatabase.SaveAssets();
```

**주의**: `BuildPlayerContent()`는 호출하지 않는다 (서버 타임아웃 위험). Editor에서 Play 시 자동 빌드됨.

#### 6-3. 코드 참조 변경

UGUI PopupKey를 사용하는 모든 `OpenWidget()`, `CloseWidget()` 호출을 새 PopupKey로 변경:

```
Grep 검색: "PXPopup_{Feature}HUD" (또는 기존 PopupKey)
변경 대상:
- 직접 진입점 (SubMenu, Reddot 등) → 새 PopupKey
- 연관 화면에서의 복귀 (Book → HUD 등) → 새 PopupKey
- UGUI 내부 참조 (같은 UGUI 위젯 간) → TK 전환 후 사용 안 되므로 선택적
```

### Phase 7: Validation

체크리스트:
- [ ] C#의 모든 `Q("name")` → UXML에 `name=""` 존재
- [ ] C#의 모든 `Q<T>(className: "class")` → USS/UXML에 class 정의
- [ ] C#의 모든 `AddToClassList("class")` → USS에 class 정의
- [ ] UXML에 inline `var()` 없음
- [ ] 모든 VisualElement에 의미 있는 name 또는 class 부여 (UI Builder 식별용)
- [ ] 템플릿 UXML의 root에 name 존재 (CloneTree 후 Q 대상)
- [ ] 프리팹: UIDocument에 PanelSettings + UXML 할당됨
- [ ] 프리팹: SerializeField (VisualTreeAsset 템플릿 등) 할당됨
- [ ] Addressables: `widget_popup/{popupkey_소문자}` 주소로 등록됨
- [ ] 코드: 모든 `OpenWidget("기존키")` → `OpenWidget("새키")` 변경됨

## 참조: Equipment Screen 변환 사례

### 파일 구조
```
Assets/GameAssets/UI/UIToolkit/Content/Equipment/
├── TK_Screen_Equipment.uxml   (메인 레이아웃)
├── TK_Screen_Equipment.uss    (es- 커스텀 스타일)
└── TK_Slot_Equip.uxml         (슬롯 템플릿)

Assets/Source/Hud/Content/Equipment/Screen/
└── TKScreen_Equipment.cs      (C# 로직)

Assets/GameAssets/UI/Prefab/Content/Equipment/Screen/
└── TKScreen_Equipment.prefab  (UIDocument + Canvas + Script)
```

### CSS 접두사: `es-` (Equipment Screen)
### PopupKey: `"TKScreen_Equipment"`
### Addressables 주소: `widget_popup/tkscreen_equipment`
### 변경된 코드 참조:
- `PXWidget_SubMenu.cs` → `OpenWidget("TKScreen_Equipment")`
- `GameReddotManager.cs` → `OpenWidget("TKScreen_Equipment")`
- `PXWidget_EquipmentEquipBook.cs` → `OpenWidget("TKScreen_Equipment")`
