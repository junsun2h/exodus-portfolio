---
name: "debug-unity-log"
description: "C# 코드 작성/리팩토링/버그수정 시 Unity 로그 API를 어떤 것으로 쓸지 선택하는 규칙. 디버깅/추적 로그는 EditorLogCollector.Log 또는 EditorLogCollectorClient.Log, 오류만 Debug.LogError, Debug.Log/Warning 금지. 새 로그 호출을 추가하거나 기존 로그를 고칠 때만 적용."
---

# Unity Debug Log Skill

Unity 프로젝트에서 디버그 로그는 **오류 상황에만** 사용합니다.

## ⚠️ 핵심 규칙 (MANDATORY)

### 1. 기능 개발 중 디버깅/추적 로그 → EditorLogCollector 필수 사용

**CRITICAL**: 기능 개발, 디버깅, 데이터 추적 시 `Debug.Log`가 아닌 **EditorLogCollector 계열**을 반드시 사용해야 합니다.

| 상황 | 사용할 API | 예시 |
|------|-----------|------|
| 에디터/서버 측 디버깅 | `EditorLogCollector.Log()` | 변수 상태, 함수 호출 추적 |
| 클라이언트 측 디버깅 | `EditorLogCollectorClient.Log()` | UI 이벤트, 네트워크 응답 추적 |
| **오류/예외 상황만** | `Debug.LogError()` | null 체크 실패, 데이터 로드 실패 |

**금지 사항**:
- ❌ `Debug.Log()` - 기능 개발/디버깅에 사용 금지
- ❌ `Debug.LogWarning()` - 사용 금지
- ❌ 에러가 아닌 상황에 `Debug.LogError()` 사용 금지

### 2. Debug.LogError는 진짜 오류에만
- null 체크 실패
- 데이터 로드 실패
- 예외 상황
- 치명적 오류

### 3. 일반 로그는 명시적 요청 시에만
사용자가 명시적으로 Debug.Log 추가를 요청한 경우에만 예외

## 좋은 예 vs 나쁜 예

Before/After 코드 예시는 [examples.md](./examples.md) 참조.

## 사용하지 말아야 할 때

- **오류 전용 `Debug.LogError` 사용 케이스**: null 체크 실패, 데이터 로드 실패, 예외/치명적 오류처럼 이미 `Debug.LogError`가 정답인 상황은 이 규칙으로 바꿀 대상이 아니다.
- **새 로그 호출을 추가하지 않는 작업**: 로그와 무관한 로직 변경, 단순 읽기/탐색, 기존 `Debug.LogError`를 그대로 두는 리팩토링.
- **사용자가 명시적으로 `Debug.Log` 추가를 요청한 경우**: 요청 그대로 따른다.

## 요약: 어떤 로그를 써야 하나?

```
디버깅/추적 필요? → EditorLogCollector.Log() 또는 EditorLogCollectorClient.Log()
진짜 오류인가?   → Debug.LogError()
그 외?          → 로그 작성하지 않음
```
