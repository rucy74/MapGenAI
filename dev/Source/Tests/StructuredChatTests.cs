using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using static CoreRegressionTests;

static class StructuredChatTests
{
    const string Ask="{\"action\":\"ask\",\"message\":\"해당 모드가 현재 로드되지 않았습니다.\"}";
    public static void RunAll()
    {
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
