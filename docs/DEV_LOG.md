# MapGen AI - 개발 로그

---

## 2026-03-21 (2차)

### 프로젝트 리뷰 문서 생성

`review/` 디렉토리에 4개 문서 생성:
- `project_brief.md` — 프로젝트 목적, 기술 스택, 주요 기능 요약
- `project_structure.md` — 전체 디렉토리 트리 + 각 파일 역할 주석
- `project_flow.md` — 데이터 흐름, 맵 생성 파이프라인, LLM 클라이언트 구조, Undo/Reset 상태 다이어그램 (Mermaid)
- `quick_start.md` — 개발 환경 설정, 빌드, API 테스트, 트러블슈팅

---

## 2026-03-21

### Settings UI 개편 (RimTalk 스타일)
- Simple/Advanced 모드 분리 — Simple은 Gemini 키 하나만 입력
- 11개 LLM 프로바이더 지원: Gemini, OpenAI, DeepSeek, Grok, GLM, GLMCoding, AlibabaIntl, AlibabaCN, OpenRouter, Local, Custom
- 멀티 API 키 리스트 + 우선순위 fallback (실패 시 자동 다음 키 시도)
- 모델 선택 드롭다운 — API fetch 후 FloatMenu로 선택

### 버그 수정: OpenRouter 연동
- base URL 오류: `/v1` → `/api/v1`
- compact JSON 파싱 실패: `"content":"..."` (공백 없음) 형태 처리 추가
- HTTP 오류 시 `return null` → `throw Exception` — fallback이 실제로 동작하지 않던 문제 수정
- 프로바이더 변경 시 모델명 자동 리셋

### 버그 수정: Ring 지형 버그
**현상**: ring 모양 요청 시 기존 산이 사라지고 초승달 형태가 됨.

**근본 원인**: Map Designer Donut 공식은 평평한 베이스 지형 전제. 링에서 멀어질수록 큰 음수 elevation을 적용해 기존 산을 파괴. center가 맵 중앙에서 벗어나면 한쪽만 보이는 초승달 형태 발생.

**수정**: `ApplyRing()` 공식을 Gaussian 프로파일로 교체. 링 능선만 올리고 나머지 지형은 건드리지 않음.

---

## 2026-03-17 (3차)

### 버그 수정: 강 방향 역전

**현상**: "강을 좌우로 흐르게" 요청 시 상하 강 생성, "상하로" 요청 시 좌우 강 생성 — 완전 역전.

**근본 원인**: RimWorld 각도 규칙을 잘못 가정.
- 잘못된 가정: 수학 좌표 기준 (0°=오른쪽=수평)
- 실제 동작: 나침반/화면 기준 (0°=북=위=수직, 90°=동=오른쪽=수평)
- 결과: horizontal→0°→수직 강, up→90°→수평 강으로 역전

**수정 내용**:
- `MapGenParams.Apply()` 각도 매핑 수정:
  - horizontal: 0° → 90°
  - left: 0° → 270° (서쪽으로 흐름)
  - up: 90° → 0° (북쪽으로 흐름)
  - right: 180° → 90° (동쪽으로 흐름)
  - down: 270° → 180° (남쪽으로 흐름)
- 시스템 프롬프트 각도 설명 수정: `0=위(북), 90=오른쪽(동), 180=아래(남), 270=왼쪽(서)`
- `RiverPatches.cs` 주석 수정 (실제 동작 반영)

**영향 없는 기능**: 강 위치(x_position, z_position), 일자 강(straight_river) — 이 두 기능은 원래부터 정상 작동, 이번 수정과 무관.

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

## 2026-03-15

### 건물/가구 시스템 웹 조사

RimWorld 모딩용 건물/가구 시스템 웹 조사 완료. 결과를 `RESEARCH_BUILDINGS.md`에 정리.

