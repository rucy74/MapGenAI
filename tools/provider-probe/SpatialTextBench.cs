using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

static class SpatialTextBench
{
    public static async Task<int> Run(GeminiClient client,string native,string output)
    {
        string[] ids={"river-banks","foothills","island-edge","water-side","missing-river"};
        string[] requests={
            "실제 강의 동쪽과 서쪽에 작은 폐허를 각각 2개씩 놓아줘. 원래 크기는 가로11 세로7칸으로 하고 90도 회전해줘. 강에서 가장 가까운 건물 칸까지 2~12칸, 폐허끼리 최소18칸 떨어뜨려줘. 다른 지형·설정은 유지해.",
            "산의 서쪽 기슭에 가로9 세로7칸 폐허를 2개 놓아줘. 산 바위에서 가장 가까운 건물 칸까지 2~10칸, 폐허 사이 최소10칸을 지켜줘. 산과 다른 지형·설정은 그대로.",
            "가운데 섬의 안쪽 가장자리에 가로9 세로7칸 폐허를 3개 놓아줘. 가장자리에서 2~5칸 안쪽으로 떨어지고 폐허 사이 최소8칸 간격을 지켜줘. 건물 전체가 섬 안에 있어야 해. 다른 지형·설정은 그대로.",
            "호수 북쪽 물가에 가로9 세로7칸 폐허를 2개 놓아줘. 물에서 가장 가까운 건물 칸까지 2~8칸, 폐허끼리 최소10칸 떨어뜨려줘. 다른 지형·설정은 그대로.",
            "강이 없는 이 타일에 강을 새로 만들지는 말고, 강가에 폐허를 배치해줘. 조건이 안 되면 현재 상태를 유지하고 설명해."};
        var results=new List<object>();bool all=true;
        for(int i=0;i<ids.Length;i++)
        {
            string id=ids[i];var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(native,id+"-before.json")));
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));File.WriteAllText(Path.Combine(output,id+"-request.txt"),requests[i]);
            string prompt=File.ReadAllText(Path.Combine(native,id+"-prompt.txt"));File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                string response=await client.SendChatAsync(new List<ChatMessage>{new ChatMessage("user",requests[i])},prompt,timeout.Token);
                File.WriteAllText(Path.Combine(output,id+"-response.json"),response);var cmd=ProviderResponse.Command(response);string action=cmd.GetString("action");bool intended=false,preserved=true;
                if(i==4)intended=action=="ask" && !string.IsNullOrWhiteSpace(cmd.GetString("message"));
                else if(action=="generate")
                {
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));MapStateValidation.Validate(after);
                    string target=new[]{"river","mountain","region_edge","water"}[i],side=new[]{"","west","any","north"}[i];int[] max={12,10,5,8},gap={18,10,8,10};
                    intended=after.structures.Sum(p=>p.count)==(i==0?4:i==2?3:2) && after.structures.All(p=>p.kind=="ruin" && p.width==(i==0?11:9) && p.height==7 && p.rotation==(i==0?90:0) &&
                        p.relation!=null && p.relation.target==target && p.relation.min_distance==2 && p.relation.max_distance==max[i] && p.spacing==gap[i] && (i==0 || p.relation.side==side) && (i!=2 || p.region=="island"));
                    if(i==0)intended &= after.structures.Where(p=>p.relation?.side=="east").Sum(p=>p.count)==2 && after.structures.Where(p=>p.relation?.side=="west").Sum(p=>p.count)==2;
                    var clean=after.Clone();clean.structures=before.structures;preserved=MapStateCodec.Serialize(clean)==MapStateCodec.Serialize(before);
                    File.WriteAllText(Path.Combine(output,id+"-after.json"),MapStateCodec.Serialize(after));
                }
                all &= intended && preserved;results.Add(new{id,action,intended,preserved,modelVersion=client.LastModelVersion,inputTokens=client.LastInputTokens,outputTokens=client.LastOutputTokens});
                Console.WriteLine($"{id}: action={action}, intended={intended}, preserved={preserved}");
            }
            catch(Exception e){all=false;results.Add(new{id,error=e.Message});Console.WriteLine(id+": "+e.Message);}
            File.WriteAllText(Path.Combine(output,"spatial-results.json"),System.Text.Json.JsonSerializer.Serialize(new{ok=all,results,scope="Five independent Korean requests against captured native production prompts; actual replay and generation checked separately."}));
        }
        return all?0:1;
    }
}

