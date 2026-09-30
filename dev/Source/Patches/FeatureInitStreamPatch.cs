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
        sealed class Holder { public FeatureInitPlan Plan; public Map ReferenceMap; }
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
            var plan = plans.GetValue(map, m => { var h = new Holder(); h.Plan = Compute(m, h); return h; }).Plan;
            if (plan == null) { worker.Init(map); return; }
            var water = map.waterInfo;
            var lake = water?.lakeCenter ?? IntVec3.Invalid;
            try
            {
                plan.Run(worker.def.defName, () =>
                {
                    var reference = plans.GetValue(map, _ => throw new InvalidOperationException()).ReferenceMap;
                    if (reference?.waterInfo.lakeCenter.IsValid == true && worker.def.categories.Contains("River"))
                        water.lakeCenter = reference.waterInfo.lakeCenter;
                    worker.Init(map);
                });
            }
            finally
            {
                // Natural generation routes the river through a lake's centre; an added lake must not move the world river.
                if (water != null && (plan.Isolates(worker.def.defName) || worker.def.categories.Contains("River"))) water.lakeCenter = lake;
                if (plan.Isolates(worker.def.defName)) Interlocked.Increment(ref IsolatedInits);
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
            if (plan == null) { worker.GeneratePostTerrain(map); return; }
            try { plan.Run(worker.def.defName, () => worker.GeneratePostTerrain(map), "PostTerrain"); }
            finally { if (plan.Isolates(worker.def.defName)) Interlocked.Increment(ref IsolatedPostTerrain); }
        }

        // Called at the first feature of the loop, before any feature has drawn from the shared stream.
        static FeatureInitPlan Compute(Map map, Holder holder)
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
                var baseline = wc.GetBaseline(id); var applied = wc.GetLastApplied(id); var snapshot = TileWorldSnapshot.Capture(world);
                var originals = baseline == null ? snapshot.mutators : WorldTileEditor.Rebase(baseline, applied, snapshot).mutators;
                var removed = originals.Where(n => !generated.Mutators.Any(d => d.defName == n) && !WorldConnection(n)).ToList();
                // Keep previous generation available for unsupported third-party or geometry-dependent workers.
                // They need a dedicated reservation contract; do not run them against a shallow reference map.
                bool supported = removed.All(SupportsReservation);
                if (!supported && generated.Mutators.Any(d => WorldConnection(d.defName)))
                    Log.Warning("[MapGenAI] Water preservation after removing these features is not supported: " + string.Join(", ", removed.Where(n => !SupportsReservation(n))));
                return FeatureInitStream.Plan(generated.Mutators.Select(d => d.defName), wc.GetBaseline(id), wc.GetLastApplied(id), TileWorldSnapshot.Capture(world), WorldConnection,
                    name => DefDatabase<TileMutatorDef>.GetNamedSilentFail(name)?.genOrder ?? 0,
                    supported && generated.Mutators.Any(d => WorldConnection(d.defName)) ? (Action<string, string>)((name, phase) => ReserveRemoved(map, holder, name, phase)) : null);
            }
            catch (Exception e)
            {
                Log.Warning("[MapGenAI] Feature initialization plan failed: " + e.Message);
                return null;
            }
        }

        static readonly MethodInfo clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        static bool SupportsReservation(string name)
        {
            var type = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name)?.Worker.GetType();
            if (type == null) return true;
            if (type.Assembly != typeof(TileMutatorWorker).Assembly) return false;
            string owner = AccessTools.Method(type, nameof(TileMutatorWorker.GeneratePostTerrain)).DeclaringType.Name;
            return owner == "TileMutatorWorker_Caves" || owner == "TileMutatorWorker" || owner == "TileMutatorWorker_Lake"
                || owner == "TileMutatorWorker_HotSprings" || owner == "TileMutatorWorker_Coast" || owner == "TileMutatorWorker_Wetland";
        }
        // Removed features never paint terrain or spawn things. Native Init builds noise only, except for
        // the lake centre and MixedBiome component; both are held on an unregistered reference map/worker.
        static void ReserveRemoved(Map map, Holder holder, string name, string phase)
        {
            var def = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
            if (def == null) return; // a disabled mod's missing baseline definition
            var workerType = def.Worker.GetType();
            if (workerType.Assembly != typeof(TileMutatorWorker).Assembly)
                throw new InvalidOperationException("Cannot preserve water while removing this external feature: " + name);
            if (phase == null)
            {
                if (holder.ReferenceMap == null)
                {
                    holder.ReferenceMap = (Map)clone.Invoke(map, null);
                    holder.ReferenceMap.waterInfo = (WaterInfo)clone.Invoke(map.waterInfo, null);
                    holder.ReferenceMap.components = map.components.Select(c => (MapComponent)clone.Invoke(c, null)).ToList();
                }
                var worker = (TileMutatorWorker)Activator.CreateInstance(workerType, def);
                worker.Init(holder.ReferenceMap);
                return;
            }
            var method = AccessTools.Method(workerType, nameof(TileMutatorWorker.GeneratePostTerrain));
            string owner = method.DeclaringType.Name;
            if (owner == "TileMutatorWorker_Caves") { _ = Rand.Int; _ = Rand.Int; return; }
            // These native methods do not draw from the shared stream (RW 1.6.4871).
            if (owner == "TileMutatorWorker" || owner == "TileMutatorWorker_Lake" || owner == "TileMutatorWorker_HotSprings"
                || owner == "TileMutatorWorker_Coast" || owner == "TileMutatorWorker_Wetland") return;
            throw new InvalidOperationException("Cannot reserve terrain-dependent native random draws for removed feature: " + name);
        }
    }
}
