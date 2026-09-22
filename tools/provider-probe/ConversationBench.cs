using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class ConversationBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output,bool ko)
    {
        var state=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,"initial-state.json")));
        string template=File.ReadAllText(Path.Combine(native,"production-system-prompt.txt"));
        int start=template.IndexOf(ko?"\n## 현재 적용된 파라미터":"\n## Current",StringComparison.Ordinal);
        int end=start<0?-1:template.IndexOf(ko?"\n규칙:":"\nRules:",start,StringComparison.Ordinal);
        if(start<0 || end<0)throw new InvalidOperationException("Native current-state section not found");
        string[] requests=ko?new[]{
            "맵의 북쪽에서 남쪽, 서쪽에서 동쪽으로 십자가 모양 흙길 두 개를 깔아줘. 맵 중앙에서 만나게 해 줘.",
            "아니 완전한 십자가 모양. 십자가가 만나는 점이 맵의 중심.",
            "이번엔 북동쪽에 작은 자연스러운 호수 하나 추가해 줘.",
            "아까 만들었던 십자가 흙길 두 개만 돌길로 바꿔줘."}
            :new[]{"Add two dirt paths in a cross from north to south and west to east, meeting in the map center.",
            "No, a perfect cross. They must meet at the exact center of the map.",
            "Now add a small natural lake in the northeast.",
            "Change only the two cross-shaped dirt paths from earlier to stone roads."};
        var history=new List<ChatMessage>();var results=new List<object>();var cases=new List<object>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var budget=await client.GetContextBudgetAsync(timeout.Token);
        File.WriteAllText(Path.Combine(output,"budget.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"inputTokens",budget.InputTokens},{"known",budget.Known},{"source",budget.Source}}));
        for(int i=0;i<requests.Length;i++)
        {
            string id="memory-"+(ko?"ko":"en")+"-"+(i+1);string current;
            using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(ko);
            string prompt=template.Substring(0,start)+"\n"+current+template.Substring(end);
            if(!prompt.Contains(ConversationMemory.Rules))prompt+=ConversationMemory.Rules;
            history.Add(new ChatMessage("user",requests[i]));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(state));
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            File.WriteAllText(Path.Combine(output,id+"-history.json"),System.Text.Json.JsonSerializer.Serialize(history));
            try
            {
                int calls=0;
                string reply=await StructuredChat.SendAsync((bad,ct)=>
                {
                    var attempt=ConversationMemory.Copy(history);
                    if(bad!=null){attempt.Add(new ChatMessage("assistant",bad));attempt.Add(new ChatMessage("user",StructuredChat.RepairInstruction));}
                    calls++;return client.SendChatAsync(attempt,prompt,ct);
                },timeout.Token,false,r=>EditIntentGuard.Rejection(history,r));
                File.WriteAllText(Path.Combine(output,id+"-response.json"),reply);
                var command=ProviderResponse.Command(reply);
                if(command.GetString("action")!="generate")throw new Exception("Expected a generate edit");
                var before=state;state=MapStateEditor.Merge(state,MapParameterParser.Parse(command.GetObject("params")));MapStateValidation.Validate(state);
                bool roadsOnly=command.GetObject("params").Keys.All(k=>k=="road_ops");
                if(i==0 && (state.localRoads.Count!=2 || !roadsOnly))throw new Exception("Expected two local roads without terrain edits");
                if(i==1 && (!roadsOnly || state.localRoads.Count!=2 || state.localRoads.Any(r=>r.route!="direct") || !before.localRoads.Select(r=>r.id).SequenceEqual(state.localRoads.Select(r=>r.id))))
                    throw new Exception("Correction did not preserve two direct road IDs");
                if(i==1 && !HasCenteredCross(state))throw new Exception("Corrected roads do not follow both exact center axes");
                if(i==2 && state.elevationShapes.Count<=before.elevationShapes.Count)throw new Exception("New lake topic was lost");
                if(i==3 && (!roadsOnly || state.localRoads.Any(r=>r.kind!="StoneRoad") || state.elevationShapes.Count!=before.elevationShapes.Count))throw new Exception("Earlier roads or newer lake lost");
                history.Add(new ChatMessage("assistant","APPLIED\n"+reply.Replace("\r","").Replace("\n","")+"\nActual changes: accepted settings; map generation remains to be verified."));
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(state));
                results.Add(new{id,ok=true,calls,input=client.LastInputTokens,output=client.LastOutputTokens,thinking=client.LastThinkingTokens,model=client.LastModelVersion});
                cases.Add(new{id,kind="local-roads",beforeFile=id+"-before.json",request=requests[i],omitAmbientRuins=true,preview=true});
                Console.WriteLine(id+": state/intent checks passed; full map separate");
            }
            catch(Exception error){results.Add(new{id,ok=false,error=error.Message});Save(false);return 1;}
            Save(true);
        }
        try
        {
        // Exercise the real summary endpoint at an intentionally small TEST budget, not the user's model limit.
        var longHistory=new List<ChatMessage>{new ChatMessage("user",ko?"이번 설계 이름은 청솔이다. 북동쪽 호수는 유지하고, 십자가 도로는 돌길이어야 한다.":"Call this design Bluepine. Preserve the northeast lake and keep both cross roads as stone roads."),
            new ChatMessage("assistant","APPLIED\n{}\nPreferences noted; no new map change.")};
        for(int n=0;n<16;n++)
        {
            longHistory.Add(new ChatMessage("user",ko?"현재 지형을 설명해 줘. 맵은 바꾸지 마.":"Describe the current terrain without changing the map."));
            longHistory.Add(new ChatMessage("assistant",string.Join(" ",Enumerable.Repeat(ko?"북동쪽 호수와 중앙에서 만나는 돌길 두 개가 있습니다. 이것은 설명이며 변경 요청이 아닙니다.":"There is a northeast lake and two stone roads meeting at the center. This describes the current design and makes no changes.",12))));
        }
        longHistory.Add(new ChatMessage("user",ko?"내가 붙인 설계 이름과 유지해달라는 것들을 말해 줘. 맵은 바꾸지 마.":"What name did I give this design and what did I ask to preserve? Do not change the map."));
        string summarySystem="Map authoring conversation. Never change the map when asked to explain. Return JSON {\"action\":\"ask\",\"message\":\"answer\"}.";
        int measured=await client.CountInputTokensAsync(longHistory,summarySystem,timeout.Token)??ConversationMemory.Estimate(summarySystem,longHistory);
        int testBudget=Math.Max(4096,(int)(measured*1.1));
        var prepared=await ConversationMemory.PrepareAsync(longHistory,summarySystem,null,testBudget,client.SendChatAsync,client.CountInputTokensAsync,timeout.Token);
        if(prepared.Memory==null)throw new Exception("Test pressure did not produce a summary");
        File.WriteAllText(Path.Combine(output,"summary.json"),System.Text.Json.JsonSerializer.Serialize(new{testBudget,measured,prepared.InputTokens,prepared.SummaryCalls,summary=prepared.Memory.Summary,rawMessages=longHistory.Count,sentMessages=prepared.Messages.Count}));
        string answer=await client.SendChatAsync(prepared.Messages,summarySystem,timeout.Token);
        File.WriteAllText(Path.Combine(output,"after-summary-response.json"),answer);
        var msg=ProviderResponse.Command(answer);string content=msg.GetString("message")??"";
        bool ok=msg.GetString("action")=="ask" && content.IndexOf(ko?"청솔":"Bluepine",StringComparison.OrdinalIgnoreCase)>=0;
        results.Add(new{id="real-summary",ok,summaryCalls=prepared.SummaryCalls,before=measured,after=prepared.InputTokens,input=client.LastInputTokens,output=client.LastOutputTokens});Save(ok);
        return ok?0:1;
        }
        catch(Exception error){results.Add(new{id="real-summary",ok=false,error=error.Message});Save(false);return 1;}
        void Save(bool ok)
        {
            File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{ok,results}));
            File.WriteAllText(Path.Combine(output,"cases.json"),System.Text.Json.JsonSerializer.Serialize(new{cases}));
        }
    }
    static bool HasCenteredCross(TileMapState state)
    {
        bool horizontal=false,vertical=false;
        foreach(var road in state.localRoads)
        {
            if(road.points.Length<2)continue;
            horizontal|=road.points.All(p=>Math.Abs(p[1]-.5f)<.0001f) && road.points.Min(p=>p[0])==0 && road.points.Max(p=>p[0])==1;
            vertical|=road.points.All(p=>Math.Abs(p[0]-.5f)<.0001f) && road.points.Min(p=>p[1])==0 && road.points.Max(p=>p[1])==1;
        }
        return horizontal && vertical;
    }
}
