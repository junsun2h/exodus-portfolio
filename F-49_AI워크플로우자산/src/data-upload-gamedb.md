---
description: 새 시트 추가 → SheetDefine → GameDB 구축 프로세스 자동화
---

# Upload GameDB Skill

이 skill은 **BGDatabase 시트**를 추가할 때 필요한 모든 코드 파일을 자동으로 생성합니다.

---

## 🚫 [MANDATORY] Generated/ 파일 직접 편집 금지 + Generate 순서 준수

**Generated/ 파일은 오직 CodeGenerator_* 실행 결과로만 갱신될 수 있다. Edit/Write 툴로 직접 수정하면 안 된다.**

대상 경로:
- `FirebaseCLI/functions/src/Data/Generated/*.ts` (GameDBData, CommonEnum, CoreData, CoreData_ParseJsonNode 등)
- `Assets/Source/Repository/generated/*.cs`
- `Assets/Source/Logic/Manager/GameDBManager/Generated/*.cs`
- `Assets/Source/Logic/Data/Generated/*.cs`
- 기타 `/Generated/`, `/generated/` 하위 자동 생성 파일

### 금지 행동

- ❌ `Edit`/`Write` 툴로 Generated/ 파일을 직접 수정 (선제 반영 포함)
- ❌ C# 원본 컴파일 완료 **전에** CodeGenerator 호출 — 리플렉션이 구버전 Type 을 읽어 잘못된 코드가 생성됨

### 허용 행동 — Claude가 Generate 를 직접 실행해도 된다 (순서 준수 시)

Claude는 아래 순서를 **정확히** 지키는 한 Generate 를 직접 호출할 수 있다. 사용자에게 수동 안내로 넘길 필요 없음.

### 작업 순서 (MANDATORY) — Generate_All 을 두 번 돌려야 한다

```
1. Claude: 원본 C# 소스 수정 (SheetDataDefine_*.cs, GameDBServer_*.cs, Enum 추가 등)
2. Claude: mcp__unityMCP__refresh_unity → 컴파일 트리거
3. Claude: 컴파일 완료 대기 + read_console 로 에러 0 확인
4. Claude: Generate_All 호출 (1차) — GameDBClient.cs, GameDBData.ts 등 갱신
5. Claude: refresh_unity → 1차가 만든 GameDBClient.cs 를 컴파일 완료
6. Claude: 컴파일 에러 0 확인
7. Claude: Generate_All 호출 (2차) — **이번에는 GameDBClient_ParseJsonNode.cs 가 갱신됨**
8. Claude: refresh_unity → 2차 결과를 컴파일
9. Claude: 컴파일 에러 0 확인
10. Claude: TypeScript 빌드 검증 ("C:\Program Files\nodejs\npm.cmd" run build)
11. 사용자: (필요 시) Firebase Editor → Upload GameDB + DataBundle 수동 실행
```

### 왜 2회인가 (핵심 메커니즘)

`CodeGenerateEditor.cs` 주석(line 210-218)에 명시된 이슈:

- `Generate_GameDBClient()` 가 `GameDBClient.cs` 를 새로 쓰지만, 같은 Generate_All 내에서 바로 이어지는 `Generate_GameDBClient_ParseJsonNode()` 실행 시점에는 그 파일이 **아직 컴파일 전**이라 `AppDomain.CurrentDomain.GetAssemblies()` 에 안 보인다.
- `CodeGenerator_GameDB_Client_ParseJsonNode` 는 `typeof(GameDBData_Client)` 기반 리플렉션을 쓰는데, 구버전 어셈블리만 본다 → 새 필드 파싱 로직이 누락된 채 생성됨.
- 1차 실행 출력 예: `(GameDBClient.cs) insert: 1, (GameDBClient_ParseJsonNode.cs) insert: 0`
- 2차 실행 시에는 1차가 만든 `GameDBClient.cs` 가 컴파일된 상태라, ParseJsonNode 가 최신 타입을 읽어 누락분을 채운다.

`Generate_GameDBData` 는 서버 타입(`typeof(GameDBData_Server)`) 기반이고 서버 타입은 사용자가 1번 단계에서 수정한 그대로이므로 1차에서 바로 반영된다. **2차가 필수인 건 GameDBClient_ParseJsonNode.cs 때문**.

### 핵심 제약

