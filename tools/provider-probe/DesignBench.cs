using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

// Four bounded first-response requests. No retries, automatic paid repair or invented pass.
static class DesignBench
{
    // Compare the entire state after allowing exactly the requested field to change.
    // A plausible gap/direction must not hide a moved basin, lost ruin, or global edit.
    internal static bool OnlyBasinChange(TileMapState before,TileMapState after,string field)
    {
        var old=before.elevationShapes.SingleOrDefault(s=>s.type=="landform" && s.landform=="open_basin");
        var basin=after.elevationShapes.SingleOrDefault(s=>s.type=="landform" && s.landform=="open_basin");
        if(old==null || basin==null)return false;
        var expected=before.Clone();var target=expected.elevationShapes.Single(s=>s.id==old.id);
        if(field=="gap")target.gap=basin.gap;
        else if(field=="direction")target.direction=basin.direction;
        else throw new ArgumentException("Unsupported allowed field");
        return MapStateCodec.Serialize(expected)==MapStateCodec.Serialize(after);
    }
    public static int AuditSelfTest(string output)
    {
        var before=new TileMapState();before.elevationShapes.Add(new ElevationShape{id="basin",type="landform",landform="open_basin",layout="organic",variant="23",direction="bottom"});
        var checks=new List<string>();
        foreach(string field in new[]{"gap","direction"})
        {
            var changed=before.Clone();if(field=="gap")changed.elevationShapes[0].gap="0.3";else changed.elevationShapes[0].direction="right";
            Check(field+" only accepted",OnlyBasinChange(before,changed,field));
            foreach(string extra in new[]{"size","position","opening","vegetation","extra-shape"})
            {
                var broken=changed.Clone();
                if(extra=="size")broken.elevationShapes[0].size="1";
                if(extra=="position")broken.elevationShapes[0].position="[0.1,0.1]";
                if(extra=="opening")broken.elevationShapes[0].opening="0.3";
                if(extra=="vegetation")broken.vegetationDensity=2;
                if(extra=="extra-shape")broken.elevationShapes.Add(new ElevationShape{id="unexpected",type="circle"});
                Check(field+" rejects unrelated "+extra,!OnlyBasinChange(before,broken,field));
            }
        }
        File.WriteAllLines(Path.Combine(output,"audit-selftest.txt"),checks);foreach(var line in checks)Console.WriteLine(line);
        return checks.All(s=>s.StartsWith("PASS ",StringComparison.Ordinal))?0:1;
        void Check(string name,bool ok)=>checks.Add((ok?"PASS ":"FAIL ")+name);
    }
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        string template=File.ReadAllText(Path.Combine(native,"system-prompt.txt"));
        int start=template.IndexOf("\n## 현재 적용된 파라미터",StringComparison.Ordinal);
        int end=start<0?-1:template.IndexOf("\n규칙:",start,StringComparison.Ordinal);
        if(start<0 || end<0)throw new InvalidOperationException("Native state section not found");
        var state=new TileMapState();var history=new List<ChatMessage>();var results=new List<object>();int calls=0;
        string[] requests={"예쁘고 정착하기 좋은 맵 추천해 줘.","자연스러운 분지 만들어 줘.","안쪽 평지만 조금 더 넓혀 줘.","출구만 동쪽으로 옮겨 줘."};
        for(int i=0;i<requests.Length;i++)
        {
            if(i==1){history.Clear();state=new TileMapState();}
            string id="design-"+(i+1).ToString("00"),current;
            using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(true);
            string prompt=template.Substring(0,start)+"\n"+current+template.Substring(end);
            history.Add(new ChatMessage("user",requests[i]));
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),requests[i]);
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(state));
            File.WriteAllText(Path.Combine(output,id+"-history.json"),System.Text.Json.JsonSerializer.Serialize(history));
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));calls++;
                string reply=await client.SendChatAsync(history,prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),reply);
                var cmd=ProviderResponse.Command(reply);bool intended;
                if(i==0)
                {
                    var plans=RecommendationPlan.Validate(cmd,state,data=>{},true);
                    intended=plans.Count==3;
                    for(int n=0;n<plans.Count;n++)
                    {var option=plans[n].Resolve(state);MapStateValidation.Validate(option);File.WriteAllText(Path.Combine(output,id+"-option-"+(n+1)+"-state.json"),MapStateCodec.Serialize(option));}
                }
                else
                {
                    if(cmd.GetString("action")!="generate")throw new Exception("Expected a terrain edit");
                    var before=state;state=MapStateEditor.Merge(state,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(state);
                    var basin=state.elevationShapes.SingleOrDefault(s=>s.type=="landform" && s.landform=="open_basin");
                    var old=before.elevationShapes.SingleOrDefault(s=>s.type=="landform" && s.landform=="open_basin");
                    intended=basin!=null && basin.layout=="organic";
                    if(i==2)intended = intended && old!=null && OnlyBasinChange(before,state,"gap") && NaturalLandformGeometry.Gap(basin)>NaturalLandformGeometry.Gap(old);
                    if(i==3)intended = intended && old!=null && OnlyBasinChange(before,state,"direction") && ElevationShape.ParseDirection(basin.direction)==0;
                    File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(state));
                }
                results.Add(new{id,request=requests[i],action=cmd.GetString("action"),intended,model=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens,thinkingTokens=client.LastThinkingTokens});
                history.Add(new ChatMessage("assistant","APPLIED SETTINGS\n"+reply));
                Save();Console.WriteLine(id+": intended="+intended);
                if(!intended)return 1;
            }
            catch(Exception error)
            {
                // Never print request URIs or configuration: an HttpClient exception may contain a key.
                results.Add(new{id,intended=false,errorType=error.GetType().Name,transportOrEnvelopeFailure=true});Save();
                Console.WriteLine(id+": "+error.GetType().Name+" (details omitted to protect configuration)");return 1;
            }
        }
        return 0;
        void Save()=>File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{calls,results,scope="Fresh first responses. Schema/intent only; actual native maps and aesthetics are evaluated separately."}));
    }
}
