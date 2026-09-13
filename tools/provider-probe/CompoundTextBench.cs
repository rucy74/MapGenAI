using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class CompoundTextBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        var state=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,"ruin-count-before.json")));
        state.elevationShapes.Single(s=>s.id=="moat").compositeOps[0].fill="water";
        string template=File.ReadAllText(Path.Combine(native,"ruin-count-prompt.txt"));
        int start=template.IndexOf("\n## 현재 적용된 파라미터",StringComparison.Ordinal),end=template.IndexOf("\n규칙:",start,StringComparison.Ordinal);
        if(start<0 || end<0)throw new InvalidOperationException("Native current-state section not found");
        template=template.Substring(0,start)+"\nMAPGENAI_CURRENT_STATE_PLACEHOLDER\n"+template.Substring(end);
        string[] requests={
            "가운데 섬과 해자의 모양은 그대로 두고, 해자의 물을 실제 용암으로 바꿔줘. 섬 안의 폐허는 가로 9칸 세로 7칸짜리 3개로 바꿔줘. 다른 설정은 그대로 유지해.",
            "그 섬과 주변 해자를 함께 [0.62,0.58]로 이동해줘. 폐허 세 개는 계속 그 섬 안에 있도록 해줘. 크기와 재료, 다른 설정은 유지해.",
            "해자 경계만 많이 자연스럽게 울퉁불퉁하게 해줘. 섬과 폐허, 위치, 재료는 그대로 유지해.",
            "이번에는 해자의 용암만 식은 용암으로 바꾸고, 섬의 폐허는 같은 크기로 하나만 남겨줘. 나머지 모양과 위치, 설정은 그대로 둬.",
            "식생 밀도만 0.7, 동물 밀도만 0.4로 바꿔줘. 지금까지 만든 지형과 폐허는 모두 그대로 둬.",
            "그 가운데 섬과 거기에 연결한 폐허를 함께 삭제해줘. 주변 식은 용암 해자와 식생·동물 밀도는 그대로 유지해.",
            "지금 남은 해자를 초콜릿 지형으로 바꿔줘. 지원하지 않으면 다른 재료로 대체하지 말고 이유를 설명해.",
            "이 내륙 타일에 바다 해안을 추가해줘. 안 되는 조건이면 현재 맵은 유지하고 설명해."};
        var results=new List<object>();bool all=true;
        for(int i=0;i<requests.Length;i++)
        {
            string id="compound-"+(i+1).ToString("00"),current;
            using(GenerationContext.Enter(1,state))current=MapGenParams.BuildCurrentParamsText(true);
            string prompt=template.Replace("MAPGENAI_CURRENT_STATE_PLACEHOLDER",current);
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(state));
            File.WriteAllText(Path.Combine(output,id+"-request.txt"),requests[i]);
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);
                var cmd=ProviderResponse.Command(response);string action=cmd.GetString("action");bool intended=false,preserved=true;
                if(i>=6)intended=action=="ask" && !string.IsNullOrWhiteSpace(cmd.GetString("message"));
                else if(action=="generate")
                {
                    var after=MapStateEditor.Merge(state,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                    var clean=after.Clone();
                    var moat=after.elevationShapes.Single(s=>s.id=="moat");
                    var island=after.elevationShapes.Find(s=>s.id=="island");
                    if(i==0 || i==3)
                    {
                        intended=Material(moat)==(i==0?"LavaDeep":"CooledLava") && after.structures.Count==1 && after.structures[0].region=="island" &&
                            after.structures[0].width==9 && after.structures[0].height==7 && after.structures[0].count==(i==0?3:1);
                        var oldMoat=state.elevationShapes.Single(s=>s.id=="moat");var geometry=moat.Clone();geometry.fill=oldMoat.fill;
                        for(int j=0;j<geometry.compositeOps.Count;j++)geometry.compositeOps[j].fill=oldMoat.compositeOps[j].fill;
                        preserved=SimpleJson.Serialize(geometry)==SimpleJson.Serialize(oldMoat) && SimpleJson.Serialize(island)==SimpleJson.Serialize(state.elevationShapes.Single(s=>s.id=="island"));
                        clean.elevationShapes=state.elevationShapes;clean.structures=state.structures;
                    }
                    if(i==1)
                    {
                        intended=after.elevationShapes.Count==2 && after.elevationShapes.All(s=>s.compositeShapes.All(p=>Math.Abs(p.GetCenter().x-.62f)<.001 && Math.Abs(p.GetCenter().y-.58f)<.001));
                        var expected=MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(@"{""shape_ops"":[{""op"":""move"",""id"":""moat"",""position"":[0.62,0.58]},{""op"":""move"",""id"":""island"",""position"":[0.62,0.58]}]}")));
                        preserved=MapStateCodec.Serialize(after)==MapStateCodec.Serialize(expected);clean.elevationShapes=state.elevationShapes;
                    }
                    if(i==2){intended=moat.edge_roughness=="high";clean.elevationShapes.Single(s=>s.id=="moat").edge_roughness=state.elevationShapes.Single(s=>s.id=="moat").edge_roughness;}
                    if(i==4){intended=Math.Abs(after.vegetationDensity-.7f)<.001 && Math.Abs(after.animalDensity-.4f)<.001;clean.vegetationDensity=state.vegetationDensity;clean.animalDensity=state.animalDensity;}
                    if(i==5){intended=after.elevationShapes.Count==1 && island==null && after.structures.Count==0;preserved=SimpleJson.Serialize(moat)==SimpleJson.Serialize(state.elevationShapes.Single(s=>s.id=="moat"));clean.elevationShapes=state.elevationShapes;clean.structures=state.structures;}
                    preserved &= MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(state);
                    state=after;
                }
                all &= intended && preserved;
                results.Add(new{id,action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{id}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception e){all=false;results.Add(new{id,error=e.Message});Console.WriteLine(id+": "+e.Message);}
            File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(state));
            File.WriteAllText(Path.Combine(output,"compound-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Eight sequential Korean requests; exact native prompt template with production current-state section refreshed every turn. Native response replay and map generation are separate checks."}));
        }
        return all?0:1;
    }
    static string Material(ElevationShape s)=>TerrainMaterials.DefName(s.fill??s.compositeOps.First().fill);
}
