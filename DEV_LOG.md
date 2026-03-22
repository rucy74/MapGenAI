# DEV_LOG.md -- MapGenAI 개발 로그

## 2026-03-22 -- MDP 아키텍처: WorldComponent 기반 타일 상태 영구 저장

### 핵심 변경: MapGenParams 정적 필드 → WorldComponent + explicitKeys 병합
- **TileMapState.cs** 신규: 타일의 현재 맵 생성 상태 (IExposable, 세이브/로드 지원)
- **MapGenAIWorldComponent.cs** 신규: Dictionary<int, TileMapState>로 타일별 상태 영구 저장
- **ElevationShape**: IExposable + Clone() 추가
- **MapGenParams.Apply()**: WorldComponent에서 기존 상태 로드 → explicitKeys로 LLM이 보낸 필드만 업데이트 → WorldComponent에 저장. 정적 필드는 패치용 캐시.
- **MapParamsData.explicitKeys**: LLM JSON에 실제 존재한 키만 HashSet으로 추적
- **ParseParams()**: 기존 "이전 값 유지" 병합 로직 제거. JSON 키 존재 여부를 explicitKeys로 추적하고, 병합은 Apply()에서 WorldComponent 기반으로 수행.
- **Dialog_TextToMap**: 생성자에서 LoadFromTile(), PostClose에서 WorldComponent 스냅샷 복원 (취소 시), Reset/Undo도 WorldComponent 연동
- **MapGenParams.LoadFromTile()**: WorldComponent에서 로드 → 정적 필드 적용
- **MapGenParams.ClearTile()**: WorldComponent에서 삭제 + Reset

### MDP 원칙 구현
- "왼쪽에 산 → 오른쪽에 산 추가": LLM이 ridge(right)만 보내도 기존 ridge(left)는 WorldComponent에 유지됨
- 대화 닫기(X) = 취소: WorldComponent를 대화 시작 시점 스냅샷으로 복원
- "맵 생성" = 확정: WorldComponent 상태 유지
- undo 외에는 이전 액션 정보가 코드에 남지 않음
- 빌드 성공 (0 error, 1 warning)

## 2026-03-21 -- LLM 프롬프트 slope/split -> ridge 전면 업데이트

- Dialog_TextToMap.cs 시스템 프롬프트에서 slope/split 완전 제거, ridge로 교체
- JSON schema에 fade/noise_amount 필드 추가
- elevation_shapes 가이드, 규칙, few-shot 예시 (한/영, 내륙/해안) 모두 수정
- JSON 파싱 및 modExample 직렬화에 fade/noise_amount 추가
- 빌드 성공 (0 warning, 0 error)

## 2026-03-21 -- 치명적 버그 3건 수정

### 간헐천(Geyser) 패치 구현
- `GeyserPatch.cs` 신규: GenStepDef `SteamGeysers`의 countPer10kCellsRange를 GeyserCount 기반으로 조정
- Prefix/Postfix 패턴으로 원본 저장/복원 (GenStepDef는 공유 데이터)

### 광석(Ore) 원본 복원 누락 수정
- `OreDensityPatch.cs` 전면 재작성: GenerateContentsIntoMap Prefix/Postfix 패턴으로 변경
- 이전 코드: GenStep_ScatterLumpsMineable.Generate Prefix만 → 값 누적 버그
- 수정: Prefix에서 원본 저장 + 수정, Postfix에서 원본 복원

### 식생/동물 밀도 > 1 효과 구현
- `BiomeDensityPatch.cs` 신규: Map Designer 방식으로 BiomeDef.plantDensity/animalDensity 수정
- plantDensity > 1f → 1f로 캡 + wildPlantRegrowDays 축소 (Map Designer 동일)
- GenStepPatches.cs 수정: density > 1일 때 기존 Postfix 스킵 (BiomeDensityPatch에서 처리)
