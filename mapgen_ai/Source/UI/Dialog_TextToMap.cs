using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    public class Dialog_TextToMap : Window
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private string _inputText = "";
        private string _statusText = "";
        private bool _isWaiting = false;
        private Vector2 _scrollPos = Vector2.zero;
        private bool _paramsReady = false;
        private int _lastMessageCount = 0; // 새 메시지 추가 시만 auto-scroll

        // 프리셋 관련 UI 상태
        private bool _showPresetNameInput = false;
        private string _presetNameInput = "";

        // 이 대화에서 열린 타일 ID (닫힐 때 파라미터 리셋 판단용)
        private readonly int _openedTileId;

        // LongEventHandler 대신 volatile 필드로 백그라운드→메인 스레드 전달
        private volatile bool _responseReady = false;
        private string _pendingResponse = null;
        private string _pendingError = null;

        private const float InputHeight = 36f;
        private const float SendButtonWidth = 80f;

        public override Vector2 InitialSize => new Vector2(600f, 500f);

        // 바닐라 기본 mutator defName (자동 관리되므로 LLM 목록에서 제외)
        private static readonly HashSet<string> VanillaAutoMutators = new HashSet<string>
            { "Mountain", "Caves", "Coast", "River" };

        /// <summary>
        /// DefDatabase에서 TileMutatorDef를 읽어 LLM용 목록 생성.
        /// Layer 1: 현재 타일 조건에 맞는 mutator만 포함 (사전 필터링).
        /// - Odyssey 비활성 → 바닐라 4개 외 전부 제거
        /// - 강 없는 타일 → River 카테고리 제거
        /// - 해안 아닌 타일 → Coast 카테고리 제거
        /// - biomeWhitelist 불일치 → 제거
        /// - 바닐라 기본(Mountain/Caves/Coast/River) → 자동 관리이므로 제거
        /// </summary>
        private static string BuildMutatorList(int tileId)
        {
            try
            {
                var allMutators = DefDatabase<TileMutatorDef>.AllDefsListForReading;
                var categories = new Dictionary<string, List<string>>();

                // 타일 정보 수집
                bool hasRiver = false;
                bool isCoastal = false;
                string currentBiomeDef = "";
                bool odysseyActive = ModsConfig.OdysseyActive;

                if (tileId >= 0)
                {
                    try
                    {
                        var tile = Find.WorldGrid[tileId];
                        if (tile != null)
                        {
                            hasRiver = tile.Rivers != null && tile.Rivers.Count > 0;
                            currentBiomeDef = tile.PrimaryBiome?.defName ?? "";

                            // 해안 감지: 이웃 타일 중 Ocean/Lake 확인 (BuildSystemPrompt와 동일 로직)
                            var neighbors = new List<RimWorld.Planet.PlanetTile>();
                            Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
                            foreach (var nTile in neighbors)
                            {
                                var nb = Find.WorldGrid[nTile];
                                if (nb?.PrimaryBiome?.defName == "Ocean" || nb?.PrimaryBiome?.defName == "Lake")
                                { isCoastal = true; break; }
                            }
                        }
                    }
                    catch { /* 타일 정보 읽기 실패 시 필터링 없이 진행 */ }
                }

                foreach (var mut in allMutators)
                {
                    if (mut.label == "none" || string.IsNullOrEmpty(mut.label)) continue;
                    if (string.IsNullOrEmpty(mut.defName)) continue;

                    // 필터 1: 바닐라 기본 mutator는 자동 관리이므로 목록에서 제외
                    if (VanillaAutoMutators.Contains(mut.defName)) continue;

                    // 필터 2: Odyssey 비활성 → 바닐라 기본 외 전부 제거 (이미 위에서 바닐라 제외했으므로 전부 스킵)
                    if (!odysseyActive) continue;

                    bool hasCats = mut.categories != null && mut.categories.Count > 0;

                    // 필터 3: 강 없는 타일 → River 카테고리 mutator 제거
                    if (!hasRiver && hasCats && mut.categories.Contains("River")) continue;

                    // 필터 4: 해안 아닌 타일 → Coast 카테고리 mutator 제거
                    if (!isCoastal && hasCats && mut.categories.Contains("Coast")) continue;

                    // 필터 5: biomeWhitelist가 있으면 현재 바이옴이 목록에 있어야 함
                    if (mut.biomeWhitelist != null && mut.biomeWhitelist.Count > 0
                        && !string.IsNullOrEmpty(currentBiomeDef))
                    {
                        if (!mut.biomeWhitelist.Any(b => b?.defName == currentBiomeDef))
                            continue;
                    }

                    string catKey = hasCats ? mut.categories[0] : "Other";
                    if (!categories.ContainsKey(catKey))
                        categories[catKey] = new List<string>();

                    // defName=내부용, label=유저 표시용, description=설명
                    string desc = !string.IsNullOrEmpty(mut.description) ? $" - {mut.description}" : "";
                    categories[catKey].Add($"{mut.defName}(표시명:{mut.label}{desc})");
                }

                if (categories.Count == 0)
                    return "  (이 타일에서 사용 가능한 특수 지형 변형 없음)";

                var sb = new System.Text.StringBuilder();
                foreach (var kv in categories)
                {
                    sb.AppendLine($"  [{kv.Key}] {string.Join(", ", kv.Value)}");
                }
                return sb.ToString();
            }
            catch
            {
                return "  (mutator 목록 로드 실패)";
            }
        }

        /// <summary>
        /// DefDatabase에서 자연 석재 목록을 동적으로 생성.
        /// 바닐라 + 모드 석재를 모두 포함하여 LLM에게 유효한 defName 목록 제공.
        /// </summary>
        private static string BuildRockTypeList()
        {
            try
            {
                var rocks = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.building != null && d.building.isNaturalRock && !d.building.isResourceRock)
                    .Select(d => $"{d.defName}({d.label})")
                    .ToList();

                if (rocks.Count == 0)
                    return "Granite, Limestone, Marble, Sandstone, Slate";

                return string.Join(", ", rocks);
            }
            catch
            {
                return "Granite, Limestone, Marble, Sandstone, Slate";
            }
        }

        private static string BuildSystemPrompt(int tileId)
        {
            // --- 타일 정보 수집 ---
            string biome = "알 수 없음";
            string biomeDef = "";
            string hillsStr = "";
            bool hasRiver = false;
            bool isCoastal = false;
            float elev = 0f;
            string riverInfo = "없음";

            try
            {
                if (tileId >= 0)
                {
                    var tile = Find.WorldGrid[tileId];
                    if (tile != null)
                    {
                        biome = tile.PrimaryBiome?.label ?? "알 수 없음";
                        biomeDef = tile.PrimaryBiome?.defName ?? "";
                        hillsStr = tile.hilliness.ToString();
                        hasRiver = tile.Rivers != null && tile.Rivers.Count > 0;
                        elev = tile.elevation;

                        if (hasRiver)
                        {
                            riverInfo = "있음";
                            foreach (var rl in tile.Rivers)
                            {
                                var nb = Find.WorldGrid[rl.neighbor];
                                if (nb?.PrimaryBiome?.defName == "Ocean" || nb?.PrimaryBiome?.defName == "Lake")
                                    riverInfo += " (바다/호수로 연결됨)";
                            }
                        }

                        // 해안 감지 (BuildMutatorList와 동일 로직)
                        var neighbors = new List<RimWorld.Planet.PlanetTile>();
                        Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
                        foreach (var nTile in neighbors)
                        {
                            var nb = Find.WorldGrid[nTile];
                            if (nb?.PrimaryBiome?.defName == "Ocean" || nb?.PrimaryBiome?.defName == "Lake")
                            { isCoastal = true; break; }
                        }
                    }
                }
            }
            catch { /* 타일 정보 읽기 실패 시 기본값 사용 */ }

            // --- Layer 1: 필터링된 mutator 목록 ---
            string mutatorList = BuildMutatorList(tileId);

            // --- 석재 목록 (동적) ---
            string rockList = BuildRockTypeList();

            // --- Layer 2: 구조화된 프롬프트 ---
            // 섹션 1: 역할 (짧게)
            string role = "당신은 RimWorld 맵 생성 도우미입니다. 유저 요청을 JSON으로 변환합니다.";

            // 섹션 2: JSON 스키마
            string schema = @"반드시 아래 두 형식 중 하나의 JSON만 출력하세요.

질문/안내: {""action"":""ask"",""message"":""내용""}
맵 생성: {""action"":""generate"",""description"":""맵 설명"",""params"":{...}}

params 스키마:
{""hills"":""left|right|center|edges|top|bottom|none"",""hill_amount"":0.5~1.6,""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""caves"":true|false,""geysers"":0~10,""coast_direction"":""auto|north|east|south|west"",""rock_count"":1~15,""rock_types"":[""Granite|Limestone|Marble|Sandstone|Slate""],""ore_density"":0.0~2.5,""ruin_density"":0.0~2.5,""danger_density"":0.0~2.5,""mutators"":[""defName""],""remove_mutators"":[""defName""],""elevation_shapes"":[{""type"":""slope|radial|split|bump|noise|ring"",""direction"":""left|right|top|bottom|top_left|top_right|bottom_left|bottom_right|0-360"",""strength"":""weak|medium|strong|negative_weak|negative_medium|negative_strong|숫자"",""position"":""center|top_left|top|top_right|left|right|bottom_left|bottom|bottom_right|[x,z]"",""size"":""small|medium|large|0-1"",""gap"":""tiny|small|medium|large"",""fill"":""water""}]}

elevation_shapes 가이드:
- slope: 한쪽이 높은 경사면. direction으로 높은 방향 지정. 예: direction=left → 왼쪽이 높음.
- radial: 가장자리가 높고 중심이 낮음. size로 산맥 두께 조절.
- split: 축 방향 분할. positive strength=협곡(양쪽 산+가운데 골짜기), negative strength=산맥(가운데 산+양쪽 평지). direction으로 축 방향, gap으로 폭.
- bump: 가우시안 돌출/함몰. position으로 위치, size로 크기. negative strength=함몰. fill=water로 호수 생성.
- noise: 펄린 노이즈로 불규칙 지형. size가 클수록 큰 덩어리.
- ring: 도넛 형태 산맥. position으로 중심, size로 링 반경, strength로 높이. 분화구/원형 요새 지형에 적합.
- 여러 shape를 조합 가능 (additive). 복잡한 지형에는 elevation_shapes를, 단순 요청에는 hills를 사용.
- hills와 elevation_shapes를 동시에 쓰지 마세요. elevation_shapes가 있으면 hills는 무시됩니다.
- 산맥=split+negative strength(가운데 높음). 협곡=split+positive strength(가운데 낮음). 대각선 산맥=split(direction=top_left, strength=negative_strong, gap=tiny).

추가 파라미터:
- rock_types: 원하는 석재 종류 지정. 바닐라 석재: Granite(화강암), Limestone(석회암), Marble(대리석), Sandstone(사암), Slate(점판암). 예: ""rock_types"":[""Marble"",""Granite""]
- ruin_density: 폐허 밀도 (0.0~2.5, 기본 1.0). 0=폐허 없음, 2.5=매우 많음.
- danger_density: 고대 위험 밀도 (0.0~2.5, 기본 1.0). 0=위험 없음, 2.5=매우 많음.";

            // 섹션 3: 타일 컨텍스트 + 유효 옵션 (동적)
            string tileContext = $@"
[타일 정보] 바이옴={biome}({biomeDef}), 지형={hillsStr}, 고도={elev:F0}m, 강={riverInfo}, 해안={(isCoastal ? "예" : "아니오")}

[사용 가능한 석재] rock_types에는 이 defName만 사용하세요.
  {rockList}

[사용 가능한 mutators] 이 목록에 있는 defName만 사용하세요.
{mutatorList}";

            // 섹션 4: 규칙 (5줄 이하, "하지 마세요" 대신 "이 중에서 골라주세요")
            string rules = @"규칙:
- 지형 형태 요청(링 형태, 대각선, 경사면, 호수 등)에는 반드시 elevation_shapes를 사용하세요. hills는 단순 요청에만.
  링/도넛=ring, 산맥=split+negative_strong+gap:medium(산 폭), 협곡=split+strong+gap:small(골짜기 폭), 호수=bump+fill:water, 경사면=slope
- mutators 배열에는 위 목록의 defName만 사용하세요. 목록에 없는 것은 추가할 수 없습니다.
- 유저에게는 영어 표시명을 한국어로 번역하여 설명하세요. defName을 그대로 보여주지 마세요.
- 유저가 한국어로 요청하면 영어 defName/label과 매칭하세요 (예: 오아시스→Oasis, 코코아 나무→WildTropicalPlants).
- 불가능한 요청에는 action=ask로 솔직하게 안내하세요.
- generate 시 description에 유저가 이해할 수 있는 한국어 맵 설명을 포함하세요.
- 동굴 추가=caves:true, 동굴 제거=caves:false (명시적으로 설정).
- 석재 요청(대리석으로만, 화강암 많이 등)은 rock_types로 처리. rock_count와 rock_types를 동시에 쓸 수 있음.
- 폐허 많이/적게 요청은 ruin_density, 고대 위험은 danger_density로 조절.";

            // 섹션 5: few-shot 예시 (한국어, 기본 + elevation_shapes + 거절)
            string fewShot;
            if (!isCoastal)
            {
                // 내륙 타일: elevation_shapes 예시 + 해안 거절 예시
                fewShot = @"
예시1) 유저: ""산 많고 온천 있는 맵 만들어줘""
응답: {""action"":""generate"",""description"":""산이 많고 온천이 있는 맵"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

예시2) 유저: ""중앙을 감싸는 언덕 형태로""
응답: {""action"":""generate"",""description"":""가장자리에 산이 둘러싼 분지 맵"",""params"":{""elevation_shapes"":[{""type"":""radial"",""strength"":""strong"",""size"":""medium""}]}}

예시3) 유저: ""가운데 호수 있는 맵""
응답: {""action"":""generate"",""description"":""중앙에 호수가 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""bump"",""position"":""center"",""size"":""large"",""strength"":""negative_strong"",""fill"":""water""}]}}

