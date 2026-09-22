using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace MapGenAI.LLM
{
    /// <summary>
    /// Ollama / LM Studio 등 OpenAI 호환 로컬 서버
    /// </summary>
    public class LocalClient : ILLMClient, IVisionClient, IContextBudgetClient
    {
        private readonly OpenAIClient _inner;

        public LocalClient(string baseUrl, string model)
        {
            // OpenAI 호환 엔드포인트 재사용
            _inner = new OpenAIClient(apiKey: "local", model: model, baseUrl: baseUrl);
        }

        public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)=>_inner.GetContextBudgetAsync(token);

        public Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default)
        {
            return _inner.SendChatAsync(history, systemPrompt, cancellationToken);
        }
        public Task<string> SendImageAsync(byte[] image,string mimeType,string instruction,CancellationToken cancellationToken=default)
            => _inner.SendImageAsync(image,mimeType,instruction,cancellationToken);
    }
}
