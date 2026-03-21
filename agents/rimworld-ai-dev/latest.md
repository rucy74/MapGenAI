# rimworld-ai-dev — 최근 작업

## 마지막 작업
- 날짜: 2026-03-21
- 요청: elevation_shapes 프롬프트 개선 — bump 조합으로 어떤 모양이든 표현 가능하게
- 수행: BuildSystemPrompt() 한/영 가이드 전면 개편, BuildCurrentParamsText() 보존 지시 강화
- 결과: 빌드 성공, feature/prompt-improvements 브랜치 커밋 완료

## 산출물
- `dev/Source/UI/Dialog_TextToMap.cs` — elevation_shapes 가이드 (한/영 모두) 재작성
- `dev/Source/MapGen/MapGenParams.cs` — 기존 shapes 보존 지시 강화

## 핵심 변경
- 좌표계 명시: x=0 왼쪽, x=1 오른쪽, z=0 아래, z=1 위. position="[x,z]"
- bump ★핵심 도구★로 강조 (fill="water" → 호수)
- 형태 레시피: 별(5봉), U자, L자, 초승달 호수, 분화구 호수, 산 위 호수
- BuildCurrentParamsText: "NEVER delete or modify existing shapes. ONLY append new ones."

## 지원 프로바이더 (11개)
Gemini, OpenAI, DeepSeek, Grok, GLM, GLMCoding, AlibabaIntl, AlibabaCN, OpenRouter, Local, Custom

## 다음 단계
- 게임에서 복합 형태 요청 테스트 (별 모양 언덕, 초승달 호수 등)
- feature/prompt-improvements → main 머지

## 다른 에이전트에게
- OpenRouter 무료 모델 대부분 429 rate limited (유료 권장)
- GLM/NVIDIA 무료 모델은 thinking 모델 → max_tokens 최소 500 필요
- WorldInspectPane_Patch.cs TestAPI 디버그 액션도 이 세션에서 수정됨
