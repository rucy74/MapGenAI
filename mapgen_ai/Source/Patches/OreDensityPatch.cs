using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// GenStep_ScatterLumpsMineable.Generate Prefix: 광석 밀도 조절.
    /// Map Designer의 OreDensityPatch 방식과 동일.
    ///
    /// OreDensity가 1.0이면 변경 없음.
    /// countPer10kCellsRange의 min/max에 밀도 승수를 곱함.
    /// 밀도가 1 초과일 때는 제곱하여 체감 효과를 높임 (Map Designer 동일).
    /// </summary>
    [HarmonyPatch(typeof(GenStep_ScatterLumpsMineable), nameof(GenStep_ScatterLumpsMineable.Generate))]
    static class Patch_OreDensity
    {
        static void Prefix(ref GenStep_ScatterLumpsMineable __instance)
        {
            if (!MapGenParams.HasParams) return;
            if (Mathf.Approximately(MapGenParams.OreDensity, 1f)) return;

            float density = MapGenParams.OreDensity;

            // 밀도 > 1일 때 제곱하여 효과 증폭 (Map Designer 방식)
            if (density > 1f)
            {
                density *= density;
            }

            __instance.countPer10kCellsRange.min *= density;
            __instance.countPer10kCellsRange.max *= density;
        }
    }
}