예시4) 유저: ""대리석으로만 된 맵, 폐허 많이""
응답: {""action"":""generate"",""description"":""대리석만 있고 폐허가 많은 맵"",""params"":{""rock_types"":[""Marble""],""ruin_density"":2.0}}

예시5) 유저: ""대각선 산맥 하나 있는 맵""
응답: {""action"":""generate"",""description"":""좌상-우하 방향으로 대각선 산맥이 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""split"",""direction"":""top_left"",""strength"":""negative_strong"",""gap"":""medium""}]}}

예시6) 유저: ""피요르드 있는 맵 만들어줘""
응답: {""action"":""ask"",""message"":""현재 타일은 해안가가 아닙니다. 피요르드를 원하시면 세계지도에서 해안가 타일을 선택해주세요.""}";
            }
            else
            {
                // 해안 타일: 기본 + elevation_shapes 예시
                fewShot = @"
예시1) 유저: ""산 많고 온천 있는 맵 만들어줘""
응답: {""action"":""generate"",""description"":""산이 많고 온천이 있는 맵"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

예시2) 유저: ""왼쪽에 산, 오른쪽 아래에 호수""
응답: {""action"":""generate"",""description"":""왼쪽에 산이 있고 오른쪽 아래에 호수가 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""slope"",""direction"":""left"",""strength"":""strong""},{""type"":""bump"",""position"":""bottom_right"",""size"":""medium"",""strength"":""negative_strong"",""fill"":""water""}]}}

