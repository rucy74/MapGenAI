using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MapGenAI.UI;
using Verse;
using UnityEngine;

namespace MapGenAI
{
    public class MapGenAISettings : ModSettings
    {
        public LLMProvider activeProvider = LLMProvider.Gemini;

        public string geminiApiKey = "";
        public string geminiModel = "gemini-2.5-flash";

        public string openAiApiKey = "";
        public string openAiModel = "gpt-4o-mini";

        public string localBaseUrl = "http://localhost:11434";
        public string localModel = "llama3";

        // 동적으로 불러온 모델 목록 (저장 안 함)
        private List<string> _geminiModels = new List<string>();
        private List<string> _openAiModels = new List<string>();
        private List<string> _localModels = new List<string>();

        // 토글 상태
        private bool _showGeminiList = false;
        private bool _showOpenAiList = false;
        private bool _showLocalList = false;

        // 스크롤 위치
        private Vector2 _modelScrollPos = Vector2.zero;

        private bool _isFetchingModels = false;
        private string _fetchStatus = "";

        // LongEventHandler 대신 직접 메인 스레드 전달용 (volatile)
        private volatile bool _modelsFetched = false;
        private volatile string _fetchedStatus = "";
        private List<string> _fetchedModels = null;
        private LLMProvider _fetchedProvider;

