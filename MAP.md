# MapGenAI 개발 지도

- 복합 요청 확장 전 기준: `dev-before-compound-2026-09-19` → `4a89b06`. `StructurePlan.region_part`는 생략/inside가 기존 영역, enclosed가 닫힌 고리에 둘러싸인 빈 내부다. 토양 비율과 독립적으로 원본 산 ID를 참조한다. Clone/Scribe/preset/update/한영 표시를 보존하며 region 해제 시 part도 null로 해제한다. `AuthoringGeneration.PlaceStructures`는 해당 마스크로 전체 footprint와 region_edge 거리를 검사하고 산 전용 통로의 전체 경로를 구조물 점유 후보에서 제외한다. 기존 지형 칠하기와 native UsedRects 예약 범위는 그대로다. `TextRegionPrompt`의 복합 조건·최소 변경 안내와 dry interior 별도 평탄화 예시를 함께 갱신했다. [복합 요청 검증](docs/analysis/2026-09-19-compound-plan/report.md).

- 대표 지형 확장 전 복구 기준: `dev-landform-baseline-2026-09-15` → `4b05726`. 기존 원형/자연윤곽/면적채움/재료/구조물 생성 의미를 유지하는 것이 사용자 최우선 요구다.
- `PassageGeometry.cs`, `PassageGeneration.cs`, `PassagePrompt.cs`: 명시적 `type:passage`, ordered `points`2..32, `width`1..64칸, 마른 `fill`. `scope:mountains`는 모든 통로 적용 직전 높이>=.7인 칸과 교차하며 평지의 높이/비옥도/재료를 보존한다. 생략/`full`은 기존 전체 경로 동작. 겹치는 통로도 같은 사전 높이를 사용한다. clone/Scribe/preset/partial edit와 한·영 설명에 범위 포함. 4방향 연결 중심선을 실제 칸 크기로 넓히고 기존 높이/물 도형 뒤에 적용한다. 기존 `SdfComposite`는 수정하지 않는다. stage400에서 기존 재료 적용 경로를 재사용하고 `UsedRects`에 실제 적용 부분만 예약한다. 생성 완료 후 실제 편집 부분의 물/위험/장애물/높이를 검사하며 산과 겹치지 않으면 무변경 안내. 예약을 무시하는 외부 생성기는 후속 장애물을 만들 수 있다. [이전 측정·회귀](docs/analysis/2026-09-15-landform-suite/report.md).

2026-09-14 개발 브랜치: `dev`. 보존판: `v1.6` (`1439bbd`), 텍스트 확장 전 복구 태그: `dev-text-baseline-2026-09-14` (`f9f6aaa`). 규칙은 Rimworld 루트의 `CLAUDE.md`와 `.claude/rules/`를 참조한다.

- [사용자용 모드 소개·사용법](docs/description-ko.md): 기존 기능, dev 추가·개선 사항, 텍스트 영역·재료·폐허 위치 지정과 이미지 일시 중단. 기능 변경 시 함께 갱신한다.

- `dev/Source/Core/TextToMapSettings.cs`: 간편 모델명 입력/API 목록 조회, 고급 공급자 설정. GUI listing Begin/End는 `DoWindowContents`에서 한 번만 수행하고 고급 본문은 그룹 밖에서 그린다. 모델 결과는 요청별 큐·계정/주소별 캐시·대상 유효성 검사로 전달한다. `tools/runtime-probe/launch.ps1 -Render -Settings`로 설정 화면을 검증한다.

