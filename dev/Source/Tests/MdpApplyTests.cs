// MdpApplyTests.cs — MapGenParams.Apply() 유닛 테스트
// dotnet run --project Tests 로 실행.
// 시나리오 #2는 현재 버그로 FAIL 예상.

using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;

/// <summary>
/// MapGenParams.Apply() 동작 검증 — 7개 시나리오.
/// hills / elevation_shapes 조합에 따른 ElevationShapes 결과를 확인.
/// </summary>
static class MdpApplyTests
{
    static int _pass;
    static int _fail;

    /// <summary>테스트 러너 진입점. TestBench.Main과 분리하여 호출.</summary>
    public static void RunAll()
    {
        Console.WriteLine("=== MdpApplyTests: MapGenParams.Apply() ElevationShapes 검증 ===\n");
        _pass = 0;
        _fail = 0;

        Test1_HillsLeft_ShapesNull_AutoGeneratesSlope();
        Test2_HillsChange_ShapesNull_ShouldRegenerate();
        Test3_HillsSame_ShapesNull_KeepsExisting();
        Test4_ExplicitShapes_OverridesHills();
        Test5_EmptyArray_MeansFlat();
        Test6_HillsNone_AfterLeft_ClearsShapes();
        Test7_HillsNone_FromStart_EmptyShapes();

        Console.WriteLine($"\n=== 결과: {_pass} PASS / {_fail} FAIL (전체 {_pass + _fail}개) ===");
        if (_fail > 0)
            Console.WriteLine("*** FAIL 있음 — 버그 재현 확인 ***");
    }

