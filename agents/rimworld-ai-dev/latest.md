# rimworld-ai-dev — 최근 작업

## 마지막 작업
- 날짜: 2026-03-21
- 요청: 멀티 프로바이더 지원, fallback, OpenRouter 버그 수정
- 수행: ILLMClient 팩토리 수정, OpenAI/Gemini 클라이언트 오류 처리 개선
- 결과: fallback 정상 동작, OpenRouter compact JSON 파싱 수정

## 산출물
- `dev/Source/LLM/ILLMClient.cs` — Create(ApiConfig, fallbackUrl) 팩토리
- `dev/Source/LLM/OpenAIClient.cs` — compact JSON 파싱, throw on HTTP error
- `dev/Source/LLM/GeminiClient.cs` — throw on HTTP error

## 지원 프로바이더 (11개)
Gemini, OpenAI, DeepSeek, Grok, GLM, GLMCoding, AlibabaIntl, AlibabaCN, OpenRouter, Local, Custom

## 프롬프트 현황
- 시스템 프롬프트: `Dialog_TextToMap.cs > BuildSystemPrompt()`
- elevation_shapes 가이드 포함 (6종 primitive 설명)
- **미완성**: bump 여러 개 조합 예시 없음 → 복잡한 모양 생성 능력 미활용

## 다음 단계 (중요)
**프롬프트 개선**: bump 조합으로 복잡한 모양 만드는 예시 추가
- 별 모양 = bump 5개 원형 배치
- 좌표 계산 가이드 (삼각함수로 원형 배치)
- "LLM이 모양을 분석해서 좌표 계산하라"는 명령 명시

## 다른 에이전트에게
- fallback: GetActiveConfig() → 실패(throw) → TryNextConfig() → 재시도
- OpenRouter 무료 모델 대부분 429 rate limited (유료 모델 권장)
- GLM/NVIDIA 무료 모델은 thinking 모델 → max_tokens 최소 500 필요
