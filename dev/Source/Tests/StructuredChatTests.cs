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