- `dev/Source/UI/Dialog_TextToMap.cs`: 타일에 고정된 대화, 실제 변경 안내, Undo/Reset/프리셋. `ShapeEditPrompt`가 대상 ID 편집 예시를 제공한다.
- `dev/Source/LLM/`: 공급자별 HTTP 요청, 엄격한 JSON 응답 경계, 취소/늦은 응답 폐기. 이미지 입력은 선택한 모델의 vision 지원이 필요하다.
- `dev/Source/MapGen/MapParameterParser.cs`, `MapStateEditor.cs`: 입력 파싱과 변경 키만 병합하는 순수 계산.
- `dev/Source/MapGen/ShapeEdits.cs`, `ShapeValidation.cs`, `SdfComposite.cs`: 도형 ID 편집, 기하/연산 제한, 실제 SDF 격자 계산.
- 마른 composite 통로/평탄화: `fill:soil` + `e:0.05`는 절대 높이 교체이며 채움이 있어도 생성 초기부터 산을 낮춘다. 무채움 음수 e는 구버전 호수 호환 의미를 유지한다. `ManualFailureTests`, `ManualFailureProbe`와 provider `ManualFailureBench`는 사용자 D04/F03을 재현한다. `launch.ps1 -ManualFailures 응답근거폴더 [-ManualResponses 새모델응답폴더]`; 응답폴더 생략 시 실제 시스템 프롬프트만 캡처. [검증·명령](docs/analysis/2026-09-14-manual-fixes/report.md).
- `dev/Source/MapGen/ContourWarp.cs`: composite 전용 `edge_roughness`(생략/none=0, low=.35, medium=.65, high=1). 도형 로컬 좌표와 안정 ID 해시로 공유 좌표장을 세 번 변형하며 전역 Rand를 소비하지 않는다. `noise_amount`/기존 bump·ring과 구별한다. `tools/runtime-probe/launch.ps1 -Render -NaturalShapes -Language Korean`은 실제 저장·Undo·생성 비교를 수행한다. 근거는 `docs/analysis/2026-09-13-natural-shapes/`.
- `dev/Source/LLM/StructuredChat.cs`: 사용자 요청·상태·카탈로그를 유지한 채 형식 오류만 공급자별1회 재요청. strict parser/취소/원자적 적용은 유지한다. `FeatureFeedbackProbe`와 provider `feedback` 모드는 VLE 활성/비활성 특징·평지 온천·형식복구를 검사한다.
- `dev/Source/MapGen/MapGenParams.cs`, `WorldTileEditor.cs`, `MapGenAIWorldComponent.cs`: 타일 상태 적용, 원래 월드 특징과 외부 변경 보존, 타일별 상태 저장.
- `MapGenParams.ValidatePatch`는 적용과 같은 후보 병합·재료·월드 계획의 dry-run이다. Dialog 후보표도 현재 설정에서 이 검사를 사용하고 충돌/교체 대상을 구별한다. `InvalidEditExplanation`과 `StartChat`은 실패 명령을 UI thread에서 검사하고 한 번 ask 설명만 재요청하며 재요청 generate는 적용하지 않는다. `EditPreflightProbe`/provider `preflight`는 원본 온천/Pond 오류, 다중대안 동의, 임의교체 거부·닫기·Undo·로드된 후보표를 확인한다.
- `dev/Source/MapGen/FeaturePolicy.cs`, `FeatureEditPrompt.cs`: 실제 Odyssey/외부 모드 정의·worker 조건을 프롬프트 후보와 적용/프리셋에 공유. 월드 River/Coast 연결은 보존하며 개별 변형 제거는 기본 연결로 복귀한다. 기존 저장의 전체 억제는 Load/생성 진입에서 이전한다. 확률·랜드마크 추첨 조건과 환경 조건을 구별한다. `-FeaturePolicy [-Landmarks] [-FeatureResponses 응답폴더]`로 실제 생성·Gemini 응답 재생을 검증한다. 이전 `-FeatureRemoval`도 현행 정책 검증으로 연결한다. [검증 보고서](docs/analysis/2026-09-13-feature-policy/report.md), [텍스트 우선 계획](docs/text-first-plan-ko.md).
- `dev/Source/MapGen/RegionGrid.cs`, `TerrainMaterials.cs`: 기존 SDF/bump/ring이 산출한 내부 마스크·재료 층 공유, 활성 영구 TerrainDef 해석. 합성 도형의 최상위 fill은 렌더 영역 재료 override다. 강/바다/도로는 최종 채움에서 보호한다.
- `RegionCoverage.cs`: `region_fill`은 기존 composite/bump/ring의 실제 마스크를 참조하여 inside 또는 enclosed의 사용 가능한 칸 수로 coverage0..1을 맞춘다. source/part/fraction은 편집·clone·Scribe·프리셋에 포함하며 참조 삭제는 함께 제거/재지정해야 한다. `AuthoringGeneration.ApplyCoverage`는 기존 목표 재료를 포함해 계산하고 건물·캐릭터·산·월드 연결을 보존한다. 790 단계에서 구조물 배치용 마스크를 만들고, 1900 단계에서 늦게 생성된 기본/DLC 구조물 이후 비율을 재검사한다. 다시 칠할 때는 모드가 칠한 뒤 외부에서 바뀌지 않은 칸만 원상태로 돌려 재선택한다. [실제 재현·최종 검증](docs/analysis/2026-09-15-region-coverage/report.md).
- `StructurePlans.cs`, `PlacementPlanner.cs`, `AuthoringGeneration.cs`: ID별 구조물 계획/변경/Scribe, 전체 면적 탐색, 좌표/영역/경계·최소 간격 제약, ruin/ancient_danger 분기. `SpatialRelation.cs`는 실제 강/물/산/영역 안쪽 경계의 정확한 유클리드 거리와 방향을 계산한다. 폐허는0/90/180/270도 회전한다. 공간 부족/대상 부재 시 위치 지정 batch는 미생성·실패 보고.
- `AncientDangerGeneration.cs`: 기본 ancientTemple BaseGen 경로, 실제 지붕/내부/전리품/경고와 peacefulTemples 규칙. BaseGen 공유 객체는 finally 복원하며 내부 예외를 삼키는 생성기의 실제 결과를 검사한다. 크기15~20·계획별1~2·전체최대4·회전0만 허용한다. 미리보기는 주황색 예약 테두리만, native pawn/내부 생성은 full map만 수행한다. 임의 모드·퀘스트 구조물 adapter는 후속이다.
- `Patches/AuthoringGenerationPatch.cs`: 생성별 추가 단계400(지형/도로 이후 재료),790(면적 비율 채움),800(기존 구조물 이후·시작 지점 이전 폐허),1900(기본/DLC 구조물 뒤 비율 재조정). `StructurePreviewPatch.cs`는 GenSpawn을 막는 Map Preview의 텍스처에 같은 벽 계획을 그린다. 실제 맵 건물과 별도로 검사하며 preview와 완성 맵의 충돌 조건은 다를 수 있다.
- `ImageInput/ImageFeatureGate.cs`: 사용자 결정에 따라 false. UI 진입/적용과 생성 스냅샷에서 차단하며 저장 데이터를 삭제하지 않는다. 아래 이미지 경로는 재활성화 전까지 보관 코드다.
- `dev/Source/MapGen/GenerationContext.cs`, `dev/Source/Patches/`: 생성할 타일의 고정 스냅샷과 RimWorld 생성 단계 연결. 공유 정의 변경은 finalizer에서도 복원한다.
- `dev/Source/ImageInput/`, `dev/Source/UI/Dialog_ImageMap.cs`: PNG/JPEG/EXIF, 원본 색상 그룹/마스크를 AI가 분류하는 기본 경로, 선택 가능한 다각형 추론/팔레트, 영역 교정. 대상은 배치를 읽을 수 있는 참고 맵이며 설명은 선택 사항이다. 새 이미지 높이 우선 옵션은 토양의 기존 산을 지우고 이미지+SDF 높이를 Odyssey elevation mutator 뒤 복원한다. 기존 저장(false)·N칸은 원래 의미를 유지한다. 얇은 지형/비슷한 색상/삽입 그림 오분류는 남았다.
- `GenerationContext.CaptureImageElevation/RestoreImageElevation`, `Patches/ImageElevationPriorityPatch.cs`: 생성별 높이 스냅샷. raw image 재적용으로 후속 SDF 편집을 지우지 않도록 주의한다. 고도 이외 mutator 효과·광물 덩어리는 유지된다.
- `dev/Source/MapGen/MapStateCodec.cs`, `PresetManager.cs`, `TileMapState.cs`: 전체 상태 프리셋 v2와 기존 프리셋 읽기, Scribe 저장. 새 프리셋을 v1.6에서 읽는 것은 보장하지 않는다.
- `dev/Source/Tests/`: production 소스를 링크하는 오프라인 회귀. Verse/Unity shim은 순수 계산용이며 Scribe/실제 생성 검증을 대신하지 않는다.
- `tools/runtime-probe/`: 별도 모드·새 프로필의 실제 RimWorld 테스트. 설치된 MapGenAI나 사용자 세이브를 교체하지 않는다. `-TextRegions [-TextResponses 응답폴더]`는 실제 용암/폐허/이미지 중단/Scribe/Dialog/배경 Map Preview를 검증한다. `-PreviewOnly`는 배경 미리보기만 검사한다. 이전 이미지 전용 실행은 보관된 도구이며 현재 기능 검증의 성공 근거로 사용하지 않는다. `-ImageInputs/-ImageStates`는 실제 Unity 전처리/전체 생성/단계 추적을 수행한다. 종료 후 `cleanup.ps1`이 검증된 임시 모드 폴더를 자동 삭제하며, 결과와 프로필은 남긴다.
- `tools/provider-probe/`: 저장된 테스트 설정으로 실제 Gemini 호출, 응답·상태·정답 마스크 비교. 비용이 발생하므로 기본 오프라인 테스트에 포함하지 않는다.
- `tools/runtime-probe/CompoundProbe.cs`, `SpatialProbe.cs`, `AncientProbe.cs`: 각각 연속 복합 요청, 실제 지형 관계, native 고대 위협 생성 검사. launch의 `-CompoundResponses`, `-Spatial [-SpatialResponses]`, `-Ancient [-AncientResponses]`로 실제 모델 응답을 Dialog/Undo에 재생한다. [최종 검증](docs/analysis/2026-09-14-text-expansion/report.md).
- `docs/analysis/2026-09-13-implementation/`: Fable 요청·답변, 실제 실패/성공 증거, 개발판 결과 보고.
- `docs/analysis/2026-09-13-real-inputs/`: 범례 없는 실제 입력 3종·두 모델·실제 생성 비교, Fable 자문, 원본 실패·제한. `tools/real-image-report.py`로 저장된 결과 비교 그림을 재생성한다.

