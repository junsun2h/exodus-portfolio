---
description: AchieveItemConfigMap의 CS↔TS 양방향 동기화
---

# Sync AchieveItemConfig Skill

CS(Unity)와 TS(Firebase Functions) 간 AchieveItemConfigMap을 동기화합니다.

## 트리거 및 인자

### 사용법

```
/sync-achieveitemconfig cs2ts    # CS → TS 동기화
/sync-achieveitemconfig ts2cs    # TS → CS 동기화
/sync-achieveitemconfig          # 인자 없음 → 방향 물어봄
```

### 인자 처리 규칙

1. **인자가 `cs2ts`**: C# 파일 기준으로 TypeScript 파일을 동기화
2. **인자가 `ts2cs`**: TypeScript 파일 기준으로 C# 파일을 동기화
3. **인자가 없음**: AskUserQuestion으로 방향을 물어본 후 진행

```
인자 없을 때 질문:
  "동기화 방향을 선택해주세요."
  옵션:
    - "CS → TS" (C# 수정됨, TypeScript에 반영)
    - "TS → CS" (TypeScript 수정됨, C#에 반영)
```

---

## 대상 파일

| 언어 | 파일 경로 |
|------|----------|
| **CS** | `Assets/Source/Logic/Manager/GameManager/AchieveItemConfigMap.cs` |
| **TS** | `FirebaseCLI/functions/src/Data/AchieveItemConfigMap.ts` |

---

## 실행 절차

### Step 1: 방향 결정

- 인자가 있으면 해당 방향으로 즉시 진행
- 인자가 없으면 AskUserQuestion으로 방향 확인

### Step 2: 양쪽 파일 읽기

두 파일을 모두 읽어서 현재 상태를 파악합니다.

### Step 3: 차이 분석

EAchieveItem 키 목록을 비교하여:
- **추가된 항목**: 소스에만 있고 대상에 없는 항목
- **삭제된 항목**: 대상에만 있고 소스에 없는 항목
- **수정된 항목**: 양쪽 다 있지만 매핑이 다른 항목 (CoreData 소스 변경 등)

### Step 4: 변환 규칙에 따라 대상 파일 수정

아래 변환 규칙표를 사용하여 대상 파일을 업데이트합니다.

### Step 5: 빌드 검증 (TS 방향인 경우)

TS 파일을 수정한 경우:
```bash
"C:\Program Files\nodejs\npm.cmd" run build
```

---

## 변환 규칙

### 1. CoreData 소스 매핑

| CS userData 프로퍼티 | TS coreDataProperty | TS coreDataKey |
|---------------------|---------------------|----------------|
| `accountData.CoreData` | `"account"` | `CoreDataKey.COREDATAKEY_ACCOUNT` |
| `playerData.CoreData` | `"player"` | `CoreDataKey.COREDATAKEY_PLAYER` |
| `statisticsData.CoreData` | `"statistics"` | `CoreDataKey.COREDATAKEY_STATISTICS` |
| `stageData.CoreData` | `"stages"` | `CoreDataKey.COREDATAKEY_STAGE` |
| `nebulaData.CoreData` | `"nebulas"` | `CoreDataKey.COREDATAKEY_NEBULA` |
| `constellationData.CoreData` | `"constellations"` | `CoreDataKey.COREDATAKEY_CONSTELLATION` |
| `questData.CoreData` | `"quests"` | `CoreDataKey.COREDATAKEY_QUEST` |
| `productData.CoreData` | `"products"` | `CoreDataKey.COREDATAKEY_PRODUCT` |

### 2. 필드 접근자 변환 (CS → TS)

