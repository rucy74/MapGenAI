using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class PlanSequenceBench
{
    // Same successful-turn behavior as Dialog: refresh state, clear conversational context.
    // The captured catalog is valid for these terrain-only edits; feature edits require a fresh capture.
    public static async Task<int> Run(GeminiClient client,string native,string spec,string output)
    {
        var input=SimpleJson.Parse(File.ReadAllText(spec));bool ko=input.GetString("language")!="English";
        var turns=input.GetObjectArray("turns");string capture=input.GetString("capture");
        var state=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,capture+"-before.json")));
        string template=File.ReadAllText(Path.Combine(native,capture+"-prompt.txt"));
        int start=template.IndexOf(ko?"\n## 현재 적용된 파라미터":"\n## Current",StringComparison.Ordinal);
        int end=start<0?-1:template.IndexOf(ko?"\n규칙:":"\nRules:",start,StringComparison.Ordinal);
        if(start<0 || end<0)throw new InvalidOperationException("Captured current-state section not found");
        var results=new List<object>();var cases=new List<object>();
        foreach(var turn in turns)
        {
            string id=turn.GetString("id"),request=turn.GetString("request"),current;
            if(File.Exists(Path.Combine(output,id+"-response.json")))throw new InvalidOperationException("Refusing to overwrite recorded response "+id);
            using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(ko);
            string prompt=template.Substring(0,start)+"\n"+current+template.Substring(end);
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(state));
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),request);
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",request)},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var command=ProviderResponse.Command(response);string action=command.GetString("action");
                if(action!="generate")throw new InvalidOperationException("Expected edit; received "+action);
                state=MapStateEditor.Merge(state,MapParameterParser.Parse(command.GetObject("params")));MapStateValidation.Validate(state);
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(state));
                results.Add(new{id,action,model=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                cases.Add(new{id,kind="compound-plan",request,beforeFile=id+"-before.json",omitAmbientRuins=true,preview=turn.GetBool("preview")});
                Console.WriteLine(id+": accepted state (intent/full-map checks are separate)");
            }
            catch(Exception error)
            {
                results.Add(new{id,error=error.Message});Save(false);Console.WriteLine(id+": "+error.Message);return 1;
            }
            Save(true);
        }
        return 0;
        void Save(bool ok)
        {
            File.WriteAllText(Path.Combine(output,"sequence-result.json"),System.Text.Json.JsonSerializer.Serialize(new{ok,results}));
            File.WriteAllText(Path.Combine(output,"cases.json"),System.Text.Json.JsonSerializer.Serialize(new{cases}));
        }
    }
}