**조사 범위 6개 항목:**
1. ThingDef 건물/가구 카탈로그 — 벽, 문, 바닥, 침대, 테이블/의자, 조명, 작업대, 저장, 온도, 레크, 방어, 전력, 의료 (13개 카테고리, 100+개 defName)
2. 건물 크기와 배치 규칙 — 1x1부터 7x2까지 크기표, 회전, Stuff 사용 여부
3. Stuff 시스템 — 6개 카테고리, 건축용 주요 재질 12종 비교표 (Beauty/HP/가연성/작업량 배율)
4. 방(Room) 시스템 — 밀폐 조건, 방 역할 12+종, Impressiveness 계산 공식 (최소 스탯 51.25% 가중), 등급표
5. 건물 배치 제약 — 야외 전용, 벽 관통형, terrainAffordance 5단계, 지붕 6칸 규칙, 풍력 배제 영역
6. 건축 API — GenConstruct (PlaceBlueprintForBuild, CanPlaceBlueprintAt 검증 9단계), Designator_Build, GhostDrawer, PlaceWorker 6종, ThingDef XML 구조 전체

**참조 소스:** RimWorld Wiki, GitHub 디컴파일 소스, Ludeon 공식 번역 저장소, Steam 커뮤니티

---

### Map Designer 누락 기능 5개 일괄 구현

Map Designer 1.6 소스를 참조하여 MapGenAI에 누락된 기능 5개를 일괄 구현.
RiverBankPatch는 Map Designer 1.6에서도 완전히 주석 처리(비활성)되어 스킵.

#### 구현 내역

| 기능 | 패치 대상 | 방식 | 설명 |
|---|---|---|---|
| 강 방향 | TileMutatorWorker_River.IsFlowingAToB + GetMapEdgeNodes | Prefix (angle 교체) | 0-360도 각도로 강 방향 제어 |
| 강 위치 | TileMutatorWorker_River.GetRiverCenter | Postfix (result.x/z 교체) | 0.0-1.0으로 강 중심점 이동 |
| 돌덩어리 | GenStep_RockChunks.Generate | Prefix (return false) | rock_chunks=false로 돌덩어리 제거 |
| 산 크기/부드러움 | GenStep_ElevationFertility.Generate | Transpiler (상수 교체) | IL에서 0.021(hillSize)과 2.0(hillSmoothness) 상수를 런타임 메서드 호출로 교체 |

#### 새 파라미터

| 파라미터 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| river_direction | string/float | auto(-1) | 강 방향. left/right/up/down 또는 0-360도 |
| river_position | string/float | center(0.5) | 강 위치. left/center/right 또는 0.0-1.0 |
| rock_chunks | bool | true | 돌덩어리 생성 여부 |
| hill_size | string/float | medium(0.021) | 산 크기. small/medium/large |
| hill_smoothness | string/float | normal(2.0) | 산 부드러움. rough/normal/smooth |

#### RiverBankPatch 스킵 사유

Map Designer 1.6에서 `RiverBankPatch`는 `/* */`로 완전히 주석 처리됨.
RW 1.6에서 강 시스템이 `RiverMaker` 기반에서 `TileMutatorWorker_River` 기반으로 리팩토링되면서
기존 `RiverMaker.TerrainAt`를 패치하던 방식이 더 이상 작동하지 않기 때문.
1.6에서 강 해변은 `TileMutatorWorker_River.RiverBankTerrainAt`가 담당하지만,
Map Designer도 이를 구현하지 않았으므로 동일하게 스킵.

---

## 2026-03-15 (4차) — 기존 모드 철저 조사

### 조사 목적
MapGen AI와 유사/관련 기능의 기존 림월드 모드들을 철저히 조사.
건물 자동 배치, AI/LLM 모드, UI 패턴, 커뮤니티 템플릿 공유, 소스 코드 패턴 등 5개 카테고리.

### 1. 건물 자동 배치/설계 관련 모드