| CS 패턴 | TS fieldAccessor 패턴 |
|---------|----------------------|
| `(core as CoreData_Xxx).FieldName.Value` | `coreData.datas.xxx.FieldName.GetValue()` |
| `(core as CoreData_Player).Growths[EMod.xxx].Level.Value` | `coreData.datas.player.Growths.get(EMod.xxx).Level.GetValue()` |
| `(core as CoreData_Player).Reinforces[EMod.xxx].Level.Value` | `coreData.datas.player.Reinforces.get(EMod.xxx).Level.GetValue()` |
| `(core as CoreData_Stages).BestStages[EStage.xxx].Value` | `coreData.datas.stages.BestStages.get(EStage.xxx).GetValue()` |
| `(core as CoreData_Constellations).Constellations[EConstellation.xxx].Level.Value` | `coreData.datas.constellations.Constellations.get(EConstellation.xxx).Level.GetValue()` |
| `(core as CoreData_Nebulas).Nebulas.TryGetValue(ENebula.xxx, out var nebula) ? nebula.Constellations.Count : 0` | `coreData.datas.nebulas.Nebulas.get(ENebula.xxx)?.Constellations.size ?? 0` |
| `(int)(core as CoreData_Player).Awaken.AwakenGrade` | `coreData.datas.player.Awaken.AwakenGrade` |
| `(core as CoreData_Constellations).IsHeir.Value ? 1 : 0` | `coreData.datas.constellations.IsHeir.GetValue() ? 1 : 0` |
| `(core as CoreData_Account).ActivityInfo.AttendanceDays.Value` | `coreData.datas.account.ActivityInfo.AttendanceDays.GetValue()` |
| `(core as CoreData_Products).ProductPass[EProductPass.xxx].ReceivedFreeRewardCount.Value` | `coreData.datas.products.ProductPass.get(EProductPass.xxx).ReceivedFreeRewardCount.GetValue()` |

### 3. 필드 setter 변환 (CS → TS)

| CS 패턴 | TS fieldSetter 패턴 |
|---------|---------------------|
| 단순 `.Value` 필드 | `(coreData: any, value: number) => coreData.datas.xxx.FieldName.Set(value)` |
| Dictionary/Map `.get(key).Level.GetValue()` | `(coreData: any, value: number) => coreData.datas.xxx.Map.get(key).Level.Set(value)` |
| 집계/Sum 계산 (nebula total 등) | `fieldSetter: null` |
| boolean → int 변환 (IsHeir, IsTrans) | `fieldSetter: null` |
| Awaken.AwakenGrade (직접 할당) | `(coreData: any, value: number) => { coreData.datas.player.Awaken.AwakenGrade = value; }` |

### 4. Nebula Total 패턴 변환 (복잡한 접근자)

**CS 패턴** (LINQ Sum):
```csharp
core => {
    var nebulas = core as CoreData_Nebulas;
    if (!nebulas.Nebulas.TryGetValue(ENebula.nebula_xxx, out var nebula)) return 0;
    return nebula.Constellations.Values.Sum(c => c.Level.Value);
}
```

**TS 패턴** (for...of 루프):
```typescript
fieldAccessor: (coreData: any) => {
    const nebula = coreData.datas.nebulas.Nebulas.get(ENebula.nebula_xxx);
    if (!nebula) return 0;
    let total = 0;
    for (const constellation of nebula.Constellations.values()) {
        total += constellation.Level.GetValue();
    }
    return total;
},
fieldSetter: null,
```

### 5. 전체 성좌 레벨 합산 패턴

**CS 패턴**:
```csharp
core => {
    var nebulas = core as CoreData_Nebulas;
    return nebulas.Nebulas.Values.Sum(n => n.Constellations.Values.Sum(c => c.Level.Value));
}
```

**TS 패턴**:
```typescript
fieldAccessor: (coreData: any) => {
    let total = 0;
    for (const nebula of coreData.datas.nebulas.Nebulas.values()) {
        for (const constellation of nebula.Constellations.values()) {
            total += constellation.Level.GetValue();
        }
    }
    return total;
},
fieldSetter: null,
```

### 6. 타입 변환 요약 (CS → TS)

| CS 문법 | TS 문법 |
|---------|---------|
| `Dictionary[key]` | `Map.get(key)` |
| `.Value` (ReactiveProperty) | `.GetValue()` |
| `.Count` (Dictionary) | `.size` (Map) |
| `TryGetValue(key, out var x) ? x.Prop : 0` | `Map.get(key)?.Prop ?? 0` |
| `.Values.Sum(x => ...)` | `for...of` 루프 + 수동 합산 |
| `(int)enumValue` | 직접 값 사용 |

### 7. 역방향 변환 (TS → CS)

위 규칙을 역으로 적용합니다:

