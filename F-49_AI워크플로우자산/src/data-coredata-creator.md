---
description: 새 CoreData 추가 시 필요한 모든 파일 자동 생성 및 수정
---

# CoreData Creator Skill

이 skill은 **새로운 CoreData**를 추가할 때 필요한 모든 파일을 자동으로 생성하고 수정합니다.

## 트리거 키워드

- `/data-coredata-creator`
- "새 CoreData 추가"
- "CoreData 생성"

---

## 필수 입력 정보

사용자에게 다음 정보를 **반드시** 확인해야 합니다:

| 항목 | 설명 | 예시 |
|------|------|------|
| `name` | CoreData 이름 (단수형, PascalCase) | `Reward` |
| `pluralName` | 복수형 이름 | `Rewards` |
| `properties[]` | 프로퍼티 정의 배열 | 아래 참조 |

### 프로퍼티 정의 형식

```
{
  propertyName: "RewardHistory",      // C# 프로퍼티명 (PascalCase)
  type: "Dictionary<EReward, int>",   // C# 타입
  description: "보상 수령 기록",       // 설명
  hasJsonConverter: true              // DictionaryWithEnumKeyConverter 필요 여부
}
```

---

## 네이밍 컨벤션 (자동 변환)

| 용도 | 변환 규칙 | 예시 (name=Reward, pluralName=Rewards) |
|------|----------|----------------------------------------|
| C# 상수 | `[NAME]` (대문자) | `REWARD` |
| CoreDataKey | `[PluralName]` | `Rewards` |
| CoreData 클래스 | `CoreData_[PluralName]` | `CoreData_Rewards` |
| Data 래퍼 클래스 | `[Name]Data` | `RewardData` |
| C# 프로퍼티 | `[name]Data` (소문자 시작) | `rewardData` |
| TS 필드 | `[pluralNameLower]` | `rewards` |
| TS 상수 키 | `COREDATAKEY_[NAME]` | `COREDATAKEY_REWARD` |

---

## 코드생성기 실행 전 주석 처리 필수

**Unity 코드생성기는 프로젝트가 컴파일 가능한 상태에서만 실행됩니다!**

이 skill이 생성하는 TypeScript 코드는 다음 타입들을 참조합니다:
- `CoreData_[PluralName]` - CoreData.ts (코드생성기 생성)
- `[PluralName]_Migrations_Generated` - 마이그레이션 파일 (코드생성기 생성)

**따라서 TypeScript 파일 수정 시 해당 부분을 주석 처리해야 합니다.**

---

## 자동 생성/수정 파일 목록

### C# 클라이언트 (Unity)

| 파일 | 작업 | 설명 |
|------|------|------|
| `Assets/Source/Logic/Data/CoreData/[Name]CoreData.cs` | **신규 생성** | CoreData 클래스 + Data 래퍼 |
| `Assets/Source/Logic/Data/CoreData/UserCoreData.cs` | **수정 (6곳)** | 상수, 배열, 프로퍼티, 생성자, Dispose, Dictionary |

### TypeScript 서버 (Firebase Functions)

| 파일 | 작업 | 설명 |
|------|------|------|
| `FirebaseCLI/functions/src/Data/Constants/FirestorePaths.ts` | **수정** | CoreDataKey 상수 추가 |
| `FirebaseCLI/functions/src/API/Factory/CoreDataFactory.ts` | **수정 (5곳)** | import, 필드, ExcludeKey, loadRequest, updateToDB |
| `FirebaseCLI/functions/src/API/Migration/CoreDataMigration.ts` | **수정 (3곳)** | import, export, MigrationFunc |

### 자동 생성 파일 (Unity 코드생성기) - 수동 수정 금지

| 파일 | 생성 주체 |
|------|----------|
| `Assets/Source/Logic/Data/Generated/CoreData_ParseJsonNode.cs` | CodeGenerator_CoreData_ParseJsonNode |
| `FirebaseCLI/functions/src/Data/Generated/CoreData.ts` | CodeGenerator_CoreData |
| `FirebaseCLI/functions/src/API/Migration/Generated/*_migrations.generated.ts` | CodeGenerator_CoreDataMigration |

