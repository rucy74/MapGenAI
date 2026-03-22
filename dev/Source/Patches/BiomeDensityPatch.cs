using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// 식생/동물 밀도 조절 — BiomeDef 수정 방식 (Map Designer 동일 패턴).
    /// MapGenerator.GenerateContentsIntoMap Prefix에서 BiomeDef 값 수정,
    /// Postfix에서 원본 복원.
    ///
    /// VegetationDensity:
    ///   BiomeDef.plantDensity에 승수 적용.
    ///   plantDensity > 1f이면 1f로 캡하고 대신 wildPlantRegrowDays를 줄여
    ///   더 빠른 재성장으로 밀집된 식생을 구현 (Map Designer 방식).
    ///   density &lt; 1이면 plantDensity 감소 → GenStep_Plants가 더 적은 식물 배치.
    ///
    /// AnimalDensity:
    ///   BiomeDef.animalDensity에 승수 적용.
    ///   GenStep_Animals가 map.wildAnimalSpawner를 통해 동물 수를 결정하는데,
    ///   이 때 BiomeDef.animalDensity를 참조하므로 Prefix에서 변경하면
    ///   density &gt; 1도 정상 반영됨.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_BiomeDensity
    {
        /// <summary>바이옴별 원본 값 저장</summary>
        private struct BiomeDefaults
        {
            public float plantDensity;
            public float wildPlantRegrowDays;
            public float animalDensity;
        }

        private static Dictionary<string, BiomeDefaults> _savedDefaults;
        private static bool _modified = false;

        [HarmonyPriority(Priority.High)]
        static void Prefix()
        {
            _modified = false;
            _savedDefaults = null;

            if (!MapGenParams.HasParams) return;

            float vegDensity = MapGenParams.VegetationDensity;
            float aniDensity = MapGenParams.AnimalDensity;

            bool needVeg = !Mathf.Approximately(vegDensity, 1f);
            bool needAni = !Mathf.Approximately(aniDensity, 1f);

            if (!needVeg && !needAni) return;

            try
            {
                _savedDefaults = new Dictionary<string, BiomeDefaults>();

                foreach (var biome in DefDatabase<BiomeDef>.AllDefsListForReading)
                {
                    // 원본 저장
                    _savedDefaults[biome.defName] = new BiomeDefaults
                    {
                        plantDensity = biome.plantDensity,
                        wildPlantRegrowDays = biome.wildPlantRegrowDays,
                        animalDensity = biome.animalDensity
                    };

                    // 식생 밀도 적용
                    if (needVeg)
                    {
                        biome.plantDensity *= vegDensity;

                        // Map Designer 패턴: plantDensity > 1이면 캡하고 재성장 속도로 보정
                        if (biome.plantDensity > 1f)
                        {
                            biome.wildPlantRegrowDays =
                                _savedDefaults[biome.defName].wildPlantRegrowDays / biome.plantDensity;
                            biome.plantDensity = 1f;
                        }
                    }

                    // 동물 밀도 적용
                    if (needAni)
                    {
                        biome.animalDensity *= aniDensity;
                    }
                }

                _modified = true;

                if (needVeg)
                    Log.Message($"[MapGenAI] 식생 밀도 적용: {vegDensity:F2}");
                if (needAni)
                    Log.Message($"[MapGenAI] 동물 밀도 적용: {aniDensity:F2}");
            }
            catch (Exception e)
            {
                Log.Warning($"[MapGenAI] 바이옴 밀도 적용 실패: {e.Message}");
            }
        }

        static void Postfix()
        {
            if (!_modified || _savedDefaults == null) return;

            try
            {
                foreach (var biome in DefDatabase<BiomeDef>.AllDefsListForReading)
                {
                    if (_savedDefaults.TryGetValue(biome.defName, out var defaults))
                    {
                        biome.plantDensity = defaults.plantDensity;
                        biome.wildPlantRegrowDays = defaults.wildPlantRegrowDays;
                        biome.animalDensity = defaults.animalDensity;
                    }
                }
            }
            catch { }

            _modified = false;
            _savedDefaults = null;
        }
    }
}