        private static readonly HttpClient Http = new HttpClient();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref activeProvider, "activeProvider", LLMProvider.Gemini);
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref geminiModel, "geminiModel", "gemini-2.5-flash");
            Scribe_Values.Look(ref openAiApiKey, "openAiApiKey", "");
            Scribe_Values.Look(ref openAiModel, "openAiModel", "gpt-4o-mini");
            Scribe_Values.Look(ref localBaseUrl, "localBaseUrl", "http://localhost:11434");
            Scribe_Values.Look(ref localModel, "localModel", "llama3");
            base.ExposeData();
        }

        public void DoWindowContents(Rect inRect)
        {
            // 백그라운드 스레드에서 결과가 도착했으면 메인 스레드에서 적용
            ApplyPendingModels();

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            bool ko = L10n.IsKorean();
            listing.Label(ko ? "LLM 제공자" : "LLM Provider");
            if (listing.RadioButton("Google Gemini", activeProvider == LLMProvider.Gemini))
                activeProvider = LLMProvider.Gemini;
            if (listing.RadioButton("OpenAI (ChatGPT)", activeProvider == LLMProvider.OpenAI))
                activeProvider = LLMProvider.OpenAI;
            if (listing.RadioButton(ko ? "로컬 (Ollama / LM Studio)" : "Local (Ollama / LM Studio)", activeProvider == LLMProvider.Local))
                activeProvider = LLMProvider.Local;

            listing.GapLine();

            if (activeProvider == LLMProvider.Gemini)
                DrawProviderSection(listing, inRect, LLMProvider.Gemini,
                    ref geminiApiKey, ko ? "Gemini API 키" : "Gemini API Key",
                    ref geminiModel, _geminiModels,
                    ref _showGeminiList);

            else if (activeProvider == LLMProvider.OpenAI)
                DrawProviderSection(listing, inRect, LLMProvider.OpenAI,
                    ref openAiApiKey, ko ? "OpenAI API 키" : "OpenAI API Key",
                    ref openAiModel, _openAiModels,
                    ref _showOpenAiList);

            else if (activeProvider == LLMProvider.Local)
                DrawLocalSection(listing, inRect);

            listing.End();
        }

        private void DrawProviderSection(Listing_Standard listing, Rect inRect, LLMProvider provider,
            ref string apiKey, string apiKeyLabel,
            ref string currentModel, List<string> modelList,
            ref bool showList)
        {
            listing.Label(apiKeyLabel);
            apiKey = listing.TextEntry(apiKey);

            // 버튼: 접기/펼치기 토글
            string btnText;
            if (_isFetchingModels && _fetchedProvider == provider)
                btnText = L10n.IsKorean() ? "불러오는 중..." : "Loading...";
            else if (showList)
                btnText = L10n.IsKorean() ? $"▲ 접기 (현재: {currentModel})" : $"▲ Collapse (current: {currentModel})";
            else if (modelList.Count > 0)
                btnText = L10n.IsKorean() ? $"▼ 모델 선택 (현재: {currentModel})" : $"▼ Select model (current: {currentModel})";
            else
                btnText = L10n.IsKorean() ? "모델 목록 불러오기" : "Fetch model list";

            if (listing.ButtonText(btnText) && !_isFetchingModels)
            {
                if (modelList.Count == 0)
                {
                    // 목록 없으면 먼저 불러오기
                    _fetchedProvider = provider;
                    FetchModelsAsync(provider);
                    showList = true;
                }
                else
                {
                    showList = !showList;
                }
            }

            // 목록 표시 (스크롤 영역)
            if (showList)
            {
                if (_isFetchingModels)
                {
                    listing.Label(L10n.IsKorean() ? "  불러오는 중..." : "  Loading...");
                }
                else if (modelList.Count == 0 && !string.IsNullOrEmpty(_fetchStatus))
                {
                    listing.Label("  " + _fetchStatus);
                }
                else if (modelList.Count > 0)
                {
                    float curY = listing.CurHeight;
                    listing.End();

                    float maxH = inRect.height - curY - 10f;
                    float scrollH = Mathf.Min(maxH, 300f);
                    float itemH = 30f;
                    float contentH = modelList.Count * itemH;
                    var outRect = new Rect(inRect.x, inRect.y + curY, inRect.width, scrollH);
                    var viewRect = new Rect(0f, 0f, outRect.width - 16f, contentH);

                    Widgets.BeginScrollView(outRect, ref _modelScrollPos, viewRect);
                    float y = 0f;
                    foreach (var m in modelList)
                    {
                        var row = new Rect(0f, y, viewRect.width, itemH);
                        if (Widgets.RadioButtonLabeled(row, "  " + m, currentModel == m))
                            currentModel = m;
                        y += itemH;
                    }
                    Widgets.EndScrollView();

                    // listing 재개 (스크롤 영역 아래부터)
                    var remainRect = new Rect(inRect.x, outRect.yMax, inRect.width, inRect.height - outRect.yMax + inRect.y);
                    listing.Begin(remainRect);
                }
            }
        }

        private void DrawLocalSection(Listing_Standard listing, Rect inRect)
        {
            listing.Label(L10n.IsKorean() ? "로컬 서버 URL" : "Local server URL");
            localBaseUrl = listing.TextEntry(localBaseUrl);

            string btnText;
            if (_isFetchingModels && _fetchedProvider == LLMProvider.Local)
                btnText = L10n.IsKorean() ? "불러오는 중..." : "Loading...";
            else if (_showLocalList)
                btnText = L10n.IsKorean() ? $"▲ 접기 (현재: {localModel})" : $"▲ Collapse (current: {localModel})";
            else if (_localModels.Count > 0)
                btnText = L10n.IsKorean() ? $"▼ 모델 선택 (현재: {localModel})" : $"▼ Select model (current: {localModel})";
            else
                btnText = L10n.IsKorean() ? "모델 목록 불러오기" : "Fetch model list";

            if (listing.ButtonText(btnText) && !_isFetchingModels)
            {
                if (_localModels.Count == 0)
                {
                    _fetchedProvider = LLMProvider.Local;
                    FetchModelsAsync(LLMProvider.Local);
                    _showLocalList = true;
                }
                else
                {
                    _showLocalList = !_showLocalList;
                }
            }

            if (_showLocalList)
            {
                if (_isFetchingModels)
                {
                    listing.Label(L10n.IsKorean() ? "  불러오는 중..." : "  Loading...");
                }
                else if (_localModels.Count > 0)
                {
                    float curY = listing.CurHeight;
                    listing.End();

                    float maxH = inRect.height - curY - 10f;
                    float scrollH = Mathf.Min(maxH, 300f);
                    float itemH = 30f;
                    float contentH = _localModels.Count * itemH;
                    var outRect = new Rect(inRect.x, inRect.y + curY, inRect.width, scrollH);
                    var viewRect = new Rect(0f, 0f, outRect.width - 16f, contentH);

                    Widgets.BeginScrollView(outRect, ref _modelScrollPos, viewRect);
                    float y = 0f;
                    foreach (var m in _localModels)
                    {
                        var row = new Rect(0f, y, viewRect.width, itemH);
                        if (Widgets.RadioButtonLabeled(row, "  " + m, localModel == m))
                            localModel = m;
                        y += itemH;
                    }
                    Widgets.EndScrollView();

                    var remainRect = new Rect(inRect.x, outRect.yMax, inRect.width, inRect.height - outRect.yMax + inRect.y);
                    listing.Begin(remainRect);
                }
            }
        }

        // 백그라운드 결과를 메인 스레드에서 안전하게 적용
        private void ApplyPendingModels()
        {
            if (!_modelsFetched) return;
            _modelsFetched = false;

            var models = _fetchedModels;
            _fetchedModels = null;

            if (models != null)
            {
                if (_fetchedProvider == LLMProvider.Gemini) _geminiModels = models;
                else if (_fetchedProvider == LLMProvider.OpenAI) _openAiModels = models;
                else _localModels = models;
            }

            _fetchStatus = _fetchedStatus;
            _isFetchingModels = false;
        }

        private void FetchModelsAsync(LLMProvider provider)
        {
            _isFetchingModels = true;
            _fetchStatus = L10n.IsKorean() ? "불러오는 중..." : "Loading...";

            Task.Run(async () =>
            {
                try
                {
                    var models = provider switch
                    {
                        LLMProvider.Gemini => await FetchGeminiModels(),
                        LLMProvider.OpenAI => await FetchOpenAIModels(),
                        LLMProvider.Local => await FetchLocalModels(),
                        _ => new List<string>()
                    };

                    _fetchedModels = models;
                    _fetchedStatus = L10n.IsKorean() ? $"✓ {models.Count}개 모델 로드됨" : $"✓ {models.Count} models loaded";
                    _modelsFetched = true; // 마지막에 set
                }
                catch (System.Exception e)
                {
                    _fetchedModels = new List<string>();
                    _fetchedStatus = L10n.IsKorean() ? $"오류: {e.Message}" : $"Error: {e.Message}";
                    _modelsFetched = true;
                }
            });
        }

        private async Task<List<string>> FetchGeminiModels()
        {
            var models = new List<string>();
            string pageToken = null;
            do
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={geminiApiKey}&pageSize=100";
                if (pageToken != null) url += $"&pageToken={pageToken}";
                var response = await Http.GetStringAsync(url);
                var parts = response.Split('"');
                pageToken = null;
                for (int i = 0; i < parts.Length - 2; i++)
                {
                    if (parts[i] == "name" && parts[i + 2].StartsWith("models/"))
                    {
                        var name = parts[i + 2].Substring("models/".Length);
                        if ((name.StartsWith("gemini-") || name.StartsWith("gemma-"))
                            && !name.Contains("embedding") && !name.Contains("imagen")
                            && !name.Contains("veo") && !name.Contains("tts")
                            && !name.Contains("audio"))
                            models.Add(name);
                    }
                    if (parts[i] == "nextPageToken")
                        pageToken = parts[i + 2];
                }
            } while (pageToken != null);
            return models;
        }

        private async Task<List<string>> FetchOpenAIModels()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Add("Authorization", $"Bearer {openAiApiKey}");
            var resp = await Http.SendAsync(request);
            var json = await resp.Content.ReadAsStringAsync();
            var models = new List<string>();
            var parts = json.Split('"');
            for (int i = 0; i < parts.Length - 2; i++)
                if (parts[i] == "id")
                {
                    var id = parts[i + 2];
                    if (id.StartsWith("gpt-") || id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4")
                        || id.StartsWith("chatgpt-"))
                        models.Add(id);
                }
            return models.OrderBy(m => m).ToList();
        }

        private async Task<List<string>> FetchLocalModels()
        {
            var models = new List<string>();
            try
            {
                var json = await Http.GetStringAsync($"{localBaseUrl.TrimEnd('/')}/api/tags");
                var parts = json.Split('"');
                for (int i = 0; i < parts.Length - 2; i++)
                    if (parts[i] == "name") models.Add(parts[i + 2]);
            }
            catch
            {
                try
                {
                    var json = await Http.GetStringAsync($"{localBaseUrl.TrimEnd('/')}/v1/models");
                    var parts = json.Split('"');
                    for (int i = 0; i < parts.Length - 2; i++)
                        if (parts[i] == "id") models.Add(parts[i + 2]);
                }
                catch { }
            }
            return models;
        }
    }

    public enum LLMProvider { Gemini, OpenAI, Local }
}
