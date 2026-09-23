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
    public class ElevationShape : IExposable
    {
        public string id;
        public bool autoHills;
        public string type;         // slope, radial, split, bump, noise
        public string direction;    // left/right/top/bottom/top_left/top_right/bottom_left/bottom_right 또는 숫자(0-360)
        public string strength;     // weak/medium/strong/negative_weak/negative_medium/negative_strong 또는 숫자
        public string position;     // center/top_left/top/... 또는 숫자 배열 "0.5,0.5"
        public string size;         // small/medium/large 또는 숫자(0-1)
        public string gap;          // small/medium/large (split용)
        public string fill;         // null 또는 "water" (bump용, 호수 생성)
        public string fade;         // ridge용: small(0.3)/medium(0.5)/large(0.7) 또는 0~1
        public string noise_amount; // ridge용: none(0)/low(0.3)/medium(0.6)/high(1.0) 또는 0~1.5
        public string edge_roughness; // composite contour or passage edges: omitted/none=precise
        public string landform, variant, opening, layout; // null layout preserves the original natural generator
        public string details; // natural = local shore/foothill surfaces and initial biome vegetation; null/none preserves legacy

        public string region, region_part, coverage; // region_fill: source area and counted cell fraction
        public float[][] points; // passage: ordered normalized centerline, distinct from legacy SDF geometry
        public int width; // passage width in map cells; zero means absent in older states
        public string scope; // passage: null/full preserves the entire route; mountains clips to pre-cut elevation

        // composite (CSG/SDF) 전용 — type="composite"일 때 사용
        public List<ShapePrimitive> compositeShapes;
        public List<ComposeOp> compositeOps;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref autoHills, "autoHills", false);
            Scribe_Values.Look(ref type, "type");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref strength, "strength");
            Scribe_Values.Look(ref position, "position");
            Scribe_Values.Look(ref size, "size");
            Scribe_Values.Look(ref gap, "gap");
            Scribe_Values.Look(ref fill, "fill");
            Scribe_Values.Look(ref fade, "fade");
            Scribe_Values.Look(ref noise_amount, "noise_amount");
            Scribe_Values.Look(ref edge_roughness, "edge_roughness");
            Scribe_Values.Look(ref landform, "landform");
            Scribe_Values.Look(ref variant, "variant");
            Scribe_Values.Look(ref opening, "opening");
            Scribe_Values.Look(ref layout, "layout");
            Scribe_Values.Look(ref details, "details");
            Scribe_Values.Look(ref region, "region");
            Scribe_Values.Look(ref region_part, "region_part");
            Scribe_Values.Look(ref coverage, "coverage");
            Scribe_Values.Look(ref width, "passageWidth", 0);
            Scribe_Values.Look(ref scope, "passageScope");
            string pointsJson=points==null?null:MapGenAI.UI.SimpleJson.Serialize(new Dictionary<string,object>{{"points",points}});
            Scribe_Values.Look(ref pointsJson,"passagePoints");
            if(Scribe.mode==LoadSaveMode.LoadingVars)points=string.IsNullOrEmpty(pointsJson)?null:MapGenAI.UI.SimpleJson.Parse(pointsJson).GetNestedFloatArray("points");
            string compositeJson = compositeShapes == null && compositeOps == null ? null :
                MapGenAI.UI.SimpleJson.Serialize(new Dictionary<string, object> { { "shapes", compositeShapes }, { "compose", compositeOps } });
            Scribe_Values.Look(ref compositeJson, "compositeJson");
            if (Scribe.mode == LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(compositeJson))
            {
                var stored = MapGenAI.UI.SimpleJson.Parse(compositeJson);
                compositeShapes = MapParameterParser.ParseCompositeShapes(stored);
                compositeOps = MapParameterParser.ParseCompositeOps(stored);
            }
        }

        public ElevationShape Clone()
        {
            return new ElevationShape
            {
                id = id, autoHills = autoHills,
                type = type, direction = direction, strength = strength,
                position = position, size = size, gap = gap, fill = fill,
                fade = fade, noise_amount = noise_amount, edge_roughness = edge_roughness,
                landform = landform, variant = variant, opening = opening, layout = layout,
                details = details,
                region = region, region_part = region_part, coverage = coverage,
                width = width, scope = scope, points = points?.Select(p => (float[])p.Clone()).ToArray(),
                compositeShapes = compositeShapes?.Select(s => s.Clone()).ToList(),
                compositeOps = compositeOps?.Select(op => op.Clone()).ToList()
            };
        }

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

        /// <summary>fade 파서 (ridge용). small=0.3(가장자리만), medium=0.5(절반), large=0.7(대부분).</summary>
        public static float ParseFade(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.5f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "small":  return 0.3f;
                case "medium": return 0.5f;
                case "large":  return 0.7f;
                default:
                    return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp(f, 0f, 1f) : 0.5f;
            }
        }

        /// <summary>noise_amount 파서 (ridge용). none=0, low=0.3, medium=0.6, high=1.0.</summary>
        public static float ParseNoiseAmount(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.6f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "none":   return 0f;
                case "low":    return 0.3f;
                case "medium": return 0.6f;
                case "high":   return 1.0f;
                default:
                    return float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp(f, 0f, 1.5f) : 0.6f;
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
        private static TileMapState editingState = new TileMapState();
        private static readonly TileMapState emptyState = new TileMapState();
        private static TileMapState ReadState => GenerationContext.Active ? GenerationContext.State ?? emptyState : editingState;
        private static bool editingActive;
        public static bool HasParams { get => GenerationContext.Active ? GenerationContext.State != null : editingActive; private set => editingActive = value; }

        // 지형
        public static string Hills => ReadState.hills;   // left, right, center, edges, top, bottom, none
        public static float HillAmount => ReadState.hillAmount;         // 0.5~1.6, 전체 고도 오프셋

        // Elevation Shape 프리미티브 목록 (additive 조합)
        public static List<ElevationShape> ElevationShapes => ReadState.elevationShapes;
        public static MapGenAI.ImageInput.ImageMapData ImageMap => ImageInput.ImageFeatureGate.Enabled ? ReadState.imageMap : null;
        public static float VegetationDensity => ReadState.vegetationDensity;  // 0.0~2.0
        /// <summary>비옥도 오프셋 (-1.0~1.0). 양수=기름진 토양 증가, 음수=감소.</summary>
        public static float FertilityOffset => ReadState.fertilityOffset;
        public static float AnimalDensity => ReadState.animalDensity;      // 0.0~2.0

        // 수계
        public static bool HasRiver => ReadState.hasRiver;
        public static string RiverDirection => RiverDirectionAngle == 90f ? "horizontal" : "vertical";
        /// <summary>강 방향 각도 (0-360도, -1=자동). 0=오른쪽, 90=위, 180=왼쪽, 270=아래.</summary>
        public static float RiverDirectionAngle => ReadState.riverDirectionAngle;
        public static float RiverXPosition => ReadState.riverXPosition;
        /// <summary>강 Z축 위치 (0.0~1.0, 0.5=중앙). 수직 강에서 위/아래 이동.</summary>
        public static float RiverZPosition => ReadState.riverZPosition;

        // 지물
        public static bool HasRoads => ReadState.hasRoads;
        public static bool HasCaves => ReadState.hasCaves;
        public static bool CavesExplicitlySet => ReadState.cavesExplicitlySet;
        public static int GeyserCount => ReadState.geyserCount;   // -1 = 기본값

        // 돌덩어리 (RockChunk) 제어
        /// <summary>돌덩어리 생성 여부 (기본 true). false면 GenStep_RockChunks를 완전히 스킵.</summary>
        public static bool HasRockChunks => ReadState.hasRockChunks;

        // 산 크기/부드러움 (Perlin 파라미터)
        /// <summary>산 크기 (Perlin frequency). 기본 0.021. 작을수록 큰 산맥, 클수록 잘게 쪼개짐.</summary>
        public static float HillSize => ReadState.hillSize;
        /// <summary>산 부드러움 (Perlin lacunarity). 기본 2.0. 낮을수록 거친 지형, 높을수록 매끄러움.</summary>
        public static float HillSmoothness => ReadState.hillSmoothness;

        /// <summary>일자 강 (구불거림 제거). true면 강이 직선.</summary>
        public static bool StraightRiver => ReadState.straightRiver;

        // TileMutator (Odyssey): LLM이 선택한 mutator defName 목록
        public static List<string> Mutators => ReadState.mutators;
        // 제거할 mutator defName 목록 (기존 타일 특징 제거용)
        public static List<string> RemoveMutators => ReadState.removeMutators;

        // 해안 방향
        public static string CoastDirection => ReadState.coastDirection; // auto, north, east, south, west

        // 석재 수량
        public static int RockCount => ReadState.rockCount; // 1~15, -1 = 기본값 (바닐라)

        // 광석 밀도
        public static float OreDensity => ReadState.oreDensity; // 0.0~2.5, 1.0 = 기본값

        // 석재 종류 (defName 목록)
        public static List<string> RockTypes => ReadState.rockTypes;

        // 폐허/위험 밀도
        public static float RuinDensity => ReadState.ruinDensity; // 0.0~2.5, 1.0 = 기본값
        public static float DangerDensity => ReadState.dangerDensity; // 0.0~2.5, 1.0 = 기본값

        /// <summary>현재 Apply 대상 타일 ID. Dialog에서 설정.</summary>
        private static int editingTileId = -1;
        public static int CurrentTileId { get => GenerationContext.Active ? GenerationContext.TileId : editingTileId; set => editingTileId = value; }

        public static void Apply(MapParamsData data)
        {
            Apply(data, CurrentTileId);
        }

        public static void Apply(MapParamsData data, int tileId)
        {
            ApplyPatch(data, tileId);
        }

        public static TileMapState CaptureState(int tileId)
        {
            var state = MapGenAIWorldComponent.Get()?.GetState(tileId);
            return state?.Clone() ?? new TileMapState();
        }

        public static string LastApplyWarning { get; private set; }
        public static string LastWorldChanges { get; private set; }

        public static void ApplyPatch(MapParamsData data, int tileId)
            => ApplyPatches(new[] { data },tileId);

        // Candidate refinements are replayed into private state, then committed once.
        public static void ApplyPatches(IReadOnlyList<MapParamsData> edits, int tileId)
        {
            LastApplyWarning = null;
            LastWorldChanges = null;
            ValidateSequenceRequests(edits,tileId);
            UpgradeStoredFeaturePolicy(tileId);
            var previous = MapGenAIWorldComponent.Get()?.GetState(tileId);
            var candidate = BuildSequenceCandidate(previous, edits, tileId);
            if (MapStateCodec.ChangedFields(previous ?? new TileMapState(), candidate).Count == 0) return;
            CommitState(candidate, tileId);
        }

        // UI-thread dry run: same request, state, material and world planning checks as application.
        // No metadata/cache/preview/warning/Undo changes; the candidate is a private clone.
        public static void ValidatePatch(MapParamsData data, int tileId)
            => ValidatePatches(new[] { data },tileId);

        public static void ValidatePatches(IReadOnlyList<MapParamsData> edits, int tileId)
            => CommitState(BuildSequenceCandidate(MapGenAIWorldComponent.Get()?.GetState(tileId), edits, tileId), tileId, true);

        private static TileMapState BuildSequenceCandidate(TileMapState previous,IReadOnlyList<MapParamsData> edits,int tileId)
        {
            ValidateSequenceRequests(edits,tileId);
            var candidate=previous;
            foreach(var data in edits)candidate=BuildPatchCandidate(candidate,data);
            return candidate;
        }

        private static void ValidateSequenceRequests(IReadOnlyList<MapParamsData> edits,int tileId)
        {
            if(edits==null || edits.Count<1 || edits.Count>33)throw new System.FormatException("Invalid candidate edit sequence");
            foreach(var data in edits)
            {
                WorldTileEditor.ValidateFeatureRequest(data);
                FeaturePolicy.ValidateRequest(data, tileId < 0 ? null : Find.WorldGrid?[tileId]);
            }
        }

        private static TileMapState BuildPatchCandidate(TileMapState previous, MapParamsData data)
        {
            var candidate = MapStateEditor.Merge(previous, data);
            if (data.mutators != null)
                foreach (var name in data.mutators)
                {
                    var feature = DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);
                    if (feature != null && feature.categories.Any(candidate.removeFeatureCategories.Contains))
                        throw new System.FormatException("Feature category is suppressed. Include restore_categories to enable: " + name);
                }
            return candidate;
        }

        public static void RestoreSnapshot(TileMapState snapshot, int tileId)
        {
            LastApplyWarning = null;
            LastWorldChanges = null;
            if (snapshot == null) { ClearTile(tileId); return; }
            CommitState(snapshot.Clone(), tileId);
        }

        private static void CommitState(TileMapState candidate, int tileId, bool validateOnly = false)
        {
            MapStateValidation.Validate(candidate);
            TerrainMaterials.Validate(candidate);
            var wc = MapGenAIWorldComponent.Get();
            var tile = tileId < 0 ? null : Find.WorldGrid?[tileId];
            if (wc == null || tile == null) throw new System.FormatException("Map editing requires a valid world tile");
            var previous = wc.GetState(tileId)?.Clone();
            var beforeWorld = TileWorldSnapshot.Capture(tile);
            var savedBaseline = wc.GetBaseline(tileId);
            var lastApplied = wc.GetLastApplied(tileId);
            var baseline = WorldTileEditor.Rebase(savedBaseline, lastApplied, beforeWorld);
            var missing = baseline.mutators.Concat(candidate.mutators).Distinct()
                .Where(n => DefDatabase<TileMutatorDef>.GetNamedSilentFail(n) == null).ToList();
            if (missing.Count > 0)
            {
                candidate.mutators.RemoveAll(missing.Contains);
                if (!validateOnly) LastApplyWarning = "비활성 모드의 특징 생략 / Unavailable features omitted: " + string.Join(", ", missing);
            }
            // A concurrent removal of one of our features must not be silently undone.
            if (lastApplied != null && previous != null)
            {
                var externallyRemoved = lastApplied.mutators.Except(beforeWorld.mutators).ToList();
                if (candidate.mutators.Any(n => externallyRemoved.Contains(n) && previous.mutators.Contains(n)))
                    throw new System.FormatException("다른 작업에서 제거한 특징과 충돌합니다. 해당 특징을 remove_mutators로 먼저 해제하세요. / A feature was removed externally; remove it from this plan before editing.");
            }
            var desired = WorldTileEditor.Plan(tile, baseline, candidate);
            if (validateOnly) return;
            var previousCacheTile = CurrentTileId;
            bool previousCacheActive = HasParams;
            try
            {
                WorldTileEditor.Replace(tile, desired);
                wc.SetState(tileId, candidate);
                wc.SetBaseline(tileId, baseline);
                wc.SetLastApplied(tileId, TileWorldSnapshot.Capture(tile));
                ApplyStateToStaticFields(candidate);
                CurrentTileId = tileId;
                HasParams = true;
            }
            catch (System.Exception applyError)
            {
                try
                {
                    WorldTileEditor.Restore(tile, beforeWorld);
                    if (previous == null) wc.RemoveState(tileId); else wc.SetState(tileId, previous);
                    if (savedBaseline == null) wc.RemoveBaseline(tileId);
                    else wc.SetBaseline(tileId, savedBaseline);
                    if (lastApplied == null) wc.RemoveLastApplied(tileId); else wc.SetLastApplied(tileId, lastApplied);
                    if (previousCacheActive) LoadFromTile(previousCacheTile); else Reset();
                }
                catch (System.Exception rollbackError)
                {
                    throw new System.AggregateException("Tile update and metadata rollback both failed", applyError, rollbackError);
                }
                throw;
            }
            var added = tile.Mutators.Select(d=>d.defName).Except(beforeWorld.mutators).ToList();
            var removed = beforeWorld.mutators.Except(tile.Mutators.Select(d=>d.defName)).ToList();
            if (added.Count > 0 || removed.Count > 0)
                LastWorldChanges = "실제 타일 특징 / Tile features: +[" + string.Join(", ",added) + "] −[" + string.Join(", ",removed) + "]";
            Log.Message("[MapGenAI] Applied state to tile " + tileId + (LastWorldChanges == null ? "" : "; " + LastWorldChanges));
            RefreshMapPreview();
        }

        /// <summary>TileMapState를 정적 필드에 적용 (패치들이 읽는 캐시).</summary>
        private static void ApplyStateToStaticFields(TileMapState state) => editingState = state.Clone();

        /// <summary>WorldComponent에서 타일 상태를 로드하여 정적 필드에 적용.</summary>
        public static void LoadFromTile(int tileId)
        {
            UpgradeStoredFeaturePolicy(tileId);
            var wc = MapGenAIWorldComponent.Get();
            var state = wc?.GetState(tileId);
            if (state != null)
            {
                ApplyStateToStaticFields(state);
                HasParams = true;
                CurrentTileId = tileId;
            }
            else
            {
                Reset();
                CurrentTileId = tileId;
            }
        }

        public static void UpgradeStoredFeaturePolicy(int tileId)
        {
            var wc = MapGenAIWorldComponent.Get();
            var state = wc?.GetState(tileId);
            var tile = tileId < 0 ? null : Find.WorldGrid?[tileId];
            if (state == null || tile == null || !FeaturePolicy.UpgradeStoredConnections(tile, state)) return;
            var before = TileWorldSnapshot.Capture(tile);
            var desired = tile.Mutators.ToList();
            WorldTileEditor.EnsureConnections(tile, desired);
            try { WorldTileEditor.Replace(tile, desired); }
            catch { WorldTileEditor.Restore(tile, before); throw; }
            wc.SetState(tileId, state);
            wc.SetLastApplied(tileId, TileWorldSnapshot.Capture(tile));
            LastApplyWarning = "이전 개발판의 강·해안 제거 설정을 해제하고 월드 연결을 복원했습니다 / Restored world connections suppressed by an older dev build.";
            Log.Warning("[MapGenAI] " + LastApplyWarning);
        }

        /// <summary>Explicitly discard this tile's settings and restore its saved metadata baseline.</summary>
        public static void ClearTile(int tileId)
        {
            var wc = MapGenAIWorldComponent.Get();
            var baseline = wc?.GetBaseline(tileId);
            if (baseline != null)
            {
                var tile = Find.WorldGrid?[tileId];
                if (tile == null) throw new System.InvalidOperationException("Cannot restore missing world tile");
                var before = TileWorldSnapshot.Capture(tile);
                baseline = WorldTileEditor.Rebase(baseline, wc.GetLastApplied(tileId), before);
                var missing = baseline.mutators.Where(n => DefDatabase<TileMutatorDef>.GetNamedSilentFail(n) == null).ToList();
                if (missing.Count > 0) LastApplyWarning = "Unavailable original features omitted: " + string.Join(", ", missing);
                try { WorldTileEditor.Restore(tile, baseline, false); }
                catch { WorldTileEditor.Restore(tile, before); throw; }
            }
            wc?.RemoveState(tileId);
            wc?.RemoveBaseline(tileId);
            wc?.RemoveLastApplied(tileId);
            if (CurrentTileId == tileId) Reset();
            RefreshMapPreview();
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
                LastApplyWarning = (LastApplyWarning == null ? "" : LastApplyWarning + "\n") + "Map preview refresh failed: " + e.Message;
                Verse.Log.Warning("[MapGenAI] " + LastApplyWarning);
            }
        }

        public static void Reset()
        {
            editingTileId = -1;
            editingActive = false;
            editingState = new TileMapState();
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
                remove_categories = new List<string>(ReadState.removeFeatureCategories),
                elevation_shapes = ElevationShapes.Select(s => s.Clone()).ToList()
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

            sb.AppendLine($"- hills: {Hills}, hill_amount: {HillAmount:F2}");
            if (ReadState.imageMap != null && !ImageInput.ImageFeatureGate.Enabled)
                sb.AppendLine(isKo ? "- 저장된 이미지 데이터는 일시 중단되어 현재 생성에 영향이 없습니다." : "- Stored image data is paused and has no generation effect.");
            if (ReadState.structures.Count > 0)
                sb.AppendLine("- positioned_structures (edit by ID with structure_ops): " + MapGenAI.UI.SimpleJson.Serialize(ReadState.structures));
            if (ReadState.localRoads.Count > 0)
                sb.AppendLine("- local_roads (edit by ID with road_ops; local map only): " + MapGenAI.UI.SimpleJson.Serialize(ReadState.localRoads));
            if(ImageMap!=null) sb.AppendLine(isKo
                ? "- 이미지 지형이 적용되어 있습니다. 일반 대화는 이미지 영역을 직접 수정할 수 없습니다. 이미지 수정 요청에는 action:ask로 '이미지 지형' 창에서 영역을 선택하도록 안내하세요. 추가 SDF는 이미지 위에 적용됩니다."
                : "- An image terrain layer is present. This chat cannot directly edit image regions. For image correction use action:ask and direct the user to select a region in Image terrain. Added SDF shapes overlay the image layer.");

            // 현재 맵의 전체 elevation_shapes를 LLM에게 표시 (MDP: LLM이 현재 상태를 보고 완전한 새 상태를 출력)
            if (ElevationShapes.Count > 0)
            {
                string shapesJson = MapGenAI.UI.SimpleJson.Serialize(ShapeEdits.Describe(ElevationShapes));
                if (isKo)
                {
                    sb.AppendLine($"- elevation_shapes (현재 맵 상태. ID를 대상으로 shape_ops를 출력하세요):");
                    sb.AppendLine($"  \"elevation_shapes\":{shapesJson}");
                }
                else
                {
                    sb.AppendLine($"- elevation_shapes (current map state. Use shape_ops to edit by ID):");
                    sb.AppendLine($"  \"elevation_shapes\":{shapesJson}");
                }
            }

            sb.AppendLine($"- vegetation_density: {VegetationDensity:F1}, animal_density: {AnimalDensity:F1}, fertility_offset: {FertilityOffset:F2}");

            // 강: 설정된 경우에만 표시 (미설정 시 생략 → LLM이 타일 정보에서 강 유무 확인)
            if (HasRiver)
                sb.AppendLine($"- river: present (direction_angle={RiverDirectionAngle:F0}, x={RiverXPosition:F2}, z={RiverZPosition:F2}, straight={StraightRiver})");
            if (ReadState.removeFeatureCategories.Count > 0)
                sb.AppendLine("- suppressed_feature_categories: [" + string.Join(", ", ReadState.removeFeatureCategories) + "] (restore_categories to restore)");
            if (RemoveMutators.Count > 0)
                sb.AppendLine("- removed_mutators: [" + string.Join(", ", RemoveMutators) + "]");

            if (HasCaves) sb.AppendLine("- caves: true");
            if (CoastDirection != "auto") sb.AppendLine($"- coast_direction: {CoastDirection}");
            if (RockTypes.Count > 0) sb.AppendLine($"- rock_types: [{string.Join(", ", RockTypes)}]");
            if (Mutators.Count > 0) sb.AppendLine($"- added_mutators: [{string.Join(", ", Mutators)}]");
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
        // LLM JSON에 명시적으로 존재했던 키 추적 (MDP 병합용)
        public HashSet<string> explicitKeys = new HashSet<string>();

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
        public List<string> remove_categories; // Suppress all mutators in these categories on this map.
        public List<string> restore_categories; // Restore category generation from this tile's baseline.
        public List<ElevationShape> elevation_shapes;  // Elevation 프리미티브 목록
        public List<ShapeEdit> shape_ops;
        public List<StructureEdit> structure_ops;
        public List<RoadEdit> road_ops;
        public bool replace_shapes;
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
