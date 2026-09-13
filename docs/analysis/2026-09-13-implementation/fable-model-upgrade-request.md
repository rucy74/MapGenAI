# Fable 모델 상향 결과 검토
추가 파일 탐색/위임/수정 없이 한국어 400단어 이내 검토. 사용자 새 결정: "gemini 성능 너무 거지같으면 blueprintai처럼 버전 높은 거 써도 돼". 실제 BlueprintAI GeminiStudioModel.DefaultModel=gemini-3.8-flash 확인.
동일한 합성 지도/빗금 그림 2장×2회, 동일 한글 범례를 전달. Gemini 2.5 Flash thinkingBudget0: 물IoU .173/.336/1회selfintersection거부/.300, 산 .370/.410/거부/.522. Gemini 3.8 Flash thinkingLevel low: 물1/.940/.954/.983, 산1/1/.931/.905; 4회모두파싱. 원문·modelVersion·token정보저장. 범례없이3.8은기하는크게개선되나산을soil로오인(4회중3회)해서 이미지설명입력칸 추가. 범례효과/모델효과를분리했고 임의사진성공으로일반화안함.
3.8 자연어실호출 5회 모두요구편집+기존지형보존:산추가/호수크기/위협1.8/하트호수추가/하트만오른쪽이동. 이전2.5동일5회도수정프롬프트에서는통과.
실제3.8 GenerateContent API에서 minimal은HTTP400이었다. Google image understanding 예시와thinking 문서표가충돌하며표와BlueprintAI가low를사용. low로실호출성공. 코드도Gemini3 low,2.5vision만budget0.
따라서개발판Simple/신규Gemini기본3.8;Simple모델선택3.8/2.5명시저장;기존Advanced선택은유지. 무료티어단정문구제거. 그림의기하재현개선이라는범위의결론/남은검증을검토하고차단할코드결함이있는지만알려주세요.

## Gemini client
```csharp
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using MapGenAI.UI;
using Verse;

namespace MapGenAI.LLM
{
    public class GeminiClient : ILLMClient, IVisionClient
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
            _model = model;
        }

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
            // temperature 0.2: 이 LLM 작업은 "말→파라미터 추출"이라 결정론적이어야 함
            // (맵 다양성은 코드의 seed/noise가 만듦, LLM 온도가 아님). 고온도는 되물음·변동성 유발.
            // responseMimeType: 모델이 평문/마크다운 대신 항상 유효 JSON을 내도록 강제 → "말로만 됐다는데 안 바뀜" 방지.
            contents.Append("],\"generationConfig\":{\"temperature\":0.2,\"maxOutputTokens\":16384,\"responseMimeType\":\"application/json\"" +
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
                "\"}}]}],\"generationConfig\":{\"temperature\":0.2,\"maxOutputTokens\":8192,\"responseMimeType\":\"application/json\"" + visionThinking + "}}";
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

```