---
name: "systematic-debugging"
description: "디버깅 시 추측-시도 루프 차단. 같은 시도 2번 실패하거나 5분 안에 안 풀리면 4단계 프로토콜(재현→격리→추적→근본원인) 발동."
---

# Systematic Debugging Skill

추측 기반 디버깅을 막고 근본 원인까지 추적하기 위한 4단계 프로토콜. superpowers/systematic-debugging 차용.

## 절대 원칙

**"근본 원인 조사 없이 수정 금지" (No fixes without root cause investigation first)**

증상을 가리는 수정은 시간 낭비 + 추가 버그 유발. 추측하지 말고 추적하라(Don't guess, trace).

## 4단계 프로토콜

### Phase 1: 재현 (Reproduce)
- 결정론적으로 재현되는 최소 케이스를 먼저 만든다
- "가끔 발생함"은 재현 미완료 — 조건을 더 좁혀라
- 에러 메시지 / 로그 / 스택 트레이스 전체를 끝까지 읽는다 (앞 3줄만 보지 마라)
- 재현 못 하면 디버깅 시작 금지

### Phase 2: 격리 (Isolate)
- 변경 사항 / 입력 / 환경을 한 번에 하나씩만 토글
- 이분 탐색으로 범위 절반씩 좁힌다 (git bisect, 코드 주석 처리)
- 동작하는 레퍼런스 케이스를 찾고 broken 케이스와 diff
- "이것도 바꾸고 저것도 바꿨더니 됐다" 금지

### Phase 3: 추적 (Trace)
- 데이터 흐름을 입력→출력까지 끝까지 따라간다
- 각 단계에서 **실제 값**을 로그/브레이크포인트로 확인 (`debug-logcollector` 활용)
- 가정을 검증으로 바꾼다: "아마 X일 것이다" → "X인지 직접 찍어봤다"
- Unity 환경 주의: reflection 결과, UnityEvent 발화 여부, 컴파일 완료 여부는 항상 직접 확인

### Phase 4: 근본 원인 (Root Cause)
- "왜?"를 5번 묻는다 (5 Whys)
- 같은 클래스의 다른 버그도 같은 fix로 고쳐지는지 검토
- 증상 수정이 아니라 원인 수정
- 수정 후 회귀 테스트 작성 (가능하면)

## 발동 트리거 (반드시)

다음 중 하나라도 해당하면 **즉시 멈추고 Phase 1부터 다시 시작**:

1. 같은 종류 시도가 **2번 연속 실패**
2. 한 문제에 **5분 이상** 매달려 진전 없음
3. 머릿속에 "아마", "그럴 거야", "타이밍 문제일 듯" 같은 추측이 떠오를 때
4. 3개 이상의 fix가 연속 실패하면 **아키텍처 자체를 의심**

## Red Flag (이런 말이 떠오르면 위험)

- "Quick fix 한 번만 더..."
- "캐시 문제일 거야 — Library 지워볼까"
- "Reimport 하면 될 거야"
- "타이밍 문제니까 delay 추가하자"
- "Reflection으로 internal 호출하면 될 거야"
- "테스트 스킵하고 일단 커밋"

## Unity 특화 함정

| 함정 | 실제 원인 가능성 |
|---|---|
| "Library 캐시 문제" | 실제론 메타 GUID 충돌 / 컴파일 에러 |
| "Reflection 호출 무반응" | EventCallback이 안 타거나 internal API가 다른 시그니처 |
| "EditMode에서 안 됨" | Runtime panel tick / coroutine / Update가 EditMode에서 안 돈다 |
| "스크린 캡처 비어있음" | Repaint 타이밍 / focus / dpi scale |
| "컴파일 됐는데 동작 안 함" | 도메인 리로드 안 됨 → 에디터 포커스 후 다시 시도 |

## 트리거 워크플로우

```
시도 1 실패
  ↓
시도 2 (다른 접근)
  ↓
실패 → 멈춤 → Phase 1 (재현 케이스 정의)
  ↓
Phase 2 (격리: 동작하는 레퍼런스 찾기)
  ↓
Phase 3 (추적: 실제 값 찍기)
  ↓
Phase 4 (근본 원인 → 수정)
```

## 관련

- `debug-logcollector` — Phase 3 추적 시 실제 값 캡처용
- `debug-unity-log` — LogError 사용 룰
- `verification-before-completion` — 수정 후 검증
