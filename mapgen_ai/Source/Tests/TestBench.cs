using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// MapGen AI 오프라인 테스트벤치.
/// 실제 Gemini API를 호출하여 각 시나리오의 LLM 응답을 검증.
/// dotnet run으로 실행.
/// </summary>
class TestBench
{
    // dev_config.json에서 API 키 로드
    static string ApiKey;
    static string Model;
    static readonly HttpClient Http = new HttpClient();

    // 테스트 케이스 정의
    record TestCase(string Name, string TileContext, string UserMessage, Func<string, bool> Validate, string ExpectDesc);

    static List<TestCase> BuildTestCases()
    {
        return new List<TestCase>
        {
            // ============================================================
            // 기존 12개 케이스
            // ============================================================

            // === 강 방향 테스트 ===
            // #1
            new("강 있는 타일 + 강 요청 → generate 또는 방향제한 안내",
                TileCtx("열대우림", "TropicalRainforest", "Flat", river: true, elevation: 15, coastal: true),
                "왼쪽에서 오른쪽으로 강이 흐르는 맵",
                r => {
                    // generate + river.present=true → 최선
                    if (GetAction(r) == "generate" && (GetJson(r).Contains("\"present\":true") || GetJson(r).Contains("\"present\": true")))
                        return true;
                    // ask로 강 방향 조절 불가 안내 → 프롬프트 규칙에 맞는 정상 응답
                    if (GetAction(r) == "ask" && (GetMessage(r).Contains("강") || GetMessage(r).Contains("방향")))
                        return true;
                    return false;
                },
                "generate(river.present=true) 또는 ask(강 방향 제한 안내)"),

            // #2
            new("강 없는 타일 + 강 요청 → 거절/안내",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: false, elevation: 200, coastal: false),
                "강이 흐르는 맵 만들어줘",
                r => GetAction(r) == "ask" && (GetMessage(r).Contains("강") || GetMessage(r).Contains("타일")),
                "ask + 강 있는 타일 선택 안내"),

            // === 해안 테스트 ===
            // #3
            new("해안 타일 + 바다 요청 → OK (거절 아님)",
                TileCtx("열대우림", "TropicalRainforest", "Flat", river: true, elevation: 1, coastal: true),
                "위쪽에 바다가 있는 맵",
                r => GetAction(r) == "generate" || (GetAction(r) == "ask" && !GetMessage(r).Contains("해안가 타일을 선택")),
                "generate 또는 해안 관련 안내 (해안 타일 선택 안내가 아닌 응답)"),

            // #4
            new("내륙 타일 + 바다 요청 → 거절/안내",
                TileCtx("건조관목림", "AridShrubland", "LargeHills", river: false, elevation: 800, coastal: false),
                "위에 바다가 있는 맵",
                r => GetAction(r) == "ask" && (GetMessage(r).Contains("해안") || GetMessage(r).Contains("바다")),
                "ask + 해안가 타일 선택 안내"),

            // === 추천 테스트 ===
            // #5
            new("추천 요청 → generate + description 포함",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: true, elevation: 150, coastal: false),
                "그냥 추천해줘",
                r => GetAction(r) == "generate" && !string.IsNullOrEmpty(GetDescription(r)),
                "generate + description 비어있지 않음"),

            // === 잘못된 요청 ===
            // #6
            new("산악 타일에서 평지 요청 → 적절한 응답",
                TileCtx("툰드라", "Tundra", "Mountainous", river: false, elevation: 1500, coastal: false),
                "완전히 평평한 맵 만들어줘",
                r => GetAction(r) == "generate" || GetAction(r) == "ask",
                "generate(hills=none) 또는 ask"),

