# DEV_LOG_RAW.md -- MapGenAI 개발 로그 (날것)

## 2026-03-22 -- MDP WorldComponent 리팩토링

### 설계 결정
- MapGenParams 정적 필드를 "WorldComponent의 캐시"로 재정의. 패치들(GenStepPatches, RiverPatches 등)은 변경 없이 MapGenParams.XXX를 그대로 읽음
- 병합 로직을 ParseParams → Apply로 이동. ParseParams는 JSON 파싱만, Apply에서 WorldComponent 기반 병합
- Dialog 닫기 = 취소 유지. WorldComponent 스냅샷을 대화 시작 시 저장하고 닫기 시 복원

### 고민
- elevation_shapes 부분 업데이트 vs 전체 교체: shape에 고유 ID가 없어서 부분 업데이트 불가. LLM에게 전체 목록을 보내라고 지시하는 현재 방식 유지. explicitKeys에 "elevation_shapes"가 없으면 기존 유지.
- 사용자의 MDP 원칙: "20턴을 거쳐도 처음 바꾸기 시작했을 때와 동일해야 됨". WorldComponent에는 최종 상태만 있고 이력 없음. undo 스택은 Dialog 내부에서만 존재.

## 2026-03-21 -- LLM 프롬프트 ridge 업데이트 과정

- 대각선 산맥 few-shot 교체 시 고민: split(direction=top_left)는 축 기반 분할이라 ridge 1개로 정확히 대체 불가. ridge(direction=top_left)는 "좌상단이 높은 산"이지 "대각선 축 산맥"이 아님. 결국 예시를 "왼쪽에 산"으로 변경하여 ridge의 정확한 용법을 보여주는 방향으로 결정.
- JSON schema에서 gap 필드: split 전용이므로 프롬프트 schema에서 제거. 하지만 파싱 코드(line 1053)는 유지 (레거시 호환 -- C# 코드에서 slope/split -> ridge 변환이 별도로 처리될 예정).
- BuildCurrentParamsText는 이미 fade/noise_amount를 직렬화하고 있어서 변경 불필요했음 (이전 csharp-dev 세션에서 추가된 것으로 추정).

## 2026-03-21 -- 치명적 버그 3건 수정 과정

### 간헐천 패치 -- GenStep_ScatterGeysers 구조 조사
- Chillu1/RimWorldDecompiled에서 GenStep_ScatterGeysers.cs 찾으려 했으나 404 (경로 구조 다름)
- 대안: RimWorld 설치 경로 `Data/Core/Defs/MapGeneration/CommonMapGenerator.xml` 직접 검색
- 발견: `<defName>SteamGeysers</defName>`, `<genStep Class="GenStep_ScatterGeysers">`, `<countPer10kCellsRange>0.7~1</countPer10kCellsRange>`
- GenStep_ScatterGeysers → GenStep_ScatterThings → GenStep_Scatterer 상속 체인
- countPer10kCellsRange → 실제 개수 변환 공식: `count = Rand.Range(min, max) * mapCells / 10000`

### 광석 원본 복원 -- FloatRange struct 주의
- 이전 코드 `__instance.countPer10kCellsRange.min *= density`가 실제로 동작하는지 불확실
  - countPer10kCellsRange가 field → 동작 (struct의 field에 직접 접근)
  - countPer10kCellsRange가 property → 복사본 수정 (무효)
- 실제로는 GenStep_Scatterer의 public field이므로 동작하긴 함
- 하지만 안전을 위해 `new FloatRange(min, max)` 전체 대입으로 통일

### 식생/동물 밀도 > 1 -- Map Designer 소스 분석
- Zylleon/MapDesigner GitHub에서 HelperMethods.cs 확인
- 핵심 발견: `plantDensity > 1f`이면 `wildPlantRegrowDays /= plantDensity` 후 `plantDensity = 1f`
  - RimWorld의 GenStep_Plants가 plantDensity를 맵 전체 식물 수 상한으로 사용
  - plantDensity > 1이면 추가 효과 없음 (이미 모든 셀에 식물 배치 시도)
  - 대신 regrowDays를 줄이면 빈 셀에 더 빨리 식물이 자라 밀집 효과
- animalDensity는 별도 캡 없음 (WildAnimalSpawner가 참조)
- 기존 코드의 "density > 1은 fertility 조정으로 반영됨" 주석은 오류 -- fertility offset과 vegetation density는 별개 파라미터
