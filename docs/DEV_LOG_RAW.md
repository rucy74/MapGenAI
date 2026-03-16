# DEV_LOG_RAW — 날것의 개발 노트

정리 없이 그냥 다 적는 파일. 실패한 시도, 헷갈린 것, 생각의 흐름, 조사 중간 결과 전부 포함.
정리된 버전은 DEV_LOG.md.

---

## 2026-03-14

### 처음 모딩 시작할 때 막혔던 것들

C# 개발 환경 자체가 없었음. dotnet 명령어 PATH에 없고, PowerShell에서도 dotnet 못 찾고. 결국 `C:\Program Files\dotnet\dotnet.exe` 전체 경로로 호출하는 방식으로 해결. 이게 앞으로 빌드할 때마다 써야 함.

About.xml 앞에 공백 들어가면 림월드가 모드를 인식 못 함. BOM 있는 UTF-8도 문제. 모든 XML 파일은 BOM 없는 UTF-8로 써야 함. PLAN.md도 나중에 한글이 깨졌는데 같은 인코딩 문제.

---

### Phase 1~2 삽질 기록

**CS8630 nullable 오류**: `<Nullable>enable</Nullable>` 넣으면 net472에서 빌드 안 됨. LangVersion 9.0으로 낮춰서 해결.

**HttpClient**: net472에서 System.Net.Http는 자동 포함이 아님. .csproj에 명시적으로 추가해야 함. 이거 몰라서 한참 헤맸음.

**Tests 폴더**: Source/Tests/ 에 콘솔 테스트 앱 만들었더니 메인 .csproj가 Tests 폴더 .cs 파일까지 같이 빌드하려 해서 컴파일 에러. `<Compile Remove="Tests\**" />` 로 해결. 이런 건 처음에 모르면 왜 에러나는지 이해하기 어려움.

**DebugAction**: RimWorld 1.5까지는 `Verse` 네임스페이스에 있었는데 1.6에서 `LudeonTK` 로 이동. `using LudeonTK;` 없으면 속성 인식 안 됨.

**selectedTile**: 1.6에서 `WorldSelector.selectedTile` → `WorldSelector.SelectedTile` 대문자로 변경. 이런 Breaking Change는 컴파일러가 잡아주긴 하지만 헷갈림.

**TextAnchor**: `using UnityEngine.TextRenderingModule` 안 추가하면 `TextAnchor` 사용 불가. UI 코드 짤 때 이거 없으면 오류남.

**gemini-2.0-flash deprecated**: 처음엔 이 모델명 썼다가 신규 유저에게 응답 없음. `gemini-2.5-flash`로 바꿔야 함.

---

### WorldInspectPane 패치 왜 실패했나

처음 설계:
```csharp
[HarmonyPatch(typeof(WorldInspectPane), "DoInspectPaneButtons")]
```

테스트해보니 버튼 안 뜸. 원인 분석:
- `WorldInspectPane.DoInspectPaneButtons`는 월드맵에서 **정착지, 캐러밴 같은 오브젝트**를 클릭했을 때만 호출됨
- 빈 타일 클릭하면 `Find.WorldSelector.SelectedTile`은 설정되지만 이 메서드는 안 불림
- 스크린샷에서 봤던 바이옴 정보 패널(온대림, 소형 언덕 등)은 Map Preview 모드가 그리는 것. 바닐라가 아님
- 즉, 바닐라에서 빈 타일 클릭 → WorldInspectPane 자체가 버튼 영역을 그리지 않음

두 번째 시도:
```csharp
[HarmonyPatch(typeof(WorldInterface), "WorldInterfaceOnGUI")]
```
이건 월드 화면이 켜져 있는 동안 매 프레임 호출되는 메서드. 여기 Postfix 달면 항상 그릴 수 있음.

→ 여전히 버튼 안 뜨는 것 같다고 유저 말함. 알고 보니 모드를 모드 목록에서 활성화 안 했을 가능성 높음. 아직 실제 확인 전.

---

### Map Preview 모드 조사 (2026-03-14 오후)

유저가 캡처한 스크린샷 보니까 우측 상단에 톱니바퀴 버튼 + 4개 맵 미리보기 썸네일. 이게 다 Map Preview 모드(2800857642) 기능.

**조사 방법**: DLL 직접 리플렉션. 소스 없음.

**Map Preview 패키지 ID**: `m00nl1ght.MapPreview`

**MapPreviewMod.dll 타입 조사 결과**:
- `MapPreview.MapPreviewToolbar` — 툴바 클래스
- `MapPreview.MapPreviewToolbar+Button` — 추상 버튼 클래스 (프로퍼티: IsVisible, IsInteractable, AlignRight, Tooltip, Icon, 메서드: OnAction)
- `MapPreview.Compatibility.ModCompat_PrepareLanding+ButtonOpenPrepareLanding` — 다른 모드(Prepare Landing)가 이 방식으로 버튼 추가함
- `MapPreview.Compatibility.ModCompat_MapDesigner`, `ModCompat_GeologicalLandforms`, `ModCompat_MapReroll` 등 — Map Preview가 직접 호환성 브릿지를 만들어줌

→ **문제**: 다른 모드들이 버튼을 추가하는 건 Map Preview가 hardcode로 지원해줘서 가능한 것. 우리가 외부에서 Map Preview 툴바에 버튼을 끼워 넣는 API는 없음.

**MapPreview.dll (core) 조사**:
- `MapPreviewAPI` — Init, Cleanup, SubscribeGenPatches, OnWorldChanged 이벤트
- `MapPreviewGenerator` — 실제 미리보기 텍스처 생성기 (백그라운드 쓰레드)
- `MapPreviewRequest` — 미리보기 요청 객체
- `MapPreviewResult` — 결과 (CopyToTexture, Pixels 등)

→ SubscribeGenPatches 같은 API가 있긴 한데, 이건 맵 생성 패치를 끼워 넣는 용도지 UI 버튼 추가하는 게 아님

**결론**: Map Preview 툴바에 직접 버튼 추가 불가. 독립적으로 우리 패널 그리는 게 최선.

---

### 우리가 Map Preview를 직접 구현하기로

유저 요구사항: "Map Preview 없어도 Map Preview가 나오도록"

이게 단순하게 들리는데 실제로는 상당히 큰 작업:
- Map Preview는 실제로 맵 생성 파이프라인을 백그라운드 쓰레드에서 돌려서 128x128 텍스처 생성
- 이걸 완전히 재구현하려면 MapGenerator 관련 패치, 백그라운드 쓰레드 관리 등이 필요

우리가 할 수 있는 현실적인 수준:
- 타일 바이옴 기본 색상 + Perlin noise로 변화 주기
- 해발고도 → 밝기로 표현
- 강 있으면 파란 가로 밴드
- 언덕 → 중앙 어두운 그림자
→ 실제 지형 렌더링은 아니지만 "어떤 종류의 타일인지" 시각적으로 파악 가능

**실제 구현 시 발견한 RimWorld 1.6 API 변경사항**:
- `Tile` → `SurfaceTile` 로 클래스명 변경
- `tile.biome` → `tile.PrimaryBiome` (프로퍼티)
- `tile.hilliness`, `tile.elevation`, `tile.Rivers` 는 그대로 (다행)
- 이 변경사항을 빌드 에러로 발견하고 수정

---

### RimWorld 1.6 API 변경사항 모음 (발견된 것만)

| 구버전 | 신버전(1.6) | 비고 |
|---|---|---|
| `Tile` | `SurfaceTile` | 월드 타일 클래스 |
| `tile.biome` | `tile.PrimaryBiome` | 프로퍼티로 변경 |
| `[DebugAction]` 네임스페이스 | `LudeonTK` | 이전: `Verse` |
| `WorldSelector.selectedTile` | `.SelectedTile` | 대소문자 |
| `gemini-2.0-flash` | `gemini-2.5-flash` | API 변경 (모델 deprecated) |