| 모드명 | Steam ID | packageId | GitHub | 핵심 기능 |
|---|---|---|---|---|
| Blueprints (Fluffy) | 708455313 | fluffy.blueprints | fluffy-mods/Blueprints | 영역 드래그→블루프린트 생성, 복사/회전/내보내기 |
| Universal Blueprints | 3540066516 | - | - | 101개 내장 블루프린트 브라우저, 프로젝션 시스템, 모드 건물 내보내기 |
| More Planning | 2551225702 | com.github.alandariva.moreplanning | alandariva/RimworldMorePlanning | 10가지 색상 계획 지정자, 복사/붙여넣기 |
| Planning Extended | 2877392159 | scherub.planningextended | Scherub/rw-planning-extended | 다양한 도형(삼각형/직사각형/타원), 계획 저장/불러오기 |
| Quick Build Tool | 3524947568 | - | - | 재료 있으면 즉시 건설 완료 |
| Smarter Construction | 2202185773 | - | dhultgren/rimworld-smarter-construction | 건설 순서 최적화 (벽에 갇히는 문제 방지) |
| Designator Shapes | 1235181370 | Merthsoft.DesignatorShapes | merthsoft/designator-shapes | 35가지 도형 + 채우기 + 언두/리두 |
| Misc. MapGenerator | - | - | HaploX1/RimWorld-Miscellaneous_Source | 맵 생성 시 블루프린트 기반 기지 배치 (커뮤니티 제출 가능) |

### 2. AI/LLM 관련 모드

| 모드명 | Steam ID | GitHub | 핵심 기능 |
|---|---|---|---|
| RimGPT | 2960127000 | pardeike/RimGPT | ChatGPT + Azure TTS 게임플레이 해설 |
| OpenRimWorldAI | 3411917876 | - | OpenRouter API 기반 일일 식민지 보고서 |
| RimWorldAI Core | 3269938006 | oidahdsah0/Rimworld_AI_Core | 오프라인 로컬 AI, 인터넷 불필요 |
| RimAI Framework | - | oidahdsah0/Rimworld_AI_Framework | LLM 프로바이더 추상화 프레임워크 |
| RimTalk | 3551203752 | - | LLM 기반 폰 대화 (OpenAI/DeepSeek/Ollama 지원) |
| Local AI Social Interactions | 3413305419 | gavinblair/SocialInteractions | Ollama 로컬 LLM으로 사회 상호작용 생성 |
| RimSaga | 3258739992 | - | Gemini API로 식민지 이야기 자동 작성 |
| RimAI Core (BETA) | 3560404184 | - | RimAI Framework 기반 AI 코어 |
| Tales from the RimWorld | - | adhikasp/TalesFromTheRimWorld | OpenRouter + 커스텀 스토리텔러 "The Narrator" |

### 3. 인게임 UI / Architect 패턴 참조 모드

| 모드명 | Steam ID | GitHub | UI 패턴 |
|---|---|---|---|
| Better Architect Menu | 3563882422 | - | Architect 메뉴 서브카테고리/정렬 |
| Architect Sense | 852998459 | - | 건물/지형 그룹핑 (1.0+ 바닐라에 흡수) |
| Tab-sorting | 2138635288 | - | 건물을 적절한 탭으로 재배치 |
| Allow Tool | 761421485 | UnlimitedHugs/RimworldAllowTool | 커스텀 Designator 정의 (ReverseDesignatorDefs XML) |
| DragSelect | 2599942235 | - | 드래그 선택 확장 |
| RimHUD | 1508850027 | Jaxe-Dev/RimHUD | 커스텀 Window/HUD 오버레이 패턴 |

### 4. REST API 모드 (외부 제어)

| 모드명 | Steam ID | GitHub | 핵심 기능 |
|---|---|---|---|
| RIMAPI | 3593423732 | IlyaChichkov/RIMAPI | 120+ REST API 엔드포인트, SSE, 외부 앱 연동 |
| ARROM | 3525153789 | - | REST API로 게임 데이터 JSON 조회 |

### 5. 건물 템플릿 공유 커뮤니티

- **r/RimWorldPorn** (Reddit) — 고해상도 식민지 렌더링 전문 서브레딧. 바이옴/테마별 기지 디자인 공유.
- **rimworld.gallery** — RimWorldPorn 미러/아카이브 사이트
- **Steam Community 가이드** — Mountain Base Plan (Haven v2) 등 개별 레이아웃 가이드
- **HaploX1/RimWorld-MapGenBaseBlueprints** (GitHub) — 커뮤니티가 제출한 맵 생성용 기지 블루프린트 XML 저장소

### 6. 건물 프로그래밍 배치 소스코드 패턴