예시3) 유저: ""그냥 추천해줘""
응답: {""action"":""generate"",""description"":""해안가 바이옴에 어울리는 자연 경관 맵"",""params"":{""hills"":""edges"",""hill_amount"":1.0,""vegetation_density"":1.3,""coast_direction"":""auto""}}";
            }

            return $@"{role}

{schema}
{tileContext}
{rules}
{fewShot}";
        }

        // Enter 키가 Window 시스템을 통해 월드맵으로 전달되는 것 방지
        public override void OnAcceptKeyPressed()
        {
            // 의도적으로 비움: Enter는 DoWindowContents에서 메시지 전송으로 처리
        }

        public Dialog_TextToMap()
        {
            doCloseButton = false;
            doCloseX = true;
            closeOnAccept = false;
            forcePause = false;  // Map Preview와 공존하기 위해 pause 안 함
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
            layer = WindowLayer.Super;

            _openedTileId = Find.WorldSelector.SelectedTile;

            _history.Add(new ChatMessage("assistant",
                "안녕하세요! 원하는 맵을 설명해주세요.\n예: '왼쪽에 언덕, 오른쪽에 강' 또는 '북유럽 느낌으로' 또는 '그냥 추천해줘'"));
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 백그라운드 스레드 응답 처리 (매 프레임 체크)
            if (_responseReady)
            {
                _responseReady = false;
                if (_pendingError != null)
                {
                    // API 오류 (토큰 소진, 네트워크 등) → 채팅에 오류 표시
                    _history.Add(new ChatMessage("assistant", $"[오류] {_pendingError}"));
                    _pendingError = null;
                    _isWaiting = false;
                    _statusText = "";
                }
                else
                {
                    var resp = _pendingResponse;
                    _pendingResponse = null;
                    HandleResponse(resp);
                }
            }

            var font = Text.Font;
            Text.Font = GameFont.Small;

            // 채팅 영역 (프리셋 이름 입력 중이면 추가 높이 확보)
            float bottomReserve = InputHeight + 50f + (_showPresetNameInput ? 34f : 0f);
            var chatRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - bottomReserve);
            DrawChat(chatRect);

            // 입력창 + 전송 버튼
            var inputAreaY = chatRect.yMax + 8f;
            var inputRect = new Rect(inRect.x, inputAreaY, inRect.width - SendButtonWidth - 8f, InputHeight);
            var sendRect = new Rect(inputRect.xMax + 8f, inputAreaY, SendButtonWidth, InputHeight);

            // Enter 키: 항상 소비 (다른 Window로 전달 방지 → Map Preview 보호)
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Log.Message($"[MapGenAI] Enter key consumed in Dialog, tile={Find.WorldSelector.SelectedTile}");
                Event.current.Use();
                if (!_isWaiting && !string.IsNullOrEmpty(_inputText))
                    SendMessage();
            }

            GUI.SetNextControlName("ChatInput");
            _inputText = Widgets.TextField(inputRect, _inputText);

            if (Widgets.ButtonText(sendRect, _isWaiting ? "..." : "전송") && !_isWaiting)
                SendMessage();

            // 하단: 맵 생성 버튼 + 프리셋 버튼 or 상태 텍스트
            var bottomY = sendRect.yMax + 6f;
            if (_paramsReady)
            {
                // 프리셋 이름 입력 모드
                if (_showPresetNameInput)
                {
                    var nameRow = new Rect(inRect.x, bottomY, inRect.width, 30f);
                    float saveBtnW = 60f;
                    float cancelBtnW = 50f;
                    float nameFieldW = nameRow.width - saveBtnW - cancelBtnW - 12f;
                    var nameFieldRect = new Rect(nameRow.x, nameRow.y, nameFieldW, nameRow.height);
                    var saveConfirmRect = new Rect(nameFieldRect.xMax + 4f, nameRow.y, saveBtnW, nameRow.height);
                    var cancelRect = new Rect(saveConfirmRect.xMax + 4f, nameRow.y, cancelBtnW, nameRow.height);

                    GUI.SetNextControlName("PresetNameInput");
                    _presetNameInput = Widgets.TextField(nameFieldRect, _presetNameInput);

                    if (Widgets.ButtonText(saveConfirmRect, "저장"))
                    {
                        if (!string.IsNullOrEmpty(_presetNameInput.Trim()))
                        {
                            SaveCurrentPreset(_presetNameInput.Trim());
                            _showPresetNameInput = false;
                            _presetNameInput = "";
                        }
                    }
                    if (Widgets.ButtonText(cancelRect, "취소"))
                    {
                        _showPresetNameInput = false;
                        _presetNameInput = "";
                    }
                    bottomY += 34f;
                }

                // 맵 생성 + 프리셋 저장 버튼 (가로 배치)
                float btnSpacing = 6f;
                float presetBtnW = 100f;
                float generateBtnW = inRect.width - presetBtnW * 2 - btnSpacing * 2;
                var generateRect = new Rect(inRect.x, bottomY, generateBtnW, 36f);
                var presetSaveRect = new Rect(generateRect.xMax + btnSpacing, bottomY, presetBtnW, 36f);
                var presetLoadRect = new Rect(presetSaveRect.xMax + btnSpacing, bottomY, presetBtnW, 36f);

                if (Widgets.ButtonText(generateRect, "이 설정으로 맵 생성"))
                    GenerateMap();

                if (Widgets.ButtonText(presetSaveRect, "프리셋 저장"))
                {
                    _showPresetNameInput = true;
                    _presetNameInput = "";
                }

                if (Widgets.ButtonText(presetLoadRect, "프리셋 불러오기"))
                    ShowPresetLoadMenu();
            }
            else
            {
                // 파라미터 미준비 상태에서도 프리셋 불러오기 가능
                var loadOnlyRect = new Rect(inRect.x, bottomY, 140f, 36f);
                if (Widgets.ButtonText(loadOnlyRect, "프리셋 불러오기"))
                    ShowPresetLoadMenu();

                if (_statusText != "")
                {
                    Widgets.Label(new Rect(loadOnlyRect.xMax + 8f, bottomY + 4f, inRect.width - loadOnlyRect.width - 8f, 28f), _statusText);
                }
            }

            Text.Font = font;
        }

        private void DrawChat(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));
            var innerRect = rect.ContractedBy(6f);

            // 실제 렌더링과 동일한 너비로 높이 계산
            float scrollWidth = innerRect.width - 16f;
            float msgRenderWidth = scrollWidth * 0.75f;
            float msgTextWidth = msgRenderWidth - 12f;

            float contentHeight = 0f;
            foreach (var msg in _history)
                contentHeight += Text.CalcHeight(msg.Content, msgTextWidth) + 12f + 6f;
            if (_isWaiting)
                contentHeight += 30f; // "AI 응답 대기 중..." 높이

            var viewRect = new Rect(0, 0, scrollWidth, Mathf.Max(contentHeight, innerRect.height));
            Widgets.BeginScrollView(innerRect, ref _scrollPos, viewRect);

            float y = 0f;
            foreach (var msg in _history)
            {
                bool isUser = msg.Role == "user";
                float msgHeight = Text.CalcHeight(msg.Content, msgTextWidth) + 12f;
                float x = isUser ? scrollWidth - msgRenderWidth : 0f;

                var bgColor = isUser
                    ? new Color(0.2f, 0.4f, 0.7f, 0.9f)
                    : new Color(0.25f, 0.25f, 0.25f, 0.9f);

                Widgets.DrawBoxSolid(new Rect(x, y, msgRenderWidth, msgHeight), bgColor);
                Widgets.Label(new Rect(x, y, msgRenderWidth, msgHeight).ContractedBy(6f), msg.Content);
                y += msgHeight + 6f;
            }

            // 대기 중이면 "AI 응답 대기 중..." 표시
            if (_isWaiting)
            {
                float dotCount = ((int)(Time.realtimeSinceStartup * 2f)) % 4;
                string dots = new string('.', (int)dotCount);
                string waitText = $"AI 응답 대기 중{dots}";
                float waitH = Text.CalcHeight(waitText, msgTextWidth) + 12f;
                var waitColor = new Color(0.3f, 0.3f, 0.3f, 0.7f);
                Widgets.DrawBoxSolid(new Rect(0f, y, msgRenderWidth, waitH), waitColor);

                var oldStyle = Text.Font;
                Text.Font = GameFont.Small;
                var oldColor = GUI.color;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Widgets.Label(new Rect(0f, y, msgRenderWidth, waitH).ContractedBy(6f), waitText);
                GUI.color = oldColor;
                Text.Font = oldStyle;

                y += waitH + 6f;
            }

            // 새 메시지 추가 시에만 auto-scroll (사용자가 위로 스크롤 가능)
            if (_history.Count != _lastMessageCount)
            {
                _lastMessageCount = _history.Count;
                _scrollPos.y = y;
            }

            Widgets.EndScrollView();
        }

        private void SendMessage()
        {
            var text = _inputText.Trim();
            if (text == "" || _isWaiting) return;

            _inputText = "";
            _history.Add(new ChatMessage("user", text));
            _isWaiting = true;
            _statusText = "LLM에게 요청 중...";
            _paramsReady = false;

            ILLMClient client;
            try
            {
                client = LLMClientFactory.Create(MapGenAIMod.Settings);
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 클라이언트 생성 실패: {e}");
                _statusText = $"오류: {e.Message}";
                _isWaiting = false;
                return;
            }

            Log.Message($"[MapGenAI] LLM 요청 시작 (provider={MapGenAIMod.Settings.activeProvider})");
            var historySnapshot = new List<ChatMessage>(_history);
            int tileId = Find.WorldSelector?.SelectedTile ?? -1;
            var systemPrompt = BuildSystemPrompt(tileId);

            Task.Run(async () =>
            {
                try
                {
                    Log.Message("[MapGenAI] Task.Run 시작");
                    var response = await client.SendChatAsync(historySnapshot, systemPrompt);
                    Log.Message($"[MapGenAI] 응답 수신: {(response == null ? "null" : response.Length + "자")}");
                    _pendingResponse = response;
                    _responseReady = true;
                }
                catch (Exception e)
                {
                    Log.Error($"[MapGenAI] API 오류: {e}");
                    _pendingError = e.Message;
                    _responseReady = true;
                }
            });
        }

        private void HandleResponse(string response)
        {
            _isWaiting = false;
            if (response == null)
            {
                _statusText = "응답 없음. API 키를 확인해주세요.";
                return;
            }

            Log.Message($"[MapGenAI] HandleResponse 원문: {response}");

            try
            {
                // JSON 추출: 첫 { ~ 마지막 } (코드블록/마크다운 무시)
                int firstBrace = response.IndexOf('{');
                int lastBrace = response.LastIndexOf('}');
                if (firstBrace < 0 || lastBrace <= firstBrace)
                {
                    _history.Add(new ChatMessage("assistant", response));
                    _statusText = "";
                    return;
                }
                var clean = response.Substring(firstBrace, lastBrace - firstBrace + 1);

                var parsed = SimpleJson.Parse(clean);
                var action = parsed.GetString("action");
                Log.Message($"[MapGenAI] 파싱된 action: {action}");

                if (action == "ask")
                {
                    _history.Add(new ChatMessage("assistant", parsed.GetString("message")));
                    _statusText = "";
                }
                else if (action == "generate")
                {
                    var data = ParseParams(parsed.GetObject("params"));

                    // --- Layer 3: 출력 검증 ---
                    var warnings = ValidateMutators(data);

                    MapGenParams.Apply(data);
                    _paramsReady = true;
                    var desc = parsed.GetString("description") ?? "맵 파라미터가 설정되었습니다.";

                    // 경고 메시지가 있으면 채팅에 추가
                    string warningText = "";
                    if (warnings.Count > 0)
                    {
                        warningText = "\n\n" + string.Join("\n", warnings);
                        Log.Message($"[MapGenAI] 검증 경고 {warnings.Count}건: {string.Join("; ", warnings)}");
                    }

                    _history.Add(new ChatMessage("assistant",
                        $"{desc}{warningText}\n\n아래 버튼으로 맵을 생성하거나, 채팅으로 수정할 수 있습니다."));
                    _statusText = "";
                }
            }
            catch
            {
                _history.Add(new ChatMessage("assistant", response));
                _statusText = "";
            }
        }

        /// <summary>
        /// Layer 3: LLM 응답의 mutator 유효성 검증.
        /// 잘못된 mutator는 data에서 제거하고, 경고 메시지 목록을 반환.
        /// </summary>
        private List<string> ValidateMutators(MapParamsData data)
        {
            var warnings = new List<string>();
            if (data.mutators == null || data.mutators.Count == 0)
                return warnings;

            int tileId = Find.WorldSelector?.SelectedTile ?? -1;

            // 타일 정보 수집
            bool hasRiver = false;
            bool isCoastal = false;
            if (tileId >= 0)
            {
                try
                {
                    var tile = Find.WorldGrid[tileId];
                    if (tile != null)
                    {
                        hasRiver = tile.Rivers != null && tile.Rivers.Count > 0;

                        var neighbors = new List<RimWorld.Planet.PlanetTile>();
                        Find.WorldGrid.GetTileNeighbors(tileId, neighbors);
                        foreach (var nTile in neighbors)
                        {
                            var nb = Find.WorldGrid[nTile];
                            if (nb?.PrimaryBiome?.defName == "Ocean" || nb?.PrimaryBiome?.defName == "Lake")
                            { isCoastal = true; break; }
                        }
                    }
                }
                catch { }
            }

            // 역순으로 순회하여 제거 (인덱스 안전)
            for (int i = data.mutators.Count - 1; i >= 0; i--)
            {
                var defName = data.mutators[i];

                // 검증 1: DefDatabase에 존재하는지
                var mutDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                if (mutDef == null)
                {
                    warnings.Add($"[!] '{defName}'은(는) 존재하지 않는 지형 변형이어서 제거되었습니다.");
                    data.mutators.RemoveAt(i);
                    continue;
                }

                bool hasCats = mutDef.categories != null && mutDef.categories.Count > 0;
                string label = mutDef.label ?? defName;

                // 검증 2: Coast 카테고리인데 해안 아닌 타일
                if (!isCoastal && hasCats && mutDef.categories.Contains("Coast"))
                {
                    warnings.Add($"[!] [{label}]은(는) 해안 타일에서만 가능하여 제거되었습니다.");
                    data.mutators.RemoveAt(i);
                    continue;
                }

                // 검증 3: River 카테고리인데 강 없는 타일
                if (!hasRiver && hasCats && mutDef.categories.Contains("River"))
                {
                    warnings.Add($"[!] [{label}]은(는) 강이 있는 타일에서만 가능하여 제거되었습니다.");
                    data.mutators.RemoveAt(i);
                    continue;
                }
            }

            return warnings;
        }

        private MapParamsData ParseParams(SimpleJsonObject obj)
        {
            var data = new MapParamsData
            {
                hills = obj.GetString("hills") ?? "none",
                hill_amount = obj.GetFloat("hill_amount", 1f),
                vegetation_density = obj.GetFloat("vegetation_density", 1f),
                animal_density = obj.GetFloat("animal_density", 1f),
                roads = obj.GetBool("roads"),
                caves = obj.GetBool("caves"),
                caves_explicit = obj.GetString("caves") != null,
                geysers = obj.GetInt("geysers", -1),
                coast_direction = obj.GetString("coast_direction") ?? "auto",
                rock_count = obj.GetInt("rock_count", -1),
                ore_density = obj.GetFloat("ore_density", 1f),
                ruin_density = obj.GetFloat("ruin_density", 1f),
                danger_density = obj.GetFloat("danger_density", 1f)
            };

            // rock_types 배열 파싱
            var rockTypesArr = obj.GetArray("rock_types");
            if (rockTypesArr != null)
            {
                data.rock_types = new System.Collections.Generic.List<string>();
                foreach (var item in rockTypesArr)
                    data.rock_types.Add(item);
            }

            var riverObj = obj.GetObject("river");
            if (riverObj != null)
                data.river = new RiverData
                {
                    present = riverObj.GetBool("present"),
                    direction = riverObj.GetString("direction") ?? "vertical",
                    x_position = riverObj.GetFloat("x_position", 0.5f)
                };

            // mutators 배열 파싱
            var mutatorsArr = obj.GetArray("mutators");
            if (mutatorsArr != null)
            {
                data.mutators = new System.Collections.Generic.List<string>();
                foreach (var item in mutatorsArr)
                    data.mutators.Add(item);
            }

            // remove_mutators 배열 파싱 (기존 특징 제거용)
            var removeArr = obj.GetArray("remove_mutators");
            if (removeArr != null)
            {
                data.remove_mutators = new System.Collections.Generic.List<string>();
                foreach (var item in removeArr)
                    data.remove_mutators.Add(item);
            }

            // elevation_shapes 오브젝트 배열 파싱
            var shapesArr = obj.GetObjectArray("elevation_shapes");
            if (shapesArr != null)
            {
                data.elevation_shapes = new List<ElevationShape>();
                foreach (var s in shapesArr)
                {
                    data.elevation_shapes.Add(new ElevationShape
                    {
                        type = s.GetString("type"),
                        direction = s.GetString("direction"),
                        strength = s.GetString("strength"),
                        position = s.GetString("position"),
                        size = s.GetString("size"),
                        gap = s.GetString("gap"),
                        fill = s.GetString("fill")
                    });
                }
            }

            return data;
        }

        private void SaveCurrentPreset(string presetName)
        {
            // 현재 MapGenParams 상태를 MapParamsData로 변환
            var data = new MapParamsData
            {
                hills = MapGenParams.Hills,
                hill_amount = MapGenParams.HillAmount,
                vegetation_density = MapGenParams.VegetationDensity,
                animal_density = MapGenParams.AnimalDensity,
                river = new RiverData
                {
                    present = MapGenParams.HasRiver,
                    direction = MapGenParams.RiverDirection,
                    x_position = MapGenParams.RiverXPosition
                },
                roads = MapGenParams.HasRoads,
                caves = MapGenParams.HasCaves,
                geysers = MapGenParams.GeyserCount,
                coast_direction = MapGenParams.CoastDirection,
                rock_count = MapGenParams.RockCount,
                ore_density = MapGenParams.OreDensity,
                ruin_density = MapGenParams.RuinDensity,
                danger_density = MapGenParams.DangerDensity,
                rock_types = MapGenParams.RockTypes.Count > 0
                    ? new List<string>(MapGenParams.RockTypes) : null,
                mutators = new List<string>(MapGenParams.Mutators),
                elevation_shapes = MapGenParams.ElevationShapes.Count > 0
                    ? new List<ElevationShape>(MapGenParams.ElevationShapes) : null
            };

            PresetManager.Save(presetName, data);
            _history.Add(new ChatMessage("assistant", $"프리셋 \"{presetName}\" 이(가) 저장되었습니다."));
        }

        private void ShowPresetLoadMenu()
        {
            var presets = PresetManager.ListPresets();
            if (presets.Count == 0)
            {
                _history.Add(new ChatMessage("assistant", "저장된 프리셋이 없습니다."));
                return;
            }

            var menuOptions = new List<FloatMenuOption>();
            foreach (var name in presets)
            {
                var presetName = name; // 클로저 캡처용 로컬 변수
                menuOptions.Add(new FloatMenuOption(presetName, () => LoadPreset(presetName)));
            }

            // 구분선 + 삭제 메뉴
            menuOptions.Add(new FloatMenuOption("--- 삭제 ---", null));
            foreach (var name in presets)
            {
                var presetName = name;
                menuOptions.Add(new FloatMenuOption($"[삭제] {presetName}", () =>
                {
                    PresetManager.Delete(presetName);
                    _history.Add(new ChatMessage("assistant", $"프리셋 \"{presetName}\" 이(가) 삭제되었습니다."));
                }));
            }

            Find.WindowStack.Add(new FloatMenu(menuOptions));
        }

        private void LoadPreset(string presetName)
        {
            var data = PresetManager.Load(presetName);
            if (data == null)
            {
                _history.Add(new ChatMessage("assistant", $"프리셋 \"{presetName}\" 불러오기에 실패했습니다."));
                return;
            }

            MapGenParams.Apply(data);
            _paramsReady = true;
            _statusText = "";
            _history.Add(new ChatMessage("assistant",
                $"프리셋 \"{presetName}\" 을(를) 불러왔습니다.\n" +
                $"언덕={data.hills}, 산양={data.hill_amount:F2}, 나무={data.vegetation_density:F1}, " +
                $"동물={data.animal_density:F1}, 강={data.river?.present ?? false}, " +
                $"동굴={data.caves}, 간헐천={data.geysers}\n\n" +
                "아래 버튼으로 맵을 생성하거나, 채팅으로 수정할 수 있습니다."));
        }

        private void GenerateMap()
        {
            // "이 설정으로 맵 생성" 클릭 시: 파라미터 유지한 채로 닫기
            _keepParams = true;
            Close();
            Messages.Message("MapGen AI: 파라미터 저장 완료. 맵을 시작하면 적용됩니다.",
                MessageTypeDefOf.PositiveEvent);
        }

        private bool _keepParams = false;

        public override void PostClose()
        {
            base.PostClose();

            if (!_keepParams && MapGenParams.HasParams)
            {
                // 대화 취소/닫기 → 파라미터 리셋 + Map Preview 원래대로
                MapGenParams.Reset();
                MapGenParams.RefreshMapPreview();
                Log.Message("[MapGenAI] 대화 취소 — 파라미터 리셋, 미리보기 원래대로");
            }
        }
    }
}
