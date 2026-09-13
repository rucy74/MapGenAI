using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class FeaturePolicyTextBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        string[] ids={"delta-only","restore-delta","inland-coast","inland-delta","inland-lake","desert-oasis","wrong-oasis","all-rivers","coast-only","lava-position","ruin-position"};
        string[] requests={"삼각주만 없애줘. 일반 강과 해안은 그대로 유지해.","제거했던 삼각주를 다시 추가해줘.","이 내륙 타일을 바다가 있는 해안으로 만들어줘.","이 내륙 강을 삼각주로 바꿔줘.","내륙에 오디세이 지형 특징인 호수를 추가해줘.","이 사막에 오아시스 지형 특징을 추가해줘.","이 온대림에 오아시스 지형 특징을 추가해줘.","월드맵에는 강이 있지만 정착지 맵에서는 강을 완전히 없애줘.","바다 해안을 완전히 없애줘.","중앙 호수를 물 대신 용암으로 채워줘. 물로 대체하지는 마.","내가 만든 중앙 호수 안의 섬에 고대 유적을 배치해줘. 밀도만 바꾸라는 게 아니라 그 위치에 놓아달라는 거야."};
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
                bool intended=false,preserved=true;bool shouldAsk=!(i==0||i==1||i==4||i==5);
                if(shouldAsk)intended=action=="ask"&&!string.IsNullOrWhiteSpace(command.GetString("message"));
                else if(action=="generate")
                {
                    var data=MapParameterParser.Parse(command.GetObject("params"));
                    var after=MapStateEditor.Merge(before,data);MapStateValidation.Validate(after);
                    intended=i==0?after.removeMutators.Contains("RiverDelta")&&!after.removeFeatureCategories.Contains("River"):
                        i==1?after.mutators.Contains("RiverDelta")&&!after.removeMutators.Contains("RiverDelta"):
                        i==4?after.mutators.Contains("Lake"):after.mutators.Contains("Oasis");
                    var restored=after.Clone();restored.removeMutators=new List<string>(before.removeMutators);
                    restored.removeFeatureCategories=new List<string>(before.removeFeatureCategories);restored.mutators=new List<string>(before.mutators);
                    preserved=MapStateCodec.Serialize(before)==MapStateCodec.Serialize(restored);
                    File.WriteAllText(Path.Combine(output,ids[i]+"-after.json"),MapStateCodec.Serialize(after));
                }
                all&=intended&&preserved;
                results.Add(new{id=ids[i],action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{ids[i]}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception error){all=false;results.Add(new{id=ids[i],error=error.Message});Console.WriteLine(ids[i]+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"feature-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Eleven independent Korean requests using captured production prompts; parser/state intent checks. Actual engine replay is recorded separately."}));
        }
        return all?0:1;
    }
}
