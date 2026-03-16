using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using MapGenAI.MapGen;
using UnityEngine;
using Verse;

namespace MapGenAI.Patches
{
    /// <summary>
    /// GenStep_ElevationFertility Postfix: ElevationShape 프리미티브 시스템.
    /// 5개 프리미티브(slope, radial, split, bump, noise)를 additive로 조합하여 지형 생성.
    /// 하위 호환: hills/hill_amount도 ElevationShape로 자동 변환되어 동일 경로로 처리.
    /// Map Preview 미리보기 생성 시에도 적용됨.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_ElevationFertility), "Generate")]
    static class Patch_GenStep_ElevationFertility
    {
        // Slope 모드의 기본 승수: Map Designer 기본값 참조
        private const float SlopeMultiplier = 0.006f;

        // Noise 기본 주파수
        private const float NoiseBaseFreq = 0.05f;

        static void Postfix(Map map)
        {
            if (!MapGenParams.HasParams) return;

            var elevGrid = MapGenerator.Elevation;
            if (elevGrid == null) return;

            var mapBounds = CellRect.WholeMap(map);

            // 1. hill_amount 전역 오프셋 (Map Designer: elevation += hillAmount - 1f)
            float hillAmount = MapGenParams.HillAmount;
            float offset = hillAmount - 1f;
            if (Mathf.Abs(offset) > 0.001f)
            {
                foreach (var cell in mapBounds)
                    elevGrid[cell] += offset;
            }

            // 2. 각 ElevationShape 적용 (비-물 먼저, 물 나중 → 산맥 위 호수 정상 생성)
            var shapes = MapGenParams.ElevationShapes;
            float fertOffset = MapGenParams.FertilityOffset;
            if (shapes.Count == 0 && Mathf.Abs(fertOffset) < 0.01f) return;

            foreach (var shape in shapes)
            {
                if (shape.fill != "water")
                    ApplyShape(shape, map, elevGrid);
            }
            foreach (var shape in shapes)
            {
                if (shape.fill == "water")
                    ApplyShape(shape, map, elevGrid);
            }

            // 3. Fertility 오프셋 적용 (기름진 토양 증감)
            if (Mathf.Abs(fertOffset) > 0.01f)
            {
                var fertGrid = MapGenerator.Fertility;
                if (fertGrid != null)
                {
                    foreach (var cell in mapBounds)
                    {
                        // 물 영역(-1000 이하)은 건드리지 않음
                        if (fertGrid[cell] > -500f)
                            fertGrid[cell] += fertOffset;
                    }
                }
            }

            // 4. 최종 클램핑
            foreach (var cell in mapBounds)
            {
                elevGrid[cell] = Mathf.Clamp(elevGrid[cell], -1f, 1f);
            }

            // 4. 자동 진단 (개발 모드에서만)
            if (Prefs.DevMode && shapes.Count > 0)
            {
                try
                {
                    var fertGrid = MapGenerator.Fertility;
                    int waterDeep = 0, waterShallow = 0, mountain = 0, normal = 0;
                    foreach (var cell in mapBounds)
                    {
                        if (fertGrid != null && fertGrid[cell] <= -2000f) waterDeep++;
                        else if (fertGrid != null && fertGrid[cell] <= -1000f) waterShallow++;
                        else if (elevGrid[cell] >= 0.7f) mountain++;
                        else normal++;
                    }
                    int total = map.Size.x * map.Size.z;
                    Log.Message($"[MapGenAI DIAG] shapes={shapes.Count}, 깊은물={waterDeep}({waterDeep*100/total}%), " +
                        $"얕은물={waterShallow}({waterShallow*100/total}%), 산={mountain}({mountain*100/total}%), " +
                        $"일반={normal}({normal*100/total}%)");
                }
                catch { }
            }
        }

        /// <summary>Shape 디스패처</summary>
        private static void ApplyShape(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            switch (shape.type)
            {
                case "slope":  ApplySlope(shape, map, grid);  break;
                case "radial": ApplyRadial(shape, map, grid); break;
                case "split":  ApplySplit(shape, map, grid);  break;
                case "bump":   ApplyBump(shape, map, grid);   break;
                case "noise":  ApplyNoise(shape, map, grid);  break;
                case "ring":   ApplyRing(shape, map, grid);   break;
                default:
                    Log.Warning($"[MapGenAI] 알 수 없는 ElevationShape type: {shape.type}");
                    break;
            }
        }

