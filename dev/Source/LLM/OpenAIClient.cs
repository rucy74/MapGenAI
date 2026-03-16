using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace MapGenAI.LLM
{
    public class OpenAIClient : ILLMClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private static readonly HttpClient Http = new HttpClient();

        public OpenAIClient(string apiKey, string model, string baseUrl = "https://api.openai.com")
        {
            _apiKey = apiKey;
            _model = model;
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt)
        {
            var url = $"{_baseUrl}/v1/chat/completions";

            var messages = new StringBuilder();
            messages.Append($"{{\"role\":\"system\",\"content\":{EscapeJson(systemPrompt)}}}");
            foreach (var msg in history)
            {
                messages.Append($",{{\"role\":\"{msg.Role}\",\"content\":{EscapeJson(msg.Content)}}}");
            }

            var body = $"{{\"model\":\"{_model}\",\"messages\":[{messages}]}}";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"[MapGenAI] OpenAI error: {json}");
                return null;
            }

            return ExtractContent(json);
        }

        private string ExtractContent(string json)
        {
            var marker = "\"content\": \"";
            var start = json.IndexOf(marker);
            if (start < 0) return null;
            start += marker.Length;
            var sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '"' && json[i - 1] != '\\') break;
                sb.Append(json[i] == '\\' && i + 1 < json.Length && json[i + 1] == 'n' ? '\n' : json[i]);
                if (json[i] == '\\') i++;
            }
            return sb.ToString();
        }

        private string EscapeJson(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                           .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        }
    }
}
