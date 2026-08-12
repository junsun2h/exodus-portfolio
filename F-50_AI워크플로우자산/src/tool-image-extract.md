---
description: AI 생성 합성 이미지에서 개별 요소를 투명 PNG로 추출
---

# AI 이미지 개별 추출 Skill

## 트리거

- `/tool-image-extract <이미지경로>` — 단일 이미지 추출 (배경색 자동 감지)
- `/tool-image-extract <폴더경로>` — 폴더 내 모든 이미지(png/jpg/jpeg)를 각각 추출
- `/tool-image-extract <경로> --bg <색상>` — 배경색 직접 지정 (예: `pink`, `white`, `black`, `#FF00FF`)
- `/tool-image-extract <경로> --threshold <값>` — 색상 허용 오차 (기본: 60, 범위 0-255)
- `/tool-image-extract <경로> --min-size <값>` — 최소 영역 크기 (기본: 50000 pixels)

### 폴더 모드 동작
- 인자가 폴더인 경우, 해당 폴더의 `*.png`, `*.jpg`, `*.jpeg` 파일을 모두 탐색
- 각 이미지마다 동일한 추출 파이프라인 실행
- `extracted/` 하위 폴더는 원본 이미지와 같은 위치에 생성
- 이미 `extracted/` 폴더 안에 있는 이미지는 건너뜀 (재귀 방지)

---

## 핵심 원칙

1. **배경 자동 감지**: 이미지 네 모서리 픽셀을 샘플링하여 배경색 추정
2. **투명 처리**: 배경색 영역을 알파 0으로 변환
3. **개별 추출**: Connected Component 분석으로 독립 요소 식별
4. **자동 트림**: 각 요소의 투명 여백 제거
5. **원본 보존**: 원본 이미지는 수정하지 않음

---

## 배경 유형 판별 (Step 0)

이미지를 Read로 확인한 후, 배경 유형을 판별한다:

| 배경 유형 | 특징 | 처리 방법 |
|-----------|------|-----------|
| **단색 배경** (핑크, 흰색 등) | 모서리 샘플이 일정한 RGB 값 | → **방법 A**: 색상 거리 기반 마스킹 |
| **실제 투명 배경** | 알파 채널에 0 값 존재 | → **방법 B**: 알파 채널 기반 분리 |

**판별 코드:**
```python
img = Image.open(image_path).convert('RGBA')
arr = np.array(img)
alpha = arr[:,:,3]

if np.any(alpha == 0):
    method = 'alpha'  # 방법 B
else:
    method = 'solid_color'  # 방법 A
```

---

## 방법 A: 단색 배경 처리

핑크/마젠타 등 뚜렷한 단색 배경에 적합.

### Step 1: 배경색 감지

```python
corners = [
    arr[0:5, 0:5],        # top-left
    arr[0:5, -5:],         # top-right
    arr[-5:, 0:5],         # bottom-left
    arr[-5:, -5:]          # bottom-right
]
bg_color = np.median(np.concatenate([c.reshape(-1, 4) for c in corners]), axis=0)[:3]
```

### Step 2: 배경 마스크 생성

```python
threshold = 60  # 색상 허용 오차
r, g, b = arr[:,:,0], arr[:,:,1], arr[:,:,2]
bg_r, bg_g, bg_b = bg_color

distance = np.sqrt(
    (r.astype(float) - bg_r)**2 +
    (g.astype(float) - bg_g)**2 +
    (b.astype(float) - bg_b)**2
)
is_bg = distance < threshold
```

### Step 2.5: anti-alias 경계 보정

```python
from scipy.ndimage import binary_dilation

# Grid 분할용 원본 마스크 보존 (dilation 전)
is_bg_raw = is_bg.copy()

# 배경 마스크를 2px 확장하여 anti-alias 번짐 잔여물 제거
is_bg = binary_dilation(is_bg, iterations=2)
```

### Step 3: 투명화 및 Connected Component 분석

```python
arr_out = arr.copy()
arr_out[is_bg, 3] = 0

mask = arr_out[:,:,3] > 0
labeled, num_features = ndimage.label(mask)
slices = ndimage.find_objects(labeled)
```

### Step 4: 개별 요소 추출 및 저장

