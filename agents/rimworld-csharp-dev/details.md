## 2026-03-22 00:30 -- ridge shape 구현 (elevation 아키텍처 재설계)

### 맥락
PLAN_ELEVATION_REDESIGN.md 설계에 따른 구현. 기존 slope/split shape의 구조적 결함 2가지를 해결:
1. slope 상쇄: slope(left) + slope(right) = 0 (선형 함수의 합은 선형 -> 완벽 상쇄)
2. split Max 덮어쓰기: `grid[cell] = Max(grid[cell], 0.85f)` -> 기존 Perlin 패턴 소실

### 분석/수행 내용

#### 1. ElevationShape 확장 (MapGenParams.cs)
- `fade` 필드 추가: ridge가 맵의 얼마나 안쪽까지 들어오는지 (small=0.3, medium=0.5, large=0.7)
- `noise_amount` 필드 추가: Perlin 디테일 강도 (none=0, low=0.3, medium=0.6, high=1.0)
- `ParseFade()`, `ParseNoiseAmount()` 파서 구현 (시맨틱/숫자 모두 지원)
- `IsHillsSlotShape`에 "ridge" case 추가 (+ 레거시 "slope" 유지)
- `GetAutoShapeForHills`: left/right/top/bottom -> slope에서 ridge로 전환
- `ToSnapshot`: fade, noise_amount 복사 추가
- `BuildCurrentParamsText`: fade, noise_amount JSON 출력 추가

#### 2. ApplyRidge 구현 (GenStepPatches.cs)
- **수학**: smoothstep 프로파일(0~1) + Verse.Noise.Perlin 디테일
- **핵심**: profile이 항상 비음수이므로 `strength * profile`은 같은 부호 -> 상쇄 구조적 불가
- `profileStart = 1.0 - fade * 2` -> fade가 클수록 산이 맵 안쪽까지
- `transitionWidth = 0.3` -> smoothstep 전환 구간 폭
- Perlin: Verse.Noise.Perlin(freq=0.035, lac=2.0, pers=0.5, oct=4) -> 자연스러운 산 윤곽
- 노이즈 변조: `profile *= Max(0, 1 + noiseAmt * noise)` -> 프로파일 경계를 불규칙하게
- Smoothstep 유틸: `t*t*(3-2t)` Hermite 보간

#### 3. 레거시 변환 (GenStepPatches.cs)
- `ApplyRidgeFromLegacySlope`: slope -> ridge(같은 direction/strength, fade=medium, noise=medium)
- `ApplyRidgeFromLegacySplit`:
  - 산맥 모드(strength<0): split축에 수직으로 2개 ridge, fade = 0.5-gap, noise=high
  - 협곡 모드(strength>0): 동일하게 2개 ridge (양쪽 높임)

#### 4. 디스패처/정리
- case "ridge" -> ApplyRidge (신규)
- case "slope" -> ApplyRidgeFromLegacySlope (레거시)
- case "split" -> ApplyRidgeFromLegacySplit (레거시)
- ApplySlope 메서드 삭제
- ApplySplit 메서드 삭제 (주석으로 대체)
- SlopeMultiplier 상수 삭제 (미사용)
- `using System.Globalization` 추가 (CultureInfo.InvariantCulture 사용)

#### 5. 설계 문서와의 차이
- 작업 지시의 원본 수학(`profileStart = 1.0 - fade`, `edgeLow = profileStart - 0.2`)과 설계 문서의 수학(`profileStart = 1.0 - fade * 2`, `transitionWidth = 0.3`)이 미묘하게 다름
- 설계 문서(PLAN_ELEVATION_REDESIGN.md Section 6.3)의 수학을 채택: `fade*2` 방식이 fade=0.5일 때 profileStart=0이 되어 중앙에서 산이 시작하는 직관적 매핑
- Perlin: 작업 지시는 Mathf.PerlinNoise 사용, 설계 문서는 Verse.Noise.Perlin 사용 -> 설계 문서 채택 (RimWorld 네이티브 노이즈 엔진, 더 높은 품질)

### 발견/이슈
- 빌드 성공, 인게임 테스트 미실시
- rimworld-ai-dev가 시스템 프롬프트 업데이트 필요 (slope/split -> ridge)
- ridge 상쇄 불가 검증은 유닛 테스트로 자동화 권장

---

