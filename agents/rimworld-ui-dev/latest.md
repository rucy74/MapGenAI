# rimworld-ui-dev — 최근 작업

## 마지막 작업
- 날짜: 2026-03-21
- 요청: RimTalk 스타일 Settings UI — Simple/Advanced 모드, 멀티 API 리스트, 모델 드롭다운
- 수행: TextToMapSettings.cs 전면 재작성
- 결과: 빌드 성공, 스팀 배포 완료

## 산출물
- `dev/Source/Core/TextToMapSettings.cs` — UI 전면 재작성
- `dev/Source/Core/ApiConfig.cs` — 신규 (API 설정 단위)
- `dev/Source/Core/LLMProviders.cs` — 신규 (11개 프로바이더 enum/registry)

## 구현된 기능
- Simple 모드: Gemini API 키 단일 입력 + 무료 키 링크
- Advanced 모드: Cloud/Local 토글, API 리스트 테이블 (프로바이더▼, 키, 모델버튼, ✓, ▲▼, ✕)
- 모델 버튼: 클릭 시 API fetch → FloatMenu 자동 표시
- 실패 시 "No API key" 주황색 표시
- 프로바이더 변경 시 모델명 자동 리셋

## 다음 단계
- 특별히 없음. 사용성 피드백 수집 중.

## 다른 에이전트에게
- 프로바이더 URL/기본모델은 LLMProviders.cs의 LLMProviderRegistry에서 관리
- OpenRouter base URL: `https://openrouter.ai/api` (주의: /v1 아님, OpenAIClient에서 붙여줌)
