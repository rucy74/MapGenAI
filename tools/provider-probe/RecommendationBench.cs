using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class RecommendationBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var results=new List<object>();bool ok=true;
        foreach(string id in new[]{"recommend-plain","recommend-hot","oasis-plain","oasis-hot"})
        {
            bool recommendation=id.StartsWith("recommend"),hot=id.EndsWith("hot");string key=hot?"hot":"plain";
            string prompt=File.ReadAllText(Path.Combine(native,key+"-prompt.txt"));
            var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,key+"-before.json")));
            var history=new List<ChatMessage>();
            if(!recommendation)
            {
                history.Add(new ChatMessage("user","오아시스 만들어 줘"));
                history.Add(new ChatMessage("assistant","현재 바이옴에서는 오아시스 특징을 직접 추가할 수 없습니다. 기존 지형 특징을 유지하며 물과 주변 토양을 편집한 오아시스 같은 곳을 만들 수 있습니다."));
            }
            history.Add(new ChatMessage("user",recommendation?(hot?"온천은 유지하고, 그냥 추천해 봐":"그냥 추천해 봐"):"알아서 해 줘. 오아시스같은 거 추가해줘"));
            File.WriteAllText(Path.Combine(output,id+"-history.json"),System.Text.Json.JsonSerializer.Serialize(history));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            int calls=0;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string reply=await StructuredChat.SendAsync(async(bad,token)=>{
                    var attempt=new List<ChatMessage>(history);
                    if(bad!=null){File.WriteAllText(Path.Combine(output,id+"-malformed.txt"),bad);attempt.Add(new ChatMessage("assistant",bad));attempt.Add(new ChatMessage("user",StructuredChat.RepairInstruction));}
                    calls++;return await client.SendChatAsync(attempt,prompt,token);
                },timeout.Token,recommendation);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),reply);var cmd=ProviderResponse.Command(reply);
                bool intended,preserved;
                if(recommendation)
                {
                    var plans=RecommendationPlan.Validate(cmd,before,data=>{},true);intended=plans.Count>0;
                    preserved=plans.All(p=>before.mutators.All(m=>MapStateEditor.Merge(before,MapParameterParser.Parse(ProviderResponse.Command(p.Command).GetObject("params"))).mutators.Contains(m)));
                }
                else
                {
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));
                    var fills=after.elevationShapes.SelectMany(s=>new[]{s.fill}.Concat(s.compositeOps?.Select(c=>c.fill)??Enumerable.Empty<string>())).Where(x=>x!=null).ToList();
                    intended=cmd.GetString("action")=="generate" && fills.Any(f=>f=="water"||f=="WaterShallow") && fills.Any(f=>f=="SoilRich"||f=="rich_soil") && !after.mutators.Contains("Pond") && !after.mutators.Contains("Oasis");
                    preserved=before.mutators.SequenceEqual(after.mutators) && before.fertilityOffset==after.fertilityOffset && before.vegetationDensity==after.vegetationDensity;
                    File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                }
                ok &= intended && preserved;results.Add(new{id,intended,preserved,calls,modelVersion=client.LastModelVersion});Console.WriteLine(id+": intended="+intended+" preserved="+preserved+" calls="+calls);
            }
            catch(Exception error){ok=false;results.Add(new{id,error=error.Message,calls});Console.WriteLine(id+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok,results,scope="Fresh Korean Gemini responses. Native recommendation preflight/selection and oasis full maps are checked separately."}));
        }
        return ok?0:1;
    }
}
