using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class StructuredChatTests
{
    const string Ask="{\"action\":\"ask\",\"message\":\"해당 모드가 현재 로드되지 않았습니다.\"}";
    public static void RunAll()
    {
        Check("Candidate refinements compose privately and preserve other candidates and original map",()=>
        {
            var before=new TileMapState{animalDensity=1.2f};string original=MapStateCodec.Serialize(before);
            var plans=RecommendationPlan.Validate(SimpleJson.Parse("{\"options\":[{\"params\":{\"fertility_offset\":0.2}},{\"params\":{\"vegetation_density\":1.3}},{\"params\":{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"exit\",\"type\":\"passage\",\"points\":[[0,0.5],[1,0.5]],\"width\":12,\"scope\":\"mountains\",\"fill\":\"Soil\"}}]}}]}"),before,data=>{},true);
            string first=MapStateCodec.Serialize(plans[0].Resolve(before)),second=MapStateCodec.Serialize(plans[1].Resolve(before));
            var old=plans[2];
            var change=SimpleJson.Parse("{\"shape_ops\":[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"edge_roughness\":\"medium\"}}]}");
            int count=0;plans[2]=RecommendationPlan.Refine(plans,3,change,before,edits=>count=edits.Count,true);
            Equal(2,count);Equal(null,old.Resolve(before).elevationShapes[0].edge_roughness);
            var rough=plans[2].Resolve(before);Equal("medium",rough.elevationShapes[0].edge_roughness);Equal(1.2f,rough.animalDensity);
            Equal(first,MapStateCodec.Serialize(plans[0].Resolve(before)));Equal(second,MapStateCodec.Serialize(plans[1].Resolve(before)));Equal(original,MapStateCodec.Serialize(before));
            plans[2]=RecommendationPlan.Refine(plans,3,SimpleJson.Parse("{\"shape_ops\":[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"width\":16}}]}"),before,edits=>{},false);
            Equal(16,plans[2].Resolve(before).elevationShapes[0].width);Equal("medium",plans[2].Resolve(before).elevationShapes[0].edge_roughness);
            string saved=MapStateCodec.Serialize(plans[2].Resolve(before));
            Throws(()=>RecommendationPlan.Refine(plans,3,change,before,edits=>throw new FormatException("native conflict"),true));
            Throws(()=>RecommendationPlan.Refine(plans,4,change,before,edits=>{},true));
            Throws(()=>RecommendationPlan.Refine(plans,3,SimpleJson.Parse("{}"),before,edits=>{},true));
            Equal(saved,MapStateCodec.Serialize(plans[2].Resolve(before)));
            Equal(true,RecommendationPlan.RequestsDirectEdit("추천 말고 현재 맵에 바로 산 만들어줘"));
            Equal(false,RecommendationPlan.RequestsDirectEdit("3번 통로만 자연스럽게"));
        });
        Check("Candidate revision envelope validates number and parameters without allowing immediate recommendation edits",()=>
        {
            StructuredChat.ValidateEnvelope("{\"action\":\"revise\",\"option\":3,\"params\":{\"fertility_offset\":0.2}}");
            foreach(string option in new[]{"0","4","1.5","null"})Throws(()=>StructuredChat.ValidateEnvelope("{\"action\":\"revise\",\"option\":"+option+",\"params\":{}}"));
            Throws(()=>StructuredChat.ValidateEnvelope("{\"action\":\"revise\",\"option\":1}"));
        });
        Check("Recommendation options are independent stored patches with authoritative summaries",()=>
        {
            var before=new TileMapState();string original=MapStateCodec.Serialize(before);int calls=0;
            var cmd=SimpleJson.Parse("{\"action\":\"recommend\",\"options\":[{\"title\":\"FAKE LAVA\",\"params\":{\"fertility_offset\":0.2}},{\"params\":{\"vegetation_density\":1.3}}]}");
            var plans=RecommendationPlan.Validate(cmd,before,data=>{calls++;},true);
            Equal(2,calls);Equal(2,plans.Count);Equal(original,MapStateCodec.Serialize(before));
            Equal(false,plans[0].Summary.Contains("FAKE LAVA"));
            var second=MapStateEditor.Merge(before,MapParameterParser.Parse(ProviderResponse.Command(plans[1].Command).GetObject("params")));
            Equal(before.fertilityOffset,second.fertilityOffset);
            cmd.GetObjectArray("options")[1].GetObject("params").SetString("vegetation_density","4");
            Equal(false,plans[1].Command.Contains("\"4\""));
        });
        Check("One invalid recommendation rejects the entire batch without mutation",()=>
        {
            int calls=0;var before=new TileMapState();string original=MapStateCodec.Serialize(before);
            Throws(()=>RecommendationPlan.Validate(SimpleJson.Parse("{\"options\":[{\"params\":{\"fertility_offset\":0.2}},{\"params\":{\"vegetation_density\":1.3}}]}"),before,data=>{if(++calls==2)throw new FormatException("Conflict");},true));
            Equal(2,calls);Equal(original,MapStateCodec.Serialize(before));
        });
        Check("Recommendation schema bounds and empty changes are rejected",()=>
        {
            foreach(var json in new[]{"{}","{\"options\":[]}","{\"options\":[{}]}","{\"options\":[{\"params\":{}},{\"params\":{}},{\"params\":{}},{\"params\":{}}]}"})Throws(()=>RecommendationPlan.Options(SimpleJson.Parse(json)));
            Throws(()=>RecommendationPlan.Validate(SimpleJson.Parse("{\"options\":[{\"params\":{}}]}"),new TileMapState(),data=>{},true));
        });
        Check("Recorded recommendations stay independent and duplicate outcomes reject atomically",()=>
        {
            var before=new TileMapState();string saved=MapStateCodec.Serialize(before);
            var recorded=ProviderResponse.Command(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"recommendation-fixtures","plain-response.json")));
            Equal(3,RecommendationPlan.Validate(recorded,before,data=>{},true).Count);
            var options=recorded.GetObjectArray("options");options[1]=options[0];
            recorded.SetObjectArray("options",options);
            Throws(()=>RecommendationPlan.Validate(recorded,before,data=>{},true));
            Equal(saved,MapStateCodec.Serialize(before));
            // Different patches with the same effective state must not become separate choices.
            Throws(()=>RecommendationPlan.Validate(SimpleJson.Parse("{\"options\":[{\"params\":{\"fertility_offset\":0.2}},{\"params\":{\"vegetation_density\":1,\"fertility_offset\":0.2}}]}"),before,data=>{},false));
        });
        foreach(string language in new[]{"ko","en"})Check("Recorded "+language+" landscape choices replay every stored native outcome without changing source",()=>
        {
            string folder=Path.Combine(AppContext.BaseDirectory,"recommendation-fixtures",language);
            var names=language=="ko"?new[]{"plain","coast","existing","scoped","two"}:new[]{"plain","existing"};
            foreach(string name in names)
            {
                string id="recommend-"+name;
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(folder,id+"-before.json")));
                string saved=MapStateCodec.Serialize(before);
                var command=ProviderResponse.Command(File.ReadAllText(Path.Combine(folder,id+"-response.json")));
                var plans=RecommendationPlan.Validate(command,before,data=>MapStateValidation.Validate(MapStateEditor.Merge(before,data)),language=="ko");
                Equal(name=="two"?2:3,plans.Count);
                for(int i=0;i<plans.Count;i++)
                {
                    var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(folder,id+"-option-"+(i+1)+"-after.json")));
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(ProviderResponse.Command(plans[i].Command).GetObject("params")));
                    Equal(MapStateCodec.Serialize(expected),MapStateCodec.Serialize(after));
                }
                Equal(saved,MapStateCodec.Serialize(before));
            }
        });
        Check("Selection accepts only a specific number without silently dropping edits",()=>
        {
            foreach(var text in new[]{"1","1번","1번으로 해줘","option 1 please"})Equal(1,RecommendationPlan.Selection(text));
            foreach(var text in new[]{"1번이랑 2번","1번 대신 2번","1번에 용암 추가","그래","모두"})Equal(-1,RecommendationPlan.Selection(text));
            Equal(true,RecommendationPlan.IsAmbiguousAcceptance("그래"));
            Equal(false,RecommendationPlan.IsAmbiguousAcceptance("알아서 해줘"));
            Equal(true,RecommendationPlan.IsRequest("그냥 추천해 봐"));
            Equal(false,RecommendationPlan.IsRequest("추천 말고 바로 생성해 줘"));
        });
        Check("Unchecked numbered recommendations cannot pass the chat envelope",()=>
        {
            Throws(()=>StructuredChat.ValidateEnvelope("{\"action\":\"ask\",\"message\":\"추천합니다.\\n\\n1. 온천과 연못\\n2. 협곡\"}"));
            Throws(()=>StructuredChat.ValidateEnvelope("{\"action\":\"generate\",\"params\":{}}",true));
            Throws(()=>StructuredChat.ValidateEnvelope(Ask,true));
            StructuredChat.ValidateEnvelope("{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.2}}]}",true);
        });
        Check("Recommendation requests repair prose once and still require a plan",()=>
        {
            int calls=0;string expected="{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.2}}]}";
            Equal(expected,StructuredChat.SendAsync((bad,token)=>Task.FromResult(++calls==1?Ask:expected),default,true).GetAwaiter().GetResult());Equal(2,calls);
        });
        Check("Valid chat commands make one provider call",()=>
        {
            foreach(string reply in new[]{Ask,"{\"action\":\"generate\",\"params\":{\"mutators\":[\"HotSprings\"]}}"})
            {
                int calls=0;Equal(reply,StructuredChat.SendAsync((invalid,token)=>{calls++;Equal(null,invalid);return Task.FromResult(reply);}).GetAwaiter().GetResult());Equal(1,calls);
            }
        });
        Check("Plain prose and bad envelopes are repaired once without accepting prose as a command",()=>
        {
            foreach(string first in new[]{"네, 현재 설치된 모드 목록에는 없습니다.",null,"{\"action\":\"ask\"}","{\"action\":\"generate\"}"})
            {
                int calls=0;string reply=StructuredChat.SendAsync((invalid,token)=>{calls++;if(calls==1){Equal(null,invalid);return Task.FromResult(first);}Equal(first??"[empty response]",invalid);return Task.FromResult(Ask);}).GetAwaiter().GetResult();
                Equal(Ask,reply);Equal(2,calls);
            }
        });
        Check("Repeated malformed chat stops after two calls with no parsed command",()=>
        {
            int calls=0;Throws(()=>StructuredChat.SendAsync((invalid,token)=>{calls++;return Task.FromResult("일반 문장만 있는 응답");}).GetAwaiter().GetResult());Equal(2,calls);
        });
        Check("Cancellation and transport errors do not trigger format repair",()=>
        {
            int calls=0;bool cancelled=false;using(var cancellation=new CancellationTokenSource())
            {
                try{StructuredChat.SendAsync((invalid,token)=>{calls++;cancellation.Cancel();return Task.FromResult("plain text");},cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            }
            Equal(true,cancelled);Equal(1,calls);calls=0;bool failed=false;
            try{StructuredChat.SendAsync((invalid,token)=>{calls++;throw new IOException("transport failed");}).GetAwaiter().GetResult();}catch(IOException){failed=true;}
            Equal(true,failed);Equal(1,calls);
        });
    }
}
