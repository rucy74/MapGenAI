using System.Reflection;
using HarmonyLib;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    // Odyssey landforms run after ElevationFertility. Image priority protects authored heights
    // including SDF edits, while N cells, legacy overlays and other mutator effects remain native.
    [HarmonyPatch]
    static class Patch_ImageElevationPriority
    {
        static MethodBase TargetMethod()=>AccessTools.Method("RimWorld.GenStep_MutatorPostElevationFertility:Generate");
        static bool Prepare()=>TargetMethod()!=null;
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Map map)=>GenerationContext.RestoreImageElevation(map,MapGenerator.Elevation);
    }
}
