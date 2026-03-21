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

        // 이 대화에서 열린 타일 ID (닫힐 때 파라미터 리셋 판단용)
        private readonly int _openedTileId;

        // Undo / Reset
        private readonly Stack<MapParamsData> _paramStack = new Stack<MapParamsData>();
        private MapParamsData _initialSnapshot; // dialog 열릴 때 저장

        // LongEventHandler 대신 volatile 필드로 백그라운드→메인 스레드 전달
        private volatile bool _responseReady = false;
        private string _pendingResponse = null;
        private string _pendingError = null;

        private const float InputHeight = 36f;
        private const float SendButtonWidth = 80f;

        public override Vector2 InitialSize => new Vector2(620f, 520f);

        // 바닐라 기본 mutator defName (자동 관리되므로 LLM 목록에서 제외)
        private static readonly HashSet<string> VanillaAutoMutators = new HashSet<string>
            { "Mountain", "Caves", "Coast", "River" };

        /// <summary>
        /// 현재 게임 언어가 한국어인지 확인.
        /// </summary>
        private static bool IsKorean() => L10n.IsKorean();

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
                    categories[catKey].Add("MapGenAI_MutatorLabel".Translate(mut.defName, mut.label, desc));
                }

                if (categories.Count == 0)
                {
                    if (!ModsConfig.OdysseyActive)
                        return "MapGenAI_OdysseyRequired".Translate();
                    return "MapGenAI_NoMutatorsAvailable".Translate();
                }

                var sb = new System.Text.StringBuilder();
                foreach (var kv in categories)
                {
                    sb.AppendLine($"  [{kv.Key}] {string.Join(", ", kv.Value)}");
                }
                return sb.ToString();
            }
            catch
            {
                return "MapGenAI_MutatorLoadFailed".Translate();
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

            // --- 석재 목록 (동적) ---
            string rockList = BuildRockTypeList();

            // --- Layer 2: 구조화된 프롬프트 ---
            // 섹션 1: 역할 (짧게)
            string role = isKo
                ? "당신은 RimWorld 맵 생성 도우미입니다. 유저 요청을 JSON으로 변환합니다."
                : "You are a RimWorld map generation assistant. Convert user requests to JSON.";

            // 섹션 2: JSON 스키마
            string schema = isKo
                ? @"반드시 아래 두 형식 중 하나의 JSON만 출력하세요.

질문/안내: {""action"":""ask"",""message"":""내용""}
맵 생성: {""action"":""generate"",""description"":""맵 설명"",""params"":{...}}

params 스키마:
{""hills"":""left|right|center|edges|top|bottom|none"",""hill_amount"":0.5~1.6,""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""fertility_offset"":-1.0~1.0,""caves"":true|false,""coast_direction"":""auto|north|east|south|west"",""rock_count"":1~15,""rock_types"":[""Granite|Limestone|Marble|Sandstone|Slate""],""ore_density"":0.0~2.5,""ruin_density"":0.0~2.5,""danger_density"":0.0~2.5,""rock_chunks"":true|false,""hill_size"":""small|medium|large"",""hill_smoothness"":""rough|normal|smooth"",""river_direction"":""left|right|up|down|0-360"",""river_position"":""left|center|right|0.0-1.0"",""mutators"":[""defName""],""remove_mutators"":[""defName""],""elevation_shapes"":[{""type"":""slope|radial|split|bump|noise|ring"",""direction"":""left|right|top|bottom|top_left|top_right|bottom_left|bottom_right|0-360"",""strength"":""weak|medium|strong|negative_weak|negative_medium|negative_strong|숫자"",""position"":""center|top_left|top|top_right|left|right|bottom_left|bottom|bottom_right|[x,z]"",""size"":""small|medium|large|0-1"",""gap"":""tiny|small|medium|large"",""fill"":""water""}]}

elevation_shapes 가이드:
모든 shape는 가산 적용(additive)된다. 여러 개를 조합하면 어떤 모양이든 표현 가능.
좌표계: x=0 왼쪽, x=1 오른쪽, z=0 아래, z=1 위. position=""[x,z]""로 정밀 지정.

primitive:
- slope: 전체 맵 경사. direction=높은 쪽(left→왼쪽 높음). 단순 경사에 사용.
- radial: 가장자리 높고 중심 낮음(분지). negative_strength=반전(중심 높음, 주변 낮음).
- split: 축 분할. negative_strength=중앙 산맥, positive_strength=중앙 협곡. direction=축 방향, gap=폭.
- bump: ★핵심 도구★ 원하는 위치에 언덕 또는 호수를 배치. position=""[x,z]""로 정확한 위치 지정.
  negative_strength=움푹한 지형. fill=""water""=호수(크기/위치 자유).
- noise: 펄린 노이즈. 불규칙 자연 지형. size 클수록 큰 덩어리.
- ring: 원형 산맥(도넛). position=중심, size=반경. 분화구/원형 요새.

★복잡한 모양은 bump 여러 개로 조합하라. 좌표를 직접 계산해서 배치.★
별 모양 언덕(5봉): bump x5, 72도 간격 배치
  [0.5,0.85] [0.79,0.41] [0.65,0.09] [0.35,0.09] [0.21,0.41]
U자형 산: bump([0.15,0.5],strong) + bump([0.5,0.15],medium) + bump([0.85,0.5],strong)
L자형 산: bump([0.2,0.8]) + bump([0.2,0.5]) + bump([0.2,0.2]) + bump([0.5,0.2])
초승달 호수: bump([0.5,0.5],fill=water,large) + bump([0.6,0.6],negative_strong,medium)
여러 호수: bump([0.3,0.7],fill=water,small) + bump([0.7,0.3],fill=water,small)
분화구 호수: ring(medium) + bump([0.5,0.5],fill=water,small)
산 위 호수: bump([0.5,0.7],strong) + bump([0.5,0.75],fill=water,small)
hills와 elevation_shapes를 동시에 쓰지 마세요. elevation_shapes가 있으면 hills는 무시됩니다.

추가 파라미터:
- rock_types: 원하는 석재 종류 지정. 바닐라 석재: Granite(화강암), Limestone(석회암), Marble(대리석), Sandstone(사암), Slate(점판암). 예: ""rock_types"":[""Marble"",""Granite""]
- ruin_density: 폐허 밀도 (0.0~2.5, 기본 1.0). 0=폐허 없음, 2.5=매우 많음.
- danger_density: 고대 위험 밀도 (0.0~2.5, 기본 1.0). 0=위험 없음, 2.5=매우 많음.
- rock_chunks: 돌덩어리 생성 여부 (기본 true). false로 설정하면 맵에 돌덩어리가 없음. ""깨끗한 맵"", ""돌 없애줘"", ""바위 없애줘"", ""돌덩어리 없애"", ""깔끔하게"" 요청 시 사용.
- hill_size: 산맥 크기 (small=잘게 쪼개짐, medium=기본, large=거대 산맥). 또는 숫자(0.005~0.1, 기본 0.021).
- hill_smoothness: 산 표면 거칠기 (rough=울퉁불퉁, normal=기본, smooth=매끄러움). 또는 숫자(0.5~6.0, 기본 2.0).
- hill_amount: 전체 고도 오프셋 (0.1~1.6, 기본 1.0). 0.1=완전 평지(강제), 0.5=완만한 평지, 1.6=전체를 높임(산이 많아짐). ""완전 평지"" 요청 시 0.1 사용. 1.0은 기본값(변화 없음).
- river_direction: 강 방향. left/right/up/down 또는 0-360도 각도. 0=위(북), 90=오른쪽(동), 180=아래(남), 270=왼쪽(서). 미지정시 자동.
- river_position: 강 위치. left/right/up/down/center 또는 0.0~1.0 숫자. 좌우 이동은 x축, 상하 이동은 z축으로 자동 처리. 미지정시 중앙.
- straight_river: 일자 강 (true/false). true면 강이 구불거리지 않고 직선으로 흐름. '일자 강', '운하', '직선 강' 요청 시 사용.
- fertility_offset: 비옥도 오프셋 (-1.0~1.0, 기본 0). 양수=기름진 토양 증가(0.5 권장), 음수=감소. '기름진 토양 많이', '비옥한 맵' 등 요청 시 사용."
                : @"Output exactly one of these two JSON formats.

Question/guide: {""action"":""ask"",""message"":""content""}
Map generation: {""action"":""generate"",""description"":""map description"",""params"":{...}}

params schema:
{""hills"":""left|right|center|edges|top|bottom|none"",""hill_amount"":0.5~1.6,""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""fertility_offset"":-1.0~1.0,""caves"":true|false,""coast_direction"":""auto|north|east|south|west"",""rock_count"":1~15,""rock_types"":[""Granite|Limestone|Marble|Sandstone|Slate""],""ore_density"":0.0~2.5,""ruin_density"":0.0~2.5,""danger_density"":0.0~2.5,""rock_chunks"":true|false,""hill_size"":""small|medium|large"",""hill_smoothness"":""rough|normal|smooth"",""river_direction"":""left|right|up|down|0-360"",""river_position"":""left|center|right|0.0-1.0"",""mutators"":[""defName""],""remove_mutators"":[""defName""],""elevation_shapes"":[{""type"":""slope|radial|split|bump|noise|ring"",""direction"":""left|right|top|bottom|top_left|top_right|bottom_left|bottom_right|0-360"",""strength"":""weak|medium|strong|negative_weak|negative_medium|negative_strong|number"",""position"":""center|top_left|top|top_right|left|right|bottom_left|bottom|bottom_right|[x,z]"",""size"":""small|medium|large|0-1"",""gap"":""tiny|small|medium|large"",""fill"":""water""}]}

elevation_shapes guide:
All shapes are applied additively. Combine multiple shapes to express any terrain.
Coordinate system: x=0 left, x=1 right, z=0 bottom, z=1 top. Use position=""[x,z]"" for precise placement.
Primitives:
- slope: Full-map slope. direction=high side (left→left side higher).
- radial: Edges high, center low (basin). Use negative_strength to invert.
- split: Axis split. negative_strength=center mountain range, positive_strength=center canyon.
- bump: ★KEY TOOL★ Place a hill or lake anywhere. position=""[x,z]"". fill=""water""=lake.
- noise: Perlin noise for irregular natural terrain.
- ring: Circular mountain range (donut). For craters/circular fortresses.
★For complex shapes, combine multiple bumps with calculated coordinates.★
Star-shaped hills (5 peaks): bump x5 at 72° intervals: [0.5,0.85][0.79,0.41][0.65,0.09][0.35,0.09][0.21,0.41]
U-shaped mountain: bump([0.15,0.5]) + bump([0.5,0.15]) + bump([0.85,0.5])
L-shaped mountain: bump([0.2,0.8]) + bump([0.2,0.5]) + bump([0.2,0.2]) + bump([0.5,0.2])
Crescent lake: bump([0.5,0.5],fill=water,large) + bump([0.6,0.6],negative_strong,medium)
Multiple lakes: bump([0.3,0.7],fill=water,small) + bump([0.7,0.3],fill=water,small)
Crater lake: ring(medium) + bump([0.5,0.5],fill=water,small)
Lake on mountain: bump([0.5,0.7],strong) + bump([0.5,0.75],fill=water,small)
Do not use hills and elevation_shapes together. If elevation_shapes is present, hills is ignored.

Additional parameters:
- rock_types: Specify desired rock types. Vanilla rocks: Granite, Limestone, Marble, Sandstone, Slate. Example: ""rock_types"":[""Marble"",""Granite""]
- ruin_density: Ruin density (0.0~2.5, default 1.0). 0=no ruins, 2.5=very many.
- danger_density: Ancient danger density (0.0~2.5, default 1.0). 0=no dangers, 2.5=very many.
- rock_chunks: Whether to generate rock chunks (default true). Set false for no rock chunks on the map. Use for ""clean map"", ""remove rocks"", ""no rocks"", ""remove boulders"", ""clear terrain"" requests.
- hill_size: Mountain size (small=fragmented, medium=default, large=huge mountains). Or a number (0.005~0.1, default 0.021).
- hill_smoothness: Mountain surface roughness (rough=jagged, normal=default, smooth=smooth). Or a number (0.5~6.0, default 2.0).
- hill_amount: Global elevation offset (0.1~1.6, default 1.0). 0.1=completely flat (forced), 0.5=gently flattened, 1.6=raises entire terrain (more mountains). Use 0.1 for ""completely flat"", ""flat terrain"", ""remove hills"", ""no mountains"" requests. 1.0 is default (no change).
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

            // 섹션 4: 규칙
            string rules = isKo
                ? @"규칙:
- [최우선] elevation_shapes 수정 시: ①추가=기존 목록 전체 복사+새 항목 추가 ②특정 항목 제거=제거할 항목을 빼고 나머지만 출력 ③전체 제거=elevation_shapes:[] ※출력에 없는 항목은 삭제됩니다.
- 완전 평지/언덕 없애/산 없애 요청(평평하게, 언덕 없애, 민둥하게, 평지로): hills:none + hill_amount:0.1 + elevation_shapes:[] 조합을 반드시 사용하세요. hills:none만으로는 기반 지형 노이즈가 남아 실제로 평평하지 않습니다.
- ""바위"" 요청 구분: 돌덩어리/바위조각(지면에 흩어진 바위) = rock_chunks:false / 바위 지형/언덕/산(솟아오른 지형) = hill_amount:0.5 + hills:none
- 지형 형태 요청(링 형태, 대각선, 경사면, 호수 등)에는 반드시 elevation_shapes를 사용하세요. hills는 단순 요청에만.
  링/도넛=ring, 산맥=split+negative_strong+gap:medium(산 폭), 협곡=split+strong+gap:small(골짜기 폭), 호수=bump+fill:water, 경사면=slope
- mutators 배열에는 위 목록의 defName만 사용하세요. 목록에 없는 것은 추가할 수 없습니다.
- 목록에 없는 동물/특수 지형을 요청받으면, 비슷한 것으로 대체하지 말고 action=ask로 해당 기능이 없다고 솔직하게 안내하세요.
- 유저에게 설명할 때는 반드시 모든 내용을 한국어로 설명하세요. defName, 영어 파라미터명, 영어 label을 그대로 보여주지 마세요.
- 유저가 한국어로 요청하면 영어 defName/label과 매칭하세요 (예: 오아시스→Oasis, 코코아 나무→WildTropicalPlants).
- 불가능한 요청에는 action=ask로 솔직하게 안내하세요.
- 타일에 강이 없으면(강=없음) 강 관련 요청(river_direction, river_position, straight_river, 강 추가 등)은 action=ask로 '이 타일에는 강이 없습니다. 세계지도에서 강이 있는 타일을 선택하세요.'라고 안내하세요.
- generate 시 description에 유저가 이해할 수 있는 한국어 맵 설명을 포함하세요.
- 유저가 요청하지 않은 파라미터는 JSON에 포함하지 마세요. 기본값을 유지하려면 해당 키를 생략하세요. 특히 rock_types, ore_density, ruin_density, danger_density 등은 요청 시에만 포함.
- 동굴 추가=caves:true, 동굴 제거=caves:false (명시적으로 설정).
- 석재 요청(대리석으로만, 화강암 많이 등)은 rock_types로 처리. rock_count와 rock_types를 동시에 쓸 수 있음.
- 폐허 많이/적게 요청은 ruin_density, 고대 위험은 danger_density로 조절.
- 온천/간헐천 추가는 반드시 mutators:[""HotSprings""]를 사용하세요. geysers 파라미터는 사용하지 마세요.
- 기름진 토양(비옥한 토양) 증감 요청은 fertility_offset으로 처리. 양수=기름진 토양 증가, 음수=감소.
- 구체적이지 않은 요청(""동물 서식지 추가"", ""특수 지형 추가"" 등)에는 action=ask로 구체적으로 어떤 것을 원하는지 목록에서 골라달라고 물어보세요."
                : @"Rules:
- [TOP PRIORITY] When modifying elevation_shapes: ①Add=copy entire existing list + append new items ②Remove specific item=output list WITHOUT that item ③Remove all=elevation_shapes:[] Items not in output are deleted.
- For flat/no-hills/no-mountains requests (make flat, remove hills, remove mountains, no mountains): use hills:none + hill_amount:0.1 + elevation_shapes:[] combination. hills:none alone leaves the base terrain noise — the map is NOT actually flat without hill_amount:0.1.
- ""rock"" disambiguation: loose rocks/boulders (scattered stones on ground) = rock_chunks:false / rocky terrain/hills/mountains (elevated ground) = hill_amount:0.5 + hills:none
- For terrain shape requests (ring, diagonal, slope, lake, etc.), always use elevation_shapes. Use hills only for simple requests.
  Ring/donut=ring, mountain range=split+negative_strong+gap:medium(mountain width), canyon=split+strong+gap:small(valley width), lake=bump+fill:water, slope=slope
- Only use defNames from the above list in the mutators array. You cannot add anything not in the list.
- If the user requests animals/special terrain not in the list, do not substitute something similar. Use action=ask to honestly inform them the feature is unavailable.
- Always respond to the user in English. Do not show raw defNames, parameter names, or labels directly.
- Match the user's natural language to the correct English defName/label (e.g., hot springs=HotSprings, marble=Marble).
- For impossible requests, use action=ask to honestly inform the user.
- If the tile has no river (River=None), reject river-related requests (river_direction, river_position, straight_river, add river, etc.) with action=ask: 'This tile has no river. Please select a tile with a river on the world map.'
- When generating, include a user-friendly English map description in the description field.
- Do not include parameters the user did not request. Omit keys to keep defaults. Especially rock_types, ore_density, ruin_density, danger_density should only be included when requested.
- Add caves=caves:true, remove caves=caves:false (set explicitly).
- Rock requests (marble only, lots of granite, etc.) use rock_types. rock_count and rock_types can be used together.
- More/fewer ruins=ruin_density, ancient dangers=danger_density.
- To add hot springs/geysers, always use mutators:[""HotSprings""]. Do not use a geysers parameter.
- Rich soil (fertile soil) adjustments use fertility_offset. Positive=more rich soil, negative=less.
- For vague requests (""add animal habitat"", ""add special terrain"", etc.), use action=ask to ask the user to specify exactly what they want from the list.";

            // 섹션 5: few-shot 예시
            string fewShot;
            if (!isCoastal)
            {
                // 내륙 타일: elevation_shapes 예시 + 해안 거절 예시
                fewShot = isKo
                    ? @"
예시1) 유저: ""산 많고 온천 있는 맵 만들어줘""
응답: {""action"":""generate"",""description"":""산이 많고 온천이 있는 맵"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

예시2) 유저: ""온천이 있는 산악 요새""
응답: {""action"":""generate"",""description"":""산으로 둘러싸인 요새 형태에 온천이 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""radial"",""strength"":""strong"",""size"":""medium""}],""mutators"":[""HotSprings""]}}

