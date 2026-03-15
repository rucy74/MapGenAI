# MapGen AI - 개발 로그

---

## 2026-03-14

### 프로젝트 방향 결정

처음엔 간단한 텍스트→맵 변환기를 생각했는데, 조사하면서 멀티턴 대화 방식이 훨씬 자연스럽다는 걸 알게 됐다.
유저가 "북유럽 느낌으로" 라고 하면 LLM이 "강도 넣을까요?" 하고 되물을 수 있는 구조.
JSON 응답을 `action: ask` / `action: generate` 두 가지로 나눈 게 핵심 설계 결정이다.

### 참조 모드 조사

- **Map Designer** (2111424996): 소스 없음. Harmony로 UI 패치하는 방식 확인.
- **Geological Landforms** (2773943594): 44종 지형 정의. 이미 설치됨. Phase 5에서 연동 예정.
- **RimTalk**: Gemini + OpenAI + Local(Ollama) 세 가지 LLM 지원하는 패턴 참조. ILLMClient 인터페이스 아이디어 여기서 가져옴.
- **바닐라 GenStep 목록**: ElevationFertility, Terrain, Plants, Animals, Roads, ScatterGeysers 등 확인.

### 기술 결정사항

| 결정 | 선택한 방법 | 이유 |
|---|---|---|
| UI 진입점 | WorldInspectPane.DoInspectPaneButtons Harmony Postfix | 타일 선택 시 자연스럽게 노출 |
| 맵 생성 방식 | 기존 GenStep 패치 (새 GenStep 추가 X) | 바닐라 호환성 유지 |
| LLM 응답 파싱 | 자체 구현 SimpleJson (외부 라이브러리 없음) | net472에서 Newtonsoft.Json 의존성 배제 |
| 멀티 LLM | ILLMClient 인터페이스 + 팩토리 패턴 | 제공자 추가/교체 용이 |
| 테스트 방법 | 인게임 [DebugAction] + 오프라인 C# 콘솔 프로젝트 | 매번 림월드 켤 필요 없이 API 테스트 가능 |

---

### Phase 1 완료 ✓ (프로젝트 세팅)

- 모드 폴더 구조 + MapGenAI.csproj (net472, LangVersion 9.0)
- About.xml — 패키지 ID: `Choco.MapGenAI`, Harmony 필수 의존성, Geological Landforms loadAfter
- `MapGenAIMod.cs` — 모드 진입점, Harmony.PatchAll() 자동 패치 등록
- `MapGenAISettings.cs` — 인게임 모드 설정창 (Gemini/OpenAI/Local API 키, 모델명, 동적 모델 목록 불러오기)
- `ILLMClient.cs` — 공통 인터페이스 + LLMClientFactory
- `GeminiClient.cs` — Gemini API (`/v1beta/models/{model}:generateContent`)
- `OpenAIClient.cs` — OpenAI 호환 API (`/v1/chat/completions`)
- `LocalClient.cs` — Ollama/LM Studio (OpenAIClient 재사용, URL만 교체)
- 빌드 성공 → `Assemblies/MapGenAI.dll` 생성

---

### Phase 2 완료 ✓ (UI + 채팅 다이얼로그)

**구현 내용:**

- `WorldInspectPane_Patch.cs` — 월드맵 타일 정보창 하단에 "Text to Map" 버튼 추가
- `Dialog_TextToMap.cs` — 멀티턴 채팅 다이얼로그 (Window 서브클래스)
- `SimpleJson.cs` — 외부 라이브러리 없는 최소 JSON 파서

**채팅 다이얼로그 동작 방식:**
- 유저 메시지는 오른쪽 파란 버블, AI 응답은 왼쪽 회색 버블
- 새 메시지 올 때마다 자동 스크롤
- Enter 키 또는 "전송" 버튼으로 전송
- LLM이 `action: generate` 응답하면 하단에 "✓ 이 설정으로 맵 생성" 버튼 활성화
- 전송 중엔 버튼이 "..." 로 바뀌어서 대기 상태 표시

**[DebugAction] 테스트 버튼 3개 추가:**
- "대화창 열기" — 타일 선택 없이 바로 다이얼로그 테스트
- "API 연결 테스트" — API 키 정상 동작 여부 Log.Message로 확인
- "현재 파라미터 출력" — MapGenParams에 저장된 값 로그 출력

**알려진 제한사항 (Phase 4에서 해결 예정):**
- "✓ 이 설정으로 맵 생성" 버튼은 현재 파라미터 저장만 하고 실제 맵 재생성은 안 됨
- 맵 재생성 연결은 Phase 4 GenStep 패치 완료 후 처리