**GenConstruct.PlaceBlueprintForBuild 패턴:**
```csharp
Blueprint_Build blueprint = (Blueprint_Build)ThingMaker.MakeThing(sourceDef.blueprintDef, null);
blueprint.SetFactionDirect(faction);
blueprint.stuffToUse = stuff;
GenSpawn.Spawn(blueprint, center, map, rotation, WipeMode.Vanish, false);
```
- 출처: josh-m/RW-Decompile, RimWorld-zh/RimWorld-Decompile

**Fluffy Blueprints 핵심 패턴 (BuildableInfo.cs):**
- 영역 드래그 → 셀별 건물/바닥 수집 → BuildableInfo 리스트로 직렬화
- 배치 시 BuildableInfo → GenConstruct.PlaceBlueprintForBuild 호출

**커스텀 Designator 패턴 (Allow Tool):**
- XML에 ReverseDesignatorDef 정의 → designatorClass로 C# 클래스 지정
- 커스텀 Designator_Build 서브클래스로 건물 배치 도구 구현 가능

---

## 2026-03-15 (5차) — 인게임 UI/건물 배치 시스템 조사

### 조사 목적
월드맵이 아닌 식민지 맵(인게임)에서의 UI 시스템과 건물 배치 메커니즘을 조사.
Phase 5 이후 "LLM이 설계한 건물을 인게임에서 실제 배치"하는 기능 구현을 위한 사전 조사.

### 1. 인게임 Window 시스템

**WindowLayer enum (Verse.WindowLayer):**
| 값 | 용도 |
|---|---|
| GameUI | 게임 화면 위 HUD 레이어 (항상 표시, 게임 일시정지 안 함) |
| Dialog | 일반 대화창 레이어 (기본값, 대부분의 Window가 사용) |
| SubSuper | Dialog 위, Super 아래 중간 레이어 |
| Super | 최상위 레이어 (에러 메시지 등) |

**Window 클래스 주요 필드:**
- `layer` = WindowLayer.Dialog (기본)
- `doCloseButton`, `doCloseX`, `forcePause`, `absorbInputAroundWindow`
- `drawShadow` = true, `closeOnAccept` = true, `closeOnCancel` = true
- `InitialSize` = Vector2(500, 500)
- `DoWindowContents(Rect inRect)` — 추상 메서드, 서브클래스에서 구현

**커스텀 윈도우 띄우기:**
```csharp
Find.WindowStack.Add(new MyCustomWindow());
```

**인게임 채팅 UI 모드 예시:**
- `kirimin/rimworld_mod_chatlog_overlay` — Social 탭 로그를 인게임에 MMO 채팅창처럼 오버레이
- `RimHUD` (Jaxe-Dev/RimHUD) — 선택된 폰 정보를 커스텀 Window/HUD로 표시

### 2. Designator 시스템

**상속 계층:** `Gizmo → Command → Designator → Designator_Place → Designator_Build`

**Designator_Place 핵심 필드:**
- `placingRot` (Rot4) — 현재 회전 (기본 North)
- `CanPlaceColor` = Color(0.5f, 1f, 0.6f, 0.4f) — 배치 가능 시 초록색
- `CannotPlaceColor` = Color(1f, 0f, 0f, 0.4f) — 배치 불가 시 빨간색
- `PlacingDef` (abstract) — 배치할 BuildableDef

**Designator_Place 핵심 메서드:**
- `SelectedUpdate()` — 매 프레임 마우스 위치에 고스트 미리보기 렌더링
- `DrawGhost()` — GhostDrawer.DrawGhostThing_NewTmp() 호출
- `DoExtraGuiControls()` — 회전 버튼 UI 표시
- `HandleRotationShortcuts()` — Q/E 키 또는 마우스 중클릭으로 회전

**Designator_Build 핵심 필드:**
- `entDef` (BuildableDef) — 건설할 건물 정의
- `stuffDef` (ThingDef) — 재료 (나무, 돌 등)

**Designator_Build 핵심 메서드:**
- `ProcessInput()` — 재료 선택 FloatMenu 표시 (MadeFromStuff인 경우)
- `DesignateSingleCell(IntVec3 c)` — 실제 청사진 배치 또는 God 모드에서 즉시 건설

