using System;
using System.Collections.Generic;
using System.Linq;
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
        public int IntendedWater, ActualWater;
        readonly HashSet<string> changedShapes = new HashSet<string>();
        readonly HashSet<int> waterIntent = new HashSet<int>();
        readonly Dictionary<string,bool> waterMaterials = new Dictionary<string,bool>();

        public CandidatePreviewSnapshot(int tileId, TileMapState state, TileMapState before = null)
        {
            Original = Find.WorldGrid[tileId];
            State = state.Clone();
            foreach(var shape in State.elevationShapes)
            {
                var old=before?.elevationShapes.Find(s=>s.id==shape.id);
                if(shape.id!=null && (old==null || UI.SimpleJson.Serialize(shape)!=UI.SimpleJson.Serialize(old)))changedShapes.Add(shape.id);
            }
            var wc = MapGenAIWorldComponent.Get();
            var baseline = WorldTileEditor.Rebase(wc.GetBaseline(tileId), wc.GetLastApplied(tileId), TileWorldSnapshot.Capture(Original));
            var desired = WorldTileEditor.Plan(Original, baseline, State);
            Tile = (Tile)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(Original, null);
            Tile.mutatorsNullable = desired;
            // These caches depend on the native feature list, which differs between candidates.
            foreach (string name in new[] { "tmpHasSecondaryBiome", "tmpSecondaryBiome", "hillinessLabelCached" })
                AccessTools.Field(typeof(Tile), name)?.SetValue(Tile, null);
        }

        // Observe intent before a later shape overwrites it. Only candidate scopes enable this callback.
        public void ObserveMaterial(string shapeId,int cellIndex,string material)
        {
            if(shapeId==null || !changedShapes.Contains(shapeId))return;
            if(!waterMaterials.TryGetValue(material,out var water))
                waterMaterials[material]=water=DefDatabase<TerrainDef>.GetNamedSilentFail(material)?.IsWater==true;
            if(water)waterIntent.Add(cellIndex);
        }

        // Compare the captured intent with final terrain. Never infer terrain from colors.
        public void InspectWater(Map map)
        {
            var regions=GenerationContext.Regions(map);
            if(regions==null || changedShapes.Count==0)return;
            foreach(var shape in State.elevationShapes.Where(s=>s.type=="region_fill" && changedShapes.Contains(s.id)))
            {
                if(!TerrainMaterials.Resolve(shape.fill).IsWater)continue;
                var mask=regions.Mask(shape.id);
                for(int i=0;i<mask.Length;i++)if(mask[i])waterIntent.Add(i);
            }
            IntendedWater=waterIntent.Count;
            ActualWater=waterIntent.Count(i=>map.terrainGrid.TerrainAt(new IntVec3(i%map.Size.x,0,i/map.Size.x)).IsWater);
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
                generation = GenerationContext.Enter(snapshot.Tile.tile, snapshot.State,snapshot.ObserveMaterial);
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
