# MapGenAI 개발 지도

2026-09-13 개발 브랜치: `dev`. 보존판: `v1.6` (`1439bbd`). 규칙은 Rimworld 루트의 `CLAUDE.md`와 `.claude/rules/`를 참조한다.

- [사용자용 모드 소개·사용법](docs/description-ko.md): 기존 기능, dev 추가·개선 사항, 텍스트 영역·재료·폐허 위치 지정과 이미지 일시 중단. 기능 변경 시 함께 갱신한다.

- `dev/Source/Core/TextToMapSettings.cs`: 간편 모델명 입력/API 목록 조회, 고급 공급자 설정. GUI listing Begin/End는 `DoWindowContents`에서 한 번만 수행하고 고급 본문은 그룹 밖에서 그린다. 모델 결과는 요청별 큐·계정/주소별 캐시·대상 유효성 검사로 전달한다. `tools/runtime-probe/launch.ps1 -Render -Settings`로 설정 화면을 검증한다.

- `dev/Source/UI/Dialog_TextToMap.cs`: 타일에 고정된 대화, 실제 변경 안내, Undo/Reset/프리셋. `ShapeEditPrompt`가 대상 ID 편집 예시를 제공한다.
- `dev/Source/LLM/`: 공급자별 HTTP 요청, 엄격한 JSON 응답 경계, 취소/늦은 응답 폐기. 이미지 입력은 선택한 모델의 vision 지원이 필요하다.
- `dev/Source/MapGen/MapParameterParser.cs`, `MapStateEditor.cs`: 입력 파싱과 변경 키만 병합하는 순수 계산.
- `dev/Source/MapGen/ShapeEdits.cs`, `ShapeValidation.cs`, `SdfComposite.cs`: 도형 ID 편집, 기하/연산 제한, 실제 SDF 격자 계산.
- `dev/Source/MapGen/ContourWarp.cs`: composite 전용 `edge_roughness`(생략/none=0, low=.35, medium=.65, high=1). 도형 로컬 좌표와 안정 ID 해시로 공유 좌표장을 세 번 변형하며 전역 Rand를 소비하지 않는다. `noise_amount`/기존 bump·ring과 구별한다. `tools/runtime-probe/launch.ps1 -Render -NaturalShapes -Language Korean`은 실제 저장·Undo·생성 비교를 수행한다. 근거는 `docs/analysis/2026-09-13-natural-shapes/`.
- `dev/Source/MapGen/MapGenParams.cs`, `WorldTileEditor.cs`, `MapGenAIWorldComponent.cs`: 타일 상태 적용, 원래 월드 특징과 외부 변경 보존, 타일별 상태 저장.
- `dev/Source/MapGen/FeaturePolicy.cs`, `FeatureEditPrompt.cs`: 실제 Odyssey/외부 모드 정의·worker 조건을 프롬프트 후보와 적용/프리셋에 공유. 월드 River/Coast 연결은 보존하며 개별 변형 제거는 기본 연결로 복귀한다. 기존 저장의 전체 억제는 Load/생성 진입에서 이전한다. 확률·랜드마크 추첨 조건과 환경 조건을 구별한다. `-FeaturePolicy [-Landmarks] [-FeatureResponses 응답폴더]`로 실제 생성·Gemini 응답 재생을 검증한다. 이전 `-FeatureRemoval`도 현행 정책 검증으로 연결한다. [검증 보고서](docs/analysis/2026-09-13-feature-policy/report.md), [텍스트 우선 계획](docs/text-first-plan-ko.md).
- `dev/Source/MapGen/RegionGrid.cs`, `TerrainMaterials.cs`: 기존 SDF/bump/ring이 산출한 내부 마스크·재료 층 공유, 활성 영구 TerrainDef 해석. 합성 도형의 최상위 fill은 렌더 영역 재료 override다. 강/바다/도로는 최종 채움에서 보호한다.
- `StructurePlans.cs`, `PlacementPlanner.cs`, `AuthoringGeneration.cs`: ID별 구조물 계획/변경/Scribe, 전체 면적 탐색, 좌표/영역/경계·회전·최소 간격 제약, 현재 ruin 생성기. `SpatialRelation.cs`는 실제 강/물/산/영역 안쪽 경계의 정확한 유클리드 거리와 방향을 계산한다. 공간 부족/대상 부재 시 위치 지정 batch는 미생성·실패 보고. 다른 구조물은 같은 배치 경로에 생성기를 연결한다.
- `Patches/AuthoringGenerationPatch.cs`: 생성별 추가 단계400(지형/도로 이후 재료),800(기존 구조물 이후·시작 지점 이전 폐허). `StructurePreviewPatch.cs`는 GenSpawn을 막는 Map Preview의 텍스처에 같은 벽 계획을 그린다. 실제 맵 건물과 별도로 검사하며 preview와 완성 맵의 충돌 조건은 다를 수 있다.
- `ImageInput/ImageFeatureGate.cs`: 사용자 결정에 따라 false. UI 진입/적용과 생성 스냅샷에서 차단하며 저장 데이터를 삭제하지 않는다. 아래 이미지 경로는 재활성화 전까지 보관 코드다.
- `dev/Source/MapGen/GenerationContext.cs`, `dev/Source/Patches/`: 생성할 타일의 고정 스냅샷과 RimWorld 생성 단계 연결. 공유 정의 변경은 finalizer에서도 복원한다.
- `dev/Source/ImageInput/`, `dev/Source/UI/Dialog_ImageMap.cs`: PNG/JPEG/EXIF, 원본 색상 그룹/마스크를 AI가 분류하는 기본 경로, 선택 가능한 다각형 추론/팔레트, 영역 교정. 대상은 배치를 읽을 수 있는 참고 맵이며 설명은 선택 사항이다. 새 이미지 높이 우선 옵션은 토양의 기존 산을 지우고 이미지+SDF 높이를 Odyssey elevation mutator 뒤 복원한다. 기존 저장(false)·N칸은 원래 의미를 유지한다. 얇은 지형/비슷한 색상/삽입 그림 오분류는 남았다.
- `GenerationContext.CaptureImageElevation/RestoreImageElevation`, `Patches/ImageElevationPriorityPatch.cs`: 생성별 높이 스냅샷. raw image 재적용으로 후속 SDF 편집을 지우지 않도록 주의한다. 고도 이외 mutator 효과·광물 덩어리는 유지된다.
- `dev/Source/MapGen/MapStateCodec.cs`, `PresetManager.cs`, `TileMapState.cs`: 전체 상태 프리셋 v2와 기존 프리셋 읽기, Scribe 저장. 새 프리셋을 v1.6에서 읽는 것은 보장하지 않는다.
- `dev/Source/Tests/`: production 소스를 링크하는 오프라인 회귀. Verse/Unity shim은 순수 계산용이며 Scribe/실제 생성 검증을 대신하지 않는다.
- `tools/runtime-probe/`: 별도 모드·새 프로필의 실제 RimWorld 테스트. 설치된 MapGenAI나 사용자 세이브를 교체하지 않는다. `-TextRegions [-TextResponses 응답폴더]`는 실제 용암/폐허/이미지 중단/Scribe/Dialog/배경 Map Preview를 검증한다. `-PreviewOnly`는 배경 미리보기만 검사한다. 이전 이미지 전용 실행은 보관된 도구이며 현재 기능 검증의 성공 근거로 사용하지 않는다. `-ImageInputs/-ImageStates`는 실제 Unity 전처리/전체 생성/단계 추적을 수행한다. 종료 후 `cleanup.ps1`이 검증된 임시 모드 폴더를 자동 삭제하며, 결과와 프로필은 남긴다.
- `tools/provider-probe/`: 저장된 테스트 설정으로 실제 Gemini 호출, 응답·상태·정답 마스크 비교. 비용이 발생하므로 기본 오프라인 테스트에 포함하지 않는다.
- `docs/analysis/2026-09-13-implementation/`: Fable 요청·답변, 실제 실패/성공 증거, 개발판 결과 보고.
- `docs/analysis/2026-09-13-real-inputs/`: 범례 없는 실제 입력 3종·두 모델·실제 생성 비교, Fable 자문, 원본 실패·제한. `tools/real-image-report.py`로 저장된 결과 비교 그림을 재생성한다.

빌드: 저장소에서 `dotnet build dev/Source/MapGenAI.csproj --nologo`.
회귀: `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj`.
실게임: 위 빌드 후 `dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo`, `tools/runtime-probe/launch.ps1`.
실게임 검증은 도구가 만든 고유 프로필과 `MAPGENAI_PROBE_OWNED` 표시 폴더만 정리해야 한다.

개발 DLL은 `dev/Assemblies/`에 빌드된다. `dist/Assemblies/MapGenAI.dll`과 설치본은 v1.6 보존 대상이며 개발 빌드로 자동 덮어쓰지 않는다.
