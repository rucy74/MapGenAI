# TO_NEXT_SESSION.md

다음 세션에서 이 파일을 읽고 작업을 이어간다. 이 파일에 필요한 모든 맥락이 있다.

---

## 0. 이전 세션의 실수 — 반드시 반복하지 말 것

### 실수 1: 에이전트 병렬 디스패치 후 검증 없이 "완료" 선언
- 에이전트 4개를 동시에 띄워서 코드 + 프롬프트를 한 번에 변경
- 빌드 성공만 확인하고 "완료" → 실제로는 전부 동작 안 함
- **규칙**: 변경 1개 → 빌드 → Player.log 확인 → 동작 확인 → 다음 변경. 한 번에 여러 개 하지 않기.

### 실수 2: DLL 배포 경로 오류
- 빌드 출력을 워크샵 폴더에 복사했는데, 게임은 로컬 Mods 폴더에서 로드
- 세션 대부분의 시간을 "안 되는데?" → "왜 안 되지?" 로 낭비
- **규칙**: 빌드 후 반드시 `G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/`에 복사. 워크샵 폴더 사용 금지.
- **규칙**: 빌드 후 `ls -la`로 DLL 타임스탬프 확인. Player.log에서 새 코드의 로그 메시지 확인.

### 실수 3: 유저 요청과 다른 방향으로 작업
- 유저 요청: MDP 수정 + 바닐라 타일 분석
- 실제로 한 것: 간헐천 패치, 광석 수정, density 수정, ridge 구현, 프롬프트 전면 교체 (전부 요청 안 한 것)
- 요청한 MDP는 안 고침
- **규칙**: 유저가 요청한 것 먼저. "다 진행해"라도 우선순위 확인. 요청 안 한 것에 시간 쓰지 않기.

### 실수 4: 겉핥기 수정 반복
- 증상 하나 → 땜질 → 새 버그 → 땜질 → 새 버그 반복
- hilliness 추가했다 제거, shapes 누적했다 복원, 히스토리 전체 보냈다 제한했다
- **규칙**: 수정 전에 근본 원인을 파악하고, 하나의 일관된 설계로 해결. 이것저것 시도하면서 코드 더럽히지 않기.

### 실수 5: 유저 말을 여러 번 반복하게 만듦
- "MDP인데 왜 clear를 하냐" → 이해 못함 → "산 타일 = 생성된 타일이어야 한다" → 이해 못함 → "이전 맵 정보가 남아있으면 안 된다" → 3번째에야 이해
- **규칙**: 유저가 한 말을 바로 이해 못하겠으면, 내가 이해한 것을 한 문장으로 다시 말해서 확인. 이해한 척 하고 엉뚱한 방향으로 가지 않기.

---

## 1. 근본 문제: MapGenAI는 MDP가 아니다

### MDP란 (이 프로젝트에서)
- "왼쪽에 산" 적용 후 → 맵은 **왼쪽에 산이 있는 맵**
- 이 맵은 자연 산 타일과 동일해야 함 — **별도 정보 없이도** 왼쪽에 산이 있어야 함
- shapes 목록, params 등 "이전 맵 정보"가 어딘가에 남아있어야 맵이 유지되는 설계는 **MDP가 아니다**
- undo용 스냅샷 외에는 이전 맵 정보가 어디에도 없어야 한다

### 현재 잘못된 구조
```
LLM 응답 → MapGenParams 정적 필드에 저장 → GenStep Postfix가 읽어서 적용
                     ↑
            이 정적 필드가 "이전 맵 정보"
            Apply() 때마다 리셋/교체됨
            → LLM이 안 보낸 것은 사라짐 (강 방향, 산 등)
```

### 증상
- "왼쪽에 산" → "오른쪽에 산": LLM이 ridge(left)를 안 보내면 왼쪽 산 사라짐
- "강을 좌우로" → "온천 추가": LLM이 강 파라미터를 안 보내면 강 방향 리셋
- "강을 좌우로" → "강을 위쪽에": LLM이 direction을 안 보내면 direction 리셋 → 대각선

### 자연 산 타일에서는 왜 되는가
- 자연 산 타일: `tile.hilliness = Mountainous` → 바닐라 GenStep이 산 생성
- 별도 shapes 목록 없음. 타일 속성 자체가 "산이 있다"는 상태
- MapGenAI가 ridge를 추가/제거해도 base 산은 hilliness에서 오므로 사라지지 않음

