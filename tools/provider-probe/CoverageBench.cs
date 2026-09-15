using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class CoverageBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var results=new List<object>();bool all=true;
        var cases=new[]{new[]{"ko70-1","baseline","도넛 안에 70%만 비옥한 토양으로 채워줘","0.7"},new[]{"ko70-2","baseline","도넛 안에 70%만 비옥한 토양으로 채워줘","0.7"},new[]{"followup50","filled70","그럼 그 비옥한 토양을 절반만 채운 걸로 줄여줘. 산은 그대로 두고.","0.5"},new[]{"repair70","legacy","도넛 안에 비옥한 토양을 70%만 채워달라고 했잖아. 거의 다 찼어. 산은 그대로 두고 비율만 고쳐줘.","0.7"},new[]{"en70","baseline","Fill only 70% of the interior of the donut mountain with rich soil. Keep the mountain unchanged.","0.7"}};
        foreach(var c in cases)
        {
            string id=c[0],prompt=File.ReadAllText(Path.Combine(native,c[1]+"-prompt.txt"));
            var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,c[1]+"-before.json")));
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),c[2]);File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",c[2])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);var cmd=ProviderResponse.Command(response);
                if(cmd.GetString("action")!="generate")throw new Exception("Expected edit on the first response");
                var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                var mountain=before.elevationShapes.First(s=>s.id=="central_mountain_donut");var fills=after.elevationShapes.Where(s=>s.type=="region_fill").ToList();
                if(after.elevationShapes.Count!=2 || fills.Count!=1 || fills[0].region!=mountain.id || fills[0].region_part!="enclosed" || Math.Abs(float.Parse(fills[0].coverage,System.Globalization.CultureInfo.InvariantCulture)-float.Parse(c[3],System.Globalization.CultureInfo.InvariantCulture))>.00001 || TerrainMaterials.DefName(fills[0].fill)!="SoilRich")throw new Exception("Wrong coverage target, fraction or material");
                if(SimpleJson.Serialize(mountain)!=SimpleJson.Serialize(after.elevationShapes.First(s=>s.id==mountain.id)))throw new Exception("Mountain geometry changed");
                var check=after.Clone();check.elevationShapes=before.Clone().elevationShapes;if(MapStateCodec.Serialize(check)!=MapStateCodec.Serialize(before))throw new Exception("Unrequested settings changed");
                File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));results.Add(new Dictionary<string,object>{{"id",id},{"pass",true}});Console.WriteLine("PASS "+id);
            }
            catch(Exception error){all=false;results.Add(new Dictionary<string,object>{{"id",id},{"pass",false},{"error",error.ToString()}});Console.WriteLine("FAIL "+id+": "+error.Message);}
        }
        File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",all},{"checks",results},{"requests",cases.Length}}));return all?0:1;
    }
}
