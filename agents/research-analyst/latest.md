# research-analyst — 현재 상태

## 마지막 작업
- 날짜: 2026-03-21
- 요청: 다른 게임의 커스텀 지형/형태 생성 방식 조사 (Minecraft WorldEdit, AI 빌딩, Factorio, Terraria 등)
- 수행: 6개 연구 주제에 대한 종합 리서치 (WorldEdit, Minecraft AI 프로젝트, 타 게임 도구, 이미지→지형, CSG 평가)
- 결과: CSG+SDF 접근법이 가장 적절함을 확인. T2BM 논문의 JSON interlayer와 우리 CSG JSON이 구조적으로 유사. WorldEdit은 영감 제공하나 직접 적용 불가.

## 산출물
- 이 latest.md (리서치 결과 요약)
- agents/log/2026-03-21-terrain-generation-research.md (상세 리서치)

## 다음 단계
- 추가 조사 필요 시: T2BM repairer 모듈 → CSG JSON validation에 적용 가능한지
- ShapeCraft (NeurIPS 2025) 논문 상세 분석 → 구조 그래프 분해가 우리 compose 체인과 어떻게 대응하는지

## 다른 에이전트에게
- rimworld-ai-dev: T2BM의 3단계 파이프라인(input refining → interlayer → repairing) 구조 참고. 특히 repairer(잘못된 블록명 수정, 좌표 검증)는 우리 CSG JSON 검증에 유용
- rimworld-csharp-dev: WorldEdit의 //generate 표현식 엔진(수식→블록)과 우리 SDF 래스터라이저는 같은 패러다임. 이미 올바른 방향
