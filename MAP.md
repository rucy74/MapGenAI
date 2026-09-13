# MapGenAI 개발 지도

2026-09-13 개발 브랜치: `dev`. 보존판: `v1.6` (`1439bbd`). 규칙은 Rimworld 루트의 `CLAUDE.md`와 `.claude/rules/`를 참조한다.

- `dev/Source/UI/Dialog_TextToMap.cs`: 타일에 고정된 대화, 실제 변경 안내, Undo/Reset/프리셋. `ShapeEditPrompt`가 대상 ID 편집 예시를 제공한다.
- `dev/Source/LLM/`: 공급자별 HTTP 요청, 엄격한 JSON 응답 경계, 취소/늦은 응답 폐기. 이미지 입력은 선택한 모델의 vision 지원이 필요하다.
- `dev/Source/MapGen/MapParameterParser.cs`, `MapStateEditor.cs`: 입력 파싱과 변경 키만 병합하는 순수 계산.
- `dev/Source/MapGen/ShapeEdits.cs`, `ShapeValidation.cs`, `SdfComposite.cs`: 도형 ID 편집, 기하/연산 제한, 실제 SDF 격자 계산.
- `dev/Source/MapGen/MapGenParams.cs`, `WorldTileEditor.cs`, `MapGenAIWorldComponent.cs`: 타일 상태 적용, 원래 월드 특징과 외부 변경 보존, 타일별 상태 저장.
- `dev/Source/MapGen/GenerationContext.cs`, `dev/Source/Patches/`: 생성할 타일의 고정 스냅샷과 RimWorld 생성 단계 연결. 공유 정의 변경은 finalizer에서도 복원한다.
- `dev/Source/ImageInput/`, `dev/Source/UI/Dialog_ImageMap.cs`: PNG/JPEG/EXIF, 원본 색상 그룹/마스크를 AI가 분류하는 기본 경로, 선택 가능한 다각형 추론/팔레트, 영역 교정. 대상은 배치를 읽을 수 있는 참고 맵이며 설명은 선택 사항이다. 새 이미지 높이 우선 옵션은 토양의 기존 산을 지우고 이미지+SDF 높이를 Odyssey elevation mutator 뒤 복원한다. 기존 저장(false)·N칸은 원래 의미를 유지한다. 얇은 지형/비슷한 색상/삽입 그림 오분류는 남았다.
- `GenerationContext.CaptureImageElevation/RestoreImageElevation`, `Patches/ImageElevationPriorityPatch.cs`: 생성별 높이 스냅샷. raw image 재적용으로 후속 SDF 편집을 지우지 않도록 주의한다. 고도 이외 mutator 효과·광물 덩어리는 유지된다.
- `dev/Source/MapGen/MapStateCodec.cs`, `PresetManager.cs`, `TileMapState.cs`: 전체 상태 프리셋 v2와 기존 프리셋 읽기, Scribe 저장. 새 프리셋을 v1.6에서 읽는 것은 보장하지 않는다.
- `dev/Source/Tests/`: production 소스를 링크하는 오프라인 회귀. Verse/Unity shim은 순수 계산용이며 Scribe/실제 생성 검증을 대신하지 않는다.
- `tools/runtime-probe/`: 별도 모드·새 프로필의 실제 RimWorld 테스트. 설치된 MapGenAI나 사용자 세이브를 교체하지 않는다. `-Render`는 의도적 생성 실패를 생략하고 이미지 창 렌더를 캡처한다. `-ImageInputs/-ImageStates`는 실제 Unity 전처리/전체 생성/단계 추적을 수행한다. 종료 후 `cleanup.ps1`이 검증된 임시 모드 폴더를 자동 삭제하며, 결과와 프로필은 남긴다.
- `tools/provider-probe/`: 저장된 테스트 설정으로 실제 Gemini 호출, 응답·상태·정답 마스크 비교. 비용이 발생하므로 기본 오프라인 테스트에 포함하지 않는다.
- `docs/analysis/2026-09-13-implementation/`: Fable 요청·답변, 실제 실패/성공 증거, 개발판 결과 보고.
- `docs/analysis/2026-09-13-real-inputs/`: 범례 없는 실제 입력 3종·두 모델·실제 생성 비교, Fable 자문, 원본 실패·제한. `tools/real-image-report.py`로 저장된 결과 비교 그림을 재생성한다.

빌드: 저장소에서 `dotnet build dev/Source/MapGenAI.csproj --nologo`.
회귀: `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj`.
실게임: 위 빌드 후 `dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo`, `tools/runtime-probe/launch.ps1`.
실게임 검증은 도구가 만든 고유 프로필과 `MAPGENAI_PROBE_OWNED` 표시 폴더만 정리해야 한다.

개발 DLL은 `dev/Assemblies/`에 빌드된다. `dist/Assemblies/MapGenAI.dll`과 설치본은 v1.6 보존 대상이며 개발 빌드로 자동 덮어쓰지 않는다.
