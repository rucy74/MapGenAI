using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// 돌덩어리(RockChunk) 생성 제어.
    /// Map Designer 1.6 RockChunkPatch.cs와 동일 방식:
    /// GenStep_RockChunks.Generate Prefix에서 HasRockChunks=false이면 return false.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_RockChunks), "Generate")]
    static class Patch_RockChunks
    {
        static bool Prefix()
        {
            if (!MapGenParams.HasParams) return true;
            return MapGenParams.HasRockChunks;
        }
    }
}