### 동굴/온천에서는 왜 되는가
- `tile.AddMutator("Caves")` → 타일에 영구 반영
- 대화 닫고 다시 열어도, Apply() 리셋해도, 동굴은 타일에 남아있음

---

## 2. 해결 방향: WorldComponent에 타일별 상태 영구 저장

### 왜 타일 속성만으로 부족한가
- `tile.hilliness`: 산 유무는 되지만 **방향**(왼쪽/오른쪽)은 인코딩 불가. Mountainous로 바꾸면 맵 전체가 바위로 뒤덮임.
- `tile.Mutators`: 바닐라 정의된 것만 사용 가능. 커스텀 "MountainLeft" mutator 같은 건 없음.
- 강 방향/위치, 석재 종류, 광석 밀도 등은 타일 속성에 넣을 곳이 없음.

### WorldComponent 방식
```csharp
public class MapGenAIWorldComponent : WorldComponent
{
    // 타일별 MapGenAI 설정 영구 저장
    private Dictionary<int, TileMapState> tileStates = new();

    public override void ExposeData()
    {
        // 세이브/로드
        Scribe_Collections.Look(ref tileStates, "tileStates", LookMode.Value, LookMode.Deep);
    }

    public TileMapState GetState(int tileId) => tileStates.TryGetValue(tileId, out var s) ? s : null;
    public void SetState(int tileId, TileMapState state) => tileStates[tileId] = state;
    public void RemoveState(int tileId) => tileStates.Remove(tileId);
}
```

### 새로운 흐름
```
1. Apply() 호출
2. LLM이 보낸 파라미터만 현재 타일의 WorldComponent 상태에 병합
   - LLM이 보낸 것: 업데이트
   - LLM이 안 보낸 것: 기존 값 유지 (WorldComponent에 저장된 값)
3. GenStep Postfix: WorldComponent에서 현재 타일 상태 읽어서 적용
4. 대화 닫기: 아무것도 리셋 안 함 (WorldComponent에 영구 저장)
5. 대화 다시 열기: WorldComponent에서 상태 읽어서 system prompt에 반영
6. Undo: WorldComponent의 이전 스냅샷 복원
7. Reset: WorldComponent에서 해당 타일 상태 삭제
```

### MapGenParams 정적 필드 → 제거 또는 최소화
- 현재: `MapGenParams.ElevationShapes`, `MapGenParams.HasRiver` 등 정적 필드가 상태 저장
- 변경: WorldComponent가 상태 저장. MapGenParams는 Apply() 시 임시로 사용하거나 제거.
- GenStep Postfix가 WorldComponent에서 직접 읽음.

---

## 3. "LLM이 보낸 것만 변경" 구현

### 문제
현재 ParseParams()는 LLM JSON의 모든 필드를 읽어서 MapParamsData에 넣음.
LLM이 안 보낸 필드는 기본값(hill_amount=1.0 등)이 됨.
Apply()는 이 기본값으로 기존 상태를 덮어씀.

### 해결
ParseParams()에서 **JSON에 해당 키가 존재하는지** 추적:
```csharp
public class MapParamsData
{
    // 기존 필드들...

    // 어떤 키가 JSON에 명시적으로 있었는지 추적
    public HashSet<string> explicitKeys = new HashSet<string>();
}
```

ParseParams()에서:
```csharp
if (obj.GetString("hill_amount") != null)
{
    data.hill_amount = obj.GetFloat("hill_amount", 1f);
    data.explicitKeys.Add("hill_amount");
}
```

Apply()에서:
```csharp
if (data.explicitKeys.Contains("hill_amount"))
    state.HillAmount = Mathf.Clamp(data.hill_amount, 0.1f, 1.6f);
// 안 보냈으면 기존 state.HillAmount 유지
```

---

## 4. 대화 히스토리 정책

### 원칙
- **generate 후**: LLM 컨텍스트 초기화. 맵 상태는 system prompt(BuildCurrentParamsText)에 있으므로 히스토리 불필요.
- **ask 후**: LLM 컨텍스트에 ask 메시지 보존. 유저가 "ㅇㅇ" 같은 짧은 응답을 할 수 있으므로.
- **UI 히스토리** (_history): 항상 전부 유지 (채팅 표시용).

### 현재 구현 상태
```csharp
private readonly List<ChatMessage> _llmContext = new List<ChatMessage>(); // LLM 전송용
// generate 후: _llmContext.Clear()
// ask 후: _llmContext.Add(ask 메시지)
// SendMessage: _llmContext에 user 메시지 추가 → _llmContext 전송
```
이 부분은 이미 구현됨. WorldComponent 전환 시에도 유지.

