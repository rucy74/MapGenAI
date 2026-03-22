## 2026-03-21 — 타 게임 지형/형태 생성 방식 종합 리서치

- 요청: Minecraft WorldEdit, AI 빌딩 프로젝트, Factorio/Terraria/DF 등 타 게임의 커스텀 지형/형태 생성 방식 조사. CSG 접근법 검증.
- 수행: 14건 이상 웹 검색 + 5건 페이지 직접 분석. WorldEdit 명령 체계, T2BM 논문 JSON 스키마, BlockGPT/Mindcraft/Voyager/Talking-to-Build 프로젝트, Factorio 블루프린트 포맷, Sponge Schematic v3, TEdit, Cities Skylines heightmap, NMS 복셀 시스템 조사.
- 결과: CSG+SDF 접근법이 기존 프로젝트들 대비 가장 적합. T2BM의 JSON interlayer가 우리 CSG JSON과 구조적으로 동일한 패러다임. Template library나 image-based는 보완재로만 유효.
- 산출물: agents/log/2026-03-21-terrain-generation-research.md

---

## 히스토리

(이전 작업 기록 없음)

---
