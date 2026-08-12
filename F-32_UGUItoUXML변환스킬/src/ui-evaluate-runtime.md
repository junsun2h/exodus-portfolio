---
description: "UI/UX 품질 평가 (Runtime/Prefab 모드)"
model: "opus"
---

# UI Evaluate Runtime Skill

UGUI 프리팹 또는 Play Mode 화면의 품질을 **스크린샷(시각) + 구조 데이터(정량)** 두 채널로 평가하는 스킬.
Generator-Evaluator 분리 원칙에 따라, UI를 만든 에이전트와 평가 에이전트를 분리하여 사용.

> **참고**: UI Builder에서 더미 데이터로 작업 중인 UXML을 평가할 때는 **`ui-evaluate-lite`** 스킬을 사용하세요. 이쪽은 Play Mode 진입 또는 UGUI Prefab 캡처가 필요한 무거운 평가용입니다.

## 사용법

```
/ui-evaluate <프리팹경로 또는 UXML경로>
```

## 평가 기준

**참조 문서**: `Assets/Editor/UIAutomation/Data/criteria.md`

5가지 항목 (5점 척도, 가중 평균):
1. **정보 계층** (×3) — 핵심 수치가 즉시 보이는가
2. **터치 적합성** (×3) — 44px 이상, 간격, 엄지 도달 영역
3. **시각적 일관성** (×2) — rules.json 준수, 템플릿 사용, 폰트 계층
4. **인지 부하** (×3) — 한 화면 정보량, 행동 버튼 수, 깊이
5. **피드백 명확성** (×2) — disabled 상태, 레드닷, 상태 전환

합격: 가중 총점 3.5/5.0 이상

---

## 두 가지 모드

| 모드 | 언제 | 장점 | 한계 |
|------|------|------|------|
| **Prefab** | 에디터에서 바로 | 빠름, Play Mode 불필요 | Grid/List 비어있음 (DynamicContainers로 보완) |
| **Runtime** | Play Mode 중 | 실제 유저 시점, 데이터 채워진 상태 | Play Mode 진입 필요 |

> Grid, ListView 등 동적 콘텐츠가 핵심인 UI는 Runtime 모드 사용 권장.
> 레이아웃/구조 중심 평가는 Prefab 모드로 충분.

---

## Prefab 모드 워크플로우

### Step 1: 데이터 수집

**스크린샷 + 요약 동시 생성** (MCP execute_code 한 번 호출):

`execute_code(action="execute", compiler="roslyn")` MCP 도구로 아래 C# 코드를 실행:
```csharp
return PX.UIAutomation.UIEvaluationSummary.CaptureAndSummarize("PREFAB_PATH", "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate");
```

**요약만 필요할 때**:

`execute_code(action="execute", compiler="roslyn")` MCP 도구로 아래 C# 코드를 실행:
```csharp
return PX.UIAutomation.UIEvaluationSummary.Generate("PREFAB_PATH");
```

- 스크린샷: `Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/` 에 PNG 저장
- 요약 JSON: execute_code return으로 반환 (~100-200줄)
- `DynamicContainers` 필드: 빈 Grid/List 영역에 어떤 아이템이 채워지는지 알려줌

---

## Runtime 모드 워크플로우

### 전제조건: Play Mode 진입 + 평가할 팝업 열기

**스크린샷 + 요약 동시 생성**:

`execute_code(action="execute", compiler="roslyn")` MCP 도구로 아래 C# 코드를 실행:
```csharp
return PX.UIAutomation.UIEvaluationSummary.CaptureRuntimeAndSummarize("POPUP_NAME", "Assets/Editor/UIAutomation/Data/Screenshot/Evaluate");
```

**요약만**:

`execute_code(action="execute", compiler="roslyn")` MCP 도구로 아래 C# 코드를 실행:
```csharp
return PX.UIAutomation.UIEvaluationSummary.GenerateRuntime("POPUP_NAME");
```

- `POPUP_NAME`: 팝업 이름 (부분 매칭, 예: "EquipmentHUD")
- 런타임 스크린샷은 ScreenCapture.CaptureScreenshot으로 실제 Game View 캡처
- Grid/List에 실제 데이터가 채워진 상태 확인 가능

---

## 공통 Step 2: 평가 (Evaluator Agent)

수집된 데이터를 **별도 Agent**에게 전달하여 평가:

```
Agent(subagent_type="general-purpose") 에게 다음을 전달:
1. 스크린샷 PNG 경로 (Read 도구로 이미지 확인)
2. 요약 JSON 데이터
3. 평가 기준 (Assets/Editor/UIAutomation/Data/criteria.md)
```

Agent 프롬프트 템플릿:
```
다음 UI를 평가해줘.

## 데이터
- 스크린샷: [PNG 경로] (Read 도구로 확인)
- 구조 요약: [JSON 데이터 붙여넣기]

## 평가 기준
Assets/Editor/UIAutomation/Data/criteria.md 파일을 읽고 5가지 기준으로 평가해줘.

## 출력 형식
각 기준별:
- 점수 (1-5)
- 근거 (구체적 요소 지적)
- 개선 제안 (있을 경우)

마지막에 가중 총점과 합격 여부 판정.

## 보고서 저장
평가 완료 후, 결과를 아래 경로에 마크다운 보고서로 저장해줘:
- 경로: Assets/Editor/UIAutomation/Data/EvaluationReports/{팝업이름}_{YYYYMMDD}.md
- Step 3의 보고서 템플릿 형식을 따를 것
```

### Step 3: 보고서 저장