예시3) 유저: ""가운데 호수 있는 맵""
응답: {""action"":""generate"",""description"":""중앙에 호수가 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""bump"",""position"":""center"",""size"":""large"",""strength"":""negative_strong"",""fill"":""water""}]}}

예시4) 유저: ""대리석으로만 된 맵, 폐허 많이""
응답: {""action"":""generate"",""description"":""대리석만 있고 폐허가 많은 맵"",""params"":{""rock_types"":[""Marble""],""ruin_density"":2.0}}

예시5) 유저: ""대각선 산맥 하나 있는 맵""
응답: {""action"":""generate"",""description"":""좌상-우하 방향으로 대각선 산맥이 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""split"",""direction"":""top_left"",""strength"":""negative_strong"",""gap"":""medium""}]}}

예시6) 유저: ""완전 평지로 만들어줘""
응답: {""action"":""generate"",""description"":""완전히 평평한 맵"",""params"":{""hills"":""none"",""hill_amount"":0.1,""elevation_shapes"":[]}}

예시7) 유저: ""피요르드 있는 맵 만들어줘""
응답: {""action"":""ask"",""message"":""현재 타일은 해안가가 아닙니다. 피요르드를 원하시면 세계지도에서 해안가 타일을 선택해주세요.""}"
                    : @"
