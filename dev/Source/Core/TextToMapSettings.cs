using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Verse;
using UnityEngine;

namespace MapGenAI
{
    public class MapGenAISettings : ModSettings
    {
        // ── 저장 필드 ───────────────────────────────────────────────────────
        public bool useSimpleMode = true;
        public string geminiApiKey = "";            // Simple 모드 전용 키

        public bool useCloudProviders = true;       // Advanced: Cloud vs Local 토글
        public List<ApiConfig> cloudConfigs = new List<ApiConfig>();
        public int currentConfigIndex = 0;

        public string localBaseUrl = "http://localhost:11434";
        public string localModel = "llama3";

        // ── UI 상태 (저장 안 함) ─────────────────────────────────────────────
        private Vector2 _configScrollPos = Vector2.zero;

        // ── 모델 목록 캐시 ───────────────────────────────────────────────────
        private readonly Dictionary<LLMProvider, List<string>> _cachedModels
            = new Dictionary<LLMProvider, List<string>>();
        private readonly HashSet<LLMProvider> _fetchingProviders = new HashSet<LLMProvider>();

        // 백그라운드 → 메인 스레드 전달 (volatile)
        private volatile bool _modelsFetchDone = false;
        private LLMProvider _fetchDoneProvider;
        private List<string> _fetchDoneList;
        private ApiConfig _fetchDoneConfig;

        // 프로바이더별 에러 메시지
        private readonly Dictionary<LLMProvider, string> _fetchErrors
            = new Dictionary<LLMProvider, string>();

        private static readonly HttpClient Http = new HttpClient();

        // ── ExposeData ───────────────────────────────────────────────────────
        public override void ExposeData()
        {
            Scribe_Values.Look(ref useSimpleMode, "useSimpleMode", true);
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref useCloudProviders, "useCloudProviders", true);
            Scribe_Collections.Look(ref cloudConfigs, "cloudConfigs", LookMode.Deep);
            Scribe_Values.Look(ref currentConfigIndex, "currentConfigIndex", 0);
            Scribe_Values.Look(ref localBaseUrl, "localBaseUrl", "http://localhost:11434");
            Scribe_Values.Look(ref localModel, "localModel", "llama3");

            if (cloudConfigs == null) cloudConfigs = new List<ApiConfig>();
            base.ExposeData();
        }

        // ── Active Config 로직 ───────────────────────────────────────────────
        public ApiConfig GetActiveConfig()
        {
            if (useSimpleMode)
            {
                return new ApiConfig
                {
                    IsEnabled = true,
                    Provider = LLMProvider.Gemini,
                    ApiKey = geminiApiKey,
                    SelectedModel = "gemini-2.5-flash"
                };
            }

            if (!useCloudProviders)
            {
                return new ApiConfig
                {
                    IsEnabled = true,
                    Provider = LLMProvider.Local,
                    CustomBaseUrl = localBaseUrl,
                    SelectedModel = localModel
                };
            }

            if (cloudConfigs == null || cloudConfigs.Count == 0) return null;

            for (int i = 0; i < cloudConfigs.Count; i++)
            {
                int idx = (currentConfigIndex + i) % cloudConfigs.Count;
                if (cloudConfigs[idx].IsValid())
                {
                    currentConfigIndex = idx;
                    return cloudConfigs[idx];
                }
            }
            return null;
        }

        public bool TryNextConfig()
        {
            if (useSimpleMode || !useCloudProviders) return false;
            if (cloudConfigs == null || cloudConfigs.Count <= 1) return false;

            int original = currentConfigIndex;
            for (int i = 1; i < cloudConfigs.Count; i++)
            {
                int next = (original + i) % cloudConfigs.Count;
                if (cloudConfigs[next].IsValid())
                {
                    currentConfigIndex = next;
                    Write();
                    return true;
                }
            }
            return false;
        }