Evaluator Agent의 결과를 마크다운 보고서로 저장:

- **경로**: `Assets/Editor/UIAutomation/Data/EvaluationReports/{팝업이름}_{YYYYMMDD}.md`
- 동일 팝업의 같은 날짜 보고서가 이미 있으면 덮어쓰기

보고서 템플릿:
```markdown
# {팝업이름} UI/UX 평가 보고서

- **날짜**: {YYYY-MM-DD}
- **모드**: {Prefab | Runtime}
- **가중 총점**: {점수}/5.0 — **{판정}**

---

## 점수 요약

| 항목 | 점수 | 가중치 | 가중점수 |
|------|------|--------|----------|
| 정보 계층 | {n} | x3 | {n*3} |
| 터치 적합성 | {n} | x3 | {n*3} |
| 시각적 일관성 | {n} | x2 | {n*2} |
| 인지 부하 | {n} | x3 | {n*3} |
| 피드백 명확성 | {n} | x2 | {n*2} |
| **합계** | | /13 | **{합계}/65 = {총점}** |

---

## 1. 정보 계층 — {n}/5
{근거 + 개선 제안}

## 2. 터치 적합성 — {n}/5
{근거 + 개선 제안}

## 3. 시각적 일관성 — {n}/5
{근거 + 개선 제안}

## 4. 인지 부하 — {n}/5
{근거 + 개선 제안}

## 5. 피드백 명확성 — {n}/5
{근거 + 개선 제안}

---

## 우선 수정 항목
{2점 이하 항목의 구체적 개선 목록}

---

## 수집 데이터
- 스크린샷: {PNG 경로}
- 구조 요약: Stats — TotalElements {n}, ButtonCount {n}, ImageCount {n}, TextCount {n}
```

### Step 4: 피드백 반영 (선택)

평가 결과에서 점수 3점 미만 항목이 있으면:
1. 해당 항목의 구체적 개선 제안 확인
2. 수정 작업 수행
3. Step 1부터 재평가 (반복)

---

## UXML 평가 워크플로우

UXML은 코드가 직접 읽히므로 구조 데이터 추출이 불필요:

1. **UXML/USS 파일 직접 읽기** — 레이아웃, 스타일 확인
2. **UI Builder 스크린샷** — MCP execute_code로 UI Builder 프리뷰 캡처 (가능 시)
3. **평가 기준 적용** — UGUI와 동일한 5가지 기준

---

## 요약 JSON 구조 (UIEvaluationSummary)

```json
{
  "PrefabPath": "Assets/.../PXPopup_Example.prefab",
  "RootName": "PXPopup_Example",
  "Overview": [                    // 1단계 자식 구조
    { "Name": "PXPanel_Top", "Type": "PXPanel_Top", "AnchorType": "top", "NestedPrefab": "Assets/..." }
  ],
  "Buttons": [                     // 모든 버튼 감사
    { "Path": "Bottom/BtnConfirm", "Size": "200x60", "ScreenZone": "bottom", "BgColor": "#EBC33CFF", "Label": "확인" }
  ],
  "TextHierarchy": [               // 폰트 크기별 그룹
    { "FontSize": 28, "Count": 2, "Samples": [...] },
    { "FontSize": 20, "Count": 8, "Samples": [...] }
  ],
  "TouchTargets": [                // 44px 미만 터치 타겟 (문제만)
    { "Path": "Header/BtnClose", "ActualSize": "30x30", "Type": "Button" }
  ],
  "LayoutStructure": {
    "MaxDepth": 7,
    "LayoutGroupCount": 3,
    "ScrollRects": ["Content/ScrollView"]
  },
  "Stats": {
    "TotalElements": 85,
    "ButtonCount": 6,
    "ImageCount": 24,
    "TextCount": 12
  }
}
```

### 데이터 크기 비교

| 방식 | 출력 크기 | 토큰 (추정) |
|------|---------|------------|
| `GetPrefabStructureAsJson()` | 수만 줄 | 10,000~50,000 |
| `UIEvaluationSummary.Generate()` | 100~200줄 | 500~1,500 |
| 스크린샷만 | 0줄 (이미지) | 이미지 토큰 |

---

## 자동 검증 항목 (데이터 채널)

요약 JSON에서 자동으로 판단 가능한 항목:

| 기준 | 자동 검증 | 데이터 소스 |
|------|---------|-----------|
| 터치 44px 미만 | `TouchTargets` 배열이 비어있으면 통과 | sizeDelta |
| 폰트 계층 유무 | `TextHierarchy`에 3단계 이상 크기 존재 | fontSize 그룹 |
| 버튼 색상 규칙 | `Buttons[].BgColor`과 rules.json 비교 | Image.color |
| 버튼 수 과다 | `Stats.ButtonCount` ≤ 적정 범위 | 카운트 |
| 계층 깊이 과다 | `LayoutStructure.MaxDepth` ≤ 8 | 재귀 탐색 |
| 템플릿 사용 여부 | `Overview[].NestedPrefab`에 Template 경로 존재 | Nested Prefab |

스크린샷이 필요한 항목 (시각 채널):
- 정보 계층의 시각적 강조
- 여백/breathing room
- 전체적인 시각 밸런스
- 색상 조화

---

## MCP execute_code 주의사항

- `Debug.LogError`만 출력 캡처됨
- `using` 불가 (풀네임 사용: `PX.UIAutomation.UIEvaluationSummary`)
- 멀티라인 코드 지원됨 (한 줄로 작성할 필요 없음)
