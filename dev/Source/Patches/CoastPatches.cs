using HarmonyLib;
using RimWorld.Planet;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// World.CoastAngleAt Postfix: 해안 방향을 강제 설정.
    /// Map Designer의 CoastDirPatch 방식과 동일.
    /// RW 1.6 시그니처: float? CoastAngleAt(PlanetTile tile, BiomeDef waterBiome)
    ///
    /// 각도 매핑: North=270, East=180, South=90, West=0
    /// __result가 null이면 해안이 아니므로 건드리지 않음.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.CoastAngleAt))]
    static class Patch_CoastDirection
    {
        static void Postfix(PlanetTile tile, ref float? __result)
        {
            if (!MapGenParams.HasParams) return;
            if (MapGenParams.CoastDirection == "auto") return;

            // 원래 해안이 아닌 타일(result == null)에는 적용하지 않음
            if (__result == null) return;

            switch (MapGenParams.CoastDirection)
            {
                case "north":
                    __result = 270f;
                    break;
                case "east":
                    __result = 180f;
                    break;
                case "south":
                    __result = 90f;
                    break;
                case "west":
                    __result = 0f;
                    break;
            }
        }
    }
}