- 3번(첫 컴파일 완료 확인)을 건너뛰면 리플렉션이 수정 전 서버 Type 을 읽어 전체가 구버전.
- 5번(1차 후 재컴파일)을 건너뛰고 바로 2차를 돌리면 ParseJsonNode 가 또 구버전으로 생성됨.
- Generate_All 은 반드시 "직전 생성물이 컴파일된 후"에만 다음 회차를 돌린다.

### Generate_All 호출 코드 (참고)

```csharp
// mcp__unityMCP__execute_code 로 실행
var windowType = typeof(PX.CodeGenerateEditor);
var window = UnityEditor.EditorWindow.CreateInstance(windowType) as UnityEditor.EditorWindow;
var awake = windowType.GetMethod("Awake", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
if (awake != null) awake.Invoke(window, null);
var mi = windowType.GetMethod("Generate_All", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
string result = mi.Invoke(window, null) as string;
UnityEngine.ScriptableObject.DestroyImmediate(window);
return result;
```

개별 생성기(`CodeGenerator_GameDBData` 단독 등)를 호출하는 것도 가능하지만, 의존성이 얽힌 경우(GameDBClient → GameDBClient_ParseJsonNode 등)는 Generate_All 순서를 따라야 안전.

---

## 트리거 키워드

- `/data-upload-gamedb`
- "새 시트 추가"
- "GameDB 생성"
- "시트 코드 생성"

---

## 필수 입력 정보

사용자에게 다음 정보를 **반드시** 확인해야 합니다:

| 항목 | 설명 | 예시 |
|------|------|------|
| `sheetName` | **BGDatabase 시트명 (정확한 이름 필수!)** | `ranking_reward` |
| `enumName` | Enum 타입명 (E 접두사) | `ERankingReward` |
| `className` | 클래스명 (PascalCase) | `RankingReward` |
| `category` | 기존 카테고리 또는 "new" | `Ranking` (신규) |
| `fields[]` | 필드 정의 배열 | 아래 참조 |

> ⚠️ **중요**: `sheetName`은 BGDatabase에 정의된 **정확한 시트명**을 사용자에게 확인 받아야 합니다. 추측하지 마세요!

### 필드 정의 형식

```
{
  bgFieldName: "reward1_currency",  // BGDatabase 실제 컬럼명 (필수!)
  propertyName: "Reward1Currency",  // C# 프로퍼티명 (PascalCase)
  type: "ECurrency",                // string, int, float, bool, Enum명
  nullable: true,                   // 선택적 필드 여부
  description: "보상1 재화 종류"
}
```

> ⚠️ **필드명 검증 필수**: `bgFieldName`은 BGDatabase의 **실제 컬럼명**이어야 합니다.
> 시트가 이미 존재하는 경우, 반드시 **Step 0의 Unity 스크립트**로 필드명을 확인 후 진행하세요.

---

## ⚠️ 중요: 코드생성기 실행 전 주석 처리 필수

**Unity 코드생성기는 프로젝트가 컴파일 가능한 상태에서만 실행됩니다!**

이 skill이 생성하는 코드는 다음 타입들을 참조합니다:
- `E{EnumName}` - CommonEnum.cs (코드생성기 생성)
- `GameDBClient_{Category}` - GameDBClient.cs (코드생성기 생성)
- `GameDB_Server_{ClassName}Map` - GameDBData.ts (코드생성기 생성)

**따라서 파일 생성/수정 시 해당 부분을 주석 처리해야 합니다.**

### 주석 처리 패턴

#### C# 파일 (신규 생성 파일)
```csharp
namespace PX
{
#if false // TODO: 코드생성기 실행 후 #if true로 변경
    public class SheetData{ClassName} : SheetDataBase
    {
        // ... 클래스 내용 ...
    }
#endif
}
```

#### C# 파일 (기존 파일 수정)
```csharp
// TODO: 코드생성기 실행 후 주석 해제
// public GameDBClient_{Category} GameDB_{Category} { get; set; }
```

#### TypeScript 파일
```typescript
// TODO: 코드생성기 실행 후 주석 해제 + import 문에 타입 추가
// {camelClassName}Map: GameDB_Server_{ClassName}Map | null = null;
```

---

## 자동 생성 파일 목록

### 클라이언트 (Unity C#)

