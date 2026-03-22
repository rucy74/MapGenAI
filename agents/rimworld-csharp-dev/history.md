## 2026-03-22 00:30 -- ridge shape 구현 (elevation 아키텍처 재설계)

- 요청: PLAN_ELEVATION_REDESIGN.md에 따라 slope 상쇄 및 split Max 덮어쓰기 문제를 구조적으로 해결하는 ridge shape 구현
- 수행: ElevationShape fade/noise_amount 필드+파서, ApplyRidge (smoothstep+Perlin), 레거시 slope/split->ridge 변환, 디스패처/GetAutoShapeForHills/IsHillsSlotShape/직렬화 업데이트
- 결과: 빌드 성공 (0 errors). slope/split 완전 대체. 하위 호환 유지.
- 산출물: MapGenParams.cs, GenStepPatches.cs

---

## 2026-03-21 23:55 -- 치명적 버그 3건 수정 (간헐천/광석/식생동물 밀도)

- 요청: 간헐천 패치 미구현, 광석 원본 복원 누락, 식생/동물 밀도 > 1 무효 수정
- 수행: GeyserPatch.cs 신규, OreDensityPatch.cs 전면 재작성 (Prefix/Postfix 복원 패턴), BiomeDensityPatch.cs 신규 (Map Designer 방식 BiomeDef 수정), GenStepPatches.cs 수정
- 결과: 빌드 성공 (0 errors). 3건 모두 해결.
- 산출물: GeyserPatch.cs, OreDensityPatch.cs, BiomeDensityPatch.cs, GenStepPatches.cs

---

## 2026-03-21 23:30 -- 바닐라 vs MapGenAI 지형 feature 전수 비교 분석

- 요청: 12개 feature 전수 비교 (산/호수/강/해안/간헐천/동굴/광석/비옥도/석재/폐허/돌덩어리/TileMutator)
- 수행: MapGenAI 소스 전체 + 바닐라 디컴파일(WebSearch) + Map Designer 소스 비교
- 결과: 수정 필요 없음 6개, 부분 수정 4개, 전면 재설계 1개, 미구현 1개
- 산출물: 분석 보고 (코드 수정 없음)

---

## 2026-03-21 21:30 -- 자연 산 vs MapGenAI 산 구조 분석

- 요청: Case A (자연 산 + 추가) 동작 / Case B (평지 + 연속 생성) 실패 원인 분석
- 수행: 바닐라 GenStep_ElevationFertility 디컴파일, MapGenTuning 상수 확인, Map Designer 소스 비교, MapGenAI postfix 흐름 추적
- 결과: 근본 원인 = base elevation 내재 산(hilliness*Perlin+DistFromAxis) vs ElevationShapes 의존 산의 비대칭. Option E (하이브리드: null=유지/[]=제거/값=교체) 권장
- 산출물: 분석 보고 (코드 수정 없음)

---

## 2026-03-21 — SDF 회전 버그 수정

- 요청: SdfStar 바람개비, 하트 분리 버그 수정
- 수행: SdfNgon/SdfStar totalRot 부호 반전 (`-rotDeg+90` → `rotDeg-90`) + Star mirror 추가
- 결과: 34/34 유닛 테스트 통과, 양쪽 빌드 성공
- 산출물: `SdfEngine.cs` (수정 3건)

---

## 2026-03-21 — Anchor 기반 자유 형태 지형 v2 (PLAN_SHAPES_V2.md)

- 요청: anchor/pos/at/size 기반 의미적 도형 배치 시스템 (v1의 좌표 직접 지정 → v2의 의미적 참조로 전환)
- 수행: ShapeBuilder.cs (신규), SdfEngine.cs (신규), ApplyComposite 래스터라이저, JSON 파싱, 직렬화
- 결과: 빌드 성공 (0 errors), 기존 코드 변경 없음 (추가만)
- 산출물: ShapeBuilder.cs, SdfEngine.cs, MapGenParams.cs, GenStepPatches.cs, Dialog_TextToMap.cs

---

## 2026-03-21 — CSG+SDF 래스터라이저 구현 (PLAN_SHAPES.md Stage 1)

- 요청: PLAN_SHAPES.md 설계에 따라 CSG+SDF 기반 자유 형태 지형 시스템 C# 백엔드 구현
- 수행: ShapePrimitive/ComposeOp 데이터 클래스, SDF 7종 (circle/ellipse/rect/tri/poly/star/heart), 불리언 6종 (union/sub/inter + smooth 3종), ApplyComposite() 래스터라이저, JSON 파싱 (중첩 배열 포함), 직렬화
- 결과: 빌드 성공 (0 errors), 기존 7종 primitive 변경 없음
- 산출물: MapGenParams.cs, GenStepPatches.cs, Dialog_TextToMap.cs, SimpleJson.cs

---

## 2026-03-21 — heightmap 지형 시스템 구현

- 요청: LLM이 문자 격자(grid)로 임의 지형 형태를 기술할 수 있는 heightmap 타입 추가
- 수행: ElevationShape.grid 필드, ApplyHeightmap() bilinear 보간, JSON 파싱 확장, ToSnapshot/BuildCurrentParamsText 직렬화
- 결과: 빌드 성공, 기존 6종 primitive 변경 없음
- 산출물: MapGenParams.cs, GenStepPatches.cs, Dialog_TextToMap.cs

---

## 2026-03-21 — ring Gaussian 프로파일 교체

- 요청: ring 모양 언덕 추가 시 기존 산이 사라지고 초승달 형태가 되는 버그 수정
- 수행: ApplyRing() 공식을 Map Designer Donut -> Gaussian 프로파일로 교체
- 결과: 빌드 성공, DLL 배포 완료
- 산출물: GenStepPatches.cs

---
