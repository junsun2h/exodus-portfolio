---
description: Markdown 문서를 reveal.js HTML 슬라이드로 변환
---

# MD → HTML 슬라이드 변환 Skill

## 트리거

- `/tool-htmlslide <md파일경로>` 명령
- `/tool-htmlslide <md파일경로> dark` (다크 테마)

---

## 핵심 원칙

1. **단일 HTML 파일**: reveal.js CDN으로 로드, 별도 설치 불필요
2. **잘림 방지**: 모든 콘텐츠가 슬라이드 영역 안에 들어와야 함. 작아져도 잘리면 안 됨
3. **내부 공유용**: 깔끔하고 읽기 쉬운 레이아웃, 과도한 장식 불필요
4. **한글 최적화**: Noto Sans KR (Google Fonts CDN) 기본, 맑은 고딕 폴백

---

## 슬라이드 분할 규칙

### md 구조 → 슬라이드 매핑

```
# H1 제목          → 타이틀 슬라이드 (1장)
## H2 섹션          → 각각 독립 슬라이드 (1장 이상)
### H3 하위 섹션     → 부모 H2 슬라이드 내 카드/영역으로 배치
```

### 콘텐츠 양에 따른 분할

- H2 섹션의 콘텐츠가 **슬라이드 1장에 들어가지 않으면** 2장 이상으로 분할
- 판단 기준: 테이블 행 7개 이상, 불릿 8개 이상, 카드 4개 이상이면 분할 검토
- **절대 잘리지 않도록** — 분할이 나음

### 콘텐츠 유형별 레이아웃

