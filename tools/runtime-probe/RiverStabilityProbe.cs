using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    // Replays the saved showcase states and plain feature additions on tiles of the showcase world, then records the
    // generated world-river and ocean cells from Map Preview's worker and from MapGenerator.GenerateMap. No model call.
    // It records facts only; tools/evaluate_river_stability.py judges the fixed and control runs.
    static class RiverStabilityProbe
    {
        const string WorldSeed = "mapgenai-showcase-20260927";
        const int Size = 250, ShowcaseRiverTile = 281, ShowcaseCoastTile = 83;
        // Features that initialize from the shared stream without creating water (decompiled 1.6.4871 Init bodies).
        static readonly string[] Dry = { "Caves", "Dunes", "Cliffs", "Valley", "Hollow", "Plateau", "Crevasse" };
        static readonly string[] Lakes = { "Lake", "Pond", "LakeWithIslands", "Basin" };
        static string folder, showcase;
        static bool active, finishing;
        static float quitAt;
        static DateTime deadline;
        static IEnumerator<object> routine;
        static readonly Dictionary<string, object> result = new Dictionary<string, object>();
        static readonly List<object> records = new List<object>();
        static readonly object gate = new object();
        static Dictionary<string, object> latest;
        static readonly ConditionalWeakTable<Map, StrongBox<IntVec3>> centres = new ConditionalWeakTable<Map, StrongBox<IntVec3>>();
        static readonly ConditionalWeakTable<Map, StrongBox<string>> coastFields = new ConditionalWeakTable<Map, StrongBox<string>>();
        static readonly ConditionalWeakTable<Map, Dictionary<int,float>> roadHeights = new ConditionalWeakTable<Map, Dictionary<int,float>>();
        static readonly ConditionalWeakTable<Map, List<int>> courses = new ConditionalWeakTable<Map, List<int>>();
        static System.Reflection.FieldInfo depthMaps;
        static AccessTools.FieldRef<TileMutatorWorker_River, IntVec3> riverCenter;

        public static void Configure()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAIRiverStability", out var path)) return;
            showcase = path;
            riverCenter = AccessTools.FieldRefAccess<TileMutatorWorker_River, IntVec3>("riverCenter");
            var h = new Harmony("choco.mapgenai.probe.riverstability");
            h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(RiverStabilityProbe), nameof(FixedWorldSeed)));
            h.Patch(AccessTools.Method(typeof(TileMutatorWorker_River), "Init"), postfix: new HarmonyMethod(typeof(RiverStabilityProbe), nameof(AfterRiverInit)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(TileMutatorWorker_Coast), "Init"), postfix: new HarmonyMethod(typeof(RiverStabilityProbe), nameof(AfterCoastInit)) { priority = Priority.Last });
            depthMaps = AccessTools.Field(typeof(TileMutatorWorker_River), "nodeDepthMaps");
            h.Patch(AccessTools.Method(typeof(TileMutatorWorker_River), "GeneratePostTerrain"), postfix: new HarmonyMethod(typeof(RiverStabilityProbe), nameof(AfterRiverTerrain)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap)), postfix: new HarmonyMethod(typeof(RiverStabilityProbe), nameof(AfterContents)) { priority = Priority.Last });
        }

        static void FixedWorldSeed(ref string seedString) => seedString = WorldSeed;

        static void AfterRiverInit(TileMutatorWorker_River __instance, Map map)
        {
            centres.Remove(map);
            centres.Add(map, new StrongBox<IntVec3>(riverCenter(__instance)));
        }

        static void AfterCoastInit(TileMutatorWorker_Coast __instance, Map map)
        {
            var noise = (Verse.Noise.ModuleBase)AccessTools.Field(typeof(TileMutatorWorker_Coast), "coastNoise").GetValue(__instance);
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes))
            using (var sha = SHA256.Create())
            {
                foreach (var cell in map.AllCells) writer.Write(noise.GetValue(cell.x, 0, cell.z));
                coastFields.Remove(map);
                coastFields.Add(map, new StrongBox<string>(BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant()));
            }
        }

        // The river's own course: cells the river worker gives positive depth, whatever other water covers them.
        static void AfterRiverTerrain(TileMutatorWorker_River __instance, Map map)
        {
            var maps = (System.Collections.IDictionary)depthMaps.GetValue(__instance);
            var nodes = map.waterInfo.riverGraph.Where(n => maps.Contains(n)).Select(n => (float[])maps[n]).ToList();
            int stride = map.Size.x + 50, count = map.cellIndices.NumGridCells;
            var course = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var cell = map.cellIndices.IndexToCell(i);
                int index = cell.x + 25 + (cell.z + 25) * stride;
                if (nodes.Any(depth => depth[index] > 0f)) course.Add(i);
            }
            courses.Remove(map);
            courses.Add(map, course);
        }

        // Runs on the generating thread (Map Preview worker or main) after every gen step, before the preview map is disposed.
        static void AfterContents(Map map)
        {
            try
            {
                // Base terrain: GenStep_Snow lays temporary ThinIce over water on cold-season maps, and TerrainAt returns it first.
                var river = new List<int>(); var ocean = new List<int>(); int springs = 0, frozen = 0;
                var names = new StringBuilder();
                int count = map.cellIndices.NumGridCells;
                for (int i = 0; i < count; i++)
                {
                    var cell = map.cellIndices.IndexToCell(i);
                    var terrain = map.terrainGrid.BaseTerrainAt(cell);
                    if (map.terrainGrid.TerrainAt(cell) != terrain) frozen++;
                    string name = terrain?.defName ?? "none";
                    names.Append(name).Append('\n');
                    if (terrain == null) continue;
                    if (terrain.IsRiver) river.Add(i);
                    if (name.Contains("Ocean")) ocean.Add(i);
                    if (name == "HotSpring") springs++;
                }
                var capture = new Dictionary<string, object> { { "tile", map.Tile.tileId }, { "width", map.Size.x }, { "height", map.Size.z },
                    { "preview", !UnityData.IsInMainThread }, { "river", river }, { "ocean", ocean },
                    { "hotSpringCells", springs }, { "coveredCells", frozen }, { "terrainSha256", Hash(names.ToString()) } };
                if (centres.TryGetValue(map, out var centre)) capture["riverCentre"] = new List<object> { centre.Value.x, centre.Value.z };
                if (coastFields.TryGetValue(map, out var coast)) capture["coastFieldSha256"] = coast.Value;
                if (courses.TryGetValue(map, out var course)) capture["course"] = course;
                var state = GenerationContext.State;
                var authored = state == null ? null : AuthoringGeneration.Latest(map.Tile.tileId, state);
                if (authored != null) capture["authoring"] = new Dictionary<string, object> {
                    { "issues", authored.issues.ToArray() }, { "roads", authored.roads.ToArray() } };
                if (authored?.roads.Count > 0)
                {
                    roadHeights.Remove(map);
                    roadHeights.Add(map, authored.roads.SelectMany(r => r.path).Select(p => p[1]*map.Size.x+p[0]).Distinct()
                        .ToDictionary(i => i, i => MapGenerator.Elevation[new IntVec3(i%map.Size.x,0,i/map.Size.x)]));
                }
                lock (gate) latest = capture;
            }
            catch (Exception error) { lock (gate) latest = new Dictionary<string, object> { { "captureError", error.ToString() } }; }
        }

        public static void Run(string output)
        {
            folder = output;
            Application.runInBackground = true;
            deadline = DateTime.UtcNow.AddMinutes(40);
            active = true;
            routine = (Path.GetFileName(showcase) == "edit-stability" ? EditStabilityScript() : Path.GetFileName(showcase) == "road-diagnostic" ? RoadDiagnosticScript() : Script()).GetEnumerator();
        }

        public static void Tick()
        {
            if (!active) return;
            if (finishing)
            {
                if (Time.realtimeSinceStartup >= quitAt) { active = false; Application.Quit(); }
                return;
            }
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("River stability run exceeded its time limit");
                if (!routine.MoveNext()) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        static void Finish(Exception error)
        {
            if (finishing) return;
            finishing = true;
            try
            {
                result["ok"] = error == null; result["error"] = error?.ToString(); result["records"] = records;
                result["isolatedInitsTotal"] = Isolated();
                var patch = AccessTools.TypeByName("MapGenAI.Patches.FeatureInitStreamPatch");
                result["isolatedPostTerrainTotal"] = patch == null ? -1 : AccessTools.Field(patch, "IsolatedPostTerrain")?.GetValue(null);
                File.WriteAllText(Path.Combine(folder, "result.json"), SimpleJson.Serialize(result));
            }
            catch (Exception writeError) { Log.Error("[MapGenAI RiverStability] result write failed: " + writeError); }
            quitAt = Time.realtimeSinceStartup + 5f;
        }

        static IEnumerable<object> Script()
        {
            foreach (var y in Frames(30)) yield return y;
            Directory.CreateDirectory(Path.Combine(folder, "captures"));
            result["worldSeed"] = Find.World.info.seedString;
            if (Find.World.info.seedString != WorldSeed) throw new InvalidOperationException("World seed was not fixed");
            var patch = AccessTools.TypeByName("MapGenAI.Patches.FeatureInitStreamPatch");
            result["routing"] = patch == null ? (object)"absent" : new Dictionary<string, object> {
                { "generateMap", AccessTools.Field(patch, "MapSiteRouted")?.GetValue(null) }, { "mapPreview", AccessTools.Field(patch, "PreviewSiteRouted")?.GetValue(null) },
                { "postTerrain", AccessTools.Field(patch, "PostTerrainRouted")?.GetValue(null) } };
            var tileFacts = new Dictionary<string, object>();
            result["tiles"] = tileFacts;

            // 1. Showcase tile 281: saved states 05, 07, 08 and additions to 07.
            var s05 = Saved("05-island-move.json"); var s07 = Saved("07-river-straight.json"); var s08 = Saved("08-hot-springs.json");
            tileFacts["A"] = Facts(ShowcaseRiverTile);
            foreach (var y in Variant("A-07", ShowcaseRiverTile, s07, true)) yield return y;
            foreach (var y in Variant("A-08", ShowcaseRiverTile, s08, true)) yield return y;
            string dry = null;
            foreach (var name in Dry)
            {
                var st = s07.Clone(); st.mutators.Add(name);
                if (!Applies(ShowcaseRiverTile, st)) continue;
                dry = name; break;
            }
            result["A-dryFeature"] = dry;
            if (dry != null) { var st = s07.Clone(); st.mutators.Add(dry); foreach (var y in Variant("A-07-dry", ShowcaseRiverTile, st, true)) yield return y; }
            string lake = null;
            foreach (var name in Lakes)
            {
                var st = s07.Clone(); st.mutators.Add(name);
                if (!Applies(ShowcaseRiverTile, st)) continue;
                lake = name; break;
            }
            result["A-lakeFeature"] = lake;
            if (lake != null) { var st = s07.Clone(); st.mutators.Add(lake); foreach (var y in Variant("A-07-lake", ShowcaseRiverTile, st, false)) yield return y; }
            var island = s07.Clone(); island.mutators.Add("RiverIsland");
            foreach (var y in Variant("A-07-island", ShowcaseRiverTile, island, false)) yield return y;
            var direction = s07.Clone(); direction.riverDirectionAngle = 0f;
            foreach (var y in Variant("A-07-north", ShowcaseRiverTile, direction, false)) yield return y;
            foreach (var y in Variant("A-05", ShowcaseRiverTile, s05, false)) yield return y;
            var s05Springs = s05.Clone(); s05Springs.mutators.Add("HotSprings");
            foreach (var y in Variant("A-05-springs", ShowcaseRiverTile, s05Springs, false)) yield return y;
            MapGenParams.ClearTile(ShowcaseRiverTile);

            // 2. More river tiles: an empty state, then hot springs, then one dry feature.
            var used = new HashSet<int> { ShowcaseRiverTile, ShowcaseCoastTile };
            var springsDef = DefDatabase<TileMutatorDef>.GetNamed("HotSprings");
            var riverTiles = Candidates(t => FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0, used);
            int rivers = 0;
            foreach (var tile in Spread(riverTiles))
            {
                if (rivers >= 7) break;
                if (FeaturePolicy.UnavailableReason(springsDef, tile) != null) continue;
                int id = tile.tile;
                string feature = Dry.FirstOrDefault(n => { var st = new TileMapState(); st.mutators.Add(n); return Applies(id, st); });
                MapGenParams.ClearTile(id);
                if (feature == null) continue;
                used.Add(id); rivers++;
                tileFacts["R" + rivers] = Facts(id);
                foreach (var y in Variant("R" + rivers + "-base", id, new TileMapState(), false)) yield return y;
                var springs = new TileMapState(); springs.mutators.Add("HotSprings");
                foreach (var y in Variant("R" + rivers + "-springs", id, springs, false)) yield return y;
                var dryState = new TileMapState(); dryState.mutators.Add(feature);
                foreach (var y in Variant("R" + rivers + "-dry-" + feature, id, dryState, false)) yield return y;
                MapGenParams.ClearTile(id);
            }

            // 3. Coast tiles: tile 83 and one more ocean shore without a river.
            tileFacts["B"] = Facts(ShowcaseCoastTile);
            foreach (var y in Variant("B-base", ShowcaseCoastTile, new TileMapState(), true)) yield return y;
            var coastSprings = new TileMapState(); coastSprings.mutators.Add("HotSprings");
            foreach (var y in Variant("B-springs", ShowcaseCoastTile, coastSprings, true)) yield return y;
            MapGenParams.ClearTile(ShowcaseCoastTile);
            var coastTiles = Candidates(t => !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count > 0
                && FeaturePolicy.WaterNeighbors(t).All(n => n.PrimaryBiome == BiomeDefOf.Ocean), used);
            int coasts = 0;
            foreach (var tile in Spread(coastTiles))
            {
                if (coasts >= 2) break;
                if (FeaturePolicy.UnavailableReason(springsDef, tile) != null) continue;
                int id = tile.tile; used.Add(id); coasts++;
                tileFacts["C" + coasts] = Facts(id);
                foreach (var y in Variant("C" + coasts + "-base", id, new TileMapState(), false)) yield return y;
                var springs = new TileMapState(); springs.mutators.Add("HotSprings");
                foreach (var y in Variant("C" + coasts + "-springs", id, springs, false)) yield return y;
                MapGenParams.ClearTile(id);
            }

            // 4. Tiles without any MapGen AI state: compared across builds.
            var wc = MapGenAIWorldComponent.Get();
            var plain = new List<SurfaceTile>();
            plain.AddRange(Spread(riverTiles).Where(t => !used.Contains(t.tile)).Take(1));
            plain.AddRange(Spread(coastTiles).Where(t => !used.Contains(t.tile)).Take(1));
            plain.AddRange(Spread(Candidates(t => !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0, used)).Take(1));
            int n = 0;
            foreach (var tile in plain)
            {
                n++; int id = tile.tile;
                tileFacts["N" + n] = Facts(id);
                tileFacts["N" + n + "-hasState"] = wc?.GetState(id) != null || wc?.GetBaseline(id) != null;
                foreach (var y in Variant("N" + n + "-none", id, null, false)) yield return y;
            }
        }

        static IEnumerable<object> EditStabilityScript()
        {
            foreach (var y in Frames(30)) yield return y;
            Directory.CreateDirectory(Path.Combine(folder, "captures"));
            var wc = MapGenAIWorldComponent.Get();
            result["worldSeed"] = Find.World.info.seedString;
            // Controlled natural features on existing river/shore tiles, not a user profile.
            foreach (int id in new[] { ShowcaseRiverTile, ShowcaseCoastTile })
            {
                string prefix = id == ShowcaseRiverTile ? "river" : "coast";
                var tile = Find.WorldGrid[id];
                var natural = TileWorldSnapshot.Capture(tile);
                var surface = (SurfaceTile)tile; var hilliness = surface.hilliness;
                MapGenParams.ClearTile(id);
                surface.hilliness = Hilliness.Mountainous; // Cavern's native requirement; restored below.
                tile.AddMutator(DefDatabase<TileMutatorDef>.GetNamed("Caves"));
                foreach (var y in Variant(prefix + "-caves", id, new TileMapState(), true)) yield return y;
                var removed = new TileMapState(); removed.removeMutators.Add("Caves");
                foreach (var y in Variant(prefix + "-removed", id, removed, true)) yield return y;
                var replaced = new TileMapState(); replaced.mutators.Add("Cavern");
                foreach (var y in Variant(prefix + "-replaced", id, replaced, true)) yield return y;
                if (id == ShowcaseRiverTile)
                {
                    var island = removed.Clone(); island.mutators.Add("RiverIsland");
                    foreach (var y in Variant(prefix + "-removed-island", id, island, false)) yield return y;
                }
                MapGenParams.ClearTile(id); WorldTileEditor.Restore(tile, natural); surface.hilliness = hilliness;
            }
            // Pure additions and unedited cells must retain the previous build's output.
            foreach (int id in new[] { ShowcaseRiverTile, ShowcaseCoastTile })
            {
                string prefix = id == ShowcaseRiverTile ? "river" : "coast";
                MapGenParams.ClearTile(id);
                foreach (var y in Variant(prefix + "-plain", id, null, true)) yield return y;
                var springs = new TileMapState(); springs.mutators.Add("HotSprings");
                foreach (var y in Variant(prefix + "-added-springs", id, springs, true)) yield return y;
                MapGenParams.ClearTile(id);
            }
            // The editor holds explicit settings for A while an ordinary preview generates B.
            var other = Candidates(t => FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0,
                new HashSet<int> { ShowcaseRiverTile, ShowcaseCoastTile }).First(t => t.Mutators.All(d => d.defName == "River"));
            int otherId = other.tile;
            MapGenParams.ClearTile(otherId); MapGenParams.ClearTile(ShowcaseRiverTile);
            foreach (var y in Variant("other-before", otherId, null, false)) yield return y;
            MapGenParams.RestoreSnapshot(new TileMapState { riverDirectionAngle = 0f, riverXPosition = .85f, straightRiver = true }, ShowcaseRiverTile);
            foreach (var y in Variant("other-editor-active", otherId, null, false)) yield return y;
            MapGenParams.ClearTile(ShowcaseRiverTile);
            // A snapshot must use native genOrder before Init, just like applying its state.
            var candidateType = AccessTools.TypeByName("MapGenAI.Patches.CandidatePreviewSnapshot");
            var state = new TileMapState(); state.mutators.Add("HotSprings"); state.mutators.Add("Caves");
            var candidate = Activator.CreateInstance(candidateType, new object[] { ShowcaseRiverTile, state, null });
            var candidateTile = (Tile)AccessTools.Field(candidateType, "Tile").GetValue(candidate);
            result["candidateOrder"] = candidateTile.Mutators.Select(d => d.defName).ToArray();
            bool candidateDone = false; string candidateError = null;
            lock (gate) latest = null;
            QueuePreview(ShowcaseRiverTile, "candidate-unapplied", message => { candidateError = message; candidateDone = true; }, candidate);
            float candidateDeadline = Time.realtimeSinceStartup + 180f;
            while (!candidateDone && Time.realtimeSinceStartup < candidateDeadline) yield return null;
            if (!candidateDone || candidateError != null) throw new InvalidOperationException("Candidate request failed: " + candidateError);
            records.Add(new Dictionary<string, object> { { "id", "candidate-unapplied" }, { "preview", Save("candidate-unapplied-preview") } });
            MapGenParams.RestoreSnapshot(state, ShowcaseRiverTile);
            result["appliedOrder"] = Find.WorldGrid[ShowcaseRiverTile].Mutators.Select(d => d.defName).ToArray();
            foreach (var y in Variant("candidate-applied", ShowcaseRiverTile, state, false)) yield return y;
            MapGenParams.ClearTile(ShowcaseRiverTile);
            var inputs = Path.GetDirectoryName(showcase);
            if (File.Exists(Path.Combine(inputs, "north-before.json")))
            {
                foreach (string name in new[] { "north", "road" })
                {
                    var before = MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(inputs, name + "-before.json")));
                    var response = ProviderResponse.Command(File.ReadAllText(Path.Combine(inputs, name + "-response.json")));
                    MapGenParams.ClearTile(ShowcaseRiverTile);
                    MapGenParams.RestoreSnapshot(before, ShowcaseRiverTile);
                    MapGenParams.ApplyPatch(MapParameterParser.Parse(response.GetObject("params")), ShowcaseRiverTile);
                    var after = MapGenParams.CaptureState(ShowcaseRiverTile);
                    result[name + "-stateBefore"] = SimpleJson.Parse(MapStateCodec.Serialize(before));
                    result[name + "-stateAfter"] = SimpleJson.Parse(MapStateCodec.Serialize(after));
                    MapGenParams.ClearTile(ShowcaseRiverTile);
                    foreach (var y in Variant(name + "-before", ShowcaseRiverTile, before, true)) yield return y;
                    foreach (var y in Variant(name + "-after", ShowcaseRiverTile, after, true)) yield return y;
                    MapGenParams.ClearTile(ShowcaseRiverTile);
                }
            }
            result["apiCalls"] = 0;
        }

        // Commits the state as the dialog does (null = no state), then generates a Map Preview and optionally a real map.
        static IEnumerable<object> RoadDiagnosticScript()
        {
            foreach (var y in Frames(30)) yield return y;
            Directory.CreateDirectory(Path.Combine(folder, "captures"));
            var inputs = Path.GetDirectoryName(showcase);
            var before = MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(inputs, "road-before.json")));
            MapGenParams.RestoreSnapshot(before, ShowcaseRiverTile);
            var response = ProviderResponse.Command(File.ReadAllText(Path.Combine(inputs, "road-response.json")));
            MapGenParams.ApplyPatch(MapParameterParser.Parse(response.GetObject("params")), ShowcaseRiverTile);
            foreach (var y in Variant("road-after", ShowcaseRiverTile, MapGenParams.CaptureState(ShowcaseRiverTile), true)) yield return y;
        }

        static IEnumerable<object> Variant(string id, int tile, TileMapState state, bool realMap)
        {
            var record = new Dictionary<string, object> { { "id", id }, { "tile", tile } };
            records.Add(record);
            try { if (state != null) MapGenParams.RestoreSnapshot(state, tile); }
            catch (Exception error) { record["applyError"] = error.Message; yield break; }
            record["features"] = Find.WorldGrid[tile].Mutators.Select(d => d.defName).ToList();
            record["state"] = state != null;
            int isolatedBefore = Isolated();
            lock (gate) latest = null;
            bool done = false; string failure = null;
            QueuePreview(tile, id, message => { failure = message; done = true; });
            float until = Time.realtimeSinceStartup + 180f;
            while (!done && Time.realtimeSinceStartup < until) yield return null;
            if (!done) throw new TimeoutException("Map Preview did not finish " + id);
            if (failure != null) throw new InvalidOperationException("Map Preview failed for " + id + ": " + failure);
            record["preview"] = Save(id + "-preview");
            record["isolatedPreview"] = Isolated() - isolatedBefore;
            if (realMap)
            {
                isolatedBefore = Isolated();
                lock (gate) latest = null;
                var parent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var map = MapGenerator.GenerateMap(new IntVec3(Size, 1, Size), parent, parent.MapGeneratorDef);
                var report = state == null ? null : AuthoringGeneration.Latest(tile, state);
                if (report != null && report.roads.Count > 0)
                {
                    record["roadObstacles"] = report.roads.SelectMany(r => r.path).Distinct().Select(p => new IntVec3(p[0],0,p[1])).Select(c => new Dictionary<string, object> {
                        { "x", c.x }, { "z", c.z }, { "terrain", c.GetTerrain(map).defName }, { "elevation", roadHeights.TryGetValue(map,out var heights) ? heights[c.z*map.Size.x+c.x] : -1f },
                        { "edifice", c.GetEdifice(map)?.def.defName }, { "edificePassability", c.GetEdifice(map)?.def.passability.ToString() }, { "water", c.GetTerrain(map).IsWater }, { "river", c.GetTerrain(map).IsRiver } })
                        .Where(c => (string)c["edifice"] != null || (bool)c["water"] || (bool)c["river"] || (float)c["elevation"] >= .7f).ToArray();
                }
                record["mapSeconds"] = watch.Elapsed.TotalSeconds;
                record["map"] = Save(id + "-map");
                record["isolatedMap"] = Isolated() - isolatedBefore;
                Current.Game.DeinitAndRemoveMap(map, false);
                if (!parent.Destroyed) parent.Destroy();
                foreach (var y in Frames(5)) yield return y;
            }
        }

        // Map Preview types stay inside this non-inlined method: that assembly is not resolvable while the starting map generates,
        // and a Map Preview type in an iterator or capture field breaks loading the whole probe type.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void QueuePreview(int tile, string id, Action<string> completed, object candidate = null)
        {
            var request = new MapPreview.MapPreviewRequest(Find.World.info.seedString, tile, new IntVec2(Size, Size)) { UseMinimalMapComponents = true, UseTrueTerrainColors = true };
            if (candidate != null)
            {
                var requests = AccessTools.Field(AccessTools.TypeByName("MapGenAI.Patches.CandidatePreviewContext"), "Requests").GetValue(null);
                AccessTools.Method(requests.GetType(), "Add").Invoke(requests, new object[] { request, candidate });
            }
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(r =>
            {
                var texture = new Texture2D(Size, Size);
                try { r.CopyToTexture(texture); texture.Apply(); File.WriteAllBytes(Path.Combine(folder, "captures", id + "-preview.png"), ImageConversion.EncodeToPNG(texture)); }
                finally { UnityEngine.Object.Destroy(texture); }
                completed(null);
            }).Catch(e => completed(e.Message));
        }

        static Dictionary<string, object> Save(string name)
        {
            Dictionary<string, object> capture;
            lock (gate) capture = latest;
            if (capture == null) return new Dictionary<string, object> { { "missing", true } };
            File.WriteAllText(Path.Combine(folder, "captures", name + ".json"), SimpleJson.Serialize(capture));
            var summary = capture.Where(p => p.Key != "river" && p.Key != "ocean" && p.Key != "course").ToDictionary(p => p.Key, p => p.Value);
            if (capture.TryGetValue("course", out var course)) { summary["courseCells"] = ((List<int>)course).Count; summary["courseSha256"] = Hash(string.Join(",", (List<int>)course)); }
            summary["riverCells"] = (capture["river"] as List<int>)?.Count;
            summary["oceanCells"] = (capture["ocean"] as List<int>)?.Count;
            summary["riverSha256"] = Hash(string.Join(",", (List<int>)capture["river"]));
            summary["oceanSha256"] = Hash(string.Join(",", (List<int>)capture["ocean"]));
            summary["file"] = "captures/" + name + ".json";
            return summary;
        }

        static bool Applies(int tile, TileMapState state)
        {
            try { MapGenParams.RestoreSnapshot(state, tile); return true; }
            catch (Exception) { return false; }
        }

        static TileMapState Saved(string file)
        {
            var step = SimpleJson.Parse(File.ReadAllText(Path.Combine(showcase, file)));
            return MapStateCodec.Deserialize(SimpleJson.Serialize(step.GetObject("stateAfter")));
        }

        static List<SurfaceTile> Candidates(Func<SurfaceTile, bool> rule, HashSet<int> used)
        {
            var list = new List<SurfaceTile>();
            foreach (var tile in Find.WorldGrid.Tiles)
            {
                if (tile.PrimaryBiome == null || tile.WaterCovered || used.Contains(tile.tile)) continue;
                if (Find.World.Impassable(tile.tile) || Find.WorldObjects.AnyWorldObjectAt(tile.tile)) continue;
                if (rule(tile)) list.Add(tile);
            }
            return list.OrderBy(t => (int)t.tile).ToList();
        }

        // Every k-th candidate first, so the tiles come from across the world rather than one region.
        static IEnumerable<SurfaceTile> Spread(List<SurfaceTile> list)
        {
            int step = Math.Max(1, list.Count / 12);
            for (int offset = 0; offset < step; offset++)
                for (int i = offset; i < list.Count; i += step) yield return list[i];
        }

        static Dictionary<string, object> Facts(int id)
        {
            var tile = Find.WorldGrid[id] as SurfaceTile;
            return new Dictionary<string, object> { { "tile", id }, { "biome", tile?.PrimaryBiome?.defName }, { "hilliness", tile?.hilliness.ToString() },
                { "features", tile?.Mutators.Select(m => m.defName).ToList() },
                { "rivers", (tile?.Rivers ?? new List<SurfaceTile.RiverLink>()).Select(r => r.river.defName).ToList() },
                { "waterNeighbours", tile == null ? new List<string>() : FeaturePolicy.WaterNeighbors(tile).Select(t => t.PrimaryBiome.defName).ToList() } };
        }

        static int Isolated()
        {
            var patch = AccessTools.TypeByName("MapGenAI.Patches.FeatureInitStreamPatch");
            return patch == null ? -1 : (int)AccessTools.Field(patch, "IsolatedInits").GetValue(null);
        }

        static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        static IEnumerable<object> Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    }
}
