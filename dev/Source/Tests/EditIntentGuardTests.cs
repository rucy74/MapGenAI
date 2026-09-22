using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class EditIntentGuardTests
{
    const string Roads = "{\"action\":\"generate\",\"params\":{\"road_ops\":[{\"op\":\"add\",\"road\":{\"id\":\"west_east\",\"kind\":\"DirtPath\",\"seed\":17,\"points\":[[0,0.4],[1,0.6]]}},{\"op\":\"add\",\"road\":{\"id\":\"south_north\",\"kind\":\"DirtPath\",\"seed\":23,\"points\":[[0.4,0],[0.6,1]]}}]}}";
    const string Mountain = "{\"action\":\"generate\",\"params\":{\"hills\":\"left\"}}";
    const string Shape = "{\"action\":\"generate\",\"params\":{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"wrong_passage\",\"type\":\"passage\",\"points\":[[0,0.5],[1,0.5]],\"width\":8,\"fill\":\"Soil\"}}]}}";
    const string Center = "{\"action\":\"generate\",\"params\":{\"road_ops\":[{\"op\":\"update\",\"id\":\"west_east\",\"changes\":{\"route\":\"direct\",\"points\":[[0,0.5],[1,0.5]]}},{\"op\":\"update\",\"id\":\"south_north\",\"changes\":{\"route\":\"direct\",\"points\":[[0.5,0],[0.5,1]]}}]}}";
    static ChatMessage Receipt(string command) => new ChatMessage("assistant", "APPLIED\n" + command + "\nActual changes: settings accepted; generation still needs validation.");
    static List<ChatMessage> Follow(string text) => new List<ChatMessage> { new ChatMessage("user", "십자 모양 흙길을 만들어 줘"), Receipt(Roads), new ChatMessage("user", text) };
    static List<ChatMessage> Initial(string text) => new List<ChatMessage> { new ChatMessage("user", text) };
    static void Reject(List<ChatMessage> history, string command) => Equal(true, !string.IsNullOrEmpty(EditIntentGuard.Rejection(history, command)));
    static void Accept(List<ChatMessage> history, string command) => Equal<string>(null, EditIntentGuard.Rejection(history, command));
    static TileMapState Apply(TileMapState before, string command) => MapStateEditor.Merge(before, MapParameterParser.Parse(ProviderResponse.Command(command).GetObject("params")));

    public static void RunAll()
    {
        Check("Explicit Korean and English road edits cannot become terrain fills or mountains", () =>
        {
            foreach (var text in new[] { "십자 모양 흙길을 만들어 줘", "가운데에 아스팔트 도로 추가", "Create a centered cross of dirt paths", "Add a road across the river" })
            {
                Reject(Initial(text), Shape); Reject(Initial(text), Mountain);
                Accept(Initial(text), Roads);
            }
        });
        Check("Confirmed road followup stays on roads without repeating the noun", () =>
        {
            foreach (var text in new[] { "아니 중앙에서 정확히 십자로 교차하게 해줘", "가운데로 옮겨 줘", "좀 자연스럽게 바꿔 줘", "Make it a straight centered cross", "Move it to the center", "Make it more natural" })
            {
                Reject(Follow(text), Shape); Accept(Follow(text), Center);
            }
            Reject(Follow("중앙으로 바꿔 줘"), "{\"action\":\"generate\",\"params\":{\"road_ops\":[]}}");
        });
        Check("New terrain topics and explicit road plus terrain requests remain supported", () =>
        {
            foreach (var text in new[] { "산을 추가해 줘", "호수를 더 자연스럽게", "Add a mountain", "Make the lake more natural", "흙길 말고 산", "도로는 유지하고 비옥한 토양만", "산을 추가하고 도로를 돌길로 바꿔", "도로와 호수를 만들어 줘", "Add roads and a lake", "Keep the road and add a mountain", "Create a road and remove the mountain" })
                Accept(Follow(text), Mountain);
        });
        Check("Pending proposals and clarification responses never establish an applied road target", () =>
        {
            foreach (var pending in new[] { Roads, "{\"action\":\"recommend\",\"options\":[{\"params\":{\"road_ops\":[]}}]}", "{\"action\":\"revise\",\"option\":1,\"params\":{\"road_ops\":[]}}", "NOT APPLIED\nNo supported road route" })
                Accept(new List<ChatMessage> { new ChatMessage("user", "Add roads"), new ChatMessage("assistant", pending), new ChatMessage("user", "Move it to the center") }, Shape);
            Accept(Follow("도로를 수정해 줘"), "{\"action\":\"ask\",\"message\":\"Which road?\"}");
            Accept(Follow("3번 도로를 중앙으로 옮겨 줘"), Center.Replace("\"generate\"", "\"revise\""));
        });
        Check("Undo preset and reset barriers stop stale target inference", () =>
        {
            foreach (var operation in new[] { "Undo restored old state", "Preset loaded", "Reset to initial state" })
            {
                var history = Follow("Move it to the center");
                history.Insert(2, new ChatMessage("assistant", "STATE REPLACED\n" + operation));
                Accept(history, Mountain);
            }
        });
        Check("Rejected proposal cannot supersede a confirmed target or invent an applied one", () =>
        {
            var history = Follow("Make it straight"); history.RemoveAt(2);
            history.Add(new ChatMessage("user", "Move the road"));
            history.Add(new ChatMessage("assistant", "NOT APPLIED\nProposed terrain changes were rejected."));
            history.Add(new ChatMessage("user", "Move it to the center"));
            Reject(history, Shape);
            history[2] = new ChatMessage("user", "Add a mountain");
            Accept(history, Shape); // A new unsuccessful topic makes 'it' ambiguous.
        });
        Check("Mixed prior edits removed roads and unqualified requests are not guessed as roads", () =>
        {
            var mixed = Roads.Replace("\"road_ops\":", "\"animal_density\":1.2,\"road_ops\":");
            var removed = "{\"action\":\"generate\",\"params\":{\"road_ops\":[{\"op\":\"remove\",\"id\":\"west_east\"}]}}";
            foreach (var prior in new[] { mixed, removed, Mountain })
            {
                var history = Follow("Make it a centered cross"); history[1] = Receipt(prior);
                Accept(history, Shape);
            }
            Accept(Follow("추천해 줘"), Mountain);
            Accept(Follow("알아서 해 줘"), Shape);
            Accept(Initial("Make it a cross"), Shape);
        });
        Check("Road-only correction cannot smuggle unrelated edits alongside valid road operations", () =>
        {
            var mixed = Center.Replace("\"road_ops\":", "\"hills\":\"left\",\"road_ops\":");
            Reject(Follow("가운데에서 십자 모양으로 교차하게 해줘"), mixed);
            Accept(Follow("도로는 중앙으로 옮기고 산을 추가해 줘"), mixed);
            Accept(Follow("Center the roads and add mountains"), mixed);
        });
        Check("Refined road-only selection retains its target while mixed plans stay ambiguous",()=>
        {
            var history=Follow("좀 더 구불구불하게 바꿔 줘");
            history[1]=Receipt("{\"action\":\"applied_plan\",\"commands\":["+Roads+","+Center+"]}");
            Reject(history,Mountain);Accept(history,Center);
            history[1]=Receipt("{\"action\":\"applied_plan\",\"commands\":["+Roads+","+Mountain+"]}");
            Accept(history,Mountain);
            history[1]=Receipt("{\"action\":\"applied_plan\",\"commands\":[]}");Accept(history,Mountain);
        });
        Check("Guard is read-only and malformed envelopes remain owned by structured validation", () =>
        {
            var history = Follow("중앙에서 십자로 교차하게 해줘");
            var snapshot = SimpleJson.Serialize(history); Reject(history, Shape);
            Equal(snapshot, SimpleJson.Serialize(history));
            Accept(history, "not JSON"); Accept(null, Mountain);
            Accept(new List<ChatMessage>(), Mountain);
        });
        Check("Centered cross corrects existing road IDs while preserving unrelated map state", () =>
        {
            var before = Apply(new TileMapState(), Roads);
            before.animalDensity = 1.3f; before.mutators.Add("ExistingFeature");
            string snapshot = MapStateCodec.Serialize(before);
            Accept(Follow("중앙으로 십자 모양을 정확히 맞춰 줘"), Center);
            var after = Apply(before, Center);
            Equal(snapshot, MapStateCodec.Serialize(before));
            Equal("localRoads", string.Join(",", MapStateCodec.ChangedFields(before, after)));
            Equal(2, after.localRoads.Count);
            Equal("west_east,south_north", string.Join(",", after.localRoads.Select(r => r.id)));
            Equal("17,23", string.Join(",", after.localRoads.Select(r => r.seed)));
            Equal(true, after.localRoads.All(r => r.kind == "DirtPath" && r.route == "direct"));
            Equal(snapshot, MapStateCodec.Serialize(MapStateCodec.Deserialize(snapshot)));
        });
        Check("Exact cross centerlines stay centered through bridgeable water on even and odd maps", () =>
        {
            var roads = Apply(Apply(new TileMapState(), Roads), Center).localRoads;
            foreach (int size in new[] { 250, 251 })
            {
                int center = 125;
                var water = Enumerable.Range(0, size * size).Select(i => Math.Abs(i % size + i / size - 2 * center) <= 8).ToArray();
                var ground = water.Select(w => !w).ToArray();
                var a = RoadRouting.PlanWithBridges(size, size, ground, water, roads[0].points, roads[0].route, 1.5f);
                var b = RoadRouting.PlanWithBridges(size, size, ground, water, roads[1].points, roads[1].route, 1.5f);
                Equal(size, a.Count); Equal(size, b.Count);
                Equal(true, a.All(i => i / size == center)); Equal(true, b.All(i => i % size == center));
                Equal(center + center * size, a.Intersect(b).Single());
                Equal(true, a.Any(i => water[i]) && b.Any(i => water[i]));
                Equal(true, a.Zip(a.Skip(1), (x, y) => y - x).All(d => d == 1));
                Equal(true, b.Zip(b.Skip(1), (x, y) => y - x).All(d => d == size));
            }
        });
    }
}