Ex1) User: ""Make a map with lots of mountains and hot springs""
Response: {""action"":""generate"",""description"":""A mountainous map with hot springs"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

Ex2) User: ""Mountain fortress with hot springs""
Response: {""action"":""generate"",""description"":""A fortress surrounded by mountains with hot springs"",""params"":{""elevation_shapes"":[{""type"":""radial"",""strength"":""strong"",""size"":""medium""}],""mutators"":[""HotSprings""]}}

Ex3) User: ""Map with a lake in the center""
Response: {""action"":""generate"",""description"":""A map with a lake in the center"",""params"":{""elevation_shapes"":[{""type"":""bump"",""position"":""center"",""size"":""large"",""strength"":""negative_strong"",""fill"":""water""}]}}

Ex4) User: ""Marble only map with lots of ruins""
Response: {""action"":""generate"",""description"":""A map with only marble and lots of ruins"",""params"":{""rock_types"":[""Marble""],""ruin_density"":2.0}}

Ex5) User: ""Map with a diagonal mountain range""
Response: {""action"":""generate"",""description"":""A map with a diagonal mountain range from top-left to bottom-right"",""params"":{""elevation_shapes"":[{""type"":""split"",""direction"":""top_left"",""strength"":""negative_strong"",""gap"":""medium""}]}}

