using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // No World/Find/preview side effects. Patch and legacy snapshot conversion are explicit.
    public static class MapStateEditor
    {
        // 유효한 hills 값
        private static readonly HashSet<string> ValidHills = new HashSet<string>
            { "left", "right", "center", "edges", "top", "bottom", "none" };

        // 유효한 coast_direction 값
        private static readonly HashSet<string> ValidCoastDirections = new HashSet<string>
            { "auto", "north", "east", "south", "west" };

        /// <summary>hills 값에 해당하는 base layer shape 반환. "none"이면 null.</summary>
        internal static ElevationShape GetAutoShapeForHills(string hills)
        {
            switch (hills)
            {
                case "left":   return new ElevationShape { type = "ridge", direction = "left", strength = "medium", fade = "medium", noise_amount = "medium" };
                case "right":  return new ElevationShape { type = "ridge", direction = "right", strength = "medium", fade = "medium", noise_amount = "medium" };
                case "top":    return new ElevationShape { type = "ridge", direction = "top", strength = "medium", fade = "medium", noise_amount = "medium" };
                case "bottom": return new ElevationShape { type = "ridge", direction = "bottom", strength = "medium", fade = "medium", noise_amount = "medium" };
                case "center": return new ElevationShape { type = "bump", position = "center", size = "large", strength = "medium" };
                case "edges":  return new ElevationShape { type = "radial", strength = "medium", size = "medium" };
                default:       return null;
            }
        }


        public static TileMapState Merge(TileMapState existing, MapParamsData data) => MergeCore(existing, data, false);
        public static TileMapState FromLegacySnapshot(MapParamsData data) => MergeCore(new TileMapState(), data, true);
        private static TileMapState MergeCore(TileMapState existing, MapParamsData data, bool fullApply)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var keys = data.explicitKeys ?? new HashSet<string>();
            var state = existing?.Clone() ?? new TileMapState();
            if (data.mutators != null && data.remove_mutators != null && data.mutators.Intersect(data.remove_mutators).Any())
                throw new FormatException("A feature cannot be added and removed in the same edit");
            // 스칼라 필드 병합: explicitKeys에 있으면 업데이트, 없으면 기존 state 유지
            if (fullApply || keys.Contains("hills"))
                state.hills = ValidHills.Contains((data.hills ?? "").ToLowerInvariant()) ? data.hills.ToLowerInvariant() : "none";
            if (fullApply || keys.Contains("hill_amount"))
                state.hillAmount = Mathf.Clamp(data.hill_amount, 0.1f, 1.6f);
            if (fullApply || keys.Contains("vegetation_density"))
                state.vegetationDensity = Mathf.Clamp(data.vegetation_density, 0f, 2f);
            if (fullApply || keys.Contains("animal_density"))
                state.animalDensity = Mathf.Clamp(data.animal_density, 0f, 2f);
            if (fullApply || keys.Contains("fertility_offset"))
                state.fertilityOffset = Mathf.Clamp(data.fertility_offset, -1f, 1f);
            if (fullApply || keys.Contains("roads"))
                state.hasRoads = data.roads;
            if (fullApply || keys.Contains("caves"))
            {
                state.hasCaves = data.caves;
                state.cavesExplicitlySet = data.caves_explicit;
            }
            if (fullApply || keys.Contains("geysers"))
                state.geyserCount = (data.geysers >= 0) ? Mathf.Min(data.geysers, 20) : -1;
            if (fullApply || keys.Contains("coast_direction"))
            {
                string coastDir = (data.coast_direction ?? "auto").ToLower();
                state.coastDirection = ValidCoastDirections.Contains(coastDir) ? coastDir : "auto";
            }
            if (fullApply || keys.Contains("rock_count"))
                state.rockCount = (data.rock_count >= 1) ? Mathf.Clamp(data.rock_count, 1, 15) : -1;
            if (fullApply || keys.Contains("ore_density"))
                state.oreDensity = Mathf.Clamp(data.ore_density, 0f, 2.5f);
            if (fullApply || keys.Contains("ruin_density"))
                state.ruinDensity = Mathf.Clamp(data.ruin_density, 0f, 2.5f);
            if (fullApply || keys.Contains("danger_density"))
                state.dangerDensity = Mathf.Clamp(data.danger_density, 0f, 2.5f);
            if (fullApply || keys.Contains("rock_chunks"))
                state.hasRockChunks = data.rock_chunks;
            if (fullApply || keys.Contains("hill_size"))
                state.hillSize = data.hill_size > 0f ? Mathf.Clamp(data.hill_size, 0.005f, 0.1f) : 0.021f;
            if (fullApply || keys.Contains("hill_smoothness"))
                state.hillSmoothness = data.hill_smoothness > 0f ? Mathf.Clamp(data.hill_smoothness, 0.5f, 6f) : 2.0f;
            if (fullApply || keys.Contains("straight_river"))
                state.straightRiver = data.straight_river;

            // 강: river 키가 있으면 업데이트
            // 강: 세부 필드별로 병합 (방향만 보내도 위치 유지, 위치만 보내도 방향 유지)
            if (fullApply || keys.Contains("river_present"))
                state.hasRiver = data.river?.present ?? false;
            if (fullApply || keys.Contains("river_direction"))
            {
                float angle = data.river?.direction_angle ?? -1f;
                // 하위 호환: direction 문자열 → angle 변환
                if (angle < 0f && data.river != null)
                {
                    string dir = data.river.direction?.ToLower();
                    if (dir == "horizontal") angle = 90f;
                    else if (dir == "vertical") angle = -1f;
                    else if (dir == "left") angle = 270f;
                    else if (dir == "up") angle = 0f;
                    else if (dir == "right") angle = 90f;
                    else if (dir == "down") angle = 180f;
                    else if (float.TryParse(dir, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                        angle = Mathf.Repeat(parsed, 360f);
                }
                else if (angle >= 0f)
                    angle = Mathf.Repeat(angle, 360f);
                state.riverDirectionAngle = angle;
            }
            if (fullApply || keys.Contains("river_x") || keys.Contains("river_position"))
                state.riverXPosition = Mathf.Clamp(data.river?.x_position ?? 0.5f, 0f, 1f);
            if (fullApply || keys.Contains("river_z") || keys.Contains("river_position"))
                state.riverZPosition = Mathf.Clamp(data.river?.z_position ?? 0.5f, 0f, 1f);

            // 석재 종류
            if (fullApply || keys.Contains("rock_types"))
            {
                state.rockTypes.Clear();
                if (data.rock_types != null)
                    foreach (var rt in data.rock_types)
                        if (!string.IsNullOrEmpty(rt)) state.rockTypes.Add(rt);
            }

            // TileMutator — additive 병합.
            // "추가"는 기존 특징에 더하기(union)로, "제거"는 remove_mutators로만.
            // 구버전은 mutators 키가 오면 clear+reset(full-replace)이라, "오로라 추가"처럼
            // 새 것 하나만 보내면 기존 별관측 등이 지워지는 버그가 있었음 → union으로 교체.
            // fullApply(프리셋/undo)는 완전한 스냅샷이므로 clear 후 대입(교체 의미 유지).
            if (fullApply || keys.Contains("mutators"))
            {
                if (fullApply) state.mutators.Clear();
                if (data.mutators != null)
                    foreach (var m in data.mutators)
                        if (!string.IsNullOrEmpty(m))
                        {
                            if (!state.mutators.Contains(m)) state.mutators.Add(m);
                            state.removeMutators.Remove(m);
                        }
            }
            if (fullApply || keys.Contains("remove_mutators"))
            {
                if (fullApply) state.removeMutators.Clear();
                if (data.remove_mutators != null)
                    foreach (var m in data.remove_mutators)
                        if (!string.IsNullOrEmpty(m))
                        {
                            if (!state.removeMutators.Contains(m)) state.removeMutators.Add(m);
                            state.mutators.Remove(m);  // desired-set에서도 빼야 재적용 때 안 살아남
                        }
            }

            if (data.remove_categories != null && data.restore_categories != null && data.remove_categories.Intersect(data.restore_categories).Any())
                throw new FormatException("Feature category cannot be removed and restored in one request");
            if (data.remove_mutators?.Contains("River") == true &&
                (data.restore_categories?.Contains("River") == true || (keys.Contains("river_present") && data.river?.present == true)))
                throw new FormatException("Contradictory river removal and restoration");
            if (fullApply) state.removeFeatureCategories.Clear();
            if (data.remove_categories != null)
                foreach (var category in data.remove_categories)
                    if (!state.removeFeatureCategories.Contains(category)) state.removeFeatureCategories.Add(category);
            if (data.restore_categories != null)
                foreach (var category in data.restore_categories) state.removeFeatureCategories.Remove(category);
            // Legacy snapshots used false for 'unspecified'. Only a new explicit request suppresses rivers.
            if (!fullApply && keys.Contains("river_present"))
            {
                bool off = data.river?.present == false;
                if ((!off && data.remove_categories?.Contains("River") == true) || (off && data.restore_categories?.Contains("River") == true))
                    throw new FormatException("Contradictory river presence and category request");
                if (off) { if (!state.removeFeatureCategories.Contains("River")) state.removeFeatureCategories.Add("River"); }
                else { state.removeFeatureCategories.Remove("River"); state.removeMutators.Remove("River"); }
            }
            // Compatibility for existing prompts which use the base River def as 'all rivers'.
            if (!fullApply && data.remove_mutators?.Contains("River") == true && !state.removeFeatureCategories.Contains("River"))
                state.removeFeatureCategories.Add("River");
            if (data.restore_categories?.Contains("River") == true) state.removeMutators.Remove("River");
            if (state.removeFeatureCategories.Contains("River")) state.hasRiver = false;

            // ElevationShapes: 키가 있으면 전체 교체, 없으면 기존 유지
            if (fullApply || keys.Contains("elevation_shapes"))
            {
                if (!fullApply && state.elevationShapes.Count > 0 && data.elevation_shapes?.Count > 0 && !data.replace_shapes)
                    throw new FormatException("Use shape_ops to edit existing terrain; full replacement requires replace_shapes:true");
                state.elevationShapes.Clear();
                if (data.elevation_shapes != null)
                    foreach (var shape in data.elevation_shapes)
                        if (shape != null && !string.IsNullOrEmpty(shape.type))
                            state.elevationShapes.Add(shape.Clone());
            }

            if (keys.Contains("hills") && state.hills == "none")
                state.elevationShapes.RemoveAll(shape => shape.autoHills);
            ShapeEdits.Apply(state.elevationShapes, data.shape_ops);
            StructurePlans.Apply(state, data.structure_ops);

            // Automatic hills only run for an explicit hills edit, never for unrelated edits.
            // hills shape 누적: 같은 type+direction이 없으면 추가
            if ((fullApply || keys.Contains("hills")) && !keys.Contains("elevation_shapes") && data.elevation_shapes == null && state.hills != "none")
            {
                var autoShape = GetAutoShapeForHills(state.hills);
                if (autoShape != null)
                {
                    bool exists = state.elevationShapes.Any(s =>
                        s.type == autoShape.type &&
                        (s.direction ?? "") == (autoShape.direction ?? "") &&
                        (s.position ?? "") == (autoShape.position ?? "") &&
                        string.IsNullOrEmpty(s.fill));
                    if (!exists)
                    {
                        autoShape.autoHills = true;
                        state.elevationShapes.Add(autoShape);
                    }
                }
            }

            if ((data.elevation_shapes != null || keys.Contains("hills")) && state.elevationShapes.Count > 0)
                ShapeEdits.AssignIds(state.elevationShapes);
            if (state.elevationShapes.Count > ShapeEdits.MaxShapes) throw new FormatException("Too many terrain shapes");
            StructurePlans.Validate(state);
            return state;
        }
    }
}
