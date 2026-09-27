using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.Patches
{
    // Each native feature-initialization loop calls TileMutatorWorker.Init exactly once per feature.
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    static class FeatureInitStream_GenerateMap
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            FeatureInitStreamPatch.Route(instructions, "MapGenerator.GenerateMap", nameof(TileMutatorWorker.Init), nameof(FeatureInitStreamPatch.Init), ok => FeatureInitStreamPatch.MapSiteRouted = ok);
    }

    [HarmonyPatch]
    static class FeatureInitStream_MapPreview
    {
        static bool Prepare() => AccessTools.TypeByName("MapPreview.MapPreviewGenerator") != null;
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator"), "GenerateContentsIntoPreview");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            FeatureInitStreamPatch.Route(instructions, "MapPreviewGenerator.GenerateContentsIntoPreview", nameof(TileMutatorWorker.Init), nameof(FeatureInitStreamPatch.Init), ok => FeatureInitStreamPatch.PreviewSiteRouted = ok);
    }

    // This step runs every feature's GeneratePostTerrain on one stream seeded for the step. The river draws its bend seed
    // there (RiverNode.seed = Rand.Int), so an added feature drawing earlier in the loop (Caves) would re-bend the world river.
    [HarmonyPatch(typeof(GenStep_MutatorPostTerrain), nameof(GenStep_MutatorPostTerrain.Generate))]
    static class FeatureInitStream_PostTerrain
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            FeatureInitStreamPatch.Route(instructions, "GenStep_MutatorPostTerrain.Generate", nameof(TileMutatorWorker.GeneratePostTerrain), nameof(FeatureInitStreamPatch.PostTerrain), ok => FeatureInitStreamPatch.PostTerrainRouted = ok);
    }

    public static class FeatureInitStreamPatch
    {
        internal static bool MapSiteRouted, PreviewSiteRouted, PostTerrainRouted;
        internal static int IsolatedInits, IsolatedPostTerrain;
        sealed class Holder { public FeatureInitPlan Plan; }
        static readonly ConditionalWeakTable<Map, Holder> plans = new ConditionalWeakTable<Map, Holder>();

        internal static IEnumerable<CodeInstruction> Route(IEnumerable<CodeInstruction> instructions, string site, string workerMethod, string hook, Action<bool> routed)
        {
            var target = AccessTools.Method(typeof(TileMutatorWorker), workerMethod);
            var list = instructions.ToList();
            var calls = list.Where(i => i.Calls(target)).ToList();
            if (calls.Count != 1)
            {
                Log.Warning("[MapGenAI] Feature initialization call not found in " + site + " (" + calls.Count + "); added features may move world rivers and shores there.");
                routed(false);
                return list;
            }
            calls[0].opcode = OpCodes.Call;
            calls[0].operand = AccessTools.Method(typeof(FeatureInitStreamPatch), hook);
            routed(true);
            return list;
        }

        // Replaces worker.Init(map) at the native call sites. Unedited tiles take the unchanged path.
        public static void Init(TileMutatorWorker worker, Map map)
        {
            var plan = plans.GetValue(map, m => new Holder { Plan = Compute(m) }).Plan;
            if (plan == null || !plan.Isolates(worker.def.defName)) { worker.Init(map); return; }
            var water = map.waterInfo;
            var lake = water?.lakeCenter ?? IntVec3.Invalid;
            try { plan.Run(worker.def.defName, () => worker.Init(map)); }
            finally
            {
                // Natural generation routes the river through a lake's centre; an added lake must not move the world river.
                if (water != null) water.lakeCenter = lake;
                Interlocked.Increment(ref IsolatedInits);
            }
        }

        // River and shore categories, the same ones WorldTileEditor.EnsureConnections keeps on every tile with world water.
        static bool WorldConnection(string name)
        {
            var def = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
            return def != null && (def.categories.Contains("River") || def.categories.Contains("Coast"));
        }

        // Replaces worker.GeneratePostTerrain(map) in GenStep_MutatorPostTerrain; the plan was made by this map's Init loop.
        public static void PostTerrain(TileMutatorWorker worker, Map map)
        {
            var plan = plans.TryGetValue(map, out var holder) ? holder.Plan : null;
            if (plan == null || !plan.Isolates(worker.def.defName)) { worker.GeneratePostTerrain(map); return; }
            try { plan.Run(worker.def.defName, () => worker.GeneratePostTerrain(map), "PostTerrain"); }
            finally { Interlocked.Increment(ref IsolatedPostTerrain); }
        }

        // Called at the first feature of the loop, before any feature has drawn from the shared stream.
        static FeatureInitPlan Compute(Map map)
        {
            try
            {
                if (map.IsPocketMap || !map.Tile.Valid || map.Tile.Layer?.IsRootSurface != true) return null;
                var wc = MapGenAIWorldComponent.Get();
                var generated = map.TileInfo;
                if (wc == null || generated == null) return null;
                // A candidate preview substitutes its planned tile for the committed one on this thread.
                var candidate = CandidatePreviewContext.Current;
                var world = candidate != null && ReferenceEquals(generated, candidate.Tile) ? candidate.Original : generated;
                int id = map.Tile.tileId;
                return FeatureInitStream.Plan(generated.Mutators.Select(d => d.defName), wc.GetBaseline(id), wc.GetLastApplied(id), TileWorldSnapshot.Capture(world), WorldConnection);
            }
            catch (Exception e)
            {
                Log.Warning("[MapGenAI] Feature initialization plan failed: " + e.Message);
                return null;
            }
        }
    }
}
