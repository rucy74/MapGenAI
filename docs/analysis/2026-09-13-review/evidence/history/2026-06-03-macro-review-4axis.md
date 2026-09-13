# MapGenAI 거시 검토 (4-axis panel + 검증) #diagnosis

> 작성: 2026-06-03 20:17 · 방법: `/panel`(verdict-free 형성 피드백) 4회차 + 사실 검증 2건
> 대상: `active/mapgen_ai` (v1.1~1.5 출시 + image-to-map WIP, ~10K LOC) · 2개월 공백 후 재개 준비
> 성격: 결함 헌팅(verdict)이 아니라 **거시 검토** — 발상·구조·품질·LLM통합의 큰 그림

## 배경

2026-04-13 이후 2개월 공백. 재개 전 프로젝트를 4개 관점(axis)으로 거시 검토. 각 axis를 panel(critic/architect/ideator/code-reviewer/test-engineer/ai-dev 중 3명, 상호 blind, 형성적 산문)로 돌리고, 검토에서 나온 **검증 가능한 사실 2건**(테스트 빌드 / thinkingBudget)을 실측으로 확정했다.

axis: A1 아이디어 / A3 아키텍처 / A2 코드 품질(scope 한정) / A4 LLM 통합 견고성. (재개 정합성 axis는 사용자 요청으로 제외.)

---

## A1 — 아이디어 (제품 발상)

세 멤버(critic·ideator·architect)가 같은 지점에서 만났다: **"자연어 채팅" 자체는 제품의 본질이 아니다.** 본질을 각자 다르게 명명 — critic "compositional 다축 입력 + 누적 편집 루프", architect "의도→즉시 시각 피드백→보정의 대화 루프", ideator "holistic 의도를 다중 파라미터로 컴파일하는 intent compiler".

- **image-to-map 4회 pivot의 의미** — 셋 다 "기술 실패가 아니라 근본 문제의 증상"으로 봄. 진단 축만 갈림: critic "정밀입력↔LLM해석 가치 양립불가", architect "유저가 형상을 이미 아는가↔모르는가의 가치명제 미설정", ideator "feature fit 부재(천장이 blurry)".
- **외부 좌표(ideator 조사)**: Map Designer(234K 구독, 슬라이더를 더 정밀하게·무료·무지연), Map Reroll, Geological Landforms(결정적·무료). "UI 치환" 프레임은 이 좌표에서 정면 열세.
- **발산 처방**: critic=grayscale heightmap 직접입력(결정적·LLM-free)으로 방향 역전 / architect=채팅 루프의 진입점으로 종속 또는 별도 모드 분리 / ideator=reframe B(LLM을 vanilla seed 큐레이터로 파이프라인 역전)·C(gameplay 제약 저작) 등.

## A3 — 아키텍처

세 멤버(architect·critic·ideator)가 코드를 직접 읽고 강하게 수렴:

- **상태 표현 중복** (최대 부채): 같은 ~25 파라미터가 `TileMapState`·정적 필드·`MapParamsData`·LLM JSON 스키마·프롬프트 텍스트 등 5~6 형태로 **손으로 동기화** (`MapGenParams.cs:486-828`). 파라미터 1개 추가의 한계비용 5~7곳. **이미 현실화된 drift**: `ExposeData`가 `compositeShapes`를 직렬화에서 빠뜨려 composite shape가 reload 시 유실(`:34-45`, `ToSnapshot:821-826`).
- **fertility 마법값 채널 과부하**: fertility가 비옥도+terrain class+물깊이 3중 인코딩. magic number(`-1005`/`-1025`/`-2005`)가 인코더 여러 곳(`SdfComposite.cs:171,345,350`)+디코더(`TerrainFromPatch.cs:49-68`)에 공유 상수 없이 흩어짐.
- **threading contract 미설정**: `MapGenParams`/`PendingImageMap`의 "메인스레드only" 단언이 같은 파일 Map Preview `[ThreadStatic]`(`GenStepPatches.cs:667`)과 자기모순. (critic 가설: 이미 "수정됨" 표기된 기하 버그가 이 state-leak 증상일 수 있음 — /trace 영역, 미검증)
- **ILLMClient Vision 누수**: `SendChatWithImageAsync`가 인터페이스에 있는데 3/4 구현이 `NotSupportedException`(ISP 위반). `IVisionClient` 분리 권고(단 vision 프로바이더가 영영 Gemini 하나면 throwing stub이 더 쌈).
- **Harmony 14패치 — 두 tier**: additive Postfix(충돌 내성) vs Prefix-replace(`return false`, 충돌 위험). `TerrainFromPatch`의 1.6/1.5 `TargetMethod` 폴백은 모범, 타입직참(`Patch_GenStep_ElevationFertility`)은 그 타입 사라지면 로드시 hard-fail.
- **ideator 최고-레버리지 reframe**: ElevationShape+SdfComposite가 이미 "node-graph-of-grid-functions"(GeologicalLandforms 코어)인데 이름만 안 붙임. ElevationShape/palette/image-to-map을 세 주입지점이 아니라 **grid-contributor 리스트로 통합**하면 reframe #3·#5·#7이 한 곳으로 수렴.