---

### Phase 3 완료 ✓ (LLM API 연동)

**시스템 프롬프트 설계:**
LLM에게 두 가지 응답 형식 중 하나만 출력하도록 강제:
```json
{"action":"ask","message":"질문"}
{"action":"generate","params":{...}}
```
파라미터 스키마: hills(방향), river(present/direction/x_position), vegetation_density, animal_density, roads, caves, geysers

**Gemini API 주의사항:**
- `gemini-2.0-flash`는 신규 유저에게 deprecated → `gemini-2.5-flash` 사용
- 응답에서 text 필드 추출 시 이스케이프 처리 주의 (`\"` → `"`, `\n` → 실제 줄바꿈)
- system_instruction은 contents 배열과 별도 필드로 전달

**오프라인 테스트 프로젝트:**
- `Source/Tests/` — net10.0 콘솔 앱 (림월드 없이 API만 테스트)
- `dev_config.json` — API 키 저장 (git 제외)
- 테스트 항목: API 연결, JSON 파라미터 생성, 멀티턴 대화 히스토리 유지

---

### 리네이밍 작업

- 폴더: `text_to_map` → `mapgen_ai`
- 어셈블리: `TextToMap.dll` → `MapGenAI.dll`
- 네임스페이스: `TextToMap` → `MapGenAI`
- .csproj: `TextToMap.csproj` → `MapGenAI.csproj`
- About.xml 모드명: `Text to Map` → `MapGen AI`

---

---

## 2026-03-14 (2차)

### WorldInspectPane 패치 → WorldInterface 방식으로 교체

**문제**: `WorldInspectPane.DoInspectPaneButtons`는 정착지/캐러밴 등 월드 오브젝트 선택 시에만 호출됨. 빈 타일 클릭 시 버튼 미표시.

**해결**: `WorldInterface.WorldInterfaceOnGUI` Postfix로 교체. 월드맵에서 타일 선택 시 좌측 하단에 "✦ Text to Map" 버튼 오버레이로 표시.

**위치**: 좌측 하단 (화면 높이 기준 MarginBottom=60f 위) — 타일 정보 텍스트 위.

**조건**: `Find.WorldSelector.SelectedTile >= 0` 일 때만 표시, Dialog_TextToMap 이미 열려 있으면 숨김.

### 기타 정리
- 구 `TextToMap.dll` / `TextToMap.pdb` Assemblies에서 제거
- `DEV_LOG_RAW.md` 생성 (날것의 개발 노트 파일 분리)

---

## 2026-03-14 (3차) — 인게임 테스트 피드백 반영

### 수정된 버그들

**1. 설정창 이름 "Text to Map" → "MapGen AI"**
- `TextToMapMod.cs`의 `SettingsCategory()` 반환값 수정.

**2. 모델 선택 UI 개선**
- 기존: 텍스트 직접 입력 방식 (오타 위험)
- 변경: 토글 목록 방식. "모델 목록 불러오기" 클릭 시 목록 펼침, 선택 시 자동 접힘.
- 버튼 텍스트: 상태에 따라 "모델 목록 불러오기" / "▼ 모델 선택 (현재: X)" / "▲ 접기 (현재: X)"

**3. LongEventHandler → volatile fields 교체 (모델 목록 & LLM 응답)**
- `LongEventHandler.ExecuteWhenFinished`는 LongEvent(로딩 화면) 중에만 실행됨. 일반 게임플레이 중 백그라운드 Task.Run 결과를 메인 스레드로 전달 불가.
- 해결: volatile bool 플래그를 DoWindowContents에서 매 프레임 체크하는 패턴으로 교체.
  ```csharp
  // 백그라운드 스레드: 결과 저장 후 플래그 set (마지막에)
  _pendingResponse = response;
  _responseReady = true;

  // 메인 스레드 (DoWindowContents): 플래그 체크 후 처리
  if (_responseReady) { _responseReady = false; HandleResponse(_pendingResponse); }
  ```

**4. Enter 키 전송 고정**
- 기존: Enter 키가 창을 닫는 이슈 (doCloseButton=true + Use() 미적용)
- 변경: `doCloseButton = false`, `doCloseX = true`, Enter 이벤트에 `Event.current.Use()` 추가.

**5. 미리보기 패널 항상 표시**
- 기존: `ModLister.GetActiveModWithIdentifier("m00nl1ght.MapPreview")` 체크 후 Map Preview 있으면 버튼만 표시
- 문제: 미설치인데도 HasMapPreview=true로 판정되어 버튼만 뜨고 미리보기 패널 미표시
- 변경: 조건 분기 제거, 항상 `DrawPreviewPanel()` 실행 (미리보기 패널 + AI 버튼)

