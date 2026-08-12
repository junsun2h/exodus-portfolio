# github-push (문서 GitHub 백업 — 올리기)

ExodusP1의 **md 문서만** 별도 GitHub private repo로 복사하고 커밋·push한다.

- 원격: `https://github.com/junsun2h/exodusp1-docs`
- 브랜치: `main`
- 미러 로컬 경로: `E:\Work\exodusp1-docs`
- 인증: Git Credential Manager (시스템 레벨 `credential.helper=manager` 설정됨)

## 왜 미러 방식인가

`<project-root>\`은 **remote가 없는 로컬 전용 git repo**다(Unity 코드·에셋 포함, 브랜치 `master`). 이 repo에 두 번째 remote나 orphan 브랜치를 붙이면 브랜치 전환 때마다 Unity 재임포트가 걸린다 — `CLAUDE.md`가 워크트리를 금지한 것과 같은 이유로 쓰지 않는다.

따라서 **별도 위치에 문서 전용 repo를 clone해두고, md만 그쪽으로 복사한 뒤 커밋·push**한다. 원본의 디렉토리 구조는 미러에서도 그대로 유지한다.

`ExodusP1` 쪽 git은 이 커맨드가 **절대 건드리지 않는다.** 커밋·push·add 모두 미러 repo에서만 실행한다.

---

## 동기화 대상 (이것만 올린다)

| 원본 | 미러 | 비고 |
| --- | --- | --- |
| `Docs\` | `Docs\` | 상품·배틀·마케팅·콘텐츠·인프라·plans 등 전부 |
| `.claude\` | `.claude\` | **md만** (commands·skills·references) |
| `CLAUDE.md` | `CLAUDE.md` | 루트 지침 |
| `FirebaseCLI\functions\Docs\` | 동일 | 서버 문서 |
| `FirebaseCLI\functions\.claude\` | 동일 | **md만** (commands·rules·skills) |
| `FirebaseCLI\functions\CLAUDE.md` | 동일 | 서버 지침 |

대략 800여 개. 이 목록에 없으면 올리지 않는다.

### 제외 대상 (사용자가 명시적으로 배제한 것들 — 임의로 되살리지 않는다)

- `Assets\` 전부 — `Assets\Editor\UIAutomation\Data\`의 UI 평가 리포트, `criteria.md`, `TMP_SpriteAtlas_Guide.md`, `modAssignByAllStage.md` 포함
- `FirebaseCLI\functions\healthcheck\reports\`, `FirebaseCLI\functions\test-report.md` — 자동 생성 리포트
- `Tools\`, `working\` — 툴 설명서, 임시 검증 결과
- `Library\`, `Temp\`, `Logs\`, `obj\`, `node_modules\`, `Build\` — 캐시·의존성 (`Library\PackageCache`에만 md가 2000개 넘게 있다. 반드시 걸러야 한다)
- 서드파티 에셋의 `CHANGELOG.md`/`README.md` (`Assets\StoreAsset`, `Assets\Plugins`, `Assets\ExternalDependencyManager`)

새 md 경로가 생겨 대상에 넣을지 애매하면 **임의로 추가하지 말고 사용자에게 묻는다.**

---

## 절대 금지

아래는 어떤 상황에서도 실행하지 않는다. 필요해 보이면 **실행하지 말고 사용자에게 보고한 뒤 판단을 받는다.**

- `git push --force`, `--force-with-lease` — 백업 이력을 파괴한다
- `git reset --hard`, `git clean` — 아직 백업 안 된 문서가 사라진다
- `git rebase`, `git commit --amend`, `filter-branch` 등 이력 재작성
- **robocopy의 `/MIR`, `/PURGE`** — 삭제 가드(아래)를 우회해 무조건 지우고, 파일 필터가 걸린 상태에서 대상 밖 파일까지 건드릴 수 있다. 삭제는 **아래 절차로 산출한 명시적 목록으로만** 한다
- `<project-root>\`에서 git 커밋·스테이징·push (이 커맨드는 미러 repo만 다룬다)
- 사용자 확인 없는 **원본**(`<project-root>\`) 파일 삭제·이동

## 삭제 정책 — 반영하되, 대량 삭제는 멈춘다

로컬에서 사라진 문서는 미러에서도 지운다. GitHub이 로컬의 현재 상태를 그대로 비추게 하려는 것이다.

**이 프로젝트는 문서 이동이 잦다.** 완료된 플랜은 `Docs\plans\` → `Docs\<도메인>\완료플랜\`으로 옮겨지고(`완료플랜` 폴더가 도메인마다 있다), rename 훅은 파일명 앞에 `(완)`을 붙인다. git에서 **이동·이름변경 = 삭제 + 추가**다. 삭제를 반영하지 않으면 옮길 때마다 옛 경로에 사본이 남아, 시간이 지나면 GitHub이 이미 정리된 문서의 무덤이 된다.

실수로 지웠을 때의 복구는 **git이 이미 보장한다.** 삭제를 커밋해도 이전 커밋 안에 파일이 남아 있어 `git checkout <해시> -- <경로>`로 되살릴 수 있다. force push와 이력 재작성이 금지되어 있는 한 아무것도 잃지 않는다.

다만 동기화 스크립트가 잘못되면(경로 오타로 원본을 못 찾는 등) 대량 삭제가 커밋될 수 있다. 그래서 **가드**를 둔다.

> 삭제 대상이 **10개를 넘으면** 커밋하지 말고 멈춘다. 목록과 개수를 사용자에게 보고하고 판단을 받는다.

정상적인 아카이빙 이동은 보통 몇 개 단위라 걸리지 않는다. 걸렸다면 스크립트 문제이거나, 한 번에 많이 정리한 것이다. 후자면 승인받고 진행하면 된다 — 가드는 멈춰 세우는 장치이지 금지가 아니다.

**비율 가드(전체의 N%)는 쓰지 않는다.** 대상이 800개 규모라 5%면 40개인데, 그 전에 개수 가드가 걸리므로 아무 역할도 못 한다. 반대로 대상이 적을 때는 정상 삭제 몇 건에도 과민하게 걸린다. 개수 하나로 판정한다.

## 한글 경로 주의

`Docs\상품`, `Docs\배틀`처럼 경로 대부분이 한글이다. git 명령은 **반드시 `-c core.quotepath=false`를 붙인다.** 안 붙이면 `\354\203\201...`으로 이스케이프되어 어느 문서가 바뀌었는지 못 읽는다.

```bash
git -C /e/Work/exodusp1-docs -c core.quotepath=false status --porcelain
```

## robocopy 종료 코드 주의

robocopy는 **0~7이 정상**이다(0=변경없음, 1=복사됨, 2=추가파일, 3=1+2…). **8 이상만 실패**다. `$LASTEXITCODE -ge 8`로 판정한다. `-ne 0`으로 보면 정상 복사를 실패로 오판한다.

---

## 실행 절차

### 0. 미러 부트스트랩

`E:\Work\exodusp1-docs`가 없으면 clone한다.

```bash
git clone https://github.com/junsun2h/exodusp1-docs.git /e/Work/exodusp1-docs
```

- 빈 repo라 `warning: You appear to have cloned an empty repository`가 뜨면 정상이다. 이 경우 브랜치가 아직 없으므로 첫 커밋 뒤 `git push -u origin main`으로 올린다(필요하면 `git checkout -b main` 선행).
- 인증 창이 뜨지 않고 실패하면 사용자에게 `! git clone https://github.com/junsun2h/exodusp1-docs.git E:\Work\exodusp1-docs`를 직접 실행하도록 안내한다.