## A2 — 코드 품질 (scope 한정: A3 중복 제외)

code-reviewer·critic·test-engineer 투입. 가장 큰 발견은 **테스트 dead 상태**(아래 검증 ①).

- **테스트 비균질**: 2,800줄이 세 부류 — 진짜 단위테스트(`ImageInputTests`, 강함) / 비결정 eval(`TestBench` 실 Gemini 호출, validation 관대 "ask면 통과") / smoke(`GeminiVisionSmoke` text필드 존재만 확인).
- **자체 SimpleJson 무방비**: untrusted LLM 출력 받는 자체 재귀 파서인데 직접 테스트 0건. truncated 입력에 예외 대신 "부분 파싱을 정상인 척" 반환(`:156-173`).
- **조용한 실패**: `GetBool` 침묵(`v=="true"`라 `"True"`/`"1"`은 false), 무음 catch(`RestoreMutatorsFromWorldTile:732`), unknownCount 높아도 "성공" 한 줄(`GridBuilder:361`→`Dialog:1052`).
- **shim 실행 회피**(test-engineer): `VerseShim`이 `Find.WorldSelector=>null`로 `ApplyMutatorsToWorldTile`을 early-return시켜 mutator 로직이 한 줄도 실행 안 됨 — 통과카운트에도 안 잡혀 구멍이 비가시화.

## A4 — LLM 통합 견고성

critic·code-reviewer·ai-dev 투입. 멈춘 지점(MAX_TOKENS)의 근본원인에 수렴(아래 검증 ②).

- **maxOutputTokens 65536은 미봉책** — 코드 주석(`LLMImageSegmenter.cs:52-54`)이 이미 "thinking 토큰이 maxOutputTokens에 합산"을 진단. 진짜 손잡이는 `thinkingConfig`(미사용).
- **finishReason이 제어신호가 아니라 로깅용**(`GeminiClient:117-124`): MAX_TOKENS/RECITATION/SAFETY 감지하나 분기 없이 부분응답 반환 → "토큰 부족"이 "파싱 실패"로 둔갑.
- **compact tiling이 실패를 성공으로 위장**: 절반 응답을 `% chars.Length`로 반복 복제(`GridParser`), unknownCount도 낮게 나와 정상 오인. `isTiled` 신호 없음.
- **토큰 정책 파편화**: `Dialog_TextToMap:878,896`이 인자 없이 호출해 계산된 65536이 기본 8192로 떨어짐 → 한 fix가 모든 경로에 안 닿음.
- **견고성 비대칭**: 재시도·CT 위생이 Gemini vision 경로에만, 텍스트 경로·타 프로바이더는 무방비. "멀티 프로바이더 지원이 드롭다운에만 있고 견고성에는 없음."

---

## 검증 ① — 테스트 dead 상태 (확정)

`dotnet build` 직접 재현 (2026-06-03 20:xx):

| 프로젝트 | 결과 |
|---|---|
| `TextToMap.Tests.csproj` | **빌드 실패, 오류 36** (CS0246 `IExposable`/`TileMapState`/`ShapePrimitive`/`Hilliness`/`LudeonTK` 미참조, CS0234 `MapGenAI.ImageInput` 없음, CS0579 sibling obj 오염) → `MdpApplyTests`·`PaletteLoadSmoke`·`GeminiVisionSmoke` **실행 불가 (dead)** |
| `ImageInputTests.csproj` | **빌드 성공 + 139 PASS / 0 FAIL** (GridBuilder/GridParser/GridSizeCalculator/BiomeValidator/Downscaler/BiomeContext/Segmenter) |