```python
min_area = 50000
transparent_img = Image.fromarray(arr_out)
img_area = h * w

components = []
for i, s in enumerate(slices):
    if s is None:
        continue
    ch = s[0].stop - s[0].start
    cw = s[1].stop - s[1].start
    if ch * cw < min_area:
        continue
    if ch * cw > img_area * 0.9:
        continue  # 전체 크기 컴포넌트 제외

    cropped = transparent_img.crop((s[1].start, s[0].start, s[1].stop, s[0].stop))
    bbox = cropped.getbbox()
    if bbox:
        cropped = cropped.crop(bbox)
    components.append((cropped, cropped.size[0], cropped.size[1]))
```

### Step 4.5: Grid 기반 사후 검증 및 재분할 (행별 독립 분할)

CC와 Grid 두 방식을 **항상 모두 실행**하고, 더 많은 요소를 추출한 쪽을 채택한다.
**행(row)별 독립 분할**로 AI가 비균등 그리드(예: 1행 2열, 2행 3열)를 생성해도 대응 가능.

**동작 원리**:
1. 수평 분할선을 찾아 행(row strip)으로 분리
2. 각 행 내에서 독립적으로 수직 분할선 탐색
3. 행마다 열 수가 달라도 정상 동작

**전략**: CC 결과와 Grid 결과를 비교하여 더 많은 유효 요소를 추출한 쪽을 최종 채택.
이렇게 하면 CC가 균일하게 합쳐지는 경우(oversized 감지 불가)도 Grid가 잡아낸다.

**중요**: Grid 분할에는 `is_bg_raw` (dilation 전 원본 배경 마스크)를 사용한다.
dilation된 마스크는 분할선까지 침범하여 Grid 감지를 방해할 수 있다.

```python
def find_split_lines(bg_ratio, min_gap=10, ratio_threshold=0.8):
    """배경 비율이 높은 연속 구간(분할선)을 찾는다."""
    is_separator = bg_ratio > ratio_threshold
    lines = []
    start = None
    for i, v in enumerate(is_separator):
        if v and start is None:
            start = i
        elif not v and start is not None:
            if i - start >= min_gap:
                lines.append((start, i))
            start = None
    if start is not None and len(bg_ratio) - start >= min_gap:
        lines.append((start, len(bg_ratio)))
    return lines

def grid_resplit_rowwise(is_bg_raw, transparent_img, min_area=50000):
    """행별 독립 분할: 먼저 행을 나누고, 각 행 내에서 열을 독립 탐색."""
    h, w = is_bg_raw.shape

    # Step 1: 수평 분할선 → 행 분리
    bg_row_ratio = is_bg_raw.mean(axis=1)
    h_lines = find_split_lines(bg_row_ratio)
    row_edges = [0] + [(s + e) // 2 for s, e in h_lines] + [h]

    cells = []
    for ri in range(len(row_edges) - 1):
        y0, y1 = row_edges[ri], row_edges[ri + 1]
        if y1 - y0 < 20:
            continue

        # Step 2: 각 행 내에서 수직 분할선 독립 탐색
        row_bg = is_bg_raw[y0:y1, :]
        bg_col_ratio = row_bg.mean(axis=0)
        v_lines = find_split_lines(bg_col_ratio)
        col_edges = [0] + [(s + e) // 2 for s, e in v_lines] + [w]

        for ci in range(len(col_edges) - 1):
            x0, x1 = col_edges[ci], col_edges[ci + 1]
            if (y1 - y0) * (x1 - x0) < min_area:
                continue
            cell = transparent_img.crop((x0, y0, x1, y1))
            bbox = cell.getbbox()
            if bbox:
                cell = cell.crop(bbox)
                if cell.size[0] * cell.size[1] >= min_area:
                    cells.append((cell, cell.size[0], cell.size[1]))

    return cells

# --- CC vs Grid 비교 ---
# is_bg_raw: dilation 전 원본 배경 마스크 (Step 2.5에서 보존)
grid_components = grid_resplit_rowwise(is_bg_raw, transparent_img, min_area)

print(f'  CC: {len(components)} elements, Grid: {len(grid_components)} elements')
if len(grid_components) > len(components):
    print(f'  -> Grid adopted ({len(grid_components)} > {len(components)})')
    components = grid_components
else:
    print(f'  -> CC kept ({len(components)} >= {len(grid_components)})')
```

**is_bg_raw 생성 위치**: Step 2.5 이후, dilation 직전에 보존한다.

