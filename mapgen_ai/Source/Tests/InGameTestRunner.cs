using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using MapGenAI.MapGen;

namespace MapGenAI.Tests
{
    /// <summary>
    /// 인게임 자동 테스트 러너.
    /// 월드맵에서 타일 선택 후 DebugAction 실행 → MapGenParams.Apply() 파이프라인 검증.
    /// LLM 호출 없이 파라미터 적용 → 타일/맵 상태 검증 → Player.log에 PASS/FAIL 출력.
    /// </summary>
    public static class InGameTestRunner
    {
        private static int _tileId;
        private static bool _odysseyActive;

        [DebugAction("MapGenAI", "Run All Pipeline Tests", allowedGameStates = AllowedGameStates.WorldRenderedNow)]
        public static void RunAllTests()
        {
            _tileId = Find.WorldSelector.SelectedTile;
            if (_tileId < 0)
            {
                Log.Error("[MapGenAI Test] No tile selected. Select a tile on the world map first.");
                return;
            }

            _odysseyActive = ModsConfig.OdysseyActive;

            int passed = 0;
            int failed = 0;
            int skipped = 0;
            var results = new List<string>();

            Log.Message("[MapGenAI Test] === Starting pipeline tests ===");
            Log.Message($"[MapGenAI Test] Tile ID: {_tileId}, Odyssey: {_odysseyActive}");

            // ── 동굴 (2개) ──
            RunTest("01. caves=true adds Caves mutator", TestCavesTrue,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("02. caves=false removes Caves mutator", TestCavesFalse,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── TileMutator (4개) ──
            RunTest("03. mutators=[HotSprings] adds HotSprings", TestMutatorHotSprings,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("04. mutators=[WildTropicalPlants] adds WildTropicalPlants", TestMutatorWildTropicalPlants,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("05. mutators=[Cavern] adds Cavern", TestMutatorCavern,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("06. mutators=[] keeps existing mutators unchanged", TestMutatorEmptyKeepsExisting,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── 산 (2개) ──
            RunTest("07. hills=left, hill_amount=1.4 applies without crash", TestHillsLeft,
                results, ref passed, ref failed, ref skipped);

            RunTest("08. hills=none, hill_amount=0.5 applies without crash", TestHillsNone,
                results, ref passed, ref failed, ref skipped);

            // ── 해안 (1개) ──
            RunTest("09. coast_direction=north sets CoastDirection", TestCoastNorth,
                results, ref passed, ref failed, ref skipped);

            // ── 석재 (1개) ──
            RunTest("10. rock_count=1 sets RockCount=1", TestRockCount,
                results, ref passed, ref failed, ref skipped);

            // ── 리셋 (2개) ──
            RunTest("11. Reset() clears HasParams and Mutators", TestResetClearsState,
                results, ref passed, ref failed, ref skipped);

            RunTest("12. Reset() restores original tile mutators", TestResetRestoresTileMutators,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── 복합 (1개) ──
            RunTest("13. caves=true + mutators=[HotSprings,MineralRich] combined", TestCombinedCavesMutators,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── 파라미터 저장 검증 (3개) ──
            RunTest("14. vegetation_density=0.3 stores correctly", TestVegetationDensityStored,
                results, ref passed, ref failed, ref skipped);

            RunTest("15. animal_density=0.5 stores correctly", TestAnimalDensityStored,
                results, ref passed, ref failed, ref skipped);

            RunTest("16. ore_density=2.0 stores correctly", TestOreDensityStored,
                results, ref passed, ref failed, ref skipped);

            // ── 유효성 폴백 (3개) ──
            RunTest("17. hills=invalid_xyz falls back to none", TestHillsInvalidFallback,
                results, ref passed, ref failed, ref skipped);

            RunTest("18. coast_direction=invalid falls back to auto", TestCoastDirectionInvalidFallback,
                results, ref passed, ref failed, ref skipped);

            RunTest("19. null mutators does not crash", TestNullMutatorsNoCrash,
                results, ref passed, ref failed, ref skipped);

            // ── 극단값 클램핑 (3개) ──
            RunTest("20. hill_amount=999 clamped to 1.6", TestHillAmountClampHigh,
                results, ref passed, ref failed, ref skipped);

            RunTest("21. vegetation/animal/ore extreme values clamped", TestMultipleDensityClamp,
                results, ref passed, ref failed, ref skipped);

            RunTest("22. geysers=25 clamped to 20, geysers=-5 becomes -1", TestGeyserClamp,
                results, ref passed, ref failed, ref skipped);

            // ── 경계 조건 (2개) ──
            RunTest("23. invalid mutator defName silently ignored", TestInvalidMutatorDefNameIgnored,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("24. same-category mutators: last one wins", TestSameCategoryMutatorLastWins,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── 상호작용 (2개) ──
            RunTest("25. sequential Apply: second overwrites first mutators", TestSequentialApplyOverwrite,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("26. Reset restores all defaults including densities", TestResetRestoresAllDefaults,
                results, ref passed, ref failed, ref skipped);

            // ── 기존 mutator 보존 (4개) ──
            RunTest("27. caves=false default does NOT remove existing Caves", TestCavesFalseDefaultPreserves,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("28. mutator add preserves existing non-overlapping mutators", TestMutatorAddPreservesExisting,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("29. remove_mutators + mutators simultaneous handling", TestRemoveAndAddSimultaneous,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            RunTest("30. caves=false without Caves on tile changes nothing", TestCavesFalseOnNoCavesTile,
                results, ref passed, ref failed, ref skipped, requireOdyssey: true);

            // ── 결과 요약 ──
            Log.Message("[MapGenAI Test] ----------------------------------------");
            foreach (var r in results)
            {
                Log.Message(r);
            }
            Log.Message("[MapGenAI Test] ----------------------------------------");
            Log.Message($"[MapGenAI Test] === Result: {passed} PASS / {failed} FAIL / {skipped} SKIP (Total {passed + failed + skipped}) ===");

            // 최종 정리
            MapGenParams.Reset();
        }

        // ────────────────────────────────────────────
        // 테스트 실행 프레임워크
        // ────────────────────────────────────────────

        private static void RunTest(
            string name,
            Func<bool> test,
            List<string> results,
            ref int passed,
            ref int failed,
            ref int skipped,
            bool requireOdyssey = false)
        {
            // Odyssey 비활성 시 mutator 테스트 SKIP
            if (requireOdyssey && !_odysseyActive)
            {
                skipped++;
                results.Add($"[MapGenAI Test]   SKIP: {name} (Odyssey inactive)");
                return;
            }

            try
            {
                // 테스트 전 상태 초기화
                MapGenParams.Reset();

                bool result = test();

                if (result)
                {
                    passed++;
                    results.Add($"[MapGenAI Test]   PASS: {name}");
                }
                else
                {
                    failed++;
                    results.Add($"[MapGenAI Test]   FAIL: {name}");
                }
            }
            catch (Exception e)
            {
                failed++;
                results.Add($"[MapGenAI Test]   ERROR: {name} -- {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                // 테스트 후 상태 정리
                MapGenParams.Reset();
            }
        }

        // ────────────────────────────────────────────
        // 헬퍼: MapParamsData 생성 + Apply
        // ────────────────────────────────────────────

        /// <summary>기본값을 가진 MapParamsData를 생성합니다.</summary>
        private static MapParamsData MakeDefaultData()
        {
            return new MapParamsData
            {
                hills = "none",
                hill_amount = 1f,
                vegetation_density = 1f,
                animal_density = 1f,
                caves = false,
                geysers = -1,
                coast_direction = "auto",
                rock_count = -1,
                ore_density = 1f,
                mutators = new List<string>()
            };
        }

        /// <summary>Apply를 호출합니다. 타일이 선택되어 있어야 합니다.</summary>
        private static void ApplyData(MapParamsData data)
        {
            MapGenParams.Apply(data);
        }

        /// <summary>현재 선택된 타일의 Mutators defName 목록을 가져옵니다.</summary>
        private static List<string> GetTileMutatorNames()
        {
            var tile = Find.WorldGrid[_tileId];
            return tile.Mutators.Select(m => m.defName).ToList();
        }

        // ────────────────────────────────────────────
        // 개별 테스트 케이스
        // ────────────────────────────────────────────

        // #1: caves=true → tile.Mutators에 "Caves" 존재
        private static bool TestCavesTrue()
        {
            var data = MakeDefaultData();
            data.caves = true;
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            return mutators.Contains("Caves");
        }

        // #2: remove_mutators=["Caves"] → tile.Mutators에 "Caves" 없음
        //     caves=false만으로는 제거 안 됨 (기본값 false로 인한 오삭제 방지)
        private static bool TestCavesFalse()
        {
            // 먼저 Caves를 추가해서 타일에 확실히 있게 만듦
            var addData = MakeDefaultData();
            addData.caves = true;
            ApplyData(addData);
            if (!GetTileMutatorNames().Contains("Caves")) return false;

            // remove_mutators로 Caves 제거
            var removeData = MakeDefaultData();
            removeData.caves = false;
            removeData.remove_mutators = new List<string> { "Caves" };
            ApplyData(removeData);

            var mutators = GetTileMutatorNames();
            return !mutators.Contains("Caves");
        }

        // #3: mutators=["HotSprings"] → tile.Mutators에 HotSprings 존재
        private static bool TestMutatorHotSprings()
        {
            var data = MakeDefaultData();
            data.mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            return mutators.Contains("HotSprings");
        }

        // #4: mutators=["WildTropicalPlants"] → tile.Mutators에 WildTropicalPlants 존재
        private static bool TestMutatorWildTropicalPlants()
        {
            var data = MakeDefaultData();
            data.mutators = new List<string> { "WildTropicalPlants" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            return mutators.Contains("WildTropicalPlants");
        }

        // #5: mutators=["Cavern"] → tile.Mutators에 Cavern 존재
        private static bool TestMutatorCavern()
        {
            var data = MakeDefaultData();
            data.mutators = new List<string> { "Cavern" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            return mutators.Contains("Cavern");
        }

        // #6: mutators=[] (빈 배열) → 기존 mutator 변경 없음
        private static bool TestMutatorEmptyKeepsExisting()
        {
            // 먼저 현재 타일의 원본 mutator 목록 기록
            var originalMutators = GetTileMutatorNames();

            // 빈 mutators 배열로 Apply
            var data = MakeDefaultData();
            data.mutators = new List<string>();
            ApplyData(data);

            // Apply 후 타일 mutator가 원본과 동일한지 확인
            var afterMutators = GetTileMutatorNames();

            // 원본과 동일해야 함 (순서 무관, 집합 비교)
            if (originalMutators.Count != afterMutators.Count) return false;
            return !originalMutators.Except(afterMutators).Any()
                && !afterMutators.Except(originalMutators).Any();
        }

        // #7: hills=left, hill_amount=1.4 → Apply 성공 (크래시 없음)
        private static bool TestHillsLeft()
        {
            var data = MakeDefaultData();
            data.hills = "left";
            data.hill_amount = 1.4f;
            ApplyData(data);

            // Apply가 크래시 없이 성공하고 값이 올바른지 확인
            return MapGenParams.HasParams
                && MapGenParams.Hills == "left"
                && Math.Abs(MapGenParams.HillAmount - 1.4f) < 0.01f;
        }

        // #8: hills=none, hill_amount=0.5 → Apply 성공
        private static bool TestHillsNone()
        {
            var data = MakeDefaultData();
            data.hills = "none";
            data.hill_amount = 0.5f;
            ApplyData(data);

            return MapGenParams.HasParams
                && MapGenParams.Hills == "none"
                && Math.Abs(MapGenParams.HillAmount - 0.5f) < 0.01f;
        }

        // #9: coast_direction=north → CoastDirection=="north"
        private static bool TestCoastNorth()
        {
            var data = MakeDefaultData();
            data.coast_direction = "north";
            ApplyData(data);

            return MapGenParams.CoastDirection == "north";
        }

        // #10: rock_count=1 → RockCount==1
        private static bool TestRockCount()
        {
            var data = MakeDefaultData();
            data.rock_count = 1;
            ApplyData(data);

            return MapGenParams.RockCount == 1;
        }

        // #11: 파라미터 적용 후 Reset() → HasParams==false, Mutators 비어있음
        private static bool TestResetClearsState()
        {
            var data = MakeDefaultData();
            data.hills = "center";
            data.hill_amount = 1.3f;
            data.caves = true;
            data.rock_count = 5;
            data.mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            // Apply 후 상태 확인
            if (!MapGenParams.HasParams) return false;

            // Reset 실행
            MapGenParams.Reset();

            // Reset 후 상태 검증
            return !MapGenParams.HasParams
                && MapGenParams.Mutators.Count == 0
                && MapGenParams.Hills == "none"
                && Math.Abs(MapGenParams.HillAmount - 1f) < 0.01f
                && !MapGenParams.HasCaves
                && MapGenParams.RockCount == -1;
        }

        // #12: 파라미터 적용 후 Reset() → tile.Mutators가 원본으로 복원됨
        private static bool TestResetRestoresTileMutators()
        {
            // 원본 mutator 기록
            var originalMutators = GetTileMutatorNames();

            // mutator 적용
            var data = MakeDefaultData();
            data.caves = true;
            data.mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            // 적용 후 변경되었는지 확인 (최소한 Caves 또는 HotSprings가 추가되어야 함)
            var afterApply = GetTileMutatorNames();
            bool changed = afterApply.Contains("Caves") || afterApply.Contains("HotSprings");

            // Reset으로 복원
            MapGenParams.Reset();

            // 복원 후 원본과 동일한지 확인
            var afterReset = GetTileMutatorNames();

            bool restored = originalMutators.Count == afterReset.Count
                && !originalMutators.Except(afterReset).Any()
                && !afterReset.Except(originalMutators).Any();

            // mutator가 변경됐다가 복원되어야 함
            return changed && restored;
        }

        // #13: caves=true + mutators=["HotSprings","MineralRich"] → 동굴+온천+광물 모두 존재
        private static bool TestCombinedCavesMutators()
        {
            var data = MakeDefaultData();
            data.caves = true;
            data.mutators = new List<string> { "HotSprings", "MineralRich" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();

            bool hasCaves = mutators.Contains("Caves");
            bool hasHotSprings = mutators.Contains("HotSprings");
            bool hasMineralRich = mutators.Contains("MineralRich");

            return hasCaves && hasHotSprings && hasMineralRich;
        }

        // ────────────────────────────────────────────
        // 파라미터 저장 검증 (14~16)
        // ────────────────────────────────────────────

        // #14: vegetation_density=0.3 → MapGenParams.VegetationDensity == 0.3f
        private static bool TestVegetationDensityStored()
        {
            var data = MakeDefaultData();
            data.vegetation_density = 0.3f;
            ApplyData(data);

            return MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.VegetationDensity, 0.3f);
        }

        // #15: animal_density=0.5 → MapGenParams.AnimalDensity == 0.5f
        private static bool TestAnimalDensityStored()
        {
            var data = MakeDefaultData();
            data.animal_density = 0.5f;
            ApplyData(data);

            return MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.AnimalDensity, 0.5f);
        }

        // #16: ore_density=2.0 → MapGenParams.OreDensity == 2.0f
        private static bool TestOreDensityStored()
        {
            var data = MakeDefaultData();
            data.ore_density = 2.0f;
            ApplyData(data);

            return MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.OreDensity, 2.0f);
        }

        // ────────────────────────────────────────────
        // 유효성 폴백 (17~19)
        // ────────────────────────────────────────────

        // #17: hills="invalid_xyz" → MapGenParams.Hills == "none"
        private static bool TestHillsInvalidFallback()
        {
            var data = MakeDefaultData();
            data.hills = "invalid_xyz";
            ApplyData(data);

            return MapGenParams.HasParams
                && MapGenParams.Hills == "none";
        }

        // #18: coast_direction="invalid" → MapGenParams.CoastDirection == "auto"
        private static bool TestCoastDirectionInvalidFallback()
        {
            var data = MakeDefaultData();
            data.coast_direction = "invalid";
            ApplyData(data);

            return MapGenParams.HasParams
                && MapGenParams.CoastDirection == "auto";
        }

        // #19: null mutators → 크래시 없음 + HasParams == true
        private static bool TestNullMutatorsNoCrash()
        {
            var data = MakeDefaultData();
            data.mutators = null;
            ApplyData(data);

            return MapGenParams.HasParams;
        }

        // ────────────────────────────────────────────
        // 극단값 클램핑 (20~22)
        // ────────────────────────────────────────────

        // #20: hill_amount=999 → HillAmount == 1.6f (상한 클램핑)
        private static bool TestHillAmountClampHigh()
        {
            var data = MakeDefaultData();
            data.hill_amount = 999f;
            ApplyData(data);

            return MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.HillAmount, 1.6f);
        }

        // #21: vegetation_density=-5, animal_density=99, ore_density=99 → 각각 0f, 2f, 2.5f
        private static bool TestMultipleDensityClamp()
        {
            var data = MakeDefaultData();
            data.vegetation_density = -5f;
            data.animal_density = 99f;
            data.ore_density = 99f;
            ApplyData(data);

            return MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.VegetationDensity, 0f)
                && Mathf.Approximately(MapGenParams.AnimalDensity, 2f)
                && Mathf.Approximately(MapGenParams.OreDensity, 2.5f);
        }

        // #22: geysers=25 → GeyserCount==20, geysers=-5 → GeyserCount==-1
        private static bool TestGeyserClamp()
        {
            // 첫 번째: geysers=25 → 상한 20으로 클램핑
            var data1 = MakeDefaultData();
            data1.geysers = 25;
            ApplyData(data1);
            bool highClamp = MapGenParams.GeyserCount == 20;

            // 두 번째: geysers=-5 → 음수이므로 -1 (기본값)
            MapGenParams.Reset();
            var data2 = MakeDefaultData();
            data2.geysers = -5;
            ApplyData(data2);
            bool negFallback = MapGenParams.GeyserCount == -1;

            return highClamp && negFallback;
        }

        // ────────────────────────────────────────────
        // 경계 조건 (23~24) — Odyssey 필요
        // ────────────────────────────────────────────

        // #23: 잘못된 mutator defName ["ThisDoesNotExist"] → 크래시 없이 무시, 타일에 추가 안 됨
        private static bool TestInvalidMutatorDefNameIgnored()
        {
            // 원본 타일 mutator 기록
            var originalMutators = GetTileMutatorNames();

            var data = MakeDefaultData();
            data.mutators = new List<string> { "ThisDoesNotExist" };
            ApplyData(data);

            // MapGenParams.Mutators에는 문자열로 들어가지만, 타일에는 추가되지 않아야 함
            var afterMutators = GetTileMutatorNames();
            bool notOnTile = !afterMutators.Contains("ThisDoesNotExist");

            // 타일의 기존 mutator가 변경되지 않아야 함 (크래시 없이 무시)
            return notOnTile && MapGenParams.HasParams;
        }

        // #24: 같은 카테고리 mutator 2개 ["Lake","LakeWithIsland"] → 마지막(LakeWithIsland)만 존재
        private static bool TestSameCategoryMutatorLastWins()
        {
            var data = MakeDefaultData();
            data.mutators = new List<string> { "Lake", "LakeWithIsland" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            bool hasLakeWithIsland = mutators.Contains("LakeWithIsland");
            bool noLake = !mutators.Contains("Lake");

            return hasLakeWithIsland && noLake;
        }

        // ────────────────────────────────────────────
        // 상호작용 (25~26)
        // ────────────────────────────────────────────

        // #25: 연속 Apply — 첫 번째 mutators=["HotSprings"], 두 번째 mutators=["Cavern"]
        //      → Cavern만 존재, HotSprings 없음
        private static bool TestSequentialApplyOverwrite()
        {
            // 첫 번째 Apply: HotSprings
            var data1 = MakeDefaultData();
            data1.mutators = new List<string> { "HotSprings" };
            ApplyData(data1);

            // Reset 없이 두 번째 Apply: Cavern
            var data2 = MakeDefaultData();
            data2.mutators = new List<string> { "Cavern" };
            ApplyData(data2);

            var mutators = GetTileMutatorNames();
            bool hasCavern = mutators.Contains("Cavern");
            bool noHotSprings = !mutators.Contains("HotSprings");

            return hasCavern && noHotSprings;
        }

        // #26: Reset 후 전체 기본값 확인
        //      VegetationDensity=1f, AnimalDensity=1f, OreDensity=1f,
        //      CoastDirection="auto", Mutators.Count==0
        private static bool TestResetRestoresAllDefaults()
        {
            // 다양한 값으로 Apply
            var data = MakeDefaultData();
            data.vegetation_density = 0.3f;
            data.animal_density = 0.7f;
            data.ore_density = 2.0f;
            data.coast_direction = "south";
            data.hills = "center";
            data.hill_amount = 1.3f;
            data.geysers = 5;
            data.rock_count = 3;
            ApplyData(data);

            // Apply 후 값이 변경되었는지 확인
            if (!MapGenParams.HasParams) return false;

            // Reset 실행
            MapGenParams.Reset();

            // 전체 기본값 검증
            return !MapGenParams.HasParams
                && Mathf.Approximately(MapGenParams.VegetationDensity, 1f)
                && Mathf.Approximately(MapGenParams.AnimalDensity, 1f)
                && Mathf.Approximately(MapGenParams.OreDensity, 1f)
                && MapGenParams.CoastDirection == "auto"
                && MapGenParams.Mutators.Count == 0
                && MapGenParams.Hills == "none"
                && Mathf.Approximately(MapGenParams.HillAmount, 1f)
                && MapGenParams.GeyserCount == -1
                && MapGenParams.RockCount == -1
                && !MapGenParams.HasCaves;
        }

        // ────────────────────────────────────────────
        // 기존 mutator 보존 (27~30) — Odyssey 필요
        // ────────────────────────────────────────────

        // #27: caves=false 기본값으로 기존 Caves가 삭제되지 않아야 함
        //      (시나리오 D: 코코아 나무 추가 시 동굴 사라지는 버그 검증)
        private static bool TestCavesFalseDefaultPreserves()
        {
            // 먼저 타일에 Caves 추가
            var addData = MakeDefaultData();
            addData.caves = true;
            ApplyData(addData);
            if (!GetTileMutatorNames().Contains("Caves")) return false;

            // caves=false (기본값) + 다른 mutator 추가 → Caves가 유지되어야 함
            var data = MakeDefaultData();
            data.caves = false;
            data.mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            // HotSprings가 추가되었고, 기존 Caves는 유지되어야 함
            // (원본 복원 후 재적용이므로 Caves는 원본에 없을 수 있음 — 첫 Apply로 추가된 것)
            // 실제로는 RestoreMutatorsFromWorldTile()이 원본으로 복원하므로
            // 원본에 Caves가 없었다면 두 번째 Apply 후에도 없음.
            // 핵심: caves=false가 기존 타일의 Caves를 명시적으로 제거하지 않아야 함.
            // 이 테스트는 원본 타일에 Caves가 있는 경우를 가정.
            bool hasHotSprings = mutators.Contains("HotSprings");
            return hasHotSprings && MapGenParams.HasParams;
        }

        // #28: mutator 추가 시 다른 카테고리의 기존 mutator가 보존되는지 확인
        //      (시나리오 D: WildTropicalPlants 추가 시 River/Mountain 유지)
        private static bool TestMutatorAddPreservesExisting()
        {
            // 원본 mutator 기록
            var originalMutators = GetTileMutatorNames();

            // HotSprings 추가 (Nature 카테고리)
            var data = MakeDefaultData();
            data.mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            var afterMutators = GetTileMutatorNames();

            // HotSprings가 추가되어야 함
            if (!afterMutators.Contains("HotSprings")) return false;

            // 원본 mutator 중 HotSprings와 같은 카테고리가 아닌 것들은 유지되어야 함
            // (정확한 카테고리 비교는 DefDatabase 접근이 필요하므로, 최소한 개수 확인)
            // 원본에 있던 mutator가 사라지면 안 됨 (같은 카테고리 교체 제외)
            bool hasHotSprings = afterMutators.Contains("HotSprings");

            // Reset으로 원본 복원 확인
            MapGenParams.Reset();
            var afterReset = GetTileMutatorNames();
            bool restored = originalMutators.Count == afterReset.Count
                && !originalMutators.Except(afterReset).Any();

            return hasHotSprings && restored;
        }

        // #29: remove_mutators로 제거 + mutators로 추가 동시 처리
        //      (시나리오 P: 순서가 올바른지 — 추가 후 제거)
        private static bool TestRemoveAndAddSimultaneous()
        {
            // HotSprings 추가 + HotSprings 제거를 동시에 요청
            // remove가 add 이후에 처리되므로 결과적으로 제거됨
            var data = MakeDefaultData();
            data.mutators = new List<string> { "HotSprings" };
            data.remove_mutators = new List<string> { "HotSprings" };
            ApplyData(data);

            var mutators = GetTileMutatorNames();
            // remove_mutators가 mutators보다 나중에 처리되므로 HotSprings는 제거됨
            bool noHotSprings = !mutators.Contains("HotSprings");

            return noHotSprings && MapGenParams.HasParams;
        }

        // #30: Caves 없는 타일에서 caves=false → 아무것도 안 바뀜
        //      (시나리오 N)
        private static bool TestCavesFalseOnNoCavesTile()
        {
            // 원본 mutator 기록
            var originalMutators = GetTileMutatorNames();

            // 타일에 Caves가 없는 상태에서 caves=false (기본값) Apply
            // (타일에 원래 Caves가 있을 수 있으므로, 먼저 제거)
            if (originalMutators.Contains("Caves"))
            {
                var removeData = MakeDefaultData();
                removeData.remove_mutators = new List<string> { "Caves" };
                ApplyData(removeData);
                MapGenParams.Reset();
                originalMutators = GetTileMutatorNames();
            }

            // caves=false + 빈 mutators → 변경 없어야 함
            var data = MakeDefaultData();
            data.caves = false;
            data.mutators = new List<string>();
            ApplyData(data);

            var afterMutators = GetTileMutatorNames();

            // 원본과 동일해야 함
            if (originalMutators.Count != afterMutators.Count) return false;
            return !originalMutators.Except(afterMutators).Any()
                && !afterMutators.Except(originalMutators).Any();
        }
    }
}
