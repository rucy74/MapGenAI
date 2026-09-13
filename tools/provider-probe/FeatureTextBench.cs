using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;

static class FeatureTextBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var catalog=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(native,"mutator-catalog.json")));
        foreach(var item in catalog.RootElement.EnumerateArray())
        {
            var def=new TileMutatorDef{defName=item.GetProperty("def").GetString(),priority=item.GetProperty("priority").GetInt32(),
                categories=item.GetProperty("categories").EnumerateArray().Select(x=>x.GetString()).ToList(),
                overrideCategories=item.GetProperty("overrideCategories").EnumerateArray().Select(x=>x.GetString()).ToList()};
            DefDatabase<TileMutatorDef>.Definitions[def.defName]=def;
        }
        string[] ids={"delta-only","all-rivers","restore-rivers","coast-only","lava-position","ruin-position"};
        string[] requests={"삼각주만 없애줘. 일반 강과 내가 만든 호수는 그대로 유지해.","강을 완전히 없애줘. 삼각주나 강 섬도 남기지 말고 내가 만든 호수는 그대로 유지해.","방금 제거한 강을 원래대로 복원해줘. 내가 만든 호수는 그대로 유지해.","바다 해안만 없애줘. 강과 내가 만든 호수는 건드리지 마.","중앙 호수를 물 대신 용암으로 채워줘. 물로 대체하지는 마.","내가 만든 중앙 호수 안의 섬에 고대 유적을 배치해줘. 밀도만 바꾸라는 게 아니라 그 위치에 놓아달라는 거야."};
        bool all=true;var results=new List<object>();
        for(int i=0;i<ids.Length;i++)
        {
            try
            {
                string prompt=File.ReadAllText(Path.Combine(native,ids[i]+"-prompt.txt"));
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,ids[i]+"-before.json")));
                File.WriteAllText(Path.Combine(output,ids[i]+"-request.txt"),requests[i]);
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,ids[i]+"-response.json"),response);
                var command=ProviderResponse.Command(response);string action=command.GetString("action");
                bool intended=false,preserved=true;
                if(i>=4)intended=action=="ask"&&!string.IsNullOrWhiteSpace(command.GetString("message"));
                else if(action=="generate")
                {
                    var data=MapParameterParser.Parse(command.GetObject("params"));
                    WorldTileEditor.ValidateFeatureRequest(data);
                    var after=MapStateEditor.Merge(before,data);MapStateValidation.Validate(after);
                    intended=i==0?after.removeMutators.Contains("RiverDelta")&&!after.removeFeatureCategories.Contains("River"):
                        i==1?after.removeFeatureCategories.Contains("River"):
                        i==2?!after.removeFeatureCategories.Contains("River"):
                        (after.removeFeatureCategories.Contains("Coast")||after.removeMutators.Contains("Coast"))&&!after.removeFeatureCategories.Contains("River");
                    var restored=after.Clone();restored.hasRiver=before.hasRiver;restored.removeMutators=new List<string>(before.removeMutators);
                    restored.removeFeatureCategories=new List<string>(before.removeFeatureCategories);restored.mutators=new List<string>(before.mutators);
                    preserved=MapStateCodec.Serialize(before)==MapStateCodec.Serialize(restored);
                    File.WriteAllText(Path.Combine(output,ids[i]+"-after.json"),MapStateCodec.Serialize(after));
                }
                all&=intended&&preserved;
                results.Add(new{id=ids[i],action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{ids[i]}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception error){all=false;results.Add(new{id=ids[i],error=error.Message});Console.WriteLine(ids[i]+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"feature-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Six independent Korean requests using actual production prompts on a disposable coastal river tile; unsupported lava/structure position must be explained, not reported as implemented"}));
        }
        return all?0:1;
    }
}
