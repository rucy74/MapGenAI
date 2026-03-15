using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// 산 크기(hillSize)와 부드러움(hillSmoothness) 제어.
    /// Map Designer 1.6 MountainSettingsPatch Transpiler와 동일 방식:
    /// GenStep_ElevationFertility.Generate IL에서
    /// - 0.021 (Perlin noise frequency = hill size) 상수를 GetHillSize() 호출로 교체
    /// - 2.0 (Perlin lacunarity = hill smoothness) 상수를 GetHillSmoothness() 호출로 교체
    ///
    /// hillSize: 작을수록 큰 산맥, 클수록 잘게 쪼개짐. 기본 0.021.
    /// hillSmoothness: 낮을수록 거친 지형, 높을수록 매끄러움. 기본 2.0.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_ElevationFertility), "Generate")]
    static class Patch_MountainSettings
    {
        /// <summary>
        /// Transpiler: IL에서 hillSize(0.021)와 hillSmoothness(2.0) 상수를
        /// 런타임 메서드 호출로 교체.
        /// </summary>
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int hillSizeIndex = -1;
            int hillSmoothnessIndex = -1;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R8)
                {
                    if (float.TryParse(codes[i].operand.ToString(), out float result))
                    {
                        // hillSmoothness: 첫 번째 2.0 (lacunarity)
                        if (hillSmoothnessIndex == -1 && result == 2f)
                        {
                            hillSmoothnessIndex = i;
                        }
                        // hillSize: 0.021 (Perlin frequency)
                        // IL에서는 float→double 변환 오차로 0.020999999716877937로 저장됨
                        if (hillSizeIndex == -1 && result == 0.020999999716877937f)
                        {
                            hillSizeIndex = i;
                        }
                    }
                }
                if (hillSizeIndex != -1 && hillSmoothnessIndex != -1)
                    break;
            }

            if (hillSizeIndex != -1)
            {
                var newInstr = new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(Patch_MountainSettings), nameof(GetHillSize)));
                newInstr.MoveLabelsFrom(codes[hillSizeIndex]);
                codes[hillSizeIndex] = newInstr;
                Log.Message($"[MapGenAI] Transpiler: hillSize 교체 (index={hillSizeIndex})");
            }
            else
            {
                Log.Warning("[MapGenAI] Transpiler: hillSize(0.021) 상수를 찾지 못함");
            }

            if (hillSmoothnessIndex != -1)
            {
                codes[hillSmoothnessIndex] = new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(Patch_MountainSettings), nameof(GetHillSmoothness)));
                Log.Message($"[MapGenAI] Transpiler: hillSmoothness 교체 (index={hillSmoothnessIndex})");
            }
            else
            {
                Log.Warning("[MapGenAI] Transpiler: hillSmoothness(2.0) 상수를 찾지 못함");
            }

            return codes.AsEnumerable();
        }

        /// <summary>
        /// 런타임에서 호출되어 hillSize 값 반환.
        /// MapGenParams에 값이 있으면 사용, 없으면 바닐라 기본값 반환.
        /// </summary>
        public static double GetHillSize()
        {
            if (MapGenParams.HasParams && MapGenParams.HillSize > 0f)
                return MapGenParams.HillSize;
            return 0.021; // 바닐라 기본값
        }

        /// <summary>
        /// 런타임에서 호출되어 hillSmoothness 값 반환.
        /// MapGenParams에 값이 있으면 사용, 없으면 바닐라 기본값 반환.
        /// </summary>
        public static double GetHillSmoothness()
        {
            if (MapGenParams.HasParams && MapGenParams.HillSmoothness > 0f)
                return MapGenParams.HillSmoothness;
            return 2.0; // 바닐라 기본값
        }
    }
}
