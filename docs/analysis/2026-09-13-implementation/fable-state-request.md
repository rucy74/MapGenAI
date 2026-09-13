# Fable: 타일 상태 적용·복원 집중 반대 검토
파일 탐색·실행·편집·위임 없이 아래 소스만 읽고 한국어로 중요 결함 최대3개와 최소 재현 순서를 답해주세요. UI는 main thread에서만 Apply/Restore를 부릅니다. ShapeEdits/reducer는 깊은복사 후보만 만든 후 실패 시 throw; parser strict bounded, provider cancel 검토는 별도 완료했습니다.
배경: 원래 전역 originalMutators 하나라 A타일 편집 뒤 B타일 편집/selectionReset/mapgenerationReset 때 A의 실제 mutator가 원본으로 돌아가고 WorldComponent state와 어긋났습니다. 이제 tileBaselines는 WorldComponent의 optional dictionary로 Scribe 저장. Reset은 static generation cache만 비움. ClearTile(id)는 해당타일 baseline으로만 복원하고state/baseline삭제. RestoreSnapshot은 해당타일 baseline+snapshot으로 worldmetadata 재구성. 구 save에baseline없으면 첫편집당시타일을baseline삼으며 보존판 이전 pristine상태복원은불가능(명시적한계).
실제 게임 Assembly-CSharp Tile.AddMutator decompile확인: 같은categories에서 new.priority>=old이면old제거, 낮으면Log.Error후new도추가. new.overrideCategories와겹치는old도제거;genOrder정렬;Worker.OnAddedToTile호출. Coast기본은Coastvariant추가때원래교체되도록설계됨. 그래서 전부공존시키지않음. WorldTileEditor.Plan은 desired mutators내충돌을거부(사용자/모델이remove_mutators로명시교체하도록안내), baseline과의적법한교체는허용하고 실제+-diff를UI에표시.
Commit은 desired목록검증→tile변경→WC와static캐시커밋, 실패하면tile metadata+WC+cache복원. Worker.OnAdded의임의다른월드사이드이펙트까지되감기는아님. PreviewRefresh실패만LastApplyWarning으로정확히별도알림. UI는 Apply 성공 이후만Undo를push하고 actualstate diff표시. UI에서기존WCstate nullableclone을Undo에저장, null snapshot은ClearTile. Reset/close는대화초기snapshot복원.
검토목표: 실제 적용/Undo/다중타일/기존 특징 보존 회귀. 과장된 완료평가/칭찬불필요. 문제와수정안을구체적으로.