---

### 현재 파일 구조 및 상태 (2026-03-14 저녁 기준)

```
mapgen_ai/
  About/About.xml            ← 패키지 ID: Choco.MapGenAI, Harmony 필수
  Assemblies/MapGenAI.dll    ← 빌드 완료, 배포됨
  Defs/                      ← (비어 있음, 나중에 GenStep Def 추가 예정)
  Source/
    Core/
      TextToMapMod.cs        ← MapGenAIMod, MapGenAISettings
    LLM/
      ILLMClient.cs
      GeminiClient.cs
      OpenAIClient.cs
      LocalClient.cs
    MapGen/
      MapGenParams.cs
    UI/
      Dialog_TextToMap.cs    ← 멀티턴 채팅 다이얼로그
      SimpleJson.cs
    Patches/
      WorldInspectPane_Patch.cs  ← WorldInterface.WorldInterfaceOnGUI 패치
      TilePreviewGenerator.cs    ← 바이옴 색상 기반 미리보기 텍스처
    Tests/
      Program.cs
  dev_config.json            ← API 키 (git 제외)
  PLAN.md
  DEV_LOG.md
  DEV_LOG_RAW.md             ← 이 파일
```

---

### 아직 해결 안 된 것들 / 불확실한 것들

**강 방향**: 현재 미리보기에서 강을 항상 가로 밴드(y=0.5)로 표시. 실제 타일 데이터에서 강이 어떤 방향으로 흐르는지(수평/수직) 가져올 수 있는지 확인 안 됨. `SurfaceTile.Rivers`에 방향 정보가 있을 수도 있음.

**Map Preview 버튼 위치 충돌 가능성**: Map Preview 있을 때 우리 버튼을 `UI.screenWidth - BtnW - 6f, 6f`에 그리는데 이게 정확히 Map Preview 창과 겹칠 수 있음. 실제 인게임에서 확인 필요.

**SurfaceTile 타입 캐스팅**: `Find.WorldGrid[tileId]`가 `SurfaceTile`을 반환하는지, 아니면 상위 타입 반환하는지 확인이 필요. 빌드는 되지만 런타임에서 null이 될 수도 있음.

**미리보기 캐시 메모리**: 타일마다 128x128 Texture2D를 캐시하면 많은 타일 클릭 시 메모리 차지. 적당한 캐시 크기 제한 필요 (현재는 무제한).

**Phase 4 강 생성 문제**: 맵 생성 레벨에서 강을 그리려면 월드 타일 레벨에서 "강 있음"이 결정돼 있어야 함. GenStep이 아닌 월드 타일 데이터를 수정하는 방식이 필요할 수 있음. 이 부분 아직 미조사.

---

### Map Preview 없을 때 미리보기 품질 개선 아이디어 (나중에)

현재: 바이옴 단색 + noise → 실제 지형과 다름

더 좋게 하려면:
1. Map Preview의 `MapPreviewGenerator`가 하는 것처럼 백그라운드 쓰레드에서 실제 맵 생성 파이프라인 일부 돌리기
   → `GenStep_ElevationFertility`, `GenStep_Terrain` 등을 fake map에 적용
   → 복잡하지만 실제 지형 미리보기 가능
2. `TrueTerrainColors` 클래스 (MapPreview.dll에 있음) 활용 가능성 조사
   → 이미 구현돼 있는 지형 색상 매핑 테이블

당장 필요 없음, Phase 5에서 검토.

---

### 메모: Map Preview의 내부 동작 (리플렉션으로 파악한 것)

`MapPreview.MapPreviewGenerator`:
- 백그라운드 쓰레드에서 실제 맵 생성 파이프라인 실행
- `MapPreviewRequest`를 큐에 넣으면 처리
- 결과는 `MapPreviewResult` (픽셀 배열 + CopyToTexture)

`MapPreview.MapPreviewAPI`:
- `SubscribeGenPatches(patchGroup)` — 맵 생성 중에 커스텀 패치 적용 가능
- `OnWorldChanged` 이벤트 — 월드 변경 시 콜백
- `IsGeneratingPreview` — 현재 생성 중 여부

`MapPreview.TrueTerrainColors`:
- 지형 타입별 실제 색상 매핑 (맵 미리보기에서 올바른 지형 색상 표시용)

우리가 나중에 쓸 수 있는 것: `TrueTerrainColors` + `MapPreviewAPI.SubscribeGenPatches`를 사용해서 우리 파라미터가 반영된 미리보기 생성 가능성 있음. Phase 5 후보.

---

### 트러블슈팅 모음 (시간순)

1. `CS8630` → LangVersion 9.0으로 낮추기
2. `HttpClient` 빌드 에러 → .csproj System.Net.Http 추가
3. Tests 폴더 충돌 → `<Compile Remove="Tests\**" />`
4. `DebugAction` 못 찾음 → `using LudeonTK;`
5. `selectedTile` 없음 → `SelectedTile` 대문자
6. `TextAnchor` 없음 → UnityEngine.TextRenderingModule 참조 추가
7. `gemini-2.0-flash` 안됨 → `gemini-2.5-flash`
8. PLAN.md 한글 깨짐 → UTF-8 no-BOM 재작성
9. `TextToMap.dll` 잔존 → Assemblies 폴더에서 삭제
10. `WorldInspectPane.DoInspectPaneButtons` 안 불림 → `WorldInterface.WorldInterfaceOnGUI`로 교체
11. `WorldRendererUtility.WorldRenderedNow` 없음 → 제거 (조건 단순화)
12. `UI.screenHeight` 네임스페이스 충돌 → `Verse.UI.screenHeight`
13. `tile.biome` 없음 (1.6 API 변경) → `tile.PrimaryBiome`
14. `tile.biome != null` 미수정 잔존 → 수동 수정
15. `LongEventHandler.ExecuteWhenFinished` 게임플레이 중 안 됨 → volatile fields 패턴으로 교체
16. Enter 키가 창을 닫음 → `doCloseButton = false` + `Event.current.Use()` 추가
17. `HasMapPreview` 오판정 (Map Preview 없는데 true 반환) → 조건 분기 제거, 항상 DrawPreviewPanel() 실행
18. 설정창 이름 "Text to Map"으로 표시 → `SettingsCategory()` 반환값 수정

---

## 2026-03-14 (3차)

### LongEventHandler 함정

`LongEventHandler.ExecuteWhenFinished`는 이름만 봐서는 "나중에 메인 스레드에서 실행해줄게" 처럼 들리는데 실제로는 **LongEvent(로딩 화면) 큐**에 넣는 거임. 일반 게임플레이 중 백그라운드 Task.Run 콜백을 메인 스레드로 전달하는 용도로 쓰면 아무것도 안 됨.

해결 패턴:
```csharp
// 백그라운드: 결과 먼저 저장, 플래그는 마지막에 set (ordering 중요)
_pendingData = result;
_dataReady = true; // 이게 먼저 set되면 null 읽을 수 있음

// 메인 스레드 (DoWindowContents, 매 프레임):
if (_dataReady) {
    _dataReady = false;
    var data = _pendingData;
    _pendingData = null;
    ProcessData(data);
}
```
`volatile` 키워드는 CPU 캐시 최적화로 인한 stale read 방지용. C# volatile은 완전한 메모리 배리어가 아니지만 단순 flag 패턴에서는 충분함.

### HasMapPreview 오판정 원인 불명

`ModLister.GetActiveModWithIdentifier("m00nl1ght.MapPreview")` 가 Map Preview 없는 환경에서 null이 아닌 값을 반환하는 게 이상함. 가능한 원인:
- Workshop에 설치는 됐지만 모드 목록에서 비활성화된 상태일 수 있음 (`GetActiveModWithIdentifier` vs `GetModWithIdentifier` 차이?)
- 캐싱 타이밍 문제 (nullable bool `_hasMapPreview` 가 게임 로드 전에 설정됨?)

