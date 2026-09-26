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
        private readonly List<ChatMessage> _history = new List<ChatMessage>(); // UI 표시용
        private readonly List<ChatMessage> _llmContext = new List<ChatMessage>(); // Full dialog transcript, retained through successful edits.
        private ConversationMemory.Checkpoint _conversationMemory;
        private string _inputText = "";
        private string _statusText = "";
        private bool _isWaiting = false;
        private AuthoringResult _shownAuthoringResult;
        private int _nextAuthoringCheck;
        private Vector2 _scrollPos = Vector2.zero;
        private bool _paramsReady = false;
        private int _lastMessageCount = 0; // 새 메시지 추가 시만 auto-scroll

        // 이 대화에서 열린 타일 ID (닫힐 때 파라미터 리셋 판단용)
        private readonly int _openedTileId;

        // Undo / Reset
        private readonly Stack<TileMapState> _paramStack = new Stack<TileMapState>();
        private TileMapState _initialSnapshot; // dialog 열릴 때 저장

        private readonly RequestGate _requests = new RequestGate();
        private Action<string,string> _explainInvalidReply;
        private bool _explanationOnly;
        private List<RecommendationPlan> _recommendations;
        private string _recommendationState;
        private bool _recommendationsRequested, _recommendationRepairUsed;
        private Action<string,string> _repairRecommendations;
        private RecommendationPreviews _recommendationPreviews;
        private string _previewError;
        private int _nextRecommendationCheck;
        private List<RecommendationPlan> _requestedCandidates;
        private bool _previewsCollapsed;
        private Dialog_RecommendationGuide _guide;
        private Dialog_RecommendationFeedback _feedback;
        private int _feedbackCandidate, _requestedCandidateNumber;

        private const float InputHeight = 36f;
        private const float SendButtonWidth = 80f;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(900f, Verse.UI.screenWidth - 40f), Mathf.Min(760f, Verse.UI.screenHeight - 40f));

        // 바닐라 기본 mutator defName (자동 관리되므로 LLM 목록에서 제외)
        private static readonly HashSet<string> VanillaAutoMutators = new HashSet<string>
            { "Mountain", "Caves", "Coast", "River" };

        /// <summary>
        /// 현재 게임 언어가 한국어인지 확인.
        /// </summary>
        private static bool IsKorean() => L10n.IsKorean();

        // The model sees both eligible features and concise reasons for unavailable requests.
        private static string BuildMutatorList(int tileId)
        {
            var tile = tileId < 0 ? null : Find.WorldGrid?[tileId];
            var available = new System.Text.StringBuilder();
            var replacements = new System.Text.StringBuilder();
            var unavailable = new System.Text.StringBuilder();
            foreach (var feature in DefDatabase<TileMutatorDef>.AllDefsListForReading)
            {
                if (string.IsNullOrEmpty(feature.defName) || string.IsNullOrEmpty(feature.label) || feature.label == "none") continue;
                string reason = FeaturePolicy.UnavailableReason(feature, tile);
                string identity = feature.defName + " (" + feature.label + ")";
                if (reason != null) unavailable.AppendLine(identity + ": " + reason);
                else if (!VanillaAutoMutators.Contains(feature.defName))
                {
                    // Base world connections may be represented by a compatible variant (e.g. RiverDelta).
                    // They must never be proposed as remove_mutators targets.
                    var conflicts = tile.Mutators.Where(old => !VanillaAutoMutators.Contains(old.defName) && WorldTileEditor.Conflict(old,feature)).ToList();
                    string entry=identity + " [" + string.Join(",", feature.categories) + "] overrides=[" + string.Join(",",feature.overrideCategories) + "]";
                    if(conflicts.Count>0) replacements.AppendLine(entry + ": requires replacing " + string.Join(", ",conflicts.Select(WorldTileEditor.FeatureName)));
                    else
                    {
                        try
                        {
                            MapGenParams.ValidatePatch(MapParameterParser.Parse(SimpleJson.Parse("{\"mutators\":[\""+feature.defName+"\"]}")),tileId);
                            available.AppendLine(entry + ": " + feature.description);
                        }
                        catch(FormatException error){unavailable.AppendLine(identity+": "+error.Message);}
                    }
                }
            }
            return "Addable while preserving the current features (checked against the current plan):\n" + available
                + "\nReplacement required — NOT an additive alternative. Ask which existing feature to replace unless the user already explicitly chose it; include remove_mutators for that choice:\n" + replacements
                + "\nUnavailable additions on this tile — explain the reason with action:ask; never substitute silently:\n" + unavailable;
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
            bool isKo = IsKorean();

            // --- 타일 정보 수집 ---
            string biome = "MapGenAI_Unknown".Translate();
            string biomeDef = "";
            string hillsStr = "";
            bool hasRiver = false;
            bool isCoastal = false;
            float elev = 0f;
            string riverInfo = "MapGenAI_RiverNone".Translate();

            try
            {
                if (tileId >= 0)
                {
                    var tile = Find.WorldGrid[tileId];
                    if (tile != null)
                    {
                        biome = tile.PrimaryBiome?.label ?? "MapGenAI_Unknown".Translate();
                        biomeDef = tile.PrimaryBiome?.defName ?? "";
                        hillsStr = tile.hilliness.ToString();
                        hasRiver = tile.Rivers != null && tile.Rivers.Count > 0;
                        elev = tile.elevation;

                        if (hasRiver)
                        {
                            riverInfo = "MapGenAI_RiverPresent".Translate();
                            foreach (var rl in tile.Rivers)
                            {
                                var nb = Find.WorldGrid[rl.neighbor];
                                if (nb?.PrimaryBiome?.defName == "Ocean" || nb?.PrimaryBiome?.defName == "Lake")
                                    riverInfo += "MapGenAI_RiverOceanLink".Translate();
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
            string featureContext = "";
            if (tileId >= 0 && Find.WorldGrid != null)
            {
                var actual = Find.WorldGrid[tileId].Mutators.Select(d => new Dictionary<string,object>{{"defName",d.defName},{"label",d.label},{"categories",d.categories}}).ToList();
                featureContext = "actual_tile_features: " + SimpleJson.Serialize(actual);
                var baseline = MapGenAIWorldComponent.Get()?.GetBaseline(tileId);
                if (baseline != null) featureContext += "\noriginal_tile_features: " + SimpleJson.Serialize(baseline.mutators);
                featureContext += "\nfeature_categories: " + string.Join(", ",DefDatabase<TileMutatorDef>.AllDefsListForReading.SelectMany(d=>d.categories).Distinct().OrderBy(c=>c));
            }

            // --- 석재 목록 (동적) ---
            string rockList = BuildRockTypeList();

            // --- Layer 2: 구조화된 프롬프트 ---
            // 섹션 1: 역할 (짧게)
            string role = isKo
                ? "당신은 RimWorld 맵 생성 도우미입니다. 유저 요청을 JSON으로 변환합니다."
                : "You are a RimWorld map generation assistant. Convert user requests to JSON.";

            // 섹션 2: JSON 스키마
            string schema = isKo
                ? @"반드시 아래 세 형식 중 하나의 JSON만 출력하세요.

질문/안내: {""action"":""ask"",""message"":""내용""}
맵 생성: {""action"":""generate"",""description"":""맵 설명"",""params"":{...}}
추천: {""action"":""recommend"",""options"":[{""params"":{...}},{""params"":{...}}]}

params 스키마:
{""hills"":""left|right|center|edges|top|bottom|none"",""hill_amount"":0.5~1.6,""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""fertility_offset"":-1.0~1.0,""caves"":true|false,""coast_direction"":""auto|north|east|south|west"",""rock_count"":1~15,""rock_types"":[""Granite|Limestone|Marble|Sandstone|Slate""],""ore_density"":0.0~2.5,""ruin_density"":0.0~2.5,""danger_density"":0.0~2.5,""rock_chunks"":true|false,""hill_size"":""small|medium|large"",""hill_smoothness"":""rough|normal|smooth"",""river_direction"":""left|right|up|down|0-360"",""river_position"":""left|center|right|0.0-1.0"",""mutators"":[""defName""],""remove_mutators"":[""defName""],""remove_categories"":[""category""],""restore_categories"":[""category""],""river"":{""present"":true|false},""elevation_shapes"":[{""type"":""ridge|split|radial|bump|noise|ring|composite|landform|passage|region_fill"",""direction"":""left|right|top|bottom|top_left|top_right|bottom_left|bottom_right|0-360"",""strength"":""weak|medium|strong|negative_weak|negative_medium|negative_strong|숫자"",""fade"":""small|medium|large|0.0-1.0"",""noise_amount"":""none|low|medium|high|0.0-1.5"",""edge_roughness"":""none|low|medium|high|0.0-1.0 (composite only)"",""position"":""center|top_left|top|top_right|left|right|bottom_left|bottom|bottom_right|[x,z]"",""size"":""small|medium|large|0-1"",""gap"":""tiny|small|medium|large"",""fill"":""water""}]}

elevation_shapes 가이드:
- ridge: 한 방향에 산맥. direction으로 산이 높은 방향. fade로 산 범위(small=가장자리만, medium=절반, large=맵 대부분). noise_amount로 자연스러움 조절(none=깨끗한 경계, high=매우 불규칙). fade와 noise_amount는 생략 가능(기본값=medium).
  ""양쪽에 산"" = [ridge(left), ridge(right)] → 양쪽 높고 가운데 골짜기.
  ""산맥"" = ridge(fade=small, noise_amount=high).
- split: 축 방향 분할. positive strength=협곡(양쪽 산+가운데 골짜기), negative strength=산맥(가운데 산+양쪽 평지). direction으로 축 방향, gap으로 폭.
  대각선 산맥=split(direction=top_left, strength=negative_strong, gap=medium). 대각선 협곡=split(direction=top_left, strength=strong, gap=small).
- radial: 가장자리가 높고 중심이 낮음(분지/요새). size=small이면 두꺼운 산벽(좁은 분지), size=large면 얇은 산벽(넓은 분지). 산악 요새=radial(strong, size:small).
- bump: 가우시안 돌출/함몰. position으로 위치, size로 크기. negative strength=함몰. fill=water로 호수 생성.
- noise: 펄린 노이즈로 불규칙 지형. size가 클수록 큰 덩어리.
- ring: 도넛 형태 산맥/호수. position으로 중심, size로 링 반경, strength로 높이. fill=water로 링 호수. 분화구/원형 요새 지형에 적합.
- composite: ★자유 형태★ 기본 도형(원/삼각형/사각형/별/하트)을 조합하여 어떤 모양이든 표현.
  shapes: 도형 목록. compose: 합치기(union)/빼기(sub) 연산 체인 → 최종 형태.
  e>=0.1 = 언덕 추가, 0<e<0.1 = 기존 높이를 평지로 교체(마른 통로는 fill:soil + e:0.05). 호수는 fill:water를 명시하세요. fill 없는 e<0는 구버전 호수 표기이므로 통로에 사용하지 마세요. 원·별·하트·도넛 등 모양을 명시하면 composite. 모양 없는 일반 호수/언덕은 기존 bump. composite의 edge_roughness는 생략/none=정확, low/medium/high 또는0~1=자연스러운 윤곽.
  좌표계: [x,z] 정규화 0~1. x=0 왼쪽, x=1 오른쪽, z=0 아래, z=1 위. ""오른쪽 아래""=[0.75,0.25], ""왼쪽 위""=[0.25,0.75].
  도형: circle(center,r), rect(center,w,h), tri(verts 3개), star(center,r,r2,n), heart(center,size), poly(verts), ellipse(center,w,h), path(verts 2..12개 곡선, w 전체 폭)
  연산: add(단일), union(합치기, k>0이면 매끄럽게), sub(빼기, 구멍)
  예: 별 언덕: shapes:[{id:""s"",prim:""star"",center:[0.5,0.5],r:0.35,r2:0.15,n:5}], compose:[{op:""add"",s:""s"",e:0.8}]
  예: 하트 호수: shapes:[{id:""h"",prim:""heart"",center:[0.5,0.45],size:0.3}], compose:[{op:""add"",s:""h"",fill:""water"",e:0}]
  예: 초승달 호수: shapes:[{id:""a"",prim:""circle"",center:[0.5,0.5],r:0.2},{id:""b"",prim:""circle"",center:[0.65,0.55],r:0.2}], compose:[{op:""sub"",a:""b"",from:""a"",out:""c""},{op:""add"",s:""c"",fill:""water"",e:0}]
  초승달 팁: 빼는 원(b)의 중심을 크게 이동시키고 반지름을 같거나 비슷하게. 중심 차이가 클수록 얇은 초승달.
- 여러 shape를 조합 가능 (additive). ""왼쪽에 산 + 오른쪽에도 산"" = [ridge(left), ridge(right)].
- 기존 지형 편집은 아래 shape_ops 계약을 따릅니다. 요청한 대상만 수정하고 다른 지형은 생략하세요.
- 현재 맵에 elevation_shapes가 없으면(첫 요청) hills만 사용해도 됩니다.
- 산맥=ridge(fade=small, noise_amount=high). 대각선 산맥=split(direction=대각선, strength=negative). ""양쪽 산맥""=2개 ridge 조합. 협곡=2개 ridge + bump(negative strength, center).
- 모양 요청(하트/별/고양이/L자/초승달 등)에는 반드시 composite 사용. 표현 한계가 있으면 한계와 대안을 설명하세요.

추가 파라미터:
- rock_types: 원하는 석재 종류 지정. 바닐라 석재: Granite(화강암), Limestone(석회암), Marble(대리석), Sandstone(사암), Slate(점판암). 예: ""rock_types"":[""Marble"",""Granite""]
- danger_density: 고대 위협/위험 밀도 (0.0~2.5, 기본 1.0). 고대 위협의 무작위 생성 밀도입니다. 특정 위치/개수 지정은 아래 structure_ops의 ancient_danger를 사용하며 밀도와 혼동하지 마세요. 0=없음, 2.5=매우 많음.
- ruin_density: 폐허/고대 유적 밀도 (부서진 벽·오래된 잔해. 폐허·고대 유적·유적은 여기. 단 정상 맵엔 효과 거의 없음 — 특수 맵 전용).
- rock_chunks: 돌덩어리 생성 여부 (기본 true). false로 설정하면 맵에 돌덩어리가 없음. ""깨끗한 맵"", ""돌 없애줘"", ""바위 없애줘"", ""돌덩어리 없애"", ""깔끔하게"" 요청 시 사용.
- hill_size: 산맥 크기 (small=잘게 쪼개짐, medium=기본, large=거대 산맥). 또는 숫자(0.005~0.1, 기본 0.021).
- hill_smoothness: 산 표면 거칠기 (rough=울퉁불퉁, normal=기본, smooth=매끄러움). 또는 숫자(0.5~6.0, 기본 2.0).
- hill_amount: 전체 고도 오프셋 (0.1~1.3, 기본 1.0). 0.1=완전 평지(강제), 0.5=완만한 평지, 1.2=산이 많아짐. 1.3 이상은 맵 대부분이 산으로 뒤덮이므로 주의. ""완전 평지"" 요청 시 0.1, ""산 많이"" 요청 시 1.2 사용. 1.0은 기본값(변화 없음).
- river_direction: 강 방향. left/right/up/down 또는 0-360도 각도. 0=위(북), 90=오른쪽(동), 180=아래(남), 270=왼쪽(서). 미지정시 자동.
- river_position: 강 위치. left/right/up/down/center 또는 0.0~1.0 숫자. 좌우 이동은 x축, 상하 이동은 z축으로 자동 처리. 미지정시 중앙.
- straight_river: 일자 강 (true/false). true면 강이 구불거리지 않고 직선으로 흐름. '일자 강', '운하', '직선 강' 요청 시 사용.
- fertility_offset: 비옥도 오프셋 (-1.0~1.0, 기본 0). 양수=기름진 토양 증가(0.5 권장), 음수=감소. '기름진 토양 많이', '비옥한 맵' 등 요청 시 사용."
                : @"Output exactly one of these three JSON formats.

Question/guide: {""action"":""ask"",""message"":""content""}
Map generation: {""action"":""generate"",""description"":""map description"",""params"":{...}}
Recommendations: {""action"":""recommend"",""options"":[{""params"":{...}},{""params"":{...}}]}

params schema:
{""hills"":""left|right|center|edges|top|bottom|none"",""hill_amount"":0.5~1.6,""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""fertility_offset"":-1.0~1.0,""caves"":true|false,""coast_direction"":""auto|north|east|south|west"",""rock_count"":1~15,""rock_types"":[""Granite|Limestone|Marble|Sandstone|Slate""],""ore_density"":0.0~2.5,""ruin_density"":0.0~2.5,""danger_density"":0.0~2.5,""rock_chunks"":true|false,""hill_size"":""small|medium|large"",""hill_smoothness"":""rough|normal|smooth"",""river_direction"":""left|right|up|down|0-360"",""river_position"":""left|center|right|0.0-1.0"",""mutators"":[""defName""],""remove_mutators"":[""defName""],""remove_categories"":[""category""],""restore_categories"":[""category""],""river"":{""present"":true|false},""elevation_shapes"":[{""type"":""ridge|split|radial|bump|noise|ring|composite|landform|passage|region_fill"",""direction"":""left|right|top|bottom|top_left|top_right|bottom_left|bottom_right|0-360"",""strength"":""weak|medium|strong|negative_weak|negative_medium|negative_strong|number"",""fade"":""small|medium|large|0.0-1.0"",""noise_amount"":""none|low|medium|high|0.0-1.5"",""edge_roughness"":""none|low|medium|high|0.0-1.0 (composite only)"",""position"":""center|top_left|top|top_right|left|right|bottom_left|bottom|bottom_right|[x,z]"",""size"":""small|medium|large|0-1"",""gap"":""tiny|small|medium|large"",""fill"":""water""}]}

elevation_shapes guide:
- ridge: Mountains on one side. Use direction to set which side is high. fade controls range (small=edge only, medium=half, large=most of map). noise_amount controls naturalness (none=clean, high=very rough). fade and noise_amount are optional (default=medium).
  ""mountains on both sides"" = [ridge(left), ridge(right)] -> both sides high, valley in center.
  ""mountain range"" = ridge(fade=small, noise_amount=high).
- split: Axis-based split. Positive strength=canyon (mountains on both sides, valley in center). Negative strength=mountain range (mountain in center, plains on sides). Use direction for axis, gap for width.
  Diagonal mountain range=split(direction=top_left, strength=negative_strong, gap=medium). Diagonal canyon=split(direction=top_left, strength=strong, gap=small).
- radial: Edges high, center low (basin/fortress). size=small means thick walls (small basin), size=large means thin walls (large basin). Fortress=radial(strong, size:small).
- bump: Gaussian bump/depression. Use position for location, size for extent. Negative strength=depression. fill=water to create a lake.
- noise: Perlin noise for irregular terrain. Larger size means bigger clusters.
- ring: Donut-shaped mountain range/lake. Use position for center, size for ring radius, strength for height. fill=water for ring lake. Suitable for craters/circular fortress terrain.
- composite: ★Free-form shapes★ Combine primitives (circle/triangle/rectangle/star/heart) to create any shape.
  shapes: list of primitives. compose: boolean chain (union/sub) → final shape.
  e>=0.1 adds a hill; 0<e<0.1 sets an absolute flat height (dry passage: fill:soil + e:0.05). Lakes must explicitly use fill:water. Negative e without fill is legacy lake notation, never a dry cut. Explicit circle/star/heart/donut shapes use composite; generic lakes/hills use legacy bump. Composite edge_roughness omitted/none=precise, low/medium/high or0..1=natural outline.
  Coordinates: [x,z] normalized 0~1. x=0 left, x=1 right, z=0 bottom, z=1 top. ""bottom right""=[0.75,0.25], ""top left""=[0.25,0.75].
  Primitives: circle(center,r), rect(center,w,h), tri(verts x3), star(center,r,r2,n), heart(center,size), poly(verts), ellipse(center,w,h), path(verts 2..12 curve controls, w full width)
  Operations: add(single), union(combine, k>0 for smooth), sub(subtract, hole)
  Ex: Star hill: shapes:[{id:""s"",prim:""star"",center:[0.5,0.5],r:0.35,r2:0.15,n:5}], compose:[{op:""add"",s:""s"",e:0.8}]
  Ex: Heart lake: shapes:[{id:""h"",prim:""heart"",center:[0.5,0.45],size:0.3}], compose:[{op:""add"",s:""h"",fill:""water"",e:0}]
  Ex: Crescent lake: shapes:[{id:""a"",prim:""circle"",center:[0.5,0.5],r:0.2},{id:""b"",prim:""circle"",center:[0.65,0.55],r:0.2}], compose:[{op:""sub"",a:""b"",from:""a"",out:""c""},{op:""add"",s:""c"",fill:""water"",e:0}]
  Crescent tip: move b's center far from a, keep radius similar. Bigger center gap = thinner crescent.
- Multiple shapes can be combined (additive). ""mountains left + right"" = [ridge(left), ridge(right)].
- For existing terrain use the shape_ops contract below. Edit requested targets only; omit other terrain.
- If current map has no elevation_shapes (first request), you may use hills alone.
- Mountain range=ridge(fade=small, noise_amount=high). Diagonal range=split(direction=diagonal, strength=negative). ""both sides mountain""=2 ridge combo. Canyon=2 ridges + bump(negative strength, center).
- For shape requests (heart/star/cat/L-shape/crescent etc.), MUST use composite. Explain representation limits and alternatives when needed.

Additional parameters:
- rock_types: Specify desired rock types. Vanilla rocks: Granite, Limestone, Marble, Sandstone, Slate. Example: ""rock_types"":[""Marble"",""Granite""]
- danger_density: Ancient danger/threat density (0.0~2.5, default 1.0). Controls random ancient danger density. For a specified location/count use structure_ops with ancient_danger below; position and density are separate. 0=none, 2.5=very many.
- ruin_density: Rubble/old-ruins density (broken walls, old debris. 폐허 / 고대 유적 / ruins map here. But little effect on normal maps — special maps only).
- rock_chunks: Whether to generate rock chunks (default true). Set false for no rock chunks on the map. Use for ""clean map"", ""remove rocks"", ""no rocks"", ""remove boulders"", ""clear terrain"" requests.
- hill_size: Mountain size (small=fragmented, medium=default, large=huge mountains). Or a number (0.005~0.1, default 0.021).
- hill_smoothness: Mountain surface roughness (rough=jagged, normal=default, smooth=smooth). Or a number (0.5~6.0, default 2.0).
- hill_amount: Global elevation offset (0.1~1.3, default 1.0). 0.1=completely flat (forced), 0.5=gently flattened, 1.2=more mountains. Values above 1.3 will cover most of the map with mountains — use with caution. Use 0.1 for ""completely flat"", 1.2 for ""lots of mountains"". 1.0 is default (no change).
- river_direction: River direction. left/right/up/down or 0-360 degree angle. 0=up(north), 90=right(east), 180=down(south), 270=left(west). Auto if unspecified.
- river_position: River position. left/right/up/down/center or 0.0~1.0 number. Left/right moves on x-axis, up/down on z-axis. Center if unspecified.
- straight_river: Straight river (true/false). If true, the river flows in a straight line without meandering. Use for 'straight river', 'canal' requests.
- fertility_offset: Fertility offset (-1.0~1.0, default 0). Positive=more rich soil (0.5 recommended), negative=less. Use for 'lots of rich soil', 'fertile map' requests.";

            // 섹션 3: 타일 컨텍스트 + 유효 옵션 (동적)
            string coastalLabel = isCoastal ? "MapGenAI_Yes".Translate().ToString() : "MapGenAI_No".Translate().ToString();
            string tileContext = isKo
                ? $@"
[타일 정보] 바이옴={biome}({biomeDef}), 지형={hillsStr}, 고도={elev:F0}m, 강={riverInfo}, 해안={coastalLabel}

[사용 가능한 석재] rock_types에는 이 defName만 사용하세요.
  {rockList}

[사용 가능한 mutators] 이 목록에 있는 defName만 사용하세요.
{mutatorList}"
                : $@"
[Tile Info] Biome={biome}({biomeDef}), Terrain={hillsStr}, Elevation={elev:F0}m, River={riverInfo}, Coastal={coastalLabel}

[Available rocks] Use only these defNames for rock_types.
  {rockList}

[Available mutators] Use only defNames from this list.
{mutatorList}";

            // 섹션 4: 규칙 (코드 검증 대상은 제외, 기능 안내만)
            string rules = isKo
                ? @"규칙:
- 요청하지 않은 파라미터는 생략하세요. 현재 값이 유지됩니다.
- 맵 특징(mutators): 추가할 것만 mutators에, 제거할 것만 remove_mutators에 넣으세요. 이미 있는 특징(actual_tile_features)은 다시 안 적어도 유지됩니다. 특징을 교체할 땐 remove_mutators로 뺀 뒤 mutators로 추가.
- 완전 평지 = hills:none + hill_amount:0.1 + elevation_shapes:[]
- 마른 통로/산 출구는 아래 passage 규칙을 따릅니다. negative bump는 마른 통로 대신 사용하지 마세요.
- fill로 지형 종류 지정: water/sand/soil/rich_soil/marsh/mud/ice. bump/ring/composite에서 사용.
- 온천=mutators:[""HotSprings""], 간헐천 개수=geysers:N.
- 한국어로 답변하세요."
                : @"Rules:
- Omit parameters not requested. Current values are kept.
- Map features (mutators): put only what to ADD in mutators, only what to REMOVE in remove_mutators. Existing features (actual_tile_features) are kept even if you don't re-list them. To replace a feature, remove it via remove_mutators then add via mutators.
- Flat terrain = hills:none + hill_amount:0.1 + elevation_shapes:[]
- Dry passages/mountain exits follow the passage rules below. Do not substitute a negative bump for a dry passage.
- fill specifies terrain type: water/sand/soil/rich_soil/marsh/mud/ice. Works with bump/ring/composite.
- Hot springs=mutators:[""HotSprings""], geyser count=geysers:N.
- Respond in English.";

            // 섹션 5: few-shot 예시
            string fewShot;
            if (!isCoastal)
            {
                // 내륙 타일: elevation_shapes 예시 + 해안 거절 예시
                fewShot = isKo
                    ? @"
예시1) 유저: ""산악 요새에 호수"" → {""action"":""generate"",""description"":""산악 요새에 호수"",""params"":{""elevation_shapes"":[{""type"":""radial"",""strength"":""strong"",""size"":""medium""},{""type"":""bump"",""position"":""center"",""size"":""small"",""strength"":""negative_strong"",""fill"":""water""}]}}
예시2) 유저: ""왼쪽에 산, 완전 평지"" → {""action"":""generate"",""description"":""왼쪽에 산"",""params"":{""elevation_shapes"":[{""type"":""ridge"",""direction"":""left"",""strength"":""medium""}]}}
예시3) 유저: ""남쪽에 통로 뚫어줘"" → {""action"":""generate"",""description"":""남쪽 통로"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""south_exit"",""type"":""passage"",""points"":[[0.5,0.5],[0.5,0]],""width"":8,""scope"":""mountains"",""fill"":""Soil""}}]}}"
                    : @"
Ex1) ""Mountain fortress with lake"" → {""action"":""generate"",""description"":""fortress with lake"",""params"":{""elevation_shapes"":[{""type"":""radial"",""strength"":""strong"",""size"":""medium""},{""type"":""bump"",""position"":""center"",""size"":""small"",""strength"":""negative_strong"",""fill"":""water""}]}}
Ex2) ""Mountains on the left"" → {""action"":""generate"",""description"":""left mountains"",""params"":{""elevation_shapes"":[{""type"":""ridge"",""direction"":""left"",""strength"":""medium""}]}}
Ex3) ""Open a passage south"" → {""action"":""generate"",""description"":""south passage"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""south_exit"",""type"":""passage"",""points"":[[0.5,0.5],[0.5,0]],""width"":8,""scope"":""mountains"",""fill"":""Soil""}}]}}";
            }
            else
            {
                // 해안 타일: 기본 + elevation_shapes 예시
                fewShot = isKo
                    ? @"
예시1) 유저: ""왼쪽에 산, 오른쪽 아래에 호수"" → {""action"":""generate"",""description"":""왼쪽 산+오른쪽 아래 호수"",""params"":{""elevation_shapes"":[{""type"":""ridge"",""direction"":""left"",""strength"":""strong""},{""type"":""bump"",""position"":""bottom_right"",""size"":""medium"",""strength"":""negative_strong"",""fill"":""water""}]}}
추천 요청은 아래 추천 규칙과 현재 타일의 지형·해안 조건을 따르세요."
                    : @"
Ex1) ""Mountains left, lake bottom-right"" → {""action"":""generate"",""description"":""left mountains + lake"",""params"":{""elevation_shapes"":[{""type"":""ridge"",""direction"":""left"",""strength"":""strong""},{""type"":""bump"",""position"":""bottom_right"",""size"":""medium"",""strength"":""negative_strong"",""fill"":""water""}]}}
For recommendations follow the rules below and this tile's terrain and shore context.";
            }

            string currentParams = MapGenParams.BuildCurrentParamsText(isKo);
            var outcome=AuthoringGeneration.Latest(tileId,MapGenParams.CaptureState(tileId));
            if(outcome?.issues.Count>0)currentParams += "\nLast generation failed: " + string.Join("\n",outcome.issues);

            string modExample = ShapeEditPrompt.Rules(isKo) + FeatureEditPrompt.Rules(isKo) + TextRegionPrompt.Rules(isKo) + PassagePrompt.Rules(isKo) + LandformPrompt.Rules(isKo,MapGenParams.ElevationShapes.Any(s=>s.type=="landform")) + RoadPrompt.Rules(isKo) + RecommendationPlan.Rules;
            // Whole-layout examples describe initial generation only.
            if (MapGenParams.ElevationShapes.Count > 0) fewShot = "";

            return $@"{role}

