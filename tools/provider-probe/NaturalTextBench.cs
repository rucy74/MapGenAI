using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class NaturalTextBench
{
    public static async Task<int> Run(GeminiClient client,string template,string output)
    {
        var state=MapStateEditor.Merge(null,MapParameterParser.Parse(SimpleJson.Parse("{\"elevation_shapes\":[{\"id\":\"west\",\"type\":\"ridge\",\"direction\":\"left\",\"strength\":\"strong\"}]}")));
        var requests=new[]{"왼쪽 산은 유지하고 중앙에 원형 호수를 추가해줘.","그 원형 호수의 윤곽만 자연스러운 원형으로 바꿔줘. 위치와 크기는 그대로.","원형 호수의 윤곽을 살짝만 울퉁불퉁하게 해줘. 다른 건 그대로.","원형 호수의 윤곽을 많이 울퉁불퉁하게 해줘. 다른 건 그대로.","원형 호수 윤곽을 다시 완전히 정확한 원형으로 돌려줘. 다른 건 그대로.","기존 지형 그대로 두고 오른쪽 위에 자연스러운 별 모양 호수를 추가해줘.","기존 지형 그대로 두고 오른쪽 아래에 자연스러운 하트 모양 호수를 추가해줘."};
        string[] expected={"none","medium","low","high","none","medium","medium"};
        var results=new List<object>();bool all=true;string circleId=null;
        for(int i=0;i<requests.Length;i++)
        {
            try
            {
                string current;using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(true);
                string prompt=File.ReadAllText(template).Replace("MAPGENAI_CURRENT_STATE_PLACEHOLDER",current);
                File.WriteAllText(Path.Combine(output,$"natural-{i+1}-prompt.txt"),prompt+"\nUSER: "+requests[i]);
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,$"natural-{i+1}-response.json"),response);
                var command=ProviderResponse.Command(response);
                if(command.GetString("action")!="generate")throw new Exception("Expected generate command");
                var before=state;state=MapStateEditor.Merge(state,MapParameterParser.Parse(command.GetObject("params")));
                bool addition=i==0||i>=5;ElevationShape target;
                bool preserved;
                if(addition)
                {
                    target=state.elevationShapes.Single(s=>!before.elevationShapes.Any(b=>b.id==s.id));
                    if(i==0)circleId=target.id;
                    var without=state.Clone();without.elevationShapes.RemoveAll(s=>s.id==target.id);
                    preserved=MapStateCodec.Serialize(before)==MapStateCodec.Serialize(without);
                }
                else
                {
                    target=state.elevationShapes.Single(s=>s.id==circleId);
                    var restored=state.Clone();restored.elevationShapes.Single(s=>s.id==circleId).edge_roughness=before.elevationShapes.Single(s=>s.id==circleId).edge_roughness;
                    preserved=MapStateCodec.Serialize(before)==MapStateCodec.Serialize(restored);
                }
                string primitive=i==5?"star":i==6?"heart":"circle";
                bool intended=target.type=="composite"&&target.compositeShapes.Any(p=>p.prim==primitive)&&ContourWarp.Amount(target.edge_roughness)==ContourWarp.Amount(expected[i]);
                all&=intended&&preserved;
                results.Add(new{turn=i+1,request=requests[i],intended,preserved,roughness=target.edge_roughness,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                File.WriteAllText(Path.Combine(output,$"natural-{i+1}-state.json"),MapStateCodec.Serialize(state));
                Console.WriteLine($"Natural turn {i+1}: intended={intended}, preserved={preserved}, roughness={target.edge_roughness??"omitted"}");
            }
            catch(Exception error){all=false;results.Add(new{turn=i+1,error=error.Message});Console.WriteLine(error.Message);break;}
        }
        File.WriteAllText(Path.Combine(output,"natural-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Seven Korean prompts with captured production template; this model/run only"}));
        return all?0:1;
    }
}