        /// <summary>
        /// slope: 한쪽이 높고 반대쪽이 낮은 경사면.
        /// elevation += strength * (dx * cos(angle) + dz * sin(angle)) / mapSize
        /// Map Designer의 AxisAsValueX + Rotate 패턴 참조.
        /// </summary>
        private static void ApplySlope(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float angleDeg = ElevationShape.ParseDirection(shape.direction);

            float thetaRad = angleDeg * Mathf.Deg2Rad;
            float cosTheta = Mathf.Cos(thetaRad);
            float sinTheta = Mathf.Sin(thetaRad);

            float centerX = map.Center.x;
            float centerZ = map.Center.z;
            float multiplier = SlopeMultiplier * strength;

            foreach (var cell in CellRect.WholeMap(map))
            {
                float dx = cell.x - centerX;
                float dz = cell.z - centerZ;
                float slope = dx * cosTheta + dz * sinTheta;
                grid[cell] += slope * multiplier;
            }
        }

        /// <summary>
        /// radial: 가장자리에서 높고 중심에서 낮음 (또는 strength 부호 반전 시 반대).
        /// elevation += strength * (distance_from_center - centerSize) / size
        /// Map Designer의 Radial 모드 참조.
        /// </summary>
        private static void ApplyRadial(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float size = ElevationShape.ParseSize(shape.size);

            // Map Designer Radial: 유클리드 거리 + centerSize
            float centerX = map.Center.x;
            float centerZ = map.Center.z;
            int mapHalf = map.Size.x / 2;
            float centerSize = size * mapHalf;

            foreach (var cell in CellRect.WholeMap(map))
            {
                float dx = cell.x - centerX;
                float dz = cell.z - centerZ;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                grid[cell] += strength * (distance - centerSize) / mapHalf;
            }
        }

        /// <summary>
        /// split: 특정 방향 축을 따라 양쪽에 산, 중앙에 골짜기.
        /// elevation += strength * (|projected| - gap) / mapSize
        /// Map Designer의 Split 모드 참조.
        /// </summary>
        private static void ApplySplit(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float gap = ElevationShape.ParseGap(shape.gap);
            float angleDeg = ElevationShape.ParseDirection(shape.direction);

            float thetaRad = angleDeg * Mathf.Deg2Rad;
            float cosTheta = Mathf.Cos(thetaRad);
            float sinTheta = Mathf.Sin(thetaRad);

            float centerX = map.Center.x;
            float centerZ = map.Center.z;
            float mapSize = Mathf.Max(map.Size.x, map.Size.z);
            float gapCells = gap * mapSize;

            foreach (var cell in CellRect.WholeMap(map))
            {
                float dx = cell.x - centerX;
                float dz = cell.z - centerZ;
                // 축에 수직 방향으로 투영
                float projected = dx * cosTheta + dz * sinTheta;
                float absDist = Mathf.Abs(projected);
                if (strength < 0)
                {
                    // 산맥 모드: 중심 축에 산을 직접 배치 + Perlin 노이즈로 자연스러운 가장자리
                    float noise = Mathf.PerlinNoise(
                        cell.x * 0.03f + cell.z * 0.02f + 500f,
                        cell.z * 0.03f + cell.x * 0.02f + 300f) * gapCells * 0.6f;
                    float noisyDist = absDist + noise - gapCells * 0.3f;
                    float falloff = noisyDist / Mathf.Max(gapCells, 1f);
                    if (falloff < 1f)
                        grid[cell] = Mathf.Max(grid[cell], 0.85f - Mathf.Max(falloff, 0f) * 0.3f);
                }
                else
                {
                    // 협곡 모드 (positive strength): 중심 축을 낮추고 양쪽을 높임
                    float val = strength * 2f * (absDist - gapCells) / mapSize;
                    grid[cell] += val;
                }
            }
        }