이미 있으면 원격 URL이 맞는지 확인한다.

```bash
git -C /e/Work/exodusp1-docs remote -v
```

clone 직후 **줄바꿈 자동 변환을 끈다.** Windows 기본값(`core.autocrlf=true`)이면 CRLF 문서를 스테이징할 때마다 파일 수만큼 경고가 쏟아져 출력이 묻히고, 저장소에는 원본과 다른 바이트가 들어간다. 백업은 원본을 그대로 보존해야 한다.

```bash
git -C /e/Work/exodusp1-docs config core.autocrlf false
```

이 설정은 미러 repo에만 적용된다(`<project-root>\`이나 전역 설정은 건드리지 않는다).

### 1. 사전 점검

```bash
git -C /e/Work/exodusp1-docs fetch origin
git -C /e/Work/exodusp1-docs -c core.quotepath=false status -sb
```

`status -sb` 첫 줄로 원격과의 관계를 판정한다.

| 상태 | 처리 |
| --- | --- |
| `[behind N]` | 원격이 앞섬 → **`/github-pull`을 먼저 돌린다** |
| `[ahead N, behind M]` | 갈라짐 → 아래 "이력이 갈라진 경우" |
| 그 외 | 2단계로 진행 |

### 2. 동기화 — 복사 (원본 → 미러)

PowerShell에서 실행한다. `*.md` 필터가 있으므로 md 외 파일(`settings.local.json`, 스킬 스크립트 등)은 애초에 복사되지 않는다.

```powershell
$src = "<project-root>\"; $dst = "E:\Work\exodusp1-docs"
$pairs = @(
  @("$src\Docs",                            "$dst\Docs"),
  @("$src\.claude",                         "$dst\.claude"),
  @("$src\FirebaseCLI\functions\Docs",      "$dst\FirebaseCLI\functions\Docs"),
  @("$src\FirebaseCLI\functions\.claude",   "$dst\FirebaseCLI\functions\.claude")
)
$failed = $false
foreach ($p in $pairs) {
  if (-not (Test-Path $p[0])) { Write-Output "SRC MISSING: $($p[0])"; $failed = $true; continue }
  robocopy $p[0] $p[1] *.md /S /NDL /NP /NJH /NJS /R:2 /W:1 | Out-Null
  if ($LASTEXITCODE -ge 8) { Write-Output "ROBOCOPY FAILED ($LASTEXITCODE): $($p[0])"; $failed = $true }
}
Copy-Item "$src\CLAUDE.md" "$dst\CLAUDE.md" -Force
New-Item -ItemType Directory -Force "$dst\FirebaseCLI\functions" | Out-Null
Copy-Item "$src\FirebaseCLI\functions\CLAUDE.md" "$dst\FirebaseCLI\functions\CLAUDE.md" -Force
Write-Output "COPY DONE (failed=$failed)"
```

`SRC MISSING`이나 `ROBOCOPY FAILED`가 하나라도 찍히면 **여기서 멈춘다.** 원본을 못 읽은 상태로 3단계에 가면 정상 문서가 통째로 삭제 대상이 된다.

### 3. 동기화 — 삭제 대상 산출 + 가드

원본에서 사라진 md를 미러에서 걷어낸다. `/PURGE`를 쓰지 않고 **목록을 먼저 만들어 검사한 뒤** 지운다.

```powershell
$stale = @()
$mirrorTotal = 0
foreach ($p in $pairs) {
  $s = $p[0]; $d = $p[1]
  if (-not (Test-Path $d)) { continue }
  $srcSet = @{}
  Get-ChildItem $s -Recurse -Filter *.md -File | ForEach-Object { $srcSet[$_.FullName.Substring($s.Length+1)] = $true }
  Get-ChildItem $d -Recurse -Filter *.md -File | ForEach-Object {
    $mirrorTotal++
    $rel = $_.FullName.Substring($d.Length+1)
    if (-not $srcSet.ContainsKey($rel)) { $stale += $_.FullName }
  }
}
Write-Output "STALE: $($stale.Count) / MIRROR TOTAL: $mirrorTotal"
$stale | ForEach-Object { $_.Substring($dst.Length+1) }
```

**가드 판정** — `$stale.Count -gt 10`이면 **아무것도 지우지 말고 멈춘다.** 목록·개수·의심 원인을 보고하고 사용자 판단을 받는다. `$mirrorTotal`이 평소(800 안팎)보다 크게 적으면 그 자체가 복사 단계 실패 신호이므로 함께 본다.

가드를 통과했으면 삭제한다. 빈 디렉토리는 git이 추적하지 않지만 미러가 지저분해지므로 함께 정리한다.

```powershell
$stale | ForEach-Object { Remove-Item $_ -Force; Write-Output "DELETED: $($_.Substring($dst.Length+1))" }
foreach ($p in $pairs) {
  if (-not (Test-Path $p[1])) { continue }
  Get-ChildItem $p[1] -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
    Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File | Select-Object -First 1) } |
    ForEach-Object { Remove-Item $_.FullName -Force }
}
Write-Output "SYNC DONE"
```

삭제가 0건이면 이 단계는 건너뛴다.

### 4. 커밋 전 검사 (필수)

```bash
git -C /e/Work/exodusp1-docs add -A
git -C /e/Work/exodusp1-docs -c core.quotepath=false status --porcelain | head -50
git -C /e/Work/exodusp1-docs -c core.quotepath=false diff --cached --name-status | awk '{print $1}' | sort | uniq -c
```

#### 4-1. 대상 밖 파일이 섞였는지

스테이징 목록의 최상위 경로가 `Docs/`, `.claude/`, `FirebaseCLI/`, `CLAUDE.md` 넷뿐인지 확인한다. `Assets/`, `Library/`, `Tools/`, `working/`가 보이면 동기화 스크립트가 잘못된 것이다. **커밋하지 말고** 보고한다.

#### 4-2. 삭제(D)가 3단계에서 승인한 목록과 일치하는지

```bash
git -C /e/Work/exodusp1-docs -c core.quotepath=false diff --cached --name-only --diff-filter=D
```

3단계에서 산출·승인한 목록과 다르면 **커밋하지 말고** 보고한다. 여기서 `R`(rename)로 잡히는 항목은 문서가 옮겨졌다는 뜻이므로 정상이다.

#### 4-3. 비밀 정보

md만 올리므로 `settings.local.json` 같은 설정 파일은 들어오지 않는다. 다만 인프라·서버 문서에 키가 적혀 있을 수 있다.

```bash
cd /e/Work/exodusp1-docs
grep -rlIiE 'AIza[0-9A-Za-z_-]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY|ghp_[0-9A-Za-z]{20,}|"private_key"' \
  --include='*.md' . 2>/dev/null | grep -v 'commands/github-push\.md$'