---

## 5. 이번 세션(2026-03-22)에서 한 것 + 현재 코드 상태

### 유효한 변경 (코드에 반영됨, 로컬 Mods에 복사됨)
1. **ridge shape 구현** — slope 상쇄 문제 해결. `GenStepPatches.cs`에 ApplyRidge 추가. slope/split → ridge 레거시 변환.
2. **간헐천 패치** — `GeyserPatch.cs` 신규. GeyserCount 동작.
3. **광석 복원** — `OreDensityPatch.cs` 재작성. 모든 광석 GenStep 처리 + Postfix 복원.
4. **식생/동물 밀도** — `BiomeDensityPatch.cs` 신규. BiomeDef 수정 방식. GenStepPatches의 이중 감소 패치 제거.
5. **프롬프트** — slope/split → ridge 교체. geysers 파라미터 허용.
6. **bump 크기 축소** — radiusScale 0.5 → 0.3. "가운데에 산" 맵 69% → 30%.
7. **LLM 히스토리** — generate 후 초기화, ask 후 유지. `_llmContext` 분리.

### 남아있는 버그 (WorldComponent 전환으로 해결될 것)
- "왼쪽에 산 → 오른쪽에 산": LLM이 left를 안 보내면 사라짐
- "강을 좌우로 → 강을 위쪽에": direction 리셋
- "온천 추가 시 강 변경": LLM이 불필요한 파라미터 변경 (히스토리 정책으로 완화됨)

### 남아있는 버그 (별도 수정 필요)
- "산 많이 만들어줘": hill_amount=1.4로 맵 90% 산. LLM 프롬프트에서 hill_amount 상한 조정 필요.
- "대각선 산": ridge(direction=315)가 왼쪽 아래 꼭지점에만 바위. 이전 split 대각선과 다른 결과. ridge가 대각선 산맥을 표현 못 함 — 별도 shape이 필요하거나 ridge 로직 수정.
- 강 방향 비일관성: LLM이 direction="left"(270°)를 보내는데 좌우 강이 아니라 대각선이 됨. 이건 RimWorld의 강 생성이 바닐라 Perlin 지형에 의존하기 때문일 수 있음 — 코드 문제가 아니라 바닐라 한계일 가능성.

---

## 6. 파일 구조 참고

```
dev/Source/
├── Core/           — TextToMapMod.cs, TextToMapSettings.cs, ApiConfig.cs, LLMProviders.cs
├── LLM/            — ILLMClient.cs, OpenAIClient.cs, GeminiClient.cs, LocalClient.cs
├── MapGen/         — MapGenParams.cs (★ 리팩토링 대상)
├── Patches/        — GenStepPatches.cs, RiverPatches.cs, CoastPatches.cs, MountainSettingsPatch.cs,
│                     GeyserPatch.cs, OreDensityPatch.cs, BiomeDensityPatch.cs,
│                     RockTypesPatch.cs, RockChunkPatch.cs, RuinDangerDensityPatch.cs,
│                     TerrainFromPatch.cs, MapPreviewIntegration.cs, WorldInspectPane_Patch.cs
├── UI/             — Dialog_TextToMap.cs (★ 리팩토링 대상), SimpleJson.cs, L10n.cs
└── Tests/          — TestBench.cs, MdpApplyTests.cs, UnityShim.cs, VerseShim.cs

빌드: cd dev/Source && dotnet build
배포: cp dev/Assemblies/MapGenAI.dll "G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/"
워크샵 폴더 사용 금지 (구독 해제됨)
```

---

## 7. 작업 순서 (제안)

1. `MapGenAIWorldComponent` 생성 + `TileMapState` 데이터 클래스
2. `ParseParams()`에 `explicitKeys` 추적 추가
3. `Apply()` 리팩토링: WorldComponent에 상태 저장 + explicitKeys로 부분 업데이트
4. GenStep Postfix들: MapGenParams 대신 WorldComponent에서 읽기
5. `BuildCurrentParamsText()`: WorldComponent에서 읽기
6. `Reset()`: WorldComponent에서 해당 타일 상태 삭제
7. Undo: WorldComponent 스냅샷 기반으로 전환
8. MapGenParams 정적 필드 정리 (가능한 한 제거)
9. 테스트: "왼쪽에 산 → 오른쪽에 산", "강 방향 유지", "대화 닫고 다시 열기"
10. 빌드 + 로컬 Mods 복사 + Player.log 확인
