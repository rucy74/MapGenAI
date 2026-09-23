using System;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld;
using Verse;

namespace MapGenAI.Patches
{
    // Initial placement only. Preserve native species selection, viability and regrowth.
    [HarmonyPatch(typeof(GenStep_Plants),nameof(GenStep_Plants.Generate))]
    static class LandscapeVegetationScope
    {
        [ThreadStatic] static Map activeMap;
        static void Prefix(Map map,out Map __state)
        {__state=activeMap;activeMap=GenerationContext.Active?map:null;}
        static void Finalizer(Map __state)=>activeMap=__state;
        public static float Weight(Map map,IntVec3 cell)
        {
            if(activeMap!=map || !GenerationContext.Active)return 1;
            var weights=GenerationContext.Regions(map)?.VegetationWeights;
            if(weights==null || !cell.InBounds(map) || !map.BiomeAt(cell).wildPlantsCareAboutLocalFertility)return 1;
            return weights[cell.z*map.Size.x+cell.x];
        }
    }
    [HarmonyPatch(typeof(WildPlantSpawner),nameof(WildPlantSpawner.GetDesiredPlantsCountAt))]
    static class LandscapeDesiredPlantsAt
    {
        static void Prefix(Map ___map,IntVec3 forCell,ref float plantDensityFactor)
        {plantDensityFactor*=LandscapeVegetationScope.Weight(___map,forCell);}
    }
    [HarmonyPatch(typeof(WildPlantSpawner),nameof(WildPlantSpawner.GetDesiredPlantsCountIn))]
    static class LandscapeDesiredPlantsIn
    {
        static void Prefix(Map ___map,IntVec3 forCell,ref float plantDensityFactor)
        {plantDensityFactor*=LandscapeVegetationScope.Weight(___map,forCell);}
    }
}
