using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace MapGenAI.LLM
{
    public interface ILLMClient
    {
        /// <summary>대화 히스토리를 포함한 멀티턴 메시지 전송</summary>
        Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default);
    }

    public interface IVisionClient
    {
        Task<string> SendImageAsync(byte[] image, string mimeType, string instruction, CancellationToken cancellationToken = default);
    }

    public static class LLMClientFactory
    {
        public static ILLMClient Create(ApiConfig config, string fallbackLocalUrl = "http://localhost:11434")
        {
            if (config == null) return null;

            switch (config.Provider)
            {
                case LLMProvider.Gemini:
                    return new GeminiClient(config.ApiKey, config.SelectedModel);

                case LLMProvider.Local:
                    return new LocalClient(
                        !string.IsNullOrWhiteSpace(config.CustomBaseUrl) ? config.CustomBaseUrl : fallbackLocalUrl,
                        config.SelectedModel);

                case LLMProvider.Custom:
                    return new OpenAIClient(config.ApiKey, config.SelectedModel, config.CustomBaseUrl);

                default:
                    return new OpenAIClient(
                        config.ApiKey,
                        config.SelectedModel,
                        LLMProviderRegistry.GetBaseUrl(config.Provider));
            }
        }
    }
}