        /// <summary>
        /// bump: 가우시안 돌출 (산봉우리 또는 움푹 패인 지형).
        /// elevation += strength * exp(-(dist^2) / (2*radius^2))
        /// fill="water"일 때 bump 영역 내 elevation &lt; 0인 셀의 fertility를 물 임계값으로 설정.
        /// </summary>
        private static void ApplyBump(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float size = ElevationShape.ParseSize(shape.size);
            Vector2 pos = ElevationShape.ParsePosition(shape.position);
            bool fillWater = shape.fill == "water";

            float mapW = map.Size.x;
            float mapH = map.Size.z;
            float posX = pos.x * mapW;
            float posZ = pos.y * mapH;

            // 호수(fill=water)는 맵의 일부만 차지해야 함 — 스케일 축소
            // small=15셀, medium=30셀, large=50셀 (맵 250기준 6-20%)
            // 일반 bump(언덕)은 더 넓게 — small=30, medium=60, large=90
            float radiusScale = fillWater ? 0.15f : 0.5f;
            float minRadius = fillWater ? 10f : 20f;
            float radius = Mathf.Max(size * Mathf.Min(mapW, mapH) * radiusScale, minRadius);
            float radiusSq2 = 2f * radius * radius;

            // 접근할 범위를 제한 (3 sigma 밖은 무시)
            float maxRange = radius * 3f;

            MapGenFloatGrid fertilityGrid = null;
            if (fillWater)
            {
                try { fertilityGrid = MapGenerator.Fertility; } catch { }
            }

            // 호수용 노이즈 — Verse.Noise.Perlin 사용 (RimWorld/Map Designer와 동일 엔진)
            Verse.Noise.ModuleBase lakeNoise = null;
            float noiseRoundness = 1.5f;
            if (fillWater)
            {
                lakeNoise = new Verse.Noise.Perlin(
                    0.021, 2.0, 0.5, 6, Rand.Range(0, 2147483647),
                    Verse.Noise.QualityMode.High);
            }

            foreach (var cell in CellRect.WholeMap(map))
            {
                float dx = cell.x - posX;
                float dz = cell.z - posZ;
                float distSq = dx * dx + dz * dz;

                // 범위 밖은 스킵
                if (distSq > maxRange * maxRange) continue;

                float gaussian = Mathf.Exp(-distSq / radiusSq2); // 0~1 마스크

                if (fillWater)
                {
                    // 호수 모드: Perlin 노이즈로 불규칙한 해안선 + fertility 음수 → 물 지형
                    // elevation 체크 제거: 산맥 위에도 호수가 올바르게 생성됨 (물이 지형을 깎음)
                    if (fertilityGrid != null)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        float noise = lakeNoise != null ? (float)lakeNoise.GetValue(cell) : 0f;
                        float lakeVal = noiseRoundness * noise + 0.1f * (radius - dist);

                        if (lakeVal > radius * 0.05f)       // 깊은 물 (중심)
                        {
                            fertilityGrid[cell] = -2005f;
                            grid[cell] = Mathf.Min(grid[cell], 0.3f);
                        }
                        else if (lakeVal > 0f)               // 얕은 물 (가장자리)
                        {
                            fertilityGrid[cell] = -1025f;
                            grid[cell] = Mathf.Min(grid[cell], 0.3f);
                        }
                        else if (lakeVal > -radius * 0.03f)  // 해변
                            fertilityGrid[cell] = 1f;
                    }
                }
                else
                {
                    // 일반 bump: elevation에 가산 (언덕/함몰)
                    grid[cell] += strength * gaussian;
                }
            }
        }

        /// <summary>
        /// noise: 펄린 노이즈로 불규칙한 지형.
        /// elevation += strength * PerlinNoise(x*freq, z*freq)
        /// Map Designer의 Clumping 모드 참조.
        /// </summary>
        private static void ApplyNoise(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float size = ElevationShape.ParseSize(shape.size);

            // Verse.Noise.Perlin 사용 (RimWorld/Map Designer와 동일, 6옥타브)
            // size가 클수록 주파수 낮음 (큰 덩어리)
            double freq = 0.021 / Mathf.Max(size, 0.1f);
            var noiseModule = new Verse.Noise.Perlin(
                freq, 2.0, 0.5, 6, Rand.Range(0, 2147483647),
                Verse.Noise.QualityMode.Medium);

            foreach (var cell in CellRect.WholeMap(map))
            {
                float noise = (float)noiseModule.GetValue(cell);
                grid[cell] += strength * noise;
            }
        }