            // === 파라미터 유효성 ===
            // #7
            new("극단적 식생 밀도 요청 → 범위 내 값",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: true, elevation: 150, coastal: false),
                "나무를 미친듯이 많이 넣어줘",
                r => {
                    var json = GetJson(r);
                    if (GetAction(r) != "generate") return true; // ask도 OK
                    var veg = ExtractNumberField(json, "vegetation_density");
                    return veg <= 2.1f;
                },
                "generate + vegetation_density <= 2.0"),

            // #8
            new("동굴 요청 (산악 타일) → caves=true",
                TileCtx("온대림", "TemperateForest", "Mountainous", river: false, elevation: 800, coastal: false),
                "동굴이 많은 맵 만들어줘",
                r => GetAction(r) == "generate" && (GetJson(r).Contains("\"caves\":true") || GetJson(r).Contains("\"caves\": true")),
                "generate + caves=true"),

            // === 해안 추가 ===
            // #9
            new("해안 타일(강 없음) + 바다 요청 → OK",
                TileCtx("초원", "Grasslands", "Flat", river: false, elevation: 9, coastal: true),
                "바다가 보이는 맵 만들어줘",
                r => GetAction(r) == "generate" || (GetAction(r) == "ask" && !GetMessage(r).Contains("해안가 타일을 선택")),
                "generate 또는 긍정적 ask (거절 아님)"),

            // === 한국어 미묘한 표현 ===
            // #10
            new("북유럽 느낌 요청 → 적절한 파라미터",
                TileCtx("아한대림", "BorealForest", "SmallHills", river: true, elevation: 300, coastal: false),
                "북유럽 느낌으로 만들어줘",
                r => GetAction(r) == "generate" && !string.IsNullOrEmpty(GetDescription(r)),
                "generate + description 포함"),

            // === 식생 밀도 불일치 ===
            // #11
            new("사막에서 나무 빽빽 요청 → ask 또는 generate",
                TileCtx("사막", "Desert", "Flat", river: false, elevation: 200, coastal: false),
                "나무가 빽빽한 숲으로 만들어줘",
                r => GetAction(r) == "generate" || GetAction(r) == "ask",
                "generate(높은 vegetation) 또는 ask(바이옴 불일치 안내)"),

            // === JSON 포맷 안정성 ===
            // #12
            new("간단한 한 줄 요청 → 유효한 JSON 응답",
                TileCtx("온대림", "TemperateForest", "Flat", river: false, elevation: 100, coastal: false),
                "그냥 평범한 맵",
                r => GetAction(r) == "generate" || GetAction(r) == "ask",
                "유효한 JSON (action 필드 존재)"),

            // ============================================================
            // 신규 18개 케이스
            // ============================================================

            // === 산 시스템 (3개) ===
            // #13
            new("산 많이 요청 → hill_amount > 1.0",
                TileCtx("온대림", "TemperateForest", "LargeHills", river: false, elevation: 500, coastal: false),
                "산을 엄청 많이 넣어줘, 산악 느낌으로",
                r => {
                    if (GetAction(r) != "generate") return true; // ask도 OK
                    var ha = ExtractNumberField(GetJson(r), "hill_amount");
                    return ha < 0 || ha > 1.0f; // 미포함(-1)이면 기본값이므로 OK, 포함 시 > 1.0
                },
                "generate + hill_amount > 1.0"),

            // #14
            new("산 적게 요청 → hill_amount < 1.0",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: true, elevation: 200, coastal: false),
                "언덕이 거의 없는 평탄한 맵으로 해줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var ha = ExtractNumberField(GetJson(r), "hill_amount");
                    var json = GetJson(r);
                    bool hillsNone = json.Contains("\"hills\":\"none\"") || json.Contains("\"hills\": \"none\"");
                    return ha < 1.0f || hillsNone; // hill_amount 낮거나 hills=none
                },
                "generate + hill_amount < 1.0 또는 hills=none"),

            // #15
            new("왼쪽에 산 + 많이 → hills=left + hill_amount > 1.0",
                TileCtx("온대림", "TemperateForest", "LargeHills", river: false, elevation: 600, coastal: false),
                "왼쪽에 산이 잔뜩 있는 맵",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    bool hillsLeft = json.Contains("\"hills\":\"left\"") || json.Contains("\"hills\": \"left\"");
                    var ha = ExtractNumberField(json, "hill_amount");
                    return hillsLeft && (ha < 0 || ha >= 1.0f); // left + 높은 hill_amount
                },
                "generate + hills=left + hill_amount >= 1.0"),

            // === 해안 방향 (2개) ===
            // #16
            new("해안 타일 + 북쪽 해안 → coast_direction=north",
                TileCtx("온대림", "TemperateForest", "Flat", river: false, elevation: 5, coastal: true),
                "북쪽에 바다가 있는 맵으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    return json.Contains("\"coast_direction\":\"north\"") || json.Contains("\"coast_direction\": \"north\"");
                },
                "generate + coast_direction=north"),

            // #17
            new("해안 타일 + 해안 방향 미지정 → coast_direction=auto 또는 미포함",
                TileCtx("초원", "Grasslands", "Flat", river: false, elevation: 8, coastal: true),
                "바다가 보이는 자연스러운 맵",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    // coast_direction이 없거나 auto이면 OK
                    bool hasCoast = json.Contains("coast_direction");
                    if (!hasCoast) return true;
                    return json.Contains("\"coast_direction\":\"auto\"") || json.Contains("\"coast_direction\": \"auto\"");
                },
                "generate + coast_direction=auto 또는 미포함"),

            // === 석재 (2개) ===
            // #18
            new("석재 1종만 요청 → rock_count=1",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: false, elevation: 200, coastal: false),
                "석재를 딱 한 종류만 나오게 해줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var rc = ExtractNumberField(GetJson(r), "rock_count");
                    return rc >= 1 && rc <= 2; // 1종 요청이므로 1~2 허용 (LLM 관대)
                },
                "generate + rock_count=1 (또는 2)"),

            // #19
            new("다양한 석재 요청 → rock_count 높은 값",
                TileCtx("온대림", "TemperateForest", "Mountainous", river: false, elevation: 700, coastal: false),
                "석재 종류를 최대한 다양하게 넣어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var rc = ExtractNumberField(GetJson(r), "rock_count");
                    return rc < 0 || rc >= 5; // 미포함(-1)이거나 5 이상
                },
                "generate + rock_count >= 5"),

            // === 광석 (2개) ===
            // #20
            new("광석 많이 → ore_density > 1.0",
                TileCtx("온대림", "TemperateForest", "LargeHills", river: false, elevation: 400, coastal: false),
                "광석을 엄청 많이 넣어줘, 광물 풍부하게",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var od = ExtractNumberField(GetJson(r), "ore_density");
                    return od < 0 || od > 1.0f; // 미포함이거나 > 1.0
                },
                "generate + ore_density > 1.0"),

            // #21
            new("광석 없이 → ore_density=0",
                TileCtx("초원", "Grasslands", "Flat", river: true, elevation: 100, coastal: false),
                "광석이 아예 없는 맵으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var od = ExtractNumberField(GetJson(r), "ore_density");
                    return od >= 0 && od <= 0.1f; // 0 또는 매우 작은 값
                },
                "generate + ore_density=0 (또는 매우 낮음)"),

            // === TileMutator (4개) ===
            // #22
            new("온천 요청 → mutators에 HotSprings",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: false, elevation: 300, coastal: false),
                "온천이 있는 맵을 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    return ContainsMutator(GetJson(r), "HotSprings");
                },
                "generate + mutators에 HotSprings 포함"),

            // #23
            new("열대식물/망그로브 요청 → mutators에 WildTropicalPlants",
                TileCtx("열대우림", "TropicalRainforest", "Flat", river: true, elevation: 10, coastal: true),
                "망그로브 숲이 있는 열대 맵",
                r => {
                    if (GetAction(r) != "generate") return true;
                    return ContainsMutator(GetJson(r), "WildTropicalPlants");
                },
                "generate + mutators에 WildTropicalPlants 포함"),

            // #24
            new("호수 요청 → mutators에 Lake",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: false, elevation: 200, coastal: false),
                "가운데 큰 호수가 있는 맵",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    return ContainsMutator(json, "Lake") || ContainsMutator(json, "LakeWithIsland");
                },
                "generate + mutators에 Lake 또는 LakeWithIsland 포함"),

            // #25
            new("피요르드 요청 (해안 타일) → mutators에 Fjord",
                TileCtx("아한대림", "BorealForest", "LargeHills", river: true, elevation: 50, coastal: true),
                "피요르드 지형으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    return ContainsMutator(GetJson(r), "Fjord");
                },
                "generate + mutators에 Fjord 포함"),

            // === 복합 요청 (3개) ===
            // #26
            new("온천 + 산 많은 맵 → HotSprings + hill_amount 높음",
                TileCtx("온대림", "TemperateForest", "LargeHills", river: false, elevation: 500, coastal: false),
                "온천이 있고 산이 많은 맵으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    bool hasMutator = ContainsMutator(json, "HotSprings");
                    var ha = ExtractNumberField(json, "hill_amount");
                    return hasMutator && (ha < 0 || ha >= 1.0f); // HotSprings + 높은 hill_amount
                },
                "generate + HotSprings mutator + hill_amount >= 1.0"),

            // #27
            new("광물 풍부한 고원 → MineralRich + ore_density 높음",
                TileCtx("온대림", "TemperateForest", "LargeHills", river: false, elevation: 600, coastal: false),
                "광물이 풍부한 고원 지형으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    bool hasMineralRich = ContainsMutator(json, "MineralRich");
                    bool hasPlateau = ContainsMutator(json, "Plateau");
                    var od = ExtractNumberField(json, "ore_density");
                    // MineralRich 또는 Plateau 중 하나 + ore_density 높음 (또는 미포함 기본)
                    return (hasMineralRich || hasPlateau) && (od < 0 || od >= 1.0f);
                },
                "generate + MineralRich 또는 Plateau mutator + ore_density >= 1.0"),

            // #28
            new("안개 낀 습지 → FoggyMutator + Wetland",
                TileCtx("온대림", "TemperateForest", "Flat", river: true, elevation: 50, coastal: false),
                "안개가 자욱한 습지 맵으로 만들어줘",
                r => {
                    if (GetAction(r) != "generate") return true;
                    var json = GetJson(r);
                    bool hasFoggy = ContainsMutator(json, "FoggyMutator");
                    bool hasWetland = ContainsMutator(json, "Wetland") || ContainsMutator(json, "Marshy");
                    return hasFoggy && hasWetland;
                },
                "generate + FoggyMutator + Wetland/Marshy mutator"),

            // === 경계/에러 (2개) ===
            // #29
            new("존재하지 않는 mutator 요청 → 가까운 것 추천 또는 ask",
                TileCtx("온대림", "TemperateForest", "SmallHills", river: false, elevation: 200, coastal: false),
                "화산 지형 맵으로 만들어줘",
                r => {
                    // LLM이 가까운 mutator를 추천하거나 ask로 안내
                    if (GetAction(r) == "ask") return true;
                    if (GetAction(r) == "generate")
                    {
                        var json = GetJson(r);
                        // LavaCaves 또는 다른 관련 mutator 사용 가능
                        return ContainsMutator(json, "LavaCaves")
                            || ContainsMutator(json, "Cavern")
                            || !string.IsNullOrEmpty(GetDescription(r));
                    }
                    return false;
                },
                "ask(안내) 또는 generate(유사 mutator 사용)"),

            // #30
            new("모든 파라미터 극단값 동시 요청",
                TileCtx("열대우림", "TropicalRainforest", "Mountainous", river: true, elevation: 50, coastal: true),
                "산 최대, 나무 최대, 동물 최대, 동굴 있고, 간헐천 10개, 바다 북쪽, 석재 15종, 광석 최대, 온천이랑 호수도 넣어줘",
                r => {
                    if (GetAction(r) == "ask") return true; // 너무 극단적이라고 안내해도 OK
                    if (GetAction(r) != "generate") return false;
                    var json = GetJson(r);
                    // 최소한 generate + 유효한 params 구조
                    bool hasParams = json.Contains("\"params\"") || json.Contains("\"hills\"");
                    // 범위 클램핑 확인
                    var veg = ExtractNumberField(json, "vegetation_density");
                    var od = ExtractNumberField(json, "ore_density");
                    bool vegOk = veg < 0 || veg <= 2.1f; // 범위 내
                    bool oreOk = od < 0 || od <= 2.6f;   // 범위 내
                    return hasParams && vegOk && oreOk;
                },
                "generate(극단값 클램핑) 또는 ask(극단 요청 안내)"),
        };
    }

    static string TileCtx(string biome, string biomeDef, string hills, bool river, float elevation, bool coastal)
    {
        string riverInfo = river ? "있음" : "없음";
        if (river && coastal) riverInfo += " (바다/호수로 연결됨 — 해안가 타일)";
        string coastalInfo = coastal ? "예 (인접 바다/호수 감지됨)" : "감지 안 됨";

        return $@"

[현재 선택된 타일 정보]
- 바이옴: {biome} ({biomeDef})
- 지형: {hills}
- 해발고도: {elevation:F0}m
- 강: {riverInfo}
- 해안가: {coastalInfo}

현재 조절 가능한 것:
- hills: 산/언덕 위치 (left, right, center, edges, top, bottom, none)
- hill_amount: 전체 산/언덕 양 (0.5=적음 ~ 1.0=기본 ~ 1.6=많음). 1.0이면 기본. hills와 조합 가능.
- vegetation_density: 나무/풀 밀도 (0.0=없음 ~ 2.0=빽빽)
- animal_density: 동물 밀도 (0.0~2.0)
- caves: 동굴 존재 여부
- geysers: 간헐천 개수 (0~10)
- coast_direction: 해안 방향 (auto/north/east/south/west). 해안가 타일에서만 의미 있음. auto=바닐라 기본.
- rock_count: 석재 종류 수 (1~15). -1=바닐라 기본. 적으면 단일 석재, 많으면 다양한 석재.
- ore_density: 광석 밀도 (0.0~2.5). 1.0=기본. 0이면 광석 없음, 2.5면 매우 많음.
- mutators: 특수 지형 변형 목록 (배열). 사용 가능한 값:
  자연: HotSprings(온천), Valley(계곡), Cavern(동굴), Chasm(협곡), Plateau(고원), Basin(분지), Cliffs(절벽), Wetland(습지), Fertile(비옥), Sandy(모래), Muddy(진흙), Marshy(습지), Dunes(사구), MixedBiome(혼합)
  수계: Lake(호수), LakeWithIsland(섬있는호수), Pond(연못), Oasis(오아시스), DryLake(마른호수), ToxicLake(독성호수), Headwater(수원)
  강: RiverConfluence(합류), RiverIsland(강섬), RiverDelta(삼각주)
  해안: Archipelago(군도), Peninsula(반도), Bay(만), Fjord(피요르드), CoastalIsland(해안섬), Cove(후미)
  동굴: IceCaves(얼음동굴), LavaCaves(용암동굴), CaveLakes(동굴호수)
  변형: AnimalLife_Increased(동물증가), PlantLife_Increased(식물증가), SteamGeysers_Increased(간헐천증가), MineralRich(광물풍부), WildTropicalPlants(열대식물/망그로브), ArcheanTrees(고대나무)
  날씨: FoggyMutator(안개), SunnyMutator(맑음), WindyMutator(바람)
  인공: AncientRuins(고대유적), AncientToxVent(독성간헐천), AncientHeatVent(열간헐천), Harbor(항구)
  각 카테고리(River, Coast, Lake, Mountain 등)에서 최대 1개만 선택. 바이옴/타일에 맞는 것만 추천.

현재 조절 불가능한 것 (솔직하게 안내):
- 강 방향/모양: 타일의 월드맵 데이터에 의해 고정됨.
- 강 추가/제거: 강이 없는 타일에 강을 넣을 수 없음.
- 바다: 해안가 타일에서만 가능.

규칙:
- 강이 있으면 river.present=true. 방향/모양 변경 요구 시 → '현재는 강 방향 조절이 불가능합니다'라고 안내.
- 강 없는 타일에서 강 요구 시 → 강 있는 타일 선택 안내.
- 바다 요구 시 해안 타일 확인 후 안내.
- 조절 불가능한 것을 요구하면 솔직하게 불가능하다고 안내하고 대안 제시.
- '추천해줘' 요청 시 바이옴에 맞게 구체적으로 description 작성.";
    }

    static string SystemPrompt = @"당신은 RimWorld 맵 생성 도우미입니다.
