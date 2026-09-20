using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.Patches
{
    [HarmonyPatch(typeof(GenStep_MutatorPostTerrain), nameof(GenStep_MutatorPostTerrain.Generate))]
    static class HotSpringPostTerrainOrder
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter=AccessTools.PropertyGetter(typeof(Tile),nameof(Tile.Mutators));
            var replacement=AccessTools.Method(typeof(HotSpringPostTerrainOrder),nameof(OrderedFeatures));
            int found=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(getter))
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=replacement;
                    found++;
                }
                yield return instruction;
            }
            if(found!=1)throw new InvalidOperationException("MapGenAI could not locate the native post-terrain feature loop.");
        }

        static IList<TileMutatorDef> OrderedFeatures(Tile tile)
        {
            var features=tile.Mutators;
            if(!GenerationContext.Active || (int)tile.tile!=GenerationContext.TileId)return features;
            return FeaturePolicy.PostTerrainOrder(features,GenerationContext.State,
                FeaturePolicy.HasRiver(tile) || FeaturePolicy.WaterNeighbors(tile).Count>0);
        }
    }

    [HarmonyPatch(typeof(TileMutatorWorker_HotSprings), nameof(TileMutatorWorker_HotSprings.GeneratePostTerrain))]
    static class HotSpringWaterPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(TileMutatorWorker_HotSprings __instance, Map map, out Dictionary<IntVec3,TerrainDef> __state)
        {
            __state=null;
            var state=GenerationContext.State;
            if(state==null || (int)map.Tile!=GenerationContext.TileId || !FeaturePolicy.NativeHotSpring(__instance.def)
                || !state.mutators.Contains(__instance.def.defName))return;
            if(!FeaturePolicy.HasRiver(map.TileInfo) && FeaturePolicy.WaterNeighbors(map.TileInfo).Count==0)return;
            __state=new Dictionary<IntVec3,TerrainDef>();
            foreach(var cell in map.AllCells)
            {
                var terrain=map.terrainGrid.TerrainAt(cell);
                if(terrain.IsWater || terrain.IsRiver)__state.Add(cell,terrain);
            }
        }

        // Per-invocation state also works on the preview worker. Do not mutate shared defs.
        // Always restore protected water, including when this worker throws.
        [HarmonyPriority(Priority.Last)]
        static void Finalizer(Map map, Dictionary<IntVec3,TerrainDef> __state)
        {
            if(__state==null)return;
            foreach(var pair in __state)
                if(map.terrainGrid.TerrainAt(pair.Key)!=pair.Value)map.terrainGrid.SetTerrain(pair.Key,pair.Value);
        }
    }
}
