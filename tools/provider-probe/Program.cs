using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.ImageInput;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

class Program
{
    static string output;
    static async Task<int> Main(string[] args)
    {
        if(args.Length<3){Console.WriteLine("Usage: <repo> <output> vision|text [runtime production-system-prompt.txt]");return 2;}
        output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var config=SimpleJson.Parse(File.ReadAllText(Path.Combine(args[0],"docs/dev_config.json")));
        string modelOverride=Environment.GetEnvironmentVariable("MAPGENAI_PROBE_MODEL");
        if(!string.IsNullOrWhiteSpace(modelOverride))config.SetString("gemini_model",modelOverride);
        var client=new GeminiClient(config.GetString("gemini_api_key"),config.GetString("gemini_model"));
        if(args[2]=="natural")return await NaturalTextBench.Run(client,args[3],output);
        if(args[2]=="features")return await FeatureTextBench.Run(client,args[3],output);
        if(args[2]=="real")return await RealImageBench.Run(client,args[3],output,config.GetString("gemini_model"));
        var results=new List<object>();
        if(args[2]=="vision")
        {
            foreach(string name in new[]{"topdown","sketch"})
            {
                var truth=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(output,name+"-truth.json"))).imageMap;
                var maps=new List<ImageMapData>();
                for(int run=1;run<=2;run++)
                {
                    string id=name+"-"+run;
                    try
                    {
                        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                        string instruction=ImageInterpretation.BuildPrompt(true,Environment.GetEnvironmentVariable("MAPGENAI_PROBE_IMAGE_NOTES"));
                        File.WriteAllText(Path.Combine(output,id+"-instruction.txt"),instruction);
                        var response=await client.SendImageAsync(File.ReadAllBytes(Path.Combine(output,name+".png")),"image/png",instruction,timeout.Token);
                        File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                        var candidates=ImageInterpretation.Parse(response,truth.width,truth.height);var map=candidates[0].map;maps.Add(map);
                        File.WriteAllText(Path.Combine(output,id+"-state.json"),MapStateCodec.Serialize(new TileMapState{imageMap=map}));
                        var metrics=new Dictionary<string,object>{{"id",id},{"candidateCount",candidates.Count},{"waterIoU",IoU(truth,map,'W')},{"mountainIoU",IoU(truth,map,'M')},{"waterCentroid",Centroid(map,'W')},{"mountainCentroid",Centroid(map,'M')},{"modelVersion",client.LastModelVersion},{"inputTokens",client.LastInputTokens},{"outputTokens",client.LastOutputTokens},{"thinkingTokens",client.LastThinkingTokens}};
                        results.Add(metrics);Console.WriteLine(SimpleJson.Serialize(metrics));
                    }
                    catch(Exception e)
                    {
                        results.Add(new{id,error=e.Message});Console.WriteLine(id+": "+e.Message);
                        if(e.Message.StartsWith("Gemini HTTP",StringComparison.Ordinal)){Save(results,config.GetString("gemini_model"),"vision");return 1;}
                    }
                    Save(results,config.GetString("gemini_model"),"vision");
                }
                if(maps.Count==2)results.Add(new{name,waterRepeatIoU=IoU(maps[0],maps[1],'W'),mountainRepeatIoU=IoU(maps[0],maps[1],'M')});
            }
            Save(results,config.GetString("gemini_model"),"vision");
        }
        else
        {
            if(args.Length<4)throw new ArgumentException("Supply a captured production system prompt");
            string prompt=File.ReadAllText(args[3]);
            var state=MapStateEditor.Merge(null,MapParameterParser.Parse(SimpleJson.Parse("{\"elevation_shapes\":[{\"id\":\"west\",\"type\":\"ridge\",\"direction\":\"left\",\"strength\":\"strong\"},{\"id\":\"lake\",\"type\":\"bump\",\"position\":\"bottom_right\",\"size\":\"small\",\"strength\":\"negative_strong\",\"fill\":\"water\"}]}")));
            string west=SimpleJson.Serialize(state.elevationShapes[0]);int turn=0;
            bool complex=args[2]=="complex";
            if(complex){state=MapStateCodec.Deserialize(File.ReadAllText(args[4]));turn=3;}
            var requests=complex?new[]{"기존 지형은 그대로 두고 맵 중앙에 하트 모양 호수를 하나 추가해줘.","방금 추가한 하트 호수를 오른쪽으로 맵 너비의 10%만큼 이동해줘. 다른 지형은 그대로 둬."}:new[]{"왼쪽 산과 오른쪽 아래 호수는 그대로 두고 오른쪽 위에 원형 산 하나만 추가해줘.","다른 지형은 그대로 두고 lake 호수 크기만 medium으로 바꿔줘.","기존 지형은 그대로 두고 고대 위협 밀도를 1.8로 해줘. 폐허나 고대 유적 밀도는 바꾸지 마."};
            foreach(string request in requests)
            {
                turn++;string current;using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(true);
                string full=prompt.Contains("MAPGENAI_CURRENT_STATE_PLACEHOLDER")?prompt.Replace("MAPGENAI_CURRENT_STATE_PLACEHOLDER",current):prompt+"\n"+current;
                File.WriteAllText(Path.Combine(output,"text-"+turn+"-prompt.txt"),full+"\nUSER: "+request);
                try
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    var response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",request)},full,timeout.Token);
                    File.WriteAllText(Path.Combine(output,"text-"+turn+"-response.json"),response);
                    var command=ProviderResponse.Command(response);if(command.GetString("action")!="generate")throw new Exception("Did not generate an edit");
                    var before=state.Clone();state=MapStateEditor.Merge(state,MapParameterParser.Parse(command.GetObject("params")));
                    bool preserved=SimpleJson.Serialize(state.elevationShapes.Find(s=>s.id=="west"))==west;
                    bool intended=turn==1?state.elevationShapes.Count==3 && SimpleJson.Serialize(before.elevationShapes[1])==SimpleJson.Serialize(state.elevationShapes.Find(s=>s.id=="lake")):
                        turn==2?state.elevationShapes.Count==3 && state.elevationShapes.Find(s=>s.id=="lake").size=="medium":
                        turn==3?state.dangerDensity==1.8f && state.ruinDensity==1f && SimpleJson.Serialize(state.elevationShapes)==SimpleJson.Serialize(before.elevationShapes):
                        turn==4?state.elevationShapes.Count==4 && state.elevationShapes.Last().type=="composite" && state.elevationShapes.Last().compositeOps.Any(o=>o.e<0 || o.fill=="water"):
                        state.elevationShapes.Count==4 && SimpleJson.Serialize(state.elevationShapes.Take(3).ToList())==SimpleJson.Serialize(before.elevationShapes.Take(3).ToList()) &&
                            state.elevationShapes.Last().compositeShapes[0].GetCenter().x>before.elevationShapes.Last().compositeShapes[0].GetCenter().x;
                    results.Add(new{turn,request,preserved,intended,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens,thinkingTokens=client.LastThinkingTokens});Console.WriteLine("text "+turn+" preserved="+preserved+" intended="+intended);
                    File.WriteAllText(Path.Combine(output,"text-"+turn+"-state.json"),MapStateCodec.Serialize(state));
                }
                catch(Exception e){results.Add(new{turn,error=e.Message});Console.WriteLine("text "+turn+": "+e.Message);}
                Save(results,config.GetString("gemini_model"),"text");
            }
        }
        return 0;
    }
    static double IoU(ImageMapData a,ImageMapData b,char label)
    {
        int union=0,intersection=0;for(int i=0;i<a.cells.Length;i++){if(a.cells[i]==label || b.cells[i]==label)union++;if(a.cells[i]==label && b.cells[i]==label)intersection++;}return union==0?1:(double)intersection/union;
    }
    static double[] Centroid(ImageMapData map,char label)
    {
        double x=0,z=0;int count=0;for(int i=0;i<map.cells.Length;i++)if(map.cells[i]==label){x+=(i%map.width+.5)/map.width;z+=(i/map.width+.5)/map.height;count++;}return count==0?null:new[]{x/count,z/count};
    }
    static void Save(List<object> results,string model,string mode)=>File.WriteAllText(Path.Combine(output,mode+"-results.json"),System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,object>{{"model",model},{"utc",DateTime.UtcNow.ToString("o")},{"results",results},{"scope","Synthetic fixtures and captured production prompt; no claim for arbitrary photos or all providers"}}));
}
