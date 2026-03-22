# MapGen AI — 개발 계획서

> 목표: Map Designer 모드의 모든 기능을 AI 대화 형식으로 대체.
> "Map Designer를 딸깍해서 만든 AI 버전"

---

## 1. Map Designer 기능 대비표

### 1.1 산/언덕 (MountainSettingsPatch)

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| hillAmount (0.5-1.6) | hills=left/right 등 (위치만) | P2 |
| hillSize (Perlin 주파수) | ✗ | P2 |
| hillSmoothness (lacunarity) | ✗ | P2 |
| Radial 모드 (중앙↔가장자리) | hills=center/edges | P2 강화 |
| Split 모드 (직선 분할) | ✗ | P2 |
| Side 모드 (한쪽 방향) | hills=left/right/top/bottom | P2 강화 |
| Donut 모드 (링 형태) | ✗ | P3 |
| 산 출구 | ✗ | P3 |
| 동굴 토글 | caves ✓ | 완료 |
| 덩어리화 | ✗ | P3 |

### 1.2 강/해안

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 강 방향 (0-359도) | ✗ | P2 |
| 강 스타일 (Canal/Fork/Oxbow 등) | ✗ | P2 (Canal만) |
| 강 너비 배수 | ✗ | P3 |
| 강 위치 오프셋 | ✗ | P3 |
| 해안 방향 (N/E/S/W) | ✗ | P2 |

### 1.3 지형

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 비옥도 배수 | ✗ (제거 — SoilRich 버그) | P2 재구현 |
| 물 지형 배수 | ✗ | P3 |
| 석재 종류 수 (1-15) | ✗ | P2 |
| 허용 석재 선택 | ✗ | P2 |

### 1.4 밀도 제어

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 식생 밀도 | ✓ | 완료 |
| 동물 밀도 | ✓ | 완료 |
| 폐허 밀도 | ✗ | P2 |
| 위험 장치 밀도 | ✗ | P2 |
| 간헐천 밀도 | 파라미터만 (패치 없음) | P2 |
| 광석 밀도 | ✗ | P2 |
| 암석 덩어리 토글 | ✗ | P3 |

### 1.5 특수 기능

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 호수 | ✗ | P3 |
| 원형 섬 | ✗ | P3 |
| 자연 섬 | ✗ | P3 |
| 프리셋 시스템 | ✗ | P3 |

### 1.6 TileMutator (Odyssey 탭 — 망그로브, 피요르드 등)

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 카테고리별 mutator 선택 | ✗ | P2 |
| 충돌 감지/동기화 | ✗ | P2 |
| Disable All | ✗ | P2 |

### 1.7 프리셋

| Map Designer 기능 | MapGenAI 현재 | 우선순위 |
|---|---|---|
| 8가지 내장 프리셋 | ✗ | P2 |
| 저장/불러오기 (최대 15개) | ✗ | P2 |

---

## 2. 구현 단계

### Phase 1 ✅ 완료
- 기본 LLM 채팅 UI + Map Preview 연동
- hills/vegetation/animal/caves 기본 파라미터
- 해안 감지, 유효성 검증, 자동 리셋
- 테스트벤치 12개

### Phase 2: Map Designer 핵심 이식 (현재 목표)
**2A. 산 시스템 강화** ✅
- hillAmount (전체 고도 오프셋) + Map Designer slope 방식 (AxisAsValueX 수학)
- left/right/top/bottom → cos/sin 회전, center/edges → 방사형

**2B. 해안 방향** ✅
- World.CoastAngleAt Postfix (N=270, E=180, S=90, W=0)
- coast_direction 파라미터

**2C. 석재/광석** ✅
- World.NaturalRockTypesIn Finalizer (석재 수 1-15)
- GenStep_ScatterLumpsMineable Prefix (광석 밀도 0-2.5)

**2D. 프롬프트** ✅
- hill_amount, coast_direction, rock_count, ore_density, mutators 반영

**2E. TileMutator** ✅
- GenStep_ElevationFertility Prefix에서 mutator 교체 (60+ mutator)
- 온천, 망그로브, 피요르드, 호수, 동굴 등 전체 지원

**2F. 프리셋 저장/불러오기** 🔄
- JSON 파일로 파라미터 세트 저장/불러오기
- 테스트벤치 30개+

**2E. TileMutator 시스템 (Odyssey 탭 기능)**
- RW 1.6 TileMutatorDef 목록 읽기 (망그로브, 피요르드, 동굴 등)
- LLM이 유저 요청에 맞는 mutator 선택
- MountainSettingsPatch Prefix에서 mutator 적용 (Map Designer 동일 방식)
- 카테고리: AncientStructure, Caves, Coast, Fish, River, Groundwater 등

