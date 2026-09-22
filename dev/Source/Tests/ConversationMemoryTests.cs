using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using static CoreRegressionTests;

static class ConversationMemoryTests
{
    static List<ChatMessage> LongHistory()
    {
        var result=new List<ChatMessage>();
        for(int i=0;i<10;i++){result.Add(new ChatMessage("user","request "+i+new string('u',590)));result.Add(new ChatMessage("assistant","APPLIED\n"+new string('a',590)));}
        result.Add(new ChatMessage("user","아니 완전한 십자가 모양. 십자가가 만나는 점이 맵의 중심"));return result;
    }
    static string Summary="{\"summary\":\"Keep the lake. Existing DirtPath roads cross at the center; use bridges.\"}";
    public static void RunAll()
    {
        Check("Short conversation keeps every user and applied response without a summary call",()=>
        {
            var raw=new List<ChatMessage>{new ChatMessage("user","흙길을 깔아줘"),new ChatMessage("assistant","APPLIED\n{}"),new ChatMessage("user","완전한 십자가로")};int calls=0;
            var p=ConversationMemory.PrepareAsync(raw,"current state",null,32768,(h,s,t)=>{calls++;return Task.FromResult(Summary);}).Result;
            Equal(0,calls);Equal(3,p.Messages.Count);Equal(raw[0].Content,p.Messages[0].Content);Equal(raw[2].Content,p.Messages[2].Content);
            p.Messages[0].Content="changed snapshot";Equal("흙길을 깔아줘",raw[0].Content);
        });
        Check("80 percent compacts old turns and keeps recent turns and raw original",()=>
        {
            var raw=LongHistory();string first=raw[0].Content;int calls=0;
            var p=ConversationMemory.PrepareAsync(raw,"current state",null,4096,(h,s,t)=>{calls++;Equal(true,s.Contains("NOT APPLIED"));return Task.FromResult(Summary);}).Result;
            Equal(true,calls>0);Equal(16,p.Memory.Covered);Equal(21,raw.Count);Equal(first,raw[0].Content);
            Equal(raw[16].Content,p.Messages[2].Content);Equal(raw.Last().Content,p.Messages.Last().Content);
            Equal(true,p.InputTokens<=4096*.6);
        });
        Check("Remote exact count below threshold overrides an oversized local estimate",()=>
        {
            int calls=0,counts=0;
            var p=ConversationMemory.PrepareAsync(LongHistory(),"current state",null,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);},
                (h,s,t)=>{counts++;return Task.FromResult<int?>(3276);}).Result;
            Equal(0,calls);Equal(1,counts);Equal(true,p.ExactCount);Equal(3276,p.InputTokens);
        });
        Check("Remote exact count at threshold triggers summary and recount",()=>
        {
            int calls=0,counts=0;
            var p=ConversationMemory.PrepareAsync(LongHistory(),"current state",null,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);},
                (h,s,t)=>Task.FromResult<int?>(++counts==1?3277:2000)).Result;
            Equal(true,calls>0);Equal(3,counts);Equal(2000,p.InputTokens);
        });
        Check("Existing checkpoint avoids repeat summaries until pressure recurs",()=>
        {
            var raw=LongHistory();var p=ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult(Summary)).Result;int calls=0;
            raw.Add(new ChatMessage("assistant","APPLIED\n{}"));raw.Add(new ChatMessage("user","고마워"));
            var q=ConversationMemory.PrepareAsync(raw,"state",p.Memory,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);}).Result;
            Equal(0,calls);Equal(p.Memory,q.Memory);Equal("고마워",q.Messages.Last().Content);
        });
        Check("Summary failure below hard budget retains every raw message",()=>
        {
            int counts=0;var raw=LongHistory();var p=ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult("{\"action\":\"generate\",\"params\":{}}"),
                (h,s,t)=>Task.FromResult<int?>(++counts==1?3500:1800)).Result;
            Equal(null,p.Memory);Equal(raw.Count,p.Messages.Count);Equal(true,p.Warning!=null);Equal(21,raw.Count);
        });
        Check("Summary failure over hard budget never falls through to an oversized edit",()=>
        {
            var raw=LongHistory();bool rejected=false;
            try{ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult("bad")).GetAwaiter().GetResult();}
            catch(InvalidOperationException){rejected=true;}Equal(true,rejected);Equal(21,raw.Count);
        });
        Check("Cancellation during summary preserves raw conversation and propagates",()=>
        {
            var raw=LongHistory();bool cancelled=false;using(var c=new CancellationTokenSource())
            {
                try{ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>{c.Cancel();return Task.FromResult(Summary);},null,c.Token).GetAwaiter().GetResult();}
                catch(OperationCanceledException){cancelled=true;}
            }
            Equal(true,cancelled);Equal(21,raw.Count);
        });
        Check("Reset and changed prefix cannot resurrect stale summary",()=>
        {
            var raw=LongHistory();var p=ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult(Summary)).Result;
            Equal(0,ConversationMemory.Pack(new List<ChatMessage>(),p.Memory).Count);
            raw[0].Content="different session";var packed=ConversationMemory.Pack(raw,p.Memory);
            Equal(raw.Count,packed.Count);Equal("different session",packed[0].Content);
        });
        Check("Fixed prompt pressure does not repeatedly summarize nonexistent older turns",()=>
        {
            int calls=0;var raw=new List<ChatMessage>{new ChatMessage("user","hello")};
            var p=ConversationMemory.PrepareAsync(raw,new string('s',7100),null,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);}).Result;
            Equal(0,calls);Equal(1,p.Messages.Count);
        });
        Check("Unknown fallback estimate cannot block a previously working short first request",()=>
        {
            int calls=0;var raw=new List<ChatMessage>{new ChatMessage("user","hello")};
            var p=ConversationMemory.PrepareAsync(raw,new string('s',10000),null,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);},null,default,false).Result;
            Equal(0,calls);Equal(1,p.Messages.Count);
        });
        Check("Large fixed context and tiny old prefix do not cause repeated paid summaries",()=>
        {
            int calls=0,counts=0;var raw=LongHistory();
            var p=ConversationMemory.PrepareAsync(raw,"large catalog",null,4096,(h,s,t)=>{calls++;return Task.FromResult(Summary);},
                (h,s,t)=>Task.FromResult<int?>(++counts==1?3500:3300)).Result;
            Equal(0,calls);Equal(raw.Count,p.Messages.Count);Equal(null,p.Memory);
        });
        Check("Changing count availability cannot disguise a larger summary",()=>
        {
            var raw=new List<ChatMessage>();for(int i=0;i<4;i++){raw.Add(new ChatMessage("user","x"));raw.Add(new ChatMessage("assistant","y"));}
            raw.Add(new ChatMessage("user",new string('z',10000)));int counts=0;
            var p=ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult("{\"summary\":\""+new string('a',900)+"\"}"),
                (h,s,t)=>Task.FromResult<int?>(++counts==1?(int?)null:1000),default,false).Result;
            Equal(null,p.Memory);Equal(true,p.Warning!=null);Equal(raw.Count,p.Messages.Count);
        });
        Check("Oversized repair output is retained and blocked before another edit call",()=>
        {
            var raw=new List<ChatMessage>{new ChatMessage("user","road"),new ChatMessage("assistant",new string('x',10000)),new ChatMessage("user",StructuredChat.RepairInstruction)};
            bool blocked=false;try{ConversationMemory.PrepareAsync(raw,"state",null,4096,(h,s,t)=>Task.FromResult(Summary)).GetAwaiter().GetResult();}
            catch(InvalidOperationException){blocked=true;}Equal(true,blocked);Equal(10000,raw[1].Content.Length);
        });
        Check("Unicode and fixed instructions count toward the same budget",()=>
        {
            Equal(true,ConversationMemory.Estimate(new string('한',1000),new ChatMessage[0])>ConversationMemory.Estimate(new string('a',1000),new ChatMessage[0]));
            Equal(true,ConversationMemory.Estimate("state",new[]{new ChatMessage("user","hello")})>ConversationMemory.Estimate("",new ChatMessage[0]));
        });
        Check("Summary checkpoint cannot survive a cancelled queued request",()=>
        {
            var gate=new RequestGate();var ticket=gate.Begin();var checkpoint=new ConversationMemory.Checkpoint{Covered=5,Summary="old"};
            gate.Complete(ticket,"reply",null,checkpoint);gate.Cancel();Equal(null,gate.Take());
        });
    }
}
