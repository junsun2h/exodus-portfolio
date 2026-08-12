# TK_PopupEquipmentReforging — UXML Design Preview 평가 (Lite)

- 캡처: `Assets/Editor/UIAutomation/Data/Screenshot/Evaluate/UXML/TK_PopupEquipmentReforging_20260407_094656.png`
- UXML: `Assets/GameAssets/UI/UIToolkit/Content/Equipment/TK_PopupEquipmentReforging.uxml`
- USS: `Assets/GameAssets/UI/UIToolkit/Content/Equipment/TK_PopupEquipmentReforging.uss` + `Theme/PXTheme.tss`
- 평가일: 2026-04-07

---

## 정적 분석 측정 결과

| 항목 | 값 | 비고 |
|---|---|---|
| 전체 element 수 | 약 78개 (VisualElement/Label/Button/ScrollView) | UXML 직접 카운트 |
| 인라인 `style="..."` element | 4개 (`dim-bg`, `label-title`, `arrow-icon`, `btn-close`, `reforge-cost-icon`) | 약 6.4% — 양호 |
| USS `var(--*)` 사용 | 24회 (color/border 토큰 일관 사용) | 양호 |
| 하드코딩 색상 `rgba(...)` / `rgb(...)` | rf USS 11회 (대부분 alpha 오버레이/그림자) + `rgb(0,0,0)` 2회 (reforge 버튼 텍스트) | 알파 오버레이 외 텍스트 리터럴 색상 2건 — 토큰화 권장 |
| 하드코딩 `#hex` | 0회 | 우수 |
| 하드코딩 `px` 리터럴 | rf USS 약 70회 (font-size/width/padding/margin) | 토큰 미사용 — 개선 여지 |
| 트리 최대 깊이 | 10단계 (`letterbox-bg > rf-root > popup-container > main-content > right-panel > scroll-pool-list > pool-table > design-preview > pool-group > pool-header > Label`) | 약간 깊지만 ScrollView 구조상 자연스러움 |
| name 누락 인터랙션 요소 | 0건 (`btn-close`, `btn-reforge`, `btn-auto-reforge` 모두 name 보유) | 우수 |
| 클래스 접두사 일관성 | `rf-` (로컬), `es-tier-badge-*` (티어 전역), `btn-*` (버튼 전역) — 일관적 | 우수 |
| 선택자 충돌 가능성 | `rf-mod-row--locked`에서 `border-left-width` 두 번 선언 (1px → 3px 덮어쓰기) | 미세 |

---

## 항목별 평가

### 1. 정보 계층 (Information Hierarchy) — 가중치 ×3

**점수: 4 / 5**

**근거**:
- 좌측(현재 옵션) → 화살표 → 우측(풀 테이블)이라는 "변경 전 → 변경 후" 흐름이 시각적으로 명확. 중앙의 화살표 아이콘이 인과관계를 강하게 전달한다.
- 상단 중앙의 `옵션 변경` 타이틀(36px)과 우측의 5종 재화 바가 헤더 영역에서 분리되어 있고, 본문과 보더로 구분된다.
- 좌측 패널의 잠금 행(`rf-mod-row--locked`)이 골드 좌측 보더 + 옅은 골드 배경 + 자물쇠 아이콘으로 다른 행과 분명히 구분된다 — 핵심 상태 표현 우수.
- 하단의 1차 행동 버튼 2개(`옵션 변경` 그린, `옵션 자동 변경` 옐로우)가 화면 하단 1/3에 위치, 비용 아이콘+숫자가 버튼 위에 인라인 배치되어 의사결정 정보가 가까이 있음.

**감점 요인**:
- 우측 테이블 헤더 `옵션 등급/값(최대)/확률` 칼럼이 18px로 본문과 같은 크기 — 헤더 위계가 약함.
- 좌측 `장착 옵션` 섹션 타이틀(24px)과 좌측 desc 텍스트(22px) 차이가 미미해 카드 내 위계가 평탄.
- `pool-group-weight`(확률 7.4%)가 그룹 헤더 우측에 골드로 표시되지만 토글 화살표와 거의 동일 weight라 어느 게 1차 정보인지 약간 모호.

