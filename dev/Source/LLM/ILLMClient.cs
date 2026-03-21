using System.Collections.Generic;
using System.Threading.Tasks;

namespace MapGenAI.LLM
{
    public interface ILLMClient
    {
        /// <summary>
        /// 대화 히스토리를 포함한 멀티턴 메시지 전송
        /// </summary>
        Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt);
    }

    public class ChatMessage
    {
        public string Role { get; set; }   // "user" or "assistant"
        public string Content { get; set; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public static class LLMClientFactory
    {
        public static ILLMClient Create(MapGenAISettings settings)
        {
            return settings.activeProvider switch
            {
                LLMProvider.Gemini => new GeminiClient(settings.geminiApiKey, settings.geminiModel),
                LLMProvider.OpenAI => new OpenAIClient(settings.openAiApiKey, settings.openAiModel),
                LLMProvider.Local => new LocalClient(settings.localBaseUrl, settings.localModel),
                LLMProvider.OpenRouter => new OpenAIClient(settings.openRouterApiKey, settings.openRouterModel, "https://openrouter.ai"),
                _ => new GeminiClient(settings.geminiApiKey, settings.geminiModel)
            };
        }
    }
}
