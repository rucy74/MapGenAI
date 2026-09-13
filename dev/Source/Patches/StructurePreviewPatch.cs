using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace MapGenAI.Patches
{
    // Map Preview suppresses GenSpawn. Render the same deterministic wall plan without spawning Things.
    [HarmonyPatch]
    static class Patch_StructurePreview
    {
        static bool Prepare()=>AccessTools.TypeByName("MapPreview.MapPreviewGenerator+PreviewTextureGenStep")!=null;
        static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator+PreviewTextureGenStep"),"Generate");
        static void Postfix(object __instance,Map map)
        {
            var plan=MapGen.AuthoringGeneration.Current;
            if(plan==null || plan.placements.Count==0)return;
            var result=Traverse.Create(__instance).Field("_result").GetValue<MapPreview.MapPreviewResult>();
            foreach(var placement in plan.placements)
                foreach(var cell in placement.wallCells)result.SetPixel(cell[0],cell[1],new Color(.7f,.68f,.60f));
        }
    }
}