```python
# Step 2.5 끝에서:
is_bg_raw = is_bg.copy()  # Grid용 원본 보존
is_bg = binary_dilation(is_bg, iterations=2)  # 투명화용 dilation
```

---

## 방법 B: 실제 투명 배경 처리

이미지 알파 채널에 이미 투명 영역이 있는 경우.

```python
mask = arr[:,:,3] > 10
labeled, num_features = ndimage.label(mask)
slices = ndimage.find_objects(labeled)
# 이후 방법 A의 Step 4와 동일
```

---

## 출력 규칙

### 파일 저장 위치
- 원본 이미지와 같은 폴더에 `extracted/` 하위 디렉토리 생성
- 예: `Button/image.png` → `Button/extracted/`

### 파일 네이밍
- 이미지에 텍스트 레이블이 보이면: 해당 텍스트를 파일명에 사용 (예: `btn_gold.png`)
- 레이블이 없으면: `element_01.png`, `element_02.png` 순번 사용
- 파일명은 소문자, 공백은 언더스코어로 치환

### 전체 이미지 크기의 컴포넌트 제외
- Connected Component 중 원본 이미지 전체 크기에 해당하는 것은 제외 (배경 텍스트/레이블 등)
- 판단 기준: 컴포넌트의 bounding box가 원본의 90% 이상이면 제외

---

## 사전 요구사항

### Python 실행 명령어
- **반드시 `python`을 사용** (`python3` 사용 금지)
- `python3`는 Windows Store 스텁으로 pip 패키지를 찾지 못함
- 실제 Python 3.10: `/c/Users/USER/AppData/Local/Programs/Python/Python310/python`

### 필요 패키지: `Pillow`, `numpy`, `scipy`

```bash
pip install Pillow numpy scipy
```

---

## 실행 절차

1. **이미지 읽기**: Read 도구로 이미지를 시각적으로 확인
2. **배경 유형 판별**: Step 0 로직으로 방법 A/B 선택, 사용자에게 보고
3. **Python 스크립트 실행**: 선택된 방법의 파이프라인을 Bash에서 python으로 실행
4. **결과 확인**: 추출된 개별 이미지를 Read 도구로 시각 확인 (최대 4개 샘플)
5. **Unity Import 설정**: MCP execute_code로 추출된 PNG를 Sprite (Single) 모드로 설정
6. **결과 보고**: 추출된 파일 목록, 크기, 저장 위치를 테이블로 표시

---

## Unity Sprite Import 설정 (Step 5)

추출된 모든 PNG 파일을 Unity에서 Sprite 텍스처로 사용할 수 있도록 Import 설정을 변경한다.
**사전 조건**: Unity Editor가 프로젝트를 열고 있어야 함.

### AssetDatabase.ImportAsset 후 TextureImporter 설정

```csharp
// MCP execute_code로 실행할 C# 코드
string[] paths = new string[] {
    "Assets/.../extracted/element_01.png",
    "Assets/.../extracted/element_02.png",
    // ... 추출된 모든 파일
};

foreach (var path in paths)
{
    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer != null)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.SaveAndReimport();
    }
}
```

### MCP execute_code 실행 예시

`execute_code(action="execute", compiler="roslyn")` MCP 도구로 위 C# 코드를 실행한다. 멀티라인 코드를 그대로 전달할 수 있다.

---

## 엣지 케이스 처리

| 상황 | 처리 |
|------|------|
| 배경색이 균일하지 않음 (그라데이션) | threshold를 높이거나 `--bg` 옵션으로 직접 지정 |
| 요소 간 연결됨 (gap 없음) | 사용자에게 알리고 수동 crop 좌표 요청 |
| 텍스트 레이블이 요소에 포함됨 | 큰 컴포넌트(전체 90%+)만 제외, 나머지는 포함 |
| 실제 투명 배경 | 방법 B: 알파 채널 기반으로 분리 |
| 매우 작은 아티팩트 | `--min-size`로 필터링 (기본 50000px) |

---

## 알려진 배경색 프리셋

| 이름 | RGB | 설명 |
|------|-----|------|
| `pink` / `magenta` | (255, 0, 255) | AI 이미지 생성 시 가장 흔한 배경색 |
| `white` | (255, 255, 255) | 흰색 배경 |
| `black` | (0, 0, 0) | 검은색 배경 |
| `green` | (0, 255, 0) | 크로마키 녹색 |