        // ── DoWindowContents ─────────────────────────────────────────────────
        public void DoWindowContents(Rect inRect)
        {
            ApplyPendingModels();

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            if (useSimpleMode)
                DrawSimpleSettings(listing, inRect);
            else
                DrawAdvancedSettings(listing, inRect);

            listing.End();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Simple Mode
        // ═══════════════════════════════════════════════════════════════════
        private void DrawSimpleSettings(Listing_Standard listing, Rect inRect)
        {
            listing.Label("MapGenAI_Settings_GeminiKey".Translate());

            const float btnWidth = 160f;
            const float gap = 6f;
            Rect rowRect = listing.GetRect(30f);
            Rect fieldRect = new Rect(rowRect.x, rowRect.y, rowRect.width - btnWidth - gap, rowRect.height);
            Rect freeBtnRect = new Rect(fieldRect.xMax + gap, rowRect.y, btnWidth, rowRect.height);

            geminiApiKey = Widgets.TextField(fieldRect, geminiApiKey);
            if (Widgets.ButtonText(freeBtnRect, "MapGenAI_Settings_GetFreeKey".Translate()))
                Application.OpenURL("https://aistudio.google.com/app/apikey");

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(listing.GetRect(Text.LineHeight), "MapGenAI_Settings_SimpleDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(12f);

            if (listing.ButtonText("MapGenAI_Settings_SwitchAdvanced".Translate()))
                useSimpleMode = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Advanced Mode
        // ═══════════════════════════════════════════════════════════════════
        private void DrawAdvancedSettings(Listing_Standard listing, Rect inRect)
        {
            if (listing.ButtonText("MapGenAI_Settings_SwitchSimple".Translate()))
            {
                useSimpleMode = true;
                return;
            }

            listing.Gap(6f);
            DrawServiceToggle(listing, inRect);
            listing.GapLine(6f);

            float usedY = listing.CurHeight;
            listing.End();

            var remainRect = new Rect(inRect.x, inRect.y + usedY, inRect.width, inRect.height - usedY);

            if (useCloudProviders)
                DrawCloudConfigPanel(remainRect);
            else
                DrawLocalPanel(remainRect);
        }

        // ── Service Toggle ───────────────────────────────────────────────────
        private void DrawServiceToggle(Listing_Standard listing, Rect inRect)
        {
            Rect cloudRow = listing.GetRect(54f);
            DrawServiceRow(cloudRow, "MapGenAI_Settings_CloudService".Translate(),
                "MapGenAI_Settings_CloudServiceDesc".Translate(), useCloudProviders);
            if (Widgets.ButtonInvisible(cloudRow)) useCloudProviders = true;

            listing.Gap(2f);

            Rect localRow = listing.GetRect(54f);
            DrawServiceRow(localRow, "MapGenAI_Settings_LocalService".Translate(),
                "MapGenAI_Settings_LocalServiceDesc".Translate(), !useCloudProviders);
            if (Widgets.ButtonInvisible(localRow)) useCloudProviders = false;
        }

        private static void DrawServiceRow(Rect row, string title, string desc, bool selected)
        {
            if (selected) Widgets.DrawHighlight(row);
            else if (Mouse.IsOver(row)) Widgets.DrawLightHighlight(row);

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(row.x + 8f, row.y + 6f, row.width - 40f, 22f), title);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Widgets.Label(new Rect(row.x + 8f, row.y + 30f, row.width - 40f, 18f), desc);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float dotSize = 16f;
            Rect dotRect = new Rect(row.xMax - dotSize - 8f, row.y + (row.height - dotSize) / 2f, dotSize, dotSize);
            Widgets.DrawBoxSolid(dotRect, selected ? new Color(0.2f, 0.75f, 0.25f) : new Color(0.35f, 0.35f, 0.35f));
        }

        // ── Cloud Config Panel ───────────────────────────────────────────────
        private void DrawCloudConfigPanel(Rect panel)
        {
            float x = panel.x;
            float w = panel.width;
            float y = panel.y;

            // 헤더
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 2f, y, w - 34f, 28f), "MapGenAI_Settings_CloudConfig".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonText(new Rect(x + w - 30f, y + 2f, 28f, 24f), "+"))
            {
                cloudConfigs.Add(new ApiConfig
                {
                    Provider = LLMProvider.Gemini,
                    SelectedModel = LLMProviderRegistry.GetDefaultModel(LLMProvider.Gemini)
                });
            }
            y += 30f;

            // 설명
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(x + 2f, y, w, 18f), "MapGenAI_Settings_CloudConfigDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 20f;

            // 열 헤더
            DrawColumnHeaders(new Rect(x, y, w, 20f));
            y += 22f;

            // config 행 목록 (스크롤)
            float listH = panel.yMax - y;
            Rect outRect = new Rect(x, y, w, listH);
            const float rowH = 32f;
            const float rowGap = 2f;
            float contentH = cloudConfigs.Count * (rowH + rowGap);
            bool needScroll = contentH > listH;
            Rect viewRect = new Rect(0, 0, w - (needScroll ? 16f : 0f), Mathf.Max(contentH, listH));

            Widgets.BeginScrollView(outRect, ref _configScrollPos, viewRect);

            int removeIdx = -1, swapIdx = -1;
            bool swapUp = false;

            for (int i = 0; i < cloudConfigs.Count; i++)
            {
                Rect rowRect = new Rect(0, i * (rowH + rowGap), viewRect.width, rowH);
                if (i % 2 == 1) Widgets.DrawLightHighlight(rowRect);
                DrawConfigRow(rowRect, cloudConfigs[i], i, cloudConfigs.Count,
                    ref removeIdx, ref swapIdx, ref swapUp);
            }

            Widgets.EndScrollView();

            if (removeIdx >= 0)
            {
                cloudConfigs.RemoveAt(removeIdx);
                if (currentConfigIndex >= cloudConfigs.Count)
                    currentConfigIndex = Mathf.Max(0, cloudConfigs.Count - 1);
            }
            if (swapIdx >= 0)
            {
                int other = swapUp ? swapIdx - 1 : swapIdx + 1;
                if (other >= 0 && other < cloudConfigs.Count)
                {
                    var tmp = cloudConfigs[swapIdx];
                    cloudConfigs[swapIdx] = cloudConfigs[other];
                    cloudConfigs[other] = tmp;
                }
            }
        }