```

**이 커맨드 파일(`github-push.md`) 자신은 제외한다.** 위 패턴 문자열을 그대로 담고 있어 항상 자기 자신을 탐지한다(실제로 `"private_key"` 리터럴이 매치된다). 제외하지 않으면 매번 오탐이 뜬다.

걸리면 **커밋하지 말고** 파일과 해당 줄을 보고한다. 다만 보고 전에 **실제 키인지 패턴을 설명하는 문서인지 반드시 줄 내용을 확인한다** — 인프라 문서가 키 형식을 예시로 적어둔 경우도 오탐이다. private repo라도 실키는 올리지 않는다.

#### 4-4. 대용량 파일

문서 repo라 드물지만, 레퍼런스 폴더에 큰 파일이 딸려올 수 있다. GitHub은 100MB 초과를 차단한다.

```bash
git -C /e/Work/exodusp1-docs -c core.quotepath=false diff --cached --name-only \
  | while IFS= read -r f; do
      p="/e/Work/exodusp1-docs/$f"
      [ -f "$p" ] && s=$(du -m "$p" 2>/dev/null | cut -f1) && [ "$s" -gt 95 ] && echo "$s MB  $f"
    done
```

95MB 초과가 나오면 **커밋하지 말고** `git reset`(`--hard` 아님)으로 스테이징만 풀고 보고한다.

### 5. 커밋

변경된 경로에서 **어느 도메인의 무엇이 바뀌었는지** 읽어 한국어 메시지를 만든다. 프로젝트 컨벤션대로 `docs:` prefix를 쓴다.

```
docs: 상품 포트폴리오 v3 개정, 배틀 스테이지 밸런스 분석 추가
docs: 마케팅 신규프로젝트 문서 12건 신규, 인프라 스케줄러 정리 갱신
docs: 완료 플랜 5건 아카이빙(plans → 콘텐츠/완료플랜), 커맨드 정의 갱신
```

- 여러 도메인이 섞이면 `,`로 잇는다. 파일이 많으면 `Docs/` 1단계 폴더(상품·배틀·마케팅·콘텐츠·인프라·plans…) 단위로 요약한다.
- 신규 파일이 많으면 **개수**를 넣는다("~ 12건 신규"). 나중에 이력만 보고 무엇이 언제 백업됐는지 알기 위한 것이다.
- **이동(아카이빙)은 "삭제"가 아니라 이동으로 적는다.** `plans → 콘텐츠/완료플랜`처럼 방향을 남겨야 나중에 이력에서 오해가 없다.
- 날짜는 넣지 않는다(커밋 타임스탬프에 있다).
- 메시지 끝에 아래를 붙인다.

```
Co-Authored-By: <현재 세션의 모델명> <noreply@anthropic.com>
Claude-Session: <현재 세션 URL>
```

### 6. push

```bash
git -C /e/Work/exodusp1-docs push origin main
```

첫 push라 upstream이 없으면 `git -C /e/Work/exodusp1-docs push -u origin main`.

### 7. 검증 (생략 금지)

push 성공 출력만 믿지 않는다. 실제로 대조한다.

```bash
git -C /e/Work/exodusp1-docs -c core.quotepath=false status -sb | head -1
echo "local : $(git -C /e/Work/exodusp1-docs rev-parse HEAD)"
echo "remote: $(git -C /e/Work/exodusp1-docs rev-parse origin/main)"
git -C /e/Work/exodusp1-docs ls-files | wc -l
```

삭제를 반영하므로 미러의 추적 파일 수는 원본의 대상 md 총합과 **정확히 같아야 한다.** 원본 쪽을 세어 대조한다.

```powershell
$src = "<project-root>\"
$n = 0
@("$src\Docs", "$src\.claude", "$src\FirebaseCLI\functions\Docs", "$src\FirebaseCLI\functions\.claude") |
  ForEach-Object { $n += (Get-ChildItem $_ -Recurse -Filter *.md -File).Count }