Ex6) User: ""Make it completely flat""
Response: {""action"":""generate"",""description"":""A completely flat map"",""params"":{""hills"":""none"",""hill_amount"":0.1,""elevation_shapes"":[]}}

Ex7) User: ""Make a map with fjords""
Response: {""action"":""ask"",""message"":""This tile is not coastal. To use fjords, please select a coastal tile on the world map.""}";
            }
            else
            {
                // 해안 타일: 기본 + elevation_shapes 예시
                fewShot = isKo
                    ? @"
예시1) 유저: ""산 많고 온천 있는 맵 만들어줘""
응답: {""action"":""generate"",""description"":""산이 많고 온천이 있는 맵"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

예시2) 유저: ""왼쪽에 산, 오른쪽 아래에 호수""
응답: {""action"":""generate"",""description"":""왼쪽에 산이 있고 오른쪽 아래에 호수가 있는 맵"",""params"":{""elevation_shapes"":[{""type"":""slope"",""direction"":""left"",""strength"":""strong""},{""type"":""bump"",""position"":""bottom_right"",""size"":""medium"",""strength"":""negative_strong"",""fill"":""water""}]}}

예시3) 유저: ""그냥 추천해줘""
응답: {""action"":""generate"",""description"":""해안가 바이옴에 어울리는 자연 경관 맵"",""params"":{""hills"":""edges"",""hill_amount"":1.0,""vegetation_density"":1.3,""coast_direction"":""auto""}}