**2F. 프리셋 저장/불러오기**
- 파라미터 세트를 이름으로 저장 (JSON)
- 불러오기 시 MapGenParams에 적용 + Map Preview 갱신
- (보류: 타일 호환성 검증 — 해안 프리셋을 내륙에서 불러올 때)

### Phase 3: 특수 기능
- 호수/섬 생성 (Lake, RoundIsland, NatIsland)
- 도넛 모드, 산 출구, 덩어리화
- 강 너비/위치, 물 지형

### Phase 4: 레이아웃 + 고급
- Zone 기반 영역 지정
- 모드 호환성

---

## 3. 에이전트 분배

### 시니어 개발자 (Architect)
- 전체 아키텍처, GenStep 실행 순서 검증
- 패치 간 충돌 방지, Map Preview 호환성 확인
- 담당: PLAN.md, 코드 리뷰

### 주니어 개발자 A (Backend — GenStep)
- Harmony 패치 구현 (산, 강, 지형, 밀도)
- Map Designer 소스 참조하여 이식
- 담당: GenStepPatches.cs, 새 패치 파일
- 참조: /tmp/MapDesigner/1.6/Source/

### 주니어 개발자 B (LLM/Prompt)
- 시스템 프롬프트 업데이트, MapParamsData 확장
- 새 파라미터 추가 시 프롬프트/파싱 동기화
- 담당: Dialog_TextToMap.cs, MapGenParams.cs

### 품질 관리 (QA)
- 테스트벤치 설계/실행/평가
- Map Designer 기능별 검증 케이스 작성
- 평가 기준: 기능 정확성, LLM 응답 안정성, 빌드 성공
- 담당: TestBench.cs, TEST_BENCH.md

---

## 4. Phase 2 세부 작업

### Step 1: MapParamsData 확장 (주니어 B)
```
추가 필드:
  hill_amount: float (0.5-1.6)
  hill_size: string ("small"/"medium"/"large")
  hill_smoothness: string ("rough"/"normal"/"smooth")
  river_direction: float (0-359, -1=자동)
  river_style: string ("vanilla"/"canal")
  coast_direction: string ("auto"/"north"/"east"/"south"/"west")
  rock_types: List<string>
  ore_density: float (0-2.5)
  ruin_density: float (0-2.5)
  fertility_level: float (0.2-2.5)
```

### Step 2: GenStep 패치 (주니어 A, 순서대로)
1. hillAmount/Size/Smoothness → MountainSettingsPatch 참조
2. hill_pattern 강화 → Radial/Split/Side
3. coast_direction → CoastPatches 참조
4. river_direction → RiverDirectionPatch 참조
5. river_style Canal → RiverStylePatch 참조
6. rock_types → RockTypesPatch 참조
7. ore/ruin/geyser density → OreDensityPatch 참조
8. fertility_level → TerrainFrom 방식

### Step 3: 프롬프트 + 테스트 (주니어 B + QA)
- 프롬프트에 새 파라미터 반영
- 테스트벤치 30개 케이스 작성 + 실행

---

## 5. 테스트벤치 목표 (30개+)

| 카테고리 | 케이스 수 | 검증 내용 |
|---|---|---|
| 산 시스템 | 6 | 양/크기/부드러움/배치 모드 |
| 강/해안 | 5 | 방향/스타일/해안 방향 |
| 지형 | 4 | 석재/비옥도/물 |
| 밀도 | 4 | 간헐천/광석/폐허/동물 |
| 특수 | 3 | 호수/섬/동굴 |
| 멀티턴 | 3 | 수정/추가/충돌 |
| 경계 | 5+ | 극단값/모순/빈 요청/한국어 |

---

## 6. 참조 파일

### MapGenAI
- `Source/Patches/GenStepPatches.cs` — GenStep 패치
- `Source/MapGen/MapGenParams.cs` — 파라미터 모델
- `Source/UI/Dialog_TextToMap.cs` — 채팅 UI + 프롬프트
- `Source/Tests/TestBench.cs` — 테스트벤치

### Map Designer (/tmp/MapDesigner/1.6/)
- `Patches/MountainSettingsPatch.cs` — 산 (5모드, Transpiler+Postfix)
- `Patches/RiverDirectionPatch.cs` — 강 방향
- `Patches/RiverStylePatch.cs` — 강 스타일 (6종)
- `Patches/RockTypesPatch.cs` — 석재 종류
- `Patches/CoastPatches.cs` — 해안 방향
- `Patches/OreDensityPatch.cs` — 광석 밀도
- `Feature/Lake.cs` — 호수
- `MapDesignerSettings.cs` — 전체 파라미터 (559줄)