어쨌든 유저가 원하는 게 "항상 미리보기 패널이 나오게"이므로 조건 분기 자체를 제거하는 것이 맞는 방향. DrawPreviewPanel()이 버튼도 포함하므로 DrawButtonOnly()는 이제 dead code.

### TypeLoadException: Map Preview soft dependency

MapPreview.dll을 직접 참조해서 MapPreviewWidget을 상속하면, Map Preview 미설치 시 모드 전체 로딩 실패.
```
ReflectionTypeLoadException: Could not resolve type 'MapPreview.MapPreviewWidget'
```

.NET/Mono는 타입을 lazy load하지만, 클래스 내부에서 참조하는 타입은 해당 클래스를 **JIT 컴파일할 때** 로드를 시도함.
`MapPreviewIntegration.Draw()` 를 직접 호출하는 메서드가 JIT되면 `MapPreviewWidget` 타입 로딩 시도 → 실패.

**해결**: 두 단계 격리
1. 어셈블리 레벨 체크: `AppDomain.CurrentDomain.GetAssemblies()`에서 "MapPreview" 이름 검색
2. `[MethodImpl(MethodImplOptions.NoInlining)]` 속성으로 Map Preview 호출 메서드를 분리. JIT가 해당 메서드를 인라인하지 않으므로 `MapPreviewIntegration` 타입이 실제 호출 시점까지 로드되지 않음.

```csharp
// 안전: MapPreviewIntegration 타입은 이 메서드가 실제 호출될 때만 JIT → 로드
[MethodImpl(MethodImplOptions.NoInlining)]
private static bool TryDrawMapPreview(Rect rect, int tileId) => MapPreviewIntegration.Draw(rect, tileId);

// 호출부: 어셈블리 확인 후에만 호출
if (IsMapPreviewLoaded()) drawnByMapPreview = TryDrawMapPreview(previewRect, tileId);
```

### GenStep 기반 미리보기 구현 (Map Preview 동일 방식)

Map Preview 소스(GitHub)를 분석하여 동일 방식 구현:
- `TerrainPreviewGenerator`: 임시 Map 생성 → GenStep_ElevationFertility + GenStep_Terrain 실행 → TerrainGrid 읽기 → Color 매핑
- Map Preview는 백그라운드 스레드 + Rand 패치로 처리하지만, 우리는 메인 스레드 + LongEventHandler로 단순 구현
- 캐시로 반복 호출 방지 (한 번만 프리즈)
- 지형 색상은 Map Preview의 TrueTerrainColors DefaultColors에서 가져옴

핵심 차이점 (Map Preview vs 우리):
- Map Preview: 백그라운드 스레드 + Rand 패치 + 전용 MapComponent → 프리즈 없음
- 우리: 메인 스레드 + LongEventHandler → 잠깐 프리즈
- 결과 품질은 동일 (같은 GenStep 실행)

### NoInlining만으로는 부족 — 상속이 문제

`[MethodImpl(NoInlining)]` 으로 메서드를 격리해도 소용없었음. 원인: `MapGenAIPreviewWidget : MapPreviewWidget` 상속.

Mono는 어셈블리 로딩 시 **모든 타입의 부모 클래스를 즉시 resolve**함. `GetTypes()` 호출이 아니더라도, Harmony의 `PatchAll()`이 어셈블리의 타입을 순회하면서 발생.

최종 해결: MapPreview 타입 상속/필드 사용 완전 제거. `RequestPreview()` 메서드 내부 로컬 변수에서만 MapPreview 타입 사용. 이러면 JIT 시점까지 해당 타입 resolve를 시도하지 않음.

### 반성: 외부 API는 먼저 공식 소스 확인해야

Map Preview API를 DLL reflection + 컴파일 오류 탐색으로 파악하려 했는데, 전부 비효율적이었음.
GitHub에 소스 있고 API 문서화 잘 돼 있었음: https://github.com/m00nl1ght-dev/MapPreview

앞으로 외부 모드/라이브러리 API 쓸 때는 먼저 WebSearch로 GitHub/스팀 페이지 확인.

**Map Preview 실제 API:**
```csharp
// 요청 생성
var req = new MapPreviewRequest(world.info.seedString, tileId, mapSize)
{
    UseMinimalMapComponents = true,
    UseTrueTerrainColors = true,
};
// 큐에 넣기
var promise = MapPreviewGenerator.Instance.QueuePreviewRequest(req);
// 위젯에 연결
widget.Await(promise, tileId);
// 매 프레임 그리기
widget.Draw(rect);
```

MapPreviewWidget은 abstract → 서브클래스 필요. `DrawGenerating` override로 생성 중 UI 커스터마이징.

### GeminiClient.ExtractText 이스케이프 버그 (LLM 응답 안 보이는 근본 원인)

Gemini API 응답에서 text 필드를 추출할 때 JSON 이스케이프 디코딩이 `\n`만 처리하고 `\"`는 처리 안 했음.