**개선 제안**:
1. 테이블 헤더 폰트를 14~16px로 작게 + uppercase/letter-spacing으로 캡션화하거나, 반대로 본문보다 크게 키워 헤더 위계를 만든다.
2. `rf-section-title`을 28px로 키우고 본문 desc는 20px로 낮춰 카드 내 3단 위계(타이틀 > 설명 > 행) 확보.
3. `pool-group-name`(22px cyan)을 24~26px로 격상해 그룹이 1차, 가중치는 2차임을 명시.

**10/10 가이드**: 테이블 헤더/본문/그룹타이틀의 폰트 위계를 3단(28/22/16)으로 정리하고, `옵션 변경 비용`을 버튼 위 별도 라인으로 분리해 강조하세요.

---

### 2. 터치 적합성 (Touch Ergonomics) — 가중치 ×3

**점수: 4 / 5**

**근거**:
- 1차 버튼 2종 모두 480×80px — 44px 최소치를 한참 상회.
- `rf-pool-group-toggle` 48×48px, `btn-close` 헤더 우측 — 각각 적절한 터치 크기.
- `rf-mod-row` 패딩 8px + 22px 폰트 + 잠금 아이콘 40px → 행 높이 약 56px 확보, 터치하기 충분.
- 좌우 패널 사이 80px `rf-arrow` 갭으로 좌/우 영역 오탭 방지.
- 위험 행동(자동 변경)이 일반 변경 버튼과 16px 떨어져 있고 색상이 다름 — 양호.

**감점 요인**:
- `rf-currency-slot`이 padding 2px 8px로 매우 컴팩트, 5개가 6px margin으로 밀집 — 터치 인터랙션이 있다면 오탭 위험. 단순 표시면 무관.
- `rf-pool-table-row`가 padding 5px 0 → 행 높이 약 30px. 캡처 우측 표에서 행 간격이 매우 좁아 보이며 행 자체에 hover 효과가 있어 클릭 의도가 있는 듯한데 터치 영역 부족.
- `btn-close` 위치가 헤더 우측 끝, currency-bar와 인접 — 캡처상 X 버튼 주변 여유가 적음.

**개선 제안**:
1. `rf-pool-table-row` padding을 `8px 0` 이상으로 키워 행 높이 44px 확보 (만약 행이 인터랙티브하다면).
2. currency 슬롯을 비인터랙티브로 명시하거나 padding을 8px 12px로 확대.
3. `btn-close`와 currency-bar 사이 12px 이상 margin 추가.

**10/10 가이드**: 우측 풀 테이블 행의 hit-area를 최소 44px로 키우고, 헤더 우측 닫기 버튼과 재화 바 사이에 명확한 시각/물리 분리를 두세요.

---

### 3. 시각적 일관성 (Visual Consistency) — 가중치 ×2

**점수: 4 / 5**

**근거**:
- 색상은 `var(--color-bg-popup)`, `var(--color-border-subtle)`, `var(--color-text-primary)`, `var(--color-grade-*)` 등 PXTheme 토큰을 24회 사용 — 우수.
- 클래스 접두사 `rf-`가 일관적으로 적용되고, 티어 색상은 전역 `es-tier-badge-*` 재사용.
- 패널 두 개가 동일한 `.rf-panel` 베이스 + `.rf-left-panel`/`.rf-right-panel` 변형으로 일관 구조.
- 잠금 상태 처리(`--locked` modifier)가 BEM 스타일로 명확.

**감점 요인**:
- `rf-reforge-cost-value`/`rf-reforge-btn-title`이 `color: rgb(0,0,0)` 하드코딩 — 토큰 미사용. 다른 곳은 모두 `var(--color-btn-text)` 사용.
- `border-radius`가 4px만 등장 (currency-slot, mod-tier, mod-row--locked) — 일관적이지만 2px/8px 같은 다른 라운드와 섞이지 않은 게 다행.
- 폰트 크기가 18, 22, 24, 26, 28, 36 의 6단으로 다양하지만 어떤 토큰 시스템(`--font-size-*`) 없이 픽셀 직접 지정 → PXTypography import는 했지만 활용 안 함.
- `text-shadow` 값이 헤더와 버튼에서 다른 값(2/2/4 vs 1/1/2)으로 두 번만 등장 — 사소.
- 캡처상 좌측 `장착 옵션` 타이틀이 그린(`--color-text-positive`)인데, 우측 풀 그룹 타이틀은 시안(`--color-text-cyan`) — 같은 "섹션 타이틀" 의미인데 색이 다름.

