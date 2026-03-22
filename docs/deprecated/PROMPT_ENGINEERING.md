# 전처리 및 프롬프트 엔지니어링 전략

> 핵심 원칙: LLM에게 규칙을 가르치는 대신, **선택지를 제한하고 출력을 검증**한다.

---

## 현재 문제

1. 60+ mutator를 전부 보여줌 → LLM이 해안 아닌 타일에서 Coast mutator 추가
2. 규칙이 20+ 줄 → 모델이 무시하거나 놓침
3. 잘못된 응답을 그대로 Apply → 유저에게 "추가했습니다" 후 실제로 안 됨
4. Gemma 같은 약한 모델에서 더 심해짐

## 해결: 4개 레이어

```
유저 입력
    ↓
[Layer 1] 사전 필터링: 현재 타일에서 유효한 옵션만 프롬프트에 포함
    ↓
[Layer 2] 프롬프트 구조화: 역할 + 스키마 + 컨텍스트 + 예시
    ↓
LLM 응답
    ↓
[Layer 3] 출력 검증: mutator/파라미터 유효성 체크
    ↓ (실패 시 에러 메시지 → 유저에게 안내)
[Layer 4] 적용: MapGenParams.Apply()
```

---

## Layer 1: 사전 필터링 (가장 효과 큼)

### 원칙
LLM에게 "이걸 하지 마세요"보다 **"이 중에서 골라주세요"**가 100배 효과적.

### 구현: BuildMutatorList(tileId)
```
현재: BuildMutatorList() → 모든 mutator 나열
개선: BuildMutatorList(tileId) → 타일 조건으로 필터링

필터 조건:
1. 강 없는 타일 → River 카테고리 mutator 제거
2. 해안 아닌 타일 → Coast 카테고리 mutator 제거
3. biomeWhitelist → 현재 바이옴과 불일치 mutator 제거
4. Odyssey 비활성 → 모든 Odyssey mutator 제거
```

### 효과
- 내륙 타일: 60개 → 30개 (Coast/River 제거)
- LLM이 잘못된 mutator를 선택할 수 없음 (선택지에 없으니까)
- 프롬프트 토큰 감소 → 응답 속도 향상

---

## Layer 2: 프롬프트 구조화

### 원칙
- 규칙 나열 < 예시 제공 (few-shot)
- 길고 복잡한 프롬프트 < 짧고 명확한 프롬프트
- "하지 마세요" < "이렇게 하세요"

### 구조
```
1. 역할 (2줄)
   "당신은 RimWorld 맵 생성 도우미입니다."

2. 출력 포맷 (JSON 스키마)
   action: ask | generate
   params: { ... }

3. 현재 타일 컨텍스트 (동적)
   바이옴, 지형, 강, 해안 정보

4. 사용 가능한 옵션 (동적 — Layer 1에서 필터링된 것만)
   파라미터 목록 + 유효한 mutator만

5. 예시 (2-3개, few-shot)
   입력→출력 예시
```

### few-shot 예시
```
유저: "산 많고 온천 있는 맵 만들어줘"
→ {"action":"generate","description":"산이 많고 온천이 있는 맵","params":{"hills":"center","hill_amount":1.4,"caves":true,"mutators":["HotSprings"]}}

유저: "바다 있는 맵 만들어줘" (내륙 타일)
→ {"action":"ask","message":"현재 타일은 해안가가 아닙니다. 바다가 있는 맵을 원하시면 세계지도에서 해안가 타일을 선택해주세요."}
```

---

## Layer 3: 출력 검증

### 원칙
LLM 출력을 신뢰하지 않음. 코드에서 검증.

### 검증 항목
```
1. JSON 파싱 성공 여부
2. action이 "ask" 또는 "generate"인지
3. mutator defName이 DefDatabase에 존재하는지
4. Coast mutator인데 해안 아닌 타일인지
5. River mutator인데 강 없는 타일인지
6. 숫자 범위 (이미 구현: Clamp)
7. hills enum 값 (이미 구현: ValidHills)
```

### 검증 실패 시 처리
```
Option A: 자동 수정 (범위 → 클램핑, 잘못된 mutator → 제거)
Option B: 에러 메시지 → 채팅에 표시 → LLM에 재요청 (API 비용 증가)
Option C: 에러 메시지 → 채팅에 표시 → 유저가 수동 재요청

현재: A (클램핑) + 무시 (잘못된 mutator는 조용히 스킵)
권장: A + C (클램핑 + 채팅에 경고 표시)
```

---

## Layer 4: 모델 비의존적 설계

### 원칙
모든 검증은 코드 레벨. LLM 모델에 의존하지 않음.

### 모델별 차이
- Gemini Flash: structured output 지원 → 보너스 신뢰도
- GPT-4o-mini: function calling 지원 → 보너스 신뢰도
- Gemma/Ollama: 지원 없음 → Layer 1-3이 필수
- 큰 모델: 프롬프트 이해도 높음 → 예시 적게
- 작은 모델: 프롬프트 이해도 낮음 → 예시 많이 + 옵션 적게

---

## 구현 우선순위

| 순서 | 작업 | 효과 | 난이도 |
|------|------|------|--------|
| **1** | BuildMutatorList 타일 필터링 | ★★★★★ | 낮음 |
| **2** | 출력 검증 + 채팅 경고 | ★★★★ | 낮음 |
| **3** | 프롬프트 재구조화 + few-shot | ★★★ | 중간 |
| **4** | Gemini structured output | ★★ | 높음 |

---

## 참고 자료

- [Production-Grade Prompt Engineering](https://latitude-blog.ghost.io/blog/10-best-practices-for-production-grade-llm-prompt-engineering/)
- [Structured Outputs Guide](https://agenta.ai/blog/the-guide-to-structured-outputs-and-function-calling-with-llms)
- [LLM Validation + Retry Pattern](https://apxml.com/courses/prompt-engineering-llm-application-development/chapter-7-output-parsing-validation-reliability/implementing-retry-mechanisms)
- [LLM Tool Calling in Production](https://medium.com/@komalbaparmar007/llm-tool-calling-in-production-rate-limits-retries-and-the-infinite-loop-failure-mode-you-must-2a1e2a1e84c8)
- [RimWorld AI Framework](https://github.com/oidahdsah0/Rimworld_AI_Framework)
