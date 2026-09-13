using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.ImageInput;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

public static class RealImageBench
{
    public static async Task<int> Run(GeminiClient client,string manifest,string output,string model)
    {
        var entries=SimpleJson.Parse(File.ReadAllText(manifest)).GetObjectArray("inputs");
        var results=new List<object>();
        string only=Environment.GetEnvironmentVariable("MAPGENAI_PROBE_ONLY");
        foreach(var entry in entries)
        {
            string id=entry.GetString("id");if(!string.IsNullOrWhiteSpace(only)&&id!=only)continue;
            var timer=Stopwatch.StartNew();
            try
            {
                var image=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("image")));
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
                ColorTerrainPlan colorPlan=null;
                if(Environment.GetEnvironmentVariable("MAPGENAI_PROBE_IMAGE_MODE")=="colors")
                {
                    var pixels=SimpleJson.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("pixels"))));
                    colorPlan=ColorTerrainPlan.Create(Convert.FromBase64String(pixels.GetString("rgb")),pixels.GetInt("width"),pixels.GetInt("height"));
                }
                string prompt=colorPlan==null?ImageInterpretation.BuildPrompt(true):colorPlan.BuildPrompt(true);
                string mime=entry.GetString("mime");
                if(Environment.GetEnvironmentVariable("MAPGENAI_PROBE_IMAGE_MODE")=="atlas")
                {
                    var plan=SimpleJson.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("colorPlan"))));
                    byte[] groups=plan.GetArray("groups").Select(byte.Parse).ToArray();
                    byte[][] colors=plan.GetNestedArray("colors").Select(row=>row.Select(byte.Parse).ToArray()).ToArray();
                    int[] groupCounts=Enumerable.Range(0,colors.Length).Select(i=>groups.Count(g=>g==i)).ToArray();
                    colorPlan=(ColorTerrainPlan)typeof(ColorTerrainPlan).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{plan.GetInt("width"),plan.GetInt("height"),groups,colors,groupCounts});
                    prompt=File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("colorPrompt")));
                    image=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("colorImage")));mime=entry.GetString("colorMime");
                }
                File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
                string response=await client.SendImageAsync(image,mime,prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var candidates=colorPlan==null?ImageInterpretation.Parse(response,entry.GetInt("width"),entry.GetInt("height")):new List<ImageCandidate>{colorPlan.Interpret(response)};
                var state=new TileMapState{imageMap=candidates[0].map};
                File.WriteAllText(Path.Combine(output,id+"-state.json"),MapStateCodec.Serialize(state));
                var counts=new Dictionary<string,int>();foreach(char c in state.imageMap.cells){string k=c.ToString();if(!counts.ContainsKey(k))counts[k]=0;counts[k]++;}
                results.Add(new{id,ok=true,counts,elapsedSeconds=timer.Elapsed.TotalSeconds,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens,thinkingTokens=client.LastThinkingTokens});
                Console.WriteLine(id+" parsed: "+System.Text.Json.JsonSerializer.Serialize(counts));
            }
            catch(Exception e)
            {
                results.Add(new{id,ok=false,error=e.Message,elapsedSeconds=timer.Elapsed.TotalSeconds});Console.WriteLine(id+": "+e.Message);
                if(e.Message.StartsWith("Gemini HTTP",StringComparison.Ordinal)){Save();return 1;}
            }
            Save();
        }
        return 0;
        void Save()=>File.WriteAllText(Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{model,utc=DateTime.UtcNow.ToString("o"),results,scope="Real map reference images, no legend; execution validity is separate from fidelity"}));
    }
}