## 2026-03-21 23:55 -- 치명적 버그 3건 수정 (간헐천/광석/식생동물 밀도)

### 맥락
이전 전수 비교 분석에서 발견된 치명적 버그 3건을 수정. 사용자가 간헐천 수, 광석 밀도, 식생/동물 밀도를 지정해도 실제 맵에 반영되지 않거나 부작용이 있는 문제.

### 분석/수행 내용

#### Fix 1: 간헐천(Geyser) 패치 구현
- **문제**: `MapGenParams.GeyserCount`가 존재하지만 이를 `GenStep_ScatterGeysers`에 적용하는 패치 코드가 없었음
- **바닐라 구조 조사**: WebSearch로 RimWorld 소스 확인
  - GenStepDef defName = `SteamGeysers` (CommonMapGenerator.xml)
  - genStep Class = `GenStep_ScatterGeysers` (GenStep_Scatterer 상속)
  - 바닐라 countPer10kCellsRange = 0.7~1.0 (250x250 맵에서 약 4~6개)
- **구현**: `GeyserPatch.cs` 신규 작성
  - RuinDangerDensityPatch와 동일한 패턴 (MapGenerator.GenerateContentsIntoMap Prefix/Postfix)
  - GeyserCount == -1: 스킵, == 0: range 0으로, >= 1: 역산 공식으로 countPer10kCellsRange 계산
  - 역산 공식: `per10k = count * 10000 / 62500` (250x250 기준), +/-0.5개 범위로 min/max 설정

#### Fix 2: 광석(Ore) 원본 복원 누락
- **문제**: `OreDensityPatch.cs`가 `GenStep_ScatterLumpsMineable.Generate` Prefix에서 `countPer10kCellsRange`를 곱셈으로 변경했지만 Postfix 복원 없음
  - 결과: 맵 생성할 때마다 GenStepDef에 수정된 값이 누적 (density 2.0 → 4.0 → 16.0 → ...)
- **해결**: 전면 재작성
  - 패치 대상을 `MapGenerator.GenerateContentsIntoMap`으로 변경 (RuinDangerDensityPatch 패턴 통일)
  - Prefix에서 원본 저장 + 수정, Postfix에서 원본 복원
  - `DefDatabase<GenStepDef>.AllDefsListForReading`을 순회하여 `GenStep_ScatterLumpsMineable` 인스턴스 탐색

#### Fix 3: 식생/동물 밀도 > 1 무효
- **문제**: 기존 코드
  - `Patch_GenStep_Plants`: density < 1만 처리 (식물 제거). density > 1은 "fertility 조정으로 반영됨"이라는 주석만 있었으나 실제로는 효과 없음
  - `Patch_GenStep_Animals`: `|| density >= 1f` 가드로 density > 1 완전 무시
- **Map Designer 소스 분석** (Zylleon/MapDesigner GitHub):
  - `BiomeDef.plantDensity` × 승수 적용
  - plantDensity > 1f이면 1f로 캡하고 대신 `wildPlantRegrowDays` 줄여 빠른 재성장 → 밀집 식생
  - `BiomeDef.animalDensity` × 승수 적용 (별도 캡 없음)
  - 핵심: 이 값들을 GenStep 실행 **전**에 수정해야 GenStep_Plants/GenStep_Animals가 참조
- **구현**: `BiomeDensityPatch.cs` 신규 작성
  - MapGenerator.GenerateContentsIntoMap Prefix/Postfix 패턴
  - 모든 BiomeDef를 순회하여 원본 저장 + 수정 + 복원
  - Map Designer와 동일한 plantDensity > 1 처리 (wildPlantRegrowDays 역비례 축소)
- **기존 패치 수정**:
  - `Patch_GenStep_Plants`: density > 1일 때 early return 추가 (BiomeDensityPatch에서 처리)
  - `Patch_GenStep_Animals`: `|| density >= 1f` → `|| density > 1f`로 변경

