using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class EditPreflightBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output,string evidence)
    {
        string prompt=File.ReadAllText(Path.Combine(native,"current-prompt.txt"));
        var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,"before.json")));
        var requests=new Dictionary<string,string>{{"oasis","오아시스 추가해 줘"},{"ambiguous","그래"},{"pair","온천은 유지하고 연못 특징도 추가해 줘"},{"replace","온천을 없애고 연못 특징으로 교체해 줘"},{"additive","온천은 그대로 두고 화창함만 추가해 줘"},{"explanation","그래"}};
        var results=new List<object>();bool all=true;
        foreach(var entry in requests)
        {
            var history=new List<ChatMessage>();
            if(entry.Key=="ambiguous")
            {
                history.Add(new ChatMessage("user","오아시스 추가해 줘"));
                history.Add(new ChatMessage("assistant",SimpleJson.Parse(File.ReadAllText(Path.Combine(evidence,"ambiguous-offer.json"))).GetString("message")));
            }
            history.Add(new ChatMessage("user",entry.Value));
            if(entry.Key=="explanation")
            {
                history.Add(new ChatMessage("assistant",File.ReadAllText(Path.Combine(evidence,"rejected-response.json"))));
                history.Add(new ChatMessage("user",InvalidEditExplanation.Instruction(File.ReadAllText(Path.Combine(native,"rejection-reason.txt")))));
            }
            File.WriteAllText(Path.Combine(output,entry.Key+"-history.json"),System.Text.Json.JsonSerializer.Serialize(history));
            File.WriteAllText(Path.Combine(output,entry.Key+"-before.json"),MapStateCodec.Serialize(before));
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await StructuredChat.SendAsync((bad,token)=>{
                    var messages=new List<ChatMessage>(history);
                    if(bad!=null){messages.Add(new ChatMessage("assistant",bad));messages.Add(new ChatMessage("user",StructuredChat.RepairInstruction));}
                    return client.SendChatAsync(messages,prompt,token);
                },timeout.Token);
                File.WriteAllText(Path.Combine(output,entry.Key+"-response.json"),response);
                var cmd=ProviderResponse.Command(response);bool generated=cmd.GetString("action")=="generate";
                var after=generated?MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params"))):before.Clone();
                bool intended=entry.Key=="replace"?generated && after.mutators.SequenceEqual(new[]{"Pond"}) && after.removeMutators.SequenceEqual(new[]{"HotSprings"}):entry.Key=="additive"?generated && new HashSet<string>(after.mutators).SetEquals(new[]{"HotSprings","SunnyMutator"}):!generated;
                var clean=after.Clone();clean.mutators=before.mutators.ToList();clean.removeMutators=before.removeMutators.ToList();
                bool preserved=MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(before);all &= intended && preserved;
                File.WriteAllText(Path.Combine(output,entry.Key+"-after.json"),MapStateCodec.Serialize(after));
                results.Add(new{id=entry.Key,intended,preserved,action=cmd.GetString("action"),modelVersion=client.LastModelVersion});Console.WriteLine(entry.Key+": intended="+intended+", preserved="+preserved);
            }
            catch(Exception error){all=false;results.Add(new{id=entry.Key,error=error.Message});Console.WriteLine(entry.Key+": "+error.Message);}
            File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Six Korean cases with native current-state catalog; explanation includes the real rejected command and actual dry-run error. Not a universal language guarantee."}));
        }
        File.WriteAllText(Path.Combine(output,"system-prompt.txt"),prompt);return all?0:1;
    }
}