예시4) 유저: ""완전 평지로 만들어줘""
응답: {""action"":""generate"",""description"":""완전히 평평한 맵"",""params"":{""hills"":""none"",""hill_amount"":0.1,""elevation_shapes"":[]}}

예시5) 유저: ""바위 없애줘""
응답: {""action"":""generate"",""description"":""돌덩어리 없는 깨끗한 맵"",""params"":{""rock_chunks"":false}}"
                    : @"
Ex1) User: ""Make a map with lots of mountains and hot springs""
Response: {""action"":""generate"",""description"":""A mountainous map with hot springs"",""params"":{""hills"":""center"",""hill_amount"":1.4,""caves"":true,""mutators"":[""HotSprings""]}}

Ex2) User: ""Mountains on the left, lake on the bottom right""
Response: {""action"":""generate"",""description"":""A map with mountains on the left and a lake in the bottom right"",""params"":{""elevation_shapes"":[{""type"":""slope"",""direction"":""left"",""strength"":""strong""},{""type"":""bump"",""position"":""bottom_right"",""size"":""medium"",""strength"":""negative_strong"",""fill"":""water""}]}}

Ex3) User: ""Just recommend something""
Response: {""action"":""generate"",""description"":""A natural landscape map suited for a coastal biome"",""params"":{""hills"":""edges"",""hill_amount"":1.0,""vegetation_density"":1.3,""coast_direction"":""auto""}}

Ex4) User: ""Make it completely flat""
Response: {""action"":""generate"",""description"":""A completely flat map"",""params"":{""hills"":""none"",""hill_amount"":0.1,""elevation_shapes"":[]}}

Ex5) User: ""Remove the rocks""
Response: {""action"":""generate"",""description"":""A clean map with no scattered boulders"",""params"":{""rock_chunks"":false}}";
            }

            string currentParams = MapGenParams.BuildCurrentParamsText(isKo);

            // 수정 예시: 현재 elevation_shapes가 있으면 수정 방법을 구체적으로 보여줌
            string modExample = "";
            if (MapGenParams.ElevationShapes.Count > 0)
            {
                var existingShapes = MapGenParams.ElevationShapes;
                var firstShape = existingShapes[0];
                // 첫 번째 shape JSON 직렬화
                var fp = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(firstShape.type))      fp.Add($"\"type\":\"{firstShape.type}\"");
                if (!string.IsNullOrEmpty(firstShape.direction)) fp.Add($"\"direction\":\"{firstShape.direction}\"");
                if (!string.IsNullOrEmpty(firstShape.strength))  fp.Add($"\"strength\":\"{firstShape.strength}\"");
                if (!string.IsNullOrEmpty(firstShape.position))  fp.Add($"\"position\":\"{firstShape.position}\"");
                if (!string.IsNullOrEmpty(firstShape.size))      fp.Add($"\"size\":\"{firstShape.size}\"");
                string firstJson = "{" + string.Join(",", fp) + "}";

                // 제거 예시: 첫 번째 shape를 뺀 나머지 목록
                string removalShapesJson;
                if (existingShapes.Count == 1)
                {
                    removalShapesJson = "[]";
                }
                else
                {
                    var others = existingShapes.Skip(1).Select(s => {
                        var p = new System.Collections.Generic.List<string>();
                        if (!string.IsNullOrEmpty(s.type))      p.Add($"\"type\":\"{s.type}\"");
                        if (!string.IsNullOrEmpty(s.direction)) p.Add($"\"direction\":\"{s.direction}\"");
                        if (!string.IsNullOrEmpty(s.strength))  p.Add($"\"strength\":\"{s.strength}\"");
                        if (!string.IsNullOrEmpty(s.position))  p.Add($"\"position\":\"{s.position}\"");
                        if (!string.IsNullOrEmpty(s.size))      p.Add($"\"size\":\"{s.size}\"");
                        return "{" + string.Join(",", p) + "}";
                    });
                    removalShapesJson = "[" + string.Join(",", others) + "]";
                }
                string firstDesc = firstShape.direction ?? firstShape.type ?? "해당";

                modExample = isKo
                    ? $"\n[추가 예시] elevation_shapes에 호수 추가:\n" +
                      $"유저: \"왼쪽 아래에 호수 추가해줘\"\n" +
                      $"응답: {{\"action\":\"generate\",\"description\":\"기존 지형에 왼쪽 아래 호수 추가\",\"params\":{{\"elevation_shapes\":[{firstJson},{{\"type\":\"bump\",\"position\":\"bottom_left\",\"size\":\"medium\",\"strength\":\"negative_strong\",\"fill\":\"water\"}}]}}}}\n" +
                      $"[제거 예시] '{firstDesc}' shape 제거:\n" +
                      $"유저: \"{firstDesc} 지형 없애줘\"\n" +
                      $"응답: {{\"action\":\"generate\",\"description\":\"{firstDesc} 지형 제거\",\"params\":{{\"elevation_shapes\":{removalShapesJson}}}}}"
                    : $"\n[Addition example] Adding a lake to current elevation_shapes:\n" +
                      $"User: \"Add a lake in the bottom left\"\n" +
                      $"Response: {{\"action\":\"generate\",\"description\":\"Added a lake in bottom-left to existing terrain\",\"params\":{{\"elevation_shapes\":[{firstJson},{{\"type\":\"bump\",\"position\":\"bottom_left\",\"size\":\"medium\",\"strength\":\"negative_strong\",\"fill\":\"water\"}}]}}}}\n" +
                      $"[Removal example] Removing '{firstDesc}' shape:\n" +
                      $"User: \"Remove the {firstDesc} terrain\"\n" +
                      $"Response: {{\"action\":\"generate\",\"description\":\"Removed {firstDesc} terrain\",\"params\":{{\"elevation_shapes\":{removalShapesJson}}}}}";
            }

            return $@"{role}