{schema}
{tileContext}
{featureContext}
{currentParams}
{rules}
{fewShot}{modExample}";
        }

        // Enter 키가 Window 시스템을 통해 월드맵으로 전달되는 것 방지
        public override void OnAcceptKeyPressed()
        {
            // 의도적으로 비움: Enter는 DoWindowContents에서 메시지 전송으로 처리
        }

        // WorldComponent의 대화 시작 시점 스냅샷 (닫기=취소 시 복원용)

        public Dialog_TextToMap()
        {
            doCloseButton = false;
            doCloseX = true;
            closeOnAccept = false;
            draggable = true;
            forcePause = false;  // Map Preview와 공존하기 위해 pause 안 함
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
            layer = WindowLayer.Super;

            _openedTileId = Find.WorldSelector.SelectedTile;
            MapGenParams.CurrentTileId = _openedTileId;

            // WorldComponent에서 기존 타일 상태 로드
            MapGenParams.LoadFromTile(_openedTileId);
            _initialSnapshot = MapGenAIWorldComponent.Get()?.GetState(_openedTileId)?.Clone();
            _paramsReady = _initialSnapshot != null;


            _history.Add(new ChatMessage("assistant",
                "MapGenAI_Welcome".Translate()));

        }

        private void PollResponse()
        {
            var reply = _requests.Take();
            if (reply != null)
            {
                _isWaiting = false;
                _statusText = "";
                if(reply.Context is ConversationMemory.Prepared prepared)
                {
                    _conversationMemory=prepared.Memory;
                    if(prepared.Warning!=null)_history.Add(new ChatMessage("assistant",prepared.Warning));
                }
                if (reply.Error != null)
                {
                    _llmContext.Add(new ChatMessage("assistant","NOT APPLIED\n"+reply.Error));
                    _history.Add(new ChatMessage("assistant", "MapGenAI_Error".Translate(reply.Error)));
                }
                else HandleResponse(reply.Text);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            PollResponse();
            UpdateRecommendationPreviews();
            var font = Text.Font;
            if (Time.frameCount >= _nextAuthoringCheck)
            {
                _nextAuthoringCheck = Time.frameCount + 60;
                var result = AuthoringGeneration.Latest(_openedTileId, MapGenParams.CaptureState(_openedTileId));
                if (result != null && result != _shownAuthoringResult)
                {
                    _shownAuthoringResult = result;
                    if(result.preview && result.nativeRoadPreviewLimited)_history.Add(new ChatMessage("assistant",RoadPlans.NativePreviewNote(IsKorean())));
                    if (result.issues.Count > 0) _history.Add(new ChatMessage("assistant", string.Join("\n",result.issues)));
                    else if (result.placements.Count > 0) _history.Add(new ChatMessage("assistant", (IsKorean()?"구조물 배치 계획: ":"Planned structures: ") + result.placements.Count +
                        (IsKorean()?". 실제 맵의 다른 건물에 따라 위치가 달라질 수 있으며, 생성 때 다시 검사합니다.":". Other buildings in the full map can affect placement; generation checks again.")));
                    if (result.protectedCells > 0) _history.Add(new ChatMessage("assistant", (IsKorean()?"기존 강·바다·도로를 유지해 채움에서 제외한 칸: ":"Fill cells excluded to preserve existing rivers, ocean or roads: ") + result.protectedCells));
                    if(result.preview && result.placements.Any(p=>p.kind=="ancient_danger")) _history.Add(new ChatMessage("assistant",IsKorean()?
                        "고대 위협의 주황 테두리는 배치할 영역입니다. 실제 건물 내부·적·전리품은 맵 생성 때 난이도와 활성 콘텐츠에 따라 결정됩니다.":
                        "The orange ancient-danger outline reserves an area. The actual interior, occupants and loot are generated according to difficulty and active content."));
                }
            }

            // 타이틀 바
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            Widgets.DrawBoxSolid(titleRect, new Color(0.15f, 0.35f, 0.55f, 0.95f));
            Text.Font = GameFont.Small;
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            var oldColor = GUI.color;
            GUI.color = new Color(0.9f, 0.95f, 1f);
            Widgets.Label(titleRect, MapGenAIMod.DisplayName);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            bool showRecommendationStart = _llmContext.Count == 0 && _recommendations == null;
            if (!showRecommendationStart && !MapGenAI.ImageInput.ImageFeatureGate.Enabled)
            {
                GUI.enabled = !_isWaiting && _guide == null;
                if (Widgets.ButtonText(new Rect(titleRect.xMax-140f,titleRect.y,140f,28f),IsKorean()?"취향 문답":"Preferences")) OpenRecommendationGuide();
                GUI.enabled = true;
            }
            if (MapGenAI.ImageInput.ImageFeatureGate.Enabled)
            {
                var imageButton = new Rect(titleRect.xMax-145f,titleRect.y,140f,28f);
                if (Widgets.ButtonText(imageButton,IsKorean()?"이미지":"Images")) OpenImageMap();
            }

            // 채팅 영역 (타이틀 아래)
            float topOffset = titleRect.yMax + 4f;
            float startHeight = showRecommendationStart ? 40f : 0f;
            if (showRecommendationStart)
            {
                float width=(inRect.width-6f)/2f;
                GUI.enabled = !_isWaiting && _guide == null;
                if (Widgets.ButtonText(new Rect(inRect.x,topOffset,width,34f),IsKorean()?"바로 추천받기":"Quick suggestions"))
                    SendText(IsKorean()?"그냥 추천해 줘":"Recommend a map.");
                if (Widgets.ButtonText(new Rect(inRect.x+width+6f,topOffset,width,34f),IsKorean()?"취향에 맞춰 추천받기":"Find my preferences")) OpenRecommendationGuide();
                GUI.enabled = true;
            }
            topOffset += startHeight;
            var layout = new RecommendationLayout(inRect.width, inRect.height-startHeight, _recommendations?.Count ?? 0, _previewsCollapsed);
            float previewHeight = layout.PreviewHeight;
            float choiceHeight = layout.ChoiceHeight;
            var chatRect = new Rect(inRect.x, topOffset, inRect.width, layout.ChatHeight);
            DrawChat(chatRect);

            // 입력창 + 전송 버튼
            if (_recommendations != null)
            {
                if (previewHeight > 0f) DrawRecommendationPreviews(new Rect(inRect.x, chatRect.yMax + 4f, inRect.width, previewHeight));
                int count=_recommendations.Count;
                GUI.enabled = !_isWaiting;
                for(int i=0;i<count;i++)
                {
                    float width=(inRect.width-6f*(count-1))/count;
                    float x=inRect.x+i*(width+6f),y=chatRect.yMax+4f+previewHeight;
                    string problem=CandidateSelectionProblem(i+1);
                    var applyRect=new Rect(x,y,width*.6f-3f,28f);
                    GUI.enabled=!_isWaiting && problem==null;
                    if(Widgets.ButtonText(applyRect,problem==null?(IsKorean()?(i+1)+"번 적용":"Apply "+(i+1)):
                        IsKorean()?(i+1)+"번 확인 필요":"Check "+(i+1)))
                    { ApplyRecommendation(i+1); break; }
                    if(problem!=null)TooltipHandler.TipRegion(applyRect,problem);
                    GUI.enabled=!_isWaiting;
                    if(Widgets.ButtonText(new Rect(x+width*.6f+3f,y,width*.4f-3f,28f),IsKorean()?"수정":"Refine"))OpenRecommendationFeedback(i+1);
                }
                GUI.enabled = true;
                // These controls do not apply an option or reset the map/Undo history.
                float controlsY = chatRect.yMax + 4f + previewHeight + 32f;
                float controlsWidth = (inRect.width - 12f) / 3f;
                if (Widgets.ButtonText(new Rect(inRect.x, controlsY, controlsWidth, 28f), IsKorean()?"다시 추천받기":"New suggestions"))
                    OpenRecommendationFeedback(0);
                if (Widgets.ButtonText(new Rect(inRect.x + controlsWidth + 6f, controlsY, controlsWidth, 28f), IsKorean()?"선택 안 함":"Select none"))
                    DismissRecommendations();
                GUI.enabled = _recommendations != null && new RecommendationLayout(inRect.width, inRect.height, count, false).PreviewHeight > 0f;
                if (Widgets.ButtonText(new Rect(inRect.x + 2f * (controlsWidth + 6f), controlsY, controlsWidth, 28f),
                    previewHeight > 0f ? (IsKorean()?"그림 접기":"Hide previews") : (IsKorean()?"그림 펼치기":"Show previews")))
                    _previewsCollapsed = !_previewsCollapsed;
                GUI.enabled = true;
            }
            var inputAreaY = chatRect.yMax + choiceHeight + 8f;
            var inputRect = new Rect(inRect.x, inputAreaY, inRect.width - SendButtonWidth - 8f, InputHeight);
            var sendRect = new Rect(inputRect.xMax + 8f, inputAreaY, SendButtonWidth, InputHeight);

            // Enter 키: 항상 소비 (다른 Window로 전달 방지 → Map Preview 보호)
            if (_guide == null && _feedback == null && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Event.current.Use();
                if (!_isWaiting && !string.IsNullOrEmpty(_inputText))
                    SendMessage();
            }

            GUI.SetNextControlName("ChatInput");
            _inputText = Widgets.TextField(inputRect, _inputText);

            // 전송 버튼
            GUI.enabled = !_isWaiting && _feedback == null && !string.IsNullOrEmpty(_inputText);
            if (Widgets.ButtonText(sendRect, _isWaiting ? "..." : "MapGenAI_Send".Translate().ToString()))
                SendMessage();
            GUI.enabled = true;

            // 하단: 맵 생성 버튼 + Undo/Reset + 프리셋 버튼 or 상태 텍스트
            var bottomY = sendRect.yMax + 6f;
            float sp = 6f;
            float undoBtnW = 80f;
            float resetBtnW = 80f;
            float presetBtnW = 90f;

            // 질문(ask) 후에도 이미 만든 맵(HasParams)이 있으면 생성 버튼 유지 —
            // 안 그러면 맵 만든 뒤 질문 한 번에 버튼이 사라져 마무리(맵 확정)를 못 함.
            if (_paramsReady || MapGenParams.HasParams)
            {
                float generateBtnW = inRect.width - undoBtnW - resetBtnW - presetBtnW * 2 - sp * 4;
                var generateRect   = new Rect(inRect.x, bottomY, generateBtnW, 36f);
                var undoRect       = new Rect(generateRect.xMax + sp, bottomY, undoBtnW, 36f);
                var resetRect      = new Rect(undoRect.xMax + sp, bottomY, resetBtnW, 36f);
                var presetSaveRect = new Rect(resetRect.xMax + sp, bottomY, presetBtnW, 36f);
                var presetLoadRect = new Rect(presetSaveRect.xMax + sp, bottomY, presetBtnW, 36f);

                GUI.enabled = !_isWaiting;
                if (Widgets.ButtonText(generateRect, "MapGenAI_Generate".Translate()))
                    GenerateMap();
                GUI.enabled = true;

                GUI.enabled = _paramStack.Count > 0 && !_isWaiting;
                if (Widgets.ButtonText(undoRect, "MapGenAI_Undo".Translate()))
                    DoUndo();
                GUI.enabled = true;

                if (Widgets.ButtonText(resetRect, "MapGenAI_Reset".Translate()))
                    DoReset();

                if (Widgets.ButtonText(presetSaveRect, "MapGenAI_PresetSave".Translate()))
                    Find.WindowStack.Add(new Dialog_PresetName(SaveCurrentPreset));

                if (Widgets.ButtonText(presetLoadRect, "MapGenAI_PresetLoad".Translate()))
                    ShowPresetLoadMenu();
            }
            else
            {
                var undoRect     = new Rect(inRect.x, bottomY, undoBtnW, 36f);
                var resetRect    = new Rect(undoRect.xMax + sp, bottomY, resetBtnW, 36f);
                var loadOnlyRect = new Rect(resetRect.xMax + sp, bottomY, presetBtnW, 36f);

                GUI.enabled = _paramStack.Count > 0 && !_isWaiting;
                if (Widgets.ButtonText(undoRect, "MapGenAI_Undo".Translate()))
                    DoUndo();
                GUI.enabled = true;

                if (Widgets.ButtonText(resetRect, "MapGenAI_Reset".Translate()))
                    DoReset();

                if (Widgets.ButtonText(loadOnlyRect, "MapGenAI_PresetLoad".Translate()))
                    ShowPresetLoadMenu();

                if (_statusText != "")
                {
                    Widgets.Label(new Rect(loadOnlyRect.xMax + 8f, bottomY + 4f, inRect.width - loadOnlyRect.xMax - 8f, 28f), _statusText);
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
            // Pending alternatives use the full chat width so three landscape descriptions remain readable.
            float MessageWidth(ChatMessage msg)=>_recommendations!=null && msg==_history.LastOrDefault()?scrollWidth:msgRenderWidth;

            float contentHeight = 0f;
            foreach (var msg in _history)
                contentHeight += Text.CalcHeight(msg.Content, MessageWidth(msg)-12f) + 12f + 6f;
            if (_isWaiting)
                contentHeight += 30f; // "AI 응답 대기 중..." 높이

            var viewRect = new Rect(0, 0, scrollWidth, Mathf.Max(contentHeight, innerRect.height));
            Widgets.BeginScrollView(innerRect, ref _scrollPos, viewRect);

            float y = 0f;
            foreach (var msg in _history)
            {
                bool isUser = msg.Role == "user";
                float width=MessageWidth(msg);
                float msgHeight = Text.CalcHeight(msg.Content, width-12f) + 12f;
                float x = isUser ? scrollWidth - width : 0f;

                var bgColor = isUser
                    ? new Color(0.18f, 0.38f, 0.62f, 0.92f)  // 유저: 진한 파랑
                    : new Color(0.18f, 0.20f, 0.22f, 0.92f);  // AI: 어두운 회색

                Widgets.DrawBoxSolid(new Rect(x, y, width, msgHeight), bgColor);
                Widgets.Label(new Rect(x, y, width, msgHeight).ContractedBy(6f), msg.Content);
                y += msgHeight + 6f;
            }

            // 대기 중이면 "AI 응답 대기 중..." 표시
            if (_isWaiting)
            {
                float dotCount = ((int)(Time.realtimeSinceStartup * 2f)) % 4;
                string dots = new string('.', (int)dotCount);
                string waitText = $"{"MapGenAI_Waiting".Translate()}{dots}";
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
            SendText(text);
        }

        private void SendText(string text)
        {
            _requestedCandidateNumber=_feedbackCandidate;
            _history.Add(new ChatMessage("user", text));
            _llmContext.Add(new ChatMessage("user", text));
            if (_recommendations != null && _feedbackCandidate==0)
            {
                if (RecommendationPlan.IsDismissal(text)) { DismissRecommendations(); return; }
                int selected=RecommendationPlan.Selection(text);
                if(selected>0){ApplyRecommendation(selected);return;}
                if(RecommendationPlan.IsAmbiguousAcceptance(text))
                {
                    if(_recommendations.Count==1){ApplyRecommendation(1);return;}
                    _history.Add(new ChatMessage("assistant",IsKorean()?"적용할 번호를 선택해 주세요. 마음에 들지 않으면 ‘다시 추천받기’나 ‘선택 안 함’을 누르세요. 아직 맵에는 적용하지 않았습니다.":"Choose an option number, or use New suggestions or Select none. Your map has not changed yet."));
                    return;
                }
            }
            bool newOptions = _feedbackCandidate<0 || _feedbackCandidate==0 && RecommendationPlan.RequestsNewOptions(text);
            _requestedCandidates=_feedbackCandidate>0?_recommendations:newOptions || RecommendationPlan.RequestsDirectEdit(text)?null:_recommendations;
            if(_requestedCandidates==null)ClearRecommendations();
            _recommendationsRequested=_requestedCandidates==null && (newOptions || RecommendationPlan.IsRequest(text));
            _recommendationRepairUsed=false;
            var settings = MapGenAIMod.Settings;
            var clients = new List<ILLMClient>();
            try
            {
                var active = settings.GetActiveConfig();
                if (active == null || !active.IsValid()) throw new Exception("No valid API configured");
                clients.Add(LLMClientFactory.Create(active, settings.localBaseUrl));
                // Capture fallback on the UI thread; workers never change saved settings.
                if (!settings.useSimpleMode && settings.useCloudProviders)
                    for (int i=1;i<settings.cloudConfigs.Count;i++)
                    {
                        var next=settings.cloudConfigs[(settings.currentConfigIndex+i)%settings.cloudConfigs.Count];
                        if (next.IsValid()) { clients.Add(LLMClientFactory.Create(next,settings.localBaseUrl)); break; }
                    }
            }
            catch (Exception e)
            {
                _statusText = "MapGenAI_Error".Translate(e.Message);
                return;
            }
            var systemPrompt = BuildSystemPrompt(_openedTileId);
            if(_recommendationsRequested)systemPrompt+=RecommendationVariation.NextInstruction();
            if(_requestedCandidates!=null)systemPrompt+=RecommendationPlan.PendingInstruction(_requestedCandidates,MapGenParams.CaptureState(_openedTileId));
            if(_requestedCandidates!=null && !MapGenParams.ElevationShapes.Any(s=>s.type=="landform") && _requestedCandidates.Any(p=>p.Resolve(MapGenParams.CaptureState(_openedTileId)).elevationShapes.Any(s=>s.type=="landform")))systemPrompt+=LandformPrompt.SavedRules(IsKorean());
            var historySnapshot = ConversationMemory.Copy(_llmContext);
            StartChat(clients,historySnapshot,systemPrompt,false);
        }

        // Only the provider call runs on a worker. Candidate planning and repair decisions stay on the UI thread.
        private void StartChat(List<ILLMClient> clients,List<ChatMessage> historySnapshot,string systemPrompt,bool explanationOnly)
        {
            if(!systemPrompt.Contains(ConversationMemory.Rules))systemPrompt+=ConversationMemory.Rules;
            var memorySnapshot=_conversationMemory;
            int budgetOverride=MapGenAIMod.Settings.conversationInputBudget;
            _explanationOnly=explanationOnly;
            _repairRecommendations=explanationOnly || _recommendationRepairUsed?null:(Action<string,string>)((rejected,reason)=>
            {
                _recommendationRepairUsed=true;
                var history=new List<ChatMessage>(historySnapshot);
                history.Add(new ChatMessage("assistant",rejected));
                history.Add(new ChatMessage("user","The proposed options failed validation before display; nothing was applied. Correct all options against the current state and return action recommend. Do not remove existing features to bypass this error unless the original request explicitly asked for replacement. Reason: "+reason));
                StartChat(clients,history,systemPrompt,false);
            });
            _explainInvalidReply=explanationOnly?null:(Action<string,string>)((rejected,reason)=>
            {
                var history=new List<ChatMessage>(historySnapshot);
                history.Add(new ChatMessage("assistant",rejected));
                history.Add(new ChatMessage("user",InvalidEditExplanation.Instruction(reason)));
                _statusText=IsKorean()?"현재 설정과 맞지 않는 부분을 확인하고 있습니다…":"Checking the conflict with your current settings…";
                StartChat(clients,history,BuildSystemPrompt(_openedTileId),true);
            });
            var ticket = _requests.Begin();
            _isWaiting = true;
            _statusText = "MapGenAI_Requesting".Translate();
            bool requireRecommendations=!explanationOnly && (_recommendationsRequested || _recommendationRepairUsed);
            Task.Run(async () =>
            {
                string result=null, error=null;
                ConversationMemory.Prepared prepared=null;
                foreach (var client in clients)
                {
                    try
                    {
                        ticket.Token.ThrowIfCancellationRequested();
                        var budget=client is IContextBudgetClient budgetClient?await budgetClient.GetContextBudgetAsync(ticket.Token):ContextBudget.Fallback;
                        int effectiveBudget=budgetOverride>=1024?(budget.Known?Math.Min(budget.InputTokens,budgetOverride):budgetOverride):budget.InputTokens;
                        prepared=await ConversationMemory.PrepareAsync(historySnapshot,systemPrompt,memorySnapshot,effectiveBudget,
                            (messages,prompt,ct)=>client.SendChatAsync(messages,prompt,ct),
                            client is IChatTokenCounter counter?(Func<List<ChatMessage>,string,System.Threading.CancellationToken,Task<int?>>)counter.CountInputTokensAsync:null,
                            ticket.Token,budget.Known || budgetOverride>=1024);
                        Log.Message("[MapGenAI] Conversation input "+prepared.InputTokens+"/"+effectiveBudget+(prepared.ExactCount?" tokens":" estimated tokens")+
                            "; budget="+budget.Source+"; summary calls="+prepared.SummaryCalls+"; retained messages="+historySnapshot.Count);
                        result=await StructuredChat.SendAsync(async (malformed, token)=>
                        {
                            var attempt=ConversationMemory.Copy(prepared.Messages);
                            if(malformed!=null)
                            {
                                Log.Warning("[MapGenAI] Retrying malformed chat response format once; no settings applied.");
                                attempt.Add(new ChatMessage("assistant",malformed));
                                attempt.Add(new ChatMessage("user",StructuredChat.RepairInstruction));
                                // Repair text can itself exceed a small window. Any checkpoint here
                                // is temporary: rejected output must not enter the dialog's memory.
                                var repair=await ConversationMemory.PrepareAsync(attempt,systemPrompt,null,effectiveBudget,
                                    (messages,prompt,ct)=>client.SendChatAsync(messages,prompt,ct),
                                    client is IChatTokenCounter repairCounter?(Func<List<ChatMessage>,string,System.Threading.CancellationToken,Task<int?>>)repairCounter.CountInputTokensAsync:null,
                                    token,budget.Known || budgetOverride>=1024);
                                attempt=repair.Messages;
                            }
                            return await client.SendChatAsync(attempt,systemPrompt,token);
                        },ticket.Token,requireRecommendations,response=>EditIntentGuard.Rejection(historySnapshot,response));
                        error=null;
                        break;
                    }
                    catch (OperationCanceledException) when (ticket.Token.IsCancellationRequested) { return; }
                    catch (OperationCanceledException) { error = "요청 시간이 초과되었습니다. 다시 시도해 주세요. / Request timed out."; }
                    catch (Exception e) { error=e.Message; }
                }
                _requests.Complete(ticket,result,error,prepared);
            });
        }

        private void HandleResponse(string response)
        {
            _isWaiting = false;
            if (response == null)
            {
                _statusText = "MapGenAI_NoResponse".Translate();
                return;
            }

            Log.Message($"[MapGenAI] HandleResponse 원문: {response}");

            try
            {
                if(_requestedCandidates!=null && (!ReferenceEquals(_requestedCandidates,_recommendations) || !RecommendationsCurrent()))
                    throw new FormatException(IsKorean()?"추천 후 설정이 바뀌어 응답을 적용하지 않았습니다.":"Settings changed; the candidate response was discarded.");
                var parsed = ProviderResponse.Command(response);
                string feedbackProblem=RecommendationFeedback.ResponseProblem(_requestedCandidateNumber,response);
                if(feedbackProblem!=null)throw new FormatException(feedbackProblem);
                var action = parsed.GetString("action");
                Log.Message($"[MapGenAI] 파싱된 action: {action}");

                if (action == "recommend")
                {
                    if(_explanationOnly)throw new FormatException("Expected a conflict explanation, not new recommendations");
                    try
                    {
                        var plans=RecommendationPlan.Validate(parsed,MapGenParams.CaptureState(_openedTileId),
                            data=>MapGenParams.ValidatePatch(data,_openedTileId),IsKorean(),DefinitionText);
                        _recommendations=plans;_recommendationState=RecommendationState();
                        _recommendationPreviews?.Dispose();
                        _recommendationPreviews=null; _previewError=null;
                        try { _recommendationPreviews = new RecommendationPreviews(_openedTileId, plans, MapGenParams.CaptureState(_openedTileId)); }
                        catch (Exception error) { _previewError = error.Message; Log.Warning("[MapGenAI] Candidate preview unavailable: " + error); }
                        string message=(IsKorean()?"현재 타일과 설정에 맞춰 추천했어요. 아래에서 하나를 골라 주세요. 아직 맵은 바뀌지 않았습니다. 번호를 입력하거나 버튼을 누르면 선택한 설정을 적용합니다. 선택 전에는 “3번 통로를 자연스럽게”처럼 후보를 수정할 수 있어요.":"Here are options for your current tile and settings. Choose one below. Your map has not changed yet. Enter a number or use its button to apply that option. Before selecting, you can refine it: e.g. “Make option 3’s passage more natural.”");
                        for(int i=0;i<plans.Count;i++)message+="\n\n"+(IsKorean()?(i+1)+"번 — ":"Option "+(i+1)+" — ")+RecommendationFeedback.Headline(plans[i].Summary,IsKorean())+"\n"+plans[i].Summary;
                        if(plans.Any(p=>p.Command.Contains("\"structure_ops\"")))
                            message+=IsKorean()?"\n\n구조물의 실제 배치는 맵 생성 때 확인합니다.":"\n\nActual structure placement is checked during generation.";
                        _history.Add(new ChatMessage("assistant",message));
                        _llmContext.Add(new ChatMessage("assistant",response));_statusText="";
                    }
                    catch(FormatException error)
                    {
                        if(_requestedCandidates==null)ClearRecommendations();
                        var repair=_repairRecommendations;_repairRecommendations=null;
                        if(repair!=null){Log.Warning("[MapGenAI] Recommendations rejected before display: "+error.Message);repair(response,error.Message);return;}
                        throw;
                    }
                }
                else if(action=="revise")
                {
                    var wrongTarget=EditIntentGuard.Rejection(_llmContext,response);
                    if(wrongTarget!=null)throw new FormatException(wrongTarget);
                    if(_explanationOnly || _recommendations==null || !RecommendationsCurrent())throw new FormatException("No current candidate to revise");
                    int number=parsed.GetInt("option");
                    var before=MapGenParams.CaptureState(_openedTileId);
                    var revised=RecommendationPlan.Refine(_recommendations,number,parsed.GetObject("params"),before,
                        edits=>MapGenParams.ValidatePatches(edits,_openedTileId),IsKorean(),DefinitionText);
                    // Build the replacement snapshot before publishing the new plan.
                    _recommendationPreviews?.Replace(number-1,revised,before);
                    _recommendations[number-1]=revised;
                    _history.Add(new ChatMessage("assistant",(IsKorean()?number+"번 후보만 수정했습니다. 아직 맵에는 적용하지 않았습니다.\n":
                        "Revised option "+number+" only. Your map has not changed yet.\n")+revised.Summary));
                    _llmContext.Add(new ChatMessage("assistant",response));_statusText="";
                }
                else if (action == "ask")
                {
                    string askMsg = parsed.GetString("message");
                    if (string.IsNullOrWhiteSpace(askMsg)) throw new FormatException("Missing clarification message");
                    if(RecommendationPlan.IsNumberedOffer(askMsg))
                    {
                        var repair=_repairRecommendations;_repairRecommendations=null;
                        if(repair!=null){repair(response,"Numbered recommendations require executable options, not unvalidated prose");return;}
                        throw new FormatException(IsKorean()?"실행할 설정이 없는 추천을 받았습니다. 추천을 다시 요청해 주세요.":"Recommendations lacked executable settings. Please request new options.");
                    }
                    _history.Add(new ChatMessage("assistant", askMsg));
                    _llmContext.Add(new ChatMessage("assistant", askMsg)); // ask는 컨텍스트 유지
                    _statusText = "";
                }
                else if (action == "generate")
                {
                    var wrongTarget=EditIntentGuard.Rejection(_llmContext,response);
                    if(wrongTarget!=null)throw new FormatException(wrongTarget);
                    if(_requestedCandidates!=null)throw new FormatException(IsKorean()?"후보 수정은 아직 맵에 적용하지 않습니다. 번호를 지정해 수정을 다시 요청해 주세요.":"Candidate edits must remain proposals. Request a revision by option number.");
                    if(_explanationOnly)throw new FormatException(IsKorean()?"충돌 안내 대신 다른 변경 명령을 받아 적용하지 않았습니다. 유지할 특징이나 교체할 특징을 명시해 주세요.":"Expected a conflict explanation; no alternative edit was applied. Specify which features to keep or replace.");
                    MapParamsData data;
                    try { data=ParseParams(parsed.GetObject("params"));MapGenParams.ValidatePatch(data,_openedTileId); }
                    catch(FormatException invalid)
                    {
                        var explain=_explainInvalidReply;_explainInvalidReply=null;
                        if(explain!=null)
                        {
                            Log.Warning("[MapGenAI] Edit preflight rejected; requesting one explanation without applying: "+invalid.Message);
                            explain(response,invalid.Message);return;
                        }
                        throw;
                    }

                    ApplyEdits(new[]{data},SimpleJson.Serialize(parsed.Values));
                }
                else throw new FormatException("Unsupported response action: " + action);
                _explainInvalidReply=null;_repairRecommendations=null;_requestedCandidates=null;
            }
            catch (Exception e)
            {
                Log.Warning("[MapGenAI] Response rejected: " + e.Message);
                _explainInvalidReply=null;_repairRecommendations=null;
                if(_requestedCandidates==null && _recommendations==null)ClearRecommendations();
                _requestedCandidates=null;
                _llmContext.Add(new ChatMessage("assistant","NOT APPLIED\n"+e.Message));
                _history.Add(new ChatMessage("assistant",
                    (IsKorean() ? "응답을 적용하지 못했습니다: " : "Response was not applied: ") + e.Message));
                _statusText = "";
            }
        }

        private bool RecommendationsCurrent()=>_recommendations!=null && RecommendationState()==_recommendationState &&
            (_recommendationPreviews==null || _recommendationPreviews.ContextMatches());

        private void ApplyEdits(IReadOnlyList<MapParamsData> edits,string command=null)
        {
            MapGenParams.ValidatePatches(edits,_openedTileId);
            var warnings = new List<string>();

            var previous = MapGenAIWorldComponent.Get()?.GetState(_openedTileId)?.Clone();
            var before = previous ?? new TileMapState();
            var proposed = before;
            foreach(var edit in edits)proposed=MapStateEditor.Merge(proposed,edit);
            var changes = MapStateCodec.ChangedFields(before,proposed);
            // Backend rejects the whole response before any state or undo history changes.
            var oldFeatures=Find.WorldGrid[_openedTileId].Mutators.Select(m=>m.defName).ToList();
            MapGenParams.ApplyPatches(edits, _openedTileId);
            ClearRecommendations();
            string desc;
            if (changes.Count == 0)
                desc = IsKorean() ? "변경된 설정이 없습니다. 지원되는 항목과 요청 내용을 확인해 주세요." : "No settings changed. Check the request and supported features.";
            else
            {
                _paramStack.Push(previous);
                _paramsReady = true;
                desc = (IsKorean()?"변경한 내용:\n":"Changes applied:\n")+new MapPlanDescription(IsKorean(),DefinitionText).Describe(before,MapGenParams.CaptureState(_openedTileId));
                var currentFeatures=Find.WorldGrid[_openedTileId].Mutators.Select(m=>m.defName).ToList();
                var actualAdded=currentFeatures.Except(oldFeatures).ToList();var actualRemoved=oldFeatures.Except(currentFeatures).ToList();
                var names=new MapPlanDescription(IsKorean(),DefinitionText);
                if(actualAdded.Count>0)desc+="\n"+(IsKorean()?"실제 추가된 특징: ":"Features added: ")+string.Join(", ",actualAdded.Select(n=>names.Name("feature",n)));
                if(actualRemoved.Count>0)desc+="\n"+(IsKorean()?"실제 제거된 특징: ":"Features removed: ")+string.Join(", ",actualRemoved.Select(n=>names.Name("feature",n)));
                if (!string.IsNullOrEmpty(MapGenParams.LastApplyWarning)) warnings.Add(MapGenParams.LastApplyWarning);
            }

            // 경고 메시지가 있으면 채팅에 추가
            string warningText = "";
            if (warnings.Count > 0)
            {
                warningText = "\n\n" + string.Join("\n", warnings);
                Log.Message($"[MapGenAI] 검증 경고 {warnings.Count}건: {string.Join("; ", warnings)}");
            }

            _history.Add(new ChatMessage("assistant",
                $"{desc}{warningText}\n\n{"MapGenAI_ModifyHint".Translate()}"));
            _llmContext.Add(new ChatMessage("assistant",(changes.Count>0?"APPLIED\n":"NO CHANGE\n")+(command??"{}")+
                "\nActual changes: "+desc+warningText+"\nSettings accepted only; actual generation may still report placement failures."));
            _statusText = "";
        }

        private void ClearRecommendations()
        {
            _feedback?.Close();
            _recommendationPreviews?.Dispose(); _recommendationPreviews=null; _previewError=null;
            _recommendations=null;_recommendationState=null;
        }

        private void CancelCandidateRequest()
        {
            _requests.Cancel();
            _isWaiting=false; _statusText="";
            _requestedCandidates=null;
            _repairRecommendations=null; _explainInvalidReply=null;
            _recommendationsRequested=false; _recommendationRepairUsed=false; _explanationOnly=false;
            ClearRecommendations();
        }

        private void DismissRecommendations()
        {
            if (_recommendations == null) return;
            CancelCandidateRequest();
            var message = new ChatMessage("assistant", IsKorean()?
                "추천을 모두 취소했습니다. 맵 설정은 그대로입니다. 새 요청을 입력하거나 다시 추천받을 수 있습니다.":
                "All suggestions discarded. Your map settings are unchanged. Enter a new request or ask for more suggestions.");
            _history.Add(message);
            _llmContext.Add(message);
        }

        private void RequestNewRecommendations()
        {
            if (_recommendations == null) return;
            CancelCandidateRequest();
            // Preserve the draft and conversation preferences, but plan against the actual map.
            SendText(IsKorean()?"이 추천들은 마음에 안 들어. 현재 맵에 맞게 다른 선택지로 다시 추천해 줘.":
                "I don't like these suggestions. Recommend different options for my current map.");
        }

        private void OpenRecommendationGuide()
        {
            if (_closed || _isWaiting || _guide != null || _feedback != null) return;
            var current=MapGenParams.CaptureState(_openedTileId);
            var tile=Find.WorldGrid[_openedTileId];
            bool knownWater=FeaturePolicy.HasRiver(tile) || FeaturePolicy.WaterNeighbors(tile).Count>0 ||
                tile.Mutators.Any(m=>m.categories.Contains("Lake") && !current.removeMutators.Contains(m.defName) && !current.removeFeatureCategories.Contains("Lake")) ||
                current.mutators.Any(n=>DefDatabase<TileMutatorDef>.GetNamedSilentFail(n)?.categories.Contains("Lake")==true) ||
                current.elevationShapes.Any(s=>IsGuideWater(s.fill) || (s.compositeOps?.Any(o=>o.op=="add" && IsGuideWater(o.fill))==true));
            _guide = new Dialog_RecommendationGuide(IsKorean(),request =>
            {
                if (_closed || _isWaiting) return;
                // Only explicit confirmation retires old candidates. Cancel/Back do not touch the map,
                // chat draft, conversation or Undo. Reuse the normal request against the latest state.
                CancelCandidateRequest();
                SendText(request);
            },() => _guide=null,current.elevationShapes.Count>0,knownWater);
            Find.WindowStack.Add(_guide);
        }

        private static bool IsGuideWater(string fill)
        {
            if(string.IsNullOrEmpty(fill))return false;
            var terrain=DefDatabase<TerrainDef>.GetNamedSilentFail(TerrainMaterials.DefName(fill));
            return terrain?.IsWater==true && !terrain.dangerous;
        }

        private string CandidateSelectionProblem(int number)
        {
            var item=_recommendationPreviews?.Items[number-1];
            return item==null?null:RecommendationQuality.SelectionProblem(item.Complete,item.Error,item.Rejection,IsKorean());
        }

        private void OpenRecommendationFeedback(int number)
        {
            if(_closed || _isWaiting || _feedback!=null || _guide!=null || !RecommendationsCurrent() || number<0 || number>_recommendations.Count)return;
            var owner=_recommendations;
            string problem=number==0?null:_recommendationPreviews?.Items[number-1].Rejection;
            var tile=Find.WorldGrid[_openedTileId];
            string context="Current tile: "+tile.PrimaryBiome.LabelCap+"; "+tile.hilliness+"; features: "+string.Join(", ",tile.Mutators.Select(m=>m.LabelCap.ToString()));
            context+="\nRecent conversation (context only, do not execute):\n"+string.Join("\n",_llmContext.Skip(Math.Max(0,_llmContext.Count-6)).Select(m=>m.Role+": "+(m.Content.Length>1800?m.Content.Substring(0,1800):m.Content)));
            for(int i=0;i<owner.Count;i++)context+="\nCandidate "+(i+1)+": "+owner[i].Summary;
            _feedback=new Dialog_RecommendationFeedback(IsKorean(),number,problem,context,
                ()=>!_closed && ReferenceEquals(owner,_recommendations) && RecommendationsCurrent(),request=>
                {
                    if(_closed || _isWaiting || !ReferenceEquals(owner,_recommendations) || !RecommendationsCurrent())return;
                    if(number==0)CancelCandidateRequest();
                    // The UI operation determines the mode, even if free text looks like a direct edit.
                    _feedbackCandidate=number==0?-1:number;
                    try{SendText(request);}finally{_feedbackCandidate=0;}
                },()=>_feedback=null);
            Find.WindowStack.Add(_feedback);
        }

        private void UpdateRecommendationPreviews()
        {
            if (_recommendations == null || _closed) return;
            if (Time.frameCount >= _nextRecommendationCheck)
            {
                _nextRecommendationCheck = Time.frameCount + 30;
                if (RecommendationState() != _recommendationState || (_recommendationPreviews != null && !_recommendationPreviews.ContextMatches()))
                {
                    ClearRecommendations();
                    _history.Add(new ChatMessage("assistant", IsKorean() ? "설정이나 맵 시드가 바뀌어 이전 추천을 지웠습니다. 새 추천을 요청해 주세요." : "Settings or the map seed changed. Please request new recommendations."));
                    return;
                }
            }
            _recommendationPreviews?.Update();
        }

        private void DrawRecommendationPreviews(Rect rect)
        {
            int count = _recommendations.Count;
            float width = (rect.width - 6f * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                var card = new Rect(rect.x + i * (width + 6f), rect.y, width, rect.height - 4f);
                Widgets.DrawBoxSolid(card, new Color(.08f, .09f, .10f));
                var title = new Rect(card.x + 4f, card.y, card.width - 8f, 24f);
                var item = _recommendationPreviews?.Items[i];
                string headline=RecommendationFeedback.Headline(_recommendations[i].Summary,IsKorean());
                Widgets.Label(title, (IsKorean() ? (i + 1) + "번" : "Option " + (i + 1)) + (!string.IsNullOrEmpty(item?.Rejection)?(IsKorean()?" · 수정 필요":" · Needs revision"):
                    item?.Texture != null ? (IsKorean() ? " · 눌러서 확대" : " · Click to enlarge") : ""));
                TooltipHandler.TipRegion(title,headline);
                var image = new Rect(card.x + 3f, card.y + 24f, card.width - 6f, card.height - 27f);
                if (item?.Texture != null)
                {
                    GUI.DrawTexture(image, item.Texture, ScaleMode.ScaleToFit, false);
                    TooltipHandler.TipRegion(image, _recommendations[i].Summary + (string.IsNullOrEmpty(item.Warning) ? "" : "\n" + item.Warning));
                    if (Widgets.ButtonInvisible(image))
                    {
                        int number = i + 1;
                        var owner = _recommendationPreviews;
                        Find.WindowStack.Add(new Dialog_RecommendationPreview(item, number,
                            () => !_closed && _recommendationPreviews == owner && owner.ContextMatches() && ReferenceEquals(owner.Items[number-1],item), () => ApplyRecommendation(number),
                            ()=>OpenRecommendationFeedback(number),()=>CandidateSelectionProblem(number),headline));
                    }
                }
                else
                {
                    string error = item?.Error ?? _previewError;
                    Widgets.Label(image.ContractedBy(6f), error == null ? (IsKorean() ? "미리보기 생성 중…" : "Rendering preview…") :
                        (IsKorean() ? "미리보기를 만들지 못했습니다.\n설명으로 선택할 수 있습니다." : "Preview unavailable.\nYou can still select using its description."));
                    if (error != null) TooltipHandler.TipRegion(image, error);
                }
            }
        }
        private static PlanDefinition DefinitionText(string kind,string id)
        {
            if(kind=="category")
            {
                var labels=DefDatabase<TileMutatorDef>.AllDefsListForReading.Where(d=>d.categories.Contains(id) && !string.IsNullOrEmpty(d.label)).Select(d=>d.label).Distinct().ToList();
                return labels.Count==0?null:new PlanDefinition(string.Join("·",labels.Take(4))+(labels.Count>4?(IsKorean()?" 등":" and others"):""));
            }
            if(kind=="feature")
            {
                var def=DefDatabase<TileMutatorDef>.GetNamedSilentFail(id);
                return def==null?null:new PlanDefinition(def.label,def.description);
            }
            if(kind=="terrain")
            {
                var def=DefDatabase<TerrainDef>.GetNamedSilentFail(TerrainMaterials.DefName(id));
                return def==null?null:new PlanDefinition(def.label,def.description);
            }
            if(kind=="rock")
            {
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(id);
                return def==null?null:new PlanDefinition(def.label,def.description);
            }
            return null;
        }
        private string RecommendationState()
        {
            var tile=Find.WorldGrid[_openedTileId];
            return MapStateCodec.Serialize(MapGenParams.CaptureState(_openedTileId))+"|"+tile.PrimaryBiome.defName+"|"+tile.hilliness+"|"+
                string.Join(",",tile.Mutators.Select(m=>m.defName).OrderBy(n=>n));
        }
        private void ApplyRecommendation(int number)
        {
            if(_closed || _isWaiting || _feedback!=null || _recommendations==null)return;
            if(number<1 || number>_recommendations.Count)
            { _history.Add(new ChatMessage("assistant",IsKorean()?"목록에 있는 번호를 선택해 주세요.":"Choose a number from the list."));return; }
            if(RecommendationState()!=_recommendationState || (_recommendationPreviews!=null && !_recommendationPreviews.ContextMatches()))
            {
                ClearRecommendations();
                _llmContext.Add(new ChatMessage("assistant","STATE REPLACED\nSettings changed externally. These suggestions are no longer valid; nothing selected."));
                _history.Add(new ChatMessage("assistant",IsKorean()?"추천 후 현재 설정이 바뀌었습니다. 새 추천을 요청해 주세요.":"Settings changed after these options were prepared. Please request new recommendations."));return;
            }
            var plan=_recommendations[number-1];
            string problem=CandidateSelectionProblem(number);
            if(problem!=null)
            {
                _history.Add(new ChatMessage("assistant",(IsKorean()?number+"번은 아직 적용할 수 없습니다. 수정 버튼으로 후보를 고쳐 주세요.\n":"Option "+number+" cannot be applied yet. Use Refine to revise it.\n")+problem));
                return;
            }
            _llmContext.Add(new ChatMessage("user","[UI selection] Apply option "+number+" only."));
            _explanationOnly=false;_explainInvalidReply=null;_repairRecommendations=null;_requestedCandidates=null;
            try
            {
                // A refined candidate applies every revision atomically, not only
                // its original command. Keep that distinction in conversation memory.
                string receipt=plan.Commands.Count==1?plan.Command:
                    "{\"action\":\"applied_plan\",\"commands\":["+string.Join(",",plan.Commands)+"]}";
                ApplyEdits(plan.Edits(),receipt);
            }
            catch(Exception error)
            { _llmContext.Add(new ChatMessage("assistant","NOT APPLIED\n"+error.Message));_history.Add(new ChatMessage("assistant",(IsKorean()?"후보를 적용하지 못했습니다: ":"Could not apply candidate: ")+error.Message)); }
        }

        private MapParamsData ParseParams(SimpleJsonObject obj) => MapParameterParser.Parse(obj);

        private void SaveCurrentPreset(string presetName)
        {
            if (PresetManager.Save(presetName,MapGenParams.CaptureState(_openedTileId)))
                _history.Add(new ChatMessage("assistant", "MapGenAI_PresetSavedMsg".Translate(presetName)));
            else _history.Add(new ChatMessage("assistant", IsKorean() ? "프리셋 저장에 실패했습니다." : "Could not save preset."));
        }

        private void DoUndo()
        {
            if (_paramStack.Count == 0 || _isWaiting) return;

            var prev = _paramStack.Peek();
            if (!TryRestore(prev)) return;
            _paramStack.Pop();
            ClearRecommendations();
            _paramsReady = prev != null;
            _llmContext.Add(new ChatMessage("user","[UI action] Undo the last applied change."));
            _llmContext.Add(new ChatMessage("assistant","STATE REPLACED\nThe last change was undone. Use the latest current map state; do not reapply it."));

            _history.Add(new ChatMessage("assistant",
                IsKorean() ? "이전 상태로 되돌렸습니다." : "Reverted to previous state."));
        }

        private void DoReset()
        {
            ClearRecommendations();
            _requests.Cancel();
            _isWaiting = false;
            _statusText = "";
            if (!TryRestore(_initialSnapshot)) return;
            _paramStack.Clear();
            _llmContext.Clear();
            _conversationMemory=null;
            _paramsReady = _initialSnapshot != null;
            _history.Clear();
            _history.Add(new ChatMessage("assistant", "MapGenAI_Welcome".Translate()));
        }

        private void ShowPresetLoadMenu()
        {
            var presets = PresetManager.ListPresets();
            if (presets.Count == 0)
            {
                _history.Add(new ChatMessage("assistant", "MapGenAI_NoPresets".Translate()));
                return;
            }

            var menuOptions = new List<FloatMenuOption>();
            foreach (var name in presets)
            {
                var presetName = name; // 클로저 캡처용 로컬 변수
                var option = new FloatMenuOption(presetName, () => LoadPreset(presetName));
                option.extraPartWidth = 30f;
                option.extraPartOnGUI = (Rect rect) =>
                {
                    // X 삭제 버튼
                    var xRect = new Rect(rect.x + 5f, rect.y + (rect.height - 20f) / 2f, 20f, 20f);
                    var oldColor = GUI.color;
                    bool xHover = xRect.Contains(Event.current.mousePosition);
                    GUI.color = xHover ? new Color(1f, 0.3f, 0.3f) : new Color(0.6f, 0.3f, 0.3f);
                    Text.Font = GameFont.Small;
                    var oldAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(xRect, "×");
                    Text.Anchor = oldAnchor;
                    GUI.color = oldColor;
                    if (Widgets.ButtonInvisible(xRect))
                    {
                        PresetManager.Delete(presetName);
                        _history.Add(new ChatMessage("assistant", "MapGenAI_PresetDeletedMsg".Translate(presetName)));
                        return true; // 메뉴 닫기
                    }
                    return false;
                };
                menuOptions.Add(option);
            }

            Find.WindowStack.Add(new FloatMenu(menuOptions));
        }

        private void LoadPreset(string presetName)
        {
            var data=PresetManager.Load(presetName);
            if(data==null)
            {
                _history.Add(new ChatMessage("assistant", "MapGenAI_PresetLoadFailed".Translate(presetName)));
                return;
            }
            ClearRecommendations();
            _requests.Cancel();
            _isWaiting=false;
            var before=MapGenAIWorldComponent.Get()?.GetState(_openedTileId)?.Clone();
            if (!TryRestore(data)) return;
            if(MapStateCodec.ChangedFields(before ?? new TileMapState(),data).Count>0) _paramStack.Push(before);
            _llmContext.Add(new ChatMessage("user","[UI action] Load preset: "+presetName));
            _llmContext.Add(new ChatMessage("assistant","STATE REPLACED\nPreset loaded. Earlier settings are superseded by the current map state."));
            _paramsReady=true;
            _statusText="";
            _history.Add(new ChatMessage("assistant", (IsKorean() ? "프리셋을 불러왔습니다: " : "Loaded preset: ") + presetName));
        }

        private void GenerateMap()
        {
            var outcome = AuthoringGeneration.Latest(_openedTileId, MapGenParams.CaptureState(_openedTileId));
            if (outcome?.issues.Count > 0) { _statusText = IsKorean()?"생성 실패 항목을 수정한 뒤 다시 미리보기를 확인해 주세요.":"Fix the generation issues and check the preview again."; return; }
            // "이 설정으로 맵 생성" 클릭 시: 파라미터 유지한 채로 닫기
            _keepParams = true;
            Close();
            Messages.Message("MapGenAI_ParamsSaved".Translate(),
                MessageTypeDefOf.PositiveEvent);
        }

        private bool _keepParams = false;
        private bool _closed;

        private void OpenImageMap()
        {
            if (!MapGenAI.ImageInput.ImageFeatureGate.Enabled) { _statusText = MapGenAI.ImageInput.ImageFeatureGate.Message; return; }
            ClearRecommendations();
            _requests.Cancel(); _isWaiting=false; _statusText="";
            var current=MapGenParams.CaptureState(_openedTileId);
            var features=Find.WorldGrid[_openedTileId].Mutators.Select(m=>m.LabelCap.ToString());
            Find.WindowStack.Add(new Dialog_ImageMap(current.imageMap,current.elevationShapes.Count,ApplyImageMap,string.Join(", ",features)));
        }
        private bool ApplyImageMap(MapGenAI.ImageInput.ImageMapData image)
        {
            if (!MapGenAI.ImageInput.ImageFeatureGate.Enabled) { _statusText = MapGenAI.ImageInput.ImageFeatureGate.Message; return false; }
            if(_closed)return false;
            var before=MapGenAIWorldComponent.Get()?.GetState(_openedTileId)?.Clone();
            var updated=(before??new TileMapState()).Clone();updated.imageMap=image?.Clone();
            if(MapStateCodec.ChangedFields(before??new TileMapState(),updated).Count==0)return true;
            if(!TryRestore(updated))return false;
            ClearRecommendations();
            _paramStack.Push(before);_paramsReady=true;
            _llmContext.Add(new ChatMessage("assistant","STATE REPLACED\nImage settings replaced. Use the latest current map state."));
            _history.Add(new ChatMessage("assistant",MapStateDescription.Describe(before??new TileMapState(),MapGenParams.CaptureState(_openedTileId),IsKorean())+
                (IsKorean()?"\nMap Preview에서 실제 생성 결과를 확인하세요.":"\nInspect the generated result in Map Preview.")));
            return true;
        }

        private bool TryRestore(TileMapState state)
        {
            try
            {
                MapGenParams.RestoreSnapshot(state, _openedTileId);
                if (!string.IsNullOrEmpty(MapGenParams.LastApplyWarning))
                    _history.Add(new ChatMessage("assistant", MapGenParams.LastApplyWarning));
                return true;
            }
            catch (Exception error)
            {
                string message = (IsKorean() ? "복원하지 못했습니다: " : "Restore failed: ") + error.Message;
                _history.Add(new ChatMessage("assistant", message));
                Log.Warning("[MapGenAI] " + message);
                return false;
            }
        }

        public override void PostClose()
        {
            _closed=true;
            _guide?.Close();
            ClearRecommendations();
            _requests.Cancel();
            base.PostClose();
            if (!_keepParams && !TryRestore(_initialSnapshot))
                Messages.Message(IsKorean() ? "MapGenAI: 창을 닫았지만 타일 상태 복원에 실패했습니다. 로그를 확인해 주세요." : "MapGenAI: the window closed, but tile restoration failed. See the log.", MessageTypeDefOf.RejectInput);
        }

    }

    /// <summary>
    /// 프리셋 이름 입력 다이얼로그 (모달 팝업)
    /// </summary>
    public class Dialog_PresetName : Window
    {
        private string _name = "";
        private readonly Action<string> _onSave;

        public override Vector2 InitialSize => new Vector2(300f, 140f);

        public Dialog_PresetName(Action<string> onSave)
        {
            _onSave = onSave;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            layer = WindowLayer.Super;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 28f), "MapGenAI_PresetNamePrompt".Translate());

            GUI.SetNextControlName("PresetNameInput");
            _name = Widgets.TextField(new Rect(inRect.x, inRect.y + 34f, inRect.width, 30f), _name);

            // Enter 키로 저장
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Event.current.Use();
                TrySave();
            }

            float btnW = 80f;
            float btnY = inRect.yMax - 36f;
            if (Widgets.ButtonText(new Rect(inRect.width / 2f - btnW - 4f, btnY, btnW, 30f), "MapGenAI_Save".Translate()))
                TrySave();
            if (Widgets.ButtonText(new Rect(inRect.width / 2f + 4f, btnY, btnW, 30f), "MapGenAI_Cancel".Translate()))
                Close();
        }

        private void TrySave()
        {
            if (!string.IsNullOrEmpty(_name.Trim()))
            {
                _onSave(_name.Trim());
                Close();
            }
        }
    }
}
