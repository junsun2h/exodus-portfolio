# F-55. AI 워크플로우 엔지니어링 자산

> 소스 발췌: `src/` — 22개 파일 (커맨드 13 + 스킬 5 + 스킬 예제 2 + 훅 2)
>
> 커스텀 커맨드 31종·스킬 8종 중 **역량이 드러나는 것만** 골라 넣었다. 자산 목록과 각각이 무엇을 보여주는지는
> 아래 [**무엇을 자산화했나**](#무엇을-자산화했나--커맨드-13종--스킬-5종) 섹션에 있다.
> 커밋·푸시·로그 정리처럼 어느 프로젝트에나 있는 것들은 뺐다.
>
> `unity-mcp-skill.md` 의 `references/` 하위 문서(약 3,000줄)는 분량 때문에 제외했다. 그래서 파일 안의
> `references/tools-reference.md`, `references/workflows.md` 링크는 이 리포에서 열리지 않는다.

**구간** Phase 3 (2026.03 ~ 08) | **포지션** TD | **AI** 오케스트레이션

- **문제**: AI와 오래 일할수록 세 가지가 문제가 된다. (1) 프로젝트 지식이 대화마다 휘발되고, (2) 같은 실수를 반복하며, (3) **컨텍스트가 유한 자원**인데 무의미한 내용이 그것을 잡아먹는다.
- **해결**: AI 협업 자체를 **엔지니어링 대상**으로 다뤄 자산화했다.

| 자산 | 규모 | 역할 |
|---|---|---|
| **문서 지식베이스** | **765개** (Docs 698 + FirebaseCLI 67) | 프로젝트 지식의 외부 저장소 |
| **INDEX 색인** | `Docs/INDEX.md` | 키워드로 위키를 찾는 진입점. 갱신도 커맨드로 자동화 |
| **커스텀 커맨드** | **31종** | 반복 작업 규격화 |
| **스킬** | **8종** | 도메인 특화 워크플로우 |
| **훅** | **3종** | 규칙의 **자동 강제** |
| **MCP** | **4종** | Unity / Context7 / Firebase 등 외부 시스템 연결 |

  **훅 3종이 특히 이 프로젝트의 성격을 보여준다** — 규칙을 CLAUDE.md에 쓰는 것으로 끝내지 않고 **차단으로 강제**한다.
  - `block-bash-token-bomb.mjs` — 출력 제한 없는 재귀 탐색(`find` 등)을 **차단**. 수만 줄 출력으로 컨텍스트가 날아가는 것을 막는다.
  - `block-full-uxml-read.mjs` — UXML 전체 읽기 차단. UXML은 한 줄이 매우 길어 줄 수와 무관하게 토큰을 폭발시킨다.
  - `rename-plans.sh` — 플랜 문서에 완료 마커가 붙으면 파일명을 자동으로 `(완)` 접두사로 변경.

  **자동 메모리 쓰기 2-tier 설계** — 대화 내용을 자동으로 메모리에 반영하는 시스템에서, **Tier 1은 LLM 호출을 0회로** 설계했다. 트랜스크립트를 파싱해 `tool_result`를 **81% 제거**한 뒤 필요한 것만 상위 tier로 넘긴다. LLM으로 요약하면 비용도 지연도 발생하지만, 대부분의 노이즈는 **파싱만으로 제거 가능**하다는 판단.
- **기술**: Claude Code 커스텀 커맨드/스킬/훅 작성, MCP 서버 연동, 트랜스크립트 파싱 기반 컨텍스트 압축, 문서 색인 자동 갱신
- **정량**: 문서 **765개** / 커맨드 31 + 스킬 8 + 훅 3 + MCP 4 = **자산 46종** / Tier1 **LLM 호출 0**으로 `tool_result` **81% 제거**
- **근거**:
  - `.claude/commands/` — 31개
  - `.claude/skills/` — 8개
  - `.claude/hooks/block-bash-token-bomb.mjs`, `.claude/hooks/block-full-uxml-read.mjs`, `.claude/hooks/rename-plans.sh`
  - `Docs/INDEX.md` — 위키 색인
  - `Docs/plans/26.06.24_자동-메모리-쓰기-시스템(memorybox-memory-sync)-명세서.md`
  - `CLAUDE.md` — 프로젝트 작업 규약 (토큰 절약 규칙, 파일 읽기 규칙 등)
- **면접 포인트**: **"AI를 쓰는 사람"과 "AI 워크플로우를 만드는 사람"의 차이를 보여주는 카드.** 세 가지가 근거다 — (1) 규칙을 **훅으로 강제**했다(문서에 적어두면 지켜지지 않는다는 것을 인정하고 차단으로 바꿨다), (2) 컨텍스트를 **유한 자원으로 관리**했다(토큰 폭탄 차단, UXML 풀리드 차단), (3) 메모리 시스템 Tier1을 **LLM 호출 0**으로 설계했다(파싱으로 풀 수 있는 문제를 굳이 AI로 풀지 않는 판단 — F-32에서 기계 판정 가능한 검증을 자동 검출로 뺀 것과 같은 원칙). 765개 문서 지식베이스는 그 자체로 **AI가 읽을 수 있는 형태로 프로젝트를 외재화한 결과물**이다.
- **슬라이드 자료**: 자산 46종 구성도 + 훅 차단 실제 출력 — **다이어그램 필요** + **캡처 필요**


---

## 무엇을 자산화했나 — 커맨드 13종 + 스킬 5종

전체 31종 중 **역량이 드러나는 것만** 실었다. 각 항목이 무엇을 보여주는지를 기준으로 묶었다.

| 자산 | 규모 | 무엇을 보여주는가 |
|---|--:|---|
| `bm-sangpum-design-discuss` <br> `bm-sangpum-portfolio` | 166줄 <br> 468줄 | **대립하는 4개 관점을 병렬로 돌린다** — 단일 응답의 편향 회피 |
| `ui-evaluate-lite` <br> `ui-evaluate-runtime` | 258줄 <br> 198줄 | **만든 에이전트와 평가 에이전트를 분리**, 가중 루브릭으로 합격/불합격 판정 |
| `data-coredata-creator` <br> `data-upload-gamedb` <br> `data-sync-achieveitemconfig` | 249줄 <br> 301줄 <br> 205줄 | 스키마 하나 바꿀 때의 **파급 범위를 실행 가능한 형태로 외재화** |
| `battlebalance` <br> `battlebalance-simulator` | 156줄 <br> 79줄 | **컨텍스트를 유한 자원으로 다룬 커맨드 설계** — 축별 분할 + 진입점 인덱스 + 네거티브 트리거 |
| `code-healthcheck` | 125줄 | 야간 무인 감사([F-34](../F-34_야간무인감사파이프라인/))의 진입점 |
| `tool-image-extract` | 254줄 | AI 생성 시안 → **개별 UI 에셋 추출 파이프라인** |
| `docs-addwiki` | 92줄 | 문서 **765개 지식베이스의 자가 증식 루프** |
| `tool-htmlslide` | 329줄 | MD → reveal.js 슬라이드 자동 변환 |
| `clarifying-questions` <br> `systematic-debugging` <br> `verification-before-completion` | 54줄 <br> 69줄 <br> 83줄 | **발동 조건을 좁게 잡는 것 자체가 설계**라는 것 |
| `debug-unity-log` <br> `unity-mcp-skill` | 37줄 <br> 161줄 | AI가 남기는 흔적 관리 / Unity Editor MCP 오케스트레이션 |

### ① 하나의 답이 아니라, 대립하는 네 개의 답

상품 설계를 AI 한 명에게 물으면 **"그럴듯한 찬성"** 이 돌아온다. `bm-sangpum-design-discuss` 는 그래서 역할을 두 개의 대립축 위에 배치하고 **4개 에이전트를 single message 로 동시 실행**한다.

```
              [수익 극대화]
                    │
   ① 수익 전략가 ────┼──── ③ 게임 시스템 설계자
                    │
   [이론/전략] ──────┼────── [실행/현실]
                    │
   ② 플레이어 심리 ──┼──── ④ 현실 검증자 (Devil's Advocate)
                    │
              [유저 보호]
```

각각의 핵심 질문이 다르다 — *"이걸로 돈을 벌 수 있는가"* / *"유저가 이걸 어떻게 느끼는가"* / *"게임 밸런스에 어떤 영향인가"* / *"그게 정말 되겠어?"*. **④번은 명시적으로 회의적 역할로 고정**해 두었다. 아무도 반대하지 않으면 논의가 아니기 때문이다.

그리고 이건 논의로 끝나지 않는다. `design-discuss`(논의) → `analysis`(포트폴리오 분석) → `portfolio`(설계 + JSON 생성) → `tosheet`(구글 시트 업로드) 로 **논의부터 운영 데이터 반영까지가 한 체인**이다.

### ② Generator ≠ Evaluator

`ui-evaluate-*` 는 **UI를 만든 에이전트와 평가하는 에이전트를 분리**한다. 자기가 만든 걸 자기가 평가하면 통과한다.

평가는 인상이 아니라 **가중 루브릭**이다 — 정보 계층(×3) / 터치 적합성(×3) / 시각적 일관성(×2) / 인지 부하(×3) / 피드백 명확성(×2), **합격선 가중 총점 3.5/5.0**. 실제로 FAIL 판정이 나온 리포트가 [F-32](../F-32_UGUItoUXML변환스킬/) 에 있다.

`lite` 와 `runtime` 을 나눈 것도 같은 성격의 판단이다. `lite` 는 UXML을 headless 로 렌더해 즉시 평가하지만 **Grid/List 가 비어 있다는 한계를 스스로 명시**하고, 동적 콘텐츠가 핵심이면 `runtime` 으로 넘기라고 적어 둔다. **무엇을 못 보는지를 도구가 알고 있어야** 결과를 믿을 수 있다.

### ③ "어디를 고쳐야 하는지"를 실행 가능한 형태로 꺼내 놓기

새 CoreData 하나를 추가할 때 손대야 하는 파일이 몇 개인지는 보통 **아는 사람만 아는 지식**이다. `data-coredata-creator` 는 그걸 문서가 아니라 **커맨드**로 만들었다 — 필수 입력(단수형/복수형/프로퍼티 정의)부터 네이밍 컨벤션 자동 변환표(`Reward` → 상수 `REWARD`, 키 `Rewards`)까지 커맨드 안에 들어 있다. 사람이 하든 AI가 하든 **같은 경로를 타게 된다**. ([F-04](../F-04_코드생성파이프라인/), [F-19](../F-19_CoreData마이그레이션자동화/) 와 짝을 이루는 자산)

### ④ 커맨드 하나를 6개 파일로 쪼갠 이유

`/battlebalance` 는 파일이 6개다 — 메인 + 분석 축별 3개(빌드·스킬 / 스테이지 / 콘텐츠) + **인덱스 2개**. 축별로 나눈 건 필요한 것만 로드하기 위해서고, 인덱스 2개(`battlebalance-damage-calc`, `battlebalance-simulator`)는 **AI가 파일 탐색에 토큰을 쓰지 않도록 진입점을 미리 적어 둔 것**이다.

그리고 `battlebalance-simulator.md` 에는 **`## 사용하지 말아야 할 때`** 섹션이 있다.

> *"일반 배틀/밸런스 질문에는 사용하지 않는다 — 그건 `battlebalance` 계열의 영역이다."*

**커맨드를 언제 쓰는지보다 언제 쓰지 않는지가 더 중요할 때가 있다.** 비슷한 이름의 자산이 늘어나면 잘못 잡히는 비용이 안 잡히는 비용보다 커진다.

### ⑤ 발동 조건을 좁히는 것 자체가 설계다

스킬은 **과발동하면 오히려 방해**가 된다. 그래서 세 스킬 모두 발동 조건이 `description` 안에 정량으로 박혀 있다.

| 스킬 | 발동 조건 | 발동하지 않는 경우 |
|---|---|---|
| `clarifying-questions` | 모호하고 **5분+** 걸리는 요청 | *"명확하거나 사소한 요청에는 발동하지 않음"* |
| `systematic-debugging` | **같은 시도 2번 실패** 또는 **5분** 미해결 | 그 전까지는 평범하게 진행 |
| `git-workflow` | 사용자가 명시적으로 커밋·푸시 요청 | *"매 파일 편집마다 자동으로 git을 실행하는 용도가 아닙니다"* |

`systematic-debugging` 과 `verification-before-completion` 은 외부 자산(superpowers)을 차용했고, **스킬 본문에 그렇게 적어 두었다.**

### ⑥ AI가 남기는 흔적을 관리한다

디버깅용 로그가 그대로 코드베이스에 남는 문제를 *"쓰지 마"* 가 아니라 **API를 갈아끼워** 해결했다. `debug-unity-log` 는 `Debug.Log`/`Debug.LogWarning` 을 금지하고 `EditorLogCollector.Log` 로 보낸다 — 오류만 `Debug.LogError`. 수집된 로그는 Unity Console과 별도로 파일에 쌓여 그대로 AI에 전달된다. 여기에 `/debug-p1clearlog` 가 **그 세션에서 AI가 직접 작성한 로그만** 골라 제거한다.

### ⑦ 모델 티어를 커맨드마다 지정

31종 중 **11종이 `model: opus`** (분석·설계·평가·논의), **3종이 `model: haiku`** (커밋 메시지 생성, 로그 제거, 파일 리네임 — 기계적 정리), 나머지는 세션 기본값을 따른다. **작업의 성격에 따라 모델 비용을 배분**한 것으로, 모든 걸 최상위 모델로 돌리는 것도 모든 걸 값싼 모델로 돌리는 것도 하지 않았다.

---

## 수록 파일

**커맨드 13종** (`.claude/commands/`)

- `battlebalance.md` · `battlebalance-simulator.md`
- `bm-sangpum-design-discuss.md` · `bm-sangpum-portfolio.md`
- `code-healthcheck.md`
- `data-coredata-creator.md` · `data-upload-gamedb.md` · `data-sync-achieveitemconfig.md`
- `docs-addwiki.md`
- `tool-htmlslide.md` · `tool-image-extract.md`
- `ui-evaluate-lite.md` · `ui-evaluate-runtime.md`

**스킬 5종** (`.claude/skills/`)

- `clarifying-questions/skill.md`
- `debug-unity-log/skill.md`
- `systematic-debugging/skill.md`
- `unity-mcp-skill/SKILL.md`
- `verification-before-completion/skill.md`

**훅 2종** (`.claude/hooks/`)

- `block-bash-token-bomb.mjs`
- `block-full-uxml-read.mjs`