| 파일 | 작업 | 조건 |
|------|------|------|
| `SheetDataDefine_{Category}.cs` | 신규 생성 or 추가 | 카테고리에 따라 |
| `SheetData.cs` | 프로퍼티 + CreateSheetDataFromBGDB 추가 | 항상 |
| `GameDBServer_{Category}.cs` | 신규 생성 or 추가 | 카테고리에 따라 |
| `GameDBClientManager.cs` | 프로퍼티 + 초기화 + **EDataBundleCategory 추가** | 신규 카테고리 시 |
| **`SheetDataManager.cs`** | **managerTypes + SheetDataManager_{Category} 클래스 추가** | **신규 카테고리 시 (필수!)** |

### 서버 (Firebase Functions)

| 파일 | 작업 | 조건 |
|------|------|------|
| `FirestorePaths.ts` | GameDBPath에 경로 상수 추가 | 항상 |
| `GameDBFactory.ts` | GameDBDatas 필드 + PATH_LOADERS 추가 | 항상 |
| **`DataBundle.ts`** | **EDataBundleCategory + getGameDBPathsForType 추가** | **신규 카테고리 시 (필수!)** |

### 코드생성기가 자동 생성하는 파일 (수동 수정 금지)

| 파일 | 생성 내용 |
|------|----------|
| `CommonEnum.cs` | E{EnumName}, E{EnumName}Type 등 |
| `GameDBClient.cs` | GameDBClient_{Category} 클래스 |
| `GameDBData.ts` | GameDB_Server_{ClassName}Map 타입 |

---

## 수동 작업 (사용자) - 순서 중요!

### Step 1: BGDatabase 작업
- [ ] **BGDatabase 시트 생성**: 지정된 컬럼과 함께 시트 생성
- [ ] **enums 시트 등록**: Enum 타입 등록

### Step 2: Unity 코드생성기 실행 (필수!)
- [ ] PX → CodeGenerator → **Generate CommonEnum**
- [ ] PX → CodeGenerator → **Generate GameDBClient**
- [ ] PX → CodeGenerator → **Generate GameDBData (TypeScript)**

### Step 3: 주석 해제 (Claude에게 요청)
코드생성기 실행 후, Claude에게 다음 요청:
```
"RankingReward 관련 주석 해제해줘"
```

또는 직접 수정:
- `SheetDataDefine_{Category}.cs`: `#if false` → `#if true`
- `SheetData.cs`: 주석 해제
- `GameDBServer_{Category}.cs`: `#if false` → `#if true`
- `GameDBClientManager.cs`: 주석 해제
- `GameDBFactory.ts`: 주석 해제 + import 문에 타입 추가

### Step 4: 빌드 검증
```bash
# Unity: 자동 컴파일 확인
# TypeScript:
npm run build  # FirebaseCLI/functions
```

---

## 코드 패턴

### SheetDataDefine 클래스

```csharp
public class SheetData{ClassName} : SheetDataBase
{
    public override void SetBGProperties()
    {
        // 1. PK (name) → Enum 파싱
        {EnumName} = GameUtility.StringToEnum<{EnumType}>.Parse(GetDBName());

        // 2. 필수 필드
        StringKeyName = GetBGEntityData<string>("str_name");

        // 3. Enum 필드
        SomeEnum = GameUtility.StringToEnum<ESomeEnum>.Parse(
            GetBGEntityData<string>("some_enum"));

        // 4. nullable 필드
        if (!string.IsNullOrEmpty(GetBGEntityData<string>("optional_field")))
        {
            OptionalField = GameUtility.StringToEnum<EOptionalEnum>.Parse(
                GetBGEntityData<string>("optional_field"));
        }
    }

    // Properties
    public {EnumType} {EnumName} { get; private set; }
    public string StringKeyName { get; private set; }
}
```

### GameDBServer Map 클래스

```csharp
[FirestoreData]
public partial class GameDB_Server_{ClassName}Map : GameDBData_Server
{
    [DictionaryEnumKey(typeof({EnumType}))]
    [FirestoreProperty]
    public Dictionary<string, GameDB_Server_{ClassName}> MapData { get; set; }
}
```

### GameDBServer Data 클래스

```csharp
[FirestoreData]
public partial class GameDB_Server_{ClassName} : GameDBData_Server
{
    [FirestoreProperty] public string StringKeyName { get; set; }
    [FirestoreProperty] public {FieldType} {FieldName} { get; set; }
}
```

### GameDBServer Disposable 클래스

