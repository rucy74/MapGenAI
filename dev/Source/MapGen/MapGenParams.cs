using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using MapGenAI.LLM;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// Elevation 프리미티브 데이터 클래스.
    /// slope, radial, split, bump, noise 5종을 조합하여 지형 생성.
    /// 시맨틱("strong") 또는 숫자("0.8") 형태 모두 지원.
    /// </summary>
    public class ElevationShape
    {
        public string type;         // slope, radial, split, bump, noise
        public string direction;    // left/right/top/bottom/top_left/top_right/bottom_left/bottom_right 또는 숫자(0-360)
        public string strength;     // weak/medium/strong/negative_weak/negative_medium/negative_strong 또는 숫자
        public string position;     // center/top_left/top/... 또는 숫자 배열 "0.5,0.5"
        public string size;         // small/medium/large 또는 숫자(0-1)
        public string gap;          // small/medium/large (split용)
        public string fill;         // null 또는 "water" (bump용, 호수 생성)

        // --- 시맨틱 ↔ 숫자 변환 헬퍼 ---

        /// <summary>방향 문자열 → 각도 (도 단위, 0=right, 반시계). 숫자 직접 입력도 지원.</summary>
        private static readonly Dictionary<string, float> DirectionAngles = new Dictionary<string, float>
        {
            { "right",        0f   },
            { "top_right",   45f   },
            { "top",         90f   },
            { "top_left",   135f   },
            { "left",       180f   },
            { "bottom_left",225f   },
            { "bottom",     270f   },
            { "bottom_right",315f  }
        };

        public static float ParseDirection(string val)
        {
            if (string.IsNullOrEmpty(val)) return 180f; // 기본: left
            val = val.Trim().ToLower();
            if (DirectionAngles.TryGetValue(val, out float deg)) return deg;
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
            return 180f;
        }

        public static float ParseStrength(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.7f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "weak":            return 0.3f;
                case "medium":          return 0.7f;
                case "strong":          return 1.2f;
                case "negative_weak":   return -0.3f;
                case "negative_medium": return -0.7f;
                case "negative_strong": return -1.2f;
                default:
                    return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.7f;
            }
        }

        public static float ParseSize(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.5f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "small":  return 0.25f;
                case "medium": return 0.5f;
                case "large":  return 0.75f;
                default:
                    return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp01(f) : 0.5f;
            }
        }

        public static float ParseGap(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.1f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "tiny":   return 0.02f;
                case "small":  return 0.05f;
                case "medium": return 0.1f;
                case "large":  return 0.2f;
                default:
                    return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp(f, 0f, 0.5f) : 0.1f;
            }
        }

        /// <summary>position 문자열 → (x, z) 정규화 좌표 (0~1)</summary>
        private static readonly Dictionary<string, Vector2> PositionPresets = new Dictionary<string, Vector2>
        {
            { "center",       new Vector2(0.5f, 0.5f) },
            { "top_left",     new Vector2(0.2f, 0.8f) },
            { "top",          new Vector2(0.5f, 0.8f) },
            { "top_right",    new Vector2(0.8f, 0.8f) },
            { "left",         new Vector2(0.2f, 0.5f) },
            { "right",        new Vector2(0.8f, 0.5f) },
            { "bottom_left",  new Vector2(0.2f, 0.2f) },
            { "bottom",       new Vector2(0.5f, 0.2f) },
            { "bottom_right", new Vector2(0.8f, 0.2f) }
        };

        public static Vector2 ParsePosition(string val)
        {
            if (string.IsNullOrEmpty(val)) return new Vector2(0.5f, 0.5f);
            val = val.Trim().ToLower();
            if (PositionPresets.TryGetValue(val, out Vector2 preset)) return preset;

            // "0.3,0.7" 또는 "[0.3, 0.7]" 형태 파싱
            var clean = val.Replace("[", "").Replace("]", "").Trim();
            var parts = clean.Split(',');
            if (parts.Length == 2
                && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float pz))
            {
                return new Vector2(Mathf.Clamp01(px), Mathf.Clamp01(pz));
            }

            return new Vector2(0.5f, 0.5f);
        }
    }

    /// <summary>
    /// LLM이 생성한 맵 파라미터 저장소. 정적으로 유지해서 GenStep 패치에서 읽음.
    /// Apply() 시 유효성 검증 + Map Preview 갱신.
    /// </summary>
    public static class MapGenParams
    {
        public static bool HasParams { get; private set; } = false;

        // 지형
        public static string Hills { get; private set; } = "none";   // left, right, center, edges, top, bottom, none
        public static float HillAmount { get; private set; } = 1f;         // 0.5~1.6, 전체 고도 오프셋

        // Elevation Shape 프리미티브 목록 (additive 조합)
        public static List<ElevationShape> ElevationShapes { get; private set; } = new List<ElevationShape>();
        public static float VegetationDensity { get; private set; } = 1f;  // 0.0~2.0
        /// <summary>비옥도 오프셋 (-1.0~1.0). 양수=기름진 토양 증가, 음수=감소.</summary>
        public static float FertilityOffset { get; private set; } = 0f;
        public static float AnimalDensity { get; private set; } = 1f;      // 0.0~2.0

        // 수계
        public static bool HasRiver { get; private set; } = false;
        public static string RiverDirection { get; private set; } = "vertical";
        /// <summary>강 방향 각도 (0-360도, -1=자동). 0=오른쪽, 90=위, 180=왼쪽, 270=아래.</summary>
        public static float RiverDirectionAngle { get; private set; } = -1f;
        public static float RiverXPosition { get; private set; } = 0.5f;
        /// <summary>강 Z축 위치 (0.0~1.0, 0.5=중앙). 수직 강에서 위/아래 이동.</summary>
        public static float RiverZPosition { get; private set; } = 0.5f;

        // 지물
        public static bool HasRoads { get; private set; } = false;
        public static bool HasCaves { get; private set; } = false;
        public static bool CavesExplicitlySet { get; private set; } = false;
        public static int GeyserCount { get; private set; } = -1;   // -1 = 기본값

        // 돌덩어리 (RockChunk) 제어
        /// <summary>돌덩어리 생성 여부 (기본 true). false면 GenStep_RockChunks를 완전히 스킵.</summary>
        public static bool HasRockChunks { get; private set; } = true;

        // 산 크기/부드러움 (Perlin 파라미터)
        /// <summary>산 크기 (Perlin frequency). 기본 0.021. 작을수록 큰 산맥, 클수록 잘게 쪼개짐.</summary>
        public static float HillSize { get; private set; } = 0.021f;
        /// <summary>산 부드러움 (Perlin lacunarity). 기본 2.0. 낮을수록 거친 지형, 높을수록 매끄러움.</summary>
        public static float HillSmoothness { get; private set; } = 2.0f;

        /// <summary>일자 강 (구불거림 제거). true면 강이 직선.</summary>
        public static bool StraightRiver { get; private set; } = false;

        // TileMutator (Odyssey): LLM이 선택한 mutator defName 목록
        public static List<string> Mutators { get; private set; } = new List<string>();
        // 제거할 mutator defName 목록 (기존 타일 특징 제거용)
        public static List<string> RemoveMutators { get; private set; } = new List<string>();

        // 월드 타일 mutator 복원용
        private static int _mutatorAppliedTileId = -1;
        private static List<string> _originalMutatorDefNames = null;

        // 해안 방향
        public static string CoastDirection { get; private set; } = "auto"; // auto, north, east, south, west

        // 석재 수량
        public static int RockCount { get; private set; } = -1; // 1~15, -1 = 기본값 (바닐라)

        // 광석 밀도
        public static float OreDensity { get; private set; } = 1f; // 0.0~2.5, 1.0 = 기본값

        // 석재 종류 (defName 목록)
        public static List<string> RockTypes { get; private set; } = new List<string>();

        // 폐허/위험 밀도
        public static float RuinDensity { get; private set; } = 1f; // 0.0~2.5, 1.0 = 기본값
        public static float DangerDensity { get; private set; } = 1f; // 0.0~2.5, 1.0 = 기본값

        // 유효한 hills 값
        private static readonly HashSet<string> ValidHills = new HashSet<string>
            { "left", "right", "center", "edges", "top", "bottom", "none" };

        // 유효한 coast_direction 값
        private static readonly HashSet<string> ValidCoastDirections = new HashSet<string>
            { "auto", "north", "east", "south", "west" };

        public static void Apply(MapParamsData data)
        {
            // 유효성 검증 + 클램핑
            Hills = ValidHills.Contains(data.hills ?? "") ? data.hills : "none";
            HillAmount = Mathf.Clamp(data.hill_amount, 0.5f, 1.6f);
            VegetationDensity = Mathf.Clamp(data.vegetation_density, 0f, 2f);
            AnimalDensity = Mathf.Clamp(data.animal_density, 0f, 2f);
            FertilityOffset = Mathf.Clamp(data.fertility_offset, -1f, 1f);
            HasRiver = data.river?.present ?? false;
            RiverDirection = (data.river?.direction == "horizontal") ? "horizontal" : "vertical";
            RiverDirectionAngle = data.river?.direction_angle ?? -1f;
            // 하위 호환: direction이 "horizontal"/"vertical" 문자열이면 angle로 변환
            if (RiverDirectionAngle < 0f && data.river != null)
            {
                string dir = data.river.direction?.ToLower();
                if (dir == "horizontal") RiverDirectionAngle = 0f;    // 수평 (좌-우)
                else if (dir == "vertical") RiverDirectionAngle = -1f; // 바닐라 자동
                else if (dir == "left") RiverDirectionAngle = 0f;
                else if (dir == "up") RiverDirectionAngle = 90f;
                else if (dir == "right") RiverDirectionAngle = 180f;
                else if (dir == "down") RiverDirectionAngle = 270f;
                else if (float.TryParse(dir, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    RiverDirectionAngle = Mathf.Repeat(parsed, 360f);
                }
            }
            else if (RiverDirectionAngle >= 0f)
            {
                RiverDirectionAngle = Mathf.Repeat(RiverDirectionAngle, 360f);
            }
            RiverXPosition = Mathf.Clamp(data.river?.x_position ?? 0.5f, 0f, 1f);
            RiverZPosition = Mathf.Clamp(data.river?.z_position ?? 0.5f, 0f, 1f);
            HasRoads = data.roads;
            HasCaves = data.caves;
            CavesExplicitlySet = data.caves_explicit;
            GeyserCount = (data.geysers >= 0) ? Mathf.Min(data.geysers, 20) : -1;

            // TileMutator 목록: defName으로 전달
            Mutators.Clear();
            if (data.mutators != null)
            {
                foreach (var m in data.mutators)
                {
                    if (!string.IsNullOrEmpty(m))
                        Mutators.Add(m);
                }
            }

            // 제거할 mutator 목록
            RemoveMutators.Clear();
            if (data.remove_mutators != null)
            {
                foreach (var m in data.remove_mutators)
                {
                    if (!string.IsNullOrEmpty(m))
                        RemoveMutators.Add(m);
                }
            }

            // 해안 방향
            string coastDir = (data.coast_direction ?? "auto").ToLower();
            CoastDirection = ValidCoastDirections.Contains(coastDir) ? coastDir : "auto";

            // 석재 수량
            RockCount = (data.rock_count >= 1) ? Mathf.Clamp(data.rock_count, 1, 15) : -1;

            // 광석 밀도
            OreDensity = Mathf.Clamp(data.ore_density, 0f, 2.5f);

            // 폐허/위험 밀도
            RuinDensity = Mathf.Clamp(data.ruin_density, 0f, 2.5f);
            DangerDensity = Mathf.Clamp(data.danger_density, 0f, 2.5f);

            // 돌덩어리
            HasRockChunks = data.rock_chunks;

            // 산 크기/부드러움
            HillSize = data.hill_size > 0f ? Mathf.Clamp(data.hill_size, 0.005f, 0.1f) : 0.021f;
            HillSmoothness = data.hill_smoothness > 0f ? Mathf.Clamp(data.hill_smoothness, 0.5f, 6f) : 2.0f;

            // 일자 강
            StraightRiver = data.straight_river;

            // 석재 종류
            RockTypes.Clear();
            if (data.rock_types != null)
            {
                foreach (var rt in data.rock_types)
                {
                    if (!string.IsNullOrEmpty(rt))
                        RockTypes.Add(rt);
                }
            }

            // --- ElevationShapes 파싱 ---
            ElevationShapes.Clear();
            if (data.elevation_shapes != null)
            {
                foreach (var shape in data.elevation_shapes)
                {
                    if (shape != null && !string.IsNullOrEmpty(shape.type))
                        ElevationShapes.Add(shape);
                }
            }

            // 하위 호환: hills != "none" && elevation_shapes가 비어있으면 hills를 자동 변환
            if (Hills != "none" && ElevationShapes.Count == 0)
            {
                switch (Hills)
                {
                    case "left":
                        ElevationShapes.Add(new ElevationShape { type = "slope", direction = "left", strength = "medium" });
                        break;
                    case "right":
                        ElevationShapes.Add(new ElevationShape { type = "slope", direction = "right", strength = "medium" });
                        break;
                    case "top":
                        ElevationShapes.Add(new ElevationShape { type = "slope", direction = "top", strength = "medium" });
                        break;
                    case "bottom":
                        ElevationShapes.Add(new ElevationShape { type = "slope", direction = "bottom", strength = "medium" });
                        break;
                    case "center":
                        ElevationShapes.Add(new ElevationShape { type = "bump", position = "center", size = "large", strength = "medium" });
                        break;
                    case "edges":
                        ElevationShapes.Add(new ElevationShape { type = "radial", strength = "medium", size = "medium" });
                        break;
                }
                Verse.Log.Message($"[MapGenAI] hills='{Hills}' → ElevationShape 자동 변환 ({ElevationShapes.Count}개)");
            }

            HasParams = true;

            Verse.Log.Message($"[MapGenAI] 파라미터 적용: 언덕={Hills}, 산양={HillAmount:F2}, 나무={VegetationDensity:F1}, " +
                $"동물={AnimalDensity:F1}, 강={HasRiver}(방향={RiverDirectionAngle:F0}, X={RiverXPosition:F2}, Z={RiverZPosition:F2}), " +
                $"동굴={HasCaves}, 간헐천={GeyserCount}, 해안={CoastDirection}, " +
                $"석재수={RockCount}, 석재종류={RockTypes.Count}개, 광석밀도={OreDensity:F2}, " +
                $"폐허밀도={RuinDensity:F2}, 위험밀도={DangerDensity:F2}, " +
                $"돌덩어리={HasRockChunks}, 산크기={HillSize:F4}, 산부드러움={HillSmoothness:F1}, " +
                $"mutators={Mutators.Count}개, elevation_shapes={ElevationShapes.Count}개");

            // 월드 타일에 mutator 영구 적용 (Map Designer 방식)
            // 타일 설명의 "특징"에 반영되고, Map Preview/실제 맵 모두 정상 동작
            ApplyMutatorsToWorldTile();

            RefreshMapPreview();
        }

        /// <summary>
        /// 현재 선택된 월드 타일에 mutator를 영구 적용 (Map Designer 방식).
        /// Reset() 시 원본으로 복원.
        /// </summary>
        private static void ApplyMutatorsToWorldTile()
        {
            try
            {
                var tileId = Verse.Find.WorldSelector?.SelectedTile ?? -1;
                if (tileId < 0) return;
                var tile = Verse.Find.WorldGrid?[tileId];
                if (tile == null) return;

                bool hasMutatorChanges = Mutators.Count > 0;
                bool hasRemoveMutators = RemoveMutators.Count > 0;
                // 현재 타일에 Caves mutator가 있는지
                bool tileHasCaves = tile.Mutators.Any(m => m.defName == "Caves");
                // caves 추가가 필요한지 (HasCaves=true이고 타일에 없을 때만)
                bool needCavesAdd = HasCaves && !tileHasCaves;
                // caves 제거: LLM이 명시적으로 caves=false 보냈거나 remove_mutators에 "Caves" 있을 때
                bool needCavesRemove = tileHasCaves &&
                    (RemoveMutators.Contains("Caves") || (CavesExplicitlySet && !HasCaves));

                // 아무 변경도 없으면 타일 건드리지 않음
                if (!hasMutatorChanges && !hasRemoveMutators && !needCavesAdd && !needCavesRemove) return;

                // 이전 적용 복원
                RestoreMutatorsFromWorldTile();

                // 원본 저장 (복원 후 다시 읽기)
                _mutatorAppliedTileId = tileId;
                _originalMutatorDefNames = tile.Mutators.Select(m => m.defName).ToList();

                // 1. Caves mutator 추가/제거
                if (needCavesAdd || needCavesRemove)
                {
                    var cavesMutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail("Caves");
                    if (cavesMutDef != null)
                    {
                        if (needCavesAdd)
                        {
                            tile.AddMutator(cavesMutDef);
                            Verse.Log.Message("[MapGenAI] 동굴 mutator 추가");
                        }
                        else if (needCavesRemove)
                        {
                            tile.RemoveMutator(cavesMutDef);
                            Verse.Log.Message("[MapGenAI] 동굴 mutator 제거");
                        }
                    }
                }

                // 2. LLM이 지정한 mutator 추가 (기존 유지, 같은 카테고리만 교체)
                // "교체 모드" 제거 — 기존 River/Coast/Mountain은 유지됨
                foreach (var defName in Mutators)
                {
                    var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                    if (mutDef == null) continue;

                    // River 카테고리는 강이 있어야 함
                    if (mutDef.categories.Contains("River"))
                    {
                        var st = tile as RimWorld.Planet.SurfaceTile;
                        if (st?.Rivers == null || st.Rivers.Count == 0)
                        {
                            Verse.Log.Message($"[MapGenAI] '{defName}' 스킵 — 강이 없는 타일");
                            continue;
                        }
                    }

                    // Coast 카테고리는 해안이어야 함
                    if (mutDef.categories.Contains("Coast"))
                    {
                        if (!tile.Mutators.Any(m => m.defName == "Coast") && !tile.IsCoastal)
                        {
                            Verse.Log.Message($"[MapGenAI] '{defName}' 스킵 — 해안이 아닌 타일");
                            continue;
                        }
                    }

                    // 같은 카테고리 기존 mutator만 교체 (다른 카테고리는 유지)
                    var toRemove = tile.Mutators
                        .Where(m => m.categories.Any(c => mutDef.categories.Contains(c)))
                        .ToList();
                    foreach (var old in toRemove)
                        tile.RemoveMutator(old);

                    tile.AddMutator(mutDef);
                    Verse.Log.Message($"[MapGenAI] 타일 mutator 적용: {mutDef.label} ({defName})");
                }

                // 3. 제거할 mutator 처리 (동굴 제거, 동물 개체수 감소 제거 등)
                foreach (var defName in RemoveMutators)
                {
                    var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                    if (mutDef == null) continue;
                    if (tile.Mutators.Contains(mutDef))
                    {
                        tile.RemoveMutator(mutDef);
                        Verse.Log.Message($"[MapGenAI] 타일 mutator 제거: {mutDef.label} ({defName})");
                    }
                }
            }
            catch (System.Exception e)
            {
                Verse.Log.Warning($"[MapGenAI] Mutator 적용 실패: {e.Message}");
            }
        }

        /// <summary>원본 mutator 복원</summary>
        private static void RestoreMutatorsFromWorldTile()
        {
            if (_mutatorAppliedTileId < 0 || _originalMutatorDefNames == null) return;

            try
            {
                var tile = Verse.Find.WorldGrid?[_mutatorAppliedTileId];
                if (tile == null) return;

                // 현재 mutator 전부 제거
                foreach (var m in tile.Mutators.ToList())
                    tile.RemoveMutator(m);

                // 원본 복원
                foreach (var defName in _originalMutatorDefNames)
                {
                    var mutDef = Verse.DefDatabase<TileMutatorDef>.GetNamedSilentFail(defName);
                    if (mutDef != null)
                        tile.AddMutator(mutDef);
                }
            }
            catch { }

            _mutatorAppliedTileId = -1;
            _originalMutatorDefNames = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RefreshMapPreview()
        {
            try
            {
                MapPreview.WorldInterfaceManager.RefreshPreview();
            }
            catch (System.Exception e)
            {
                Verse.Log.Warning($"[MapGenAI] Map Preview 갱신 실패: {e.Message}");
            }
        }

        public static void Reset()
        {
            // 월드 타일 mutator 원본 복원
            RestoreMutatorsFromWorldTile();

            HasParams = false;
            Hills = "none";
            HillAmount = 1f;
            VegetationDensity = 1f;
            AnimalDensity = 1f;
            FertilityOffset = 0f;
            HasRiver = false;
            RiverDirection = "vertical";
            RiverDirectionAngle = -1f;
            RiverXPosition = 0.5f;
            RiverZPosition = 0.5f;
            HasRoads = false;
            HasCaves = false;
            CavesExplicitlySet = false;
            GeyserCount = -1;
            HasRockChunks = true;
            HillSize = 0.021f;
            HillSmoothness = 2.0f;
            StraightRiver = false;
            CoastDirection = "auto";
            RockCount = -1;
            OreDensity = 1f;
            RuinDensity = 1f;
            DangerDensity = 1f;
            RockTypes.Clear();
            Mutators.Clear();
            RemoveMutators.Clear();
            ElevationShapes.Clear();
        }

        /// <summary>현재 파라미터 상태를 MapParamsData 스냅샷으로 반환 (undo용).</summary>
        public static MapParamsData ToSnapshot()
        {
            return new MapParamsData
            {
                hills = Hills,
                hill_amount = HillAmount,
                vegetation_density = VegetationDensity,
                animal_density = AnimalDensity,
                fertility_offset = FertilityOffset,
                river = new RiverData
                {
                    present = HasRiver,
                    direction_angle = RiverDirectionAngle,
                    x_position = RiverXPosition,
                    z_position = RiverZPosition
                },
                roads = HasRoads,
                caves = HasCaves,
                caves_explicit = CavesExplicitlySet,
                geysers = GeyserCount,
                coast_direction = CoastDirection,
                rock_count = RockCount,
                ore_density = OreDensity,
                ruin_density = RuinDensity,
                danger_density = DangerDensity,
                rock_chunks = HasRockChunks,
                hill_size = HillSize,
                hill_smoothness = HillSmoothness,
                straight_river = StraightRiver,
                rock_types = new List<string>(RockTypes),
                mutators = new List<string>(Mutators),
                remove_mutators = new List<string>(RemoveMutators),
                elevation_shapes = ElevationShapes.Select(s => new ElevationShape
                {
                    type = s.type, direction = s.direction, strength = s.strength,
                    position = s.position, size = s.size, gap = s.gap, fill = s.fill
                }).ToList()
            };
        }

        /// <summary>현재 파라미터 상태를 시스템 프롬프트용 텍스트로 반환.</summary>
        public static string BuildCurrentParamsText(bool isKo)
        {
            if (!HasParams) return "";

            var sb = new System.Text.StringBuilder();
            if (isKo)
                sb.AppendLine("\n## 현재 적용된 파라미터 (이 상태에서 수정하세요. 변경하지 않는 값은 그대로 유지하세요.):");
            else
                sb.AppendLine("\n## Currently applied parameters (modify from this state. Keep unchanged values as-is.):");

            if (ElevationShapes.Count > 0)
            {
                foreach (var s in ElevationShapes)
                {
                    var parts = new List<string> { $"type={s.type}" };
                    if (!string.IsNullOrEmpty(s.direction)) parts.Add($"direction={s.direction}");
                    if (!string.IsNullOrEmpty(s.strength)) parts.Add($"strength={s.strength}");
                    if (!string.IsNullOrEmpty(s.position)) parts.Add($"position={s.position}");
                    if (!string.IsNullOrEmpty(s.size)) parts.Add($"size={s.size}");
                    if (!string.IsNullOrEmpty(s.gap)) parts.Add($"gap={s.gap}");
                    if (!string.IsNullOrEmpty(s.fill)) parts.Add($"fill={s.fill}");
                    sb.AppendLine($"- elevation_shape: {{{string.Join(", ", parts)}}}");
                }
            }
            else
                sb.AppendLine($"- hills: {Hills}, hill_amount: {HillAmount:F2}");

            sb.AppendLine($"- vegetation_density: {VegetationDensity:F1}, animal_density: {AnimalDensity:F1}, fertility_offset: {FertilityOffset:F2}");

            if (HasRiver)
                sb.AppendLine($"- river: present (direction_angle={RiverDirectionAngle:F0}, x={RiverXPosition:F2}, z={RiverZPosition:F2}, straight={StraightRiver})");
            else
                sb.AppendLine("- river: none");

            if (HasCaves) sb.AppendLine("- caves: true");
            if (CoastDirection != "auto") sb.AppendLine($"- coast_direction: {CoastDirection}");
            if (RockTypes.Count > 0) sb.AppendLine($"- rock_types: [{string.Join(", ", RockTypes)}]");
            if (Mutators.Count > 0) sb.AppendLine($"- active_mutators: [{string.Join(", ", Mutators)}]");
            if (RockCount > 0) sb.AppendLine($"- rock_count: {RockCount}");
            if (OreDensity != 1f) sb.AppendLine($"- ore_density: {OreDensity:F2}");
            if (RuinDensity != 1f) sb.AppendLine($"- ruin_density: {RuinDensity:F2}");
            if (DangerDensity != 1f) sb.AppendLine($"- danger_density: {DangerDensity:F2}");
            if (!HasRockChunks) sb.AppendLine("- rock_chunks: false");
            if (HillSize != 0.021f) sb.AppendLine($"- hill_size: {HillSize:F4}");
            if (HillSmoothness != 2.0f) sb.AppendLine($"- hill_smoothness: {HillSmoothness:F1}");

            return sb.ToString();
        }
    }

    // LLM JSON 역직렬화용 데이터 클래스
    public class LLMResponse
    {
        public string action;
        public string message;
        public MapParamsData params_data;
    }

    public class MapParamsData
    {
        public string hills;
        public float hill_amount = 1f;
        public float vegetation_density = 1f;
        public float animal_density = 1f;
        public float fertility_offset = 0f;        // -1.0~1.0, 비옥도 오프셋
        public RiverData river;
        public bool roads;
        public bool caves;
        public bool caves_explicit = false;  // JSON에 "caves" 키가 명시적으로 존재하는지
        public int geysers = -1;
        public string coast_direction = "auto";    // auto, north, east, south, west
        public int rock_count = -1;                // 1~15, -1 = 기본값
        public float ore_density = 1f;             // 0.0~2.5, 1.0 = 기본값
        public float ruin_density = 1f;            // 0.0~2.5, 1.0 = 기본값 (폐허 밀도)
        public float danger_density = 1f;          // 0.0~2.5, 1.0 = 기본값 (고대 위험 밀도)
        public bool rock_chunks = true;            // true=돌덩어리 생성, false=제거
        public float hill_size = 0f;               // Perlin frequency (기본 0.021). 0=기본값 사용
        public float hill_smoothness = 0f;         // Perlin lacunarity (기본 2.0). 0=기본값 사용
        public bool straight_river = false;       // true=일자 강 (구불거림 제거)
        public List<string> rock_types;            // 원하는 석재 defName 목록 (Granite, Limestone, Marble, Sandstone, Slate)
        public List<string> mutators;           // 추가할 TileMutator defName 목록
        public List<string> remove_mutators;    // 제거할 TileMutator defName 목록
        public List<ElevationShape> elevation_shapes;  // Elevation 프리미티브 목록
    }

    public class RiverData
    {
        public bool present;
        public string direction = "vertical";   // 하위 호환: "horizontal"/"vertical" 또는 "left"/"up"/"right"/"down" 또는 각도 문자열
        public float direction_angle = -1f;     // 0-360도, -1=자동
        public float x_position = 0.5f;
        public float z_position = 0.5f;
    }
}
