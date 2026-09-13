using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class ManualFailureBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var results=new List<object>();bool all=true;
        foreach(string family in new[]{"d04","f03","f03-original"})
        for(int repeat=1;repeat<=3;repeat++)
        {
            string id=family+"-"+repeat;
            string request=family=="d04"?"왼쪽 산맥 중앙을 동서로 관통하는, 너비가 맵 높이의 8% 정도인 협곡 통로를 만들어줘. 호수와 오른쪽 위 산은 그대로 둬.":"해자의 모양과 섬, 폐허는 그대로 두고 물 해자만 실제 깊은 용암으로 바꿔줘.";
            var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,family+"-before.json")));
            string prompt=File.ReadAllText(Path.Combine(native,family+"-prompt.txt"));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),request);File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                // Independent first attempts: no previous successful answer or failed attempt in history.
                var response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",request)},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var cmd=ProviderResponse.Command(response);if(cmd.GetString("action")!="generate")throw new InvalidOperationException("Expected first-attempt edit");
                var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                var clean=after.Clone();bool intended;
                if(family=="d04")
                {
                    var added=after.elevationShapes.Where(s=>!before.elevationShapes.Any(old=>old.id==s.id)).ToList();
                    intended=added.Count==1 && added[0].type=="composite" && added[0].compositeOps.Any(op=>op.e>0 && op.e<.1f && new[]{"soil","Soil"}.Contains(added[0].fill??op.fill));
                    clean.elevationShapes.RemoveAll(s=>added.Any(a=>a.id==s.id));
                }
                else
                {
                    string target=family=="f03"?"water_moat":"moat";
                    var shape=after.elevationShapes.Single(s=>s.id==target);var old=before.elevationShapes.Single(s=>s.id==target);
                    intended=new[]{"LavaDeep","lava"}.Contains(shape.fill) && SimpleJson.Serialize(shape.compositeShapes)==SimpleJson.Serialize(old.compositeShapes) && SimpleJson.Serialize(shape.compositeOps)==SimpleJson.Serialize(old.compositeOps);
                    clean.elevationShapes.Single(s=>s.id==target).fill=old.fill;
                }
                bool preserved=MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(before);all &= intended && preserved;
                results.Add(new{id,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine(id+": intended="+intended+", preserved="+preserved);
            }
            catch(Exception error){all=false;results.Add(new{id,error=error.Message});Console.WriteLine(id+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Nine independent Korean first attempts, exact D04/F03 manual prompts with native prompts and actual recorded starting states. Native map/preview checks run separately; not a universal success-rate claim."}));
        }
        return all?0:1;
    }
}
