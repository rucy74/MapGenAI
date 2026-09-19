using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.Patches
{
    // A request owns its snapshot. Nothing is written to the world or the editor cache.
    internal sealed class CandidatePreviewSnapshot
    {
        public readonly Tile Original, Tile;
        public readonly TileMapState State;
        public AuthoringResult Report;

        public CandidatePreviewSnapshot(int tileId, TileMapState state)
        {
            Original = Find.WorldGrid[tileId];
            State = state.Clone();
            var wc = MapGenAIWorldComponent.Get();
            var baseline = WorldTileEditor.Rebase(wc.GetBaseline(tileId), wc.GetLastApplied(tileId), TileWorldSnapshot.Capture(Original));
            var desired = WorldTileEditor.Plan(Original, baseline, State);
            Tile = (Tile)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(Original, null);
            Tile.mutatorsNullable = desired;
            // These caches depend on the native feature list, which differs between candidates.
            foreach (string name in new[] { "tmpHasSecondaryBiome", "tmpSecondaryBiome", "hillinessLabelCached" })
                AccessTools.Field(typeof(Tile), name)?.SetValue(Tile, null);
        }
    }

    internal static class CandidatePreviewContext
    {
        internal static readonly ConditionalWeakTable<object, CandidatePreviewSnapshot> Requests = new ConditionalWeakTable<object, CandidatePreviewSnapshot>();
        [ThreadStatic] internal static CandidatePreviewSnapshot Current;
        internal static Tile Substitute(Tile tile) => Current != null && ReferenceEquals(tile, Current.Original) ? Current.Tile : tile;

        internal static IDisposable Enter(object request)
        {
            if (!Requests.TryGetValue(request, out var snapshot)) return null;
            return new Scope(snapshot);
        }

        private sealed class Scope : IDisposable
        {
            readonly CandidatePreviewSnapshot previous;
            readonly IDisposable generation;
            public Scope(CandidatePreviewSnapshot snapshot)
            {
                generation = GenerationContext.Enter(snapshot.Tile.tile, snapshot.State);
                previous = Current;
                Current = snapshot;
            }
            public void Dispose()
            {
                try { Current = previous; }
                finally { generation.Dispose(); }
            }
        }
    }

    [HarmonyPatch]
    static class Patch_CandidatePreviewRequest
    {
        static bool Prepare() => AccessTools.TypeByName("MapPreview.MapPreviewGenerator") != null;
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator"), "GeneratePreview");
        [HarmonyPriority(Priority.First)]
        static void Prefix(object request, out IDisposable __state) => __state = CandidatePreviewContext.Enter(request);
        [HarmonyPriority(Priority.Last)]
        static void Finalizer(IDisposable __state) => __state?.Dispose();
    }

    // Map.TileInfo, PlanetTile.Tile and feature workers all use these accessors.
    // The main/UI thread always sees the real tile, including while a candidate is rendering.
    [HarmonyPatch(typeof(WorldGrid), "get_Item", new[] { typeof(PlanetTile) })]
    static class Patch_CandidateWorldTile
    {
        static void Postfix(ref Tile __result) => __result = CandidatePreviewContext.Substitute(__result);
    }

    [HarmonyPatch(typeof(PlanetLayer), "get_Item", new[] { typeof(int) })]
    static class Patch_CandidateLayerTile
    {
        static void Postfix(ref Tile __result) => __result = CandidatePreviewContext.Substitute(__result);
    }

    [HarmonyPatch(typeof(WorldGrid), "get_Item", new[] { typeof(int) })]
    static class Patch_CandidateSurfaceTile
    {
        static void Postfix(ref SurfaceTile __result) => __result = (SurfaceTile)CandidatePreviewContext.Substitute(__result);
    }
}