---

## 코드 템플릿

### C# CoreData 클래스 (`[Name]CoreData.cs`)

```csharp
using Newtonsoft.Json;
using SimpleJSON;
using System.Collections.Generic;

namespace PX
{
    public partial class CoreData_{PluralName} : CommonCoreData
    {
        // {프로퍼티 설명}
        [JsonConverter(typeof(DictionaryWithEnumKeyConverter))]  // Dictionary인 경우만
        public {PropertyType} {PropertyName} { get; private set; }
    }

    public class {Name}Data : Item
    {
        public CoreData_{PluralName} CoreData { get; private set; } = null;

        public {Name}Data(string InUUID)
        {
            UUID = InUUID;

            CoreData = new CoreData_{PluralName}();
        }

        ~{Name}Data()
        {

        }

        public override bool ParseJsonNode(string InDataKey, JSONNode InNode, ECoreDataChangeType InChangeType)
        {
            return CoreData.ParseJsonNode(InDataKey, InNode, InChangeType);
        }
        public override bool ChangedJsonNode(JSONNode InNode, ECoreDataChangeType InChangeType)
        {
            return CoreData.ChangedJsonNode(InNode, InChangeType);
        }
    }
}
```

### UserCoreData.cs 수정 패턴 (6곳)

#### 1. 상수 추가 (line ~31, CONSTELLATION 다음)
```csharp
public const string {NAME} = "{PluralName}";
```

#### 2. FIREBASE_COREDATA_TYPES 배열 (line ~59, 배열 끝에)
```csharp
{NAME},
```

#### 3. 프로퍼티 추가 (line ~79, constellationData 다음)
```csharp
public {Name}Data {name}Data { get; private set; } = null;
```

#### 4. 생성자 초기화 (line ~106, constellationData 초기화 다음)
```csharp
{name}Data = new {Name}Data(InUUID);
```

#### 5. ManagedDispose (line ~151, constellationData Dispose 다음)
```csharp
{name}Data.Dispose();
{name}Data = null;
```

#### 6. ConvertUserDataToDictionary (line ~237, CONSTELLATION 다음)
```csharp
[UserData.{NAME}] = userData.{name}Data.CoreData,
```

---

### FirestorePaths.ts 추가 패턴

CoreDataKey 객체 끝에 추가:
```typescript
COREDATAKEY_{NAME}: "{PluralName}",
```

---

### CoreDataFactory.ts 수정 패턴 (5곳)

#### 1. import 추가 (line 5, 기존 import에 추가 - 코드생성기 실행 후 주석 해제)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
// CoreData_{PluralName}
```

#### 2. CoreDatas 클래스 필드 (line ~33, titles 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
// {pluralNameLower}: CoreData_{PluralName} | null = null;
```

#### 3. ExcludeCoreDataKey (line ~61, TITLE 다음)
```typescript
{NAME}: "{pluralNameLower}",
```

#### 4. loadRequestObject switch case (line ~281, COREDATAKEY_TITLE case 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
/*
case CoreDataKey.COREDATAKEY_{NAME}:
    {
        this._datasOriginal.{pluralNameLower} = plainToInstance(CoreData_{PluralName}, docData);
        this.datas.{pluralNameLower} = utilBasic.deepClone(this._datasOriginal.{pluralNameLower});
    }
    break;
*/
```

#### 5. updateToUserDBOnlyDiff switch case (line ~361, CoreData_Titles case 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
/*
case "CoreData_{PluralName}":
    updateStrMap.set(CoreDataKey.COREDATAKEY_{NAME}, instanceToPlain(this.datas.{pluralNameLower}));
    break;
*/
```

---

### CoreDataMigration.ts 수정 패턴 (3곳)