### Map Preview 호환성 조사 결과

Map Preview(m00nl1ght.MapPreview) DLL 리플렉션으로 분석:
- `MapPreviewToolbar+Button` 클래스 존재하나 외부 모드가 버튼을 추가하는 public API 없음
- `MapPreviewAPI.SubscribeGenPatches` 등은 맵 생성 패치용이지 UI 버튼 추가용 아님
- Map Preview 미설치 사용자를 위해 독자적인 미리보기 패널 구현

### TilePreviewGenerator 구현

`Source/Patches/TilePreviewGenerator.cs` — 바이옴 색상 기반 미리보기 텍스처 생성:
- `tile.PrimaryBiome.DrawMaterial.color` 기반 색상 (실제 바이옴 색)
- Perlin noise로 지형 변화 표현
- `tile.elevation` 값으로 밝기 조정
- 강 있으면(`tile.Rivers != null && Count > 0`) 파란 가로 밴드
- LargeHills/Mountainous면 중앙 어두운 그림자
- 캐시: Dictionary<int, Texture2D> (tileId 키)

---

## 2026-03-14 (4차) — 추가 버그 수정

**1. closeOnAccept = false 추가**
- RimWorld `Window` 기본값: `closeOnAccept = true` → Enter 키가 창을 닫음
- `doCloseButton = false`와 `Event.current.Use()`만으로는 부족했음
- `closeOnAccept = false`를 생성자에 추가해서 해결

**2. 모델 선택 시 자동 접힘 제거**
- RadioButton 클릭 시 `showList = false` 제거
- 접기는 "▲ 접기" 버튼으로만 가능

**3. LLM + 미리보기 예외 로그 추가**
- `Dialog_TextToMap.SendMessage()`: 클라이언트 생성 실패 즉시 표시, Task.Run 시작/응답수신/오류에 Log.Message 추가
- `WorldInterface_Patch.Postfix()`: try/catch + Log.Error
- `TilePreviewGenerator.Get()`: try/catch + Log.Error
- 이제 Player.log에서 `[MapGenAI]` 검색으로 어디서 막히는지 추적 가능

---

## 2026-03-14 (5차) — 미리보기 텍스처 + Enter 키 수정

**1. 미리보기 텍스처 안 보이는 문제 수정**
- 원인 A: `WorldTerrain` 셰이더에 `_Color` 속성이 없어 `DrawMaterial.color` 잘못된 값 반환 → Player.log에 경고 찍힘
- 원인 B: `GUI.DrawTexture` 호출 시 `GUI.color`가 이미 비정상 값으로 설정돼 있어 텍스처가 검게 렌더링됨
- 해결:
  - `DrawMaterial.color` 제거, `GetBiomeColor()` 메서드로 defName 기반 하드코딩 색상 사용
  - `GUI.DrawTexture` 전후로 `GUI.color = Color.white` / 복원 추가

**2. 채팅창 열려도 미리보기 패널 유지**
- `if (Find.WindowStack.IsOpen<Dialog_TextToMap>()) return;` 제거

**3. Enter 키 전송 조건 완화**
- `GUI.GetNameOfFocusedControl() == "ChatInput"` 조건 제거 → Enter만 누르면 전송
- `closeOnAccept = false` (이전 수정)로 창 닫힘 방지

**현재 미리보기 한계 (Phase 5 예정)**
- 미리보기는 현재 타일의 바이옴 색상 기반 (AI 파라미터 반영 불가)
- AI 채팅으로 파라미터를 바꿔도 미리보기는 그대로
- 실제 반영은 맵 시작 시점에 GenStep 패치를 통해 이루어짐

---

## 2026-03-14 (6차) — Map Preview 실제 지형 연동

**Map Preview API 연동 (`MapPreviewIntegration.cs`)**
- 소스: https://github.com/m00nl1ght-dev/MapPreview
- `MapPreviewWidget` 서브클래스 `MapGenAIPreviewWidget` 구현
- 타일 선택 시 `MapPreviewRequest(seedString, tileId, mapSize)` 생성 후 `QueuePreviewRequest`
- `UseMinimalMapComponents = true`, `UseTrueTerrainColors = true` 설정으로 빠르고 정확한 렌더링
- `widget.Draw(rect)` 으로 매 프레임 패널에 그림
- Map Preview 미설치/비활성화 시: `TilePreviewGenerator` fallback (바이옴 색상 기반)
- `MapPreviewAPI.IsReadyForPreviewGen` 으로 가용성 체크

