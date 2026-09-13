using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;

static class AncientTextBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        string[] ids={"ancient-island","ancient-foothill","ancient-rotation","ancient-unknown","ancient-density"};
        string[] requests={
            "용암과 가운데 흙 섬은 그대로 두고, 그 섬 안에 게임 기본 고대 위협을 하나 배치해줘. 가로18 세로18칸으로 하고 건물 전체가 섬 안에 있어야 해. 자연 생성 밀도와 다른 설정은 바꾸지 마.",
            "산 서쪽 기슭에 게임 기본 고대 위협을 하나 놓아줘. 가로18 세로16칸이고, 산 바위에서 가장 가까운 건물 칸까지 2~12칸 떨어뜨려줘. 산과 자연 생성 밀도, 다른 설정은 그대로.",
            "기존 temple 고대 위협의 내부와 건물을 정확히 90도 회전해줘. 지원하지 않으면 다른 것으로 대체하지 말고 현재 상태를 유지한 채 설명해.",
            "기존 지형은 그대로 두고 아노말리 퀘스트 전용 복합 연구 단지를 새로 하나 지정 생성해줘. 단순 폐허나 고대 위협으로 대체하지 말고 지원하지 않으면 설명해.",
            "이미 배치한 temple 고대 위협과 용암, 섬은 그대로 두고 자연 생성 고대 위협 밀도만 0.6으로 바꿔줘. 폐허 밀도와 다른 설정은 바꾸지 마."};
        var results=new List<object>();bool all=true;
        for(int i=0;i<ids.Length;i++)
        {
            string id=ids[i];var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,id+"-before.json")));
            string prompt=File.ReadAllText(Path.Combine(native,id+"-prompt.txt"));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),requests[i]);File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var cmd=ProviderResponse.Command(response);string action=cmd.GetString("action");bool intended=false,preserved=true;
                if(i==2 || i==3)intended=action=="ask" && !string.IsNullOrWhiteSpace(cmd.GetString("message"));
                else if(action=="generate")
                {
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                    var clean=after.Clone();
                    if(i==4){intended=after.dangerDensity==.6f;clean.dangerDensity=before.dangerDensity;}
                    else
                    {
                        var p=after.structures.Single();
                        intended=p.kind=="ancient_danger" && p.count==1 && p.width==18 && p.height==(i==0?18:16) && p.rotation==0;
                        intended &= i==0?p.region=="island":p.relation!=null && p.relation.target=="mountain" && p.relation.side=="west" && p.relation.min_distance==2 && p.relation.max_distance==12;
                        clean.structures=before.structures;
                    }
                    preserved=MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(before);
                    File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                }
                all &= intended && preserved;
                results.Add(new{id,action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{id}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception e){all=false;results.Add(new{id,error=e.Message});Console.WriteLine(id+": "+e.Message);}
            File.WriteAllText(Path.Combine(output,"ancient-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Five independent Korean requests against captured native production prompts; actual Dialog replay, Undo and native generation checked separately."}));
        }
        return all?0:1;
    }
}
