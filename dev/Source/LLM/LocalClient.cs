using System.Collections.Generic;
using System.Threading.Tasks;

namespace MapGenAI.LLM
{
    /// <summary>
    /// Ollama / LM Studio 등 OpenAI 호환 로컬 서버
    /// </summary>
    public class LocalClient : ILLMClient
    {
        private readonly OpenAIClient _inner;

        public LocalClient(string baseUrl, string model)
        {
            // OpenAI 호환 엔드포인트 재사용
            _inner = new OpenAIClient(apiKey: "local", model: model, baseUrl: baseUrl);
        }

        public Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt)
        {
            return _inner.SendChatAsync(history, systemPrompt);
        }
    }
}