Gemini API 응답 형태:
```json
"text": "```json\n{\"action\":\"ask\",\"message\":\"msg\"}\n```"
```

구 버전 ExtractText:
```csharp
sb.Append(json[i] == '\\' && json[i+1] == 'n' ? '\n' : json[i]);
if (json[i] == '\\') i++;
```
→ `\"` 를 만나면 `\`만 출력, `"`는 스킵됨 → 결과: `\action\:\ask\`

수정 후:
```csharp
if (json[i] == '\\') {
    char next = json[i+1];
    switch(next) {
        case 'n': sb.Append('\n'); break;
        case '"': sb.Append('"'); break;   // 이게 빠져있었음!
        case '\\': sb.Append('\\'); break;
        ...
    }
    i++; continue;
}
```
→ 결과: `"action":"ask"` 정상 출력

추가로 HandleResponse에서 코드블록 처리도 `{` ~ `}` 직접 추출로 변경 (더 견고함).

오프라인 테스트 프로젝트로 검증 완료 (C:\Users\choco\AppData\Local\Temp\parsetest).

### closeOnAccept 함정

`doCloseButton = false` + `Event.current.Use()` 조합으로 Enter 키 문제를 해결했다고 생각했는데 여전히 창이 닫혔음.

원인: RimWorld `Window` 클래스에는 `closeOnAccept`라는 별도 필드가 있음. 기본값이 `true`라서 Accept 키(Enter)를 누르면 창이 닫힘. 이건 `doCloseButton`이나 `Event.current.Use()`와 독립적으로 작동하는 Window 레벨의 처리.

수정: 생성자에 `closeOnAccept = false;` 추가.

RimWorld Window에서 신경써야 할 닫힘 관련 필드들:
- `doCloseButton` — 하단 닫기 버튼 표시 여부
- `doCloseX` — 우상단 X 버튼 표시 여부
- `closeOnAccept` — Enter/Accept 키로 닫힘 여부 (기본 true)
- `closeOnCancel` — Escape 키로 닫힘 여부 (기본 true)
- `closeOnClickedOutside` — 창 밖 클릭으로 닫힘 여부 (기본 false)

### 미리보기 텍스처 안 보이는 원인 두 가지

**원인 1: DrawMaterial.color**
`tile.PrimaryBiome.DrawMaterial.color` → `WorldTerrain` 셰이더에는 `_Color` 프로퍼티가 없음.
Unity에서 `Material.color`는 `GetColor("_Color")` 의 단축 호출인데, 프로퍼티가 없으면 경고만 찍히고 `Color.white` 반환할 수도, 예외 던질 수도 있음 (Unity 버전별로 다름).
Player.log에서 "Material 'WorldTerrain' with Shader 'Custom/WorldTerrain' doesn't have a color property '_Color'" 경고가 찍혔음 → 우리 코드에서 호출한 것.
해결: defName 기반 하드코딩 색상 테이블 `GetBiomeColor()` 로 교체.

**원인 2: GUI.color 오염**
RimWorld의 월드맵 렌더링 코드가 `GUI.color`를 변경하고 미처 복원 안 하는 경우가 있음.
`GUI.DrawTexture(rect, tex)` 는 현재 `GUI.color` 값을 텍스처 색상에 곱해서 렌더링함.
`Widgets.DrawBoxSolid`는 내부적으로 `GUI.color = color; ... GUI.color = oldColor;` 패턴을 쓰지만, `GUI.DrawTexture` 직접 호출은 그렇지 않음.
해결: `GUI.DrawTexture` 전후로 `GUI.color = Color.white` / 복원.

이 두 가지 중 하나만 해도 됐을 수 있는데, 둘 다 수정.

### LLM 응답 안 오는 이유 조사 중

로그 추가 전에는 어디서 막히는지 전혀 알 수 없었음. 가능한 원인들:
1. `LLMClientFactory.Create()` 자체에서 throw (switch expression에서 예외)
2. Task.Run이 실행되지만 HTTP 요청에서 exception
3. 응답은 왔는데 `_responseReady` 플래그 처리 안 됨 (volatile 문제?)
4. API 키 빈 문자열로 HTTP 400 반환 → catch에서 `_pendingError` 설정 → `_responseReady = true` → DoWindowContents에서 읽혀야 하는데 안 읽힘?

이번에 Log.Message 추가해서 다음 테스트에서 Player.log 확인 예정.

### 모델 선택 UI 설계 결정

처음엔 텍스트 입력창만 있었는데 유저가 "스펠링 틀릴 수 있잖아" 라고 지적. API에서 모델 목록 불러와서 선택하는 게 맞는 UX. 기존 목록 불러오기 버튼이 이미 있었으니 그걸 선택 UI로 확장.

핵심 UX 결정:
- 목록이 없으면: "모델 목록 불러오기" → 클릭 시 API 호출
- 목록 있고 접혀있으면: "▼ 모델 선택 (현재: X)"
- 펼쳐있으면: "▲ 접기 (현재: X)"
- RadioButton 클릭 즉시 접힘 (showList = false)
- 선택된 모델명이 버튼 텍스트에 항상 표시됨

---

## 2026-03-15

### 프리셋 저장/불러오기 구현

**설계 결정: JSON 직렬화 방식**
net472에서 System.Text.Json 사용 불가. Newtonsoft.Json 의존성도 배제 방침. 두 가지 선택지:
1. key=value 텍스트 포맷 (Map Designer 방식) — 단순하지만 중첩 객체(river), 배열(mutators) 처리 번거로움
2. 수동 JSON 문자열 생성 + 기존 SimpleJson 파서 재사용 — JSON 포맷이라 범용적이고 디버깅 편함

→ 2번 선택. 쓰기는 StringBuilder로 JSON 문자열 생성, 읽기는 SimpleJson.Parse() 재사용.

**UI 레이아웃 결정**
하단 버튼 영역에 3개 버튼 가로 배치: [이 설정으로 맵 생성] [프리셋 저장] [프리셋 불러오기]
- "프리셋 저장" 클릭 시 버튼 위에 이름 입력 행 표시 (chat 영역 높이 자동 줄어듦)
- "프리셋 불러오기"는 FloatMenu 사용 (RimWorld 네이티브 드롭다운)
- 파라미터 미준비 상태에서도 "프리셋 불러오기" 버튼 표시 (기존 프리셋 바로 로드 가능)

**삭제 기능을 FloatMenu에 통합**
별도 삭제 UI 대신 FloatMenu 하단에 "[삭제] 프리셋이름" 항목 추가. 실수로 누르기 어려운 위치 + 별도 확인 대화 없음 (JSON 파일 하나 삭제라서 복구 쉬움).

**파일명 sanitize**
한글 프리셋 이름 지원 필요. `Path.GetInvalidFileNameChars()`로 OS별 금지 문자만 언더스코어로 치환. 한글, 일본어 등 유니코드 문자는 그대로 유지.

**PresetManager가 static class인 이유**
MapGenParams도 static class이고, 프리셋 관리는 전역 상태. 인스턴스 불필요. 테스트 시에는 직접 파일 시스템 접근하므로 DI 불필요.

---

## 2026-03-15 (2차) -- 테스트벤치 30개 확장

### SystemPrompt 동기화 삽질

TestBench.cs의 SystemPrompt가 Dialog_TextToMap.cs와 다르다는 걸 발견. Dialog_TextToMap에는 coast_direction, rock_count, ore_density, mutators 전체 목록이 포함되어 있는데 TestBench에는 없었음. 이러면 LLM이 테스트벤치에서 새 파라미터를 전혀 모르는 상태로 응답함.

해결: Dialog_TextToMap.cs의 BuildSystemPrompt()에서 생성하는 프롬프트 구조를 그대로 가져옴:
- SystemPrompt (기본 JSON 스키마 + 규칙) — params에 coast_direction, rock_count, ore_density, mutators 추가
- TileCtx() (타일 컨텍스트) — 조절 가능 항목/불가능 항목, mutator 카테고리별 defName 전체 목록 포함

### TileCtx() 확장

기존 TileCtx()는 바이옴/지형/강/해안 정도만 포함. 새 TileCtx()는 Dialog_TextToMap.cs의 tileContext 전체를 재현:
- 조절 가능한 파라미터 목록 (hills, hill_amount, vegetation_density, ..., mutators)
- mutators 사용 가능한 값 카테고리별 전체 목록
- 조절 불가능한 것 (강 방향/모양, 강 추가/제거, 바다)
- 규칙 (강 있으면 river.present=true 등)

### ContainsMutator() 헬퍼

mutators 검증은 단순 Contains("HotSprings")로는 불충분. JSON에서 "mutators" 배열 범위를 찾아서 그 안에 defName이 있는지 확인하는 전용 함수 작성. 이유: 만약 description이나 다른 필드에 mutator 이름이 언급되면 false positive.

### 케이스 #1 FAIL 원인과 해결

첫 실행 시 29/30. 케이스 #1 ("왼쪽에서 오른쪽으로 강이 흐르는 맵")이 FAIL.

원인: 새 SystemPrompt에 "강 방향/모양은 타일의 월드맵 데이터에 의해 고정됨", "방향/모양 변경 요구 시 '현재는 강 방향 조절이 불가능합니다'라고 안내" 규칙이 포함됨. LLM이 이 규칙을 충실히 따라 ask로 강 방향 제한을 안내한 것. 기존 검증은 generate만 PASS로 판정.

해결: 검증 로직 완화. generate + river.present=true 뿐 아니라, ask + 강/방향 관련 안내도 PASS.

### LLM 비결정성 대응

30개 케이스로 늘리면서 느낀 점: LLM 응답은 비결정적이므로 검증은 관대해야 함. 특히:
- ask도 generate도 둘 다 합리적인 응답인 경우가 많음 (케이스 #9 해안 바다 요청, #17 해안 방향 미지정 등)
- 숫자 파라미터는 정확한 값보다 범위 체크 (hill_amount > 1.0, ore_density <= 0.1 등)
- mutator는 정확한 defName 매칭이지만, 유사 mutator도 허용 (Lake 또는 LakeWithIsland)
- 극단 케이스(#30)는 generate든 ask든 유효한 JSON이면 PASS

### FAIL 응답 출력 길이 확장

기존: 응답 앞 200자만 출력. 신규 케이스는 mutators 배열이 포함되어 200자로는 부족.
변경: 300자로 확장. 이래도 부족하면 나중에 전체 출력 옵션 추가.

### 2차 실행 결과: 30/30 PASS

---

## 2026-03-15 (3차) -- 인게임 자동 테스트 시스템

### 왜 인게임 테스트가 필요한가

기존 TestBench.cs는 LLM 응답 품질만 검증. 실제 MapGenParams.Apply()가 타일에 mutator를 제대로 적용하는지, Reset()이 원본을 복원하는지 등은 인게임에서만 검증 가능. 매번 수동으로 하나씩 확인하는 건 시간 낭비. DebugAction 한 번 클릭으로 전부 돌릴 수 있어야 함.

### .csproj 수정이 필요했던 이유

Tests\ 디렉터리는 `<Compile Remove="Tests\**" />`로 전체 제외돼 있음. 이건 오프라인 TestBench.cs가 net10.0 콘솔 앱이라서 net472 메인 프로젝트와 함께 빌드하면 안 되기 때문. 하지만 InGameTestRunner.cs는 RimWorld API(DebugAction, Find.WorldGrid 등)를 써야 하므로 메인 어셈블리에 포함해야 함. `<Compile Include="Tests\InGameTestRunner.cs" />`로 이 파일만 다시 포함.

### 설계 결정

**테스트 격리**: 각 테스트 전후에 MapGenParams.Reset() 호출. try/catch/finally 패턴으로 테스트 중 예외 발생해도 다음 테스트에 영향 없음.

**Odyssey 의존성 처리**: Odyssey DLC 없으면 TileMutatorDef가 DefDatabase에 없음. mutator 테스트를 강제 실행하면 NullReferenceException. ModsConfig.OdysseyActive로 체크하고 SKIP 처리.

**결과 출력 방식**: Log.Message 사용. Player.log에서 `[MapGenAI Test]` 검색으로 결과 확인 가능. 인게임 팝업은 번거로우므로 생략.

**타일 mutator 검증 방식**: Find.WorldGrid[tileId].Mutators로 직접 접근. MapGenParams.Apply()가 내부적으로 tile.AddMutator()/RemoveMutator()를 호출하므로, Apply 후 타일의 Mutators 리스트에 해당 defName이 있는지 확인하면 됨.

### 테스트 #6 (빈 mutators) 설계

빈 배열 mutators=[]은 "mutator 변경 없음"을 의미. MapGenParams.Apply() 내부에서 Mutators.Clear() 후 빈 리스트를 순회하므로 타일에는 아무 변화 없어야 함. 단, caves 파라미터는 별도 경로로 처리되므로 caves=false로 설정.

검증 방식: Apply 전 원본 mutator 목록 기록 -> Apply -> Apply 후 mutator 목록 비교 -> 집합 동일성 확인.

### 테스트 #12 (리셋 복원) 설계

가장 까다로운 테스트. Apply()가 타일 mutator를 변경하고, Reset()이 원본으로 복원하는 전체 흐름을 검증.
- 원본 기록 -> Apply(caves=true, HotSprings) -> 변경 확인 -> Reset() -> 복원 확인
- "변경 확인"이 필요한 이유: 타일이 이미 Caves를 가지고 있으면 변경이 없었을 수 있고, 그러면 복원 검증이 무의미해짐

---

## 2026-03-15

### Map Designer 누락 기능 구현 — 분석

요청: Map Designer 1.6에서 활성 패치 중 MapGenAI에 누락된 기능 6개 일괄 구현.

**1. RiverDirectionPatch** — TileMutatorWorker_River.IsFlowingAToB + GetMapEdgeNodes Prefix.
RW 1.6에서 IsFlowingAToB(Vector3, Vector3, ref float), GetMapEdgeNodes(Map, ref float) 둘 다 확인.
Map Designer의 HelperMethods.GetRiverDirection()은 단순히 settings.riverDir을 angle에 대입. 간단.

**2. RiverCenterPatch** — TileMutatorWorker_River.GetRiverCenter Postfix.
RW 1.6에서 GetRiverCenter(Map) -> IntVec3 확인. Map Designer는 mapSize * (0.5 + displacement) 패턴.
displacement = UI 슬라이더 값인데, 우리는 0.0-1.0 position으로 변환 필요. position - 0.5 = displacement.

**3. RiverBankPatch** — 스킵. Map Designer 1.6에서 완전히 주석 처리됨 (`/* */`).
RW 1.6에서 RiverMaker 대신 TileMutatorWorker_River 기반으로 변경. 기존 RiverMaker.TerrainAt 패치 불가.

**4. RockChunkPatch** — GenStep_RockChunks.Generate Prefix. 매우 간단 (3줄).

**5. MountainSettingsPatch Transpiler** — 가장 복잡한 부분.
GenStep_ElevationFertility.Generate의 IL에서 Ldc_R8 0.021과 2.0을 찾아 Call로 교체.
Map Designer는 `float.TryParse(codes[i].operand.ToString(), out result)` 로 float 비교.
주의: IL의 Ldc_R8은 double이지만 float.TryParse로 비교하면 0.020999999716877937 -> 0.021f가 됨. 이게 매칭 트릭.

**RiverDirection 각도 규칙 분석:**
RW 내부: angle은 "강이 흐르는 방향"을 의미. 0=east(오른쪽), 90=north(위), 180=west(왼쪽), 270=south(아래).
Map Designer는 settings.riverDir을 그대로 대입. UI에서 슬라이더로 0-360 조절.
LLM 프롬프트에는 "left=0, up=90, right=180, down=270" 시맨틱으로 제공.

### 구현 상세

**RiverData 확장**: direction_angle(float, -1=auto), z_position(float) 추가. 하위 호환 위해 direction 문자열도 유지.
"horizontal"/"vertical" 문자열은 Apply()에서 angle로 자동 변환 (horizontal->0, vertical->-1=auto).

**ParseParams 단축키**: river_direction/river_position을 params 최상위에 직접 쓸 수 있도록 지원.
river 객체와 동시 사용 시 단축키가 river 객체의 값을 override.

**Transpiler 참고**: Map Designer의 Transpiler는 `float.TryParse(codes[i].operand.ToString(), out result)`를 사용하여
double 상수를 float로 변환 후 비교. 이 트릭이 0.020999999716877937(double) == 0.021(float) 매칭을 가능하게 함.
동일 방식으로 구현.

**hillSize 시맨틱 매핑**: small=0.035(잘게 쪼개짐), medium=0.021(바닐라), large=0.012(거대 산맥).
직관과 반대: 높은 frequency = 작은 패턴 = "small hills". Map Designer UI에서도 슬라이더가 이 방식.

---

## 2026-03-15 (4차) — 기존 모드 철저 조사

### 조사 방법
WebSearch로 Steam Workshop, GitHub, Reddit, Nexus Mods 등 멀티 소스 검색. 키워드: "auto build", "building planner", "colony layout", "base designer", "auto architect", "blueprint template", "AI LLM", "GenConstruct PlaceBlueprintForBuild" 등.

### 발견한 것 중 특히 주목할 만한 것

**1. Universal Blueprints — 우리 모드와 가장 유사한 컨셉**
- 101개 내장 블루프린트 + 브라우저 UI + 프로젝션(고스트) 시스템
- 모드 건물 포함 내보내기 가능 (XML + PNG 이미지 병합)
- 다른 모드가 Blueprints 폴더에 블루프린트 넣으면 자동 임포트
- → LLM이 생성한 레이아웃을 Universal Blueprints 포맷으로 내보내는 연동 가능성 있음

**2. HaploX1/RimWorld-MapGenBaseBlueprints — 맵 생성 시 기지 자동 배치**
- 맵 생성 GenStep에서 커뮤니티 제출 블루프린트 기반으로 기지를 자동 배치
- Genstep_CreateBlueprintSingle.cs에 건물 배치 코드 전체 공개
- Fluffy Blueprints → MapGen Blueprints 변환 도구도 포함
- → 우리 Phase 5 이후에 "LLM이 설계한 기지를 맵 생성 시 자동 배치" 기능의 직접적 참조가 될 수 있음

**3. RIMAPI — REST API로 외부에서 림월드 제어**
- 120+ 엔드포인트, localhost:8765
- 외부 Python/JS 스크립트에서 식민지 데이터 읽기 + 명령 내리기 가능
- → LLM 에이전트가 RIMAPI를 통해 실시간으로 건물 배치 지시하는 아키텍처도 가능

**4. AI 모드 생태계 현황**
- RimGPT (pardeike/brrainz) — Harmony 제작자가 만든 모드. ChatGPT + Azure TTS. 소스 완전 공개.
- RimTalk — 가장 활발. OpenAI/DeepSeek/OpenRouter/Ollama 멀티 프로바이더.
- RimWorldAI Core — 완전 오프라인 로컬 AI. 인터넷 불필요.
- → 현재 AI 모드들은 전부 "대화/내러티브" 용도. 건물 배치에 AI를 쓰는 모드는 아직 없음. 우리가 최초.

**5. 건물 프로그래밍 배치 코드 패턴**
핵심은 GenConstruct.PlaceBlueprintForBuild(). RW-Decompile에서 확인:
```csharp
Blueprint_Build bp = (Blueprint_Build)ThingMaker.MakeThing(sourceDef.blueprintDef, null);
bp.SetFactionDirect(faction);
bp.stuffToUse = stuff;
GenSpawn.Spawn(bp, center, map, rotation, WipeMode.Vanish, false);
```
Fluffy Blueprints의 BuildableInfo.cs가 이 패턴의 가장 깔끔한 실제 사용 예시.

**6. Architect 탭 커스텀 카테고리 방법**
- XML로 DesignationCategoryDef 정의 → Architect 메뉴에 새 탭 자동 추가
- Ludeon 포럼 "[Solved] Adding a new designationCategory" 스레드가 정확한 가이드
- designatorClass로 커스텀 C# Designator를 지정 가능

**7. 드래그로 영역 선택하는 UI 패턴**
- Designator_Place (바닐라) — 단일 셀 배치
- Designator_ZoneAdd (바닐라) — 영역 드래그 (존/구역용)
- Designator Shapes (merthsoft) — 모든 designator에 35가지 도형 적용. 소스 완전 공개.
- → 우리가 "LLM이 설계한 건물 배치 영역을 지정"하는 UI를 만들 때 Designator_ZoneAdd 패턴 참조

**8. 커뮤니티 레이아웃 공유**
- r/RimWorldPorn — 고해상도 렌더 전문. Progress Renderer 모드 필수.
- rimworld.gallery — 웹 갤러리 미러. 카테고리 정리됨.
- Steam 가이드 — Mountain Base Plan (Haven v2) 같은 텍스트 기반 설계도
- → LLM 프롬프트에 "이런 레이아웃 참고" 예시로 넣을 수 있는 소스

### 결론

현재 림월드 모드 생태계에서 "AI가 건물을 자동으로 설계/배치하는 모드"는 존재하지 않음.
AI 모드들은 전부 대화/내러티브 용도. 건물 배치 모드들은 전부 수동 (사용자가 직접 배치).
MapGen AI가 이 두 영역을 결합하면 최초의 "AI 건물 설계자" 모드가 됨.

---

## 2026-03-15 (5차) — 인게임 UI/건물 배치 시스템 조사 (상세)

### 조사 동기
지금까지는 월드맵에서 "맵 생성 파라미터를 AI로 설정"하는 기능만 만들었음. 다음 단계는 식민지 맵에서 "AI가 설계한 건물 레이아웃을 실제로 배치"하는 것. 이를 위해 인게임 UI, 건물 배치 API, 좌표 시스템을 파악해야 함.

### WindowLayer enum 발견

Verse/WindowLayer.cs 디컴파일 결과:
```csharp
public enum WindowLayer { GameUI, Dialog, SubSuper, Super }
```

기존 Dialog_TextToMap은 layer를 명시 안 해서 기본값 Dialog 사용 중. 인게임에서 채팅 오버레이처럼 항상 떠있는 UI를 만들려면 GameUI 레이어를 쓰거나, Window가 아닌 직접 OnGUI 렌더링 방식을 써야 할 수 있음.

GameUI vs Dialog 차이: GameUI는 게임이 일시정지되지 않고 배경에서 계속 돌아감. Dialog는 보통 absorbInputAroundWindow=true와 함께 쓰여서 배경 클릭 차단.

### Window 클래스 필드 정리 (디컴파일)

이전에 closeOnAccept 함정에 빠졌을 때 필드 몇 개만 파악했는데, 이번에 전체 정리:
- `layer` (WindowLayer) = Dialog
- `windowRect` (Rect)
- `InitialSize` (Vector2) = (500, 500) — 오버라이드 가능
- `doCloseButton` = false — 하단 닫기 버튼
- `doCloseX` = false — 우상단 X 버튼
- `forcePause` = false — 창이 열리면 게임 일시정지
- `absorbInputAroundWindow` = false — 창 밖 클릭 차단
- `drawShadow` = true — 창 뒤에 그림자
- `closeOnAccept` = true — Enter로 닫힘 (이미 겪은 함정)
- `closeOnCancel` = true — Esc로 닫힘
- `DoWindowContents(Rect inRect)` — abstract, 서브클래스에서 UI 그리기

→ 인게임 건물 배치 UI를 만들 때: forcePause=false, absorbInputAroundWindow=false로 해야 맵 조작 가능.

### Designator_Place 색상 상수 발견

디컴파일에서 발견한 핵심 색상:
- `CanPlaceColor = new Color(0.5f, 1f, 0.6f, 0.4f)` — 배치 가능 초록
- `CannotPlaceColor = new Color(1f, 0f, 0f, 0.4f)` — 배치 불가 빨강

이 색상을 GhostDrawer에 전달하면 바닐라와 동일한 미리보기 느낌.

### GhostDrawer 분석

GhostDrawer.cs 전체가 매우 짧음 (메서드 2개):
1. `DrawGhostThing` — Obsolete, 하위호환용
2. `DrawGhostThing_NewTmp` — 실제 구현

핵심 플로우:
```
baseGraphic ?? thingDef.graphic
→ GhostUtility.GhostGraphicFor(baseGraphic, thingDef, ghostCol)
→ GenThing.TrueCenter(center, rot, thingDef.Size, altitude)
→ graphic.DrawFromDef(loc, rot, thingDef)
→ thingDef.comps[i].DrawGhost(...)  // 각 컴포넌트의 고스트
→ thingDef.PlaceWorkers[j].DrawGhost(...)  // PlaceWorker 고스트
```

GhostUtility는 ShaderTypeDefOf.EdgeDetect.Shader를 사용해서 반투명 윤곽선 효과 생성. Linked 건물(벽 등)은 uiIconPath 사용, 일반 건물은 원본 그래픽 경로 사용.

고스트 그래픽은 Dictionary<int, Graphic>으로 캐시됨 (key = hash of baseGraphic + thingDef + ghostCol).

### IntVec3의 y는 뭔가

결론: **y는 맵 그리드에서 거의 항상 0**. RimWorld는 Unity의 3D 좌표계를 쓰지만 실제 맵은 x-z 2D 평면. y는 렌더링 고도(AltitudeLayer)에만 사용됨.

증거:
- `ToVector3Shifted()` = `new Vector3(x + 0.5f, y, z + 0.5f)` — x와 z에만 0.5 오프셋(셀 중앙)
- `LengthHorizontalSquared` = `x*x + z*z` — y 완전 무시
- `IsValid` = `y >= 0` — y가 음수가 아니면 유효

맵에서 건물 위치를 다룰 때는 IntVec3(x, 0, z) 형태로 사용.

### Rot4 값과 건물 크기 관계

rotInt 값: 0=North, 1=East, 2=South, 3=West

건물 Size가 (2, 1, 3)이면:
- North/South (rotInt 0, 2): 실제 점유 = 2x3
- East/West (rotInt 1, 3): 실제 점유 = 3x2

`GenAdj.OccupiedRect(center, rot, size)` 가 회전을 자동으로 적용해서 CellRect 반환. 직접 계산할 필요 없음.

IsHorizontal: East(1) 또는 West(3)일 때 true. Horizontal일 때 size.x와 size.z가 swap됨.

### GenConstruct.CanPlaceBlueprintAt 전체 검증 순서

디컴파일로 확인한 검증 순서 (순서 중요):
1. 모든 점유 셀이 맵 범위 안인지 (InBounds)
2. 맵 가장자리 금지 구역인지 (InNoBuildEdgeArea, godMode면 무시)
3. 안개 영역인지 (Fogged)
4. 같은 위치+회전에 동일 ThingDef 또는 동일 Blueprint가 이미 있는지
5. InteractionCell(의자 접근 셀 등)이 맵 밖이거나 Impassable에 막히는지
6. 인접 건물의 InteractionCell을 이 건물이 막는지
7. TerrainDef 중복 체크 (같은 바닥 또는 스무딩 중)
8. CanBuildOnTerrain — 지형 어포던스 체크 (Heavy 건물은 Heavy 지형 필요)
9. Royalty DLC: MonumentMarker 충돌 체크
10. 기존 건물/아이템 위에 배치 가능한지 (CanPlaceBlueprintOver)
11. PlaceWorker.AllowsPlacing() — 커스텀 배치 조건 (환풍구는 벽 위만, 등)

→ 우리 모드에서 일괄 배치 시 godMode=true로 하면 2번 건너뛸 수 있지만, 사용자 모드에서는 false로 해야 안전.

### Command_Action의 단순함

디컴파일 결과 단 5줄:
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
모든 복잡한 렌더링(아이콘, 텍스트, 툴팁, 단축키)은 부모 Command 클래스가 처리. Command_Action은 클릭 시 action() 호출만 담당.

### CompGetGizmosExtra 패턴 (CompTempControl 예시)

RimWorld 바닐라 CompTempControl이 온도 조절 버튼 5개를 추가하는 방식:
```csharp
public override IEnumerable<Gizmo> CompGetGizmosExtra()
{
    foreach (Gizmo item in base.CompGetGizmosExtra())
        yield return item;

    Command_Action cmd = new Command_Action();
    cmd.action = delegate { /* 로직 */ };
    cmd.defaultLabel = "Label";
    cmd.defaultDesc = "Description";
    cmd.hotKey = KeyBindingDefOf.Misc5;
    cmd.icon = ContentFinder<Texture2D>.Get("UI/Commands/TempLower");
    yield return cmd;
}
```

이 패턴으로 건물 선택 시 "AI로 주변 건물 배치" 같은 커스텀 버튼 추가 가능.

### DesignationCategoryDef XML 구조

바닐라 Core에서 DesignationCategories.xml:
```xml
<DesignationCategoryDef>
  <defName>Structure</defName>
  <label>Structure</label>
  <order>200</order>
  <specialDesignatorClasses>
    <li>Verse.Designator_Cancel</li>
  </specialDesignatorClasses>
</DesignationCategoryDef>
```
specialDesignatorClasses에 커스텀 Designator C# 클래스 지정 가능.
건물의 ThingDef에 `<designationCategory>` 태그로 이 카테고리에 소속시킴.
PatchOperation으로 기존 카테고리에 Designator 추가도 가능.

### 다음 단계에서 쓸 수 있는 것들

**인게임 건물 배치 UI 구현 시:**
1. Window 서브클래스 (layer=GameUI 또는 Dialog with absorbInputAroundWindow=false)로 레이아웃 선택 UI
2. GenConstruct.CanPlaceBlueprintAt()로 각 건물 배치 가능 여부 검증
3. GhostDrawer.DrawGhostThing_NewTmp()로 일괄 미리보기 표시
4. GenConstruct.PlaceBlueprintForBuild()로 일괄 청사진 배치
5. CompGetGizmosExtra() 또는 Harmony 패치로 건물 선택 시 "AI 배치" 버튼 추가

**참조할 모드 소스코드:**
- Fluffy Blueprints (fluffy-mods/Blueprints) — BuildableInfo → GenConstruct 패턴
- HaploX1/RimWorld-MapGenBaseBlueprints — GenStep에서 건물 일괄 배치
- kirimin/rimworld_mod_chatlog_overlay — 인게임 채팅 오버레이 Window
- Designator Shapes (merthsoft/designator-shapes) — 커스텀 Designator 확장

---

## 2026-03-15 (6차) — 건물/가구 시스템 웹 조사

### 조사 동기
AI 건물 배치 기능 구현을 위해 RimWorld의 건물/가구 시스템을 체계적으로 파악해야 함. defName 카탈로그, 크기/배치 규칙, Stuff 시스템, 방 시스템, 배치 제약, 건축 API 6개 항목을 웹 조사.

### 조사 방법
RimWorld Wiki (rimworldwiki.com), GitHub 디컴파일 소스 (Chillu1, josh-m, RimWorld-zh), Ludeon 공식 번역 저장소 (Finnish/Spanish/Danish), Steam 커뮤니티 가이드에서 정보 수집. WebSearch 15+회, WebFetch 20+회 수행.

### 주요 발견사항

**1. Stuff 시스템의 건축 영향이 생각보다 큼**
Gold 벽은 Beauty x4이지만 HP x0.6. Granite 벽은 HP x1.7이고 Beauty x1. Plasteel은 HP x2.8이지만 작업량 x2.2. 재질 선택이 건물의 거의 모든 스탯에 영향을 미침. LLM이 건물 배치 시 재질까지 고려해야 할 수 있음.

**2. Impressiveness 공식이 최소값 치중**
4개 방 스탯 중 가장 약한 것이 51.25% 가중치. 나머지 3개가 각 16.25%. 이건 방 설계 AI가 "가장 약한 스탯을 먼저 개선"하는 전략을 취해야 함을 의미. 단순히 조각품 하나 넣어서 Beauty만 올리는 건 비효율.

**3. 배치 제약 조건이 복잡**
- Cooler는 벽 관통 설치 (PlaceWorker_Cooler)
- Solar은 지붕 불가 (PlaceWorker_NotUnderRoof)
- Geothermal은 간헐천 위만 (PlaceWorker_OnlyOnThing)
- 풍력은 7x18 배제 영역
- 당구대는 주변 1칸 빈 공간 필수
- 체스/포커는 인접 의자 필수
→ AI가 건물을 배치할 때 이런 제약을 전부 알아야 함. CanPlaceBlueprintAt이 검증해주긴 하지만, 처음부터 제약을 고려한 설계가 더 효율적.

**4. ThingDef XML 구조 파악 완료**
BuildingBase 추상 클래스, 주요 태그(size, rotatable, passability, costStuffCount, stuffCategories, costList, terrainAffordanceNeeded, designationCategory, placeWorkers 등) 전부 정리. 커스텀 건물 Def 작성 가능.

**5. GitHub 429 Rate Limit**
WebFetch가 GitHub 페이지에서 429 (Too Many Requests) 에러를 자주 반환. 디컴파일 소스 전문을 가져오려면 로컬에서 직접 확인하는 게 더 나음. Chillu1/RimWorldDecompiled 클론 추천.

### 결과물
`RESEARCH_BUILDINGS.md` — 6개 섹션, 25+ 테이블, 100+ defName, API 코드 예시 포함.

---

## 2026-03-17 — feature/reset-undo 개발 과정

### 멀티턴 버그 발견 경위

유저가 "4턴만 되도 오류가 난다"고 함. 처음엔 단순히 히스토리가 길어서 토큰 초과라고 생각했는데, 유저가 다른 증상을 설명:
"왼쪽에 산 → 왼쪽에 강 이런식으로 채팅하면 망가져버리는거고?"

이게 단순 토큰 문제가 아님. BuildSystemPrompt를 분석해보니 `Find.WorldGrid[tileId]`로 원본 타일만 읽고 있음. `MapGenParams`에 이미 수정된 파라미터가 있는데 LLM에게는 원본 타일 기준 정보만 전달됨.

→ LLM이 매 턴마다 "타일 원본 상태 + 유저 요청" → 파라미터 생성. 이전 파라미터는 완전히 무시됨.

### 해결 방향 결정 과정

유저가 제안: "파라미터를 LLM에 전달할 필요 없잖아. undo를 위해 메모리에 저장만 해놓고. stack 형식으로."

이 제안이 정확히 맞음. 두 가지 해결이 하나로 묶임:
1. 파라미터 누적 버그 → 현재 파라미터를 system prompt에 포함
2. Undo → Stack에 스냅샷 push

히스토리를 1개 user 메시지만 보내는 방식도 맞는 방향. 어차피 현재 파라미터가 system prompt에 있으면 이전 대화 맥락은 필요 없음. LLM은 현재 상태를 알고 있음.

### ToSnapshot() 설계

`MapParamsData` 클래스가 이미 있었음. Apply()에서 이미 쓰이는 클래스.
ToSnapshot()은 현재 static MapGenParams 상태를 MapParamsData로 복사하는 것.

복잡한 점: `ElevationShapes` 리스트를 deep copy해야 함. 리스트만 새로 만들어도 안에 있는 ElevationShape 객체가 참조 공유되면 안 됨 → LINQ Select로 새 ElevationShape 객체 생성.

### BuildCurrentParamsText() 설계

기본값과 다른 항목만 출력하는 게 좋음. 기본값 그대로인 파라미터까지 전부 나열하면 LLM이 혼란스러울 수 있음.

주요 출력 항목:
- hills (방향), hill_amount (1.0 아닌 경우), vegetation_density, animal_density
- river (present=true인 경우 전체 출력)
- caves, rock_types, mutators, elevation_shapes

### _initialSnapshot 설계 결정

다이얼로그가 열릴 때 `MapGenParams.HasParams`가 true면 스냅샷. false면 null.
Reset 시 null이면 `MapGenParams.Reset()` 호출, null 아니면 스냅샷 복원.

이유: 유저가 프리셋 로드한 상태에서 채팅하다 Reset 누르면 프리셋 상태로 돌아가야 함.
채팅 시작 전 아무 파라미터 없었으면 Reset 후 파라미터 없는 상태로 돌아가야 함.

### DoUndo() 채팅 히스토리 제거

SendMessage 한 번 = user 메시지 + assistant 메시지 2개 추가.
Undo 시 이 2개를 제거해야 함.

처음 구현에서 `_history.Count >= 2` 체크하고 마지막 2개 제거.
단 Welcome 메시지는 항상 있으므로 실제로는 Count >= 3이어야 user+assistant 2개가 있음.
if/else 분기로 처리.

### catch 수정 이유

기존 catch가 `response` 전체를 히스토리에 저장하고 있었음.
변수명이 `response`인데, HandleResponse 이전에 예외가 나면 `response`에 파라미터 JSON이 담겨있음.
이게 채팅창에 그대로 출력되는 버그. 에러 메시지 문자열만 저장으로 변경.

### 빌드 관련

`cmd.exe /c "msbuild ..."` 방식이 계속 출력 없이 종료됨. Git Bash + cmd.exe 혼합 환경에서 `cmd /c` 로 실행한 명령의 stdout이 Git Bash 터미널에 제대로 연결 안 됨.

결국 PowerShell에서 dotnet.exe 전체 경로로 빌드:
```
powershell.exe -Command "& 'C:\Program Files\dotnet\dotnet.exe' build ... 2>&1"
```
→ 빌드 성공 (1 Warning, 0 Error)

---

## 2026-03-15 (7차) — BlueprintAI 독립 콘솔 테스트앱

### 목적

Engine 코드 (WallMapParser, FurniturePlacer, BlueprintEngine, TestDefResolver)를 RimWorld 없이 단독 검증하는 콘솔 테스트앱.
인게임 테스트는 림월드 로딩 시간이 길어서 빠른 반복 개발에 부적합. 콘솔 테스트로 빌드+실행 3초 이내.

### 프로젝트 구조 결정

Engine 소스를 DLL 참조가 아닌 `<Compile Include>` 링크로 직접 포함하는 방식 선택.
이유: blueprint_ai 메인 프로젝트가 RimWorld DLL을 참조하는 net472 프로젝트인데, Engine 소스만 별도 DLL로 빌드하면 RimWorld 의존성을 분리해야 함. 이미 Engine 코드는 RimWorld 의존성 없이 설계되어 있으므로 소스 직접 포함이 가장 단순.

```xml
<Compile Include="..\Engine\BlueprintData.cs" Link="Engine\BlueprintData.cs" />
```

### 테스트 기대값 오류 발견

첫 실행 결과: 34/37 PASS, 3 FAIL.

3개 FAIL 모두 Engine 버그가 아닌 **테스트 기대값 오류**:

**Test01 (Square 5x5) Wall count:**
```
WWWWW   -> Row 0: 5 walls
W...W   -> Row 1: 2 walls
W...W   -> Row 2: 2 walls
W...W   -> Row 3: 2 walls
WWDWW   -> Row 4: 4 walls + 1 door
Total = 15 walls (not 16)
```
D는 Door이지 Wall이 아니므로 벽 15개가 맞음. 기대값 16이 틀림.

**Test03 (Octagon) Floor count:**
```
 WWWWW    -> 0 floors
WW...WW   -> 3 floors
W.....W   -> 5 floors
W.....W   -> 5 floors
W.....W   -> 5 floors
WW...WW   -> 3 floors
 WWDWW    -> 0 floors
Total = 21 floors (not 19)
```
직접 세어보면 21이 맞음. 기대값 19가 틀림.

**Test13 (Bedroom 7x7) Wall count:**
```
WWWWWWW   -> 7 walls
W.....W   -> 2 walls (x5 rows)
WWWDWWW   -> 6 walls + 1 door
Total = 7 + 10 + 6 = 23 walls (not 24)
```
D는 Door이므로 벽 23개가 맞음. 기대값 24가 틀림.

모든 오류가 "Door(D)를 Wall 카운트에 포함" 또는 "Octagon 바닥 수 계산 실수" 패턴. Engine 로직은 완벽히 정상.

### 시각화 출력 확인

통합 테스트(Test13, 14)에서 PrintLayout이 가구를 첫 글자로 표시하는 시각화 출력 정상 동작:
- D = DoubleBed (2x2), E = EndTable (1x1), S = StandingLamp (1x1), A = Armchair (1x1)

```
WWWWWWW
WEDDDDW    <- E=EndTable, D=DoubleBed(2x2), D=Dresser(2x1)
W.DD..W    <- DoubleBed 하단 행
W.....W
W.....W
W....SW    <- S=StandingLamp
WWWDWWW
```

### dotnet 경로 문제 (Windows + Git Bash)

bash 셸에서 `dotnet` 명령어를 직접 찾지 못함. 전체 경로 필요:
```bash
"/c/Program Files/dotnet/dotnet.exe" run
```
이건 mapgen_ai 프로젝트 때부터 계속 있던 문제. PATH에 등록 안 되어 있음.

### 수정 후 결과: 37/37 PASS
