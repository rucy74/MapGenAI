## 2026-03-21 24:30 -- LLM 시스템 프롬프트 slope/split -> ridge 전면 업데이트

- 요청: 시스템 프롬프트에서 slope/split을 ridge로 교체 (PLAN_ELEVATION_REDESIGN.md 섹션 5 기반)
- 수행: elevation_shapes 가이드(한/영), JSON schema enum, 규칙(한/영), few-shot 예시(한/영, 내륙/해안) 전면 수정. fade/noise_amount 파라미터를 schema/파싱/직렬화에 추가. slope/split 문자열 완전 제거.
- 결과: 빌드 성공 (0 warning, 0 error). grep으로 slope/split 잔존 확인 → 0건.
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`

---

## 2026-03-21 23:50 -- 산/elevation 아키텍처 재설계 (ridge shape 도입)

- 요청: slope 상쇄, split Max 덮어쓰기, 바닐라 비유사성 3대 문제 해결을 위한 elevation 아키텍처 재설계
- 수행: 4개 접근법 분석 후 C(하이브리드: DistFromEdge + Perlin) 선택. 새로운 `ridge` shape 설계 (smoothstep 프로파일 + Perlin 디테일). slope/split 제거 및 ridge로 대체. 6개 시나리오 검증. 의사코드, 프롬프트 변경안, 레거시 호환 방안 작성.
- 결과: 설계 문서 완성. ridge(left)+ridge(right)가 양쪽 산+가운데 골짜기를 형성함을 수학적으로 증명. 코드 구현은 별도 에이전트 담당.
- 산출물: `docs/PLAN_ELEVATION_REDESIGN.md`

---

## 2026-03-21 23:30 -- hills/elevation_shapes 상호작용 아키텍처 설계

- 요청: hills 변경 시 elevation_shapes가 갱신되지 않는 버그의 코드 레벨 해결 아키텍처 설계
- 수행: 4개 옵션(A/B/C/D) + 하이브리드 분석. Option C(refined) 채택: IsHillsSlotShape 패턴 매칭으로 hills-slot shapes만 교체, 커스텀 shapes(호수/노이즈 등) 보존. 시스템 프롬프트 변경안(hills-slot shapes 비표시), hills/shapes 관계 명세 작성.
- 결과: 설계 완료. 11개 시나리오 모두 정상 동작 확인. 코드 구현은 별도 에이전트 담당.
- 산출물: 대화 내 설계 문서 (MapGenParams.cs, Dialog_TextToMap.cs 변경 예정)

---

## 2026-03-21 -- composite(v2) anchor 기반 프롬프트 추가 (PLAN_SHAPES_V2.md)

- 요청: 기존 코드 수정 없이 composite v2 프롬프트 추가 (anchor 기반, 좌표 없음)
- 수행: params 스키마에 |composite+shapes/compose 추가, 가이드 추가(ring 뒤), few-shot 추가(내륙 별+하트, 해안 별) (한/영 모두)
- 결과: 빌드 성공 (0 errors). straight_river=7, hills=31 보존. 기존 코드 0바이트 변경.
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`

---

## 2026-03-21 -- composite(CSG) 프롬프트 엔지니어링 (PLAN_SHAPES.md Stage 2)

- 요청: heightmap 가이드/예시/few-shot을 composite(CSG)로 전면 교체
- 수행: params 스키마에 composite+shapes/compose 추가, 가이드 교체, 예시 5종(별/하트/초승달/L자/삼각형), few-shot 교체(내륙 3개+해안 1개), 규칙 업데이트 (한/영 모두)
- 결과: 빌드 성공 (0 errors). 기존 primitive 6종 보존.
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`

---

## 2026-03-21 -- heightmap 프롬프트 개선 (테스트 결과 기반)

- 요청: 테스트에서 발견된 3가지 문제 수정 (bump 오선택, 별 grid가 다이아몬드, 해상도 부족)
- 수행: heightmap 사용 기준 강화, 별 grid 11x11 교체, 삼각형 예시 추가, grid 크기 가이드 세분화 (한/영 모두)
- 결과: 빌드 성공 (0 errors). 내륙 few-shot 10개, 해안 few-shot 6개. heightmap 가이드 예시 5종.
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`

---

## 2026-03-21 -- heightmap 프롬프트 엔지니어링

- 요청: heightmap elevation_shape 타입 프롬프트 추가
- 수행: BuildSystemPrompt()에 heightmap 스키마/가이드/예시/few-shot 추가 (한/영 모두)
- 결과: 빌드 성공. 스키마에 heightmap+grid, primitive 설명 3줄, 예시 3종, few-shot 내륙 2개+해안 1개 추가.
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`

---

## 2026-03-21 -- elevation_shapes 프롬프트 개선

- 요청: bump 조합으로 어떤 모양이든 표현 가능하게 프롬프트 개편
- 수행: BuildSystemPrompt() 한/영 가이드 전면 개편, BuildCurrentParamsText() 보존 지시 강화
- 결과: 빌드 성공, feature/prompt-improvements 브랜치 커밋 완료
- 산출물: `dev/Source/UI/Dialog_TextToMap.cs`, `dev/Source/MapGen/MapGenParams.cs`

---
