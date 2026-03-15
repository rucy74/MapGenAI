using HarmonyLib;
using RimWorld;
using Verse;

namespace MapGenAI
{
    public class MapGenAIMod : Mod
    {
        public static MapGenAISettings Settings { get; private set; }
        public static HarmonyLib.Harmony Harmony { get; private set; }

        public MapGenAIMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MapGenAISettings>();
            Harmony = new HarmonyLib.Harmony("Choco.MapGenAI");
            Harmony.PatchAll();
            Log.Message("[MapGenAI] Mod loaded.");
        }

        public override string SettingsCategory() => "MapGen AI";

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}
