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
    /// 폐허(ScatterRuinsSimple) 및 고대 위험(ScatterShrines) 밀도 조절.
    /// Map Designer의 HelperMethods.ApplyBiomeSettings 방식과 동일:
    /// MapGenerator.GenerateMap Prefix에서 GenStepDef의 countPer10kCellsRange를 수정,
    /// Postfix에서 원본 복원.
    ///
    /// 폐허: density > 1일 때 3승 (Map Designer: Math.Pow(density, 3))
    /// 위험: density > 1일 때 4승 (Map Designer: Math.Pow(density, 4))
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_RuinDangerDensity
    {
        // 원본 값 저장 (복원용)
        private static FloatRange _originalRuinRange;
        private static FloatRange _originalDangerRange;
        private static bool _ruinModified = false;
        private static bool _dangerModified = false;

        [HarmonyPriority(Priority.High)]
        static void Prefix()
        {
            _ruinModified = false;
            _dangerModified = false;

            if (!MapGenParams.HasParams) return;

            // 폐허 밀도
            if (!Mathf.Approximately(MapGenParams.RuinDensity, 1f))
            {
                try
                {
                    var ruinDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterRuinsSimple");
                    if (ruinDef?.genStep is GenStep_Scatterer ruinScatterer)
                    {
                        _originalRuinRange = ruinScatterer.countPer10kCellsRange;
                        _ruinModified = true;

                        float density = MapGenParams.RuinDensity;
                        if (density > 1f)
                            density = density * density * density; // 3승

                        ruinScatterer.countPer10kCellsRange.min = _originalRuinRange.min * density;
                        ruinScatterer.countPer10kCellsRange.max = _originalRuinRange.max * density;

                        Log.Message($"[MapGenAI] 폐허 밀도 적용: {MapGenParams.RuinDensity:F2} (보정={density:F2}), " +
                            $"range={ruinScatterer.countPer10kCellsRange.min:F1}~{ruinScatterer.countPer10kCellsRange.max:F1}");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"[MapGenAI] 폐허 밀도 적용 실패: {e.Message}");
                }
            }

            // 위험 밀도
            if (!Mathf.Approximately(MapGenParams.DangerDensity, 1f))
            {
                try
                {
                    var dangerDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterShrines");
                    if (dangerDef?.genStep is GenStep_Scatterer dangerScatterer)
                    {
                        _originalDangerRange = dangerScatterer.countPer10kCellsRange;
                        _dangerModified = true;

                        float density = MapGenParams.DangerDensity;
                        if (density > 1f)
                            density = density * density * density * density; // 4승

                        dangerScatterer.countPer10kCellsRange.min = _originalDangerRange.min * density;
                        dangerScatterer.countPer10kCellsRange.max = _originalDangerRange.max * density;

                        Log.Message($"[MapGenAI] 위험 밀도 적용: {MapGenParams.DangerDensity:F2} (보정={density:F2}), " +
                            $"range={dangerScatterer.countPer10kCellsRange.min:F1}~{dangerScatterer.countPer10kCellsRange.max:F1}");
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"[MapGenAI] 위험 밀도 적용 실패: {e.Message}");
                }
            }
        }

        static void Postfix()
        {
            // 원본 복원 (GenStepDef는 공유 데이터이므로 반드시 복원)
            if (_ruinModified)
            {
                try
                {
                    var ruinDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterRuinsSimple");
                    if (ruinDef?.genStep is GenStep_Scatterer ruinScatterer)
                    {
                        ruinScatterer.countPer10kCellsRange = _originalRuinRange;
                    }
                }
                catch { }
                _ruinModified = false;
            }

            if (_dangerModified)
            {
                try
                {
                    var dangerDef = DefDatabase<GenStepDef>.GetNamedSilentFail("ScatterShrines");
                    if (dangerDef?.genStep is GenStep_Scatterer dangerScatterer)
                    {
                        dangerScatterer.countPer10kCellsRange = _originalDangerRange;
                    }
                }
                catch { }
                _dangerModified = false;
            }
        }
    }
}