#### 1. import (line ~25, Currencies_Migrations_Generated 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
// import { {PluralName}_Migrations_Generated } from "./Generated/{PluralName}_migrations.generated";
```

#### 2. export (line ~51, Constellation_Migrations 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
// export const {Name}_Migrations = { ...{PluralName}_Migrations_Generated };
```

#### 3. MigrationFunc 등록 (line ~74, migration_Statistics 다음)
```typescript
// TODO: 코드생성기 실행 후 주석 해제
// migration_{Name}: createMigrationFunction(CoreDataKey.COREDATAKEY_{NAME}, {Name}_Migrations),
```

---

## 실행 절차

### Step 1: 정보 수집
사용자에게 필수 입력 정보 확인:
- CoreData 이름 (단수형, PascalCase)
- 복수형 이름
- 프로퍼티 목록 (이름, 타입, 설명)

### Step 2: C# 파일 생성
1. `[Name]CoreData.cs` 신규 생성
2. `UserCoreData.cs` 6곳 수정
3. `git add` 로 스테이징

### Step 3: TypeScript 파일 수정 (주석 처리 상태로)
1. `FirestorePaths.ts` - CoreDataKey 추가
2. `CoreDataFactory.ts` - 5곳 수정 (주석 처리)
3. `CoreDataMigration.ts` - 3곳 수정 (주석 처리)
4. `git add` 로 스테이징

### Step 4: 수동 작업 안내

사용자에게 다음 안내:

```
## 코드생성기 실행 (Unity 에디터)

1. Unity 에디터에서 메뉴 실행:
   - PX → CodeGenerator → **Generate CoreData**
   - PX → CodeGenerator → **Generate CoreDataMigration**

2. 생성 확인:
   - Assets/Source/Logic/Data/Generated/CoreData_ParseJsonNode.cs
   - FirebaseCLI/functions/src/Data/Generated/CoreData.ts
   - FirebaseCLI/functions/src/API/Migration/Generated/{PluralName}_migrations.generated.ts

## 주석 해제 요청

코드생성기 실행 후, Claude에게 다음 요청:
"[CoreData이름] 관련 TypeScript 주석 해제해줘"

## 빌드 검증

```bash
# Unity: 자동 컴파일 확인
# TypeScript:
cd FirebaseCLI/functions
npm run build
```
```

---

## 검증 체크리스트

### 코드생성기 실행 전
- [ ] `[Name]CoreData.cs` 파일 생성 확인
- [ ] `UserCoreData.cs` 6곳 수정 확인
- [ ] `FirestorePaths.ts` CoreDataKey 추가 확인
- [ ] `CoreDataFactory.ts` 주석 처리 상태로 5곳 수정 확인
- [ ] `CoreDataMigration.ts` 주석 처리 상태로 3곳 수정 확인
- [ ] Unity 컴파일 성공 확인
- [ ] TypeScript 빌드 성공 확인 (주석 상태)

### 코드생성기 실행 후
- [ ] Generated/CoreData.ts에 `CoreData_{PluralName}` 타입 생성 확인
- [ ] Generated/{PluralName}_migrations.generated.ts 생성 확인
- [ ] TypeScript 주석 해제 완료
- [ ] TypeScript 빌드 성공 확인 (`npm run build`)
- [ ] Unity 빌드 성공 확인

---

## Critical Files

| 파일 | 용도 |
|------|------|
| `Assets/Source/Logic/Data/CoreData/TitleCoreData.cs` | CoreData 클래스 템플릿 참조 |
| `Assets/Source/Logic/Data/CoreData/UserCoreData.cs` | UserData 통합 수정 위치 |
| `FirebaseCLI/functions/src/Data/Constants/FirestorePaths.ts` | CoreDataKey 상수 |
| `FirebaseCLI/functions/src/API/Factory/CoreDataFactory.ts` | Factory 패턴 |
| `FirebaseCLI/functions/src/API/Migration/CoreDataMigration.ts` | Migration 등록 |
