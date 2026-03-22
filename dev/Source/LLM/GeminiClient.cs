using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace MapGenAI.LLM
{
    public class GeminiClient : ILLMClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        private static readonly HttpClient Http = new HttpClient();

        public GeminiClient(string apiKey, string model)
        {
            _apiKey = apiKey;
            _model = model;
        }

        public async Task<string> SendChatAsync(List<ChatMessage> history, string systemPrompt)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

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
            contents.Append("],\"generationConfig\":{\"temperature\":0.7}}");

            var response = await Http.PostAsync(url,
                new StringContent(contents.ToString(), Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"[MapGenAI] Gemini error: {json}");
                throw new System.Exception($"HTTP {(int)response.StatusCode}: {json}");
            }

            // 간단한 JSON 파싱 (text 필드 추출)
            var textStart = json.IndexOf("\"text\": \"") + 9;
            var textEnd = json.IndexOf("\"", textStart);
            // 더 견고한 파싱은 Phase 3에서 JSON 라이브러리 추가 후 처리
            return ExtractText(json);
        }

        private string ExtractText(string json)
        {
            // candidates[0].content.parts[0].text 추출 (JSON 이스케이프 디코딩)
            var marker = "\"text\": \"";
            var start = json.IndexOf(marker);
            if (start < 0)
            {
                // 공백 없는 형태도 대응
                marker = "\"text\":\"";
                start = json.IndexOf(marker);
                if (start < 0) return null;
            }
            start += marker.Length;
            var sb = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        default:   sb.Append(next); break;
                    }
                    i++;
                    continue;
                }
                if (json[i] == '"') break; // 문자열 종료
                sb.Append(json[i]);
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
