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
    /// 광석 밀도 조절 — 1.6 실제 경로.
    /// RimWorld 1.6에서 광석 배치는 GenStep_ScatterLumpsMineable(1.5 이하)이 아니라
    /// GenStep_RocksFromGrid 내부의 "resource blotches"로 통합됨
    /// (1.6 Data 전체에 ScatterLumpsMineable을 쓰는 GenStepDef가 0개 — 2026-07-19 확인).
    /// 블롯치 수를 결정하는 static GetResourceBlotchesPer10KCellsForMap(Map)의
    /// 반환값에 배율을 곱한다. 타일 mutator(광물 풍부 등) 보정 위에 중첩 적용됨.
    ///
    /// maxMineableValue는 광석 '종류' 가치 필터(RocksFromGrid_NoMinerals=0이 전부 차단)라
    /// 밀도와 무관 — 건드리지 않는다.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_RocksFromGrid), "GetResourceBlotchesPer10KCellsForMap")]
    static class Patch_OreDensity_RocksFromGrid
    {
        static void Postfix(ref float __result)
        {
            if (!MapGenParams.HasParams) return;

            float density = MapGenParams.OreDensity;
            if (Mathf.Approximately(density, 1f)) return;

            // 기존 정책과 동일: >1은 제곱 보정 (2.5 → 6.25배)
            float adjusted = density > 1f ? density * density : density;
            float before = __result;
            __result *= adjusted;

            Log.Message($"[MapGenAI] 광석 밀도 적용(1.6 RocksFromGrid): {density:F2} " +
                $"(보정={adjusted:F2}), blotches/10k {before:F2} → {__result:F2}");
        }
    }

    /// <summary>
    /// (레거시, 1.5 이하 호환) GenStep_ScatterLumpsMineable 기반 광석 GenStepDef 배율.
    /// 1.6 바닐라에서는 해당 GenStepDef가 없어 no-op이지만, 1.5 세이브나
    /// 이 클래스를 쓰는 서드파티 모드와의 호환을 위해 유지.
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
