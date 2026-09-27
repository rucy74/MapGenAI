using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

// User-facing failure messages follow the game language: an English screen shows no Korean,
// and a Korean screen does not get the English sentence appended after it.
static class MessageLanguageTests
{
    const string English = "English";
    const string Korean = "Korean (한국어)"; // RimWorld 1.6 language folder name

    public static void RunAll() => Run(CoreRegressionTests.Check);

    public static void Run(Action<string,Action> test)
    {
        test("English road route failure shows no Korean text", () =>
            EnglishOnly(In(English, RoadRouteFailure), null));
        test("Korean road route failure has no English sentence appended", () =>
            KoreanOnly(In(Korean, RoadRouteFailure), null));
        test("English passage obstruction shows no Korean text and keeps the cell count", () =>
            EnglishOnly(In(English, () => PassageGeometry.BlockedMessage(17)), "17"));
        test("Korean passage obstruction has no English sentence appended and keeps the cell count", () =>
            KoreanOnly(In(Korean, () => PassageGeometry.BlockedMessage(17)), "17"));
        test("English feature requirement shows no Korean text and keeps the allowed biome", () =>
            EnglishOnly(In(English, BiomeRequirement), "desert"));
        test("Korean feature requirement has no English sentence appended and keeps the allowed biome", () =>
            KoreanOnly(In(Korean, BiomeRequirement), "desert"));
    }

    static string In(string language, Func<string> message)
    {
        string saved = Prefs.LangFolderName;
        Prefs.LangFolderName = language;
        try
        {
            Need(L10n.IsKorean() == (language == Korean), "Fixture did not switch the game language to " + language);
            return message();
        }
        finally { Prefs.LangFolderName = saved; }
    }

    static void EnglishOnly(string message, string value)
    {
        Need(!HasHangul(message), "Korean text on an English screen: " + message);
        Need(HasLatin(message), "English message has no English text: " + message);
        if (value != null) Need(message.Contains(value), "English message lost its value " + value + ": " + message);
    }

    static void KoreanOnly(string message, string value)
    {
        Need(HasHangul(message), "Korean message has no Korean text: " + message);
        if (value != null) Need(message.Contains(value), "Korean message lost its value " + value + ": " + message);
        Need(!HasLatin(value == null ? message : message.Replace(value, "")), "English text appended on a Korean screen: " + message);
    }

    // A wall splits the map, so no dry road can connect the two waypoints.
    static string RoadRouteFailure()
    {
        const int cols = 25, rows = 19;
        var ground = Enumerable.Repeat(true, cols * rows).ToArray();
        for (int z = 0; z < rows; z++) ground[z * cols + 12] = false;
        var points = new[] { new[] { 3f / (cols - 1), 9f / (rows - 1) }, new[] { 21f / (cols - 1), 9f / (rows - 1) } };
        try { RoadRouting.Plan(cols, rows, ground, points, "avoid", 0f); }
        catch (InvalidOperationException error) { return error.Message; }
        throw new Exception("Fixture road was not blocked");
    }

    // The tile's biome (TemperateForest) is outside the feature's allowed biome list.
    static string BiomeRequirement()
    {
        var saved = Find.WorldGrid;
        Find.WorldGrid = new WorldGrid();
        try
        {
            Find.WorldGrid.Tiles[1] = new SurfaceTile();
            var feature = new TileMutatorDef { defName = "DesertFeature", biomeWhitelist = new List<BiomeDef> { new BiomeDef { defName = "Desert", label = "desert" } } };
            return FeaturePolicy.UnavailableReason(feature, Find.WorldGrid[1]) ?? throw new Exception("Fixture feature was accepted");
        }
        finally { Find.WorldGrid = saved; }
    }

    static bool HasHangul(string s) => s.Any(c => c >= '가' && c <= '힣');
    static bool HasLatin(string s) => s.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
