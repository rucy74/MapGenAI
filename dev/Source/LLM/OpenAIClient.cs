using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using MapGenAI.UI;
using Verse;

namespace MapGenAI.LLM
{
    public class OpenAIClient : ILLMClient, IVisionClient, IContextBudgetClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private static readonly HttpClient Http = new HttpClient();

        public OpenAIClient(string apiKey, string model, string baseUrl = "https://api.openai.com")
        {
            _apiKey = apiKey;
            _model = model;
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            if (!System.Uri.TryCreate(_baseUrl, System.UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new System.ArgumentException("Expected an http(s) provider URL");
        }

        public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ContextBudget.Fallback);
        }

        public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt, CancellationToken cancellationToken = default)
        {
            var messages = new StringBuilder();
            messages.Append($"{{\"role\":\"system\",\"content\":{EscapeJson(systemPrompt)}}}");
            foreach (var msg in history)
            {
                messages.Append($",{{\"role\":\"{msg.Role}\",\"content\":{EscapeJson(msg.Content)}}}");
            }

            var body = $"{{\"model\":{EscapeJson(_model)},\"temperature\":0.7,\"messages\":[{messages}]}}";

            return await SendBodyAsync(body, cancellationToken);
        }

        public Task<string> SendImageAsync(byte[] image, string mimeType, string instruction, CancellationToken cancellationToken = default)
        {
            VisionPayload.Validate(image, mimeType);
            var body = "{\"model\":" + EscapeJson(_model) + ",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":" + EscapeJson(instruction) +
                "},{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:" + mimeType + ";base64," + System.Convert.ToBase64String(image) + "\"}}]}]}";
            return SendBodyAsync(body, cancellationToken);
        }

        private async Task<string> SendBodyAsync(string body, CancellationToken cancellationToken)
        {
            var url = _baseUrl.EndsWith("/chat/completions") ? _baseUrl : _baseUrl.EndsWith("/v1") ? _baseUrl + "/chat/completions" : _baseUrl + "/v1/chat/completions";

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var response = await Http.SendAsync(request, cancellationToken))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!response.IsSuccessStatusCode) throw new System.Exception("Provider HTTP " + (int)response.StatusCode + " (check URL, model, credentials and quota)");
                    return ExtractContent(json);
                }
            }
        }

        private string ExtractContent(string json) => ProviderResponse.OpenAI(json);

        private string EscapeJson(string text) => SimpleJson.Serialize(text);
    }
}
