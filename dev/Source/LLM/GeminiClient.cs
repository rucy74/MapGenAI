using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using MapGenAI.UI;
using Verse;

namespace MapGenAI.LLM
{
    public class GeminiClient : ILLMClient, IVisionClient, IContextBudgetClient, IChatTokenCounter
    {
        private readonly string _apiKey;
        private readonly string _model;
        public string LastModelVersion { get; private set; }
        public int LastInputTokens { get; private set; }
        public int LastOutputTokens { get; private set; }
        public int LastThinkingTokens { get; private set; }
        private static readonly HttpClient Http = new HttpClient();

        public GeminiClient(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = (model ?? "").Trim();
        }

        public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)
            => ProviderContextBudgets.GeminiAsync(_apiKey,_model,token);
        public Task<int?> CountInputTokensAsync(List<ChatMessage> history,string systemPrompt,CancellationToken token)
            => ProviderContextBudgets.CountGeminiAsync(_apiKey,_model,history,systemPrompt,token);

        public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default)
        {
            var contents = new StringBuilder();
            contents.Append("{\"system_instruction\":{\"parts\":[{\"text\":");
            contents.Append(EscapeJson(systemPrompt));
            contents.Append("}]},\"contents\":[");

            for (int i = 0; i < history.Count; i++)
            {
                if (i > 0) contents.Append(",");
                var role = history[i].Role == "assistant" ? "model" : "user";
                contents.Append($"{{\"role\":\"{role}\",\"parts\":[{{\"text\":{EscapeJson(history[i].Content)}}}]}}");
            }
            // Keep the temperature used in the recorded development benchmarks.
            // This does not guarantee determinism; compare Gemini 3 defaults before changing it.
            // JSON mode requests structured output; ProviderResponse still validates the result.
            contents.Append("],\"generationConfig\":{\"temperature\":0.2,\"maxOutputTokens\":"+ProviderContextBudgets.GeminiOutputTokens(_apiKey,_model,16384)+",\"responseMimeType\":\"application/json\"" +
                (_model.StartsWith("gemini-3",System.StringComparison.OrdinalIgnoreCase)?",\"thinkingConfig\":{\"thinkingLevel\":\"low\"}":"")+"}}");

            return await SendBodyAsync(contents.ToString(), cancellationToken);
        }

        public Task<string> SendImageAsync(byte[] image, string mimeType, string instruction, CancellationToken cancellationToken = default)
        {
            VisionPayload.Validate(image, mimeType);
            // Google recommends thinkingBudget=0 for Gemini 2.5 Flash segmentation.
            string visionThinking = _model.StartsWith("gemini-2.5-flash",System.StringComparison.OrdinalIgnoreCase)
                ? ",\"thinkingConfig\":{\"thinkingBudget\":0}" : _model.StartsWith("gemini-3",System.StringComparison.OrdinalIgnoreCase)
                ? ",\"thinkingConfig\":{\"thinkingLevel\":\"low\"}" : "";
            var body = "{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":" + EscapeJson(instruction) +
                "},{\"inline_data\":{\"mime_type\":" + EscapeJson(mimeType) + ",\"data\":\"" + System.Convert.ToBase64String(image) +
                "\"}}]}],\"generationConfig\":{\"temperature\":0.2,\"maxOutputTokens\":"+ProviderContextBudgets.GeminiOutputTokens(_apiKey,_model,8192)+",\"responseMimeType\":\"application/json\"" + visionThinking + "}}";
            return SendBodyAsync(body, cancellationToken);
        }

        private async Task<string> SendBodyAsync(string body, CancellationToken cancellationToken)
        {
            var url = "https://generativelanguage.googleapis.com/v1beta/models/" + System.Uri.EscapeDataString(_model) + ":generateContent";
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("x-goog-api-key", _apiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var response = await Http.SendAsync(request, cancellationToken))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!response.IsSuccessStatusCode) throw new System.Exception("Gemini HTTP " + (int)response.StatusCode + " (check model, credentials and quota)");
                    var envelope=SimpleJson.Parse(json);var usage=envelope.GetObject("usageMetadata");
                    LastModelVersion=envelope.GetString("modelVersion");LastInputTokens=usage?.GetInt("promptTokenCount")??0;
                    LastOutputTokens=usage?.GetInt("candidatesTokenCount")??0;LastThinkingTokens=usage?.GetInt("thoughtsTokenCount")??0;
                    return ExtractText(json);
                }
            }
        }

        private string ExtractText(string json) => ProviderResponse.Gemini(json);

        private string EscapeJson(string text) => SimpleJson.Serialize(text);
    }
}