## MapGen/WorldTileEditor.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.MapGen
{
    // Tile metadata baseline is world-owned, never part of transferable map presets.
    public sealed class TileWorldSnapshot : IExposable
    {
        public List<string> mutators = new List<string>();
        public Hilliness hilliness;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref mutators, "mutators", LookMode.Value);
            Scribe_Values.Look(ref hilliness, "hilliness", Hilliness.Undefined);
            if (mutators == null) mutators = new List<string>();
        }
        public static TileWorldSnapshot Capture(Tile tile) => new TileWorldSnapshot { mutators = tile.Mutators.Select(m => m.defName).ToList(), hilliness = tile.hilliness };
    }

    public static class WorldTileEditor
    {
        public static List<TileMutatorDef> Plan(Tile tile, TileWorldSnapshot baseline, TileMapState state)
        {
            var additions = state.mutators.Select(Resolve).ToList();
            if (state.hasCaves && !additions.Any(d => d.defName == "Caves")) additions.Add(Resolve("Caves"));
            for (int i = 0; i < additions.Count; i++)
                for (int j = i + 1; j < additions.Count; j++)
                    if (Conflict(additions[i], additions[j]))
                        throw new FormatException("함께 적용할 수 없는 특징 / Incompatible features: " + additions[i].defName + ", " + additions[j].defName + ". remove_mutators로 교체할 대상을 지정하세요.");
            var removals = new HashSet<string>(state.removeMutators);
            if (state.cavesExplicitlySet && !state.hasCaves) removals.Add("Caves");
            var result = baseline.mutators.Select(Resolve).Where(d => !removals.Contains(d.defName)).ToList();
            foreach (var added in additions)
            {
                if (removals.Contains(added.defName)) throw new FormatException("Feature both enabled and removed: " + added.defName);
                if (added.categories.Contains("River") && (!(tile is SurfaceTile surface) || surface.Rivers == null || surface.Rivers.Count == 0))
                    throw new FormatException("River feature requires a natural river tile: " + added.defName);
                if (added.categories.Contains("Coast") && !tile.IsCoastal && !baseline.mutators.Contains("Coast"))
                    throw new FormatException("Coast feature requires a coastal tile: " + added.defName);
                foreach (var old in result.ToList())
                {
                    if (old == added) continue;
                    bool same = old.categories.Any(added.categories.Contains);
                    bool overridden = old.categories.Any(added.overrideCategories.Contains);
                    if (same && !overridden && added.priority < old.priority)
                        throw new FormatException("Feature priority prevents replacement: " + old.defName + " / " + added.defName);
                    if (same || overridden) result.Remove(old);
                }
                if (!result.Contains(added)) result.Add(added);
            }
            return result;
        }

        static bool Conflict(TileMutatorDef a, TileMutatorDef b) => a.categories.Any(b.categories.Contains)
            || a.overrideCategories.Any(b.categories.Contains) || b.overrideCategories.Any(a.categories.Contains);

        static TileMutatorDef Resolve(string name) => DefDatabase<TileMutatorDef>.GetNamedSilentFail(name)
            ?? throw new FormatException("Feature is unavailable in the active mod list: " + name);

        public static void Replace(Tile tile, List<TileMutatorDef> desired)
        {
            foreach (var item in tile.Mutators.ToList()) if (!desired.Contains(item)) tile.RemoveMutator(item);
            foreach (var item in desired) if (!tile.Mutators.Contains(item)) tile.AddMutator(item);
            var expected = new HashSet<string>(desired.Select(d => d.defName));
            if (!expected.SetEquals(tile.Mutators.Select(d => d.defName))) throw new InvalidOperationException("World tile did not retain the requested features");
        }

        public static void Restore(Tile tile, TileWorldSnapshot snapshot)
        {
            Replace(tile, snapshot.mutators.Select(Resolve).ToList());
            tile.hilliness = snapshot.hilliness;
        }
    }
}

```

## MapGen/MapGenAIWorldComponent.cs
```csharp
using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// 타일별 맵 생성 상태를 영구 저장하는 WorldComponent.
    /// 세이브/로드 시 유지됨. 각 타일의 TileMapState는 "현재 상태"이며 이력이 아님.
    /// </summary>
    public class MapGenAIWorldComponent : WorldComponent
    {
        private Dictionary<int, TileMapState> tileStates = new Dictionary<int, TileMapState>();
        private Dictionary<int, TileWorldSnapshot> tileBaselines = new Dictionary<int, TileWorldSnapshot>();

        public MapGenAIWorldComponent(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref tileStates, "tileStates", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref tileBaselines, "tileBaselines", LookMode.Value, LookMode.Deep);
            if (tileBaselines == null) tileBaselines = new Dictionary<int, TileWorldSnapshot>();
            if (tileStates == null)
                tileStates = new Dictionary<int, TileMapState>();
        }

        /// <summary>타일의 현재 상태를 반환. 없으면 null.</summary>
        public TileMapState GetState(int tileId)
        {
            return tileStates.TryGetValue(tileId, out var state) ? state : null;
        }

        /// <summary>타일의 현재 상태를 설정.</summary>
        public void SetState(int tileId, TileMapState state)
        {
            tileStates[tileId] = state.Clone();
        }

        public TileWorldSnapshot GetBaseline(int tileId) => tileBaselines.TryGetValue(tileId, out var value) ? value : null;
        public void SetBaseline(int tileId, TileWorldSnapshot baseline) => tileBaselines[tileId] = baseline;
        public void RemoveBaseline(int tileId) => tileBaselines.Remove(tileId);

        /// <summary>타일의 상태를 삭제 (리셋).</summary>
        public void RemoveState(int tileId)
        {
            tileStates.Remove(tileId);
        }

        /// <summary>해당 타일에 상태가 있는지 확인.</summary>
        public bool HasState(int tileId)
        {
            return tileStates.ContainsKey(tileId);
        }

        /// <summary>현재 월드의 WorldComponent 인스턴스를 가져옴.</summary>
        public static MapGenAIWorldComponent Get()
        {
            if (Find.World == null) return null;
            return Find.World.GetComponent<MapGenAIWorldComponent>();
        }
    }
}

