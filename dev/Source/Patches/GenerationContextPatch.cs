using System;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.Patches
{
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    static class Patch_GenerationContext_Map
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(MapParent parent, out IDisposable __state)
        {
            int tile = parent == null ? -1 : (int)parent.Tile;
            __state = GenerationContext.Enter(tile, MapGenAIWorldComponent.Get()?.GetState(tile));
        }
        [HarmonyPriority(Priority.Last)]
        static void Finalizer(IDisposable __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_GenerationContext_Contents
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Map map, out IDisposable __state)
        {
            int tile = map == null ? -1 : (int)map.Tile;
            __state = GenerationContext.Enter(tile, MapGenAIWorldComponent.Get()?.GetState(tile));
        }
        [HarmonyPriority(Priority.Last)]
        static void Finalizer(IDisposable __state) => __state?.Dispose();
    }
}
