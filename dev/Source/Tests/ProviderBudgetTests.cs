using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.UI;
using static CoreRegressionTests;

static class ProviderBudgetTests
{
    public static void RunAll()
    {
        Check("Input-only metadata keeps output separate and leaves safety headroom",()=>
        {
            var budget=ContextBudget.FromLimits(100000,0,16384,"metadata");
            Equal(98000,budget.InputTokens);Equal(true,budget.Known);Equal("metadata",budget.Source);
            Equal(384,ContextBudget.FromLimits(512,0,16384,"small").InputTokens);
        });
        Check("Combined context budgets reserve completion room as well as input safety",()=>
        {
            Equal(30976,ContextBudget.FromLimits(80000,40000,8000,"combined").InputTokens);
            Equal(48976,ContextBudget.FromLimits(50000,100000,8000,"both").InputTokens);
            Equal(1,ContextBudget.FromLimits(8000,8000,16000,"insufficient").InputTokens);
        });
        Check("Unknown capacity is labelled fallback and callers cannot mutate the shared default",()=>
        {
            var first=ContextBudget.Fallback;first.InputTokens=5;
            var next=ContextBudget.FromLimits(0,0,0,"unknown");
            Equal(32768,next.InputTokens);Equal(false,next.Known);Equal("fallback",next.Source);
        });
        Check("Model-list limits reach the matching model and credentials without another lookup",()=>
        {
            const string key="offline-budget-fixture",model="budget-test-model";
            ProviderContextBudgets.RegisterGeminiModels(key,"{\"models\":[{\"name\":\"models/budget-test-model\",\"inputTokenLimit\":100000,\"outputTokenLimit\":4096}]}");
            var budget=ProviderContextBudgets.GeminiAsync(key,model,CancellationToken.None).GetAwaiter().GetResult();
            Equal(98000,budget.InputTokens);Equal(true,budget.Known);
            Equal(4096,ProviderContextBudgets.GeminiOutputTokens(key,model,16384));
            Equal(2048,ProviderContextBudgets.GeminiOutputTokens(key,model,2048));
            Equal(16384,ProviderContextBudgets.GeminiOutputTokens(key,"another-model",16384));
            Equal(16384,ProviderContextBudgets.GeminiOutputTokens("another-credential",model,16384));
            budget.InputTokens=1;
            Equal(98000,ProviderContextBudgets.GeminiAsync(key,model,CancellationToken.None).GetAwaiter().GetResult().InputTokens);
        });
        Check("Malformed optional model metadata does not prevent registering valid models",()=>
        {
            const string key="offline-budget-malformed";
            ProviderContextBudgets.RegisterGeminiModels(key,"not json");
            ProviderContextBudgets.RegisterGeminiModels(key,"{\"models\":[{\"name\":\"models/bad\",\"inputTokenLimit\":1.5},{\"name\":\"models/good\",\"inputTokenLimit\":50000,\"outputTokenLimit\":8192}]}");
            Equal(48976,ProviderContextBudgets.GeminiAsync(key,"good",CancellationToken.None).GetAwaiter().GetResult().InputTokens);
            Equal(16384,ProviderContextBudgets.GeminiOutputTokens(key,"bad",16384));
        });
        Check("Token counting includes system instructions, every turn and Unicode text",()=>
        {
            var history=new List<ChatMessage>{new ChatMessage("user","온천 \"여기\""),new ChatMessage("assistant","{\"action\":\"message\"}"),new ChatMessage("user","계속")};
            var request=SimpleJson.Parse(ProviderContextBudgets.CountRequest("budget-model",history,"상태: 강을 유지\n규칙"));
            Equal(false,request.ContainsKey("contents"));
            var generation=request.GetObject("generateContentRequest");
            Equal("models/budget-model",generation.GetString("model"));
            Equal("상태: 강을 유지\n규칙",generation.GetObject("system_instruction").GetObjectArray("parts")[0].GetString("text"));
            var turns=generation.GetObjectArray("contents");Equal(3,turns.Count);
            Equal("user",turns[0].GetString("role"));Equal("model",turns[1].GetString("role"));
            Equal(history[0].Content,turns[0].GetObjectArray("parts")[0].GetString("text"));
            Equal(history[1].Content,turns[1].GetObjectArray("parts")[0].GetString("text"));
            Equal("계속",turns[2].GetObjectArray("parts")[0].GetString("text"));
        });
        Check("Concurrent metadata requests load once and cache failures without repeated traffic",()=>
        {
            var cache=new ProviderBudgetCache();int calls=0;
            var pending=new TaskCompletionSource<GeminiBudgetInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken,Task<GeminiBudgetInfo>> load=token=>{Interlocked.Increment(ref calls);return pending.Task;};
            var first=cache.GetAsync("same",load,CancellationToken.None);
            var second=cache.GetAsync("same",load,CancellationToken.None);
            pending.SetResult(new GeminiBudgetInfo());
            Task.WhenAll(first,second).GetAwaiter().GetResult();
            Equal(false,cache.GetAsync("same",load,CancellationToken.None).GetAwaiter().GetResult().Budget.Known);
            Equal(1,calls);
        });
        Check("Cancelled metadata waiters preserve other callers and cancellation is not cached",()=>
        {
            var cache=new ProviderBudgetCache();int calls=0;
            var pending=new TaskCompletionSource<GeminiBudgetInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first=cache.GetAsync("shared",token=>{calls++;return pending.Task;},CancellationToken.None);
            using(var cancelled=new CancellationTokenSource())
            {
                var waiter=cache.GetAsync("shared",token=>{calls++;return pending.Task;},cancelled.Token);
                cancelled.Cancel();ExpectCancelled(()=>waiter.GetAwaiter().GetResult());
            }
            pending.SetResult(new GeminiBudgetInfo{Budget=ContextBudget.FromLimits(50000,0,0,"fixture")});
            Equal(48976,first.GetAwaiter().GetResult().Budget.InputTokens);Equal(1,calls);
            ExpectCancelled(()=>cache.GetAsync("retry",token=>Task.FromCanceled<GeminiBudgetInfo>(new CancellationToken(true)),CancellationToken.None).GetAwaiter().GetResult());
            Equal(true,cache.Read("retry")==null);
            Equal(32768,cache.GetAsync("retry",token=>Task.FromResult(new GeminiBudgetInfo()),CancellationToken.None).GetAwaiter().GetResult().Budget.InputTokens);
        });
        Check("Refreshing model metadata wins over an in-flight failed lookup",()=>
        {
            var cache=new ProviderBudgetCache();var pending=new TaskCompletionSource<GeminiBudgetInfo>();
            var request=cache.GetAsync("refresh",token=>pending.Task,CancellationToken.None);
            cache.Register("refresh",new GeminiBudgetInfo{Budget=ContextBudget.FromLimits(100000,0,0,"refresh")});
            pending.SetResult(new GeminiBudgetInfo());
            Equal(98000,request.GetAwaiter().GetResult().Budget.InputTokens);
        });
        Check("Cancelled public budget and token requests do not attempt network calls",()=>
        {
            var token=new CancellationToken(true);
            ExpectCancelled(()=>ProviderContextBudgets.GeminiAsync("unused","unused",token).GetAwaiter().GetResult());
            ExpectCancelled(()=>ProviderContextBudgets.CountGeminiAsync("unused","unused",new List<ChatMessage>(),"",token).GetAwaiter().GetResult());
        });
    }
    static void ExpectCancelled(Action action)
    {
        try{action();}catch(OperationCanceledException){return;}
        throw new Exception("Expected cancellation");
    }
}