**커스텀 Designator 만들기:**
1. `Designator` 또는 `Designator_Place` 서브클래스 작성
2. `CanDesignateCell()`, `DesignateSingleCell()` 오버라이드
3. XML DesignationCategoryDef의 `specialDesignatorClasses`에 등록

**Architect 탭에 카테고리 추가 (DesignationCategoryDef):**
```xml
<DesignationCategoryDef>
  <defName>MyModCategory</defName>
  <label>My Mod</label>
  <order>999</order>
  <specialDesignatorClasses>
    <li>MyMod.Designator_MyCustomTool</li>
  </specialDesignatorClasses>
</DesignationCategoryDef>
```
건물을 이 카테고리에 넣으려면 ThingDef에 `<designationCategory>MyModCategory</designationCategory>` 지정.

### 3. GenConstruct API

**PlaceBlueprintForBuild 시그니처:**
```csharp
public static Blueprint_Build PlaceBlueprintForBuild(
    BuildableDef sourceDef, IntVec3 center, Map map,
    Rot4 rotation, Faction faction, ThingDef stuff)
```

**구현:**
```csharp
Blueprint_Build obj = (Blueprint_Build)ThingMaker.MakeThing(sourceDef.blueprintDef);
obj.SetFactionDirect(faction);
obj.stuffToUse = stuff;
GenSpawn.Spawn(obj, center, map, rotation);
return obj;
```

**CanPlaceBlueprintAt 시그니처:**
```csharp
public static AcceptanceReport CanPlaceBlueprintAt(
    BuildableDef entDef, IntVec3 center, Rot4 rot, Map map,
    bool godMode = false, Thing thingToIgnore = null,
    Thing thing = null, ThingDef stuffDef = null)
```
검증 항목: 범위 체크, 맵 가장자리, 안개, 중복 건물/청사진, 상호작용 셀 차단, 지형 지원, PlaceWorker 검증.

**CanBuildOnTerrain:** 지형 어포던스 검증 (예: Heavy 구조물은 Light 지형 불가).

**Blueprint_Build 클래스:**
- `stuffToUse` (ThingDef) — 건설 재료
- `WorkTotal` — 건설 소요 작업량
- `MaterialsNeeded()` — 필요 자원 목록
- `MakeSolidThing()` — Frame(건설 프레임) 생성

**여러 건물 한 번에 배치:**
```csharp
foreach (var building in layoutList)
{
    var report = GenConstruct.CanPlaceBlueprintAt(
        building.def, building.pos, building.rot, map);
    if (report.Accepted)
    {
        GenConstruct.PlaceBlueprintForBuild(
            building.def, building.pos, map,
            building.rot, Faction.OfPlayer, building.stuff);
    }
}
```

### 4. Ghost/Preview 렌더링

**GhostDrawer.DrawGhostThing_NewTmp:**
```csharp
public static void DrawGhostThing_NewTmp(
    IntVec3 center, Rot4 rot, ThingDef thingDef,
    Graphic baseGraphic, Color ghostCol, AltitudeLayer drawAltitude,
    Thing thing = null, bool drawPlaceWorkers = true)
```
동작: GhostUtility.GhostGraphicFor()로 반투명 그래픽 생성 -> GenThing.TrueCenter()로 위치 계산 -> graphic.DrawFromDef()로 렌더링 -> 컴포넌트/PlaceWorker의 DrawGhost 호출.

**GhostUtility.GhostGraphicFor:** 원본 그래픽 + ghostCol 색상으로 EdgeDetect 셰이더 적용. 결과를 Dictionary로 캐시.

**배치 가능/불가능 색상:**
- 가능: `new Color(0.5f, 1f, 0.6f, 0.4f)` (초록)
- 불가능: `new Color(1f, 0f, 0f, 0.4f)` (빨강)

