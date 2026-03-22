# MapGen AI — 프로젝트 구조

```
mapgen_ai/
├── dev/                          # 개발용 전체 모드 폴더
│   ├── About/
│   │   ├── About.xml             # 모드 메타데이터 (패키지ID, 의존성, 버전)
│   │   └── Preview.png           # 스팀 워크샵 썸네일
│   ├── Assemblies/               # 빌드 출력 (MapGenAI.dll)
│   ├── Defs/                     # XML 정의 (현재 미사용 — 패치 방식)
│   ├── Languages/                # 번역 키 (Korean, English, Japanese, ChineseSimplified)
│   └── Source/
│       ├── MapGenAI.csproj       # 빌드 설정 (net472, RimWorld DLL 참조)
│       ├── Core/
│       │   ├── TextToMapMod.cs   # 모드 진입점 — Harmony.PatchAll() 등록
│       │   ├── TextToMapSettings.cs  # 모드 설정창 UI (Settings API)
│       │   ├── ApiConfig.cs      # API 키/모델/URL 설정 데이터
│       │   └── LLMProviders.cs   # 11개 LLM 프로바이더 enum + URL 레지스트리
│       ├── LLM/
│       │   ├── ILLMClient.cs     # 공통 인터페이스 + LLMClientFactory
│       │   ├── GeminiClient.cs   # Gemini API 클라이언트
│       │   ├── OpenAIClient.cs   # OpenAI-compatible 클라이언트 (다수 프로바이더 공유)
│       │   └── LocalClient.cs    # Ollama / LM Studio (OpenAIClient 래퍼)
│       ├── MapGen/
│       │   ├── MapGenParams.cs   # 파라미터 정적 저장소 + ElevationShape 모델
│       │   └── PresetManager.cs  # 프리셋 JSON 저장/불러오기/삭제
│       ├── Patches/
│       │   ├── GenStepPatches.cs # 핵심 — ElevationFertility, Plants, Animals 패치
│       │   ├── MountainSettingsPatch.cs   # 산 크기/부드러움 Transpiler
│       │   ├── RiverPatches.cs   # 강 방향/위치/일자강
│       │   ├── CoastPatches.cs   # 해안 방향 (N/E/S/W)
│       │   ├── RockTypesPatch.cs # 석재 종류/수량
│       │   ├── OreDensityPatch.cs        # 광석 밀도
│       │   ├── RuinDangerDensityPatch.cs # 폐허/위험 밀도
│       │   ├── RockChunkPatch.cs         # 돌덩어리 생성 토글
│       │   ├── TerrainFromPatch.cs       # 지형 패치
│       │   ├── MapPreviewIntegration.cs  # Map Preview API 연동
│       │   ├── MapPreviewToolbarButton.cs # 미리보기 툴바 버튼
│       │   └── WorldInspectPane_Patch.cs # 월드맵 AI 버튼 주입
│       ├── UI/
│       │   ├── Dialog_TextToMap.cs  # 채팅 다이얼로그 (핵심 UI)
│       │   ├── SimpleJson.cs        # 외부 의존 없는 JSON 파서/직렬화
│       │   └── L10n.cs              # 언어 감지 헬퍼
│       └── Tests/
│           ├── TestBench.cs         # 오프라인 API 테스트 (30개 케이스)
│           ├── InGameTestRunner.cs  # 인게임 DebugAction 테스트 (13개)
│           └── TextToMap.Tests.csproj  # net8.0/net10.0 콘솔 테스트 앱
│
├── dist/                         # 배포용 (RimWorld/Mods/에 복사)
│   └── About/                    # 최소 메타데이터만 포함
│
├── docs/
│   ├── PLAN.md                   # Phase별 개발 계획 + Map Designer 기능 대비표
│   ├── DEV_LOG.md                # 세션별 완료 작업 (정리)
│   ├── DEV_LOG_RAW.md            # 실패/시행착오 원본 노트
│   ├── PROMPT_ENGINEERING.md     # 시스템 프롬프트 설계 문서
│   ├── TEST_BENCH.md             # 테스트 케이스 명세
│   ├── dev_config.json           # 테스트용 API 키 (git 제외)
│   └── assets/                   # 스크린샷, HTML 미리보기
│
└── agents/                       # Claude Code 에이전트 역할 분담
    ├── README.md                 # 에이전트별 담당 범위 + 주요 경로
    ├── rimworld-csharp-dev/      # C# 코어 / Harmony 패치 담당
    ├── rimworld-ai-dev/          # LLM 클라이언트 / 프롬프트 담당
    ├── rimworld-ui-dev/          # 설정 UI / 다이얼로그 담당
    ├── rimworld-xml-dev/         # 언어 파일 / Def 담당
    ├── rimworld-test-engineer/   # 테스트 설계 담당
    └── log/                      # 날짜별 작업 로그
```
