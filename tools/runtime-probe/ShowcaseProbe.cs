using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LudeonTK;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    // Workshop showcase capture mode (-mapgenAIShowcase). Loaded only by the disposable probe mod in a marked
    // isolated profile. Drives the production Dialog_TextToMap with real Gemini calls: the key is read from the
    // -mapgenAIModelConfig file into memory only and is never logged or written. Paid calls are capped by a
    // ledger shared by every run of the scenario. Alert, letter and learning-helper suppression exist only here.
    static class ShowcaseProbe
    {
        const int PaidCallCap = 16;
        sealed class Step
        {
            public string Id, Tile, Action, Text, Requires, StateFrom;
            public int Priority;
            public float MaxMinutes;
            public bool Skipped, Succeeded;
        }
        sealed class Call
        {
            public string Run, Step, Status, Started, Ended, Error, ModelVersion, Text;
            public int InputTokens, OutputTokens, ThinkingTokens;
            public bool NotSent;
        }

        static string folder, runName, ledgerPath, worldSeed;
        static volatile string stepId = "setup";
        static SimpleJsonObject scenario;
        static bool dryRun, active, finishing;
        static int entryDraws; static Rect entryRect;
        // Entry-button checks: the new-colony starting-site screen, Map Preview's toolbar setting, and a click on the drawn button.
        static int clickPhase;
        static Event heldEvent;
        static readonly List<string> clickTrace = new List<string>();
        public static bool WantsStartingSite => scenario != null && scenario.GetBool("startingSite");
        static readonly object gate = new object();
        static readonly List<Step> steps = new List<Step>();
        static readonly List<Call> calls = new List<Call>();
        static readonly List<object> priorLedger = new List<object>();
        static readonly List<string[]> responses = new List<string[]>();
        static readonly Dictionary<string, string> statesAfter = new Dictionary<string, string>();
        static readonly Dictionary<string, object> result = new Dictionary<string, object>();
        static readonly Dictionary<string, object> tileFacts = new Dictionary<string, object>();
        static readonly List<object> stepSummaries = new List<object>();
        static int logWindowsClosed, ledgerUsed, metadataCalls, countCalls, lettersRemoved, previewBegins, tileA = -1, tileB = -1, currentTile = -1, previewSize = 250, captureDelay = 30;
        static IEnumerator<object> routine;
        static Dialog_TextToMap dialog;
        static float quitAt;
        static DateTime deadline;
        static Stopwatch clock;
        static Type windowType, managerType;
        static string windowAtStartup;

        // launch.ps1 starts the game hidden. A hidden Unity window never runs OnGUI, so nothing renders and RimWorld
        // resets the UI scale (UI size 0x0). Show only our own process window, without activating it (no focus steal).
        delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
        static IntPtr gameWindow;
        static bool CursorInsideGameWindow() => gameWindow != IntPtr.Zero && GetWindowRect(gameWindow, out var r) && GetCursorPos(out var p) &&
            p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        static string ShowGameWindow()
        {
            uint self = (uint)Process.GetCurrentProcess().Id;
            var windows = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint owner);
                var name = new StringBuilder(64);
                if (owner == self && GetClassName(window, name, name.Capacity) > 0 && name.ToString() == "UnityWndClass") windows.Add(window);
                return true;
            }, IntPtr.Zero);
            var states = new List<string>();
            foreach (var window in windows)
            {
                bool before = IsWindowVisible(window);
                if (!before)
                {
                    ShowWindow(window, 4); // SW_SHOWNOACTIVATE
                    SetWindowPos(window, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_BOTTOM; NOSIZE|NOMOVE|NOACTIVATE
                }
                gameWindow = window;
                states.Add((before ? "visible" : "hidden") + "->" + (IsWindowVisible(window) ? "visible" : "hidden"));
            }
            return windows.Count == 0 ? "no Unity window found" : string.Join(",", states);
        }

        public static void Configure()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAIShowcase", out var path)) return;
            scenario = SimpleJson.Parse(File.ReadAllText(path));
            dryRun = scenario.GetBool("dryRun");
            worldSeed = scenario.GetString("worldSeed");
            if (!dryRun) LoadLedger(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), scenario.GetString("callLedger") ?? "model-call-ledger.json"));
            var h = new Harmony("choco.mapgenai.probe.showcase");
            if (!string.IsNullOrEmpty(worldSeed))
                h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(FixedWorldSeed)));
            h.Patch(AccessTools.Method(typeof(AlertsReadout), "AlertsReadoutOnGUI"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(HideAlerts)));
            // The shown window can receive the operator's real cursor; its cell readout is not part of the showcase.
            h.Patch(AccessTools.Method(typeof(MouseoverReadout), "MouseoverReadoutOnGUI"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(HideAlerts)));
            // The quick-test colonists are not part of the showcase; the bar would show them behind the dialog and over the map.
            h.Patch(AccessTools.Method(typeof(ColonistBar), "ColonistBarOnGUI"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(HideAlerts)));
            // Every action is scripted: park the GUI mouse so hover highlights, tooltips and stray clicks cannot reach the UI.
            h.Patch(AccessTools.Method(typeof(UIRoot_Play), "UIRootOnGUI"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(ParkMouse)));
            // The new-colony starting-site screen is drawn by the main-menu UI root, not the play one.
            h.Patch(AccessTools.Method(typeof(UIRoot_Entry), "UIRootOnGUI"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(ParkMouse)));
            h.Patch(AccessTools.Method(typeof(GeminiClient), "SendChatAsync"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(BeforeChat)),
                postfix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(AfterChat)));
            h.Patch(AccessTools.Method(typeof(ProviderContextBudgets), "GeminiAsync"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(BeforeBudget)));
            h.Patch(AccessTools.Method(typeof(ProviderContextBudgets), "CountGeminiAsync"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(BeforeCount)));
            h.Patch(AccessTools.Method(typeof(Dialog_TextToMap), "HandleResponse"), prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(RecordResponse)));
            if (scenario.GetBool("entryAudit"))
            {
                // Counts real draws of the world-map entry button through the production OnGUI path (not a direct dialog open).
                var draw = AccessTools.Method("MapGenAI.Patches.WorldInterface_Patch:DrawAIButton");
                if (draw != null) h.Patch(draw, prefix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(BeforeEntryDraw)), postfix: new HarmonyMethod(typeof(ShowcaseProbe), nameof(AfterEntryDraw)),
                    finalizer: new HarmonyMethod(typeof(ShowcaseProbe), nameof(RestoreEntryEvent)));
            }
            windowAtStartup = ShowGameWindow();
        }

        static void LoadLedger(string path)
        {
            ledgerPath = path;
            if (!File.Exists(path)) return;
            var ledger = SimpleJson.Parse(File.ReadAllText(path));
            var previous = ledger.GetObjectArray("calls") ?? new List<SimpleJsonObject>();
            ledgerUsed = ledger.GetInt("used");
            // Fail closed: an unreadable or inconsistent ledger must never reset the shared budget.
            if (previous.Count != ledgerUsed) throw new InvalidOperationException("Model call ledger is inconsistent");
            priorLedger.AddRange(previous);
        }
        static void WriteLedger()
        {
            if (ledgerPath == null) return;
            var entries = new List<object>(priorLedger);
            entries.AddRange(calls.Where(c => !c.NotSent).Select(c => (object)new Dictionary<string, object> {
                { "run", c.Run }, { "step", c.Step }, { "status", c.Status }, { "started", c.Started }, { "ended", c.Ended }, { "modelVersion", c.ModelVersion },
                { "inputTokens", c.InputTokens }, { "outputTokens", c.OutputTokens }, { "thinkingTokens", c.ThinkingTokens }, { "error", c.Error } }));
            File.WriteAllText(ledgerPath, SimpleJson.Serialize(new Dictionary<string, object> { { "cap", PaidCallCap }, { "used", ledgerUsed }, { "calls", entries } }));
        }

        static void FixedWorldSeed(ref string seedString) => seedString = worldSeed;
        static void AfterEntryDraw(Rect btnRect) { entryDraws++; entryRect = btnRect; }
        // A click is a mouse-down on one draw of the button and a mouse-up on a later draw, handed only to the button's own
        // Widgets.ButtonInvisible (GUI.Button) call. The event of that GUI pass is put back right after the call.
        static void BeforeEntryDraw(Rect btnRect)
        {
            if (clickPhase != 1 && clickPhase != 2) return;
            // Both halves go into repaint passes: IMGUI gives the button a different control id in layout passes, and a
            // mouse-up only counts for the control that took the mouse-down.
            if (Event.current.type != EventType.Repaint) return;
            heldEvent = Event.current;
            var synthetic = new Event { type = clickPhase == 1 ? EventType.MouseDown : EventType.MouseUp, mousePosition = btnRect.center, button = 0, clickCount = 1 };
            Event.current = synthetic;
            clickTrace.Add(synthetic.type + " at " + (int)btnRect.center.x + "," + (int)btnRect.center.y + " in a " + heldEvent?.type + " pass, hotControl before " + GUIUtility.hotControl);
        }
        static void RestoreEntryEvent(Exception __exception)
        {
            if (heldEvent == null) return;
            clickTrace[clickTrace.Count - 1] += ", after " + GUIUtility.hotControl + (__exception != null ? ", draw threw " + __exception.GetType().Name + ": " + __exception.Message : "");
            Event.current = heldEvent; heldEvent = null; clickPhase++;
        }
        // Whether the entry button and the settings page survived startup: the Mod instance exists only if its constructor finished.
        static Dictionary<string, object> EntryAudit()
        {
            const string owner = "Choco.MapGenAI";
            var onGui = AccessTools.Method(typeof(WorldInterface), "WorldInterfaceOnGUI");
            var info = onGui == null ? null : HarmonyLib.Harmony.GetPatchInfo(onGui);
            int ours = HarmonyLib.Harmony.GetAllPatchedMethods().Count(m => HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.Contains(owner) == true);
            var mod = LoadedModManager.ModHandles.FirstOrDefault(m => m is MapGenAIMod);
            var toolbar = Find.WindowStack.Windows.FirstOrDefault(w => w.GetType().FullName == "MapPreview.MapPreviewToolbar");
            var preview = PreviewWindow();
            // A window over the button's centre takes the click, so the button is usable only when none is there.
            var over = entryDraws > 0 ? Find.WindowStack.GetWindowAt(entryRect.center) : null;
            return new Dictionary<string, object> {
                { "programState", Current.ProgramState.ToString() },
                { "startingSiteScreen", Find.WindowStack.IsOpen<Page_SelectStartingSite>() },
                { "toolbarRect", toolbar != null ? RectInfo(toolbar.windowRect) : null },
                { "previewRect", preview != null ? RectInfo(preview.windowRect) : null },
                { "windowAtButtonCentre", over?.GetType().FullName },
                { "windowsOverlappingButton", Find.WindowStack.Windows.Where(w => entryDraws > 0 && w.windowRect.Overlaps(entryRect)).Select(w => (object)w.GetType().FullName).ToList() },
                { "buttonPatchApplied", info?.Owners.Contains(owner) == true },
                { "methodsPatchedByMapGenAI", ours },
                { "modSettingsRegistered", mod != null },
                { "settingsCategory", mod?.SettingsCategory() },
                { "buttonDrawCalls", entryDraws },
                { "buttonRect", RectInfo(entryRect) },
                { "tile", currentTile } };
        }
        // New-colony starting-site screen: the same entry-state game the quick test builds (scenario, storyteller, world from the
        // fixed seed), opened on Page_SelectStartingSite instead of generating a map. Map Preview has separate settings for this screen.
        public static void BeginStartingSite(string output, Action<Exception> fail)
        {
            GenCommandLine.TryGetCommandLineArg("mapgenAIShowcase", out var path);
            LongEventHandler.QueueLongEvent(() =>
            {
                try { Root_Play.SetupForQuickTestPlay(); }
                catch (Exception error) { fail(error); return; }
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        Find.World.renderer.RegenerateAllLayersNow();
                        Find.WindowStack.Add(new Page_SelectStartingSite());
                        Run(output, path);
                    }
                    catch (Exception error) { fail(error); }
                });
            }, "GeneratingWorld", false, null);
        }
        // Map Preview's own switches for its toolbar on the starting-site screen and during play (both on by default).
        static Dictionary<string, object> SetMapPreviewToolbar(bool enabled)
        {
            var settings = AccessTools.Field(AccessTools.TypeByName("MapPreview.MapPreviewMod"), "Settings").GetValue(null);
            var record = new Dictionary<string, object>();
            foreach (var name in new[] { "EnableToolbar", "EnableToolbarInPlay" })
            {
                var entry = AccessTools.Field(settings.GetType(), name).GetValue(settings);
                var value = AccessTools.Field(entry.GetType(), "Value");
                value.SetValue(entry, enabled);
                record[name] = value.GetValue(entry);
            }
            AccessTools.Method("MapPreview.WorldInterfaceManager:RefreshInterface").Invoke(null, null);
            record["toolbarOpenAfterRefresh"] = Find.WindowStack.Windows.Any(w => w.GetType().FullName == "MapPreview.MapPreviewToolbar");
            return record;
        }
        static IEnumerable<object> ClickEntry(Dictionary<string, object> entry)
        {
            bool openBefore = Find.WindowStack.IsOpen<Dialog_TextToMap>();
            clickTrace.Clear();
            clickPhase = 1;
            var ok = new bool[1];
            foreach (var y in WaitFor(() => clickPhase >= 3, 10f, ok)) yield return y;
            if (!ok[0]) { clickPhase = 0; GUIUtility.hotControl = 0; }
            int hotAfterClick = GUIUtility.hotControl;
            foreach (var y in Frames(10)) yield return y;
            var opened = Find.WindowStack.Windows.OfType<Dialog_TextToMap>().FirstOrDefault();
            var click = new Dictionary<string, object> {
                { "delivered", ok[0] }, { "trace", clickTrace.Cast<object>().ToList() },
                { "dialogOpenBefore", openBefore }, { "dialogOpenedByClick", !openBefore && opened != null }, { "hotControlAfterClick", hotAfterClick } };
            if (opened == null) GUIUtility.hotControl = 0;
            entry["click"] = click;
            if (opened == null) yield break;
            foreach (var y in Capture("01-after-click", click)) yield return y;
            opened.Close(false);
            foreach (var y in Frames(10)) yield return y;
        }
        static bool HideAlerts() => false;
        static void ParkMouse() { if (Event.current != null) Event.current.mousePosition = new Vector2(-100000f, -100000f); }
        static bool BeforeChat(GeminiClient __instance, ref Task<string> __result, out object __state)
        {
            var call = new Call { Run = runName, Step = stepId, Started = Now() };
            __state = call;
            lock (gate)
            {
                calls.Add(call);
                if (dryRun)
                {
                    call.NotSent = true; call.Status = "dry-run"; call.Text = DryRunReply(call.Step); call.Ended = Now();
                    __result = Task.FromResult(call.Text);
                    return false;
                }
                if (ledgerUsed >= PaidCallCap)
                {
                    call.NotSent = true; call.Status = "blocked-cap"; call.Ended = Now();
                    __result = Task.FromException<string>(new InvalidOperationException("Showcase paid-call cap of " + PaidCallCap + " reached; request not sent."));
                    return false;
                }
                ledgerUsed++; call.Status = "sent";
                try { WriteLedger(); } catch (Exception error) { call.Error = "ledger write failed: " + error.Message; }
            }
            return true;
        }
        static void AfterChat(GeminiClient __instance, Task<string> __result, object __state)
        {
            var call = __state as Call;
            if (call == null || call.NotSent || __result == null) return;
            var client = __instance;
            __result.ContinueWith(task =>
            {
                lock (gate)
                {
                    call.Ended = Now();
                    if (task.Status == TaskStatus.RanToCompletion) { call.Status = "ok"; call.Text = task.Result; }
                    else if (task.IsCanceled) call.Status = "canceled";
                    else { call.Status = "error"; call.Error = task.Exception?.GetBaseException().Message; }
                    call.ModelVersion = client.LastModelVersion; call.InputTokens = client.LastInputTokens;
                    call.OutputTokens = client.LastOutputTokens; call.ThinkingTokens = client.LastThinkingTokens;
                    try { WriteLedger(); } catch (Exception) { }
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        static bool BeforeBudget(ref Task<ContextBudget> __result)
        {
            lock (gate) metadataCalls++;
            if (!dryRun) return true;
            __result = Task.FromResult(ContextBudget.FromLimits(1048576, 0, 0, "showcase dry run"));
            return false;
        }
        static bool BeforeCount(ref Task<int?> __result)
        {
            lock (gate) countCalls++;
            if (!dryRun) return true;
            __result = Task.FromResult<int?>(null);
            return false;
        }
        static void RecordResponse(string response)
        {
            if (active) lock (gate) responses.Add(new[] { stepId, response });
        }
        static string DryRunReply(string step)
        {
            var reply = scenario.GetObject("dryRunReplies")?.GetObject(step);
            return reply != null ? SimpleJson.Serialize(reply) : "{\"action\":\"ask\",\"message\":\"Dry run: no model call was made for this step.\"}";
        }

        public static void Run(string output, string scenarioPath)
        {
            folder = output; runName = new DirectoryInfo(output).Name; clock = Stopwatch.StartNew();
            Application.runInBackground = true;
            typeof(Prefs).GetProperty("RunInBackground")?.SetValue(null, true, null);
            try
            {
                if (scenario == null) throw new InvalidOperationException("Showcase scenario was not configured at startup");
                foreach (var s in scenario.GetObjectArray("steps") ?? throw new FormatException("Scenario has no steps"))
                    steps.Add(new Step { Id = s.GetString("id"), Tile = s.GetString("tile") ?? "A", Action = s.GetString("action"), Text = s.GetString("text"),
                        Requires = s.GetString("requires"), StateFrom = s.GetString("stateFrom"), Priority = s.GetInt("priority", 99), MaxMinutes = s.GetFloat("maxMinutes", 45f) });
                previewSize = scenario.GetInt("previewMapSize", 250);
                captureDelay = Math.Max(30, scenario.GetInt("captureDelayFrames", 30));
                windowType = AccessTools.TypeByName("MapPreview.MapPreviewWindow");
                managerType = AccessTools.TypeByName("MapPreview.WorldInterfaceManager");
                if (windowType == null || managerType == null) throw new InvalidOperationException("Map Preview types are not loaded");
                ConfigureModel();
                SubscribePreviewEvents();
                deadline = DateTime.UtcNow.AddMinutes(scenario.GetFloat("runMinutes", 100f));
                result["run"] = runName; result["dryRun"] = dryRun; result["scenario"] = Path.GetFullPath(scenarioPath);
                result["paidCallCap"] = PaidCallCap; result["ledgerUsedAtStart"] = ledgerUsed; result["startedUtc"] = Now();
                result["gameWindowAtStartup"] = windowAtStartup; result["gameWindowAtRun"] = ShowGameWindow();
                AccessTools.Field(typeof(EditWindow_Log), "canAutoOpen").SetValue(null, false); // the log window's own Auto-open toggle
                float uiScale = scenario.GetFloat("uiScale", 0f);
                result["uiScaleAtRun"] = Prefs.UIScale;
                if (uiScale > 0f && Prefs.UIScale != uiScale) { Prefs.UIScale = uiScale; GenUI.ClearLabelWidthCache(); }
                active = true;
                routine = Script().GetEnumerator();
            }
            catch (Exception error) { active = true; Finish(error); }
        }

        static void ConfigureModel()
        {
            var settings = MapGenAIMod.Settings;
            settings.useSimpleMode = true;
            if (dryRun)
            {
                // Dry runs never read the credential file; every transport call is answered locally.
                settings.geminiApiKey = "dry-run-placeholder"; settings.simpleGeminiModel = "gemini-3.8-flash";
                result["model"] = settings.simpleGeminiModel + " (dry run, not called)";
                return;
            }
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAIModelConfig", out var path) || !File.Exists(path))
                throw new InvalidOperationException("Showcase requires -mapgenAIModelConfig");
            string key, model;
            try { var config = SimpleJson.Parse(File.ReadAllText(path)); key = config.GetString("gemini_api_key"); model = config.GetString("gemini_model"); }
            catch (Exception) { throw new InvalidOperationException("Model config could not be read"); } // never propagate file contents
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Model config has no Gemini key");
            settings.geminiApiKey = key.Trim();
            settings.simpleGeminiModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.8-flash" : model.Trim();
            if (settings.GetActiveConfig()?.IsValid() != true) throw new InvalidOperationException("Gemini configuration is not valid");
            result["model"] = settings.simpleGeminiModel;
        }

        public static void Tick()
        {
            if (!active) return;
            if (finishing)
            {
                if (Time.realtimeSinceStartup >= quitAt || PreviewIdle()) { active = false; Application.Quit(); }
                return;
            }
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Showcase run exceeded its time limit");
                if (dialog != null && Find.WindowStack.IsOpen(dialog)) Invoke("PollResponse");
                if (!routine.MoveNext()) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        static void Finish(Exception error)
        {
            if (finishing) return;
            finishing = true;
            try { if (MapGenAIMod.Settings != null) MapGenAIMod.Settings.geminiApiKey = ""; } catch (Exception) { }
            try
            {
                if (error != null) File.WriteAllText(Path.Combine(folder, "error.txt"), error.ToString());
                lock (gate)
                {
                    result["ok"] = error == null; result["error"] = error?.Message; result["endedUtc"] = Now();
                    result["minutes"] = clock?.Elapsed.TotalMinutes; result["tiles"] = tileFacts; result["steps"] = stepSummaries;
                    result["paidCallsThisRun"] = calls.Count(c => !c.NotSent); result["ledgerUsedAtEnd"] = ledgerUsed;
                    result["blockedByCap"] = calls.Count(c => c.Status == "blocked-cap"); result["dryRunAnswers"] = calls.Count(c => c.Status == "dry-run");
                    result["modelCalls"] = calls.Select(c => (object)new Dictionary<string, object> { { "step", c.Step }, { "status", c.Status }, { "started", c.Started },
                        { "ended", c.Ended }, { "modelVersion", c.ModelVersion }, { "inputTokens", c.InputTokens }, { "outputTokens", c.OutputTokens },
                        { "thinkingTokens", c.ThinkingTokens }, { "error", c.Error } }).ToList();
                    result["providerMetadataCalls"] = metadataCalls; result["providerTokenCountCalls"] = countCalls;
                    result["suppression"] = new Dictionary<string, object> { { "alertsReadoutPatched", true }, { "lettersRemoved", lettersRemoved }, { "debugLogWindowsClosed", logWindowsClosed }, { "mouseoverReadoutPatched", true }, { "colonistBarPatched", true }, { "guiMouseParked", true },
                        { "adaptiveTraining", Prefs.AdaptiveTrainingEnabled }, { "gamePausedForCaptures", true } };
                    File.WriteAllText(Path.Combine(folder, "result.json"), SimpleJson.Serialize(result));
                }
            }
            catch (Exception writeError) { Log.Error("[MapGenAI Showcase] result write failed: " + writeError.Message); }
            quitAt = Time.realtimeSinceStartup + 60f;
        }

        static IEnumerable<object> Script()
        {
            // Fail before any model call unless the GUI renders at the requested scale (Verse.UI size follows Screen / UIScale).
            var guiOk = new bool[1];
            foreach (var y in WaitFor(() => Verse.UI.screenWidth > 0 && Verse.UI.screenWidth == Mathf.RoundToInt(Screen.width / Prefs.UIScale), 60f, guiOk)) yield return y;
            result["gui"] = new Dictionary<string, object> { { "screen", Screen.width + "x" + Screen.height }, { "uiUnits", Verse.UI.screenWidth + "x" + Verse.UI.screenHeight }, { "uiScale", Prefs.UIScale } };
            if (!guiOk[0]) throw new InvalidOperationException("The game GUI is not rendering (UI size " + Verse.UI.screenWidth + "x" + Verse.UI.screenHeight + "); stopped before any model call");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Prefs.AdaptiveTrainingEnabled = false;
            Find.World.info.initialMapSize = new IntVec3(previewSize, 1, previewSize);
            ClearLetters();
            var rules = scenario.GetObject("tiles") ?? throw new FormatException("Scenario has no tile rules");
            tileA = SelectTile("A", rules.GetObject("A"), -1);
            tileB = SelectTile("B", rules.GetObject("B"), tileA);
            File.WriteAllText(Path.Combine(folder, "tiles.json"), SimpleJson.Serialize(tileFacts));
            if (scenario.GetBool("mapPreviewToolbarOff")) result["mapPreviewToolbarOff"] = SetMapPreviewToolbar(false);
            CameraJumper.TryShowWorld();
            foreach (var y in Frames(30)) yield return y; // Map Preview ignores selections in the first world frames.
            foreach (var y in ShowTile(tileA)) yield return y;
            if (scenario.GetBool("entryAudit"))
            {
                foreach (var y in Frames(captureDelay)) yield return y;
                var entry = EntryAudit();
                foreach (var y in Capture("00-world-entry", entry)) yield return y;
                entry["buttonDrawCallsAfterCapture"] = entryDraws;
                if (scenario.GetBool("clickEntry")) foreach (var y in ClickEntry(entry)) yield return y;
                result["entryAudit"] = entry;
                if (steps.Count == 0) { stepId = "done"; yield break; }
            }
            OpenDialog();
            foreach (var y in Frames(10)) yield return y;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                stepId = step.Id;
                if (step.Action == "generate-map") { foreach (var y in GenerateMapStep(step)) yield return y; continue; }
                int tile = step.Tile == "B" ? tileB : tileA;
                Dictionary<string, object> beforeTile = null;
                if (tile != currentTile)
                {
                    CloseDialog();
                    foreach (var y in Frames(10)) yield return y;
                    foreach (var y in ShowTile(tile)) yield return y;
                    beforeTile = SavePreview(step.Id + "-before-preview");
                    OpenDialog();
                    foreach (var y in Frames(captureDelay)) yield return y;
                }
                foreach (var y in RunStep(i, step, beforeTile)) yield return y;
            }
            stepId = "done";
            CloseDialog();
            foreach (var y in Frames(10)) yield return y;
        }

        static IEnumerable<object> RunStep(int index, Step step, Dictionary<string, object> beforeTile)
        {
            var record = new Dictionary<string, object> { { "id", step.Id }, { "action", step.Action }, { "tileLabel", step.Tile }, { "tile", currentTile },
                { "text", step.Text }, { "startedUtc", Now() } };
            if (beforeTile != null) record["previewBeforeTileRequest"] = beforeTile;
            var attempts = new List<object>();
            record["attempts"] = attempts;
            string before = State();
            record["stateBefore"] = SimpleJson.Parse(before);
            record["tileFeaturesBefore"] = Features(currentTile);
            int historyStart = History().Count, beginsBefore = previewBegins;
            string hashBefore = PreviewHash();
            var settle = new Dictionary<string, object>();
            bool captured = false, settled = false;
            if (step.Requires != null && !steps.Any(s => s.Id == step.Requires && s.Succeeded))
            { step.Skipped = true; record["skipped"] = "requires " + step.Requires; }
            else if (step.Action == "open") step.Succeeded = true;
            else if (step.Action == "guide")
            {
                Invoke("OpenRecommendationGuide");
                var guide = Field("_guide") as Window;
                if (guide == null) throw new InvalidOperationException("Find my preferences did not open the guide");
                guide.windowRect.x = dialog.windowRect.x + (dialog.windowRect.width - guide.windowRect.width) / 2f;
                guide.windowRect.y = dialog.windowRect.y;
                record["guide"] = GuideFacts(guide);
                foreach (var y in Frames(captureDelay)) yield return y;
                foreach (var y in Capture(step.Id, record)) yield return y;
                captured = true;
                guide.Close(false);
                foreach (var y in Frames(10)) yield return y;
                record["guideClosedWithoutModelCall"] = Field("_guide") == null && CallCount(step.Id) == 0;
                step.Succeeded = true;
            }
            else if (step.Action == "undo")
            {
                Invoke("DoUndo");
                step.Succeeded = State() != before;
            }
            else if (step.Action == "send" || step.Action == "refine" || step.Action == "reset-quick-suggestions")
            {
                for (int attempt = 1; attempt <= 2 && !step.Succeeded; attempt++)
                {
                    if (!(attempt == 1 ? CanStart(index) : CanRetry(index)))
                    {
                        if (attempt == 1) { step.Skipped = true; record["skipped"] = "paid-call budget reserved for higher-priority steps"; }
                        else record["retrySkipped"] = "paid-call budget reserved for the remaining steps";
                        break;
                    }
                    var info = new Dictionary<string, object> { { "attempt", attempt }, { "startedUtc", Now() } };
                    attempts.Add(info);
                    string pre = State();
                    if (step.Action == "reset-quick-suggestions")
                    {
                        int begins = previewBegins; string hash = PreviewHash();
                        Invoke("DoReset");
                        info["resetChangedState"] = State() != pre;
                        if (State() != pre) { var resetSettle = new Dictionary<string, object>(); foreach (var y in SettleAfterChange(resetSettle, begins, hash)) yield return y; info["resetSettle"] = resetSettle; }
                        pre = State();
                    }
                    int history = History().Count, responsesBefore = ResponseCount(step.Id), callsBefore = CallCount(step.Id);
                    var plans = Field("_recommendations") as List<RecommendationPlan>;
                    object option2 = plans != null && plans.Count >= 2 ? plans[1] : null;
                    if (step.Action == "reset-quick-suggestions") Invoke("SendText", step.Text ?? "Recommend a map."); // the English "Quick suggestions" button
                    else { SetField("_inputText", step.Text); Invoke("SendMessage"); }
                    if (!(bool)Field("_isWaiting")) info["requestNotStarted"] = (string)Field("_statusText");
                    else
                    {
                        var ok = new bool[1];
                        foreach (var y in WaitFor(() => !(bool)Field("_isWaiting"), 300f, ok)) yield return y;
                        if (!ok[0]) { info["timedOut"] = true; Invoke("CancelCandidateRequest"); }
                    }
                    var now = Field("_recommendations") as List<RecommendationPlan>;
                    if (now != null)
                    {
                        var ok = new bool[1];
                        foreach (var y in WaitFor(CandidatesDone, 420f, ok)) yield return y;
                        info["candidatePreviewsComplete"] = ok[0];
                    }
                    info["endedUtc"] = Now();
                    info["modelCalls"] = CallsSince(step.Id, callsBefore);
                    info["responsesHandled"] = ResponsesSince(step.Id, responsesBefore);
                    info["dialogMessages"] = MessagesSince(history);
                    info["stateChanged"] = State() != pre;
                    if (step.Action == "send") step.Succeeded = State() != pre;
                    else if (step.Action == "refine")
                    {
                        bool revised = now != null && now.Count >= 2 && option2 != null && !ReferenceEquals(now[1], option2);
                        info["option2Revised"] = revised;
                        step.Succeeded = revised && State() == pre && CandidatesRendered();
                    }
                    else
                    {
                        info["options"] = now?.Count ?? 0;
                        step.Succeeded = now?.Count == 3 && CandidatesRendered();
                    }
                }
            }
            else throw new FormatException("Unknown showcase action " + step.Action);

            if (!step.Skipped && State() != before && !(step.Action == "reset-quick-suggestions" && attempts.Count > 0))
            { foreach (var y in SettleAfterChange(settle, beginsBefore, hashBefore)) yield return y; settled = true; }
            else if (!step.Skipped) settle["settle"] = step.Action == "reset-quick-suggestions" ? "reset settled inside attempt; candidates awaited" : "no map-state change";
            record["settle"] = settle;
            record["stateAfter"] = SimpleJson.Parse(State());
            record["changedFields"] = ChangedFields(before, State());
            record["tileFeaturesAfter"] = Features(currentTile);
            record["undoDepth"] = ((ICollection)Field("_paramStack")).Count;
            record["dialogMessages"] = MessagesSince(History().Count < historyStart ? 0 : historyStart);
            record["restoresStateOf"] = statesAfter.Where(p => p.Value == State()).Select(p => p.Key).ToList();
            if (!step.Skipped && !captured)
            {
                // At least one authoring check (every 60 frames) must run after the preview so its notes are in the chat.
                foreach (var y in Frames(settled ? Math.Max(captureDelay, 65) : captureDelay)) yield return y;
                foreach (var y in Capture(step.Id, record)) yield return y;
            }
            if (!step.Skipped)
            {
                record["preview"] = SavePreview(step.Id + "-preview");
                if (Field("_recommendations") != null) record["candidates"] = SaveCandidates(step.Id);
            }
            statesAfter[step.Id] = State();
            record["succeeded"] = step.Succeeded; record["skippedStep"] = step.Skipped; record["endedUtc"] = Now();
            File.WriteAllText(Path.Combine(folder, step.Id + ".json"), SimpleJson.Serialize(record));
            lock (gate) stepSummaries.Add(new Dictionary<string, object> { { "id", step.Id }, { "action", step.Action }, { "succeeded", step.Succeeded },
                { "skipped", step.Skipped ? record["skipped"] : null }, { "attempts", attempts.Count }, { "paidCalls", calls.Count(c => c.Step == step.Id && !c.NotSent) },
                { "capture", record.TryGetValue("capture", out var shot) ? shot : null } });
        }

        static IEnumerable<object> GenerateMapStep(Step step)
        {
            var record = new Dictionary<string, object> { { "id", step.Id }, { "action", step.Action }, { "stateFrom", step.StateFrom }, { "startedUtc", Now() } };
            string json = null;
            if (step.StateFrom == null || !statesAfter.TryGetValue(step.StateFrom, out json) || steps.Any(s => s.Id == step.StateFrom && !s.Succeeded))
                record["skipped"] = "source state " + step.StateFrom + " is unavailable or its step did not succeed";
            else if (clock.Elapsed.TotalMinutes + step.MaxMinutes > scenario.GetFloat("runMinutes", 100f))
                record["skipped"] = "less than " + step.MaxMinutes + " minutes left in the run window";
            if (record.ContainsKey("skipped"))
            {
                step.Skipped = true;
                File.WriteAllText(Path.Combine(folder, step.Id + ".json"), SimpleJson.Serialize(record));
                lock (gate) stepSummaries.Add(new Dictionary<string, object> { { "id", step.Id }, { "action", step.Action }, { "succeeded", false }, { "skipped", record["skipped"] } });
                yield break;
            }
            CloseDialog();
            foreach (var y in Frames(10)) yield return y;
            var watch = Stopwatch.StartNew();
            Map map = null;
            try
            {
                MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(json), tileA);
                record["restoredState"] = SimpleJson.Parse(State(tileA));
                var parent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                parent.Tile = tileA; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                map = MapGenerator.GenerateMap(new IntVec3(previewSize, 1, previewSize), parent, parent.MapGeneratorDef);
            }
            catch (Exception error) { record["error"] = error.ToString(); }
            record["generationSeconds"] = watch.Elapsed.TotalSeconds;
            if (map != null)
            {
                Current.Game.CurrentMap = map;
                CameraJumper.TryHideWorld();
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                int fogged = map.AllCells.Count(c => map.fogGrid.IsFogged(c));
                int hour = GenLocalDate.HourOfDay(map), shift = (12 - hour + 24) % 24 * GenDate.TicksPerHour;
                // Light the far view at local noon: game time moves forward without simulating ticks; recorded below.
                if (shift > 0) Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + shift);
                map.skyManager.SkyManagerUpdate();
                record["map"] = new Dictionary<string, object> { { "tile", (int)map.Tile }, { "size", map.Size.x + "x" + map.Size.z }, { "localHourGenerated", hour },
                    { "localHourCaptured", GenLocalDate.HourOfDay(map) }, { "ticksShifted", shift },
                    { "foggedFraction", fogged / (float)map.cellIndices.NumGridCells } };
                Find.CameraDriver.SetRootPosAndSize(map.Center.ToVector3Shifted(), 60f);
                foreach (var y in Frames(150)) yield return y;
                foreach (var y in Capture(step.Id + "-1", record, "capture1")) yield return y;
                Find.CameraDriver.SetRootPosAndSize(new IntVec3(map.Size.x / 2, 0, map.Size.z / 4).ToVector3Shifted(), 40f);
                foreach (var y in Frames(90)) yield return y;
                foreach (var y in Capture(step.Id + "-2", record, "capture2")) yield return y;
                step.Succeeded = true;
            }
            record["succeeded"] = step.Succeeded; record["minutes"] = watch.Elapsed.TotalMinutes; record["endedUtc"] = Now();
            File.WriteAllText(Path.Combine(folder, step.Id + ".json"), SimpleJson.Serialize(record));
            lock (gate) stepSummaries.Add(new Dictionary<string, object> { { "id", step.Id }, { "action", step.Action }, { "succeeded", step.Succeeded }, { "skipped", null } });
        }

        // Budget: a first attempt keeps one call for every later step of higher priority; a retry keeps one for every later step.
        static bool IsModelStep(Step s) => s.Action == "send" || s.Action == "refine" || s.Action == "reset-quick-suggestions";
        static bool CanStart(int index)
        {
            if (dryRun) return true;
            int reserve = steps.Skip(index + 1).Count(s => IsModelStep(s) && !s.Skipped && s.Priority < steps[index].Priority);
            lock (gate) return ledgerUsed + 1 + reserve <= PaidCallCap;
        }
        static bool CanRetry(int index)
        {
            if (dryRun) return true;
            int reserve = steps.Skip(index + 1).Count(s => IsModelStep(s) && !s.Skipped);
            lock (gate) return ledgerUsed + 1 + reserve <= PaidCallCap;
        }

        static int SelectTile(string label, SimpleJsonObject rule, int exclude)
        {
            if (rule == null) throw new FormatException("Scenario has no rule for tile " + label);
            var biomes = rule.GetArray("biomes") ?? new List<string>();
            var hills = rule.GetArray("hilliness") ?? new List<string>();
            bool river = rule.GetBool("river"), coastal = rule.GetBool("coastal");
            var matches = new List<SurfaceTile>();
            foreach (var tile in Find.WorldGrid.Tiles)
            {
                if (tile.PrimaryBiome == null || !biomes.Contains(tile.PrimaryBiome.defName) || !hills.Contains(tile.hilliness.ToString())) continue;
                if ((int)tile.tile == exclude || Find.World.Impassable(tile.tile) || Find.WorldObjects.AnyWorldObjectAt(tile.tile)) continue;
                if (FeaturePolicy.HasRiver(tile) != river) continue;
                var water = FeaturePolicy.WaterNeighbors(tile);
                bool ocean = water.Any(n => n.PrimaryBiome == BiomeDefOf.Ocean), lake = water.Any(n => n.PrimaryBiome == BiomeDefOf.Lake);
                if (coastal ? !ocean || lake : water.Count > 0) continue;
                matches.Add(tile);
            }
            if (matches.Count == 0) throw new InvalidOperationException("No world tile matches rule " + label);
            var chosen = matches.OrderBy(t => hills.IndexOf(t.hilliness.ToString())).ThenBy(t => ExtraFeatures(t).Count)
                .ThenBy(t => river && !RiverWidthPreferred(t) ? 1 : 0).ThenBy(t => (int)t.tile).First();
            tileFacts[label] = new Dictionary<string, object> { { "tile", (int)chosen.tile }, { "rule", rule.GetString("rule") }, { "biome", chosen.PrimaryBiome.defName },
                { "hilliness", chosen.hilliness.ToString() }, { "features", chosen.Mutators.Select(m => m.defName).ToList() }, { "extraFeatures", ExtraFeatures(chosen) },
                { "rivers", (chosen.Rivers ?? new List<SurfaceTile.RiverLink>()).Select(r => r.river.defName + " (width " + r.river.widthOnMap + ")").ToList() },
                { "waterNeighbours", FeaturePolicy.WaterNeighbors(chosen).Select(n => n.PrimaryBiome.defName).ToList() },
                { "longLat", Find.WorldGrid.LongLatOf(chosen.tile).ToString() }, { "matchingTiles", matches.Count }, { "worldSeed", Find.World.info.seedString } };
            return chosen.tile;
        }
        static List<string> ExtraFeatures(Tile t) => t.Mutators.Where(m => m.defName != "River" && m.defName != "Coast" &&
            !(m.categories ?? new List<string>()).Any(c => c.Contains("River") || c.Contains("Coast"))).Select(m => m.defName).ToList();
        static bool RiverWidthPreferred(SurfaceTile t)
        {
            float width = t.Rivers == null || t.Rivers.Count == 0 ? 0f : t.Rivers.Max(r => r.river.widthOnMap);
            return width >= 6f && width <= 20f;
        }

        static IEnumerable<object> ShowTile(int tile)
        {
            CameraJumper.TryShowWorld();
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.SelectedTile = tile;
            Find.WorldCameraDriver.JumpTo((PlanetTile)tile);
            currentTile = tile;
            var ok = new bool[1];
            foreach (var y in WaitFor(() => PreviewSettled(tile), 180f, ok)) yield return y;
            if (!ok[0]) throw new TimeoutException("Map Preview panel did not render world tile " + tile);
            foreach (var y in WaitFor(InterpolationDone, 5f, ok)) yield return y;
        }
        static void OpenDialog()
        {
            dialog = new Dialog_TextToMap();
            Find.WindowStack.Add(dialog);
            // Place the chat where a player would drag it: left of the Map Preview panel, never covering it.
            var preview = PreviewWindow();
            float right = preview != null ? preview.windowRect.x - 24f : Verse.UI.screenWidth - 24f;
            dialog.windowRect.x = Mathf.Max(8f, right - dialog.windowRect.width);
        }
        static void CloseDialog()
        {
            if (dialog != null && Find.WindowStack.IsOpen(dialog)) dialog.Close(false);
            dialog = null;
        }

        static IEnumerable<object> SettleAfterChange(Dictionary<string, object> info, int beginsBefore, string hashBefore)
        {
            float start = Time.realtimeSinceStartup;
            var ok = new bool[1];
            foreach (var y in WaitFor(() => previewBegins > beginsBefore || WindowPending(), 15f, ok)) yield return y;
            if (ok[0])
            {
                foreach (var y in WaitFor(() => PreviewSettled(currentTile), 180f, ok)) yield return y;
                info["settle"] = ok[0] ? "map-preview-signal" : "timeout";
            }
            else
            {
                foreach (var y in WaitFor(() => PreviewHash() != hashBefore, 30f, ok)) yield return y;
                foreach (var y in Frames(180)) yield return y;
                info["settle"] = ok[0] ? "texture-change-fallback" : "no-preview-change-detected";
            }
            foreach (var y in WaitFor(InterpolationDone, 5f, ok)) yield return y;
            info["seconds"] = Time.realtimeSinceStartup - start;
            info["previewGenerationsStarted"] = previewBegins - beginsBefore;
        }

        static IEnumerable<object> Capture(string name, Dictionary<string, object> record, string key = "capture")
        {
            ClearLetters();
            EditWindow_Log.wantsToOpen = false;
            foreach (var log in Find.WindowStack.Windows.OfType<EditWindow_Log>().ToList()) { log.Close(false); logWindowsClosed++; }
            foreach (var y in Frames(2)) yield return y;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            string path = Path.Combine(folder, name + ".png");
            var away = new bool[1];
            foreach (var y in WaitFor(() => !CursorInsideGameWindow(), 10f, away)) yield return y;
            var layout = Layout();
            bool cursorInside = CursorInsideGameWindow();
            ScreenCapture.CaptureScreenshot(path);
            var ok = new bool[1];
            foreach (var y in WaitFor(() => PngSize(path) != null, 30f, ok)) yield return y;
            foreach (var y in Frames(3)) yield return y;
            var size = PngSize(path);
            record[key] = new Dictionary<string, object> { { "file", name + ".png" }, { "written", ok[0] }, { "width", size?[0] }, { "height", size?[1] }, { "cursorInsideGameWindow", cursorInside },
                { "bytes", File.Exists(path) ? new FileInfo(path).Length : 0 }, { "layout", layout } };
        }
        static int[] PngSize(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length < 24) return null;
                    var header = new byte[24];
                    if (stream.Read(header, 0, 24) < 24) return null;
                    return new[] { header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19], header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23] };
                }
            }
            catch (IOException) { return null; }
        }
        static Dictionary<string, object> Layout()
        {
            var layout = new Dictionary<string, object> { { "screen", Screen.width + "x" + Screen.height }, { "uiUnits", Verse.UI.screenWidth + "x" + Verse.UI.screenHeight },
                { "uiScale", Prefs.UIScale }, { "windows", Find.WindowStack.Windows.Select(w => (object)new Dictionary<string, object> { { "type", w.GetType().FullName }, { "rect", RectInfo(w.windowRect) } }).ToList() } };
            var preview = PreviewWindow();
            if (dialog != null && preview != null && Find.WindowStack.IsOpen(dialog)) layout["dialogOverlapsMapPreview"] = dialog.windowRect.Overlaps(preview.windowRect);
            return layout;
        }
        static Dictionary<string, object> RectInfo(Rect r) => new Dictionary<string, object> { { "x", r.x }, { "y", r.y }, { "width", r.width }, { "height", r.height } };

        // Map Preview access. The optional assembly's types appear only inside these non-inlined methods.
        static Window PreviewWindow() => windowType == null ? null : Find.WindowStack.Windows.FirstOrDefault(w => windowType.IsInstanceOfType(w));
        static object Widget(Window window) => window == null ? null : Traverse.Create(window).Field("_previewWidget").GetValue();
        static int ManagerTile() => Traverse.Create(managerType).Field("TileId").GetValue() is PlanetTile tile ? tile.tileId : -1;
        static bool WindowPending()
        {
            var widget = Widget(PreviewWindow());
            return ManagerTile() != currentTile || widget != null && Traverse.Create(widget).Field("AwaitingRequest").GetValue() != null;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool PreviewSettled(int tile)
        {
            var widget = Widget(PreviewWindow());
            if (widget == null || ManagerTile() != tile) return false;
            var traverse = Traverse.Create(widget);
            if (traverse.Field("AwaitingRequest").GetValue() != null || traverse.Property("PreviewMap").GetValue() == null) return false;
            return MapPreview.MapPreviewGenerator.CurrentRequest == null;
        }
        static bool InterpolationDone()
        {
            var widget = Widget(PreviewWindow());
            if (widget == null) return true;
            var spawn = Traverse.Create(widget).Field("SpawnInterpolator");
            return spawn.Field("finished").GetValue<bool>() && spawn.Field("value").GetValue<float>() >= 0.999f;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool PreviewIdle() => MapPreview.MapPreviewGenerator.CurrentRequest == null && (MapPreview.MapPreviewGenerator.Instance == null || MapPreview.MapPreviewGenerator.Instance.WaitUntilIdle(0));
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SubscribePreviewEvents() =>
            MapPreview.MapPreviewGenerator.OnBeginGenerating += new Action<MapPreview.MapPreviewRequest>(OnPreviewBegin);
        static void OnPreviewBegin(MapPreview.MapPreviewRequest request) => Interlocked.Increment(ref previewBegins);
        static bool CandidatesDone()
        {
            Invoke("UpdateRecommendationPreviews");
            var previews = Field("_recommendationPreviews") as RecommendationPreviews;
            return (previews == null || previews.Items.All(i => i.Complete)) && PreviewIdle();
        }
        static bool CandidatesRendered() => Field("_recommendationPreviews") is RecommendationPreviews previews && previews.Items.All(i => i.Texture != null && i.Error == null);

        static Color32[] PreviewPixels(out int width, out int height, out Map map)
        {
            width = height = 0; map = null;
            var widget = Widget(PreviewWindow());
            if (widget == null) return null;
            var traverse = Traverse.Create(widget);
            var texture = traverse.Property("Texture").GetValue<Texture2D>();
            map = traverse.Property("PreviewMap").GetValue<Map>();
            if (texture == null || map == null) return null;
            var coords = traverse.Field("TexCoords").GetValue<Rect>();
            width = Mathf.RoundToInt(coords.width * texture.width); height = Mathf.RoundToInt(coords.height * texture.height);
            var all = texture.GetPixels32();
            var pixels = new Color32[width * height];
            for (int z = 0; z < height; z++) Array.Copy(all, z * texture.width, pixels, z * width, width);
            return pixels;
        }
        static string PreviewHash() => PreviewPixels(out _, out _, out _) is Color32[] pixels ? Hash(pixels) : null;
        static Dictionary<string, object> SavePreview(string name)
        {
            var pixels = PreviewPixels(out int w, out int h, out Map map);
            if (pixels == null) return new Dictionary<string, object> { { "available", false } };
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try { copy.SetPixels32(pixels); copy.Apply(false); File.WriteAllBytes(Path.Combine(folder, name + ".png"), ImageConversion.EncodeToPNG(copy)); }
            finally { UnityEngine.Object.Destroy(copy); }
            // Cell classes straight from the preview map (terrain) and preview colours (solid rock), north row first.
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var legend = new Dictionary<string, object> { { "#", "solid rock (Map Preview stone colour)" } };
            var symbols = new Dictionary<string, char>();
            var counts = new Dictionary<string, object>();
            var rows = new List<string>();
            for (int z = h - 1; z >= 0; z--)
            {
                var row = new char[w];
                for (int x = 0; x < w; x++)
                {
                    string key;
                    if (IsStone(pixels[z * w + x])) { row[x] = '#'; key = "solid rock"; }
                    else
                    {
                        key = map.terrainGrid.TerrainAt(new IntVec3(x, 0, z))?.defName ?? "none";
                        if (!symbols.TryGetValue(key, out char symbol))
                        {
                            symbol = symbols.Count < alphabet.Length ? alphabet[symbols.Count] : '?';
                            symbols[key] = symbol; legend[symbol.ToString()] = key;
                        }
                        row[x] = symbol;
                    }
                    counts[key] = (counts.TryGetValue(key, out var n) ? (int)n : 0) + 1;
                }
                rows.Add(new string(row));
            }
            File.WriteAllText(Path.Combine(folder, name + "-cells.txt"), SimpleJson.Serialize(new Dictionary<string, object> { { "width", w }, { "height", h },
                { "rows", "north first" }, { "legend", legend } }) + "\n" + string.Join("\n", rows) + "\n");
            return new Dictionary<string, object> { { "available", true }, { "file", name + ".png" }, { "cells", name + "-cells.txt" }, { "width", w }, { "height", h },
                { "mapTile", (int)map.Tile }, { "sha256", Hash(pixels) }, { "cellCounts", counts } };
        }
        static bool IsStone(Color32 c) => Same(c, 0x36, 0x27, 0x1C) || Same(c, 0x4C, 0x34, 0x26) || Same(c, 0x1C, 0x13, 0x0E) || Same(c, 0x42, 0x37, 0x2B);
        static bool Same(Color32 c, byte r, byte g, byte b) => c.r == r && c.g == g && c.b == b;
        static List<object> SaveCandidates(string id)
        {
            var list = new List<object>();
            var plans = Field("_recommendations") as List<RecommendationPlan>;
            var previews = Field("_recommendationPreviews") as RecommendationPreviews;
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                var item = previews != null && i < previews.Items.Count ? previews.Items[i] : null;
                string file = null, hash = null;
                if (item?.Texture != null)
                {
                    file = id + "-option-" + (i + 1) + ".png";
                    File.WriteAllBytes(Path.Combine(folder, file), ImageConversion.EncodeToPNG(item.Texture));
                    hash = Hash(item.Texture.GetPixels32());
                }
                list.Add(new Dictionary<string, object> { { "option", i + 1 }, { "summary", plans[i].Summary }, { "commands", plans[i].Commands.ToList() },
                    { "complete", item?.Complete }, { "error", item?.Error }, { "warning", item?.Warning }, { "rejection", item?.Rejection },
                    { "seconds", item?.Seconds }, { "file", file }, { "sha256", hash } });
            }
            return list;
        }

        static Dictionary<string, object> GuideFacts(Window window)
        {
            var guide = Traverse.Create(window).Field("guide").GetValue() as RecommendationGuide;
            if (guide == null) return new Dictionary<string, object> { { "available", false } };
            return new Dictionary<string, object> { { "question", guide.Current.Title }, { "position", guide.Position + 1 }, { "questions", guide.Questions.Count } };
        }
        static void ClearLetters()
        {
            var stack = Find.LetterStack;
            if (stack == null) return;
            foreach (var letter in stack.LettersListForReading.ToList()) { stack.RemoveLetter(letter); lettersRemoved++; }
        }
        static IEnumerable<object> Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerable<object> WaitFor(Func<bool> condition, float seconds, bool[] ok)
        {
            float until = Time.realtimeSinceStartup + seconds;
            ok[0] = false;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until) yield break;
                yield return null;
            }
            ok[0] = true;
        }

        static string State() => State(currentTile);
        static string State(int tile) => MapStateCodec.Serialize(MapGenParams.CaptureState(tile));
        static List<string> Features(int tile) => Find.WorldGrid[tile].Mutators.Select(m => m.defName).ToList();
        static List<string> ChangedFields(string before, string after)
        {
            try { return MapStateCodec.ChangedFields(MapStateCodec.Deserialize(before), MapStateCodec.Deserialize(after)); }
            catch (Exception error) { return new List<string> { "unavailable: " + error.Message }; }
        }
        static List<ChatMessage> History() => dialog == null ? new List<ChatMessage>() : (List<ChatMessage>)Field("_history");
        static List<object> MessagesSince(int from) => History().Skip(from).Select(m => (object)new Dictionary<string, object> { { "role", m.Role }, { "content", m.Content } }).ToList();
        static int CallCount(string step) { lock (gate) return calls.Count(c => c.Step == step); }
        static List<object> CallsSince(string step, int from)
        {
            lock (gate) return calls.Where(c => c.Step == step).Skip(from).Select(c => (object)new Dictionary<string, object> { { "status", c.Status }, { "started", c.Started },
                { "ended", c.Ended }, { "modelVersion", c.ModelVersion }, { "inputTokens", c.InputTokens }, { "outputTokens", c.OutputTokens },
                { "thinkingTokens", c.ThinkingTokens }, { "error", c.Error }, { "rawModelText", c.Text } }).ToList();
        }
        static int ResponseCount(string step) { lock (gate) return responses.Count(r => r[0] == step); }
        static List<object> ResponsesSince(string step, int from) { lock (gate) return responses.Where(r => r[0] == step).Skip(from).Select(r => (object)r[1]).ToList(); }
        static object Field(string name) => AccessTools.Field(typeof(Dialog_TextToMap), name).GetValue(dialog);
        static void SetField(string name, object value) => AccessTools.Field(typeof(Dialog_TextToMap), name).SetValue(dialog, value);
        static object Invoke(string name, params object[] args) => AccessTools.Method(typeof(Dialog_TextToMap), name).Invoke(dialog, args);
        static string Now() => DateTime.UtcNow.ToString("o");
        static string Hash(Color32[] colors)
        {
            var bytes = new byte[colors.Length * 4];
            for (int i = 0; i < colors.Length; i++) { bytes[4 * i] = colors[i].r; bytes[4 * i + 1] = colors[i].g; bytes[4 * i + 2] = colors[i].b; bytes[4 * i + 3] = colors[i].a; }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