유저의 맵 설명을 분석하여 다음 두 가지 중 하나로 응답하세요.

1. 추가 정보가 필요하거나, 제안/설명을 하려면:
{""action"":""ask"",""message"":""질문, 제안, 또는 설명 내용""}

2. 맵을 생성할 준비가 됐으면 (반드시 description에 어떤 맵을 만드는지 설명 포함):
{""action"":""generate"",""description"":""이 맵의 설명"",""params"":{""hills"":""left""|""right""|""center""|""edges""|""top""|""bottom""|""none"",""hill_amount"":0.5~1.6,""river"":{""present"":true|false},""vegetation_density"":0.0~2.0,""animal_density"":0.0~2.0,""caves"":true|false,""geysers"":0~10,""coast_direction"":""auto""|""north""|""east""|""south""|""west"",""rock_count"":1~15,""ore_density"":0.0~2.5,""mutators"":[""HotSprings"",""WildTropicalPlants""]}}

규칙:
- 반드시 JSON만 출력하세요.
- generate 시 description 필드에 유저가 이해할 수 있는 맵 설명을 포함하세요.
- '그냥 추천해줘' 같은 요청에는 바이옴에 어울리는 설정을 추천하고 description으로 설명하세요.
- 한국어 대화를 지원합니다.";

    // === mutator 검증 헬퍼 ===
    static bool ContainsMutator(string json, string mutatorName)
    {
        // "mutators" 배열 안에 mutatorName이 포함되는지 확인
        // JSON에서 "mutators":[..."mutatorName"...] 패턴 매칭
        int mutIdx = json.IndexOf("\"mutators\"");
        if (mutIdx < 0) return false;
        int arrStart = json.IndexOf('[', mutIdx);
        if (arrStart < 0) return false;
        int arrEnd = json.IndexOf(']', arrStart);
        if (arrEnd < 0) return false;
        string arrContent = json.Substring(arrStart, arrEnd - arrStart + 1);
        return arrContent.Contains($"\"{mutatorName}\"");
    }

    // === Gemini API 호출 ===
    static async Task<string> CallGemini(string systemPrompt, string userMessage)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={ApiKey}";

        var body = $@"{{
            ""system_instruction"":{{""parts"":[{{""text"":{JsonEscape(systemPrompt)}}}]}},
            ""contents"":[{{""role"":""user"",""parts"":[{{""text"":{JsonEscape(userMessage)}}}]}}]
        }}";

        var response = await Http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  API ERROR: {json.Substring(0, Math.Min(200, json.Length))}");
            return null;
        }

        return ExtractText(json);
    }

    static string ExtractText(string json)
    {
        var marker = "\"text\": \"";
        var start = json.IndexOf(marker);
        if (start < 0) { marker = "\"text\":\""; start = json.IndexOf(marker); if (start < 0) return null; }
        start += marker.Length;
        var sb = new StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            if (json[i] == '\\' && i + 1 < json.Length)
            {
                char next = json[i + 1];
                if      (next == 'n')  sb.Append('\n');
                else if (next == '"')  sb.Append('"');
                else if (next == '\\') sb.Append('\\');
                else sb.Append(next);
                i++;
                continue;
            }
            if (json[i] == '"') break;
            sb.Append(json[i]);
        }
        return sb.ToString();
    }

    // === JSON 파싱 헬퍼 ===
    static string GetJson(string response)
    {
        if (response == null) return "";
        int f = response.IndexOf('{');
        int l = response.LastIndexOf('}');
        if (f < 0 || l <= f) return "";
        return response.Substring(f, l - f + 1);
    }

    static string GetAction(string response)
    {
        var json = GetJson(response);
        return ExtractField(json, "action");
    }

    static string GetMessage(string response)
    {
        var json = GetJson(response);
        return ExtractField(json, "message") ?? "";
    }

    static string GetDescription(string response)
    {
        var json = GetJson(response);
        return ExtractField(json, "description");
    }

    static string ExtractField(string json, string field)
    {
        var marker = $"\"{field}\"";
        var idx = json.IndexOf(marker);
        if (idx < 0) return null;
        idx = json.IndexOf("\"", idx + marker.Length + 1);
        if (idx < 0) return null;
        idx++;
        var end = json.IndexOf("\"", idx);
        if (end < 0) return null;
        return json.Substring(idx, end - idx);
    }

    static float ExtractNumberField(string json, string field)
    {
        var marker = $"\"{field}\"";
        var idx = json.IndexOf(marker);
        if (idx < 0) return -1f;
        idx += marker.Length;
        // Skip whitespace and colon
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == ':')) idx++;
        var sb = new StringBuilder();
        while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '-'))
        {
            sb.Append(json[idx]);
            idx++;
        }
        if (float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
            return val;
        return -1f;
    }

    static string JsonEscape(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                       .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }

    // === 메인 ===
    static async Task Main(string[] args)
    {
        // API 키 로드
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "dev_config.json");
        if (!File.Exists(configPath))
            configPath = "F:\\Projects\\Rimworld\\mapgen_ai\\dev_config.json";

        var config = JsonDocument.Parse(File.ReadAllText(configPath));
        ApiKey = config.RootElement.GetProperty("gemini_api_key").GetString();
        Model = config.RootElement.GetProperty("gemini_model").GetString();

        Console.WriteLine($"=== MapGen AI 테스트벤치 ===");
        Console.WriteLine($"모델: {Model}");
        Console.WriteLine();

        var cases = BuildTestCases();
        int passed = 0, failed = 0;

        foreach (var tc in cases)
        {
            Console.Write($"[{tc.Name}] ... ");

            var fullPrompt = SystemPrompt + tc.TileContext;
            var response = await CallGemini(fullPrompt, tc.UserMessage);

            if (response == null)
            {
                Console.WriteLine("SKIP (API 오류)");
                continue;
            }

            var action = GetAction(response);
            bool pass = tc.Validate(response);

            if (pass)
            {
                Console.WriteLine($"PASS (action={action})");
                passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL");
                Console.ResetColor();
                Console.WriteLine($"  기대: {tc.ExpectDesc}");
                Console.WriteLine($"  실제 action: {action}");
                Console.WriteLine($"  응답: {GetJson(response).Substring(0, Math.Min(300, GetJson(response).Length))}");
                failed++;
            }

            await Task.Delay(500); // rate limit
        }

        Console.WriteLine();
        Console.WriteLine($"=== 결과: {passed} PASS / {failed} FAIL (총 {cases.Count}) ===");

        // CI 대응: 실패 시 exit code 1
        Environment.Exit(failed > 0 ? 1 : 0);
    }
}
