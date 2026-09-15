using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.UI;

static class LandformBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var results=new List<object>();bool ok=true;
        foreach(string path in Directory.GetFiles(native,"*-request.txt").OrderBy(p=>p))
        {
            string id=Path.GetFileName(path).Replace("-request.txt","");
            foreach(string suffix in new[]{"-request.txt","-prompt.txt","-before.json"})File.Copy(Path.Combine(native,id+suffix),Path.Combine(output,id+suffix),true);
            if(File.Exists(Path.Combine(output,id+"-response.json")))throw new InvalidOperationException("Refusing to overwrite recorded response: "+id);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",File.ReadAllText(path))},File.ReadAllText(Path.Combine(native,id+"-prompt.txt")),timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var command=ProviderResponse.Command(response);results.Add(new{id,action=command.GetString("action"),model=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});Console.WriteLine(id+": "+command.GetString("action"));
            }
            catch(Exception error){ok=false;results.Add(new{id,error=error.Message});Console.WriteLine(id+": request or response failure");}
            File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{requestsCompleted=results.Count,transportAndEnvelopeOk=ok,results,scope="Raw first responses only. Intent and actual maps are evaluated separately."}));
        }
        return ok?0:1;
    }
}
