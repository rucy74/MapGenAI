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
        /// Map Designer가 로드된 경우 이 Transpiler를 스킵.
        /// 두 Transpiler가 동일한 IL 상수(0.021, 2.0)를 교체하려 해서
        /// 먼저 실행된 쪽이 상수를 메서드 호출로 바꾸면 나중 쪽이 index=-1 → crash.
        /// Map Designer가 있으면 Map Designer의 슬라이더로 해당 기능을 대체.
        /// </summary>
        static bool Prepare()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "MapDesigner")
                {
                    Log.Message("[MapGenAI] Map Designer 감지 — hill_size/hill_smoothness Transpiler 비활성화 (충돌 방지). Map Designer 슬라이더를 대신 사용하세요.");
                    return false;
                }
            }
            return true;
        }

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
                if (Prefs.DevMode) Log.Message($"[MapGenAI] Transpiler: hillSize (index={hillSizeIndex})");
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
                if (Prefs.DevMode) Log.Message($"[MapGenAI] Transpiler: hillSmoothness (index={hillSmoothnessIndex})");
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
