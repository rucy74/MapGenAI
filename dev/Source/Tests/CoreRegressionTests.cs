using System;
using MapGenAI.UI;
using MapGenAI.LLM;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

static class CoreRegressionTests
{
    static int passed, failed;
    // Each assertion protects externally observable behavior, not implementation text.
    public static void RunAll()
    {
        TextRegionTests.SeedMaterials();
        Check("Unicode escapes decode", () => Equal("한글", SimpleJson.Parse("{\"message\":\"\\uD55C\\uAE00\"}").GetString("message")));
        Check("Truncated root rejected", () => Throws(() => SimpleJson.Parse("{\"action\":\"generate\",\"params\":{\"hill_amount\":1.2}")));
        Check("Empty object array preserved", () => Equal(0, SimpleJson.Parse("{\"elevation_shapes\":[]}").GetObjectArray("elevation_shapes")?.Count ?? -1));
        Check("Duplicate keys rejected", () => Throws(() => SimpleJson.Parse("{\"caves\":true,\"caves\":false}")));
        Check("Malformed array terminates", () => Throws(() => SimpleJson.Parse("{\"action\":\"generate\",\"params\":{\"elevation_shapes\":[}")));
        Check("JSON grammar boundaries", () =>
        {
            foreach(var value in new[] { "{", "{\"x\":[}", "{\"x\":01}", "{\"x\":1.}", "{\"x\":1e}", "{\"x\":NaN}", "{\"x\":Infinity}", "{\"x\":1e999}", "{\"x\":true,}", "{\"x\":[1,]}", "{x:1}", "{}{}", "{\"x\":\"\\uD800\"}", "{\"x\":\"\\q\"}", "{\"x\":\"raw\nline\"}" }) Throws(() => SimpleJson.Parse(value));
            Throws(() => SimpleJson.Parse("{\"x\":" + new string('[',80) + "0" + new string(']',80) + "}"));
            Equal(true,SimpleJson.Parse("{\"x\":null}").IsNull("x"));
            Equal(false,SimpleJson.Parse("{}").ContainsKey("x"));
        });
        Check("Escaped text roundtrip", () =>
        {
            string text="한글 😀\n\r\t\b\f\\\"";
            var encoded=SimpleJson.Serialize(new Dictionary<string,object> { {"text",text} });
            Equal(text,SimpleJson.Parse(encoded).GetString("text"));
            using(var standard=JsonDocument.Parse(encoded)) Equal(text,standard.RootElement.GetProperty("text").GetString());
        });
        Check("Gemini envelope preserves escaped Korean", () => Equal("한글",ProviderResponse.Gemini("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"\\uD55C\\uAE00\"}]}}]}")));
        Check("Provider incomplete output rejected", () =>
        {
            Throws(() => ProviderResponse.Gemini("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"{}\"}]}}]}"));
            Throws(() => ProviderResponse.OpenAI("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}"));
        });
        Check("OpenAI envelope ignores unrelated content", () => Equal("한글",ProviderResponse.OpenAI("{\"content\":\"wrong\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\\uD55C\\uAE00\"}}]}")));
        Check("Command fences preserve completeness", () =>
        {
            Equal("ask",ProviderResponse.Command("```json\n{\"action\":\"ask\"}\n```").GetString("action"));
            Throws(() => ProviderResponse.Command("done {\"action\":\"ask\"}"));
            Throws(() => ProviderResponse.Command("{\"params\":{}"));
        });
        Check("Reset rejects pending and late replies", () =>
        {
            var gate=new RequestGate(); var first=gate.Begin(); gate.Complete(first,"old",null); gate.Cancel();
            Equal(true,gate.Take()==null); gate.Complete(first,"late",null); Equal(true,gate.Take()==null);
            var second=gate.Begin(); gate.Complete(first,"stale",null); gate.Complete(second,"new",null);
            Equal("new",gate.Take().Text); Equal(true,gate.Take()==null); gate.Cancel();
        });
        Check("Generated parser corpus terminates and accepts only valid JSON", () =>
        {
            var random=new Random(719); var clock=Stopwatch.StartNew();
            const string chars="{}[],:\"\\012true falsenull\n\t";
            for(int i=0;i<3000;i++)
            {
                var body=new char[random.Next(1,60)];
                for(int j=0;j<body.Length;j++) body[j]=chars[random.Next(chars.Length)];
                string input="{\"x\":"+new string(body)+"}";
                bool accepted=true; try { SimpleJson.Parse(input); } catch(FormatException) { accepted=false; }
                if(accepted) { using(var standard=JsonDocument.Parse(input)) { } }
            }
            if(clock.Elapsed.TotalSeconds>5) throw new Exception("Parser corpus exceeded time bound");
        });
        MdpApplyTests.RunAll();
        ShapeEditTests.RunAll();
        NaturalShapeTests.RunAll();
        NaturalLandformTests.RunAll();
        WorldStateTests.RunAll();
        ImageMapTests.RunAll();
        TextRegionTests.RunAll();
        CompoundPlanTests.RunAll();
        RecordedLandformTests.RunAll();
        ManualFailureTests.RunAll();
        StructuredChatTests.RunAll();
        ProviderBudgetTests.RunAll();
        ConversationMemoryTests.RunAll();
        EditIntentGuardTests.RunAll();
        MapPlanDescriptionTests.RunAll();
        RecommendationControlsTests.RunAll();
        RecommendationGuideTests.RunAll();
        RecommendationFeedbackTests.RunAll();
        RegionCoverageTests.RunAll();
        PassageTests.RunAll();
        SpatialRelationTests.RunAll();
        AncientPlanTests.RunAll();
        RoadPlanTests.RunAll();
        RoadRoutingTests.RunAll();
        RoadBridgeRoutingTests.RunAll();
        Console.WriteLine($"CoreRegressionTests: {passed} PASS / {failed} FAIL");
        if (failed > 0) throw new Exception($"{failed} regression tests failed");
    }
    public static void Check(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    public static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
    public static void Throws(Action action)
    {
        try { action(); } catch (FormatException) { return; }
        throw new Exception("Expected FormatException");
    }
}
