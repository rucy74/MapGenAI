using System;
using System.Collections.Concurrent;
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
        public string simpleGeminiModel = LLMProviderRegistry.GetDefaultModel(LLMProvider.Gemini);

        public bool useCloudProviders = true;       // Advanced: Cloud vs Local 토글
        public List<ApiConfig> cloudConfigs = new List<ApiConfig>();
        public int currentConfigIndex = 0;

        public string localBaseUrl = "http://localhost:11434";
        public string localModel = "llama3";

        // ── UI 상태 (저장 안 함) ─────────────────────────────────────────────
        private Vector2 _configScrollPos = Vector2.zero;

        // ── 모델 목록 캐시 ───────────────────────────────────────────────────
        private readonly Dictionary<string, List<string>> _cachedModels = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _fetchingProviders = new HashSet<string>();
        private readonly ConcurrentQueue<ModelFetchResult> _modelResults = new ConcurrentQueue<ModelFetchResult>();
        private readonly ApiConfig _simpleConfig = new ApiConfig();
        private sealed class ModelFetchResult
        {
            public string Key, Error;
            public List<string> Models;
            public Action<string> Select;
            public Func<bool> IsCurrent;
            public Action Refresh;
        }

        // 프로바이더별 에러 메시지
        private readonly Dictionary<string, string> _fetchErrors = new Dictionary<string, string>();

        private static readonly HttpClient Http = new HttpClient();

        // ── ExposeData ───────────────────────────────────────────────────────
        public override void ExposeData()
        {
            Scribe_Values.Look(ref useSimpleMode, "useSimpleMode", true);
            Scribe_Values.Look(ref geminiApiKey, "geminiApiKey", "");
            Scribe_Values.Look(ref simpleGeminiModel,"simpleGeminiModel",LLMProviderRegistry.GetDefaultModel(LLMProvider.Gemini));
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
                    SelectedModel = string.IsNullOrWhiteSpace(simpleGeminiModel)?LLMProviderRegistry.GetDefaultModel(LLMProvider.Gemini):simpleGeminiModel
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
            var oldColor=GUI.color;var oldFont=Text.Font;var oldAnchor=Text.Anchor;
            try
            {
                ApplyPendingModels();
                bool advanced=!useSimpleMode;
                var listing = new Listing_Standard();
                listing.Begin(inRect);
                float usedY;
                try
                {
                    if (advanced) DrawAdvancedSettings(listing, inRect);
                    else DrawSimpleSettings(listing, inRect);
                    usedY=listing.CurHeight;
                }
                finally { listing.End(); }
                // Draw the remainder only after closing the header's GUI group, exactly once.
                if (advanced && !useSimpleMode && inRect.height > usedY)
                {
                    var panel=new Rect(inRect.x,inRect.y+usedY,inRect.width,inRect.height-usedY);
                    if(useCloudProviders) DrawCloudConfigPanel(panel);
                    else DrawLocalPanel(panel);
                }
            }
            finally {GUI.color=oldColor;Text.Font=oldFont;Text.Anchor=oldAnchor;}
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
            listing.Label("MapGenAI_Settings_SimpleDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(8f);
            listing.Label("MapGenAI_Settings_ColModel".Translate());
            var modelRow=listing.GetRect(30f);
            float loadWidth=Math.Min(210f,modelRow.width*.4f);
            simpleGeminiModel=Widgets.TextField(new Rect(modelRow.x,modelRow.y,modelRow.width-loadWidth-gap,30f),simpleGeminiModel??"");
            _simpleConfig.ApiKey=geminiApiKey;
            _simpleConfig.SelectedModel=simpleGeminiModel;
            string context=ModelKey(_simpleConfig);
            DrawModelButton(new Rect(modelRow.xMax-loadWidth,modelRow.y,loadWidth,30f),_simpleConfig,
                model=>simpleGeminiModel=model,()=>useSimpleMode && ModelKey(GetActiveConfig())==context,
                "MapGenAI_Settings_FetchModels".Translate());

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
            string description="MapGenAI_Settings_CloudConfigDesc".Translate();
            float descriptionHeight=Text.CalcHeight(description,w-4f);
            Widgets.Label(new Rect(x + 2f, y, w-4f, descriptionHeight),description);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += descriptionHeight+4f;

            // 열 헤더
            DrawColumnHeaders(new Rect(x, y, w, 20f));
            y += 22f;

            // config 행 목록 (스크롤)
            float listH = panel.yMax - y;
            if(listH<=0f)return;
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

            // API 키 또는 URL (Custom은 둘 다 필요)
            if (config.Provider == LLMProvider.Custom)
            {
                const float splitGap = 3f;
                float halfW = (keyR.width - splitGap) / 2f;
                Rect urlRect = new Rect(keyR.x, keyR.y, halfW, keyR.height);
                Rect apiKeyRect = new Rect(keyR.x + halfW + splitGap, keyR.y, halfW, keyR.height);
                config.CustomBaseUrl = Widgets.TextField(urlRect, config.CustomBaseUrl ?? "");
                if (string.IsNullOrEmpty(config.CustomBaseUrl))
                    DrawPlaceholder(urlRect, "URL");
                config.ApiKey = Widgets.TextField(apiKeyRect, config.ApiKey ?? "");
                if (string.IsNullOrEmpty(config.ApiKey))
                    DrawPlaceholder(apiKeyRect, "API Key");
            }
            else if (config.Provider == LLMProvider.Local)
            {
                config.CustomBaseUrl = Widgets.TextField(keyR, config.CustomBaseUrl ?? "");
            }
            else
            {
                config.ApiKey = Widgets.TextField(keyR, config.ApiKey ?? "");
            }

            // 모델 선택 버튼
            string context=ModelKey(config);
            DrawModelButton(modelR, config, model=>config.SelectedModel=model,
                ()=>!useSimpleMode && useCloudProviders && cloudConfigs.Contains(config) && ModelKey(config)==context);

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

        private static void DrawPlaceholder(Rect rect, string text)
        {
            var oldColor = GUI.color;
            var oldAnchor = Text.Anchor;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height), text);
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
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

        // A catalog belongs to an endpoint/account, not every configuration of a provider.
        private static string ModelKey(ApiConfig config) => config.Provider+"\n"+config.CustomBaseUrl+"\n"+config.ApiKey;

        private void DrawModelButton(Rect rect, ApiConfig config, Action<string> select, Func<bool> isCurrent, string buttonLabel=null)
        {
            if (config.Provider == LLMProvider.Custom)
            {
                config.SelectedModel = Widgets.TextField(rect, config.SelectedModel ?? "");
                return;
            }
            string key=ModelKey(config);
            bool fetching=_fetchingProviders.Contains(key);
            bool hasError=_fetchErrors.TryGetValue(key,out var error);
            string label=fetching?"MapGenAI_Settings_Loading".Translate().ToString():
                buttonLabel??(string.IsNullOrWhiteSpace(config.SelectedModel)?"MapGenAI_Settings_FetchModels".Translate().ToString():config.SelectedModel);
            var oldColor=GUI.color;
            if(fetching)GUI.color=Color.gray;
            else if(hasError)GUI.color=new Color(1f,.55f,.15f);
            bool clicked=Widgets.ButtonText(rect,label);
            GUI.color=oldColor;
            if(hasError)TooltipHandler.TipRegion(rect,"MapGenAI_Settings_FetchError".Translate(error));
            if(clicked && !fetching)
            {
                Action refresh=()=> {if(isCurrent())FetchModelsForConfig(config,select,isCurrent);};
                if(_cachedModels.TryGetValue(key,out var cached) && cached.Count>0)
                    ShowModelFloatMenu(cached,select,isCurrent,refresh);
                else refresh();
            }
        }

        private static void ShowModelFloatMenu(List<string> models, Action<string> select, Func<bool> isCurrent, Action refresh)
        {
            var options=new List<FloatMenuOption>();
            foreach(string model in models.Distinct().OrderBy(m=>m))
            {
                string selected=model;
                options.Add(new FloatMenuOption(selected,()=> {if(isCurrent())select(selected);}));
            }
            options.Add(new FloatMenuOption("MapGenAI_Settings_FetchModels".Translate(),()=> {if(isCurrent())refresh();}));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void FetchModelsForConfig(ApiConfig config, Action<string> select, Func<bool> isCurrent)
        {
            string key=ModelKey(config);
            var provider=config.Provider;
            string apiKey=config.ApiKey;
            string baseUrl=!string.IsNullOrWhiteSpace(config.CustomBaseUrl)?config.CustomBaseUrl:LLMProviderRegistry.GetBaseUrl(provider);
            if(!_fetchingProviders.Add(key))return;
            _fetchErrors.Remove(key);
            Action refresh=()=> {if(isCurrent())FetchModelsForConfig(config,select,isCurrent);};
            Task.Run(async()=>
            {
                var result=new ModelFetchResult{Key=key,Select=select,IsCurrent=isCurrent,Refresh=refresh};
                try
                {
                    result.Models=provider==LLMProvider.Gemini?await FetchGeminiModels(apiKey):
                        provider==LLMProvider.Local?await FetchLocalModels(baseUrl):await FetchOpenAICompatibleModels(baseUrl,apiKey);
                    if(result.Models==null || result.Models.Count==0)result.Error="MapGenAI_Settings_NoModels";
                }
                catch {result.Error="MapGenAI_Settings_ModelRequestFailed";}
                // Each request carries its own result and target; concurrent requests cannot cross-apply.
                _modelResults.Enqueue(result);
            });
        }

        private void ApplyPendingModels()
        {
            while(_modelResults.TryDequeue(out var result))
            {
                _fetchingProviders.Remove(result.Key);
                if(result.Error==null)
                {
                    _cachedModels[result.Key]=result.Models;
                    _fetchErrors.Remove(result.Key);
                    if(result.IsCurrent())ShowModelFloatMenu(result.Models,result.Select,result.IsCurrent,result.Refresh);
                }
                else _fetchErrors[result.Key]=result.Error.Translate();
            }
        }

        // ── 프로바이더별 Fetch 구현 ─────────────────────────────────────────

        private async Task<List<string>> FetchGeminiModels(string apiKey)
        {
            var models = new List<string>();
            string pageToken = null;
            do
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}&pageSize=100";
                if (pageToken != null) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
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
            resp.EnsureSuccessStatusCode();
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