**여러 건물 동시 고스트 표시:**
```csharp
foreach (var building in layoutList)
{
    var report = GenConstruct.CanPlaceBlueprintAt(
        building.def, building.pos, building.rot, map);
    Color ghostCol = report.Accepted
        ? Designator_Place.CanPlaceColor
        : Designator_Place.CannotPlaceColor;
    GhostDrawer.DrawGhostThing_NewTmp(
        building.pos, building.rot, building.def,
        null, ghostCol, AltitudeLayer.Blueprint);
}
```

### 5. 인게임 맵 좌표 시스템

**IntVec3 (Verse.IntVec3):**
- `x` — 동서(좌우) 축
- `y` — 수직/고도 축 (맵 그리드에서는 보통 0, AltitudeLayer가 이 역할)
- `z` — 남북(상하) 축
- `ToVector3()` → `new Vector3(x, y, z)`
- `ToVector3Shifted()` → `new Vector3(x + 0.5f, y, z + 0.5f)` (셀 중앙)
- `LengthHorizontalSquared` → `x*x + z*z` (y 무시)

핵심: RimWorld는 **탑다운 2D 게임이지만 Unity 3D 좌표계 사용**. 맵 그리드는 x-z 평면, y는 렌더링 순서(고도)에만 사용.

**CellRect (Verse.CellRect):**
- 필드: `minX, maxX, minZ, maxZ`
- `Width` = maxX - minX + 1, `Height` = maxZ - minZ + 1
- `CenteredOn(IntVec3 center, int width, int height)` — 중심점 기준 사각형
- `Contains(IntVec3)`, `Overlaps(CellRect)`, `ClipInsideMap(Map)`
- `IEnumerable<IntVec3>` 구현 → foreach로 모든 셀 순회 가능
- `WholeMap(Map)` — 맵 전체 사각형

**Rot4 (Verse.Rot4):**
| rotInt | 방향 | FacingCell |
|---|---|---|
| 0 | North (Z+) | (0,0,1) |
| 1 | East (X+) | (1,0,0) |
| 2 | South (Z-) | (0,0,-1) |
| 3 | West (X-) | (-1,0,0) |

- `IsHorizontal` → East 또는 West
- `Rotate(RotationDirection)` — 시계/반시계 회전
- 건물 크기와 회전: Size(2,1,3)인 건물을 East로 회전하면 실제 점유 영역이 (3,1,2)가 됨. `GenAdj.OccupiedRect(center, rot, size)` 가 이를 자동 처리.

### 6. Gizmo 시스템

**상속 계층:** `Gizmo → Command → Command_Action / Command_Toggle / Designator`

**Command 주요 필드:**
- `defaultLabel` (string) — 버튼 텍스트
- `defaultDesc` (string) — 툴팁 설명
- `icon` (Texture2D) — 아이콘
- `hotKey` (KeyBindingDef) — 단축키
- `defaultIconColor` (Color) — 아이콘 색상
- `disabled` (bool) + `disabledReason` (string) — 비활성화

**Command_Action 클래스 (매우 단순):**
```csharp
public class Command_Action : Command
{
    public Action action;
    public override void ProcessInput(Event ev)
    {
        base.ProcessInput(ev);
        action();
    }
}
```

**CompGetGizmosExtra로 커스텀 버튼 추가 (CompTempControl 패턴):**
```csharp
public override IEnumerable<Gizmo> CompGetGizmosExtra()
{
    foreach (Gizmo g in base.CompGetGizmosExtra())
        yield return g;

    yield return new Command_Action
    {
        action = delegate { DoMyAction(); },
        defaultLabel = "버튼 텍스트",
        defaultDesc = "버튼 설명",
        hotKey = KeyBindingDefOf.Misc5,
        icon = ContentFinder<Texture2D>.Get("UI/Icons/MyIcon")
    };
}
```

**건물 선택 시 커스텀 액션 추가 방법:**
1. ThingComp 서브클래스 작성 + `CompGetGizmosExtra()` 오버라이드
2. CompProperties 정의
3. XML ThingDef에 `<comps><li Class="MyMod.CompProperties_MyComp"/></comps>` 추가
4. 또는 Harmony로 기존 건물의 GetGizmos() Postfix 패치

### 참조 소스

