using HarmonyLib;
using RimWorld;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MapGenAI.Patches
{
    /// <summary>
    /// 강 방향 패치: TileMutatorWorker_River.IsFlowingAToB와 GetMapEdgeNodes의
    /// angle 파라미터를 사용자 지정 각도로 교체.
    /// Map Designer 1.6 RiverDirectionPatch.cs와 동일 방식.
    ///
    /// 각도 규칙 (RimWorld 내부):
    /// 0도=오른쪽, 90도=위, 180도=왼쪽, 270도=아래
    /// "왼쪽으로 흐름" = 강이 왼쪽→오른쪽, angle=0
    /// "위로 흐름" = 강이 아래→위, angle=90
    /// </summary>
    [HarmonyPatch(typeof(TileMutatorWorker_River), "IsFlowingAToB")]
    static class Patch_RiverDirection_Flowing
    {
        static bool Prefix(Vector3 a, Vector3 b, ref float angle)
        {
            if (!MapGenParams.HasParams) return true;
            if (MapGenParams.RiverDirectionAngle < 0f) return true; // -1 = 자동

            angle = MapGenParams.RiverDirectionAngle;
            return true;
        }
    }

    [HarmonyPatch(typeof(TileMutatorWorker_River), "GetMapEdgeNodes")]
    static class Patch_RiverDirection_EdgeNodes
    {
        static bool Prefix(Map map, ref float angle)
        {
            if (!MapGenParams.HasParams) return true;
            if (MapGenParams.RiverDirectionAngle < 0f) return true; // -1 = 자동

            angle = MapGenParams.RiverDirectionAngle;
            return true;
        }
    }

    /// <summary>
    /// 강 위치(중심) 패치: TileMutatorWorker_River.GetRiverCenter Postfix로
    /// 강의 중심점을 이동.
    /// Map Designer 1.6 RiverCenterPatch와 동일 방식.
    ///
    /// RiverPosition: 0.0=왼쪽, 0.5=중앙(기본), 1.0=오른쪽
    /// 내부적으로 displacement = position - 0.5 로 변환하여
    /// __result.x = mapSize.x * (0.5 + displacement) 적용.
    /// </summary>
    [HarmonyPatch(typeof(TileMutatorWorker_River), "GetRiverCenter")]
    static class Patch_RiverCenter
    {
        static void Postfix(Map map, ref IntVec3 __result)
        {
            if (!MapGenParams.HasParams) return;

            float xPos = MapGenParams.RiverXPosition;
            float zPos = MapGenParams.RiverZPosition;

            // 기본값(0.5)이면 이동 불필요
            bool xChanged = Mathf.Abs(xPos - 0.5f) > 0.01f;
            bool zChanged = Mathf.Abs(zPos - 0.5f) > 0.01f;
            if (!xChanged && !zChanged) return;

            // Map Designer 방식: result = mapSize * (0.5 + displacement)
            // displacement = position - 0.5
            var mapSize = map.Size;
            if (xChanged)
                __result.x = (int)(mapSize.x * xPos);
            if (zChanged)
                __result.z = (int)(mapSize.z * zPos);

            Log.Message($"[MapGenAI] 강 중심 이동: ({__result.x}, {__result.z}), xPos={xPos:F2}, zPos={zPos:F2}");
        }
    }

    /// <summary>
    /// 일자 강 패치: GetCurveAmplitude를 0으로 만들어 강의 구불거림 제거.
    /// RW 1.6의 TileMutatorWorker_River.GetDisplacedPoint에서:
    ///   displaced = basePoint + noiseValue * GetCurveAmplitude * ...
    /// GetCurveAmplitude=0이면 noise 항이 0 → 완벽한 직선 강.
    /// Map Designer 1.6에서도 미구현 (RiverMaker 삭제로 1.5 코드 컴파일 불가 상태).
    /// </summary>
    [HarmonyPatch(typeof(TileMutatorWorker_River), "get_GetCurveAmplitude")]
    static class Patch_StraightRiver
    {
        static void Postfix(ref float __result)
        {
            if (!MapGenParams.HasParams) return;
            if (!MapGenParams.StraightRiver) return;

            __result = 0f;
        }
    }

    /// <summary>
    /// 일자 강 + 균일 폭: Init Postfix에서 riverWidthNoise를 Const(0)으로 교체.
    /// 폭 변동도 제거하여 완전히 균일한 운하 형태.
    /// </summary>
    [HarmonyPatch(typeof(TileMutatorWorker_River), "Init")]
    static class Patch_StraightRiver_Width
    {
        static void Postfix(TileMutatorWorker_River __instance)
        {
            if (!MapGenParams.HasParams) return;
            if (!MapGenParams.StraightRiver) return;

            try
            {
                var field = HarmonyLib.AccessTools.Field(typeof(TileMutatorWorker_River), "riverWidthNoise");
                if (field != null)
                    field.SetValue(__instance, new Const(0.0));
            }
            catch { }
        }
    }
}
