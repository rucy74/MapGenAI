using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.LLM;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;
using static CoreRegressionTests;

static class RoadPlanTests
{
    const string Add = @"{""road_ops"":[{""op"":""add"",""road"":{""id"":""secret_road"",""kind"":""AncientAsphaltRoad"",""points"":[[0,0.5],[0.5,0.7],[1,0.5]]}}]}";
    static MapParamsData Parse(string json) => MapParameterParser.Parse(SimpleJson.Parse(json));
    static TileMapState Edit(TileMapState before, string json) => MapStateEditor.Merge(before, Parse(json));
    static string Update(string fields) => "{\"road_ops\":[{\"op\":\"update\",\"id\":\"secret_road\",\"changes\":{" + fields + "}}]}";
    public static void RunAll()
    {
        Check("Road-only edits preserve every other terrain structure river and legacy road field", () =>
        {
            var before = Edit(new TileMapState(), @"{""hills"":""left"",""animal_density"":1.3,""river"":{""present"":true,""x_position"":0.2},""roads"":true,""structure_ops"":[{""op"":""add"",""structure"":{""id"":""ruins"",""kind"":""ruin"",""position"":[0.4,0.6],""width"":9,""height"":9}}]}" );
            before.mutators.Add("ExistingFeature");
            before.imageMap = new MapGenAI.ImageInput.ImageMapData { width = 1, height = 1, cells = "W" };
            var after = Edit(before, Add);
            Equal("localRoads", string.Join(",", MapStateCodec.ChangedFields(before, after)));
            Equal(0, before.localRoads.Count); Equal(1, after.localRoads.Count); Equal("avoid", after.localRoads[0].route);
            Equal(true, after.hasRoads); Equal(false, after.IsDefault());
            Equal(MapStateCodec.Serialize(after), MapStateCodec.Serialize(Edit(after, "{}")));
            Equal(MapStateCodec.Serialize(after), MapStateCodec.Serialize(Edit(after, "{\"road_ops\":[]}")));
            var untracked = Parse(Add); untracked.explicitKeys.Clear();
            Equal(MapStateCodec.Serialize(before), MapStateCodec.Serialize(MapStateEditor.Merge(before, untracked)));
        });
        Check("Road kind and waypoint updates preserve unspecified fields and removal targets only its road", () =>
        {
            var before = Edit(new TileMapState(), Add);
            var changed = Edit(before, Update(@"""kind"":""StoneRoad"""));
            Equal("AncientAsphaltRoad", before.localRoads[0].kind); Equal("StoneRoad", changed.localRoads[0].kind);
            Equal(SimpleJson.Serialize(before.localRoads[0].points), SimpleJson.Serialize(changed.localRoads[0].points));
            var moved = Edit(changed, Update(@"""points"":[[0.3,0],[0.3,1]],""route"":""direct"",""seed"":45"));
            Equal("StoneRoad", moved.localRoads[0].kind); Equal(45, moved.localRoads[0].seed); Equal(2, moved.localRoads[0].points.Length);
            var second = Edit(moved, Add.Replace("secret_road", "second_road"));
            var removed = Edit(second, @"{""road_ops"":[{""op"":""remove"",""id"":""secret_road""}]}" );
            Equal("second_road", removed.localRoads.Single().id); Equal(2, second.localRoads.Count);
            Equal(true, Edit(before, @"{""road_ops"":[{""op"":""remove"",""id"":""secret_road""}]}" ).IsDefault());
        });
        Check("Road snapshots deep copy nested waypoints and codec preserves all fields", () =>
        {
            var before = Edit(new TileMapState(), Add);
            var clone = before.Clone(); clone.localRoads[0].points[1][0] = .2f; clone.localRoads[0].kind = "DirtPath";
            Equal(.5f, before.localRoads[0].points[1][0]); Equal("AncientAsphaltRoad", before.localRoads[0].kind);
            var loaded = MapStateCodec.Deserialize(MapStateCodec.Serialize(before));
            Equal(MapStateCodec.Serialize(before), MapStateCodec.Serialize(loaded));
            loaded.localRoads[0].points[0][1] = .1f; Equal(.5f, before.localRoads[0].points[0][1]);
        });
        Check("Old presets normalize missing or null road lists without inventing a road", () =>
        {
            foreach (var json in new[] { "{\"schema_version\":2,\"state\":{}}", "{\"schema_version\":2,\"state\":{\"localRoads\":null}}", "{\"roads\":true,\"hill_amount\":1}" })
                Equal(0, MapStateCodec.Deserialize(json).localRoads.Count);
            Equal(true, MapStateCodec.Deserialize("{\"roads\":true}").hasRoads);
            Throws(() => MapStateCodec.Deserialize("{\"schema_version\":2,\"state\":{\"localRoads\":[null]}}"));
            var malformed = Edit(new TileMapState(), Add); malformed.localRoads[0].points[0][0] = -1;
            Throws(() => MapStateCodec.Deserialize(MapStateCodec.Serialize(malformed)));
        });
        Check("Invalid road changes fail atomically after earlier valid terrain or road operations", () =>
        {
            var before = Edit(new TileMapState(), Add); string saved = MapStateCodec.Serialize(before);
            foreach (var command in new[] {
                "{\"animal_density\":1.7,\"road_ops\":[{\"op\":\"update\",\"id\":\"secret_road\",\"changes\":{\"kind\":\"StoneRoad\"}},{\"op\":\"remove\",\"id\":\"missing\"}]}",
                Add,
                Update("\"points\":[[0,0.5],[2,0.5]]"),
                "{\"road_ops\":[{\"op\":\"remove\",\"id\":\"secret_road\"},{\"op\":\"update\",\"id\":\"secret_road\",\"changes\":{\"kind\":\"DirtPath\"}}]}"
            }) { Throws(() => Edit(before, command)); Equal(saved, MapStateCodec.Serialize(before)); }
        });
        Check("Road input rejects malformed coordinates nulls unsupported fields and operation shapes", () =>
        {
            var before = Edit(new TileMapState(), Add);
            foreach (var fields in new[] {
                "\"kind\":\"Bridge\"", "\"route\":\"tunnel\"", "\"id\":\"changed\"", "\"kind\":null", "\"width\":5", "\"terrain\":\"Soil\"", "\"seed\":2.5", "\"seed\":true", "\"seed\":\"3\"",
                "\"points\":null", "\"points\":{}", "\"points\":[]", "\"points\":[[0,0]]", "\"points\":[[0,0],[0,0]]", "\"points\":[null,[1,1]]", "\"points\":[[0,0,0],[1,1]]", "\"points\":[[true,0],[1,1]]", "\"points\":[[\"0\",0],[1,1]]", "\"points\":[[0,0],[1,2]]"
            }) Throws(() => Edit(before, Update(fields)));
            foreach (var payload in new[] { "null", "{}", "[null]", "[1]", "[{\"op\":\"clear\"}]", "[{\"op\":\"remove\",\"id\":\"secret_road\",\"changes\":{}}]", "[{\"op\":\"add\",\"road\":{\"id\":\"bad id\",\"points\":[[0,0],[1,1]]}}]" })
                Throws(() => Edit(before, "{\"road_ops\":" + payload + "}"));
            var boolId = Edit(new TileMapState(), Add.Replace("secret_road", "true"));
            Throws(() => Edit(boolId, "{\"road_ops\":[{\"op\":\"remove\",\"id\":true}]}"));
            Throws(() => Edit(boolId, "{\"road_ops\":[{\"op\":\"update\",\"id\":true,\"changes\":{\"kind\":\"DirtPath\"}}]}"));
        });
        Check("Road capacity and waypoint limits reject without partial state", () =>
        {
            var state = new TileMapState();
            for (int i = 0; i < 8; i++) state = Edit(state, Add.Replace("secret_road", "road_" + i));
            Equal(8, state.localRoads.Count); Throws(() => Edit(state, Add)); Equal(8, state.localRoads.Count);
            string manyPoints = string.Join(",", Enumerable.Range(0, 17).Select(i => "[" + (i % 2) + ",0.5]"));
            Throws(() => Edit(Edit(new TileMapState(), Add), Update("\"points\":[" + manyPoints + "]")));
            string manyOps = string.Join(",", Enumerable.Range(0, 17).Select(i => "{\"op\":\"remove\",\"id\":\"road_" + i + "\"}"));
            Throws(() => Parse("{\"road_ops\":[" + manyOps + "]}"));
        });
        Check("Road summaries show translated types positions and operations without internal identifiers", () =>
        {
            var before = new TileMapState(); var after = Edit(before, Add);
            foreach (bool korean in new[] { true, false })
            {
                string added = new MapPlanDescription(korean).Describe(before, after);
                foreach (var word in korean ? new[] { "고대 아스팔트 도로", "서쪽", "동쪽", "장애물 우회", "중간 경유지 1", "추가" } : new[] { "Ancient asphalt road", "west", "east", "route around obstacles", "intermediate waypoints: 1", "added" }) Equal(true, added.Contains(word));
                var changed = Edit(after, Update("\"kind\":\"StoneRoad\",\"route\":\"direct\""));
                string changes = new MapPlanDescription(korean).Describe(after, changed);
                Equal(true, changes.Contains(korean ? "돌길" : "Stone road")); Equal(true, changes.Contains(korean ? "경로 조정" : "route adjusted"));
                string removed = MapStateDescription.Describe(after, before, korean);
                Equal(true, removed.Contains(korean ? "제거" : "removed"));
                foreach (var text in new[] { added, changes, removed, MapStateDescription.Describe(before, after, korean) })
                    foreach (var internalId in new[] { "secret_road", "AncientAsphaltRoad", "StoneRoad", "localRoads" }) Equal(false, text.Contains(internalId));
            }
            foreach (var kind in RoadPlans.Kinds) { Equal(false, RoadPlans.Label(kind, true) == kind); Equal(false, RoadPlans.Label(kind, false) == kind); }
        });
        Check("Road recommendations refine one stored candidate and keep other candidates and base state", () =>
        {
            var before = new TileMapState(); string saved = MapStateCodec.Serialize(before);
            var payload = SimpleJson.Parse("{\"options\":[{\"params\":" + Add + "},{\"params\":" + Add.Replace("AncientAsphaltRoad", "DirtPath") + "}]}");
            var plans = RecommendationPlan.Validate(payload, before, data => { }, true);
            var first = plans[0].Resolve(before); string second = MapStateCodec.Serialize(plans[1].Resolve(before));
            var revised = RecommendationPlan.Refine(plans, 1, SimpleJson.Parse(Update("\"route\":\"direct\"")), before, edits => { }, true);
            Equal("direct", revised.Resolve(before).localRoads[0].route); Equal("avoid", first.localRoads[0].route);
            Equal(second, MapStateCodec.Serialize(plans[1].Resolve(before))); Equal(saved, MapStateCodec.Serialize(before));
            Equal(true, RecommendationPlan.PendingInstruction(plans, before).Contains("secret_road"));
        });
        Check("Road application and snapshot undo stay on the selected local map and preserve world roads", () =>
        {
            MapGenParams.Reset(); Find.World = new World(); Find.WorldGrid = new WorldGrid();
            var tile = new SurfaceTile(); var other = new SurfaceTile(); var originalRoad = new object();
            tile.Roads.Add(originalRoad); Find.WorldGrid.Tiles[1] = tile; Find.WorldGrid.Tiles[2] = other;
            try
            {
                MapGenParams.ApplyPatch(Parse(Add), 1); var before = MapGenParams.CaptureState(1);
                Equal(1, before.localRoads.Count); Equal(0, MapGenParams.CaptureState(2).localRoads.Count);
                Equal(originalRoad, tile.Roads.Single()); Equal(0, other.Roads.Count);
                Equal(true, MapGenParams.BuildCurrentParamsText(true).Contains("local_roads"));
                MapGenParams.ApplyPatch(Parse(Update("\"kind\":\"StoneRoad\"")), 1);
                MapGenParams.RestoreSnapshot(before, 1);
                Equal(MapStateCodec.Serialize(before), MapStateCodec.Serialize(MapGenParams.CaptureState(1)));
                Equal(originalRoad, tile.Roads.Single());
                MapGenParams.ClearTile(1); Equal(0, MapGenParams.CaptureState(1).localRoads.Count); Equal(originalRoad, tile.Roads.Single());
            }
            finally { MapGenParams.Reset(); Find.World = null; Find.WorldGrid = null; Find.WorldSelector = null; }
        });
    }
}