→ "테스트 2,800줄" 중 실제로 도는 건 `ImageInputTests`뿐. `MapGenParams`/MDP/mutator/SimpleJson 검증 묶음은 Verse 어셈블리 미참조로 컴파일조차 안 됨. `MdpApplyTests` 헤더의 "FAIL 예상" 주석은 MDP 리팩터 이전 화석.

## 검증 ② — thinkingBudget paired 실험 (부분확정/단순해결 반증)

ai-dev가 ApiTests에 paired 모드 추가 후 실 Gemini 호출 18회 (ref_examples 3장 × A/B × 3회, gemini-2.5-flash):

| 지표 (n=9) | A (현행 65536, thinking on) | B (thinkingBudget=0, 2048) |
|---|---|---|
| 평균 thoughtsTokenCount | 6,381 (max 15,272) | 0 |
| 평균 totalTokens | 6,867 | 560 (-91.8%) |
| 평균 레이턴시 | 55.9초 (2회 100초 타임아웃) | 6.4초 (-88.5%) |
| strict 파싱 성공 | 0/9 | 8/9 |

- ✅ **thinking 토큰이 비용·레이턴시·중단압박의 근본 driver — 확정.**
- ❌ **thinkingBudget=0이 해결책 — 반증.** B의 88.9% 파싱성공은 **degenerate 출력**(전부 `G`, 동일 행 반복)으로 얻은 가짜 성공. A는 풍부한 grid를 내지만 행 길이 불일치(21/19/17자)로 strict 파싱만 실패(내용은 살아있음).
- 🎯 **방향**: 중간 thinkingBudget(512~1024) + maxOutputTokens 2048~4096 + GridParser의 "내용 맞지만 형식 흐트러진" 케이스 구제.
- API 검증: `generationConfig.thinkingConfig.thinkingBudget=0`이 v1beta generateContent에서 수용됨(B 전수 HTTP 200 + thoughts=0)을 live로 확인.

---

## 🔭 cross-axis 메타 패턴

- **패턴 ① "근본 변수를 안 잡고 표면을 여러 번 누른다"** — A1(image-to-map 4회 pivot=정체성 미설정), A3(25파라미터 5형태 동기화 / fertility 3중 인코딩), A4(thinking 비결정성을 token·temp·tiling·grid 4손잡이로). 같은 구조가 세 축에서 독립 출현.
- **패턴 ② "조용한 실패"** — A2(unknownCount→Grass 성공메시지, 무음 catch) + A4(tiling 위장, finishReason 무시).
- **패턴 ③ "검증층의 비어있음"** — A2(테스트 dead, SimpleJson 테스트 0) + A4(스키마 검증 없음, T4 blocker).
- **A1↔A3/A4 연결**: A1에서 architect가 우려한 "공유 파이프라인이 본체 천장을 낮춘다"가 A3의 `PendingImageMap` 결합 + A4 vision-only 견고성으로 코드 레벨 증거를 얻음.

### 가장 강한 관통 통찰: "parse-OK ≠ 맵 품질"

세 번 독립 수렴 — ① 2026-04-07 adversarial-review 미해결 노트 "파싱 성공 vs 맵 품질 목적 충돌" ② A2 critic "파싱 성공 vs 맵 품질 괴리" ③ 검증② 실험 "thinking 끄면 형식 완벽하나 내용 degenerate". **이 프로젝트의 핵심 미해결 문제는 '구조 유효성'과 '시맨틱 정확성'을 한 메트릭으로 묶어 본다는 것.**

---

## 다음 액션 후보 (미결정 — 사용자 판단)