**Enter 키 수정**
- `Widgets.TextField` 가 KeyDown 이벤트를 삼킬 수 있음
- Enter 체크를 TextField 호출보다 먼저 위치시켜 해결

---

## 2026-03-14 (7차) — LLM 근본 버그 수정 + 타일 컨텍스트

**GeminiClient.ExtractText 이스케이프 버그 수정**
- 근본 원인: `\"` 이스케이프를 `\`만 출력하고 `"` 스킵 → JSON 파싱 실패
- 수정: switch 문으로 `\n`, `\"`, `\\` 등 모든 이스케이프 시퀀스 처리

**HandleResponse JSON 추출 개선**
- 코드블록 제거 로직 대신 `{` ~ `}` 직접 추출 (더 견고)

**generate 시 description 필드 추가**
- LLM 응답에 `"description"` 필드 포함 → 어떤 맵을 만드는지 채팅에 설명 표시

**타일 컨텍스트 시스템 프롬프트**
- 선택된 타일의 바이옴, 지형, 강 여부, 해발 정보를 LLM에 전달
- 강 없는 타일에서 강 요구 시 → 타일 변경 안내
- 바다 요구 시 → 해안가 타일 선택 안내

---

## 다음 단계: Phase 4 (GenStep 패치 — 실제 맵 생성 적용)

현재 MapGenParams에 파라미터를 저장하는 것까지는 됨.
이걸 실제 맵 생성에 반영하는 Harmony 패치가 남아 있음.

**구현 목표:**
- `GenStep_ElevationFertility` 패치 → 언덕 방향/위치 제어
- `GenStep_Plants` 패치 → 나무 밀도 오버라이드
- `GenStep_Animals` 패치 → 동물 밀도 오버라이드
- `GenStep_Roads` 패치 → 도로 유무
- `GenStep_ScatterGeysers` 패치 → 간헐천 개수

**강 생성은 별도 접근 필요:**
맵 생성 이전 월드 타일 단계에서 river 데이터가 결정됨.
GenStep 단계에서 직접 강을 그리는 방식으로 우회해야 할 수도 있음.

---

## 2026-03-15 — 프리셋 저장/불러오기 기능

### 구현 내용

유저가 AI와 대화로 생성한 맵 파라미터를 이름으로 저장하고, 나중에 불러올 수 있는 프리셋 시스템 추가.

**PresetManager.cs (신규: Source/MapGen/PresetManager.cs)**
- `Save(name, data)` — MapParamsData를 JSON 파일로 저장
- `Load(name)` — JSON 파일에서 MapParamsData로 역직렬화
- `ListPresets()` — 저장된 프리셋 이름 목록 (알파벳 정렬)
- `Delete(name)` — 프리셋 삭제
- 저장 위치: `GenFilePaths.ConfigFolderPath/MapGenAI_Presets/{이름}.json`
- 직렬화: 수동 JSON 생성 (net472에서 System.Text.Json 사용 불가)
- 역직렬화: 기존 SimpleJson 파서 재사용
- 파일명 sanitize: `Path.GetInvalidFileNameChars()` 기반 치환
- 모든 파일 I/O에 예외 처리 + Log.Error 로깅

**Dialog_TextToMap.cs 수정**
- `_paramsReady = true` 상태: "이 설정으로 맵 생성" + "프리셋 저장" + "프리셋 불러오기" 3개 버튼 가로 배치
- `_paramsReady = false` 상태: "프리셋 불러오기" 버튼만 표시 (파라미터 없어도 기존 프리셋 로드 가능)
- "프리셋 저장" 클릭 시: 이름 입력 필드 + 저장/취소 버튼 노출
- "프리셋 불러오기" 클릭 시: FloatMenu로 프리셋 목록 표시 + 하단에 삭제 옵션
- 프리셋 로드 시: `MapGenParams.Apply()` 호출 + Map Preview 갱신 + 채팅에 파라미터 요약 표시

**저장되는 필드 (MapParamsData 전체)**
- hills, hill_amount, vegetation_density, animal_density
- river (present, direction, x_position)
- roads, caves, geysers
- coast_direction, rock_count, ore_density
- mutators (문자열 배열)

---

## 2026-03-15 (2차) -- 테스트벤치 12개 -> 30개 확장

### SystemPrompt 동기화
TestBench.cs의 SystemPrompt를 Dialog_TextToMap.cs와 동기화.
기존 프롬프트에 누락되어 있던 새 파라미터들 추가:
- `coast_direction` (auto/north/east/south/west)
- `rock_count` (1~15, -1=기본)
- `ore_density` (0.0~2.5)
- `mutators` (배열, 전체 mutator defName 목록 포함)

