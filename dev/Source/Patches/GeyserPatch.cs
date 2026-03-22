using System;
using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// 간헐천(SteamGeyser) 수 조절.
    /// RuinDangerDensityPatch와 동일한 패턴:
    /// MapGenerator.GenerateContentsIntoMap Prefix에서 GenStepDef의 countPer10kCellsRange를 수정,
    /// Postfix에서 원본 복원.
    ///
    /// GeyserCount == -1: 기본값 유지 (변경 없음)
    /// GeyserCount == 0: countPer10kCellsRange를 0으로 설정 (간헐천 없음)
    /// GeyserCount >= 1: 원하는 개수에 맞게 countPer10kCellsRange 계산
    ///
    /// 바닐라 기본값: countPer10kCellsRange = 0.7~1.0
    /// 250x250 맵 = 62,500 셀 → 기본 4~6개 간헐천
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_GeyserCount
    {
        private static FloatRange _originalRange;
        private static bool _modified = false;

        [HarmonyPriority(Priority.High)]
        static void Prefix()
        {
            _modified = false;

            if (!MapGenParams.HasParams) return;
            if (MapGenParams.GeyserCount < 0) return; // -1 = 기본값

            try
            {
                var geyserDef = DefDatabase<GenStepDef>.GetNamedSilentFail("SteamGeysers");
                if (geyserDef?.genStep is GenStep_Scatterer scatterer)
                {
                    _originalRange = scatterer.countPer10kCellsRange;
                    _modified = true;

                    int desiredCount = MapGenParams.GeyserCount;

                    if (desiredCount == 0)
                    {
                        scatterer.countPer10kCellsRange = new FloatRange(0f, 0f);
                    }
                    else
                    {
                        // countPer10kCellsRange → 실제 개수 변환:
                        // 바닐라 공식: count = Rand.Range(range.min, range.max) * mapCells / 10000
                        // 역산: range = desiredCount * 10000 / mapCells
                        // 맵 크기를 모르므로(아직 Prefix라 map 인스턴스 없음) 표준 250x250 = 62500 기준
                        // ±0.5 범위로 min/max 설정하여 약간의 랜덤성 유지
                        float per10k = desiredCount * 10000f / 62500f;
                        float halfCount = 0.5f * 10000f / 62500f; // ±0.5개 범위
                        float min = Math.Max(0f, per10k - halfCount);
                        float max = per10k + halfCount;

                        scatterer.countPer10kCellsRange = new FloatRange(min, max);
                    }

                    Log.Message($"[MapGenAI] 간헐천 수 적용: {desiredCount}개, " +
                        $"range={scatterer.countPer10kCellsRange.min:F2}~{scatterer.countPer10kCellsRange.max:F2} " +
                        $"(원본={_originalRange.min:F2}~{_originalRange.max:F2})");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MapGenAI] 간헐천 수 적용 실패: {e.Message}");
            }
        }

        static void Postfix()
        {
            if (!_modified) return;

            try
            {
                var geyserDef = DefDatabase<GenStepDef>.GetNamedSilentFail("SteamGeysers");
                if (geyserDef?.genStep is GenStep_Scatterer scatterer)
                {
                    scatterer.countPer10kCellsRange = _originalRange;
                }
            }
            catch { }

            _modified = false;
        }
    }
}