### 발견/이슈
- `FloatRange`는 struct이므로 `scatterer.countPer10kCellsRange.min *= density`처럼 직접 필드 수정하면 복사본만 수정될 수 있음. `new FloatRange(min, max)`로 전체 대입하는 것이 안전.
- RuinDangerDensityPatch에서는 `_originalRuinRange = scatterer.countPer10kCellsRange`로 저장하고 `.min = ...` 으로 수정하는데, 이는 C#에서 property가 아닌 field 직접 접근이라 동작함. 하지만 OreDensity 이전 코드에서 `__instance.countPer10kCellsRange.min *= density`가 실제로 동작했는지는 불확실 (struct의 field라면 동작하지만 property getter라면 복사본에 대한 수정). 새 코드에서는 전체 대입으로 통일.
- 4개 패치가 모두 `GenerateContentsIntoMap`을 패치하지만 서로 다른 Def/필드를 수정하므로 충돌 없음.
- Map Preview도 `GenerateContentsIntoMap`을 호출하므로 미리보기에서도 밀도 변경이 반영됨 (의도된 동작).

---

## 2026-03-21 23:30 -- 바닐라 vs MapGenAI 지형 feature 전수 비교 분석

### 맥락
MapGenAI에서 slope(left)+slope(right)가 상쇄되어 평지가 되는 근본적 문제가 발견됨. 이 문제가 산만의 문제인지, 다른 feature에도 동일한 구조적 문제가 있는지 전수 비교 분석 필요.

### 분석/수행 내용

