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
        // Validate new commands, not old stored removals whose supplying mod may have been disabled.
        public static void ValidateFeatureRequest(MapParamsData data)
        {
            if (data.remove_mutators != null) foreach (string name in data.remove_mutators) Resolve(name);
            foreach (var category in (data.remove_categories ?? new List<string>()).Concat(data.restore_categories ?? new List<string>()))
                if (string.IsNullOrWhiteSpace(category) || !DefDatabase<TileMutatorDef>.AllDefsListForReading.Any(d => d.categories.Contains(category)))
                    throw new FormatException("Unknown feature category: " + category);
        }

        public static TileWorldSnapshot Rebase(TileWorldSnapshot baseline, TileWorldSnapshot applied, TileWorldSnapshot current)
        {
            if (baseline == null) return current;
            var names = new List<string>(baseline.mutators);
            if (applied != null)
            {
                names.RemoveAll(n => applied.mutators.Contains(n) && !current.mutators.Contains(n));
                foreach (var added in current.mutators.Except(applied.mutators)) if (!names.Contains(added)) names.Add(added);
            }
            return new TileWorldSnapshot { mutators = names, hilliness = current.hilliness };
        }

        public static List<TileMutatorDef> Plan(Tile tile, TileWorldSnapshot baseline, TileMapState state)
        {
            var suppressed = new HashSet<string>(state.removeFeatureCategories);
            if (state.removeMutators.Contains("River")) suppressed.Add("River");
            var additions = state.mutators.Select(Resolve).Where(d => !d.categories.Any(suppressed.Contains)).ToList();
            if (state.hasCaves && !additions.Any(d => d.defName == "Caves")) additions.Add(Resolve("Caves"));
            for (int i = 0; i < additions.Count; i++)
                for (int j = i + 1; j < additions.Count; j++)
                    if (Conflict(additions[i], additions[j]))
                        throw new FormatException("함께 적용할 수 없는 특징 / Incompatible features: " + additions[i].defName + ", " + additions[j].defName + ". remove_mutators로 교체할 대상을 지정하세요.");
            var removals = new HashSet<string>(state.removeMutators);
            if (state.cavesExplicitlySet && !state.hasCaves) removals.Add("Caves");
            var result = ResolveExisting(baseline.mutators).Where(d => !removals.Contains(d.defName) && !d.categories.Any(suppressed.Contains)).ToList();
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
            // Removing only a river variant means a normal river remains; world links stay intact.
            bool removedRiverVariant = ResolveExisting(removals).Any(d => d.defName != "River" && d.categories.Contains("River"));
            if (!suppressed.Contains("River") && !result.Any(d => d.categories.Contains("River")) && (state.hasRiver || removedRiverVariant))
            {
                if (tile is SurfaceTile riverTile && riverTile.Rivers?.Count > 0) result.Add(Resolve("River"));
                else if (state.hasRiver) throw new FormatException("This tile has no world river to restore; use a water shape for a custom channel.");
            }
            // Explicit category suppression also wins over convenience flags such as caves.
            return result.Where(d => !d.categories.Any(suppressed.Contains)).ToList();
        }

        static bool Conflict(TileMutatorDef a, TileMutatorDef b) => a.categories.Any(b.categories.Contains)
            || a.overrideCategories.Any(b.categories.Contains) || b.overrideCategories.Any(a.categories.Contains);

        static TileMutatorDef Resolve(string name) => DefDatabase<TileMutatorDef>.GetNamedSilentFail(name)
            ?? throw new FormatException("Feature is unavailable in the active mod list: " + name);

        public static List<TileMutatorDef> ResolveExisting(IEnumerable<string> names) => names
            .Select(n => DefDatabase<TileMutatorDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();

        public static void Replace(Tile tile, List<TileMutatorDef> desired)
        {
            foreach (var item in tile.Mutators.ToList()) if (!desired.Contains(item)) tile.RemoveMutator(item);
            foreach (var item in desired) if (!tile.Mutators.Contains(item)) tile.AddMutator(item);
            var expected = new HashSet<string>(desired.Select(d => d.defName));
            if (!expected.SetEquals(tile.Mutators.Select(d => d.defName))) throw new InvalidOperationException("World tile did not retain the requested features");
        }

        public static void Restore(Tile tile, TileWorldSnapshot snapshot, bool restoreHilliness = true)
        {
            Replace(tile, ResolveExisting(snapshot.mutators));
            if (restoreHilliness) tile.hilliness = snapshot.hilliness;
        }
    }
}
