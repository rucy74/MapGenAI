using HarmonyLib;
using RimWorld;
using Verse;

namespace MapGenAI
{
    public class MapGenAIMod : Mod
    {
        public static MapGenAISettings Settings { get; private set; }
        public static HarmonyLib.Harmony Harmony { get; private set; }
        public static string DisplayName { get; private set; } = "MapGen AI";

        public MapGenAIMod(ModContentPack content) : base(content)
        {
            DisplayName = string.Equals(content.PackageId, "choco.mapgenai.dev", System.StringComparison.OrdinalIgnoreCase)
                ? "MapGen AI [DEV]" : "MapGen AI";
            Settings = GetSettings<MapGenAISettings>();
            Harmony = new HarmonyLib.Harmony("Choco.MapGenAI");
            Harmony.PatchAll();
        }

        public override string SettingsCategory() => DisplayName;

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}
