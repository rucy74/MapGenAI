using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class TextRegionBench
{
    static bool Fills(TileMapState state,string material)=>state.elevationShapes.Any(s=>TerrainMaterials.DefName(s.fill??"")==material ||
        (s.fill==null && s.compositeOps?.Any(o=>TerrainMaterials.DefName(o.fill??"")==material)==true));
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        string[] ids={"lava-fill","ruin-island","move-island","ruin-count","remove-area","material-volcanic","unknown-material","ancient-danger-position","image-paused","lava-from-scratch"};
        string[] requests={
            "moat의 물만 실제 용암으로 바꿔줘. 가운데 섬과 모양은 그대로 유지해.",
            "island 영역 안에 벽과 바닥이 남은 작은 폐허를 2개 배치해줘. 다른 지형이나 밀도는 바꾸지 마.",
            "island를 [0.7,0.65]로 이동해줘. 섬에 연결한 폐허도 그 섬 안에 따라오게 하고 다른 건 그대로 둬.",
            "ruins_1 폐허 개수만 1개로 줄여줘. 다른 지형과 설정은 그대로.",
            "island 영역과 거기에 연결한 폐허를 둘 다 삭제해줘. 용암 해자는 유지해.",
            "moat의 물만 화산암 VolcanicRock으로 채워줘. 나머지는 그대로.",
            "moat를 초콜릿 지형으로 채워줘. 모드에 없으면 다른 지형으로 대체하지 마.",
            "island 가운데에 적과 전리품을 가진 완전한 고대 위협 시설을 위치 지정해서 넣어줘.",
            "맵 이미지를 불러와서 이미지 기반으로 생성하고 싶어. 이미지 기능을 열어줘.",
            "중앙에 흙으로 된 평평한 섬을 만들고 그 주변을 도넛 모양의 실제 용암으로 채워줘. 섬 안에는 벽과 바닥이 남은 작은 폐허를 하나 놓아줘. 물은 쓰지 마."};
        bool all=true;var results=new List<object>();
        for(int i=0;i<ids.Length;i++)
        {
            string id=ids[i];
            try
            {
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,id+"-before.json")));
                string prompt=File.ReadAllText(Path.Combine(native,id+"-prompt.txt"));
                File.WriteAllText(Path.Combine(output,id+"-request.txt"),requests[i]);
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var cmd=ProviderResponse.Command(response);string action=cmd.GetString("action");bool intended=false,preserved=true;
                if(i>=6 && i<=8)intended=action=="ask" && !string.IsNullOrWhiteSpace(cmd.GetString("message"));
                else if(action=="generate")
                {
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                    if(i==0)intended=Fills(after,"LavaDeep") && !Fills(after,"WaterDeep") && after.elevationShapes.Count==2;
                    if(i==1)intended=after.structures.Sum(p=>p.count)==2 && after.structures.All(p=>p.kind=="ruin"&&p.region=="island");
                    if(i==2){var center=after.elevationShapes.Single(s=>s.id=="island").compositeShapes[0].GetCenter();intended=Math.Abs(center.x-.7)<.001 && Math.Abs(center.y-.65)<.001;}
                    if(i==3)intended=after.structures.Single().count==1;
                    if(i==4)intended=after.structures.Count==0 && after.elevationShapes.Count==1 && after.elevationShapes[0].id=="moat";
                    if(i==5)intended=Fills(after,"VolcanicRock") && !Fills(after,"WaterDeep");
                    if(i==9)intended=Fills(after,"LavaDeep") && Fills(after,"Soil") && !Fills(after,"WaterDeep") && after.structures.Sum(p=>p.count)==1 && after.structures.All(p=>p.region!=null);
                    var reverted=after.Clone();
                    if(i==0 || i==2 || i==4 || i==5)reverted.elevationShapes=before.elevationShapes;
                    if(i==1 || i==3 || i==4)reverted.structures=before.structures;
                    if(i!=9)preserved=MapStateCodec.Serialize(before)==MapStateCodec.Serialize(reverted);
                    if(i==0 || i==5)preserved &= SimpleJson.Serialize(before.elevationShapes.Single(s=>s.id=="island"))==SimpleJson.Serialize(after.elevationShapes.Single(s=>s.id=="island"));
                    File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                }
                all &= intended && preserved;
                results.Add(new{id,action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{id}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception e){all=false;results.Add(new{id,error=e.Message});Console.WriteLine(id+": "+e.Message);}
            File.WriteAllText(Path.Combine(output,"text-region-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="10 independent Korean requests using native captured production prompts; real dialog/generation replay tested separately."}));
        }
        return all?0:1;
    }
}