{schema}
{tileContext}
{currentParams}
{rules}
{fewShot}{modExample}";
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
            _initialSnapshot = MapGenParams.HasParams ? MapGenParams.ToSnapshot() : null;

            _history.Add(new ChatMessage("assistant",
                "MapGenAI_Welcome".Translate()));

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
                    _history.Add(new ChatMessage("assistant", "MapGenAI_Error".Translate(_pendingError)));
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

            // 타이틀 바
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            Widgets.DrawBoxSolid(titleRect, new Color(0.15f, 0.35f, 0.55f, 0.95f));
            Text.Font = GameFont.Small;
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            var oldColor = GUI.color;
            GUI.color = new Color(0.9f, 0.95f, 1f);
            Widgets.Label(titleRect, "MapGen AI");
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;

            // 채팅 영역 (타이틀 아래)
            float topOffset = titleRect.yMax + 4f;
            float bottomReserve = InputHeight + 50f;
            var chatRect = new Rect(inRect.x, topOffset, inRect.width, inRect.height - topOffset - bottomReserve + inRect.y);
            DrawChat(chatRect);

            // 입력창 + 전송 버튼
            var inputAreaY = chatRect.yMax + 8f;
            var inputRect = new Rect(inRect.x, inputAreaY, inRect.width - SendButtonWidth - 8f, InputHeight);
            var sendRect = new Rect(inputRect.xMax + 8f, inputAreaY, SendButtonWidth, InputHeight);

            // Enter 키: 항상 소비 (다른 Window로 전달 방지 → Map Preview 보호)
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Event.current.Use();
                if (!_isWaiting && !string.IsNullOrEmpty(_inputText))
                    SendMessage();
            }

            GUI.SetNextControlName("ChatInput");
            _inputText = Widgets.TextField(inputRect, _inputText);

            // 전송 버튼
            GUI.enabled = !_isWaiting && !string.IsNullOrEmpty(_inputText);
            if (Widgets.ButtonText(sendRect, _isWaiting ? "..." : "MapGenAI_Send".Translate().ToString()))
                SendMessage();
            GUI.enabled = true;

            // 하단: 맵 생성 버튼 + Undo/Reset + 프리셋 버튼 or 상태 텍스트
            var bottomY = sendRect.yMax + 6f;
            float sp = 6f;
            float undoBtnW = 80f;
            float resetBtnW = 80f;
            float presetBtnW = 90f;

            if (_paramsReady)
            {
                float generateBtnW = inRect.width - undoBtnW - resetBtnW - presetBtnW * 2 - sp * 4;
                var generateRect   = new Rect(inRect.x, bottomY, generateBtnW, 36f);
                var undoRect       = new Rect(generateRect.xMax + sp, bottomY, undoBtnW, 36f);
                var resetRect      = new Rect(undoRect.xMax + sp, bottomY, resetBtnW, 36f);
                var presetSaveRect = new Rect(resetRect.xMax + sp, bottomY, presetBtnW, 36f);
                var presetLoadRect = new Rect(presetSaveRect.xMax + sp, bottomY, presetBtnW, 36f);

                if (Widgets.ButtonText(generateRect, "MapGenAI_Generate".Translate()))
                    GenerateMap();

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
                    ? new Color(0.18f, 0.38f, 0.62f, 0.92f)  // 유저: 진한 파랑
                    : new Color(0.18f, 0.20f, 0.22f, 0.92f);  // AI: 어두운 회색

                Widgets.DrawBoxSolid(new Rect(x, y, msgRenderWidth, msgHeight), bgColor);
                Widgets.Label(new Rect(x, y, msgRenderWidth, msgHeight).ContractedBy(6f), msg.Content);
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
            _history.Add(new ChatMessage("user", text));
            _isWaiting = true;
            _statusText = "MapGenAI_Requesting".Translate();
            _paramsReady = false;

            var settings = MapGenAIMod.Settings;
            ILLMClient client;
            try
            {
                var config = settings.GetActiveConfig();
                if (config == null)
                {
                    _statusText = "MapGenAI_Error".Translate("No API configured");
                    _isWaiting = false;
                    return;
                }
                client = LLMClientFactory.Create(config, settings.localBaseUrl);
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 클라이언트 생성 실패: {e}");
                _statusText = "MapGenAI_Error".Translate(e.Message);
                _isWaiting = false;
                return;
            }

            Log.Message($"[MapGenAI] LLM 요청 시작");

            // 전송 전 현재 파라미터 스냅샷 저장 (undo용)
            if (MapGenParams.HasParams)
                _paramStack.Push(MapGenParams.ToSnapshot());

            // 현재 user 메시지 1개만 전달 (현재 파라미터 상태는 system prompt에 포함)
            var singleMessage = new List<ChatMessage> { new ChatMessage("user", text) };
            int tileId = Find.WorldSelector?.SelectedTile ?? -1;
            var systemPrompt = BuildSystemPrompt(tileId);

            Task.Run(async () =>
            {
                string result = null;
                string error = null;
                try
                {
                    Log.Message("[MapGenAI] Task.Run 시작");
                    result = await client.SendChatAsync(singleMessage, systemPrompt);
                    Log.Message($"[MapGenAI] 응답 수신: {(result == null ? "null" : result.Length + "자")}");
                }
                catch (Exception e)
                {
                    Log.Error($"[MapGenAI] API 오류: {e}");
                    // Fallback: 다음 유효한 config 시도
                    if (settings.TryNextConfig())
                    {
                        try
                        {
                            var nextClient = LLMClientFactory.Create(settings.GetActiveConfig(), settings.localBaseUrl);
                            if (nextClient != null)
                            {
                                Log.Message("[MapGenAI] Fallback API 시도");
                                result = await nextClient.SendChatAsync(singleMessage, systemPrompt);
                            }
                            else error = e.Message;
                        }
                        catch (Exception e2)
                        {
                            Log.Error($"[MapGenAI] Fallback 오류: {e2}");
                            error = e2.Message;
                        }
                    }
                    else
                    {
                        error = e.Message;
                    }
                }
                _pendingResponse = result;
                _pendingError = error;
                _responseReady = true;
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
                    var desc = parsed.GetString("description") ?? "MapGenAI_ParamsSet".Translate().ToString();

                    // 경고 메시지가 있으면 채팅에 추가
                    string warningText = "";
                    if (warnings.Count > 0)
                    {
                        warningText = "\n\n" + string.Join("\n", warnings);
                        Log.Message($"[MapGenAI] 검증 경고 {warnings.Count}건: {string.Join("; ", warnings)}");
                    }

                    _history.Add(new ChatMessage("assistant",
                        $"{desc}{warningText}\n\n{"MapGenAI_ModifyHint".Translate()}"));
                    _statusText = "";
                }
            }
            catch
            {
                _history.Add(new ChatMessage("assistant",
                    IsKorean() ? "응답을 처리할 수 없습니다. 다시 시도해 주세요." : "Failed to process response. Please try again."));
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
                    warnings.Add("MapGenAI_MutatorNotFound".Translate(defName));
                    data.mutators.RemoveAt(i);
                    continue;
                }

                bool hasCats = mutDef.categories != null && mutDef.categories.Count > 0;
                string label = mutDef.label ?? defName;

                // 검증 2: Coast 카테고리인데 해안 아닌 타일
                if (!isCoastal && hasCats && mutDef.categories.Contains("Coast"))
                {
                    warnings.Add("MapGenAI_MutatorCoastalOnly".Translate(label));
                    data.mutators.RemoveAt(i);
                    continue;
                }

                // 검증 3: River 카테고리인데 강 없는 타일
                if (!hasRiver && hasCats && mutDef.categories.Contains("River"))
                {
                    warnings.Add("MapGenAI_MutatorRiverOnly".Translate(label));
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
                fertility_offset = obj.GetFloat("fertility_offset", 0f),
                roads = obj.GetBool("roads"),
                caves = obj.GetBool("caves"),
                caves_explicit = obj.GetString("caves") != null,
                geysers = obj.GetInt("geysers", -1),
                coast_direction = obj.GetString("coast_direction") ?? "auto",
                rock_count = obj.GetInt("rock_count", -1),
                ore_density = obj.GetFloat("ore_density", 1f),
                ruin_density = obj.GetFloat("ruin_density", 1f),
                danger_density = obj.GetFloat("danger_density", 1f),
                rock_chunks = obj.GetString("rock_chunks") != null ? obj.GetBool("rock_chunks") : true,
                hill_size = ParseHillSize(obj.GetString("hill_size")),
                hill_smoothness = ParseHillSmoothness(obj.GetString("hill_smoothness")),
                straight_river = obj.GetBool("straight_river")
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
                    direction_angle = riverObj.GetFloat("direction_angle", -1f),
                    x_position = riverObj.GetFloat("x_position", 0.5f),
                    z_position = riverObj.GetFloat("z_position", 0.5f)
                };

            // river_direction / river_position 단축키 지원 (river 객체 없이 직접 지정 가능)
            {
                string rdStr = obj.GetString("river_direction");
                string rpStr = obj.GetString("river_position");
                if (rdStr != null || rpStr != null)
                {
                    if (data.river == null)
                        data.river = new RiverData { present = true };
                    if (rdStr != null) data.river.direction = rdStr;
                    if (rpStr != null)
                    {
                        string rp = rpStr.Trim().ToLower();
                        // up/down은 z축 이동
                        if (rp == "up" || rp == "top")
                            data.river.z_position = 0.8f;
                        else if (rp == "down" || rp == "bottom")
                            data.river.z_position = 0.2f;
                        else
                            data.river.x_position = ParseRiverPosition(rpStr);
                    }
                }
            }

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

            // --- 기존 파라미터 병합: JSON에 없는 필드는 이전 값 유지 ---
            if (MapGenParams.HasParams)
            {
                if (obj.GetString("hills") == null && data.elevation_shapes == null)
                    data.hills = MapGenParams.Hills;
                if (obj.GetString("hill_amount") == null)
                    data.hill_amount = MapGenParams.HillAmount;
                if (obj.GetString("vegetation_density") == null)
                    data.vegetation_density = MapGenParams.VegetationDensity;
                if (obj.GetString("animal_density") == null)
                    data.animal_density = MapGenParams.AnimalDensity;
                if (obj.GetString("fertility_offset") == null)
                    data.fertility_offset = MapGenParams.FertilityOffset;
                if (!data.caves_explicit)
                    data.caves = MapGenParams.HasCaves;
                if (obj.GetString("coast_direction") == null)
                    data.coast_direction = MapGenParams.CoastDirection;
                if (obj.GetString("rock_count") == null)
                    data.rock_count = MapGenParams.RockCount;
                if (obj.GetString("ore_density") == null)
                    data.ore_density = MapGenParams.OreDensity;
                if (obj.GetString("ruin_density") == null)
                    data.ruin_density = MapGenParams.RuinDensity;
                if (obj.GetString("danger_density") == null)
                    data.danger_density = MapGenParams.DangerDensity;
                if (obj.GetString("rock_chunks") == null)
                    data.rock_chunks = MapGenParams.HasRockChunks;
                if (obj.GetString("hill_size") == null)
                    data.hill_size = MapGenParams.HillSize;
                if (obj.GetString("hill_smoothness") == null)
                    data.hill_smoothness = MapGenParams.HillSmoothness;
                if (obj.GetString("straight_river") == null)
                    data.straight_river = MapGenParams.StraightRiver;

                // 강: JSON에 river 관련 키가 하나도 없으면 이전 값 유지
                if (data.river == null && obj.GetString("river_direction") == null
                    && obj.GetString("river_position") == null && obj.GetObject("river") == null)
                {
                    data.river = new RiverData
                    {
                        present = MapGenParams.HasRiver,
                        direction_angle = MapGenParams.RiverDirectionAngle,
                        x_position = MapGenParams.RiverXPosition,
                        z_position = MapGenParams.RiverZPosition
                    };
                }

                // 석재: JSON에 없으면 이전 값 유지
                if (data.rock_types == null && MapGenParams.RockTypes.Count > 0)
                    data.rock_types = new List<string>(MapGenParams.RockTypes);

                // elevation_shapes: JSON에 없으면 이전 값 유지
                if (data.elevation_shapes == null && MapGenParams.ElevationShapes.Count > 0)
                    data.elevation_shapes = new List<ElevationShape>(MapGenParams.ElevationShapes);

                // mutators: JSON에 없으면 이전 값 유지
                if (data.mutators == null && MapGenParams.Mutators.Count > 0)
                    data.mutators = new List<string>(MapGenParams.Mutators);
            }

            return data;
        }

        /// <summary>hill_size 시맨틱 파싱. small/medium/large 또는 숫자.</summary>
        private static float ParseHillSize(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0f; // 0 = 기본값 사용
            val = val.Trim().ToLower();
            switch (val)
            {
                case "small":  return 0.035f; // 큰 산맥 (frequency 높음 = 작은 패턴이지만, Map Designer에서는 반대 해석)
                case "medium": return 0.021f; // 바닐라 기본
                case "large":  return 0.012f; // 거대한 산맥 (낮은 frequency = 큰 패턴)
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
            }
        }

        /// <summary>hill_smoothness 시맨틱 파싱. rough/normal/smooth 또는 숫자.</summary>
        private static float ParseHillSmoothness(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0f; // 0 = 기본값 사용
            val = val.Trim().ToLower();
            switch (val)
            {
                case "rough":  return 1.0f; // 매우 거친
                case "normal": return 2.0f; // 바닐라 기본
                case "smooth": return 3.5f; // 매끄러운
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
            }
        }

        /// <summary>river_position 시맨틱 파싱. left/center/right 또는 0.0-1.0 숫자.</summary>
        private static float ParseRiverPosition(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.5f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "left":   return 0.2f;
                case "center": return 0.5f;
                case "right":  return 0.8f;
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp(f, 0f, 1f) : 0.5f;
            }
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
                    direction_angle = MapGenParams.RiverDirectionAngle,
                    x_position = MapGenParams.RiverXPosition,
                    z_position = MapGenParams.RiverZPosition
                },
                roads = MapGenParams.HasRoads,
                caves = MapGenParams.HasCaves,
                geysers = MapGenParams.GeyserCount,
                coast_direction = MapGenParams.CoastDirection,
                rock_count = MapGenParams.RockCount,
                ore_density = MapGenParams.OreDensity,
                ruin_density = MapGenParams.RuinDensity,
                danger_density = MapGenParams.DangerDensity,
                rock_chunks = MapGenParams.HasRockChunks,
                hill_size = MapGenParams.HillSize,
                hill_smoothness = MapGenParams.HillSmoothness,
                rock_types = MapGenParams.RockTypes.Count > 0
                    ? new List<string>(MapGenParams.RockTypes) : null,
                mutators = new List<string>(MapGenParams.Mutators),
                elevation_shapes = MapGenParams.ElevationShapes.Count > 0
                    ? new List<ElevationShape>(MapGenParams.ElevationShapes) : null
            };

            PresetManager.Save(presetName, data);
            _history.Add(new ChatMessage("assistant", "MapGenAI_PresetSavedMsg".Translate(presetName)));
        }

        private void DoUndo()
        {
            if (_paramStack.Count == 0 || _isWaiting) return;

            var prev = _paramStack.Pop();
            MapGenParams.Apply(prev);
            _paramsReady = true;

            // 마지막 user + assistant 메시지 쌍 제거 (환영 메시지는 유지)
            if (_history.Count >= 3)
                _history.RemoveRange(_history.Count - 2, 2);
            else if (_history.Count == 2)
                _history.RemoveAt(_history.Count - 1);

            _history.Add(new ChatMessage("assistant",
                IsKorean() ? "이전 상태로 되돌렸습니다." : "Reverted to previous state."));
        }

        private void DoReset()
        {
            _paramStack.Clear();

            if (_initialSnapshot != null)
            {
                MapGenParams.Apply(_initialSnapshot);
                _paramsReady = true;
            }
            else
            {
                MapGenParams.Reset();
                _paramsReady = false;
            }

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
            var data = PresetManager.Load(presetName);
            if (data == null)
            {
                _history.Add(new ChatMessage("assistant", "MapGenAI_PresetLoadFailed".Translate(presetName)));
                return;
            }

            MapGenParams.Apply(data);
            _paramsReady = true;
            _statusText = "";
            _history.Add(new ChatMessage("assistant",
                "MapGenAI_PresetLoadedMsg".Translate(
                    presetName, data.hills, data.hill_amount.ToString("F2"),
                    data.vegetation_density.ToString("F1"), data.animal_density.ToString("F1"),
                    (data.river?.present ?? false).ToString(), data.caves.ToString(),
                    data.geysers.ToString())
                + "\n\n" + "MapGenAI_ModifyHint".Translate()));
        }

        private void GenerateMap()
        {
            // "이 설정으로 맵 생성" 클릭 시: 파라미터 유지한 채로 닫기
            _keepParams = true;
            Close();
            Messages.Message("MapGenAI_ParamsSaved".Translate(),
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
                Log.Message($"[MapGenAI] {"MapGenAI_DialogCancelled".Translate()}");
            }
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
