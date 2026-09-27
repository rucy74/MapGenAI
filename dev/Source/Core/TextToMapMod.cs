using System;
using System.Linq;
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
            ApplyPatches();
        }

        /// <summary>
        /// One patch that fails in a player's setup must not remove the world-map button or this settings page.
        /// Harmony's PatchAll would stop at the first failure (see PatchPlan), so each class is applied on its own.
        /// </summary>
        static void ApplyPatches()
        {
            var types = AccessTools.GetTypesFromAssembly(typeof(MapGenAIMod).Assembly).Where(t => t.HasHarmonyAttribute()).ToList();
            // Test seam for the runtime probe; inert unless the game is started with this argument.
            GenCommandLine.TryGetCommandLineArg("mapgenAISimulatePatchFailure", out string simulated);
            var result = PatchPlan.Apply(types, t => t == typeof(Patches.WorldInterface_Patch), t => t.Name, t =>
            {
                if (!string.IsNullOrEmpty(simulated) && t.Name == simulated)
                    throw new InvalidOperationException("simulated patch failure (-mapgenAISimulatePatchFailure)");
                Harmony.CreateClassProcessor(t).Patch();
            });
            foreach (var failure in result.Failed)
                Log.Error("[MapGenAI] A game patch failed and only its feature is turned off: " + failure);
            Log.Message($"[MapGenAI] Game patches applied: {result.Applied.Count}/{types.Count} (entry button first).");
        }

        public override string SettingsCategory() => DisplayName;

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}