| md 요소 | 슬라이드 레이아웃 |
|---------|-----------------|
| 표 (table) | `<table>` 스타일링 |
| 불릿 리스트 | `<ul>` + 카드 레이아웃 |
| 코드블록 (```) | 카드 내 구조화된 텍스트 또는 플로우 다이어그램 |
| 인용 (>) | `.quote` 스타일 블록 |
| 비교 구조 (Before/After, 장단점) | 2컬럼 `.vs-container` |
| 순서/단계 (Phase, Step) | `.flow` 플로우 또는 `.grid-N` 카드 |
| 수치 강조 | `.big-num` + `.big-label` |

---

## HTML 템플릿

### 기본 구조

```html
<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{문서 제목}</title>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.css">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/theme/white.css">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Noto+Sans+KR:wght@400;500;700;900&display=swap" rel="stylesheet">
<style>
  /* 테마 변수 (라이트/다크) */
  {THEME_VARIABLES}

  /* 공통 스타일 */
  {COMMON_STYLES}
</style>
</head>
<body>
<div class="reveal">
<div class="slides">

  {SLIDES}

</div>
</div>
<script src="https://cdn.jsdelivr.net/npm/reveal.js@5.1.0/dist/reveal.js"></script>
<script>
  Reveal.initialize({
    hash: true,
    slideNumber: 'c/t',
    width: 1280,
    height: 720,
    margin: 0.04,
    transition: 'slide',
    transitionSpeed: 'fast',
    center: true,
    controls: true,
    progress: true,
  });
</script>
</body>
</html>
```

---

## 테마 변수

### 라이트 테마 (기본)

```css
:root {
  --bg: #f5f5fa;
  --bg-slide: #ffffff;
  --card: #f0f1f5;
  --text: #2d2d3a;
  --text-sub: #6b6b80;
  --text-muted: #9999aa;
  --accent: #2563eb;
  --purple: #7c3aed;
  --green: #059669;
  --orange: #d97706;
  --red: #dc2626;
  --yellow: #ca8a04;
  --border: rgba(0,0,0,0.08);
}
```

reveal.js 테마 CSS: `white.css`
슬라이드 배경: `data-background-color="#ffffff"`

### 다크 테마 (dark 인자)

```css
:root {
  --bg: #1a1a2e;
  --bg-slide: #1a1a2e;
  --card: #16213e;
  --text: #e8e8f0;
  --text-sub: #a0a0b0;
  --text-muted: #666680;
  --accent: #00d2ff;
  --purple: #c49bff;
  --green: #00e676;
  --orange: #ff9f43;
  --red: #ff6b6b;
  --yellow: #ffd93d;
  --border: rgba(255,255,255,0.08);
}
```

reveal.js 테마 CSS: `black.css`
슬라이드 배경: `data-background-color="#1a1a2e"`

---

## 공통 CSS 스타일

```css
.reveal {
  font-family: 'Noto Sans KR', 'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif;
}
.reveal h1, .reveal h2, .reveal h3 {
  font-family: 'Noto Sans KR', sans-serif;
  font-weight: 700;
  text-transform: none;
  color: var(--text);
}
.reveal h1 { font-size: 2.0em; }
.reveal h2 { font-size: 1.3em; color: var(--accent); }
.reveal h3 { font-size: 0.9em; color: var(--purple); margin-bottom: 0.2em; }
.reveal section { padding: 10px !important; }
.reveal p, .reveal li { color: var(--text); }

/* 카드 */
.card {
  background: var(--card);
  border-radius: 10px;
  padding: 14px 18px;
  margin: 4px 0;
  text-align: left;
  border: 1px solid var(--border);
}
.card.blue   { border-left: 4px solid var(--accent); }
.card.purple { border-left: 4px solid var(--purple); }
.card.green  { border-left: 4px solid var(--green); }
.card.orange { border-left: 4px solid var(--orange); }
.card.red    { border-left: 4px solid var(--red); }
.card.yellow { border-left: 4px solid var(--yellow); }

/* 그리드 */
.grid { display: grid; gap: 10px; }
.grid-2 { grid-template-columns: 1fr 1fr; }
.grid-3 { grid-template-columns: 1fr 1fr 1fr; }
.grid-4 { grid-template-columns: 1fr 1fr 1fr 1fr; }

/* 배지 */
.badge {
  display: inline-block;
  padding: 2px 10px;
  border-radius: 20px;
  font-size: 0.55em;
  font-weight: bold;
  vertical-align: middle;
  margin-left: 6px;
}
.badge.blue   { background: var(--accent); color: #fff; }
.badge.purple { background: var(--purple); color: #fff; }
.badge.green  { background: var(--green); color: #fff; }
.badge.orange { background: var(--orange); color: #fff; }
.badge.red    { background: var(--red); color: #fff; }
.badge.yellow { background: var(--yellow); color: var(--bg); }

/* 테이블 */
.reveal table {
  font-size: 0.55em;
  margin: 0 auto;
  border-collapse: collapse;
}
.reveal table th {
  background: var(--accent);
  color: #fff;
  padding: 5px 10px;
  font-weight: bold;
}
.reveal table td {
  padding: 4px 10px;
  border-bottom: 1px solid var(--border);
  color: var(--text);
}
.reveal table tr:nth-child(even) td {
  background: rgba(0,0,0,0.02);
}

/* 리스트 */
.reveal ul { font-size: 0.65em; line-height: 1.5; }
.reveal li { margin-bottom: 2px; }
.reveal li::marker { color: var(--accent); }

/* 인용 */
.quote {
  background: linear-gradient(135deg, rgba(124,58,237,0.08), rgba(37,99,235,0.06));
  border-left: 4px solid var(--purple);
  border-radius: 0 10px 10px 0;
  padding: 12px 20px;
  font-size: 0.65em;
  font-style: italic;
  color: var(--text);
  margin: 8px 0;
  text-align: left;
}

/* 플로우 (단계/순서) */
.flow {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  flex-wrap: wrap;
  margin: 8px 0;
}
.flow-item {
  background: var(--card);
  border-radius: 8px;
  padding: 7px 14px;
  font-size: 0.55em;
  border: 1px solid var(--border);
  white-space: nowrap;
  color: var(--text);
}
.flow-arrow {
  color: var(--accent);
  font-size: 1em;
  font-weight: bold;
}

/* VS 비교 */
.vs-container {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 10px;
  align-items: start;
}
.vs-divider {
  color: var(--accent);
  font-size: 1.6em;
  font-weight: bold;
  align-self: center;
  padding-top: 20px;
}

/* 수치 강조 */
.big-num {
  font-size: 1.6em;
  font-weight: bold;
  color: var(--accent);
}
.big-label {
  font-size: 0.58em;
  color: var(--text-sub);
  display: block;
  margin-top: 2px;
}

/* 슬라이드 제목 라인 */
.slide-header {
  border-bottom: 2px solid var(--accent);
  padding-bottom: 4px;
  margin-bottom: 10px;
}

/* 유틸리티 */
.small { font-size: 0.52em; color: var(--text-muted); margin-top: 8px; }
.mt-1 { margin-top: 10px; }
.mt-2 { margin-top: 16px; }
.text-left { text-align: left; }
.accent { color: var(--accent); }
.green { color: var(--green); }
.red { color: var(--red); }
.orange { color: var(--orange); }
.yellow { color: var(--yellow); }
.purple { color: var(--purple); }
```

---

## 슬라이드 제작 가이드라인

### 타이틀 슬라이드 (항상 1장째)

```html
<section data-background-color="{BG_COLOR}">
  <div style="margin-top: 40px;">
    <h1 style="color: var(--accent); margin-bottom: 0;">{H1 제목}</h1>
    <h2 style="color: var(--text); font-size: 1.3em; margin-top: 8px;">{부제}</h2>
    <div style="width: 200px; height: 3px; background: var(--accent); margin: 24px auto;"></div>
    <p style="color: var(--text-sub); font-size: 0.6em;">
      내부 기획 공유 &nbsp;|&nbsp; {날짜}
    </p>
    <p style="color: var(--text-muted); font-size: 0.48em; margin-top: 30px;">
      방향키 또는 스페이스바로 넘기기
    </p>
  </div>
</section>
```

### 콘텐츠 슬라이드

```html
<section data-background-color="{BG_COLOR}">
  <h2 class="slide-header">{번호} &nbsp; {섹션 제목}</h2>
  <!-- 콘텐츠 -->
</section>
```

### 요약 슬라이드 (마지막)

문서의 핵심 포인트 3~4개를 카드로 정리하여 마지막 슬라이드로 구성.

---

## 잘림 방지 체크리스트

생성 시 반드시 확인:

1. **폰트 사이즈**: 본문 0.55~0.65em, 카드 내부 0.52~0.55em
2. **카드 패딩**: `padding: 10px~14px 16px~18px` (과도하면 안 됨)
3. **마진/갭**: grid gap 8~10px, 카드 margin 4px
4. **슬라이드 패딩**: `section { padding: 10px !important; }`
5. **콘텐츠 분할**: 한 슬라이드에 카드 4개 이상이면 2장으로 분할 검토
6. **테이블 행**: 7행 이상이면 폰트 0.5em 이하로 축소
7. **reveal.js margin**: `0.04` (슬라이드 주변 여백 최소화)

---

## 가독성 체크리스트

1. **다크 테마에서 보라색**: `#c49bff` 사용 (어두운 `#7b2fff` 금지)
2. **라이트 테마에서 노란색**: `#ca8a04` 사용 (밝은 `#ffd93d` 금지)
3. **배지 텍스트**: 배경색과 충분한 대비
4. **회색 텍스트**: 다크에서 `#a0a0b0`, 라이트에서 `#6b6b80` (너무 연하면 안 됨)

---

## 출력 규칙

1. **파일 위치**: md 파일과 같은 폴더에 같은 이름의 `.html` 확장자로 생성
   - 예: `논의/설계문서.md` → `논의/설계문서.html`
2. **기존 파일 덮어쓰기**: 같은 이름의 html이 이미 있으면 덮어씀
3. **브라우저 열기**: 생성 후 `start "" "{파일경로}"` 로 브라우저에서 열기
4. **슬라이드 수 보고**: 생성 완료 시 총 슬라이드 수와 간략한 목차 표시
