using HarmonyLib;
using RimWorld;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// GenStep_Terrain(또는 MapGenUtility)의 TerrainFrom을 패치하여
    /// fertility가 음수 마법값일 때 물/특수 지형으로 변환.
    /// Map Designer Feature_TerrainFrom.cs와 동일한 방식 (MIT License).
    ///
    /// 인코딩 규칙:
    /// fertility < -2000 → 강보다 우선 (호수 중심부)
    /// fertility < -1000 → 강 다음 (호수 가장자리)
    /// fertility >= -1000 → 바닐라 처리
    ///
    /// 값 매핑:
    /// -x005 → WaterDeep
    /// -x025 → WaterShallow
    /// 1     → 해변 (Sand)
    /// </summary>
    [HarmonyPatch]
    static class Patch_TerrainFrom
    {
        // RW 1.5: GenStep_Terrain.TerrainFrom
        // RW 1.6: MapGenUtility.TerrainFrom (리팩토링됨)
        // 둘 다 시도하여 존재하는 것에 패치
        static System.Reflection.MethodBase TargetMethod()
        {
            // RW 1.6 먼저 시도
            var method = AccessTools.Method("RimWorld.MapGenUtility:TerrainFrom");
            if (method != null) return method;

            // RW 1.5 폴백
            method = AccessTools.Method("RimWorld.GenStep_Terrain:TerrainFrom");
            return method;
        }

        static bool Prefix(float fertility, ref TerrainDef __result)
        {
            if (fertility >= -1000f)
                return true; // 바닐라 처리

            float f = fertility;
            if (f < -2000f)
                f += 1000f; // 강 우선 모드 → 같은 값으로 정규화

            // 값 매핑 (Map Designer 동일)
            if (f >= -1005f)
                __result = TerrainDefOf.WaterDeep;
            else if (f >= -1015f)
                __result = TerrainDefOf.WaterOceanDeep;
            else if (f >= -1025f)
                __result = TerrainDefOf.WaterShallow;
            else if (f >= -1035f)
                __result = TerrainDefOf.WaterOceanShallow;
            else if (f >= -1045f)
                __result = TerrainDef.Named("MarshyTerrain");
            else if (f >= -1055f)
                __result = TerrainDef.Named("Mud");
            else if (f >= -1065f)
                __result = TerrainDefOf.Ice;
            else if (f >= -1075f)
                __result = TerrainDefOf.Sand;
            else if (f >= -1085f)
                __result = TerrainDefOf.Soil;
            else
                __result = TerrainDef.Named("SoilRich");

            return false; // 바닐라 스킵
        }
    }
}
