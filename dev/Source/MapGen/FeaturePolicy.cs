using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.MapGen
{
    // Use resolved DefDatabase data, including inheritance and other mods' XML patches.
    // Natural spawn probability and existing-feature priority are not environmental requirements.
    public static class FeaturePolicy
    {
        public static bool HasRiver(Tile tile) => tile is SurfaceTile surface && surface.Rivers?.Count > 0;

        public static List<Tile> WaterNeighbors(Tile tile)
        {
            var neighbors = new List<PlanetTile>();
            tile.tile.Layer.GetTileNeighbors(tile.tile, neighbors);
            return neighbors.Select(n => n.Tile).Where(t => t.PrimaryBiome == BiomeDefOf.Ocean || t.PrimaryBiome == BiomeDefOf.Lake).ToList();
        }

        public static string UnavailableReason(TileMutatorDef def, Tile tile)
        {
            if (tile == null) return "유효한 월드 타일이 필요합니다 / Requires a world tile";
            try
            {
                var water = WaterNeighbors(tile);
                var surface = tile as SurfaceTile;
                int rivers = surface?.Rivers?.Count ?? 0;
                var worker = def.Worker;
                bool riverWorker = IsWorker(worker, "RimWorld.TileMutatorWorker_River");
                if ((def.categories.Contains("River") || riverWorker) && rivers == 0)
                    return "월드 강이 있는 타일에서만 가능합니다 / Requires an existing world river";
                if (def.categories.Contains("Coast") && water.Count == 0)
                    return "내륙에는 해안을 만들 수 없습니다 / Requires an existing ocean or lake shore";
                if (IsWorker(worker, "RimWorld.TileMutatorWorker_RiverDelta") &&
                    (water.Count == 0 || !surface.Rivers.Any(r => !r.neighbor.Tile.WaterCovered)))
                    return "삼각주는 육지에서 오는 강과 바다·호수 하구가 필요합니다 / Delta requires a land river and coastal outlet";
                if (IsWorker(worker, "RimWorld.TileMutatorWorker_RiverConfluence") &&
                    (rivers < 3 || surface.Rivers.Count(r => !r.neighbor.Tile.WaterCovered) < 2))
                    return "합류점은 세 개 이상의 강 연결과 두 육지 강 연결이 필요합니다 / Requires a world river junction";
                if (IsWorker(worker, "RimWorld.TileMutatorWorker_Headwater") && rivers != 1)
                    return "발원지는 강 연결이 하나인 타일에서만 가능합니다 / Headwater requires one river link";
                if (IsWorker(worker, "RimWorld.TileMutatorWorker_RiverIsland") && rivers < 2)
                    return "강 섬은 통과하는 강 연결이 필요합니다 / River island requires at least two river links";
                if (IsWorker(worker, "RimWorld.TileMutatorWorker_Lakeshore") && !water.Any(t => t.PrimaryBiome == BiomeDefOf.Lake))
                    return "월드 호수에 접한 타일에서만 가능합니다 / Requires an adjacent world lake";
                if (worker?.GetType().GetMethod("GetCoastAngle", BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType.FullName == "RimWorld.TileMutatorWorker_Coast"
                    && !water.Any(t => t.PrimaryBiome == BiomeDefOf.Ocean))
                    return "이 해안 생성기는 월드 바다에 접해야 합니다 / This coast worker requires an adjacent ocean";
                if (!def.EverValid()) return "필요한 세력이 현재 월드에 없습니다 / Required faction is absent";
                var biome = tile.PrimaryBiome;
                if (biome == null) return "타일 바이옴을 확인할 수 없습니다 / Missing biome";
                if (def.biomeWhitelist != null && !def.biomeWhitelist.Contains(biome))
                    return "허용 바이옴 / Allowed biomes: " + string.Join(", ", def.biomeWhitelist.Select(b => b.label ?? b.defName));
                if (def.biomeBlacklist?.Contains(biome) == true) return "이 바이옴에서는 생성할 수 없습니다 / Excluded biome: " + biome.defName;
                if (!def.animalDensityRange.Includes(biome.animalDensity) || !def.plantDensityRange.Includes(biome.plantDensity))
                    return "바이옴의 동물·식물 조건이 맞지 않습니다 / Biome animal or plant density requirement";
                if (!def.pollutionRange.Includes(tile.pollution)) return "타일 오염도 조건이 맞지 않습니다 / Pollution requirement";
                if (!def.averageTemperatureRange.Includes(tile.temperature)) return "타일 온도 조건이 맞지 않습니다 / Temperature requirement";
                if ((def.minHilliness != Hilliness.Undefined && tile.hilliness < def.minHilliness) ||
                    (def.maxHilliness != Hilliness.Undefined && tile.hilliness > def.maxHilliness))
                    return "월드 지형 조건 / World hilliness required: " + def.minHilliness + " ~ " + def.maxHilliness;
                if (def.coastSidesRange != IntRange.Invalid && (water.Count < def.coastSidesRange.min || water.Count > def.coastSidesRange.max))
                    return "해안 접면 수 조건 / Coastal sides required: " + def.coastSidesRange;
                if (rivers > 0 && !def.canSpawnOnRiver) return "강이 있는 타일에서는 생성할 수 없습니다 / Excludes river tiles";
                if (surface?.Roads?.Count > 0 && !def.canSpawnOnRoad) return "도로가 있는 타일에서는 생성할 수 없습니다 / Excludes road tiles";
                // Landmark placement itself is not being re-rolled. Internal features can be edited there.
                // canSpawnOnLandmark/chanceOnNonLandmarkTile control random placement, not map generation.
                if (worker != null && !worker.IsValidTile(tile.tile, tile.tile.Layer))
                    return "이 특징의 생성기가 현재 타일을 지원하지 않습니다 / Feature worker rejects this tile";
                return null;
            }
            catch (Exception e)
            {
                return "특징 조건 확인 실패 / Could not validate " + def.defName + ": " + e.GetType().Name;
            }
        }

        static bool IsWorker(object worker, string name)
        {
            for (var type = worker?.GetType(); type != null; type = type.BaseType)
                if (type.FullName == name) return true;
            return false;
        }

        public static void ValidateRequest(MapParamsData data, Tile tile)
        {
            if (tile == null) throw new FormatException("Map editing requires a valid world tile");
            bool riverOff = data.explicitKeys.Contains("river_present") && data.river?.present == false;
            bool riverEdit = data.explicitKeys.Any(k => k.StartsWith("river_", StringComparison.Ordinal) || k == "straight_river");
            if (HasRiver(tile) && (riverOff || data.remove_categories?.Contains("River") == true || data.remove_mutators?.Contains("River") == true))
                throw new FormatException("월드 강 연결은 제거할 수 없습니다. 삼각주 등 변형만 제거하거나 강 방향을 바꿔 주세요. / World river connections must remain.");
            if (!HasRiver(tile) && riverEdit && !riverOff)
                throw new FormatException("이 타일에는 월드 강이 없습니다. 내부 수로·호수는 물 도형으로 요청하세요. / No world river on this tile.");
            bool coastal = WaterNeighbors(tile).Count > 0;
            if (coastal && (data.remove_categories?.Contains("Coast") == true || data.remove_mutators?.Any(n => n == "Coast" || n == "Lakeshore") == true))
                throw new FormatException("월드 해안 연결은 제거할 수 없습니다. 해안 변형이나 방향을 수정해 주세요. / World shore connections must remain.");
            if (!coastal && data.coast_direction != null && data.coast_direction != "auto")
                throw new FormatException("내륙 타일에는 해안 방향을 적용할 수 없습니다 / No shore on this tile.");
            foreach (var name in data.mutators ?? new List<string>())
                if (DefDatabase<TileMutatorDef>.GetNamedSilentFail(name) == null)
                    throw new FormatException("Feature is unavailable in the active mod list: " + name);
        }

        public static void ValidateState(Tile tile, TileMapState state)
        {
            if (HasRiver(tile) && (state.removeFeatureCategories.Contains("River") || state.removeMutators.Contains("River")))
                throw new FormatException("이 프리셋은 월드 강을 제거합니다 / This state removes a protected world river.");
            if (WaterNeighbors(tile).Count > 0 && (state.removeFeatureCategories.Contains("Coast") || state.removeMutators.Any(n => n == "Coast" || n == "Lakeshore")))
                throw new FormatException("이 프리셋은 월드 해안을 제거합니다 / This state removes a protected world shore.");
            if (!HasRiver(tile) && (state.hasRiver || state.riverDirectionAngle >= 0f || state.riverXPosition != .5f || state.riverZPosition != .5f || state.straightRiver))
                throw new FormatException("이 프리셋의 강 설정에는 월드 강이 필요합니다 / This state's river settings require an existing world river.");
            if (WaterNeighbors(tile).Count == 0 && state.coastDirection != "auto") throw new FormatException("This state requires an existing shore.");
        }

        // Only for already stored worlds from the earlier development build, never new presets/requests.
        public static bool UpgradeStoredConnections(Tile tile, TileMapState state)
        {
            bool changed = false;
            if (HasRiver(tile))
            {
                changed |= state.removeFeatureCategories.Remove("River");
                changed |= state.removeMutators.Remove("River");
            }
            if (WaterNeighbors(tile).Count > 0)
            {
                changed |= state.removeFeatureCategories.Remove("Coast");
                changed |= state.removeMutators.Remove("Coast");
                changed |= state.removeMutators.Remove("Lakeshore");
            }
            return changed;
        }
    }
}
