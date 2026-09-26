using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

// Recorded native temperate/flat context, refreshed production rule blocks, real first replies.
// Geometry, native placement, and visual appeal remain separate from this intent check.
static class CompositionBench
{
    public static async Task<int> Run(GeminiClient client,string capture,string output,string mode)
    {
        string template=File.ReadAllText(capture);
        int natural=template.IndexOf("\n자연스러운 육상 지형",StringComparison.Ordinal);
        int road=template.IndexOf("\n정착 맵의 도로:",natural,StringComparison.Ordinal);
        if(natural<0 || road<natural)throw new InvalidOperationException("Captured native rule boundaries not found");
        template=template.Substring(0,natural)+LandformPrompt.Rules(true)+RoadPrompt.Rules(true)+RecommendationPlan.Rules;
        int start=template.IndexOf("\n## 현재 적용된 파라미터",StringComparison.Ordinal);
        int end=start<0?-1:template.IndexOf("\n규칙:",start,StringComparison.Ordinal);
        if(start<0 || end<0)throw new InvalidOperationException("Native state section not found");
        string[] requests=mode=="refine"?new[]{"이 추천들은 마음에 안 들어. 현재 맵에 맞게 다른 선택지로 다시 추천해 줘."}:
            mode=="dry"?new[]{"Make a natural open basin with a wide settlement plain. Keep it dry, with no ponds or other additions."}:
            new[]{"자연스러운 열린 분지 만들어 줘. 정착할 평지는 넓게.",
                "자동 바닥·식생 조화 효과만 꺼 줘. 산과 연못 모양은 유지해.",
                "다시 주변 바닥과 식생을 자연스럽게 조화시켜 줘.",
                "이 추천들은 마음에 안 들어. 현재 맵에 맞게 다른 선택지로 다시 추천해 줘."};
        var state=new TileMapState();var history=new List<ChatMessage>();var results=new List<object>();
        if(mode=="refine")state=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(Path.GetDirectoryName(output),"provider-ko","composition-03-after.json")));
        int calls=0;bool all=true;
        foreach(string request in requests)
        {
            string id="composition-"+(calls+1).ToString("00"),current;
            if(File.Exists(Path.Combine(output,id+"-response.json")))throw new InvalidOperationException("Fresh output required");
            using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(true);
            string prompt=template.Substring(0,start)+"\n"+current+template.Substring(end)+ConversationMemory.Rules;
            history.Add(new ChatMessage("user",request));
            Save(id+"-before.json",MapStateCodec.Serialize(state));Save(id+"-prompt.txt",prompt);
            Save(id+"-request.txt",request);Save(id+"-history.json",System.Text.Json.JsonSerializer.Serialize(history));
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));calls++;
                string reply=await client.SendChatAsync(history,prompt,timeout.Token);Save(id+"-response.json",reply);
                var cmd=ProviderResponse.Command(reply);bool intended;
                if(calls==4 || mode=="refine")
                {
                    var plans=RecommendationPlan.Validate(cmd,state,data=>{},true);
                    intended=cmd.GetString("action")=="recommend" && plans.Count==3;
                    int n=0;
                    foreach(var plan in plans)
                    {
                        var after=plan.Resolve(state);MapStateValidation.Validate(after);
                        intended &= SameGlobalSettings(state,after) && state.elevationShapes.All(s=>after.elevationShapes.Any(t=>t.id==s.id));
                        intended &= !after.elevationShapes.Any(s=>s.type!="passage" && HasSoilFill(s) && !state.elevationShapes.Any(old=>old.id==s.id && HasSoilFill(old)));
                        Save(id+"-option-"+(++n)+"-state.json",MapStateCodec.Serialize(after));
                    }
                }
                else
                {
                    if(cmd.GetString("action")!="generate")throw new InvalidOperationException("Expected a generated plan");
                    var before=state;state=MapStateEditor.Merge(state,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(state);
                    var basin=state.elevationShapes.SingleOrDefault(s=>s.type=="landform" && s.landform=="open_basin");
                    intended=basin!=null && basin.layout=="organic" && SameGlobalSettings(before,state);
                    if(calls==1)
                    {
                        bool water=state.elevationShapes.Any(HasWater);
                        intended &= basin?.details=="natural" && !state.elevationShapes.Any(HasSoilFill) && (mode=="dry"?!water:water);
                    }
                    else
                    {
                        var expected=before.Clone();
                        foreach(var shape in expected.elevationShapes.Where(s=>s.type=="landform"))shape.details=calls==2?"none":"natural";
                        intended &= MapStateCodec.Serialize(expected)==MapStateCodec.Serialize(state);
                    }
                    Save(id+"-after.json",MapStateCodec.Serialize(state));
                }
                all &= intended;
                results.Add(new{id,request,action=cmd.GetString("action"),intended,model=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens,thinkingTokens=client.LastThinkingTokens});
                history.Add(new ChatMessage("assistant",(calls==4 || mode=="refine"?"PENDING RECOMMENDATIONS\n":"APPLIED SETTINGS\n")+reply));
                Console.WriteLine(id+": intended="+intended);
                Summary();if(!intended)return 1;
            }
            catch(Exception error)
            {
                all=false;results.Add(new{id,intended=false,errorType=error.GetType().Name});Summary();
                Console.WriteLine(id+": "+error.GetType().Name+" (request/configuration omitted)");return 1;
            }
        }
        return all?0:1;
        void Save(string name,string text)=>File.WriteAllText(Path.Combine(output,name),text);
        void Summary()=>Save("result.json",System.Text.Json.JsonSerializer.Serialize(new{ok=all,calls,results,
            scope="Captured native flat temperate context with current production landform/road/recommendation rules. Real first replies, no repair. Intent/schema only, not exact user grassland or native map aesthetics."}));
    }

    static bool HasWater(ElevationShape s)=>Water(s.fill) || s.compositeOps!=null && s.compositeOps.Any(o=>Water(o.fill));
    static bool HasSoilFill(ElevationShape s)=>Soil(s.fill) || s.compositeOps!=null && s.compositeOps.Any(o=>Soil(o.fill));
    static bool Water(string fill)=>fill!=null && (fill.StartsWith("Water",StringComparison.OrdinalIgnoreCase) || fill=="water");
    static bool Soil(string fill)=>fill!=null && (fill.StartsWith("Soil",StringComparison.OrdinalIgnoreCase) || fill=="soil" || fill=="rich_soil");
    static bool SameGlobalSettings(TileMapState a,TileMapState b)
    {
        var copy=b.Clone();copy.elevationShapes=a.Clone().elevationShapes;copy.structures=a.Clone().structures;
        copy.localRoads=a.Clone().localRoads;
        return MapStateCodec.Serialize(a)==MapStateCodec.Serialize(copy);
    }
}