Write-Output "SRC md total: $($n + 2)   # +2 = CLAUDE.md x2"
```

- 두 해시가 일치하고 `status -sb`에 `ahead`/`behind`가 없어야 성공이다.
- 두 숫자가 어긋나면 동기화가 새거나 남은 것이다. 성공 보고하지 말고 조사한다.
- 참고로 이 커맨드를 만든 시점의 대상 md는 807개였다. 문서가 늘면 이 값도 늘어난다.

### 8. 저장소 크기 점검

```bash
git -C /e/Work/exodusp1-docs count-objects -vH | grep size-pack
```

**800MB 초과 시 알린다**(GitHub 소프트 리밋 1GB). 텍스트뿐이라 증가가 느리다. 갑자기 뛰면 대상 밖 파일이 섞인 것이니 무엇이 들어왔는지 함께 보고한다.

---

## 예외 상황

### 원격이 앞선 경우 (`behind`)

다른 PC나 GitHub 웹에서 바꾼 것이다. **`/github-pull`로 처리한다** — 그쪽에 원본 덮어쓰기 방지 절차가 있다. 여기서 급하게 `git pull`을 치지 않는다.

**이 경우 3단계 삭제를 절대 그냥 통과시키지 않는다.** 원격에서 새로 추가된 문서는 로컬 원본에 없으므로 전부 "삭제 대상"으로 잡힌다. 여기서 지우면 방금 다른 곳에서 쓴 문서를 되돌려 없애는 셈이다. `behind` 상태면 무조건 `/github-pull` 먼저다.

### 이력이 갈라진 경우 (`ahead N, behind M`)

`git -C /e/Work/exodusp1-docs pull --no-rebase`로 merge 커밋을 만들어 합친다. **rebase를 쓰지 않는다.** 백업 repo에서는 이력이 지저분해지는 것보다 아무것도 잃지 않는 것이 중요하다.

merge 후 2단계(복사)부터 다시 실행하되, **3단계 삭제 가드를 특히 엄격히 본다.** merge로 들어온 원격 신규 문서가 삭제 대상 목록에 섞여 있으면, 그건 지울 게 아니라 `/github-pull`로 로컬에 반영해야 할 것이다. 삭제 목록에 이번 merge로 들어온 파일이 하나라도 있으면 멈추고 보고한다.

### 충돌이 난 경우

**자동으로 해결하지 않는다.** 다음을 하고 멈춘다.

1. `git -C /e/Work/exodusp1-docs -c core.quotepath=false status`로 충돌 파일 확보
2. 각 파일이 어느 도메인 문서인지, 양쪽이 어떻게 다른지 요약
3. 사용자에게 보고하고 어느 쪽을 살릴지 판단을 받는다
4. `git merge --abort`로 되돌릴 수 있음을 함께 안내한다

특히 `Docs/INDEX.md`는 양쪽에서 같은 표에 행을 더하므로 충돌이 잦고, 기계 병합 결과가 그럴듯해 보여도 행이 중복·누락되기 쉽다.

### push가 거부된 경우

- `non-fast-forward` → 위 갈라짐 절차. **force push로 뚫지 않는다.**
- `GH001: Large files detected` → 이미 커밋된 상태다. 이력 재작성이 필요하므로 직접 처리하지 말고 선택지(LFS / 커밋 되돌리기 / 파일 제외 후 재커밋)를 사용자에게 제시한다.
- 인증 실패 → 사용자에게 `! git -C E:\Work\exodusp1-docs push origin main`을 직접 실행하도록 안내한다(브라우저 로그인 창이 이 세션에서는 안 뜰 수 있다).

---

## 보고

작업 후 한국어로 간결히 보고한다.

- 커밋 내용(도메인 단위 요약)과 커밋 해시
- 신규/수정/**삭제** 파일 수. 삭제가 있었으면 **무엇을 지웠는지 목록**을 반드시 남긴다(이동이면 이동으로 표기)
- push 성공 여부 + **검증 결과**(로컬/원격 해시 일치, 파일 수 대조)
- 경고가 있었다면 그 내용과 무시해도 되는 이유
- 저장소 크기가 800MB를 넘었다면 경고

막연한 성공 보고는 하지 않는다. 검증하지 않았으면 검증하지 않았다고 쓴다.
