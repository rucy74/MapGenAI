using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

// Paired first-attempt interpretation checks; no repair, retry, fallback, or user save.
// Controlled histories/states are identical between variants (not divergent live sessions).
static class PromptCostBench
{
    sealed class Case
    {
        public string Id, Request, PriorRequest, PriorReply, Extra="";
        public bool Korean=true;
        public TileMapState Before=new TileMapState();
        public Action<SimpleJsonObject,TileMapState> Check;
    }
    const string Ring=@"{""elevation_shapes"":[{""id"":""donut"",""type"":""composite"",""shapes"":[{""id"":""outer"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.35},{""id"":""inner"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.20}],""compose"":[{""op"":""sub"",""a"":""inner"",""from"":""outer"",""out"":""rim""},{""op"":""add"",""s"":""rim"",""e"":0.8}]}]}";
    const string Fill=@"{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""inner_soil"",""type"":""region_fill"",""region"":""donut"",""region_part"":""enclosed"",""coverage"":0.7,""fill"":""SoilRich""}}]}";
    const string Roads=@"{""road_ops"":[{""op"":""add"",""road"":{""id"":""ns"",""kind"":""DirtPath"",""route"":""avoid"",""points"":[[0.5,0],[0.5,1]]}},{""op"":""add"",""road"":{""id"":""ew"",""kind"":""DirtPath"",""route"":""avoid"",""points"":[[0,0.5],[1,0.5]]}}]}";

    public static async Task<int> Run(GeminiClient client,string prompts,string output,int recommendationRepeats=0,bool compactEditsOnly=false)
    {
        var cases=Cases();var results=new List<object>();bool all=true;
        // A no-op response must fail the geometry/coverage/road/material checks.
        var detectorCases=cases.Where(c=>!c.Id.Contains("recommend") && c.Id!="candidate").ToList();
        foreach(var c in detectorCases)
        {
            bool rejected=false;
            try {c.Check(SimpleJson.Parse("{\"action\":\"generate\",\"params\":{}}"),c.Before.Clone());}
            catch {rejected=true;}
            if(!rejected)throw new Exception("No-op detector failed: "+c.Id);
        }
        File.WriteAllText(Path.Combine(output,"detector-controls.json"),System.Text.Json.JsonSerializer.Serialize(new{noOpRejected=true,cases=detectorCases.Count}));
        if(recommendationRepeats>0)
        {
            Need(recommendationRepeats<=3,"At most three recommendation samples per language");
            cases=Enumerable.Range(1,recommendationRepeats).SelectMany(n=>Cases().Where(c=>c.Id.StartsWith("recommend-")).Select(c=>{c.Id+="-"+n;return c;})).ToList();
        }
        if(compactEditsOnly)cases=cases.Where(c=>!c.Id.StartsWith("recommend-")).ToList();
        foreach(var c in cases)
        foreach(string variant in compactEditsOnly?new[]{"after"}:new[]{"before","after"})
        {
            string id=c.Id+"-"+variant;
            var template=File.ReadAllText(Path.Combine(prompts,(c.Korean?"ko":"en")+"-"+(c.Before.elevationShapes.Count>0?"edit":"system")+"-"+variant+".txt"));
            string current;using(GenerationContext.Enter(1,c.Before))current=MapGenParams.BuildCurrentParamsText(c.Korean);
            string prompt;
            if(template.Contains("MAPGENAI_CURRENT_STATE_PLACEHOLDER"))prompt=template.Replace("MAPGENAI_CURRENT_STATE_PLACEHOLDER",current);
            else
            {
                int a=template.IndexOf(c.Korean?"\n## 현재 적용된 파라미터":"\n## Current",StringComparison.Ordinal);
                int b=template.IndexOf(c.Korean?"\n규칙:":"\nRules:",Math.Max(a,0),StringComparison.Ordinal);
                if(b<0)throw new Exception("Missing captured rules section");
                // A pristine state is serialized as empty text by BuildCurrentParamsText.
                if(a<0)a=b;
                prompt=template.Substring(0,a)+"\n"+current+template.Substring(b);
            }
            prompt+=c.Extra+ConversationMemory.Rules;
            var history=new List<ChatMessage>();
            if(c.PriorRequest!=null)
            {
                history.Add(new ChatMessage("user",c.PriorRequest));
                history.Add(new ChatMessage("assistant","APPLIED\n"+c.PriorReply+"\nActual changes: accepted settings; map generation remains to be verified."));
            }
            history.Add(new ChatMessage("user",c.Request));
            File.WriteAllText(Path.Combine(output,id+"-request.json"),System.Text.Json.JsonSerializer.Serialize(new{history,prompt,before=MapStateCodec.Serialize(c.Before)}));
            string reply=null,error=null;bool ok=false,language=false;int input=0,tokensOut=0,thinking=0;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                reply=await client.SendChatAsync(history,prompt,timeout.Token);
                input=client.LastInputTokens;tokensOut=client.LastOutputTokens;thinking=client.LastThinkingTokens;
                File.WriteAllText(Path.Combine(output,id+"-response.json"),reply);
                var cmd=ProviderResponse.Command(reply);string action=cmd.GetString("action");
                string prose=(cmd.GetString("description")??"")+(cmd.GetString("message")??"");
                language=prose.Length==0 || (c.Korean?prose.Any(ch=>ch>='가' && ch<='힣'):!prose.Any(ch=>ch>='가' && ch<='힣'));
                var after=action=="generate"?MapStateEditor.Merge(c.Before,MapParameterParser.Parse(cmd.GetObject("params"))):c.Before.Clone();
                MapStateValidation.Validate(after);c.Check(cmd,after);
                Need(language,"Player-facing text language changed");ok=true;
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
            }
            catch(Exception e){error=e.Message;all=false;}
            results.Add(new{id,ok,language,error,inputTokens=input,outputTokens=tokensOut,thinkingTokens=thinking,model=client.LastModelVersion});
            File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,calls=results.Count,results,scope="One first attempt per paired case, controlled identical history/state; no retry. Parser/state/intent and text language only, not map appearance or full mod compatibility."}));
            Console.WriteLine(id+": "+(ok?"PASS":"FAIL "+error)+" input="+input+" output="+tokensOut);
            // Do not continue spending on authentication/quota/transport failure.
            if(reply==null)return 1;
        }
        return all?0:1;
    }

    static List<Case> Cases()
    {
        var ring=Patch(new TileMapState(),Ring);var filled=Patch(ring,Fill);
        var roads=Patch(new TileMapState(),Roads);
        var lake=Patch(new TileMapState(),@"{""elevation_shapes"":[{""id"":""west"",""type"":""ridge"",""direction"":""left"",""strength"":""strong""},{""id"":""lake"",""type"":""bump"",""position"":[0.75,0.25],""size"":""small"",""strength"":""negative_strong"",""fill"":""water""}]}" );
        var moat=ring.Clone();moat.elevationShapes[0].id="moat";moat.elevationShapes[0].fill="water";
        moat=Patch(moat,@"{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""island"",""type"":""bump"",""position"":[0.5,0.5],""size"":""small"",""fill"":""soil"",""strength"":0.05}}],""structure_ops"":[{""op"":""add"",""structure"":{""id"":""ruins"",""kind"":""ruin"",""region"":""island"",""width"":9,""height"":7,""count"":1}}]}" );
        var candidates=RecommendationPlan.Validate(SimpleJson.Parse(@"{""action"":""recommend"",""options"":[{""params"":{""hills"":""left""}},{""params"":{""hills"":""right""}},{""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""exit"",""type"":""passage"",""scope"":""mountains"",""points"":[[0.5,0.5],[0.5,0]],""width"":8,""fill"":""Soil""}}]}}]}"),new TileMapState(),d=>MapStateValidation.Validate(MapStateEditor.Merge(new TileMapState(),d)),false);
        var list=new List<Case>{
            new Case {Id="donut",Request="도넛 모양 산 만들어 줘.",Check=(c,a)=>{
                Generate(c);Need(a.elevationShapes.Count==1,"Expected one donut");var s=a.elevationShapes[0];
                Need(s.type=="composite" && s.compositeOps.Any(o=>o.op=="sub") && s.compositeOps.Any(o=>o.e>=.1f),"Expected closed mountain ring");
                var clean=a.Clone();clean.elevationShapes.Clear();Same(new TileMapState(),clean);
            }},
            new Case {Id="coverage70",Before=ring,Request="도넛 안에 70%만 비옥한 토양으로 채워줘.",PriorRequest="도넛 모양 산 만들어 줘.",PriorReply=Reply(Ring),Check=(c,a)=>{
                Generate(c);var f=a.elevationShapes.Single(s=>s.type=="region_fill");Need(f.region=="donut" && f.region_part=="enclosed" && Math.Abs(float.Parse(f.coverage,System.Globalization.CultureInfo.InvariantCulture)-.7f)<.001 && TerrainMaterials.DefName(f.fill)=="SoilRich","Expected exact enclosed 70% fill");
                var clean=a.Clone();clean.elevationShapes.RemoveAll(s=>s.type=="region_fill");Same(ring,clean);
            }},
            new Case {Id="coverage50",Before=filled,Request="아니 절반만 해 줘.",PriorRequest="도넛 안에 70%만 비옥한 토양으로 채워줘.",PriorReply=Reply(Fill),Check=(c,a)=>{Generate(c);var expected=filled.Clone();expected.elevationShapes.Single(s=>s.id=="inner_soil").coverage="0.5";Same(expected,a);}},
            new Case {Id="lava-ruins",Before=moat,Request="해자 물만 실제 용암으로 바꾸고 섬 안 폐허는 같은 크기로 3개로 해줘. 모양과 위치는 그대로.",Check=(c,a)=>{
                Generate(c);var expected=moat.Clone();expected.elevationShapes.Single(s=>s.id=="moat").fill="LavaDeep";expected.structures[0].count=3;
                var normalized=a.Clone();normalized.elevationShapes.Single(s=>s.id=="moat").fill=TerrainMaterials.DefName(normalized.elevationShapes.Single(s=>s.id=="moat").fill);Same(expected,normalized);
            }},
            new Case {Id="cross-ko",Before=roads,Request="아니 완전한 십자가 모양. 십자가가 만나는 점이 맵의 중심.",PriorRequest="북쪽에서 남쪽, 서쪽에서 동쪽으로 십자가 모양 흙길을 깔아줘.",PriorReply=Reply(Roads),Check=(c,a)=>Cross(c,a,roads)},
            new Case {Id="cross-en",Korean=false,Before=roads,Request="No, a perfect cross. The intersection must be the exact center of the map.",PriorRequest="Add dirt paths north-south and west-east in a cross.",PriorReply=Reply(Roads),Check=(c,a)=>Cross(c,a,roads)},
            new Case {Id="spring",Request="온천을 지형 특징으로 추가해 줘.",Check=(c,a)=>{Generate(c);Need(a.mutators.Contains("HotSprings"),"Missing native HotSprings");var clean=a.Clone();clean.mutators.Remove("HotSprings");Same(new TileMapState(),clean);}},
            new Case {Id="move-en",Korean=false,Before=lake,Request="Move only the bottom-right lake to [0.7,0.3]. Keep everything else.",Check=(c,a)=>{Generate(c);var expected=Patch(lake,@"{""shape_ops"":[{""op"":""move"",""id"":""lake"",""position"":[0.7,0.3]}]}");Same(expected,a);}},
            new Case {Id="inland-coast",Korean=false,Request="Add an ocean coast to this inland tile. If unsupported, explain without substituting terrain.",Check=(c,a)=>{Need(c.GetString("action")=="ask","Expected inland coast explanation");Same(new TileMapState(),a);}},
            new Case {Id="recommend-ko",Request="그냥 추천해 줘.",Check=(c,a)=>Recommend(c,true)},
            new Case {Id="recommend-en",Korean=false,Request="Just recommend a map.",Check=(c,a)=>Recommend(c,false)},
            new Case {Id="candidate",Korean=false,Request="Make option 3's straight passage edges more natural. Keep its width and route. Do not apply it yet.",Extra=RecommendationPlan.PendingInstruction(candidates,new TileMapState()),Check=(c,a)=>{
                Need(c.GetString("action")=="revise" && c.GetInt("option")==3,"Expected pending candidate revision");
                var plan=RecommendationPlan.Refine(candidates,3,c.GetObject("params"),new TileMapState(),edits=>{var s=new TileMapState();foreach(var d in edits)s=MapStateEditor.Merge(s,d);MapStateValidation.Validate(s);},false);
                var expected=candidates[2].Resolve(new TileMapState());expected.elevationShapes.Single().edge_roughness="medium";Same(expected,plan.Resolve(new TileMapState()));
            }}
        };
        return list;
    }
    static void Recommend(SimpleJsonObject c,bool ko)
    {
        Need(c.GetString("action")=="recommend","Expected recommendations");
        var plans=RecommendationPlan.Validate(c,new TileMapState(),d=>MapStateValidation.Validate(MapStateEditor.Merge(new TileMapState(),d)),ko);
        Need(plans.Count==3,"Expected three valid distinct candidates");
        foreach(var p in plans)
        {
            var s=p.Resolve(new TileMapState());Need(s.elevationShapes.Count>0,"Expected actual landscape geometry");
            Need(s.fertilityOffset==0 && s.vegetationDensity==1 && s.animalDensity==1 && !s.mutators.Contains("UndergroundCave"),"Unexpected bonuses/underground feature");
        }
    }
    static void Cross(SimpleJsonObject c,TileMapState a,TileMapState before)
    {
        Generate(c);Need(c.GetObject("params").Keys.All(k=>k=="road_ops"),"Cross correction changed terrain");
        var expected=before.Clone();foreach(var r in expected.localRoads)r.route="direct";Same(expected,a);
    }
    static TileMapState Patch(TileMapState s,string json)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static string Reply(string p)=>"{\"action\":\"generate\",\"params\":"+p+"}";
    static void Generate(SimpleJsonObject c)=>Need(c.GetString("action")=="generate","Expected generate");
    static void Same(TileMapState a,TileMapState b)=>Need(MapStateCodec.Serialize(a)==MapStateCodec.Serialize(b),"Unrequested state changed or requested patch missing");
    static void Need(bool ok,string error){if(!ok)throw new Exception(error);}
}
