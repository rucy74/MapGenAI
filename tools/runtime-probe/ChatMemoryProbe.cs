using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
#if BASELINE_PROBE
    // Old production DLLs lack the new provider-budget interfaces. Keep this
    // opt-in harness out of baseline assembly reflection/type discovery.
    static class ChatMemoryProbe
    {
        public static void Configure() { }
        public static void Tick() { }
        public static void Run(string output) { throw new NotSupportedException("Baseline probe cannot test new chat memory"); }
    }
#else
    // Only enabled in the existing marked disposable-profile harness. Provider
    // factory interception never falls through, including an unarmed request.
    static class ChatMemoryProbe
    {
        const string RoadRequest = "십자 모양 흙길을 만들어 줘";
        const string Correction = "아니, 정확히 중앙에서 십자로 교차하게 해 줘";
        const string Roads = "{\"action\":\"generate\",\"params\":{\"road_ops\":[{\"op\":\"add\",\"road\":{\"id\":\"west_east\",\"kind\":\"DirtPath\",\"seed\":17,\"points\":[[0,0.4],[1,0.6]]}},{\"op\":\"add\",\"road\":{\"id\":\"south_north\",\"kind\":\"DirtPath\",\"seed\":23,\"points\":[[0.4,0],[0.6,1]]}}]}}";
        const string Wrong = "{\"action\":\"generate\",\"params\":{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"cross_hill\",\"type\":\"bump\",\"position\":\"center\",\"size\":\"large\",\"strength\":\"strong\"}}]}}";
        const string Correct = "{\"action\":\"generate\",\"params\":{\"road_ops\":[{\"op\":\"update\",\"id\":\"west_east\",\"changes\":{\"route\":\"direct\",\"points\":[[0,0.5],[1,0.5]]}},{\"op\":\"update\",\"id\":\"south_north\",\"changes\":{\"route\":\"direct\",\"points\":[[0.5,0],[0.5,1]]}}]}}";
        const string Explanation = "{\"action\":\"ask\",\"message\":\"설정을 유지했습니다. / Settings are unchanged.\"}";
        const string Options = "{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.13}},{\"params\":{\"ore_density\":1.37}},{\"params\":{\"vegetation_density\":1.21}}]}";
        static readonly List<string> checks = new List<string>();
        static readonly List<object> measurements = new List<object>();
        static string folder, initial, roadState, world, rawPrefix;
        static int target, stage, frame, captureNumber, unexpectedCalls, rawCount;
        static bool active;
        static DateTime deadline;
        static Dialog_TextToMap dialog;
        static Scenario armed, current;

        sealed class Scenario
        {
            public readonly Queue<string> Replies = new Queue<string>();
            public readonly List<List<ChatMessage>> Outbound = new List<List<ChatMessage>>();
            public int Budget = 1000000, EditCalls, SummaryCalls;
            public volatile bool SummaryStarted, SummaryDelivered;
            public CancellationToken SummaryToken;
            public TaskCompletionSource<string> Delay;
        }
        sealed class Replay : ILLMClient, IContextBudgetClient
        {
            readonly Scenario scenario;
            public Replay(Scenario scenario) { this.scenario = scenario; }
            public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new ContextBudget { InputTokens = scenario?.Budget ?? 1000000, Known = true, Source = "isolated probe fixture" });
            }
            public async Task<string> SendChatAsync(List<ChatMessage> history, string prompt, CancellationToken token = default)
            {
                if (scenario == null) { Interlocked.Increment(ref unexpectedCalls); throw new Exception("Unarmed probe request; all real providers are disabled"); }
                bool summary = prompt.Contains("You compact conversation memory");
                int id = Interlocked.Increment(ref captureNumber);
                WriteFresh("outbound-" + id.ToString("D2") + "-" + (summary ? "summary" : "edit") + "-prompt.txt", prompt);
                WriteFresh("outbound-" + id.ToString("D2") + "-history.json", SimpleJson.Serialize(history));
                if (summary)
                {
                    Interlocked.Increment(ref scenario.SummaryCalls);
                    scenario.SummaryToken = token; scenario.SummaryStarted = true;
                    // Deliberately ignore cancellation at this transport boundary.
                    // The product must discard even a late successful provider reply.
                    string reply = scenario.Delay == null ? "{\"summary\":\"Earlier fixture background preferences. Current state and recent turns remain authoritative.\"}" : await scenario.Delay.Task;
                    scenario.SummaryDelivered = true;
                    return reply;
                }
                scenario.Outbound.Add(ConversationMemory.Copy(history));
                Interlocked.Increment(ref scenario.EditCalls);
                if (scenario.Replies.Count == 0) { Interlocked.Increment(ref unexpectedCalls); throw new Exception("Unexpected extra probe edit or repair call"); }
                return scenario.Replies.Dequeue();
            }
        }

        public static void Configure()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAIChatMemory", out _)) return;
            var h = new Harmony("choco.mapgenai.probe.chat-memory");
            h.Patch(AccessTools.Method(typeof(LLMClientFactory), "Create"), prefix: new HarmonyMethod(typeof(ChatMemoryProbe), nameof(Factory)));
        }
        static bool Factory(ref ILLMClient __result)
        { var next = armed; armed = null; __result = new Replay(next); return false; }
        static object Field(string name) => AccessTools.Field(typeof(Dialog_TextToMap), name).GetValue(dialog);
        static object Invoke(string name, params object[] args) => AccessTools.Method(typeof(Dialog_TextToMap), name).Invoke(dialog, args);
        static List<ChatMessage> Raw => (List<ChatMessage>)Field("_llmContext");
        static int UndoCount => ((ICollection)Field("_paramStack")).Count;
        static string State() => MapStateCodec.Serialize(MapGenParams.CaptureState(target));
        static string Prompt()
        {
            string value = (string)AccessTools.Method(typeof(Dialog_TextToMap), "BuildSystemPrompt").Invoke(null, new object[] { target });
            return value.Contains(ConversationMemory.Rules) ? value : value + ConversationMemory.Rules;
        }
        static string Metadata()
        {
            var tile = Find.WorldGrid[target];
            return SimpleJson.Serialize(new Dictionary<string, object> { { "mutators", tile.Mutators.Select(m => m.defName).ToArray() },
                { "hilliness", tile.hilliness.ToString() }, { "pollution", tile.pollution },
                { "roads", tile.Roads?.Select(r => ((int)r.neighbor) + ":" + r.road?.defName).OrderBy(r => r).ToArray() },
                { "rivers", tile.Rivers?.Select(r => ((int)r.neighbor) + ":" + r.river?.defName).OrderBy(r => r).ToArray() } });
        }
        static void WriteFresh(string name, string value)
        { using (var stream = new FileStream(Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream)) writer.Write(value); }
        static void Check(bool ok, string label)
        { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
        static void Next(int value) { stage = value; frame = Time.frameCount; deadline = DateTime.UtcNow.AddSeconds(90); }
        static Scenario Arm(params string[] replies)
        {
            MapGenAIMod.Settings.useSimpleMode = true;
            MapGenAIMod.Settings.geminiApiKey = "isolated-probe-placeholder";
            MapGenAIMod.Settings.conversationInputBudget = 0;
            current = new Scenario(); foreach (string reply in replies) current.Replies.Enqueue(reply);
            armed = current; return current;
        }
        public static void Run(string output)
        {
            folder = output; Application.runInBackground = true;
            try
            {
                var tile = Find.WorldGrid.Tiles.First(t => t.PrimaryBiome.defName == "TemperateForest" && t.hilliness == Hilliness.Flat &&
                    t.Mutators.Count == 0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                target = tile.tile; Find.WorldSelector.SelectedTile = target;
                Find.World.info.initialMapSize = new IntVec3(250, 1, 250);
                MapGenParams.RestoreSnapshot(new TileMapState { animalDensity = 1.13f }, target);
                dialog = new Dialog_TextToMap(); initial = State(); world = Metadata();
                WriteFresh("initial-state.json", initial);
                WriteFresh("initial-world.json", world);
                WriteFresh("production-system-prompt.txt", Prompt());
                WriteFresh("road-add-response.json", Roads); WriteFresh("cross-hill-response.json", Wrong); WriteFresh("cross-corrected-response.json", Correct);
                WriteFresh("fixture-note.txt", "Disposable TemperateForest flat inland tile; controlled initial animal_density=1.13 protects unrelated-state preservation. All provider traffic is intercepted and replayed. This probe validates dialog/state wiring, not rendered terrain or real provider quality.");
                active = true; Arm(Roads); Invoke("SendText", RoadRequest); Next(1);
            }
            catch (Exception error) { Finish(error); }
        }
        public static void Tick()
        {
            if (!active) return;
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Chat memory stage " + stage);
                Invoke("PollResponse");
                if (stage == 1)
                {
                    if ((bool)Field("_isWaiting")) return;
                    Check(current.EditCalls == 1 && current.SummaryCalls == 0, "initial road request sends one replay call without premature compaction");
                    Check(Raw.Any(m => m.Role == "user" && m.Content == RoadRequest) && Raw.Last().Content.StartsWith("APPLIED\n"), "successful road edit retains original user turn and confirmed receipt");
                    Check(UndoCount == 1 && MapGenParams.CaptureState(target).localRoads.Count == 2, "road add creates two authored roads and exactly one Undo entry");
                    roadState = State();
                    Raw.Add(new ChatMessage("user", Correction));
                    Invoke("HandleResponse", Wrong);
                    Check(State() == roadState && UndoCount == 1 && Metadata() == world, "cross_hill response is rejected before state Undo or world metadata mutation");
                    Check(Raw.Last().Content.StartsWith("NOT APPLIED\n") && !Raw.Last().Content.Contains("cross_hill"), "rejected proposal is recorded as a safe failure, not an applied command");
                    Arm(Wrong, Correct); Invoke("SendText", Correction); Next(2);
                }
                else if (stage == 2)
                {
                    if ((bool)Field("_isWaiting")) return;
                    Check(current.EditCalls == 2 && current.Replies.Count == 0, "wrong-family provider response is repaired once through the real structured pipeline");
                    Check(current.Outbound[0].Any(m => m.Content == RoadRequest) && current.Outbound[0].Any(m => m.Content.StartsWith("APPLIED\n")) && current.Outbound[0].Last().Content == Correction, "outbound followup includes earlier request receipt and current correction");
                    var before = MapStateCodec.Deserialize(roadState); var after = MapGenParams.CaptureState(target);
                    Check(string.Join(",", MapStateCodec.ChangedFields(before, after)) == "localRoads" && UndoCount == 2, "corrected cross changes only roads with one additional Undo entry");
                    Check(after.localRoads.Count == 2 && after.localRoads[0].id == "west_east" && after.localRoads[1].id == "south_north" &&
                        after.localRoads[0].seed == 17 && after.localRoads[1].seed == 23 && after.localRoads.All(r => r.kind == "DirtPath" && r.route == "direct"), "correction preserves both existing IDs kind and seed");
                    Check(SimpleJson.Serialize(after.localRoads[0].points) == "[[0,0.5],[1,0.5]]" && SimpleJson.Serialize(after.localRoads[1].points) == "[[0.5,0],[0.5,1]]", "cross endpoints are exactly orthogonal through map center");
                    WriteFresh("corrected-state.json", State());
                    int count = Raw.Count; Invoke("DoUndo");
                    Check(State() == roadState && UndoCount == 1 && Raw.Count > count && Raw.Last().Content.StartsWith("STATE REPLACED\n") && Raw.Any(m => m.Content == RoadRequest), "Undo restores roads while retaining transcript and adding a state-replacement boundary");
                    StartCompaction(false); Next(3);
                }
                else if (stage == 3)
                {
                    if ((bool)Field("_isWaiting")) return;
                    Check(current.SummaryCalls > 0 && current.EditCalls == 1 && Field("_conversationMemory") != null, "native request compacts old turns and installs a checkpoint before the one edit call");
                    Check(SimpleJson.Serialize(Raw.Take(rawCount).ToList()) == rawPrefix && Raw.Count == rawCount + 2, "compaction retains the entire raw transcript and appends only the request and answer");
                    Check(current.Outbound.Single().Count < Raw.Count && current.Outbound.Single()[0].Content.StartsWith("Earlier conversation summary"), "provider sees a compact prefix with recent turns instead of the full archived text");
                    Check(State() == roadState && UndoCount == 1, "explanation and memory compaction do not edit map state or Undo");
                    measurements.Add(new Dictionary<string, object> { { "rawMessages", Raw.Count }, { "outboundMessages", current.Outbound.Single().Count }, { "summaryCalls", current.SummaryCalls }, { "budget", current.Budget } });
                    WriteFresh("retained-transcript.json", SimpleJson.Serialize(Raw));
                    Invoke("DoReset");
                    Check(State() == initial && UndoCount == 0 && Raw.Count == 0 && Field("_conversationMemory") == null, "Reset restores initial state and explicitly clears both raw transcript and checkpoint");
                    StartCompaction(true); Next(4);
                }
                else if (stage == 4)
                {
                    if (!current.SummaryStarted) return;
                    Invoke("DoReset");
                    Check(current.SummaryToken.IsCancellationRequested && !(bool)Field("_isWaiting"), "Reset cancels the actual in-flight summary request");
                    current.Delay.SetResult("{\"summary\":\"Late reply which must never be installed\"}"); Next(5);
                }
                else if (stage == 5)
                {
                    if (!current.SummaryDelivered || Time.frameCount - frame < 30) return;
                    Check(current.EditCalls == 0 && Raw.Count == 0 && Field("_conversationMemory") == null && State() == initial && UndoCount == 0, "late summary cannot resurrect a reset checkpoint transcript edit or Undo entry");
                    Invoke("HandleResponse", Options);
                    Check(Field("_recommendations") != null && State() == initial && UndoCount == 0, "recommendation proposals remain unapplied before selection");
                    Invoke("HandleResponse", "{\"action\":\"revise\",\"option\":2,\"params\":{\"ore_density\":1.51}}");
                    Check(State() == initial && UndoCount == 0, "candidate refinement remains unapplied before selection");
                    int count = Raw.Count;
                    Invoke("ApplyRecommendation", 2);
                    Check(Raw.Count == count + 2 && Raw[count].Content.Contains("[UI selection]") && Raw.Last().Content.StartsWith("APPLIED\n"), "selection button records the explicit choice and applied receipt");
                    Check(MapGenParams.CaptureState(target).oreDensity == 1.51f && UndoCount == 1, "selection applies its exact revised option and one Undo snapshot");
                    Check(Raw.Last().Content.Contains("applied_plan") && Raw.Last().Content.Contains("1.51"), "applied receipt includes the refinement rather than only the original proposal");
                    Invoke("DoUndo"); Check(State() == initial && Raw.Last().Content.StartsWith("STATE REPLACED\n"), "selected recommendation remains undoable with conversation continuity");
                    Check(unexpectedCalls == 0 && Metadata() == world, "no real-provider fallback unexpected calls or world changes occurred");
                    Finish();
                }
            }
            catch (Exception error) { Finish(error); }
        }
        static void StartCompaction(bool delayed)
        {
            var scenario = Arm(Explanation);
            int fixedTokens = ConversationMemory.Estimate(Prompt(), new List<ChatMessage>());
            scenario.Budget = Math.Max(16384, fixedTokens * 2 + 8192);
            int charsPerTurn = Math.Max(2000, (scenario.Budget - fixedTokens) * 2 / 12);
            var synthetic = new List<ChatMessage>();
            for (int i = 0; i < 12; i++)
            {
                synthetic.Add(new ChatMessage("user", "Controlled old background fixture " + i + ": " + new string('x', charsPerTurn)));
                synthetic.Add(new ChatMessage("assistant", "No map change was requested or applied in this background fixture."));
            }
            Raw.InsertRange(0, synthetic);
            rawCount = Raw.Count; rawPrefix = SimpleJson.Serialize(Raw);
            if (delayed) scenario.Delay = new TaskCompletionSource<string>();
            Invoke("SendText", "현재 설정을 짧게 설명해 줘 / Explain the current settings briefly");
        }
        static void Finish(Exception error = null)
        {
            active = false;
            current?.Delay?.TrySetCanceled();
            try { WriteFresh("result.json", SimpleJson.Serialize(new Dictionary<string, object> { { "ok", error == null }, { "tile", target },
                { "checks", checks }, { "measurements", measurements }, { "unexpectedProviderCalls", unexpectedCalls }, { "error", error?.ToString() } })); }
            finally { Application.Quit(); }
        }
    }
#endif
}
