# Unity Debug Log — Before/After 코드 예시

이 문서는 `skill.md`의 로그 API 선택 규칙을 실제 코드에 적용하는 예시 모음입니다.

## ❌ 절대 하지 말 것

```csharp
public void Attack(Enemy enemy)
{
    Debug.Log("공격 시작");           // ❌ 금지! EditorLogCollector 사용
    int damage = CalculateDamage();
    Debug.Log($"데미지: {damage}");   // ❌ 금지! EditorLogCollector 사용
    Debug.LogWarning("주의!");        // ❌ Warning 금지
    enemy.TakeDamage(damage);
}

// ❌ 에러가 아닌데 LogError 사용 - 금지!
Debug.LogError($"현재 HP: {hp}");     // ❌ 이건 에러가 아님!
```

## ✅ 기능 개발/디버깅 시 — EditorLogCollector 사용

```csharp
public void Attack(Enemy enemy)
{
    EditorLogCollector.Log("공격 시작");           // ✅ 디버깅용
    int damage = CalculateDamage();
    EditorLogCollector.Log($"데미지: {damage}");   // ✅ 변수 추적
    enemy.TakeDamage(damage);
}

// 클라이언트 측 디버깅
public void OnButtonClick()
{
    EditorLogCollectorClient.Log("버튼 클릭됨");   // ✅ 클라이언트 디버깅
    EditorLogCollectorClient.Log($"선택된 아이템: {itemId}");
}
```

## ✅ 진짜 오류 상황에만 — Debug.LogError 사용

```csharp
public void LoadSkillData(int skillId)
{
    SkillData data = Resources.Load<SkillData>($"Skills/{skillId}");
    if (data == null)
    {
        Debug.LogError($"스킬 데이터 없음: {skillId}");  // ✅ 오류만
        return;
    }
}

public void Initialize()
{
    if (battleManager == null)
    {
        Debug.LogError("BattleManager가 설정되지 않음");  // ✅ 오류만
        return;
    }
}
```