- 디컴파일 소스: Chillu1/RimWorldDecompiled (GitHub)
- GenConstruct.cs: josh-m/RW-Decompile, RimWorld-zh/RimWorld-Decompile
- 인게임 채팅 오버레이: kirimin/rimworld_mod_chatlog_overlay
- 모딩 가이드: roxxploxx/RimWorldModGuide (GitHub Wiki)
- RimWorld Wiki: rimworldwiki.com/wiki/Modding_Tutorials

---

## 2026-03-15 (7차) — BlueprintAI 독립 콘솔 테스트앱

### 구현 내용

RimWorld 없이 WallMapParser + FurniturePlacer + BlueprintEngine을 검증하는 독립 콘솔 테스트앱 생성.

**프로젝트 위치:** `F:\Projects\Rimworld\blueprint_ai\Source\Tests\`

**프로젝트 구성:**
- `BlueprintAI.Tests.csproj` — net472 콘솔앱, Engine 소스를 `<Compile Include>` 링크로 직접 포함 (DLL 참조 대신)
- `TestRunner.cs` — 14개 테스트 케이스, 37개 개별 Assert

**테스트 카테고리 (14개):**

| 카테고리 | 케이스 수 | 검증 내용 |
|---|---|---|
| WallMapParser | 5 | Square 5x5, L-shape, Octagon, NoDoor, HoleInWall |
| FurniturePlacer | 5 | FloorOK, WallSkip, PartialWallSkip, Overlap, UnknownDef |
| BlueprintData 구조 | 2 | Valid construction, Empty wallmap |
| 통합 파이프라인 | 2 | Bedroom 7x7 (4가구), L-shaped (6가구) |

**수정 사항:**
- 테스트 기대값 3곳 수정 (Engine 코드는 정상, 원래 기대값이 잘못됨):
  - Test01 Wall count: 16 -> 15 (5x5에서 D 1개 빼야 함)
  - Test03 Floor count: 19 -> 21 (Octagon 바닥 실제 21칸)
  - Test13 Walls: 24 -> 23 (7x7에서 D 1개 빼야 함)

**결과: 37/37 PASS**

---

---

## 2026-03-17 — 멀티턴 파라미터 누적 버그 수정 + Reset/Undo 기능 (`feature/reset-undo`)

### 근본 버그: BuildSystemPrompt가 원본 타일 상태만 읽는 문제

**원인**: `BuildSystemPrompt()`가 `Find.WorldGrid[tileId]`(타일 원본 데이터)만 읽고,
`MapGenParams`에 저장된 **수정된 파라미터 상태**를 LLM에 전달하지 않았음.
→ 멀티턴 채팅 시 LLM이 매 요청마다 원본 타일 기준으로 파라미터를 생성 → 이전 수정이 덮어쓰여짐.
→ "왼쪽에 산 → 그다음에 왼쪽에 강" 하면 강 추가 시 산이 사라짐.

**해결**:
- `MapGenParams.BuildCurrentParamsText()` 메서드 추가 → 현재 적용된 파라미터 상태를 텍스트로 직렬화
- `BuildSystemPrompt()`에서 현재 파라미터 상태 섹션을 system prompt에 포함
- `SendMessage()`: 전체 대화 히스토리 대신 **현재 user 메시지 1개만** LLM에 전달 (파라미터 상태는 system prompt에 있으므로 히스토리 불필요)
- 부작용: 대화 히스토리 길이 무제한 누적에 의한 토큰 폭증 + API 오류도 해결

### 되돌리기(Undo) / 초기화(Reset) 기능

| 버튼 | 동작 |
|---|---|
| **되돌리기** | 한 번 전송 이전 파라미터로 복원. Stack에서 pop. 채팅 마지막 Q&A 제거. |
| **초기화** | 다이얼로그 열릴 때의 최초 상태로 복원. 채팅 히스토리 전부 삭제. |

**구현**:
- `MapGenParams.ToSnapshot()` — 현재 파라미터 전체를 `MapParamsData`로 스냅샷
- `Dialog_TextToMap`: `_paramStack`(Stack), `_initialSnapshot`(다이얼로그 열릴 때 스냅샷) 필드 추가
- `SendMessage()`: LLM 요청 직전에 현재 파라미터를 스택에 push
- `DoUndo()`: 스택 pop → `MapGenParams.Apply()` → 채팅 마지막 exchange 제거
- `DoReset()`: 스택 clear → `_initialSnapshot`으로 복원 → 채팅 Welcome 메시지로 초기화
- 하단 버튼 UI: `[이 설정으로 생성] [되돌리기] [초기화] [프리셋 저장] [프리셋 불러오기]`
- 되돌리기 버튼은 스택이 비어있거나 대기 중일 때 비활성화
- 번역 키 추가: `MapGenAI_Undo`, `MapGenAI_Reset`

### catch 버그 수정

기존: `response` 원본(파라미터 JSON 포함) 전체를 히스토리에 저장 → 채팅창에 JSON 출력되는 문제.
수정: catch에서 에러 메시지 텍스트만 저장.

---

## 2026-03-17 (2차) — 배포 준비: i18n 확장 + 프롬프트 수정

### 다국어 지원 추가 (간체 중국어 / 일본어)

- RimWorld 언어 폴더명 기준: `ChineseSimplified`, `Japanese` (언어 폴더 prefix 매칭 방식)
- `Languages/ChineseSimplified/Keyed/MapGenAI.xml`, `Languages/Japanese/Keyed/MapGenAI.xml` 신규 추가
- 설정창 전체(11개 `MapGenAI_Settings_*` 키) 포함하여 모든 UI 키 번역

### 설정창 다국어화 (L10n.IsKorean() → .Translate() 마이그레이션)

- `TextToMapSettings.cs`: `using MapGenAI.UI;` 제거, 모든 `L10n.IsKorean() ? "ko" : "en"` 인라인 패턴을 `.Translate()` 호출로 교체
- 이제 4개 언어 모두 설정창 자동 대응

### 버튼 텍스트 축약

- `MapGenAI_PresetSave`: "프리셋 저장"→"저장" / "Save Preset"→"Save"
- `MapGenAI_PresetLoad`: "프리셋 불러오기"→"불러오기" / "Load Preset"→"Load"

### Welcome 문구 수정

- 한국어: "자연어로 설명해 보세요" → "자유롭게 설명해 보세요" (보다 자연스러운 표현)

### 시스템 프롬프트 개선: 언덕/바위 제거 처리

**배경**: GenStep_ElevationFertility 패치는 Postfix — RimWorld 기반 Perlin 노이즈가 먼저 실행됨.
`hills:none`만으로는 기반 노이즈가 남아 실제로 평평하지 않음.
`hill_amount:0.5`가 전체 고도에 -0.5 오프셋을 적용하여 실질적인 평지화.

**수정 내용**:
- 영어 추가파라미터 섹션에 `hill_amount` 설명 추가 (한국어에는 이전 세션에서 추가됨)
- 한/영 규칙 섹션: "완전 평지 요청 = hills:none + hill_amount:0.5 + elevation_shapes:[] 조합" 명시
- 한/영 규칙 섹션: "바위" 요청 구분 — 돌덩어리 = rock_chunks:false / 바위 지형(산) = hill_amount:0.5 + hills:none
- 비해안/해안 few-shot 양쪽에 평지 예시 + 바위 제거 예시 추가
- elevation_shapes 수정 규칙: 추가/제거/전체삭제 ①②③ 명시 + 동적 제거 예시 추가

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
| 번역 키가 키 이름 그대로 출력 (예: `MapGenAI_Undo`) | `.Tr()` 확장 메서드가 게임 시작 시 빌드된 내부 카탈로그를 참조하여, 나중에 추가된 키는 `keyedReplacements` 딕셔너리에 정상 로드되어도 찾지 못함. 반면 `.Translate()`는 `TryGetTextFromKey()`를 직접 호출하므로 항상 정상 작동. | **모든 번역 키 호출을 `.Tr()` 대신 `.Translate()` 로 통일할 것.** 파라미터 있는 경우도 동일: `.Tr(arg)` → `.Translate(arg)`. 디버그로 `keyedReplacements`에 키가 있는데도 표시가 안 된다면 `.Tr()` → `.Translate()` 교체가 해답. |