        /// <summary>
        /// ring: 도넛 형태 산맥 (중심에서 일정 거리에 링 모양 능선).
        /// Map Designer의 Donut 모드 참조:
        /// distance = |sqrt((x-cx)^2+(z-cz)^2) - ringRadius| * 3.5
        /// elevation += strength * (distance - ringRadius) / mapSize
        /// position으로 중심, size로 링 반경, strength로 높이 조절.
        /// </summary>
        private static void ApplyRing(ElevationShape shape, Map map, MapGenFloatGrid grid)
        {
            float strength = ElevationShape.ParseStrength(shape.strength);
            float size = ElevationShape.ParseSize(shape.size);
            Vector2 pos = ElevationShape.ParsePosition(shape.position);

            float mapW = map.Size.x;
            float mapH = map.Size.z;
            float centerX = pos.x * mapW;
            float centerZ = pos.y * mapH;

            int mapHalf = (int)(Mathf.Min(mapW, mapH) / 2f);
            // Map Designer: donutSize = size * mapHalf * 0.78
            float ringRadius = size * mapHalf * 0.78f;

            foreach (var cell in CellRect.WholeMap(map))
            {
                float dx = cell.x - centerX;
                float dz = cell.z - centerZ;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                // Map Designer Donut: hillDonutAmt 기본값이 -0.9 (음수 = 링에 산)
                // 유저는 "strong"(양수)으로 요청하므로 부호 반전 필요
                distance -= ringRadius;
                distance *= 3.5f;
                distance = Mathf.Abs(distance);
                // 부호 반전: 양수 strength → 링에 산 (Map Designer -0.9와 동일 효과)
                grid[cell] += -strength * (distance - ringRadius) / mapHalf;
            }
        }
    }

    /// <summary>
    /// GenStep_Plants Postfix: VegetationDensity에 따라 식물 수 조절.
    /// 밀도 < 1이면 일부 식물 제거, > 1이면 추가 스폰 시도.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_Plants), "Generate")]
    static class Patch_GenStep_Plants
    {
        static void Postfix(Map map)
        {
            if (!MapGenParams.HasParams) return;
            float density = MapGenParams.VegetationDensity;
            if (Mathf.Approximately(density, 1f)) return;

            if (density < 1f)
            {
                // 밀도 감소: 확률적으로 식물 제거
                float removeChance = 1f - density;
                var plants = map.listerThings?.ThingsInGroup(ThingRequestGroup.Plant)?.ToList();
                if (plants != null)
                {
                    foreach (var plant in plants)
                    {
                        if (Rand.Value < removeChance)
                            plant.Destroy();
                    }
                }
            }
            // density > 1은 fertility 조정으로 이미 반영됨 (더 비옥한 토양 → 더 많은 식물 자연 성장)
        }
    }

    /// <summary>
    /// GenStep_Animals Postfix: AnimalDensity에 따라 동물 수 조절.
    /// density &lt; 1이면 스폰된 동물 일부 제거.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_Animals), "Generate")]
    static class Patch_GenStep_Animals
    {
        static void Postfix(Map map)
        {
            if (!MapGenParams.HasParams) return;
            float density = MapGenParams.AnimalDensity;
            if (Mathf.Approximately(density, 1f) || density >= 1f) return;

            // 밀도 감소: 스폰된 동물을 확률적으로 제거
            float removeChance = 1f - density;
            var pawns = map.mapPawns?.AllPawnsSpawned?.Where(p => p.RaceProps?.Animal == true)?.ToList();
            if (pawns != null)
            {
                foreach (var animal in pawns)
                {
                    if (Rand.Value < removeChance && !animal.Dead)
                        animal.Destroy();
                }
            }
        }
    }

    // TileMutator 패치 제거됨 — MapGenParams.Apply()에서 월드 타일에 직접 적용 (Map Designer 방식).
    // GenStep 시점에서 하면 복원 문제, Map Preview 충돌 등 버그 발생.
    // 이제 Apply()가 mutator를 영구 변경하고, Reset()이 원본 복원.

    /* 이전 TileMutator 패치 (비활성) ───────────────────────
    /// <summary>
    /// TileMutator 교체 및 복원 시스템.
    /// Map Preview도 GenerateContentsIntoMap을 호출하므로 미리보기 생성 시
    /// 바닐라 River/Coast mutator가 영구 삭제되어 강/해안이 표시 안 되는 문제 발생.
    ///
    /// 해결 전략 (2개 패치):
    ///
    /// 1) GenerateMap Prefix: 실제 맵 생성 시 mutator를 GenStep 수집 전에 변경.
    ///    바닐라의 foreach(tile.Mutators) extraGenSteps 수집보다 앞서 실행되어
    ///    커스텀 mutator의 extraGenSteps가 정상적으로 파이프라인에 포함됨.
    ///    실제 맵에서는 변경을 영구 유지 (복원 안 함).
    ///
    /// 2) GenerateContentsIntoMap Prefix/Postfix: Map Preview 전용 보호.
    ///    Map Preview는 GenerateMap을 거치지 않고 직접 호출하므로,
    ///    Prefix에서 mutator 변경 + Postfix에서 원본 복원.
    ///    Map Preview의 LINQ lazy evaluation 덕분에 Prefix 변경이
    ///    extraGenSteps 수집에 반영됨.
    /// </summary>
    static class TileMutatorHelper
    {
        /// <summary>
        /// 타일에 커스텀 mutator를 적용. 같은 카테고리의 기존 mutator는 교체.
        /// </summary>
        /// <returns>적용 전 원본 mutator 목록 (복원용). 변경 없으면 null.</returns>
        public static List<TileMutatorDef> ApplyMutators(Map map)
        {
            if (!MapGenParams.HasParams) return null;
            if (MapGenParams.Mutators.Count == 0) return null;
            if (!ModsConfig.OdysseyActive) return null;

            var tile = map.TileInfo;
            if (tile == null) return null;

            // 원본 mutator 목록 저장
            var originalMutators = tile.Mutators.ToList();
            bool changed = false;

            foreach (var defName in MapGenParams.Mutators)
            {
                var mutDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                if (mutDef == null)
                {
                    Log.Warning($"[MapGenAI] TileMutator '{defName}' not found");
                    continue;
                }

                // River 카테고리 mutator는 강이 있어야 함
                if (mutDef.categories.Contains("River"))
                {
                    var surfaceTile = tile as SurfaceTile;
                    if (surfaceTile?.Rivers == null || surfaceTile.Rivers.Count == 0)
                    {
                        Log.Message($"[MapGenAI] '{defName}' 스킵 — 강이 없는 타일");
                        continue;
                    }
                }

                // 같은 카테고리의 기존 mutator 제거 (충돌 방지)
                var existingToRemove = tile.Mutators
                    .Where(m => m.categories.Any(c => mutDef.categories.Contains(c)))
                    .ToList();
                foreach (var old in existingToRemove)
                    tile.RemoveMutator(old);

                tile.AddMutator(mutDef);
                changed = true;
                Log.Message($"[MapGenAI] TileMutator 추가: {mutDef.label} ({defName})");
            }

            if (!changed) return null;

            // 새로 추가된 mutator 초기화
            foreach (var defName in MapGenParams.Mutators)
            {
                var mutDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                if (mutDef != null && tile.Mutators.Contains(mutDef))
                    mutDef.Worker?.Init(map);
            }

            return originalMutators;
        }

        /// <summary>
        /// 원본 mutator 목록 복원.
        /// </summary>
        public static void RestoreMutators(Tile tile, List<TileMutatorDef> originalMutators)
        {
            if (tile == null || originalMutators == null) return;

            var currentMutators = tile.Mutators.ToList();
            foreach (var m in currentMutators)
                tile.RemoveMutator(m);

            foreach (var m in originalMutators)
                tile.AddMutator(m);
        }
    }

    /// <summary>
    /// GenerateMap Prefix: 실제 맵 생성 시 mutator를 GenStep 수집 전에 변경.
    /// 이 패치는 바닐라 GenerateMap의 foreach(tile.Mutators) extraGenSteps 수집보다
    /// 앞서 실행되어, 커스텀 mutator의 extraGenSteps가 파이프라인에 포함됨.
    /// 실제 맵은 사용자가 선택한 구성이므로 변경을 영구 유지함.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    static class Patch_TileMutator_GenerateMap
    {
        // GenerateMap 진입 플래그. GenerateContentsIntoMap 패치에서 중복 적용 방지용.
        // Map Preview는 별도 스레드이므로 ThreadStatic 사용.
        [ThreadStatic]
        internal static bool _insideGenerateMap;

        [HarmonyPriority(Priority.First)]
        static void Prefix(MapParent parent)
        {
            _insideGenerateMap = true;

            if (!MapGenParams.HasParams || MapGenParams.Mutators.Count == 0) return;
            if (!ModsConfig.OdysseyActive) return;
            if (parent == null) return;

            try
            {
                var worldGrid = Find.WorldGrid;
                if (worldGrid == null) return;
                var tile = worldGrid[parent.Tile];
                if (tile == null) return;

                // 실제 맵 생성: mutator를 영구적으로 변경 (복원 안 함)
                // 임시 Map 객체가 아직 없으므로 Init(map)은 바닐라 코드에서 처리
                foreach (var defName in MapGenParams.Mutators)
                {
                    var mutDef = DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                    if (mutDef == null)
                    {
                        Log.Warning($"[MapGenAI] TileMutator '{defName}' not found");
                        continue;
                    }

                    if (mutDef.categories.Contains("River"))
                    {
                        var surfaceTile = tile as SurfaceTile;
                        if (surfaceTile?.Rivers == null || surfaceTile.Rivers.Count == 0)
                        {
                            Log.Message($"[MapGenAI] '{defName}' 스킵 — 강이 없는 타일");
                            continue;
                        }
                    }

                    var existingToRemove = tile.Mutators
                        .Where(m => m.categories.Any(c => mutDef.categories.Contains(c)))
                        .ToList();
                    foreach (var old in existingToRemove)
                        tile.RemoveMutator(old);

                    tile.AddMutator(mutDef);
                    Log.Message($"[MapGenAI] TileMutator 추가 (실제 맵): {mutDef.label} ({defName})");
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[MapGenAI] TileMutator 적용 실패 (GenerateMap): {e.Message}");
            }
        }

        static void Postfix()
        {
            _insideGenerateMap = false;
        }
    }

    /// <summary>
    /// GenerateContentsIntoMap Prefix/Postfix: Map Preview 전용 mutator 보호.
    /// Map Preview는 GenerateMap을 거치지 않고 직접 호출하므로,
    /// 여기서 mutator를 임시 변경하고 완료 후 복원.
    /// GenerateMap 내부에서 호출된 경우 (실제 맵 생성)는 스킵.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_TileMutator_Preview
    {
        // Map Preview는 별도 스레드에서 실행되므로 ThreadStatic 사용.
        [ThreadStatic]
        private static List<TileMutatorDef> _savedOriginalMutators;
        [ThreadStatic]
        private static Tile _savedTile;

        [HarmonyPriority(Priority.First)]
        static void Prefix(Map map)
        {
            _savedOriginalMutators = null;
            _savedTile = null;

            // GenerateMap 내부에서 호출된 경우 스킵 (이미 Patch_TileMutator_GenerateMap에서 처리)
            if (Patch_TileMutator_GenerateMap._insideGenerateMap) return;

            if (!MapGenParams.HasParams) return;
            if (MapGenParams.Mutators.Count == 0) return;
            if (!ModsConfig.OdysseyActive) return;

            try
            {
                var tile = map.TileInfo;
                if (tile == null) return;

                _savedTile = tile;
                _savedOriginalMutators = TileMutatorHelper.ApplyMutators(map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[MapGenAI] TileMutator 적용 실패 (Preview): {e.Message}");
            }
        }

        /// <summary>
        /// 원본 mutator 복원. Map Preview 생성 완료 후 월드 타일 데이터를 원래대로 되돌림.
        /// </summary>
        static void Postfix()
        {
            if (_savedOriginalMutators == null || _savedTile == null) return;

            try
            {
                TileMutatorHelper.RestoreMutators(_savedTile, _savedOriginalMutators);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[MapGenAI] TileMutator 복원 실패: {e.Message}");
            }
            finally
            {
                _savedOriginalMutators = null;
                _savedTile = null;
            }
        }
    }

    이전 TileMutator 패치 끝 ─────────────────────── */

    /// <summary>
    /// MapGenerator.GenerateMap Postfix: 맵 생성 완료 후 파라미터 자동 리셋.
    /// 다음 맵에 이전 AI 파라미터가 누출되지 않도록 함.
    /// </summary>
    [HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
    static class Patch_MapGenerator_Reset
    {
        static void Postfix()
        {
            if (MapGenParams.HasParams)
            {
                Log.Message("[MapGenAI] 맵 생성 완료 — 파라미터 자동 리셋");
                MapGenParams.Reset();
            }
        }
    }
}
