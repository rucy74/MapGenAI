using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class FeatureFeedbackBench
{
    static readonly string[] Combo={"VEE_VolcanicRichSoil","VEE_FertileRains","HotSprings","VEE_SkygazingSpot","VEE_FrequentAuroras","SunnyMutator","VEE_PlantLife_Overgrown"};
    public static async Task<int> Run(GeminiClient client,string native,string output,string recordedProse)
    {
        bool vle=File.ReadAllText(Path.Combine(native,"feature-sources.json")).Contains("Vanilla Landmarks Expanded");
        var requests=vle?new Dictionary<string,string>{
            {"combo","비옥한 화산 토양 + 비옥한 비 + 온천 + 별 보기 좋은 곳 + 빈번한 오로라 + 화창함 + overgrown 특징을 전부 추가해 줘."},
            {"rich-fill","맵 중앙에 반지름이 맵 너비의 20%인 원형 영역을 실제 비옥한 화산 토양으로 채워줘. 특징 전체 추가 말고 그 원 안의 바닥 재료만 바꿔줘."}
        }:new Dictionary<string,string>{
            {"hot-temperate","온천 그냥 특징으로 추가해 줘."},
            {"hot-tropical","온천 추가해 줘."},
            {"missing","비옥한 화산 토양이랑 비옥한 비 추가해 줘."},
            {"followup","비옥한 화산 토양이랑 비옥한 비 없어?"}
        };
        if(!vle && !string.IsNullOrEmpty(recordedProse))requests.Add("repair","비옥한 화산 토양이랑 비옥한 비 없어?");
        var results=new List<object>();bool all=true;
        foreach(var entry in requests)
        {
            string only=Environment.GetEnvironmentVariable("MAPGENAI_FEEDBACK_CASE");
            if(!string.IsNullOrEmpty(only) && entry.Key!=only)continue;
            string id=entry.Key,family=id=="repair"?"followup":id;
            string prompt=File.ReadAllText(Path.Combine(native,family+"-prompt.txt"));
            var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,family+"-before.json")));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),entry.Value);
            int attempts=0,apiCalls=0;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
                var response=await StructuredChat.SendAsync(async (malformed,token)=>{
                    attempts++;
                    var messages=new List<ChatMessage>{new ChatMessage("user",entry.Value)};
                    if(malformed!=null){messages.Add(new ChatMessage("assistant",malformed));messages.Add(new ChatMessage("user",StructuredChat.RepairInstruction));}
                    string answer;
                    if(id=="repair" && attempts==1)answer=File.ReadAllText(recordedProse);
                    else{apiCalls++;answer=await client.SendChatAsync(messages,prompt,token);}
                    File.WriteAllText(Path.Combine(output,id+"-attempt-"+attempts+".txt"),answer);return answer;
                },timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var command=ProviderResponse.Command(response);bool generate=command.GetString("action")=="generate";
                var after=generate?MapStateEditor.Merge(before,MapParameterParser.Parse(command.GetObject("params"))):before.Clone();
                MapStateValidation.Validate(after);File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                bool intended;
                var clean=after.Clone();
                if(id.StartsWith("hot-")){intended=generate && after.mutators.SequenceEqual(new[]{"HotSprings"});clean.mutators.Clear();}
                else if(id=="combo"){intended=generate && after.mutators.Count==7 && Combo.All(after.mutators.Contains);clean.mutators.Clear();}
                else if(id=="rich-fill"){
                    intended=generate && after.elevationShapes.Count==1 &&
                        (after.elevationShapes[0].fill=="VEE_VolcanicSoilRich" || after.elevationShapes[0].compositeOps?.Any(o=>o.fill=="VEE_VolcanicSoilRich")==true);
                    clean.elevationShapes.Clear();
                }
                else intended=!generate;
                bool preserved=MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(before);
                all &= intended && preserved;
                results.Add(new{id,intended,preserved,attempts,apiCalls,controlledFirstResponse=id=="repair",modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine(id+": intended="+intended+", preserved="+preserved+", API calls="+apiCalls);
            }
            catch(Exception error){all=false;results.Add(new{id,attempts,apiCalls,error=error.Message});Console.WriteLine(id+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,vle,results,scope="Captured native catalogs, independent Korean requests through production bounded-format retry. repair injects one recorded malformed first answer before a real API repair; it is not a fresh first-attempt success. Native map/Undo replay is separate."}));
        }
        return all?0:1;
    }
}