- **중간 thinkingBudget 실험 확장**: 512/1024 × maxOutputTokens 2048~4096, + 시맨틱 품질 메트릭(unknownCount, degenerate 검출, IoU). 단순 파싱률 금지.
- **테스트 복구**: `TextToMap.Tests.csproj`에 Verse 어셈블리 참조 추가 또는 폐기 결정. sibling obj 빌드 오염(디렉토리 중첩) 정리. `MdpApplyTests` "영구 FAIL 박제" → `[KnownBug]` 격리 또는 수정.
- **멤버 REFLECTION 4건** (knowledge 반영 후보, 미처리): panel-mode verdict 억제(code-reviewer) / 테스트 검토 전 build-run으로 dead 확정(critic) / shim early-return 회피 체크(test-engineer) / 구조유효성↔시맨틱정확성 분리(ai-dev).
- **방향 결정** (A1): image-to-map(현 LLM-segmentation)을 계속할지, grayscale 직접입력/intent-compiler/제약저작 등으로 재프레임할지.

> 주의: 본 문서는 panel(형성 피드백)+검증 결과의 종합이며, 처방(어느 방향이 옳다)은 사용자 결정 영역. panel 자체는 verdict-free.

---

## 부록 — harness/agentic 에이전트 방향 탐색 (2026-06-03 추가)

사용자 질문: API 단발이 아니라 **harness 기반 에이전트**(OpenClaw·Hermes Agent류)로 맵을 생성하면 품질이 어떨까. local gaming GPU 모델 또는 저가 API로.

### 정체 (외부 조사)
- **OpenClaw** = 에이전트 오케스트레이션 게이트웨이 (ACP로 외부 코딩 harness 구동). 모델 아님.
- **Hermes Agent** (Nous, 2026-02) = 모델 교체형 자율 에이전트 프레임워크 (학습 루프 + 11 tool-call 파서). Hermes 4 *모델*은 오히려 tool-loop 비추(chat 튜닝).
- 둘 다 "생성→검증→재시도 루프 + 도구"를 모델에 입히는 **계층**.

### 핵심
harness가 단발 대비 나은 건 **검증 루프를 강제**하기 때문 — 이건 본 검토가 "없다"고 지적한 바로 그 층(parse-OK≠품질). 단 효과는 루프에 **시맨틱 검증 도구**(degenerate 검출·다양성·이미지 유사도)를 넣느냐에 달림. 형식 검증(GridParser)만 있으면 큰 모델·에이전트도 degenerate를 "성공"이라 함. **상한 = 검증 도구의 질 × 모델 공간추론력.**

### 실행 옵션 (2026)
- **local 24GB (RTX 3090/4090)**: Qwen3 VL 30B A3B(vision+tool use), Qwen3.6 27B(텍스트 agentic TAU2 94.2%). 비용 0, 세팅 장벽 높음. vision projector가 VRAM 잠식.
- **저가 API**: DeepSeek V3.2 $0.14/$0.28 per 1M, Qwen3 Coder(라이트 $0.70/day), GLM 5.1. 장벽 낮음, 키+소액.
- 작은 local 모델은 "이미지→정확한 지형 이해"가 frontier보다 약함 — 루프가 degenerate는 걸러도 vision 이해 한계는 못 메움.

### 통찰
agentic은 image-to-map보다 **대화형 누적 편집 루프**("더 험준하게"→수정→자기검증→재제시)에 자연스럽고, 이게 A1이 짚은 제품의 진짜 가치("누적 편집 루프")와 일치. vision 불필요 → local 작은 모델로도 가능. + 진짜 강한 루프는 **프리뷰 이미지를 다시 vision으로 읽어 "의도대로 됐나" 확인**하는 단계까지 가야 완성(메인 에이전트 직접 시연 시 확인된 한계: params 논리 검증까지는 되나 렌더 결과는 ground truth가 없으면 못 봄).

### 권장 prototype 경로
이미지가 아니라 **텍스트 대화형 편집**에, 저가 API(DeepSeek/Qwen)로 **검증 도구 설계 먼저** → 되면 local 이식. "제품 완성"보다 **"실험·학습"** 트랙에 적합.

출처: OpenClaw docs / Hermes Agent docs(Nous) / localllm.in·toolhalla(24GB 모델) / costgoat·mindstudio(API 가격).

---
## 작성 이력
- 2026-06-03 20:17 — 초안. 4-axis panel(A1/A3/A2/A4) + 검증 ①dead-test ②thinkingBudget 실험 종합. 메인 직접 작성.
- 2026-06-03 21:19 — 부록 추가: harness/agentic 방향 탐색 (OpenClaw·Hermes 외부 조사 + 검토 연결 + 메인 에이전트 맵 생성 자기검증 시연).
