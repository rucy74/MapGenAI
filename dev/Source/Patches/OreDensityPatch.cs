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
    /// 광석 밀도 조절.
    /// MapGenerator.GenerateContentsIntoMap Prefix에서 모든 광석 GenStepDef의
    /// countPer10kCellsRange를 수정, Postfix에서 원본 복원.
    ///
    /// 광석 종류별로 별도 GenStepDef가 있으므로 (SteelMineables, SilverMineables 등)
    /// 모든 GenStep_ScatterLumpsMineable 인스턴스를 순회하여 처리.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_OreDensity
    {
        private static List<(GenStep_ScatterLumpsMineable scatterer, FloatRange original)> _saved;

        [HarmonyPriority(Priority.High)]
        static void Prefix()
        {
            _saved = null;

            if (!MapGenParams.HasParams) return;
            if (Mathf.Approximately(MapGenParams.OreDensity, 1f)) return;

            try
            {
                float density = MapGenParams.OreDensity;
                float adjusted = density > 1f ? density * density : density;

                _saved = new List<(GenStep_ScatterLumpsMineable, FloatRange)>();

                foreach (var genStepDef in DefDatabase<GenStepDef>.AllDefsListForReading)
                {
                    if (genStepDef.genStep is GenStep_ScatterLumpsMineable scatterer)
                    {
                        var original = scatterer.countPer10kCellsRange;
                        _saved.Add((scatterer, original));

                        scatterer.countPer10kCellsRange = new FloatRange(
                            original.min * adjusted,
                            original.max * adjusted);
                    }
                }

                if (_saved.Count > 0)
                    Log.Message($"[MapGenAI] 광석 밀도 적용: {density:F2} (보정={adjusted:F2}), {_saved.Count}개 GenStep 수정");
            }
            catch (Exception e)
            {
                Log.Warning($"[MapGenAI] 광석 밀도 적용 실패: {e.Message}");
            }
        }

        static void Postfix()
        {
            if (_saved == null) return;

            try
            {
                foreach (var (scatterer, original) in _saved)
                    scatterer.countPer10kCellsRange = original;
            }
            catch { }

            _saved = null;
        }
    }
}