TileCtx() 헬퍼도 확장 -- 조절 가능/불가능 항목, mutator 카테고리 전체 목록, 규칙 등을 Dialog_TextToMap.cs와 동일하게 반영.

### 신규 18개 케이스

| 카테고리 | 케이스 수 | 검증 내용 |
|---|---|---|
| 산 시스템 | 3 | hill_amount > 1.0, < 1.0, hills=left + hill_amount 조합 |
| 해안 방향 | 2 | coast_direction=north, auto/미포함 |
| 석재 | 2 | rock_count=1, rock_count >= 5 |
| 광석 | 2 | ore_density > 1.0, ore_density=0 |
| TileMutator | 4 | HotSprings, WildTropicalPlants, Lake, Fjord |
| 복합 요청 | 3 | HotSprings+hill_amount, MineralRich+ore_density, FoggyMutator+Wetland |
| 경계/에러 | 2 | 존재하지 않는 mutator, 모든 파라미터 극단값 동시 |

### ContainsMutator() 헬퍼 추가
JSON 응답에서 `"mutators"` 배열 범위를 찾아 특정 mutator defName 포함 여부를 확인하는 전용 검증 함수.

### 케이스 #1 검증 완화
기존: `generate + river.present=true` 만 PASS
변경: `ask + 강 방향 제한 안내`도 PASS (프롬프트에 "강 방향 조절 불가능" 규칙이 추가되었으므로)

### 결과: 30/30 PASS

---

## 2026-03-15 (3차) -- 인게임 자동 테스트 시스템

### 구현 내용

LLM 호출 없이 MapGenParams.Apply() 파이프라인을 직접 검증하는 인게임 테스트 러너 추가.
월드맵에서 타일 선택 후 DebugAction "Run All Pipeline Tests" 한 번 클릭으로 13개 테스트 실행.

**InGameTestRunner.cs (신규: Source/Tests/InGameTestRunner.cs)**
- DebugAction 카테고리: "MapGenAI", 월드맵 전용 (AllowedGameStates.WorldRenderedNow)
- 테스트 프레임워크: RunTest() 헬퍼 -- 각 테스트 전후 MapGenParams.Reset() 호출 (상태 격리)
- Odyssey 비활성 시 mutator 관련 테스트 자동 SKIP (ModsConfig.OdysseyActive 체크)
- 결과: Log.Message로 Player.log에 출력 (PASS/FAIL/SKIP/ERROR)

**테스트 케이스 13개:**

| 카테고리 | 케이스 수 | 검증 내용 |
|---|---|---|
| 동굴 | 2 | caves=true -> Caves 추가, caves=false -> Caves 없음 |
| TileMutator | 4 | HotSprings/WildTropicalPlants/Cavern 추가, 빈 배열 -> 기존 유지 |
| 산 | 2 | hills=left+hill_amount=1.4, hills=none+hill_amount=0.5 적용 성공 |
| 해안 | 1 | coast_direction=north 설정 확인 |
| 석재 | 1 | rock_count=1 설정 확인 |
| 리셋 | 2 | Reset() -> 상태 초기화, Reset() -> 타일 mutator 원본 복원 |
| 복합 | 1 | caves=true+HotSprings+MineralRich 동시 적용 |

**MapGenAI.csproj 수정:**
- Tests\ 디렉터리는 기본적으로 제외 (오프라인 TestBench용)
- InGameTestRunner.cs만 명시적으로 메인 어셈블리에 포함: `<Compile Include="Tests\InGameTestRunner.cs" />`

**빌드 성공 확인 (0 Warning, 0 Error)**

---

## 트러블슈팅 기록

| 문제 | 원인 | 해결 |
|---|---|---|
| CS8630 nullable 오류 | net472는 Nullable enable 미지원 | LangVersion 9.0으로 변경 |
| HttpClient 빌드 오류 | System.Net.Http 참조 누락 | .csproj에 명시적 추가 |
| Tests 폴더 충돌 | 메인 프로젝트가 Tests\ 소스 같이 빌드 | `<Compile Remove="Tests\**" />` 추가 |
| DebugAction 인식 안 됨 | LudeonTK 네임스페이스 누락 | `using LudeonTK;` 추가 |
| selectedTile 없음 | RimWorld 1.6에서 SelectedTile로 대문자 변경 | 수정 |
| gemini-2.0-flash 안 됨 | 신규 유저에게 deprecated | gemini-2.5-flash로 변경 |
| PLAN.md 한글 깨짐 | 인코딩 문제 | UTF-8 no-BOM으로 재작성 |