**개선 제안**:
1. `rf-reforge-cost-value`/`rf-reforge-btn-title`의 `rgb(0,0,0)`을 `var(--color-btn-text-on-primary)` 등 토큰으로 교체.
2. 폰트 크기를 PXTypography 변수(`--font-size-h1/h2/body/caption`)로 매핑.
3. 좌/우 섹션 타이틀 색상을 통일(둘 다 `--color-text-cyan` 또는 둘 다 positive).

**10/10 가이드**: 모든 폰트 크기·색상을 PXTheme 토큰 변수로 치환하고, 좌/우 섹션 타이틀 색상을 의미 단위로 통일하세요.

---

### 4. 인지 부하 (Cognitive Load) — 가중치 ×3

**점수: 3 / 5**

**근거**:
- 좌(현재) | 화살표 | 우(가능성) 2-컬럼 구조가 이해하기 쉽다.
- 1차 행동 버튼 2개(체크포인트 충족), 풀 그룹은 접기/펼치기로 한 번에 한 그룹만 보도록 설계.
- 상단 설명 텍스트 2줄이 좌/우 각각 위치해 맥락 안내.

**감점 요인**:
- 우측 풀 테이블이 펼쳐졌을 때 6행(S~E) × 3열의 빽빽한 숫자 표가 등장 — 캡처상 명확히 "벽돌"느낌. 행간이 좁고 헤더와 본문 색 대비가 약하다.
- 헤더 우측 재화 5종이 모두 비슷한 보라색 동전 아이콘으로 보여 시각적으로 구분이 어려움 → 어느 게 어떤 티어인지 즉시 판독 곤란 (캡처상 모든 아이콘이 거의 동일한 보라톤).
- 화면에 동시에 노출되는 정보: 5종 재화 + 좌측 3옵션 + 우측 3그룹(1개 펼침 시 6행) + 2개 액션 + 비용 = 인지 항목이 많다.
- 상/하단 설명 텍스트(좌 2줄, 우 2줄)가 본문(22px)과 같은 크기여서 "설명인지 옵션인지" 즉시 구분 불가.
- 우측 desc("변경 시 아래 목록에서 새 옵션이 결정됩니다.")는 본문과 동일 폰트로 위에 떠 있어 시선이 한 번 더 분산.

**개선 제안**:
1. 재화 아이콘에 티어 색 보더(currency-icon-common~legendary)를 명시 적용해 색상으로 구분 가능하게.
2. desc 텍스트를 16px caption + opacity 0.7로 처리해 본문과 분리.
3. 풀 테이블 행 높이를 키우고(8px+) 짝수행 zebra background 추가로 가독성 향상.
4. 기본 펼침 그룹은 1개로 유지, 다른 그룹은 접힌 상태 유지(현재 구조 그대로면 OK). `pool-group-name`에 그룹별 1차 가치(예: 최대치)를 미리 표시해 펼치지 않아도 비교 가능하게.
5. 화살표 아이콘(64×64 인라인 width)과 USS 정의(48×48)가 충돌 — 인라인 제거하고 USS로 통일해 시각적 일관성과 인지 부담 감소.

**10/10 가이드**: 재화 5종을 티어 색상으로 구분하고, 풀 테이블에 zebra/행간 확장과 그룹 접힘 시 요약값 노출을 추가해 한눈에 비교 가능하게 만드세요.

---

### 5. 피드백 명확성 (Feedback Clarity) — 가중치 ×2

**점수: 4 / 5**

**근거**:
- `rf-mod-row:hover`, `rf-pool-group-header:hover`, `rf-pool-table-row:hover` — 인터랙티브 요소에 hover 시각 피드백 정의됨.
- 잠금 상태가 골드 보더 + 좌측 액센트 + 자물쇠 아이콘 + 행 배경의 4중 신호로 매우 명확.
- `rf-pool-group-toggle` / `--expanded` 스왑으로 펼침 상태가 아이콘 변화로 표시됨(우향 → 하향).
- `btn-reforge`가 비용 아이콘+숫자를 버튼 안에 표시 → 클릭 결과 예측 가능.