```

## MapGenParams core
```csharp
        public static string LastApplyWarning { get; private set; }
        public static string LastWorldChanges { get; private set; }

        public static void ApplyPatch(MapParamsData data, int tileId)
        {
            LastApplyWarning = null;
            LastWorldChanges = null;
            var previous = MapGenAIWorldComponent.Get()?.GetState(tileId);
            var candidate = MapStateEditor.Merge(previous, data);
            if (MapStateCodec.ChangedFields(previous ?? new TileMapState(), candidate).Count == 0) return;
            CommitState(candidate, tileId);
        }

        public static void RestoreSnapshot(TileMapState snapshot, int tileId)
        {
            LastApplyWarning = null;
            LastWorldChanges = null;
            if (snapshot == null) { ClearTile(tileId); return; }
            CommitState(snapshot.Clone(), tileId);
        }

        private static void CommitState(TileMapState candidate, int tileId)
        {
            var wc = MapGenAIWorldComponent.Get();
            var tile = tileId < 0 ? null : Find.WorldGrid?[tileId];
            if (wc == null || tile == null) throw new System.FormatException("Map editing requires a valid world tile");
            var previous = wc.GetState(tileId)?.Clone();
            var beforeWorld = TileWorldSnapshot.Capture(tile);
            var savedBaseline = wc.GetBaseline(tileId);
            var baseline = savedBaseline ?? beforeWorld;
            var desired = WorldTileEditor.Plan(tile, baseline, candidate);
            var previousCacheTile = CurrentTileId;
            bool previousCacheActive = HasParams;
            try
            {
                WorldTileEditor.Replace(tile, desired);
                wc.SetState(tileId, candidate);
                if (savedBaseline == null) wc.SetBaseline(tileId, baseline);
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
        private static void ApplyStateToStaticFields(TileMapState state)
        {
            Hills = state.hills;
            HillAmount = state.hillAmount;
            VegetationDensity = state.vegetationDensity;
            AnimalDensity = state.animalDensity;
            FertilityOffset = state.fertilityOffset;
            HasRiver = state.hasRiver;
            RiverDirection = state.riverDirectionAngle == 90f ? "horizontal" : "vertical";
            RiverDirectionAngle = state.riverDirectionAngle;
            RiverXPosition = state.riverXPosition;
            RiverZPosition = state.riverZPosition;
            HasRoads = state.hasRoads;
            HasCaves = state.hasCaves;
            CavesExplicitlySet = state.cavesExplicitlySet;
            GeyserCount = state.geyserCount;
            HasRockChunks = state.hasRockChunks;
            HillSize = state.hillSize;
            HillSmoothness = state.hillSmoothness;
            StraightRiver = state.straightRiver;
            CoastDirection = state.coastDirection;
            RockCount = state.rockCount;
            OreDensity = state.oreDensity;
            RuinDensity = state.ruinDensity;
            DangerDensity = state.dangerDensity;

            Mutators.Clear();
            Mutators.AddRange(state.mutators);
            RemoveMutators.Clear();
            RemoveMutators.AddRange(state.removeMutators);
            RockTypes.Clear();
            RockTypes.AddRange(state.rockTypes);

            ElevationShapes.Clear();
            foreach (var s in state.elevationShapes)
                ElevationShapes.Add(s.Clone());
        }

        /// <summary>WorldComponent에서 타일 상태를 로드하여 정적 필드에 적용.</summary>
        public static void LoadFromTile(int tileId)
        {
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
                try { WorldTileEditor.Restore(tile, baseline); }
                catch { WorldTileEditor.Restore(tile, before); throw; }
            }
            wc?.RemoveState(tileId);
            wc?.RemoveBaseline(tileId);
            if (CurrentTileId == tileId) Reset();
            RefreshMapPreview();
        }


```