        private static void DrawColumnHeaders(Rect rect)
        {
            GetColumnRects(rect, out var provR, out var keyR, out var modelR,
                out var checkR, out _, out _, out _);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.55f, 0.55f, 0.55f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(provR, "MapGenAI_Settings_ColService".Translate());
            Widgets.Label(keyR, "MapGenAI_Settings_ColApiKey".Translate());
            Widgets.Label(modelR, "MapGenAI_Settings_ColModel".Translate());
            Widgets.Label(new Rect(checkR.x - 8f, rect.y, 55f, rect.height),
                "MapGenAI_Settings_ColEnabled".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawConfigRow(Rect row, ApiConfig config, int idx, int total,
            ref int removeIdx, ref int swapIdx, ref bool swapUp)
        {
            GetColumnRects(row, out var provR, out var keyR, out var modelR,
                out var checkR, out var upR, out var downR, out var delR);

            // 프로바이더 드롭다운
            if (Widgets.ButtonText(provR, LLMProviderRegistry.GetLabel(config.Provider)))
            {
                var options = new List<FloatMenuOption>();
                foreach (var p in LLMProviderRegistry.All)
                {
                    var pCopy = p;
                    var cfg = config;
                    options.Add(new FloatMenuOption(LLMProviderRegistry.GetLabel(pCopy), () =>
                    {
                        cfg.Provider = pCopy;
                        cfg.SelectedModel = LLMProviderRegistry.GetDefaultModel(pCopy);
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            // API 키 또는 URL
            bool isUrl = config.Provider == LLMProvider.Local || config.Provider == LLMProvider.Custom;
            if (isUrl)
                config.CustomBaseUrl = Widgets.TextField(keyR, config.CustomBaseUrl ?? "");
            else
                config.ApiKey = Widgets.TextField(keyR, config.ApiKey ?? "");

            // 모델 선택 버튼
            DrawModelButton(modelR, config);

            // 활성화 체크박스
            bool en = config.IsEnabled;
            Widgets.Checkbox(checkR.x, checkR.y, ref en);
            config.IsEnabled = en;

            if (idx > 0 && Widgets.ButtonText(upR, "▲")) { swapIdx = idx; swapUp = true; }
            if (idx < total - 1 && Widgets.ButtonText(downR, "▼")) { swapIdx = idx; swapUp = false; }

            var prev = GUI.color;
            GUI.color = new Color(0.9f, 0.3f, 0.3f);
            if (Widgets.ButtonText(delR, "✕")) removeIdx = idx;
            GUI.color = prev;
        }

        private static void GetColumnRects(Rect row,
            out Rect provR, out Rect keyR, out Rect modelR,
            out Rect checkR, out Rect upR, out Rect downR, out Rect delR)
        {
            const float gap = 3f;
            const float provW = 110f;
            const float modelW = 130f;
            const float checkW = 28f;
            const float arrowW = 22f;
            const float delW = 24f;
            float keyW = row.width - provW - modelW - checkW - arrowW * 2f - delW - gap * 6f;

            float x = row.x;
            float y = row.y;
            float h = row.height;

            provR  = new Rect(x, y, provW, h); x += provW + gap;
            keyR   = new Rect(x, y, keyW, h);  x += keyW + gap;
            modelR = new Rect(x, y, modelW, h); x += modelW + gap;
            checkR = new Rect(x + 2f, y + (h - 24f) / 2f, 24f, 24f); x += checkW + gap;
            upR    = new Rect(x, y, arrowW, h); x += arrowW + gap;
            downR  = new Rect(x, y, arrowW, h); x += arrowW + gap;
            delR   = new Rect(x, y, delW, h);
        }

        // ── Local Panel ──────────────────────────────────────────────────────
        private void DrawLocalPanel(Rect panel)
        {
            var listing = new Listing_Standard();
            listing.Begin(panel);
            listing.Label("MapGenAI_Settings_LocalUrl".Translate());
            localBaseUrl = listing.TextEntry(localBaseUrl);
            listing.Gap(4f);
            listing.Label("MapGenAI_Settings_ColModel".Translate());
            localModel = listing.TextEntry(localModel);
            listing.End();
        }

        // ═══════════════════════════════════════════════════════════════════
        // 모델 선택 버튼 & Fetch 로직
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>모델 컬럼: Custom이면 텍스트 입력, 나머지는 드롭다운 버튼</summary>
        private void DrawModelButton(Rect rect, ApiConfig config)
        {
            if (config.Provider == LLMProvider.Custom)
            {
                config.SelectedModel = Widgets.TextField(rect, config.SelectedModel ?? "");
                return;
            }

            bool fetching = _fetchingProviders.Contains(config.Provider);
            bool hasError = _fetchErrors.TryGetValue(config.Provider, out string errMsg);
            bool hasCached = _cachedModels.TryGetValue(config.Provider, out var cached) && cached.Count > 0;

            string label;
            if (fetching)
                label = "...";
            else if (hasError)
                label = errMsg;
            else if (!string.IsNullOrWhiteSpace(config.SelectedModel))
                label = config.SelectedModel;
            else
                label = "▼ Select";

            if (fetching)       GUI.color = Color.gray;
            else if (hasError)  GUI.color = new Color(1f, 0.55f, 0.15f);  // 주황색
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.color = Color.white;

            if (clicked && !fetching)
            {
                if (hasCached)
                    ShowModelFloatMenu(config, cached);
                else
                {
                    // 에러 상태에서 클릭 → 재시도
                    _fetchErrors.Remove(config.Provider);
                    FetchModelsForConfig(config);
                }
            }
        }

        private static void ShowModelFloatMenu(ApiConfig config, List<string> models)
        {
            var options = new List<FloatMenuOption>();
            foreach (var m in models)
            {
                var mCopy = m;
                var cfg = config;
                options.Add(new FloatMenuOption(mCopy, () => cfg.SelectedModel = mCopy));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>백그라운드에서 모델 목록 fetch. 완료 시 자동으로 드롭다운 표시.</summary>
        private void FetchModelsForConfig(ApiConfig config)
        {
            var provider = config.Provider;
            var apiKey = config.ApiKey;
            var baseUrl = !string.IsNullOrWhiteSpace(config.CustomBaseUrl)
                ? config.CustomBaseUrl
                : LLMProviderRegistry.GetBaseUrl(provider);

            _fetchingProviders.Add(provider);
            _fetchDoneConfig = config;

            Task.Run(async () =>
            {
                try
                {
                    List<string> models;
                    if (provider == LLMProvider.Gemini)
                        models = await FetchGeminiModels(apiKey);
                    else if (provider == LLMProvider.Local)
                        models = await FetchLocalModels(baseUrl);
                    else
                        models = await FetchOpenAICompatibleModels(baseUrl, apiKey);

                    _fetchDoneList = models;
                }
                catch
                {
                    _fetchDoneList = new List<string>();
                }
                _fetchDoneProvider = provider;
                _modelsFetchDone = true;
            });
        }

        /// <summary>메인 스레드에서 호출 — 백그라운드 결과를 적용하고 드롭다운 표시</summary>
        private void ApplyPendingModels()
        {
            if (!_modelsFetchDone) return;
            _modelsFetchDone = false;

            _fetchingProviders.Remove(_fetchDoneProvider);

            if (_fetchDoneList != null && _fetchDoneList.Count > 0)
            {
                _cachedModels[_fetchDoneProvider] = _fetchDoneList;
                _fetchErrors.Remove(_fetchDoneProvider);
                if (_fetchDoneConfig != null)
                    ShowModelFloatMenu(_fetchDoneConfig, _fetchDoneList);
            }
            else
            {
                // 목록이 비어있거나 오류 — 에러 메시지 저장
                _fetchErrors[_fetchDoneProvider] = "No API key";
            }

            _fetchDoneConfig = null;
            _fetchDoneList = null;
        }

        // ── 프로바이더별 Fetch 구현 ─────────────────────────────────────────

        private async Task<List<string>> FetchGeminiModels(string apiKey)
        {
            var models = new List<string>();
            string pageToken = null;
            do
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}&pageSize=100";
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

        private async Task<List<string>> FetchOpenAICompatibleModels(string baseUrl, string apiKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl.TrimEnd('/')}/v1/models");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
            var resp = await Http.SendAsync(request);
            var json = await resp.Content.ReadAsStringAsync();
            var models = new List<string>();
            var parts = json.Split('"');
            for (int i = 0; i < parts.Length - 2; i++)
                if (parts[i] == "id")
                    models.Add(parts[i + 2]);
            return models.OrderBy(m => m).ToList();
        }

        private async Task<List<string>> FetchLocalModels(string baseUrl)
        {
            var models = new List<string>();
            try
            {
                // Ollama: GET /api/tags
                var json = await Http.GetStringAsync($"{baseUrl.TrimEnd('/')}/api/tags");
                var parts = json.Split('"');
                for (int i = 0; i < parts.Length - 2; i++)
                    if (parts[i] == "name") models.Add(parts[i + 2]);
            }
            catch
            {
                try
                {
                    // LM Studio: GET /v1/models
                    var json = await Http.GetStringAsync($"{baseUrl.TrimEnd('/')}/v1/models");
                    var parts = json.Split('"');
                    for (int i = 0; i < parts.Length - 2; i++)
                        if (parts[i] == "id") models.Add(parts[i + 2]);
                }
                catch { }
            }
            return models;
        }
    }
}