빌드: 저장소에서 `dotnet build dev/Source/MapGenAI.csproj --nologo`.
회귀: `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj`.
실게임: 위 빌드 후 `dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo`, `tools/runtime-probe/launch.ps1`.
실게임 검증은 도구가 만든 고유 프로필과 `MAPGENAI_PROBE_OWNED` 표시 폴더만 정리해야 한다.

개발 DLL은 `dev/Assemblies/`에 빌드된다. `dist/Assemblies/MapGenAI.dll`과 설치본은 v1.6 보존 대상이며 개발 빌드로 자동 덮어쓰지 않는다.


## 실행할 설정이 포함된 추천

- `MapPlanDescription.cs`: 검증 전후 상태로 플레이어용 설명을 작성한다. `Dialog_TextToMap.DefinitionText`는 실행 시점의 DefDatabase label/description을 제공하므로 원래 defName이나 생성한 ID를 선택 문구로 노출하지 않는다. 기존 MapStateDescription은 진단용으로 보존한다. [표시 개선 근거](docs/analysis/2026-09-15-readable-choices/report.md).

- `dev/Source/LLM/RecommendationPlan.cs`: recommend/options 계약, 독립 patch 검증, 직렬화한 명령과 실제 변경 요약 보관. 임의 제목·설명을 실제 효과로 표시하지 않는다.
- `StructuredChat`은 추천 요청의 즉시 generate와 설정 없는 번호 목록을 형식 복구 대상으로 처리한다. `Dialog_TextToMap`은 UI thread에서 후보 전체를 ValidatePatch한 뒤 공개하고, 부적합 batch에는 한 번 수정 요청한다.
- 번호/버튼 선택은 보관 명령을 재검증해 원래 ApplyPatch/Undo 경로로 적용한다. 상태·실제 특징·바이옴·산악도 변경, reset/undo/close/preset 시 폐기한다. 이미지 gate OFF이면 버튼도 그리지 않는다.
- `tools/runtime-probe/RecommendationProbe.cs`, `tools/provider-probe/RecommendationBench.cs`: 실제 Dialog 선택·재시도·취소·오아시스 완성맵 및 새 모델 응답. [근거](docs/analysis/2026-09-15-recommendations/report.md).

---
## 작성 이력
- 2026-09-14 23:34 — 활성 모드 특징 자동 인식, 온천 직접 추가 조건, 제한된 JSON 형식 복구 안내.
- 2026-09-15 00:01 — 현재 특징 조합 후보표와 적용 전 공통 검증·설명 재요청 안내.
- 2026-09-15 00:34 — 추천 실행 계획 검증·저장 선택, 오아시스 주변 토양, 이미지 버튼 숨김.
- 2026-09-15 22:06 — 플레이어용 지형 설명과 런타임 번역 조회, 선택·적용·Undo/화면 검증.
- 2026-09-15 22:58 — 실제 내부 칸 수 기반 비율 채움과 후속 편집·늦은 구조물 보존, 영어 실제 추천 창 검증.
