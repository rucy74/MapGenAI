# MapGen AI — 프로젝트 개요

## 프로젝트 목적
- RimWorld 모드 — 자연어 채팅으로 AI가 맵 파라미터를 생성
- "mountain fortress with hot springs" 같은 자유 텍스트 입력 → 즉시 맵 반영

## 핵심 기술 스택
- C# / .NET 4.7.2 (net472), LangVersion 9.0
- Harmony (패치 프레임워크), RimWorld 1.5 / 1.6 API
- Map Preview 모드 (필수 의존), Odyssey DLC 지원

## 지원 LLM 프로바이더
- Google Gemini, OpenAI, DeepSeek, Grok, GLM, Alibaba, OpenRouter
- Local (Ollama / LM Studio), Custom (OpenAI-compatible)
- 멀티 API 키 + 우선순위 fallback

## 주요 기능 (5개)
- **자연어 → 맵 파라미터**: 채팅 기반 멀티턴 대화, 파라미터 누적 수정
- **지형 제어**: 5가지 ElevationShape 프리미티브 (slope/radial/split/bump/noise/ring)
- **Odyssey TileMutator**: 60+ mutator (온천, 피요르드, 동굴 등) 월드 타일에 직접 적용
- **Undo / Reset**: 전송 전 스냅샷 스택, 원본 타일 상태 복원
- **프리셋 시스템**: 파라미터 세트를 JSON으로 저장/불러오기

## 다국어 지원
- 한국어, 영어, 일본어, 중국어 간체 (UI + AI 응답)

## 대상 사용자
- RimWorld 플레이어 중 맵 커스터마이징에 관심 있는 유저
- AI 코딩 에이전트로 100% 구현된 "제로 C# 경험" 모드 케이스 스터디