#### 분석 방법론
1. MapGenAI 소스 전체 읽기 (Patches/*.cs + MapGen/MapGenParams.cs)
2. 바닐라 RimWorld 디컴파일 코드 WebSearch로 확인 (josh-m/RW-Decompile, Chillu1/RimWorldDecompiled, Dyyrlysh/RimworldDecompile)
3. Map Designer (Zylleon/MapDesigner) 소스 구조 참조
4. RW 1.6 Odyssey 변경사항 확인 (TileMutatorWorker_River, CoastAngleAt 등)

#### Feature별 분석 결과

**1. 산/언덕 (elevation) -- 전면 재설계 필요**

바닐라 파이프라인:
- Perlin(0.021, lac=2.0, pers=0.5, oct=6) -> ScaleBias(0.5, 0.5) -> 0~1
- hilliness factor 곱셈: Flat=0.8, SmallHills=0.9, LargeHills=1.0, Mountainous=1.1, Impassable=1.2
- Mountainous 이상: DistFromAxis(span=0.42*mapSize) 추가 산맥 (랜덤 방향)
- elevation >= 0.7이면 GenStep_RocksFromGrid에서 산/벽으로 변환
- 0.728 이상 thin roof, 0.798 이상 thick roof
- **핵심: Perlin 노이즈가 자연스러운 봉우리/계곡 생성. 단순 경사면이 아님.**

MapGenAI 방식:
- Postfix로 바닐라 grid에 additive 적용
- slope: 선형 경사 (dx*cos+dz*sin) * multiplier
- bump: 가우시안 돌출
- radial: 중심에서 거리 비례
- split: 축 기준 양쪽 (산맥 모드: 직접 0.85f 배치 + Perlin 노이즈)
- noise: 추가 Perlin 노이즈 레이어
- ring: 도넛 형태 Gaussian

**문제점**:
- slope는 선형 경사면. 두 개의 반대 slope가 상쇄되어 평지가 됨. 바닐라는 Perlin 봉우리이므로 이런 상쇄 없음.
- hill_amount는 전체 고도를 오프셋하는 방식 (`elevation += hillAmount - 1f`). 바닐라의 hilliness factor는 곱셈. 동작 차이 있음.
- hillSize/hillSmoothness Transpiler는 바닐라 Perlin 상수를 교체하는 방식으로, Map Designer와 동일하며 적절함.
- split 산맥 모드(-strength): grid[cell] = Max(grid, 0.85f) 방식은 바닐라의 DistFromAxis+Clamp+Invert Add와 다름. 바닐라는 기존 Perlin에 추가하여 자연스럽고, MapGenAI는 기존값을 Max로 덮어쓰므로 Perlin 봉우리 패턴이 소실됨.

**2. 호수 (water) -- 부분 수정 필요**

바닐라 방식:
- GenStep_ElevationFertility는 호수를 직접 생성하지 않음
- 낮은 elevation + 바이옴 TerrainThreshold로 습지/물 타일 결정
- RW 1.6 Odyssey에서 TileMutatorWorker_Lake 등 추가 (맵 특성으로)

MapGenAI 방식:
- bump(fill="water"): 가우시안 영역 내에서 Verse.Noise.Perlin으로 불규칙 해안선 생성
- fertility에 음수 마법값(-2005, -1025) 설정하여 TerrainFromPatch에서 물 지형으로 변환
- 2단계 물: -2005=WaterDeep(강보다 우선), -1025=WaterShallow
- 해변 영역: fertility=1

**문제점**:
- fertility 마법값 인코딩 방식은 Map Designer에서 가져온 검증된 접근법. TerrainFromPatch도 Map Designer Feature_TerrainFrom.cs와 동일.
- 호수 자체는 잘 동작하지만, elevation을 Min(grid, 0.3f)로 낮추는 처리가 있어 산 위 호수가 가능하나 elevation 클램핑 순서 주의 필요.
- 호수 크기 스케일이 바닐라 대비 적절한지 검증 필요 (radiusScale=0.15 -> small=10셀, medium=30셀, large=50셀).

**3. 강 (river) -- 수정 필요 없음**

바닐라 방식 (RW 1.6):
- TileMutatorWorker_River: IsFlowingAToB(angle), GetMapEdgeNodes(angle), GetRiverCenter, GetCurveAmplitude, riverWidthNoise 등
- angle은 월드 타일 간 heading에서 결정
- 구불거림은 GetCurveAmplitude * noise displacement

MapGenAI 방식:
- RiverDirectionAngle: IsFlowingAToB/GetMapEdgeNodes Prefix로 angle 파라미터 교체
- RiverPosition: GetRiverCenter Postfix로 중심점 이동
- StraightRiver: GetCurveAmplitude Postfix로 0f 반환 + Init Postfix로 riverWidthNoise=Const(0)
- Map Designer 1.6 방식과 동일

**문제점**: 없음. Prefix/Postfix로 적절한 인터셉트 포인트 선택. 바닐라 로직 자체는 그대로 실행되며 파라미터만 교체.

**4. 해안 (coast) -- 수정 필요 없음**

바닐라 방식 (RW 1.6):
- `World.CoastAngleAt(PlanetTile, BiomeDef)`: 인접 물 바이옴 타일들의 heading 평균 계산
- 반환값: nullable float (null이면 해안 아님)
- GenStep_Terrain에서 BeachMaker에 전달

MapGenAI 방식:
- CoastPatches.cs: World.CoastAngleAt Postfix
- result == null이면 건드리지 않음 (해안 아닌 타일 보호)
- direction 문자열 → 각도 매핑: north=270, east=180, south=90, west=0

**문제점**: 없음. null 체크 + 각도 교체만 수행. 안전.

**5. 간헐천 (geysers) -- 미구현 (패치 없음)**

바닐라 방식:
- GenStep_ScatterGeysers (GenStep_Scatterer 상속): countPer10kCellsRange로 개수 결정, 자연 암반 영역에 스폰

MapGenAI 방식:
- MapGenParams.GeyserCount에 값 저장 (0~20, -1=기본)
- **패치 파일 없음!** GeyserCount가 저장되지만 실제로 GenStep_ScatterGeysers에 적용하는 코드가 없음
- SteamGeysers_Increased TileMutator로 간헐천 증가는 가능하지만, 정확한 개수 제어 불가

**문제점**: 파라미터는 존재하지만 적용 패치가 누락됨. 사용자가 geysers=5 등을 요청해도 아무 효과 없음.

**6. 동굴 (caves) -- 수정 필요 없음**

바닐라 방식 (RW 1.6):
- TileMutatorDef "Caves"가 타일에 있으면 GenStep_Caves 실행
- GenStep_Caves: Find.World.HasCaves(map.Tile) 체크 -> elevation grid에서 rock 영역(elevation>0.7) flood fill -> 300셀 이상 그룹에 open/closed tunnel 생성
- caves grid (MapGenerator.Caves)에 기록, elevation 직접 수정 안 함

MapGenAI 방식:
- MapGenParams.HasCaves + CavesExplicitlySet 플래그
- ApplyMutatorsToWorldTile()에서 TileMutatorDef "Caves"를 타일에 AddMutator/RemoveMutator
- caves=true: Caves mutator 추가 -> 바닐라 GenStep_Caves가 자연스럽게 실행
- caves=false + 명시적(caves_explicit): Caves mutator 제거
- 기본값 false(비명시적): 기존 상태 유지 (오삭제 방지)

**문제점**: 없음. TileMutator 기반으로 바닐라 로직을 그대로 활용. 안전.

**7. 광석 밀도 (ore) -- 부분 수정 필요**

바닐라 방식:
- GenStep_ScatterLumpsMineable.Generate(): countPer10kCellsRange로 개수 결정, minSpacing=5, 자연 암반에 IrregularLump 생성

MapGenAI 방식:
- OreDensityPatch.cs: GenStep_ScatterLumpsMineable.Generate Prefix
- density > 1일 때 제곱 (`density *= density`), countPer10kCellsRange.min/max에 곱셈
- Map Designer와 동일 방식

**문제점**:
- Prefix에서 __instance.countPer10kCellsRange를 직접 수정하지만, 원본 복원을 하지 않음. GenStepDef는 공유 데이터이므로, 다음 맵 생성 시에도 수정된 값이 남아있을 수 있음.
- RuinDangerDensityPatch는 Prefix/Postfix 쌍으로 원본 복원을 하는데, OreDensityPatch는 복원 코드가 없음.

**8. 식생/비옥도 (fertility) -- 부분 수정 필요**

바닐라 방식:
- GenStep_ElevationFertility: Perlin(0.021) -> ScaleBias(0.5, 0.5) -> MapGenerator.Fertility
- GenStep_Terrain의 TerrainFrom: fertility 값으로 바이옴별 TerrainThreshold에 따라 지형 결정
- GenStep_Plants: 바이옴 식물 목록에서 적합한 위치에 스폰

MapGenAI 방식:
- FertilityOffset: Postfix에서 전체 fertility grid에 오프셋 가산 (물 영역 -500 이하 제외)
- VegetationDensity: GenStep_Plants Postfix에서 density<1이면 확률적 제거, >1이면 fertility 조정으로 간접 반영
- AnimalDensity: GenStep_Animals Postfix에서 density<1이면 확률적 제거

**문제점**:
- FertilityOffset은 단순 가산. 바닐라 fertility는 0~1 Perlin이므로 offset 가산은 합리적.
- VegetationDensity > 1일 때 추가 식물을 직접 스폰하지 않고 "fertility 조정으로 반영됨"이라고 주석이 있지만, FertilityOffset 외에 별도 처리가 없음. VegetationDensity > 1 효과가 제한적일 수 있음.
- AnimalDensity > 1은 아무 효과 없음 (if density >= 1f return).

**9. 석재 종류/수량 (rock types) -- 수정 필요 없음**

바닐라 방식 (RW 1.6):
- World.NaturalRockTypesIn(PlanetTile): 바이옴 forceRockTypes 우선, 없으면 랜덤 2~3종 선택
- RockAllowedInBiome 필터 적용
- tile hashCode 기반 시드로 일관성 보장

MapGenAI 방식:
- RockTypesPatch.cs: World.NaturalRockTypesIn Finalizer
- RockTypes 있으면 해당 석재만 반환 (isNaturalRock 검증)
- RockCount 있으면 목록 잘라내기/추가
- Finalizer 사용으로 예외 안전
- Rand.PushState/PopState로 시드 보호

**문제점**: 없음. Finalizer로 안전하게 처리. 유효성 검증 충분.

**10. 폐허/위험 밀도 (ruin/danger) -- 수정 필요 없음**

바닐라 방식:
- GenStep_Scatterer 상속: ScatterRuinsSimple, ScatterShrines
- countPer10kCellsRange로 개수 결정

MapGenAI 방식:
- RuinDangerDensityPatch.cs: GenerateContentsIntoMap Prefix/Postfix
- Prefix: GenStepDef에서 countPer10kCellsRange 수정 (폐허: density>1 3승, 위험: density>1 4승)
- Postfix: 원본 복원 (공유 데이터 보호)
- Map Designer와 동일 방식

**문제점**: 없음. Prefix/Postfix 쌍으로 원본 복원. 안전.

**11. 돌덩어리 (rock chunks) -- 수정 필요 없음**

바닐라 방식:
- GenStep_RockChunks: 산 지형 근처에 돌덩어리 스폰

MapGenAI 방식:
- RockChunkPatch.cs: GenStep_RockChunks.Generate Prefix
- HasRockChunks=false이면 return false로 전체 스킵
- Map Designer와 동일 방식

**문제점**: 없음. 단순 on/off. 안전.

**12. TileMutator 시스템 -- 부분 수정 필요**

바닐라 방식 (RW 1.6 Odyssey):
- TileMutatorDef: 월드 타일에 특성 부여 (Caves, HotSprings, LavaCaves, MineralRich 등)
- TileMutatorWorker: Init/Generate로 맵 생성 파이프라인에 참여
- categories로 충돌 방지 (같은 카테고리 mutator 교체)

MapGenAI 방식:
- MapGenParams.Mutators/RemoveMutators 목록
- ApplyMutatorsToWorldTile(): 월드 타일에 직접 AddMutator/RemoveMutator
- Reset()에서 원본 복원 (_originalMutatorDefNames 저장)
- 기존 TileMutator 패치(GenerateMap/GenerateContentsIntoMap Prefix/Postfix)는 주석 처리되어 비활성

**문제점**:
- 비활성화된 패치 코드가 600줄 이상 주석으로 남아있어 가독성 저하.
- ApplyMutatorsToWorldTile에서 카테고리 충돌 체크가 없음 (기존 패치에는 있었음). 같은 카테고리의 mutator 중복 추가 가능.

### 발견/이슈

**발견한 치명적 문제:**
1. GeyserCount 미구현: 파라미터만 있고 패치 없음
2. OreDensityPatch 원본 미복원: 공유 GenStepDef 오염 가능
3. slope 상쇄 문제: 이것이 분석의 시작점이었으며, 확인 결과 slope 자체가 산을 표현하는 부적절한 방법

**경미한 이슈:**
4. VegetationDensity > 1 효과 미미
5. AnimalDensity > 1 무효
6. TileMutator 비활성 코드 600줄+ 잔류
7. TileMutator 카테고리 충돌 체크 누락

---

## 2026-03-21 21:30 -- 자연 산 vs MapGenAI 산 구조 분석

### 맥락
MapGenAI에서 심각한 아키텍처 문제 발견: 자연 산 타일에서는 추가 산 요청이 올바르게 누적되지만(Case A), 평지에서 연속으로 산을 요청하면 이전 산이 사라지는 문제(Case B). MDP 원칙 위반.

### 분석/수행 내용

**바닐라 GenStep_ElevationFertility 파이프라인:**
1. Perlin(0.021, lacunarity=2.0, persistence=0.5, 6 octaves)
2. ScaleBias(0.5, 0.5) -> 0~1 정규화
3. hilliness factor 곱셈: Flat=0.8, SmallHills=0.9, LargeHills=1.0, Mountainous=1.1, Impassable=1.2
4. Mountainous 이상: DistFromAxis(span=0.42) 추가 산맥 (랜덤 사면)
5. 최종 elevation >= 0.7이면 산

**MapGenAI Postfix:**
- 바닐라 Generate() 완료 후 additive로 shapes 적용
- Apply() 시 ElevationShapes.Clear() 후 새 shapes만 적용 (345-356행)
- 코드 주석: "이전 상태 보존/병합 로직 없음"

**근본 원인:**
- Case A: 자연 산 = hilliness factor + DistFromAxis로 base elevation에 내재. Postfix shapes가 바뀌어도 base는 유지.
- Case B: AI 산 = ElevationShapes에만 존재. shapes 교체 시 이전 산 소멸. base elevation은 Flat(0.8 factor)이므로 산 없음.

**Option E (하이브리드) 설계:**
- `data.elevation_shapes == null` -> 이전 ElevationShapes 유지 (변경 의도 없음)
- `data.elevation_shapes == []` -> 의도적 제거 (Clear)
- `data.elevation_shapes`에 값 있음 -> 전체 교체 (MDP, 현재와 동일)
- LLM 프롬프트에 현재 shapes 상태 포함

### 발견/이슈
- MapGenTuning.ElevationFactorFlat = 0.8은 ScaleBias 후 최대 0.8이므로 0.7 임계값 근처. 거의 산 없는 평지.
- Map Designer도 동일한 additive 패턴 사용 (MountainSettingsPatch.cs Postfix). 그러나 Map Designer는 설정 UI가 있어서 사용자가 직접 전체 설정을 관리. LLM은 이것을 못 함.
- Map Preview 재생성 시 새 랜덤 시드 사용 -> grid 캐시 방식은 이음새 문제 발생 가능.

---

## 2026-03-21 — Anchor 기반 자유 형태 지형 v2 (PLAN_SHAPES_V2.md)

### 맥락
v1에서 LLM이 좌표를 직접 지정하는 방식(circle center:[0.28,0.64])은 정밀도 부족 문제가 있었다. v2는 blueprint_ai의 attach 패턴을 terrain에 적용하여 LLM이 의미적 참조(pos:"center", anchor:"face", at:"top_left", size:"large")만 출력하고 ShapeBuilder가 실제 좌표를 계산하는 구조로 전환.

### 구조 결정

**ShapeBuilder** (MapGen/ShapeBuilder.cs, 신규):
- `ResolveAll(List<ShapePrimitive>)` → `Dictionary<string, ResolvedShape>`
- Position 프리셋 9종: center(0.5,0.5), top(0.5,0.8), bottom(0.5,0.2) 등
- Size → Radius: tiny=0.05, small=0.10, medium=0.20, large=0.35, huge=0.50
- Anchor+At: 12종. top/bottom/left/right (외부, newR*0.5 걸침), top_left/top_right/bottom_left/bottom_right (모서리, newR*0.3), inner_left/right/top/bottom (내부, anchorR*0.4)
- Offset: left/right/up/down (반지름의 60% 이동)
- BBox: rect는 aspect 고려, circle/ngon/star는 외접원 기준

**SdfEngine** (MapGen/SdfEngine.cs, 신규):
- 4종으로 축소 (v1의 7종에서): circle, rect, ngon, star
- circle: `length(p-c) - r`
- rect: Quilez box SDF, aspect로 halfW/halfH 계산
- ngon: 세그먼트 접기(fold) 방식. 2pi/n 주기로 접어서 한 변까지 거리 계산
- star: outer tip ↔ inner valley 선분까지 signed distance. innerR = outerR * 0.4
- 모든 SDF에 회전 지원 (역회전 + 90도 보정으로 위쪽 시작)
- Boolean 6종 유지 (v1과 동일): union/sub/inter + smooth 3종

**ApplyComposite** (GenStepPatches.cs):
- v1과 동일한 래스터라이징 파이프라인이지만, ShapeBuilder를 통한 좌표 해석 단계 추가
- 1) ShapeBuilder.ResolveAll → 2) SDF 함수 생성 → 3) compose 체인 → 4) 맵 래스터라이징
- compose chain에서 `add`는 단일 도형 적용, `union/sub/inter`는 두 도형 조합
- 마지막 연산의 e/f가 최종 elevation/falloff 결정

**JSON 파싱** (Dialog_TextToMap.cs):
- type="composite"일 때 shapes[]/compose[] 중첩 파싱
- shapes: id, prim, pos, anchor, at, offset, size, n, rot, aspect
- compose: op, s, a, b, from, out, k, e, f, hasE
- compose의 "out"은 C# 예약어 → @out으로 접근
- 최종 e<0이면 fill="water" 자동 설정 → 기존 물/비-물 순서 정렬 활용

**직렬화** (MapGenParams.cs):
- ToSnapshot()에 shapes/compose 깊은 복사 (Clone 메서드)
- BuildCurrentParamsText()에 composite JSON 직렬화 추가

### v1 → v2 차이점
- v1: LLM이 center:[0.28,0.64], r:0.15 직접 지정 → 정밀도 문제
- v2: LLM이 pos:"center", size:"large", anchor:"face", at:"top_left" → ShapeBuilder가 좌표 계산
- v1: 7종 SDF (circle/ellipse/rect/tri/poly/star/heart)
- v2: 4종 SDF (circle/rect/ngon/star) — 복잡한 형태는 boolean 조합으로 구현 (하트 = circle*2 + ngon(3,rot:180) → union)
- v1의 SDF 코드는 GenStepPatches.cs 내부에 있었으나, v2에서는 별도 SdfEngine.cs + ShapeBuilder.cs로 분리

### 발견/이슈
- ngon SDF: 90도 보정이 필요 (기본 Quilez 구현은 첫 꼭지점이 오른쪽이지만, 직관적으로 위쪽 시작이 자연스러움)
- star SDF: Quilez sdStar 원본은 m 파라미터 사용. innerR=outerR*0.4 고정이 더 직관적
- anchor 참조 순서: shapes 배열에서 앞에 선언된 도형만 anchor로 참조 가능 (resolved Dictionary 기반)
- fill="water" 자동 설정: compose 마지막 e<0이면 자동으로 water 처리하여 기존 순서 정렬(비-물 먼저, 물 나중)과 호환

---

## 2026-03-21 — CSG+SDF 래스터라이저 구현

### 맥락
PLAN_SHAPES.md Stage 1 구현. LLM이 기본 도형(circle/rect/tri/star/heart 등)을 조합하여 자유 형태 지형을 생성하는 CSG+SDF 시스템의 C# 백엔드.

### 구조 결정

**데이터 클래스** (MapGenParams.cs):
- `ShapePrimitive`: id, prim(도형타입), center, r, w, h, verts, rot 등. 7종 도형의 유니언 필드.
- `ComposeOp`: op(연산타입), s/a/b/from/@out(피연산자 ID), k(smooth), e(elevation), f(falloff).
- `ElevationShape`에 `List<ShapePrimitive> shapes`와 `List<ComposeOp> compose` 추가. type="composite"일 때만 사용.

**SDF 구현** (GenStepPatches.cs):
- 7종 SDF 함수 모두 `static float Sdf*(Vector2 p, ...)` 시그니처.
- `RotatePoint()` 헬퍼로 회전 지원. 점을 역회전하여 도형 로컬 좌표로 변환.
- circle: 가장 단순, `length(p-c) - r`.
- ellipse: 스케일 보정 원 근사. 정확한 Quilez 타원 SDF는 반복 풀이가 필요해서 근사 사용.
- rect: Quilez box SDF. `length(max(d,0)) + min(max(d.x,d.y), 0)`.
- tri: Quilez triangle SDF. 3변 clamped projection + winding sign.
- poly: 일반 N각형. winding number + edge distance. O(N) per query.
- star: 대칭 접기 + tip-valley edge distance. outerR/innerR 직접 지정. Quilez sdStar의 m 파라미터 대신 직관적 inner/outer radius 사용.
- heart: Quilez sdHeart 정확한 해석적 공식 포팅. upper arc + lower cusp 두 영역 분기.

**불리언 연산**:
- 6종: union, subtract, intersect + smooth 3종.
- smooth union: Quilez polynomial smooth min. `k *= 4; h = max(k - |a-b|, 0); min(a,b) - h*h*0.25/k`.
- smooth subtract/intersect: smooth union의 부호 조합으로 유도.

**래스터라이저** (ApplyComposite):
1. shapes[] -> `Dictionary<string, Func<Vector2, float>>` SDF 함수 사전
2. compose[] 순회 -> 각 op에 따라 SDF 조합, out ID로 사전에 저장
3. 마지막 연산의 SDF + elevation + falloff 확정
4. 맵 셀 순회: 정규화 좌표 -> SDF 평가 -> Smoothstep -> elevation/water 적용

**좌표계**:
- 정규화: x=0 왼쪽, x=1 오른쪽, z=0 아래(남), z=1 위(북)
- RimWorld: cell.x 왼->오, cell.z 아래->위 (동일 방향)
- 변환: `px = cell.x / mapW`, `pz = cell.z / mapH`

**JSON 파싱** (Dialog_TextToMap.cs + SimpleJson.cs):
- SimpleJson에 중첩 배열(nested array) 파싱 추가: `GetNestedArray`, `ParseNestedArray`.
- verts 필드가 `[[x,z],[x,z],...]` 형태이므로 중첩 배열 지원 필요.
- center는 `[x,z]` 단순 배열 -> GetArray로 파싱.
- compose의 "out" 키는 C# 예약어이므로 `@out`으로 접근.

**직렬화** (ToSnapshot, BuildCurrentParamsText):
- shapes/compose 깊은 복사 (snapshot용 verts는 Clone).
- BuildCurrentParamsText에서 composite shapes/compose를 JSON 형태로 직렬화하여 LLM이 현재 상태를 볼 수 있도록.

### 발견/이슈
- star SDF: Quilez의 m 파라미터와 우리의 innerR 매핑이 복잡해서 직접 edge-distance 방식 사용. 동일한 결과지만 구현이 더 직관적.
- heart SDF: 초기 구현이 부정확해서 Quilez 원본 공식으로 재구현.
- ellipse SDF: 정확한 해는 반복 풀이 필요. 지형 생성 용도로는 근사로 충분.
- composite water 처리: fill="water" 메커니즘 대신 compose chain의 e<0으로 판정. 기존 물/비-물 ordering과 독립적으로 동작.
- SimpleJson 한계: 기존에 중첩 배열을 지원하지 않아서 ParseNestedArray 추가 필요했음.

---
