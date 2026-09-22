using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    public sealed class ContextBudget
    {
        public int InputTokens;
        public string Source;
        public bool Known;
        // An application fallback, not a claim about an unknown model's capacity.
        public static ContextBudget Fallback => new ContextBudget { InputTokens=32768, Source="fallback", Known=false };

        // inputLimit is an input-only limit. Subtract output only from a separately
        // reported combined context window; Gemini metadata reports separate limits.
        public static ContextBudget FromLimits(int inputLimit,int contextWindow,int outputReserve,string source)
        {
            if(inputLimit<=0 && contextWindow<=0)return Fallback;
            long available=inputLimit>0?inputLimit:contextWindow;
            if(contextWindow>0)available=Math.Min(available,Math.Max(1L,(long)contextWindow-Math.Max(0,outputReserve)));
            long safety=Math.Min(available/4,Math.Max(1024L,(long)Math.Ceiling(available*.02)));
            return new ContextBudget { InputTokens=(int)Math.Max(1,available-safety),Source=source,Known=true };
        }
    }

    public interface IContextBudgetClient
    {
        Task<ContextBudget> GetContextBudgetAsync(CancellationToken token);
    }
    public interface IChatTokenCounter
    {
        Task<int?> CountInputTokensAsync(List<ChatMessage> history,string systemPrompt,CancellationToken token);
    }

    internal sealed class GeminiBudgetInfo
    {
        internal ContextBudget Budget=ContextBudget.Fallback;
        internal int OutputLimit;
        internal ContextBudget CopyBudget()=>new ContextBudget { InputTokens=Budget.InputTokens,Source=Budget.Source,Known=Budget.Known };
    }

    // Serializes only matching metadata requests. Cancellations are never cached;
    // failure fallback is cached for this session and can be replaced by model refresh.
    internal sealed class ProviderBudgetCache
    {
        readonly object sync=new object();
        readonly Dictionary<string,GeminiBudgetInfo> values=new Dictionary<string,GeminiBudgetInfo>();
        readonly Dictionary<string,SemaphoreSlim> gates=new Dictionary<string,SemaphoreSlim>();
        internal GeminiBudgetInfo Read(string key){lock(sync)return values.TryGetValue(key,out var value)?value:null;}
        internal void Register(string key,GeminiBudgetInfo value){lock(sync)values[key]=value;}
        internal async Task<GeminiBudgetInfo> GetAsync(string key,Func<CancellationToken,Task<GeminiBudgetInfo>> load,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            SemaphoreSlim gate;
            lock(sync)
            {
                if(values.TryGetValue(key,out var value))return value;
                if(!gates.TryGetValue(key,out gate))gates[key]=gate=new SemaphoreSlim(1,1);
            }
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var current=Read(key);if(current!=null)return current;
                var loaded=await load(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock(sync)
                {
                    // A settings refresh may have populated metadata while the request ran.
                    if(values.TryGetValue(key,out current))return current;
                    values[key]=loaded;return loaded;
                }
            }
            finally{gate.Release();}
        }
    }

    public static class ProviderContextBudgets
    {
        static readonly ProviderBudgetCache GeminiCache=new ProviderBudgetCache();
        static readonly HttpClient Http=new HttpClient();
        const string GeminiModels="https://generativelanguage.googleapis.com/v1beta/models/";
        internal static string Key(string apiKey,string model)
        {
            using(var hash=SHA256.Create())
                return (model??"").Trim()+"\n"+Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(apiKey??"")));
        }
        internal static GeminiBudgetInfo ParseGemini(SimpleJsonObject model,string source)
        {
            int input=model.GetInt("inputTokenLimit"),output=model.GetInt("outputTokenLimit");
            return new GeminiBudgetInfo { Budget=ContextBudget.FromLimits(input,0,0,source),OutputLimit=Math.Max(0,output) };
        }
        public static void RegisterGeminiModels(string apiKey,string json)
        {
            // Metadata enrichment must never break the existing model picker.
            try
            {
                var models=SimpleJson.Parse(json).GetObjectArray("models");if(models==null)return;
                foreach(var model in models)
                {
                    try
                    {
                        string name=model.GetString("name");if(name==null || !name.StartsWith("models/",StringComparison.Ordinal))continue;
                        var info=ParseGemini(model,"Gemini models.list");
                        if(info.Budget.Known)GeminiCache.Register(Key(apiKey,name.Substring(7)),info);
                    }
                    catch(FormatException) { }
                }
            }
            catch(FormatException) { }
        }
        public static async Task<ContextBudget> GeminiAsync(string apiKey,string model,CancellationToken token)
        {
            var info=await GeminiCache.GetAsync(Key(apiKey,model),async cancellation=>
            {
                if(string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))return new GeminiBudgetInfo();
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        using(var request=new HttpRequestMessage(HttpMethod.Get,GeminiModels+Uri.EscapeDataString(model.Trim())))
                        {
                            request.Headers.Add("x-goog-api-key",apiKey);
                            using(var response=await Http.SendAsync(request,timeout.Token).ConfigureAwait(false))
                            {
                                if(!response.IsSuccessStatusCode)return new GeminiBudgetInfo();
                                string json=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                timeout.Token.ThrowIfCancellationRequested();
                                return ParseGemini(SimpleJson.Parse(json),"Gemini models.get");
                            }
                        }
                    }
                    catch(OperationCanceledException){cancellation.ThrowIfCancellationRequested();return new GeminiBudgetInfo();}
                    catch(Exception){cancellation.ThrowIfCancellationRequested();return new GeminiBudgetInfo();}
                }
            },token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();return info.CopyBudget();
        }
        public static int GeminiOutputTokens(string apiKey,string model,int requested)
        {
            var info=GeminiCache.Read(Key(apiKey,model));
            return info!=null && info.OutputLimit>0?Math.Min(requested,info.OutputLimit):requested;
        }
        internal static string CountRequest(string model,List<ChatMessage> history,string systemPrompt)
        {
            var contents=new List<object>();
            foreach(var message in history)
                contents.Add(new Dictionary<string,object>{{"role",message.Role=="assistant"?"model":"user"},
                    {"parts",new[]{new Dictionary<string,object>{{"text",message.Content}}}}});
            return SimpleJson.Serialize(new Dictionary<string,object>{{"generateContentRequest",new Dictionary<string,object>{
                {"model","models/"+model.Trim()},{"system_instruction",new Dictionary<string,object>{{"parts",new[]{new Dictionary<string,object>{{"text",systemPrompt}}}}}},
                {"contents",contents}}}});
        }
        public static async Task<int?> CountGeminiAsync(string apiKey,string model,List<ChatMessage> history,string systemPrompt,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    using(var request=new HttpRequestMessage(HttpMethod.Post,GeminiModels+Uri.EscapeDataString(model.Trim())+":countTokens"))
                    {
                        request.Headers.Add("x-goog-api-key",apiKey);
                        request.Content=new StringContent(CountRequest(model,history,systemPrompt),Encoding.UTF8,"application/json");
                        using(var response=await Http.SendAsync(request,timeout.Token).ConfigureAwait(false))
                        {
                            if(!response.IsSuccessStatusCode)return null;
                            string json=await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            timeout.Token.ThrowIfCancellationRequested();
                            var parsed=SimpleJson.Parse(json);
                            if(!parsed.ContainsKey("totalTokens"))return null;
                            int count=parsed.GetInt("totalTokens");return count>=0?(int?)count:null;
                        }
                    }
                }
                catch(OperationCanceledException){token.ThrowIfCancellationRequested();return null;}
                catch(Exception){token.ThrowIfCancellationRequested();return null;}
            }
        }
    }
}
