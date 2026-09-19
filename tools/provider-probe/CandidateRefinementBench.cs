using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class CandidateRefinementBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output,bool korean)
    {
        var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,"refinement-before.json")));
        var initial=File.ReadAllText(Path.Combine(native,"refinement-options-response.json"));
        var plans=RecommendationPlan.Validate(ProviderResponse.Command(initial),before,_=>{},korean);
        string basePrompt=File.ReadAllText(Path.Combine(native,"candidate-editor-prompt.txt"));
        int marker=basePrompt.IndexOf("PENDING RECOMMENDATION EDITOR:",StringComparison.Ordinal);
        basePrompt=basePrompt.Substring(0,marker);
        var results=new List<object>();var history=new List<ChatMessage>{new ChatMessage("assistant",initial)};
        var requests=korean?new[]{"3번에서 저런 곧은 일자 말고 자연스러운 일자로 해줘. 좀 울퉁불퉁하게.","3번 통로 다시 반듯하게 해 줘.","1번에 비옥도를 조금만 높여줘."}:
            new[]{"Make the passage in option 3 look natural with uneven edges, keeping its overall route.","Make option 3's passage edges precise again.","Increase fertility slightly in option 1."};
        for(int i=0;i<requests.Length;i++)
        {
            string id="refine-"+new[]{"natural","precise","fertility"}[i];
            string prompt=basePrompt+RecommendationPlan.PendingInstruction(plans,before);
            history.Add(new ChatMessage("user",requests[i]));
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt+"\nUSER: "+requests[i]);
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            {
                string response=await client.SendChatAsync(history,prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                StructuredChat.ValidateEnvelope(response);
                var command=ProviderResponse.Command(response);int expected=i<2?3:1;
                if(command.GetString("action")!="revise" || command.GetInt("option")!=expected)throw new Exception("Expected candidate revision "+expected);
                var prior=plans[expected-1].Resolve(before);
                var revised=RecommendationPlan.Refine(plans,expected,command.GetObject("params"),before,edits=>{},korean);
                var after=revised.Resolve(before);
                if(i<2)
                {
                    var oldPassage=prior.elevationShapes.Single(s=>s.type=="passage");
                    var newPassage=after.elevationShapes.Single(s=>s.type=="passage");
                    if(i==0?ContourWarp.Amount(newPassage.edge_roughness)<=0:ContourWarp.Amount(newPassage.edge_roughness)!=0)throw new Exception("Wrong passage roughness");
                    var compared=after.Clone();compared.elevationShapes.Single(s=>s.type=="passage").edge_roughness=oldPassage.edge_roughness;
                    if(MapStateCodec.Serialize(prior)!=MapStateCodec.Serialize(compared))throw new Exception("Roughness request altered other candidate state");
                }
                else
                {
                    var compared=after.Clone();compared.fertilityOffset=prior.fertilityOffset;
                    if(after.fertilityOffset<=prior.fertilityOffset || MapStateCodec.Serialize(prior)!=MapStateCodec.Serialize(compared))throw new Exception("Fertility-only request altered other candidate state");
                }
                plans[expected-1]=revised;history.Add(new ChatMessage("assistant",response));
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                results.Add(new Dictionary<string,object>{{"id",id},{"request",requests[i]},{"option",expected},{"preservedOtherState",true},{"model",client.LastModelVersion},{"inputTokens",client.LastInputTokens},{"outputTokens",client.LastOutputTokens}});
                File.WriteAllText(Path.Combine(output,"results.json"),SimpleJson.Serialize(results));
                Console.WriteLine(id+" candidate="+expected+" preserved=true");
            }
        }
        return 0;
    }
}