```csharp
[FirestoreData]
public class GameDBServer_{Category} : IGameDBServer_Disposable
{
    public override void Dispose() { /* ... */ }
    public void Initialize(SheetData InSheetData) { /* ... */ }

    [FirestoreProperty]
    public GameDB_Server_{ClassName}Map {ClassName} { get; set; }

    public void Create{ClassName}() { /* SheetData → Map 변환 */ }
}
```

---

## FirestorePaths.ts 패턴

```typescript
export const GameDBPath = {
    // ...기존 경로...

    // ───── {Category} ─────
    {ClassName}: "GameDB/{Category}/{ClassName}/{ClassName}",
} as const;
```

---

## GameDBFactory.ts 패턴

### Import 문 (코드생성기 실행 후 추가)

```typescript
import {
    // ...기존 import...
    GameDB_Server_{ClassName}Map,  // 추가 필요!
} from "../../Data/Generated/GameDBData";
```

### GameDBDatas 필드

```typescript
export class GameDBDatas {
    // ...기존 필드...
    {camelClassName}Map: GameDB_Server_{ClassName}Map | null = null;
}
```

### PATH_LOADERS

```typescript
private static readonly PATH_LOADERS: Record<string, LoaderFn> = {
    // ...기존 로더...

    /* ───── {Category} ───── */
    [GameDBPath.{ClassName}]: (d, doc) => (d.{camelClassName}Map = plainToInstance(GameDB_Server_{ClassName}Map, doc)),
};
```

---

## 실행 절차

### Step 0: BGDatabase 필드명 검증 (필수!)

**BGDatabase 시트가 이미 존재하는 경우**, 정확한 필드명을 확인해야 합니다.

사용자에게 다음 스크립트를 Unity 에디터에서 실행하도록 요청:

```csharp
// Unity Console 또는 에디터 스크립트에서 실행
var meta = BGRepo.I["시트명"];  // 예: "ranking_reward"
foreach (var field in meta.Fields)
{
    Debug.Log($"Field: {field.Name}, Type: {field.ValueType}");
}
```

**출력 결과를 받아서** SheetDataDefine 코드의 `GetBGEntityData<T>("필드명")` 부분에 정확한 필드명을 사용합니다.

> ⚠️ **중요**: 필드명을 추측하지 마세요! BGDatabase의 실제 필드명과 코드가 불일치하면 런타임 에러가 발생합니다.

---

### Step 1-5: 코드 생성

1. **정보 수집**: 사용자에게 필수 입력 정보 확인 (시트명, Enum명, 클래스명, 카테고리, **실제 필드명**)
2. **기존 패턴 분석**: 해당 카테고리의 기존 파일 패턴 확인
3. **파일 생성/수정** (주석 처리 상태로):
   - 신규 파일: `#if false ... #endif`로 감싸서 생성
   - 기존 파일 수정: `// TODO: 코드생성기 실행 후 주석 해제` 주석 처리
4. **Git 스테이징**: `git add` 로 변경 파일 스테이징
5. **수동 작업 안내**: 사용자에게 다음 안내:
   - BGDatabase 시트 생성 (아직 없는 경우)
   - enums 시트 등록
   - 코드생성기 실행 (CommonEnum, GameDBClient, GameDBData)
   - **Claude에게 주석 해제 요청** 또는 직접 수정
   - 빌드 검증

---

## 검증 체크리스트

### 코드생성기 실행 전
- [ ] SheetDataDefine 클래스가 올바른 패턴을 따르는가
- [ ] SheetData.cs에 프로퍼티와 CreateSheetDataFromBGDB 호출이 추가되었는가
- [ ] GameDBServer Map/Data/Disposable 클래스가 올바른 패턴을 따르는가
- [ ] GameDBClientManager.cs에 프로퍼티와 초기화가 추가되었는가 (신규 카테고리)
- [ ] FirestorePaths.ts에 경로가 추가되었는가
- [ ] GameDBFactory.ts에 필드와 로더가 추가되었는가

### 코드생성기 실행 후
- [ ] CommonEnum.cs에 Enum이 생성되었는가
- [ ] GameDBClient.cs에 GameDBClient_{Category}가 생성되었는가
- [ ] GameDBData.ts에 GameDB_Server_{ClassName}Map이 생성되었는가
- [ ] GameDBFactory.ts import 문에 타입이 추가되었는가
- [ ] Unity 빌드 성공
- [ ] TypeScript 빌드 성공 (`npm run build`)