| TS 패턴 | CS 패턴 |
|---------|---------|
| `coreData.datas.xxx.FieldName.GetValue()` | `(core as CoreData_Xxx).FieldName.Value` |
| `Map.get(key)` | `Dictionary[key]` |
| `.size` | `.Count` |
| `?.Prop ?? 0` | `TryGetValue(key, out var x) ? x.Prop : 0` |
| `for...of` 합산 | `.Values.Sum(x => ...)` |

**CS CoreData Provider 결정**: TS의 `coreDataProperty`로부터 CS의 `GameAPIUserManager.Instance.userData.{property}Data.CoreData` 를 결정합니다.

| TS coreDataProperty | CS CoreData Provider |
|--------------------|---------------------|
| `"account"` | `GameAPIUserManager.Instance.userData.accountData.CoreData` |
| `"player"` | `GameAPIUserManager.Instance.userData.playerData.CoreData` |
| `"statistics"` | `GameAPIUserManager.Instance.userData.statisticsData.CoreData` |
| `"stages"` | `GameAPIUserManager.Instance.userData.stageData.CoreData` |
| `"nebulas"` | `GameAPIUserManager.Instance.userData.nebulaData.CoreData` |
| `"constellations"` | `GameAPIUserManager.Instance.userData.constellationData.CoreData` |
| `"quests"` | `GameAPIUserManager.Instance.userData.questData.CoreData` |
| `"products"` | `GameAPIUserManager.Instance.userData.productData.CoreData` |

---

## CS 엔트리 템플릿

```csharp
{ EAchieveItem.{ACHIEVE_ITEM_KEY}, new AchieveItemConfig(
    () => GameAPIUserManager.Instance.userData.{dataProperty}Data.CoreData,
    core => (core as CoreData_{PluralName}).{FieldPath}
) },
```

## TS 엔트리 템플릿

### 단순 필드 (setter 있음)
```typescript
[EAchieveItem.{ACHIEVE_ITEM_KEY}]: {
    fieldAccessor: (coreData: any) => coreData.datas.{coreDataProperty}.{FieldPath}.GetValue(),
    fieldSetter: (coreData: any, value: number) => coreData.datas.{coreDataProperty}.{FieldPath}.Set(value),
    coreDataProperty: "{coreDataProperty}",
    coreDataKey: CoreDataKey.COREDATAKEY_{KEY},
},
```

### 읽기 전용 (setter null)
```typescript
[EAchieveItem.{ACHIEVE_ITEM_KEY}]: {
    fieldAccessor: (coreData: any) => {/* 복잡한 접근 로직 */},
    fieldSetter: null,
    coreDataProperty: "{coreDataProperty}",
    coreDataKey: CoreDataKey.COREDATAKEY_{KEY},
},
```

---

## TS Import 확인

TS 파일 상단에 사용된 enum/타입이 import 되어 있는지 확인:

```typescript
import { AchieveItemConfig } from "../API/CoreData/AchieveItem";
import { EAchieveItem, EMod, EProductPass, EStage, EConstellation, ENebula } from "./Generated/CommonEnum";
import { CoreDataKey } from "./Constants/FirestorePaths";
```

새로운 Enum 타입이 추가될 경우 import에도 추가해야 합니다.

---

## TS 파일의 TODO/주석 보존

TS 파일에는 아직 미구현된 항목에 대한 TODO 주석이 있습니다:
- `// TODO: EProductPackage에 productpackage_daily_free / productpackage_weekly_free 값이 없음`
- `// TODO: Stage_ClearCount_MainEndless 필드가 CoreData에 없음`
- `// TODO: Stage_FailCount_* 필드들이 CoreData에 없음`

동기화 시 이런 TODO 주석은 보존해야 합니다. CS에 해당 항목이 있더라도 TS에서 구현 불가능한 경우 TODO로 남겨둡니다.

---

## 검증 체크리스트

- [ ] 양쪽 EAchieveItem 키 목록이 일치하는가 (TODO 항목 제외)
- [ ] 각 항목의 coreDataProperty / CoreData 소스가 일치하는가
- [ ] 필드 접근 경로가 동일한 데이터를 가리키는가
- [ ] TS 빌드 성공 (TS 수정 시): `"C:\Program Files\nodejs\npm.cmd" run build`
- [ ] 새로운 Enum import 추가 여부 확인
- [ ] TS 섹션 주석(===== Section =====)이 적절히 유지되는가