**감점 요인**:
- 버튼의 `:disabled` 상태 정의 없음 (재화 부족 시 어떻게 보이는가?). `btn-positive`/`btn-primary`가 PXButton에서 disabled를 제공한다고 가정해도 rf 로컬에서 override나 보강 없음.
- `:active`(pressed) 상태가 hover만 정의되고 누름 피드백 없음.
- 잠금 토글 자체의 인터랙션 피드백 없음 (행 클릭으로 잠금이 토글된다면 클릭 직후 변화가 골드 보더 등장뿐 — 트랜지션 미정의).
- 재화 부족/변경 가능 횟수 부족 같은 조건 미충족 시 사용자에게 이유를 알릴 텍스트 슬롯이 없음.

**개선 제안**:
1. `.btn-positive:disabled`, `.rf-reforge-btn:disabled`에 grayscale + opacity 0.5 + 부족량 라벨 슬롯 추가.
2. `.rf-mod-row:active` / `.rf-pool-group-header:active`에 살짝 눌림 효과(translateY 1px or 배경 강도).
3. `transition: background-color 120ms` 추가로 hover/lock 변화를 부드럽게.
4. 비용 라벨 옆에 보유량/변경 횟수 표시 영역을 추가해 부족 시 빨간색으로 경고.

**10/10 가이드**: 모든 버튼·행에 `:active` / `:disabled` 상태를 명시하고, 재화 부족·잠금 변경 등 조건 미충족 사유를 텍스트로 노출하세요.

---

## AI 슬롭 체크리스트

| 항목 | 발견 | 비고 |
|---|---|---|
| 의미 없는 그라데이션 | 없음 | 깨끗한 플랫 다크 |
| 코너 라운드 4종 이상 | 없음 (4px만 사용) | 우수 |
| 과도한 보더(3px+ 곳곳) | 부분 (`rf-mod-row--locked` 좌측 3px만, 의도적 강조) | 감점 없음 |
| 빈 패널/플레이스홀더 잔존 | `pool-group-AllDamage`의 `pool-body`가 빈 채로 있음 (펼침 상태 아니므로 OK), 캡처상 잔존 없음 | 감점 없음 |
| 폰트 weight 단일 사용 | font-weight 명시 0회 — 모두 기본값. 위계가 크기로만 표현됨 | -0.3 |
| 색상 팔레트 폭발 | 다크 네이비 + 골드 + 시안 + 그린 + 옐로우 + 티어 6색 → 7~8색이지만 모두 의미 있게 분류됨 | 감점 없음 |
| 장식용 반복 아이콘 | 5종 재화 아이콘이 모두 비슷한 보라톤 → 의미 식별 곤란 | -0.3 |
| 계층 강조 부재 | 헤더/본문 폰트 차이가 약한 곳 일부 (테이블 헤더) | -0.2 |

**AI 슬롭 감점 합계**: **-0.8**

---

## 가중 총점

```
(정보계층 4 ×3) + (터치 4 ×3) + (시각일관성 4 ×2) + (인지부하 3 ×3) + (피드백 4 ×2)
= 12 + 12 + 8 + 9 + 8
= 49 / 13
= 3.77
```

**기본 가중 총점: 3.77 / 5.00**
**AI 슬롭 감점: -0.8 (×0.2 환산 약 -0.16)** → 반영 시 **약 3.61 / 5.00**

### 합격 여부

**합격 (≥ 3.5)** — 출시 가능 수준이지만 인지부하(3점) 항목 보강 권장.

---

## 우선 수정 항목 (점수 ≤ 2)

해당 없음. 단, **인지부하(3점)** 항목이 합격선 가까이에 있어 다음을 권장 우선 작업으로 제시:

1. 우측 풀 테이블 가독성 개선 (zebra, 행간, 그룹 요약)
2. 5종 재화 아이콘의 티어 색상 차별화
3. desc 텍스트와 본문의 폰트 위계 분리

---

## 요약

| 항목 | 점수 | 가중 |
|---|---|---|
| 정보 계층 | 4 | ×3 |
| 터치 적합성 | 4 | ×3 |
| 시각 일관성 | 4 | ×2 |
| 인지 부하 | 3 | ×3 |
| 피드백 명확성 | 4 | ×2 |
| **가중 총점** | **3.77** | (슬롭 감점 후 ~3.61) |
| **합격 여부** | **PASS** | |
| **우선 수정 (≤2)** | **0건** | |
