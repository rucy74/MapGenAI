using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// World.NaturalRockTypesIn Finalizer: 석재 종류 수와 종류를 제어.
    /// Map Designer의 RockTypesPatch 방식 참조.
    /// RW 1.6 시그니처: IEnumerable&lt;ThingDef&gt; NaturalRockTypesIn(PlanetTile tile)
    ///
    /// 1) RockTypes가 있으면 해당 석재만 반환 (LLM이 "대리석만" 등 요청 시).
    /// 2) RockTypes가 없고 RockCount >= 1이면 해당 개수로 석재 목록을 조절.
    /// 3) 둘 다 없으면 바닐라 기본값 사용.
    /// Finalizer를 사용하여 예외 발생 시에도 안전하게 처리.
    /// </summary>
    [HarmonyPatch(typeof(World), nameof(World.NaturalRockTypesIn))]
    static class Patch_RockTypes
    {
        static System.Exception Finalizer(PlanetTile tile, ref IEnumerable<ThingDef> __result, System.Exception __exception)
        {
            if (!MapGenParams.HasParams)
                return __exception;

            bool hasRockTypes = MapGenParams.RockTypes.Count > 0;
            bool hasRockCount = MapGenParams.RockCount >= 1;

            if (!hasRockTypes && !hasRockCount)
                return __exception;

            // 예외가 발생했으면 빈 목록으로 시작
            if (__exception != null)
            {
                Log.Warning($"[MapGenAI] NaturalRockTypesIn 원본 예외 발생, 빈 목록으로 대체: {__exception.Message}");
                __result = new List<ThingDef>();
            }

            // 우선순위 1: rock_types (특정 석재 지정)
            if (hasRockTypes)
            {
                var result = new List<ThingDef>();
                foreach (var defName in MapGenParams.RockTypes)
                {
                    var rockDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (rockDef != null && rockDef.building != null && rockDef.building.isNaturalRock)
                    {
                        if (!result.Contains(rockDef))
                            result.Add(rockDef);
                    }
                    else
                    {
                        Log.Warning($"[MapGenAI] 석재 '{defName}'을(를) 찾을 수 없거나 자연 석재가 아닙니다.");
                    }
                }

                if (result.Count > 0)
                {
                    __result = result;
                    Log.Message($"[MapGenAI] 석재 종류 지정: {string.Join(", ", result.Select(r => r.defName))}");
                    return null;
                }
                // rock_types에 유효한 석재가 없으면 rock_count 또는 바닐라로 폴백
                Log.Warning("[MapGenAI] rock_types에 유효한 석재가 없어 rock_count/바닐라로 폴백");
            }

            // 우선순위 2: rock_count (개수 조절)
            if (hasRockCount)
            {
                int targetCount = MapGenParams.RockCount;

                Rand.PushState();
                try
                {
                    // 타일 해시 기반 시드로 일관된 결과 보장
                    Rand.Seed = tile.GetHashCode();

                    List<ThingDef> currentRocks = __result.ToList();

                    if (currentRocks.Count >= targetCount)
                    {
                        // 목록이 더 길면 잘라냄
                        __result = currentRocks.Take(targetCount).ToList();
                    }
                    else
                    {
                        // 전체 석재 풀에서 아직 포함되지 않은 것들을 추가
                        List<ThingDef> allRocks = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(d => d.building != null && d.building.isNaturalRock && !d.building.isResourceRock)
                            .ToList();

                        List<ThingDef> available = allRocks.Where(r => !currentRocks.Contains(r)).ToList();

                        while (currentRocks.Count < targetCount && available.Count > 0)
                        {
                            ThingDef pick = available.RandomElement();
                            available.Remove(pick);
                            currentRocks.Add(pick);
                        }

                        __result = currentRocks;
                    }
                }
                finally
                {
                    Rand.PopState();
                }
            }

            return null; // 예외 소멸
        }
    }
}