    // ---------------------------------------------------------
    // #1: 첫 요청 — hills="left", elevation_shapes=null
    //     → ElevationShapes에 slope(left) 자동 생성됨
    // ---------------------------------------------------------
    static void Test1_HillsLeft_ShapesNull_AutoGeneratesSlope()
    {
        MapGenParams.Reset();

        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            elevation_shapes = null  // LLM 생략
        });

        bool ok =
            MapGenParams.ElevationShapes.Count == 1 &&
            MapGenParams.ElevationShapes[0].type == "slope" &&
            MapGenParams.ElevationShapes[0].direction == "left";

        Report("#1 hills=left, shapes=null -> slope(left) 자동 생성", ok,
            $"Count={MapGenParams.ElevationShapes.Count}" +
            (MapGenParams.ElevationShapes.Count > 0
                ? $", type={MapGenParams.ElevationShapes[0].type}, dir={MapGenParams.ElevationShapes[0].direction}"
                : ""));
    }

    // ---------------------------------------------------------
    // #2: hills 변경 — hills="right", elevation_shapes=null
    //     → ElevationShapes가 slope(right)로 변경되어야 함
    //     *** 현재 버그: 이전 slope(left)가 그대로 남아 FAIL ***
    // ---------------------------------------------------------
    static void Test2_HillsChange_ShapesNull_ShouldRegenerate()
    {
        // #1 상태를 유지 (Reset 안 함) — 현재 ElevationShapes에 slope(left)가 있음
        MapGenParams.Apply(new MapParamsData
        {
            hills = "right",
            elevation_shapes = null  // LLM 생략
        });

        bool ok =
            MapGenParams.ElevationShapes.Count == 1 &&
            MapGenParams.ElevationShapes[0].type == "slope" &&
            MapGenParams.ElevationShapes[0].direction == "right";

        Report("#2 hills=left->right, shapes=null -> slope(right)로 변경 [버그 재현]", ok,
            $"Count={MapGenParams.ElevationShapes.Count}" +
            (MapGenParams.ElevationShapes.Count > 0
                ? $", type={MapGenParams.ElevationShapes[0].type}, dir={MapGenParams.ElevationShapes[0].direction}"
                : ""));
    }

    // ---------------------------------------------------------
    // #3: hills 동일 유지 — hills="left" + animal_density=1.5
    //     → 기존 shapes 유지 (slope(left) 그대로)
    // ---------------------------------------------------------
    static void Test3_HillsSame_ShapesNull_KeepsExisting()
    {
        MapGenParams.Reset();

        // 먼저 hills="left" 적용
        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            elevation_shapes = null
        });

        // 같은 hills로 다시 적용 (다른 파라미터만 변경)
        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            animal_density = 1.5f,
            elevation_shapes = null  // LLM 생략
        });

        bool ok =
            MapGenParams.ElevationShapes.Count == 1 &&
            MapGenParams.ElevationShapes[0].type == "slope" &&
            MapGenParams.ElevationShapes[0].direction == "left" &&
            Math.Abs(MapGenParams.AnimalDensity - 1.5f) < 0.01f;

        Report("#3 hills=left 유지 + animal_density 변경 -> shapes 유지", ok,
            $"Count={MapGenParams.ElevationShapes.Count}, AnimalDensity={MapGenParams.AnimalDensity:F1}" +
            (MapGenParams.ElevationShapes.Count > 0
                ? $", dir={MapGenParams.ElevationShapes[0].direction}"
                : ""));
    }

    // ---------------------------------------------------------
    // #4: 커스텀 shapes (split 등) — hills base layer + 커스텀 보존
    //     2-layer 모델: hills-slot shapes는 LLM이 보내도 제거됨,
    //     커스텀 shapes(split, bump+water 등)는 보존됨
    // ---------------------------------------------------------
    static void Test4_ExplicitShapes_OverridesHills()
    {
        MapGenParams.Reset();

        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            elevation_shapes = new List<ElevationShape>
            {
                // hills-slot shape (LLM이 복사한 거) → 제거됨
                new ElevationShape { type = "slope", direction = "left", strength = "medium" },
                // 커스텀 shape → 보존됨
                new ElevationShape { type = "bump", position = "center", size = "medium", strength = "medium", fill = "water" }
            }
        });

        // 결과: [slope(left) from hills base] + [bump(center, water) custom]
        bool ok =
            MapGenParams.ElevationShapes.Count == 2 &&
            MapGenParams.ElevationShapes[0].type == "slope" &&
            MapGenParams.ElevationShapes[0].direction == "left" &&
            MapGenParams.ElevationShapes[1].type == "bump" &&
            MapGenParams.ElevationShapes[1].fill == "water";

        Report("#4 커스텀 shapes(lake) + hills base layer 공존", ok,
            $"Count={MapGenParams.ElevationShapes.Count}" +
            (MapGenParams.ElevationShapes.Count > 0
                ? $", [0]={MapGenParams.ElevationShapes[0].type}({MapGenParams.ElevationShapes[0].direction})"
                    + (MapGenParams.ElevationShapes.Count > 1 ? $", [1]={MapGenParams.ElevationShapes[1].type}(fill={MapGenParams.ElevationShapes[1].fill})" : "")
                : ""));
    }

    // ---------------------------------------------------------
    // #5: 빈 배열 — elevation_shapes=[] + hills="left"
    //     MDP: LLM이 빈 배열을 명시 = "shapes 없음"이 의도. 평지.
    //     hills fallback은 LLM이 shapes를 안 보냈을 때만 작동.
    // ---------------------------------------------------------
    static void Test5_EmptyArray_MeansFlat()
    {
        MapGenParams.Reset();

        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            elevation_shapes = new List<ElevationShape>()  // LLM이 명시적으로 비움
        });

        // MDP: LLM이 []를 보냄 = shapes 없음이 의도
        bool ok = MapGenParams.ElevationShapes.Count == 0;

        Report("#5 elevation_shapes=[] -> LLM 의도 존중, shapes 비어있음 (MDP)", ok,
            $"Count={MapGenParams.ElevationShapes.Count}");
    }

    // ---------------------------------------------------------
    // #6: 산 제거 — hills="left" 후, hills="none" + elevation_shapes=null
    //     → shapes 비어있음
    // ---------------------------------------------------------
    static void Test6_HillsNone_AfterLeft_ClearsShapes()
    {
        MapGenParams.Reset();

        // 먼저 hills="left" 적용
        MapGenParams.Apply(new MapParamsData
        {
            hills = "left",
            elevation_shapes = null
        });
        // slope(left)가 있는 상태

        // hills="none"으로 변경
        MapGenParams.Apply(new MapParamsData
        {
            hills = "none",
            elevation_shapes = null  // LLM 생략
        });

        // hills="none"이므로 자동 변환도 안 되지만,
        // 기존 shapes가 null 스킵으로 유지될 수 있음
        bool ok = MapGenParams.ElevationShapes.Count == 0;

        Report("#6 hills=left -> none, shapes=null -> shapes 비어있음", ok,
            $"Count={MapGenParams.ElevationShapes.Count}" +
            (MapGenParams.ElevationShapes.Count > 0
                ? $", type={MapGenParams.ElevationShapes[0].type}, dir={MapGenParams.ElevationShapes[0].direction}"
                : ""));
    }

    // ---------------------------------------------------------
    // #7: 처음부터 none — hills="none", elevation_shapes=null
    //     → shapes 비어있음
    // ---------------------------------------------------------
    static void Test7_HillsNone_FromStart_EmptyShapes()
    {
        MapGenParams.Reset();

        MapGenParams.Apply(new MapParamsData
        {
            hills = "none",
            elevation_shapes = null
        });

        bool ok = MapGenParams.ElevationShapes.Count == 0;

        Report("#7 처음부터 hills=none, shapes=null -> shapes 비어있음", ok,
            $"Count={MapGenParams.ElevationShapes.Count}");
    }

    // ---------------------------------------------------------
    // 헬퍼
    // ---------------------------------------------------------
    static void Report(string name, bool passed, string detail)
    {
        if (passed)
        {
            _pass++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        -> {detail}");
        }
    }
}
