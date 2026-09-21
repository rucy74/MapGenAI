using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
    static class CandidatePreviewProbe
    {
        static string folder, fixtures, reply, before, worldBefore;
        static int target, stage, frame, option, caseIndex;
        static bool active, comparing, observedIsolation;
        static DateTime deadline;
        static Tile original;
        static Dialog_TextToMap dialog;
        static Dialog_RecommendationPreview zoom;
        static RecommendationPreviews previews, discarded;
        static Color32[][] pixels;
        static AuthoringResult reportBefore;
        static volatile bool failNext;
        static string naturalReply,preciseReply;
        static Texture2D[] retainedTextures;
        static Color32[] precisePixels,naturalPixels;
        static List<RecommendationPlan> plans => (List<RecommendationPlan>)Field("_recommendations");
        static volatile string replayReply;
        static int replayCalls;
        sealed class ReplayScenario
        {
            public string Response, Prompt, History;
            public bool PendingEditor;
            public int Calls;
            public volatile bool Started, Delivered;
            public System.Threading.CancellationToken Token;
            public System.Threading.Tasks.TaskCompletionSource<string> Delay;
        }
        static ReplayScenario armedReplay, controlReplay;
        static int unexpectedReplayCalls, candidateJobs, controlJobs, controlCalls;
        static string controlState, controlMetadata, controlUndo, controlInitial;
        static bool controlReady;
        static Rect controlWindow;
        const string ControlDraft = "이 문장은 아직 보내지 않은 입력 / unsent draft";
        const string ControlEdit = "{\"animal_density\":0.73}";
        const string ControlOptions = "{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.13}},{\"params\":{\"ore_density\":1.37}},{\"params\":{\"vegetation_density\":1.21}}]}";
        const string OtherControlOptions = "{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.26}},{\"params\":{\"ore_density\":1.61}},{\"params\":{\"vegetation_density\":1.42}}]}";
        static RecommendationPreviews.Item superseded;
        static int refinementRound, refinementRounds=1;
        static bool controlDone,lateDone;
        static volatile bool holdNext,workerHeld;
        static readonly System.Threading.AutoResetEvent releaseWorker=new System.Threading.AutoResetEvent(false);
        static object orphanRequest;
        static float queueLostAt;
        static RecommendationPreviews.Item timedOutItem;
        static readonly List<string> checks = new List<string>();
        static readonly List<object> measurements = new List<object>();
        static readonly string[] cases = { "recommend-plain", "recommend-existing", "native-features" };
        static string State() => MapStateCodec.Serialize(MapGenParams.CaptureState(target));
        static string Metadata() => SimpleJson.Serialize(new Dictionary<string, object> {
            {"mutators", original.Mutators.Select(d=>d.defName).ToArray()}, {"hilliness", original.hilliness.ToString()}, {"pollution", original.pollution},
            {"baseline", MapGenAIWorldComponent.Get().GetBaseline(target)}, {"last", MapGenAIWorldComponent.Get().GetLastApplied(target)} });
        static object Invoke(string name, params object[] args)
        {
            Trace("enter "+name);
            var result=AccessTools.Method(typeof(Dialog_TextToMap), name).Invoke(dialog, args);
            Trace("exit "+name);
            return result;
        }
        static void Trace(string message)
        {
            if(folder!=null)File.AppendAllText(Path.Combine(folder,"trace.txt"),DateTime.UtcNow.ToString("o")+" frame="+Time.frameCount+" stage="+stage+" round="+refinementRound+" "+message+"\n");
        }
        static object Field(string name) => AccessTools.Field(typeof(Dialog_TextToMap), name).GetValue(dialog);
        static void Check(bool ok, string name) { checks.Add((ok ? "PASS " : "FAIL ") + name); Trace(checks.Last()); if (!ok) throw new Exception(name); }
        public static void Configure()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAICandidatePreviews", out _)) return;
            var h = new Harmony("choco.mapgenai.probe.candidate-previews");
            h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(CandidatePreviewProbe), nameof(WorldSeed)));
            h.Patch(AccessTools.Method(typeof(MapGenerator), "GenerateContentsIntoMap"), prefix: new HarmonyMethod(typeof(CandidatePreviewProbe), nameof(InspectWorker)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),prefix:new HarmonyMethod(typeof(CandidatePreviewProbe),nameof(ReplayFactory)));
        }
        static bool ReplayFactory(ref ILLMClient __result)
        {
            // This patch exists only in the explicit disposable candidate probe. Never
            // fall through to a real provider, even if a test forgot to arm a reply.
            var scenario=armedReplay;armedReplay=null;
            if(scenario==null && replayReply!=null)
            { scenario=new ReplayScenario{Response=replayReply,PendingEditor=true};replayReply=null; }
            __result=new ReplayClient(scenario);return false;
        }
        sealed class ReplayClient : ILLMClient
        {
            readonly ReplayScenario scenario;
            public ReplayClient(ReplayScenario scenario){this.scenario=scenario;}
            public async System.Threading.Tasks.Task<string> SendChatAsync(List<ChatMessage> history,string prompt,System.Threading.CancellationToken token=default)
            {
                System.Threading.Interlocked.Increment(ref replayCalls);
                if(scenario==null || System.Threading.Interlocked.Increment(ref scenario.Calls)!=1)
                { System.Threading.Interlocked.Increment(ref unexpectedReplayCalls);throw new Exception("Unconfigured or repeated probe transport call; real providers are disabled"); }
                scenario.Prompt=prompt;scenario.History=SimpleJson.Serialize(history);scenario.Token=token;
                bool pending=prompt.Contains("PENDING RECOMMENDATION EDITOR:");
                if(pending!=scenario.PendingEditor || pending && !prompt.Contains("Candidate 3 proposed state:"))
                { System.Threading.Interlocked.Increment(ref unexpectedReplayCalls);throw new Exception("Probe transport received the wrong candidate context"); }
                scenario.Started=true;
                // Deliberately ignore cancellation here: RequestGate must reject a
                // provider which returns late despite its cancelled token.
                string result=scenario.Delay==null?scenario.Response:await scenario.Delay.Task;
                scenario.Delivered=true;return result;
            }
        }
        static void WorldSeed(ref string seedString) => seedString = "mapgenai-landforms-v1";
        static void InspectWorker(Map map)
        {
            var context = AccessTools.TypeByName("MapGenAI.Patches.CandidatePreviewContext");
            if (context == null || AccessTools.Field(context, "Current").GetValue(null) == null) return;
            var tile = map.TileInfo;
            if (ReferenceEquals(tile, original) || !ReferenceEquals(tile, Find.WorldGrid[target]) || !ReferenceEquals(tile, map.Tile.Tile))
                throw new Exception("Candidate tile is not isolated consistently on the worker");
            observedIsolation = true;
            System.Threading.Interlocked.Increment(ref candidateJobs);
            if (failNext) { failNext = false; throw new InvalidOperationException("Intentional candidate preview failure"); }
            // Ensure at least one UI tick can inspect the live world during this candidate job.
            System.Threading.Thread.Sleep(100);
        }
        public static void Run(string output, string input)
        {
            folder = output; fixtures = input; Application.runInBackground = true;
            if(GenCommandLine.TryGetCommandLineArg("mapgenAICandidateStressRounds",out string count))refinementRounds=int.Parse(count);
            typeof(Prefs).GetProperty("RunInBackground")?.SetValue(null, true, null);
            try
            {
                original = Find.WorldGrid.Tiles.First(t => t.PrimaryBiome.defName == "TemperateForest" && t.hilliness == Hilliness.Flat && t.Mutators.Count == 0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                target = original.tile; Find.WorldSelector.SelectedTile = target;
                Find.World.info.initialMapSize = new IntVec3(250, 1, 250);
                var methods = new[] { AccessTools.PropertyGetter(typeof(Map), "TileInfo"), AccessTools.Method(typeof(MapGenerator), "GenerateContentsIntoMap"),
                    AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator"), "GeneratePreview") };
                File.WriteAllText(Path.Combine(folder, "patch-owners.txt"), string.Join("\n", methods.Select(m => m + ": " + string.Join(",", (IEnumerable<string>)Harmony.GetPatchInfo(m)?.Owners ?? new List<string>()))));
                active = true;
                if(GenCommandLine.TryGetCommandLineArg("mapgenAICandidateControlsOnly",out _))StartControls();else StartCase();
            }
            catch (Exception e) { Finish(e); }
        }
        static void StartCase()
        {
            var id = cases[caseIndex];
            TileMapState state;
            if (id == "native-features")
            {
                state = new TileMapState(); state.mutators.Add("HotSprings");
                reply = "{\"action\":\"recommend\",\"options\":[{\"params\":{\"remove_mutators\":[\"HotSprings\"],\"mutators\":[\"Pond\"]}},{\"params\":{\"fertility_offset\":0.3}},{\"params\":{\"remove_mutators\":[\"HotSprings\"],\"elevation_shapes\":[{\"id\":\"pool\",\"type\":\"bump\",\"position\":\"top_right\",\"size\":\"small\",\"strength\":\"negative_strong\",\"fill\":\"water\"}]}}]}";
            }
            else
            {
                state = MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(fixtures, id + "-before.json")));
                reply = File.ReadAllText(Path.Combine(fixtures, id + "-response.json"));
            }
            MapGenParams.RestoreSnapshot(state, target);
            before = State(); worldBefore = Metadata();
            var reports = (System.Collections.Concurrent.ConcurrentDictionary<int, AuthoringResult>)AccessTools.Field(typeof(AuthoringGeneration), "Results").GetValue(null);
            reports.TryGetValue(target, out reportBefore);
            dialog = new Dialog_TextToMap(); Invoke("HandleResponse", reply);
            previews = (RecommendationPreviews)Field("_recommendationPreviews");
            Check(previews != null && previews.Items.Count == 3, id + ": three preview candidates prepared");
            Check(State() == before && Metadata() == worldBefore && ((ICollection)Field("_paramStack")).Count == 0, id + ": preparing candidates leaves world and Undo unchanged");
            Find.WindowStack.Add(dialog); stage = 0; deadline = DateTime.UtcNow.AddSeconds(100);
        }
        public static void Tick()
        {
            if (!active) return;
            try
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Candidate preview stage " + stage);
                if(dialog!=null)Invoke("PollResponse");
                if(stage>=50 && stage<=69){TickControls();return;}
                if (stage == 0)
                {
                    Invoke("UpdateRecommendationPreviews");
                    if (State() != before || Metadata() != worldBefore || !ReferenceEquals(Find.WorldGrid[target], original)) throw new Exception("Live editor/world changed during rendering");
                    if (previews.Items.Any(i => !i.Complete)) return;
                    Check(observedIsolation, "generation worker reads isolated candidate tiles");
                    Check(previews.Items.All(i => i.Texture != null && i.Error == null), cases[caseIndex] + ": all three renders succeeded: " + string.Join(";", previews.Items.Select(i=>i.Error)));
                    pixels = previews.Items.Select(i => i.Texture.GetPixels32()).ToArray();
                    Check(pixels.Select(Hash).Distinct().Count() == 3, cases[caseIndex] + ": three visually distinct results");
                    for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(folder, cases[caseIndex] + "-" + (i+1) + ".png"), ImageConversion.EncodeToPNG(previews.Items[i].Texture));
                    measurements.Add(new Dictionary<string,object>{{"id",cases[caseIndex]}, {"seconds",previews.Items.Select(i=>i.Seconds).ToArray()}, {"hashes",pixels.Select(Hash).ToArray()}, {"warnings",previews.Items.Select(i=>i.Warning).ToArray()}});
                    var reports = (System.Collections.Concurrent.ConcurrentDictionary<int, AuthoringResult>)AccessTools.Field(typeof(AuthoringGeneration), "Results").GetValue(null);
                    reports.TryGetValue(target, out var reportAfter);
                    Check(ReferenceEquals(reportBefore, reportAfter), "candidate report does not replace current map report");
                    frame = Time.frameCount; stage = 1;
                }
                else if (stage == 1 && Time.frameCount - frame >= 25)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, cases[caseIndex] + "-ui.png"));
                    frame = Time.frameCount; stage = 11;
                }
                else if (stage == 11 && Time.frameCount - frame >= 4)
                {
                    zoom = new Dialog_RecommendationPreview(previews.Items[0], 1, ()=>true, ()=>{}); Find.WindowStack.Add(zoom);
                    frame = Time.frameCount; stage = 2;
                }
                else if (stage == 2 && Time.frameCount - frame >= 25)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, cases[caseIndex] + "-zoom.png"));
                    frame = Time.frameCount; stage = 12;
                }
                else if (stage == 12 && Time.frameCount - frame >= 4)
                {
                    zoom.Close(false); dialog.Close(false); option = 0; stage = 3;
                }
                else if (stage == 3 && !comparing)
                {
                    if (option == 3)
                    {
                        caseIndex++;
                        if (caseIndex < cases.Length) { StartCase(); return; }
                        StartRefinement(); return;
                    }
                    MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(before), target);
                    dialog = new Dialog_TextToMap(); Invoke("HandleResponse", reply);
                    AccessTools.Field(typeof(Dialog_TextToMap), "_inputText").SetValue(dialog, (option+1)+"번"); Invoke("SendMessage");
                    Check(Field("_recommendations") == null && ((ICollection)Field("_paramStack")).Count == 1, "selecting candidate uses normal apply and Undo");
                    comparing = true;
                    int selectedOption = option;
                    var request = new MapPreview.MapPreviewRequest(Find.World.info.seedString, target, new IntVec2(250, 250)) { UseMinimalMapComponents=true, UseTrueTerrainColors=true };
                    MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result =>
                    {
                        try
                        {
                            var tex = new Texture2D(250,250); result.CopyToTexture(tex); tex.Apply(false);
                            var actual = tex.GetPixels32(); UnityEngine.Object.Destroy(tex);
                            int different = actual.Zip(pixels[selectedOption], (a,b)=>a.Equals(b)?0:1).Sum();
                            Check(different == 0, cases[caseIndex] + " option " + (option+1) + ": all 62500 pixels match selected normal preview (diff=" + different + ")");
                            Invoke("DoUndo"); Check(State() == before && Metadata() == worldBefore, "Undo restores full state and tile metadata after preview selection");
                            dialog.PostClose(); option++; comparing = false;
                        }
                        catch(Exception error) { Finish(error); }
                    }).Catch(Finish);
                }
                else if (stage == 4)
                {
                    if (Time.frameCount - frame < 70 || MapPreview.MapPreviewGenerator.CurrentRequest != null) return;
                    Check(discarded.Items.All(i=>i.Texture == null), "closed batch does not publish late textures");
                    Check(State() == before && Metadata() == worldBefore, "close during rendering restores original state");
                    dialog = new Dialog_TextToMap(); Invoke("HandleResponse", reply);
                    var size = Find.World.info.initialMapSize; Find.World.info.initialMapSize = new IntVec3(200,1,200);
                    Invoke("ApplyRecommendation", 1);
                    Check(State()==before && Field("_recommendations")==null, "map size change invalidates stale candidates before selection");
                    Find.World.info.initialMapSize=size; dialog.PostClose();
                    dialog = new Dialog_TextToMap(); Invoke("HandleResponse", reply);
                    string seed=Find.World.info.seedString;Find.World.info.seedString="changed-preview-seed";
                    Invoke("ApplyRecommendation", 1);
                    Check(State()==before && Field("_recommendations")==null, "seed change invalidates stale candidates before selection");
                    Find.World.info.seedString=seed;dialog.PostClose();
                    dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);previews=(RecommendationPreviews)Field("_recommendationPreviews");
                    failNext=true;stage=5;deadline=DateTime.UtcNow.AddSeconds(100);
                }
                else if(stage==5)
                {
                    Invoke("UpdateRecommendationPreviews");
                    if(previews.Items.Any(i=>!i.Complete))return;
                    Check(previews.Items[0].Error!=null && previews.Items[0].Texture==null && previews.Items.Skip(1).All(i=>i.Texture!=null),"one failed preview restores scope and remaining candidates render");
                    Check(State()==before && Metadata()==worldBefore,"preview exception leaves editor and native features unchanged");
                    Invoke("ApplyRecommendation",2);Invoke("DoUndo");Check(State()==before,"selection still works after another candidate preview fails");dialog.PostClose();
                    dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);discarded=(RecommendationPreviews)Field("_recommendationPreviews");Invoke("UpdateRecommendationPreviews");
                    Invoke("DoReset");Check(Field("_recommendations")==null,"reset clears pending previews and choices");dialog.PostClose();
                    dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);var old=(RecommendationPreviews)Field("_recommendationPreviews");
                    Invoke("HandleResponse",reply);Check(!ReferenceEquals(old,Field("_recommendationPreviews")) && !old.ContextMatches(),"new recommendation replaces and disposes previous batch");dialog.PostClose();
                    Check(!MapGenAI.ImageInput.ImageFeatureGate.Enabled, "input image feature remains off");
                    // Disposed batches still allow their in-flight Map Preview request to finish.
                    // Quitting Unity here aborts that worker and can crash the test process.
                    frame=Time.frameCount;stage=6;deadline=DateTime.UtcNow.AddSeconds(60);
                }
                else if(stage==6 && Time.frameCount-frame>=30)
                {
                    if(MapPreview.MapPreviewGenerator.CurrentRequest!=null || !MapPreview.MapPreviewGenerator.Init().WaitUntilIdle(0))return;
                    Check(discarded.Items.All(i=>i.Texture==null),"reset batch discards late textures before clean shutdown");
                    if(GenCommandLine.TryGetCommandLineArg("mapgenAICandidateQueueLoss",out _))StartQueueLoss();else Finish();
                }
                else if(stage==40)
                {
                    if(!workerHeld)return;
                    dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);previews=(RecommendationPreviews)Field("_recommendationPreviews");
                    Invoke("UpdateRecommendationPreviews");
                    var generator=MapPreview.MapPreviewGenerator.Instance;
                    var queue=(System.Collections.Concurrent.ConcurrentQueue<MapPreview.MapPreviewRequest>)AccessTools.Field(generator.GetType(),"_queuedRequests").GetValue(generator);
                    Check(queue.Count==1,"candidate queued behind held control preview");orphanRequest=queue.ToArray()[0];
                    generator.ClearQueue();Check(queue.Count==0,"external queue clear removes the candidate request without completion");
                    queueLostAt=Time.realtimeSinceStartup;releaseWorker.Set();stage=41;deadline=DateTime.UtcNow.AddSeconds(150);
                }
                else if(stage==41)
                {
                    Invoke("UpdateRecommendationPreviews");
                    if(!controlDone || Time.realtimeSinceStartup-queueLostAt<125f)return;
                    Check(previews.Items[0].Complete && previews.Items[0].Error!=null,"dropped request reaches a bounded error instead of waiting forever");
                    if(previews.Items.Skip(1).Any(i=>!i.Complete))return;
                    Check(previews.Items.Skip(1).All(i=>i.Texture!=null),"remaining candidates render after a dropped request");
                    Check(State()==before && Metadata()==worldBefore,"queue interruption preserves live state and native features");
                    timedOutItem=previews.Items[0];retainedTextures=previews.Items.Select(i=>i.Texture).ToArray();
                    Revise("{\"action\":\"revise\",\"option\":1,\"params\":{\"fertility_offset\":0.23}}");
                    var ticket=(MapPreview.MapPreviewRequest)orphanRequest;
                    MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(ticket).Then(new Action<MapPreview.MapPreviewResult>(LateReady));
                    Invoke("UpdateRecommendationPreviews");stage=42;deadline=DateTime.UtcNow.AddSeconds(60);
                }
                else if(stage==42)
                {
                    Invoke("UpdateRecommendationPreviews");if(!lateDone || previews.Items.Any(i=>!i.Complete))return;
                    Check(timedOutItem.Texture==null && timedOutItem.Error!=null,"late timed-out result cannot publish a stale texture");
                    Check(previews.Items[0].Texture!=null && ReferenceEquals(previews.Items[1].Texture,retainedTextures[1]) && ReferenceEquals(previews.Items[2].Texture,retainedTextures[2]),"replacement renders while other candidate textures stay unchanged");
                    var expected=plans[0].Resolve(MapGenParams.CaptureState(target));Invoke("ApplyRecommendation",1);
                    Check(State()==MapStateCodec.Serialize(expected),"candidate selection still applies exact state after queue recovery");Invoke("DoUndo");
                    Check(State()==before && Metadata()==worldBefore,"queue recovery selection remains one-step undoable");
                    dialog.PostClose();orphanRequest=null;
                    MapPreview.MapPreviewGenerator.OnBeginGenerating-=new Action<MapPreview.MapPreviewRequest>(HoldPreview);
                    stage=43;deadline=DateTime.UtcNow.AddSeconds(60);
                }
                else if(stage==43)
                {
                    if(MapPreview.MapPreviewGenerator.CurrentRequest==null && MapPreview.MapPreviewGenerator.Init().WaitUntilIdle(0))Finish();
                }
                else if(stage==20)
                {
                    Invoke("UpdateRecommendationPreviews");if(previews.Items.Any(i=>!i.Complete))return;
                    retainedTextures=previews.Items.Select(i=>i.Texture).ToArray();precisePixels=retainedTextures[2].GetPixels32();
                    File.WriteAllBytes(Path.Combine(folder,"refine-precise.png"),ImageConversion.EncodeToPNG(retainedTextures[2]));
                    string prompt=(string)AccessTools.Method(typeof(Dialog_TextToMap),"BuildSystemPrompt").Invoke(null,new object[]{target});
                    File.WriteAllText(Path.Combine(folder,"candidate-editor-prompt.txt"),prompt+RecommendationPlan.PendingInstruction(plans,MapGenParams.CaptureState(target)));
                    File.WriteAllText(Path.Combine(folder,"refinement-before.json"),before);
                    File.WriteAllText(Path.Combine(folder,"refinement-options-response.json"),reply);
                    var passage=plans[2].Resolve(MapGenParams.CaptureState(target)).elevationShapes.Single(s=>s.type=="passage");
                    naturalReply=ReadRevision("natural",passage.id,"medium");preciseReply=ReadRevision("precise",passage.id,"none");
                    SendRevision(naturalReply,"3번 통로를 자연스럽게 해 줘");stage=21;
                }
                else if(stage==21)
                {
                    if((bool)Field("_isWaiting"))return;
                    Invoke("UpdateRecommendationPreviews");if(!previews.Items[2].Complete)return;
                    Check(previews.Items[2].Texture!=null,"natural candidate preview rendered");
                    naturalPixels=previews.Items[2].Texture.GetPixels32();
                    Check(Hash(naturalPixels)!=Hash(precisePixels),"natural passage visibly changes candidate terrain");
                    Check(ReferenceEquals(retainedTextures[0],previews.Items[0].Texture) && ReferenceEquals(retainedTextures[1],previews.Items[1].Texture),"refining option 3 preserves options 1 and 2 textures");
                    Check(State()==before && Metadata()==worldBefore && ((ICollection)Field("_paramStack")).Count==0,"revision leaves current map world and Undo unchanged");
                    var proposed=plans[2].Resolve(MapGenParams.CaptureState(target));
                    File.WriteAllText(Path.Combine(folder,"refine-natural-after.json"),MapStateCodec.Serialize(proposed));
                    File.WriteAllBytes(Path.Combine(folder,"refine-natural.png"),ImageConversion.EncodeToPNG(previews.Items[2].Texture));
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,"refine-natural-ui.png"));frame=Time.frameCount;stage=22;
                }
                else if(stage==22 && Time.frameCount-frame>=4)
                {
                    var prior=plans[2];var texture=previews.Items[2].Texture;
                    Revise("{\"action\":\"revise\",\"option\":3,\"params\":{\"mutators\":[\"HotSprings\",\"Pond\"]}}");
                    Check(ReferenceEquals(prior,plans[2]) && ReferenceEquals(texture,previews.Items[2].Texture),"invalid candidate refinement preserves last valid plan and texture");
                    AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidates").SetValue(dialog,plans);
                    Invoke("HandleResponse","{\"action\":\"generate\",\"params\":{\"fertility_offset\":0.8}}");
                    Check(State()==before && ReferenceEquals(prior,plans[2]),"immediate generate response during candidate editing cannot change live map");
                    SendRevision(preciseReply,"3번 통로 다시 반듯하게 해 줘");stage=23;
                }
                else if(stage==23)
                {
                    if((bool)Field("_isWaiting"))return;
                    Invoke("UpdateRecommendationPreviews");if(!previews.Items[2].Complete)return;
                    Check(previews.Items[2].Texture!=null && Hash(previews.Items[2].Texture.GetPixels32())==Hash(precisePixels),"precise-again revision restores every original preview pixel");
                    File.WriteAllText(Path.Combine(folder,"refine-precise-after.json"),MapStateCodec.Serialize(plans[2].Resolve(MapGenParams.CaptureState(target))));
                    SendRevision(naturalReply,"3번 통로 다시 자연스럽게");stage=24;
                }
                else if(stage==24)
                {
                    if((bool)Field("_isWaiting"))return;
                    if(superseded==null)
                    {
                        Invoke("UpdateRecommendationPreviews");superseded=previews.Items[2];
                        Revise(preciseReply);Revise(naturalReply);
                        Check(!ReferenceEquals(superseded,previews.Items[2]),"new revision supersedes an in-flight candidate image");
                    }
                    Invoke("UpdateRecommendationPreviews");if(!previews.Items[2].Complete)return;
                    Check(superseded.Texture==null,"superseded request cannot publish a stale texture");
                    Check(Hash(previews.Items[2].Texture.GetPixels32())==Hash(naturalPixels),"repeated natural revision is deterministic");
                    var expected=plans[2].Resolve(MapGenParams.CaptureState(target));
                    Invoke("ApplyRecommendation",3);
                    Check(State()==MapStateCodec.Serialize(expected) && ((ICollection)Field("_paramStack")).Count==1,"refined sequence applies atomically with one Undo entry");
                    stage=25;
                    var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,target,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
                    // Capture only core types, never optional MapPreview types in closure fields.
                    int refinedOption=3;
                    MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>
                    {
                        try
                        {
                            var tex=new Texture2D(250,250);result.CopyToTexture(tex);tex.Apply(false);
                            Check(refinedOption==3 && Hash(tex.GetPixels32())==Hash(naturalPixels),"refined candidate matches all 62500 normal preview pixels after selection");UnityEngine.Object.Destroy(tex);
                            Invoke("DoUndo");Check(State()==before && Metadata()==worldBefore,"one Undo restores the map before all candidate refinements");
                            Check(replayCalls==3,"SendMessage and StructuredChat route all three recorded revisions without extra selection calls");
                            dialog.Close(false);
                            dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);
                            var firstBefore=plans[0].Resolve(MapGenParams.CaptureState(target));
                            string fertilityFile=Path.Combine(fixtures,"refine-fertility-response.json");
                            string fertility=File.Exists(fertilityFile)?File.ReadAllText(fertilityFile):"{\"action\":\"revise\",\"option\":1,\"params\":{\"fertility_offset\":0.2}}";
                            Revise(fertility);var firstAfter=plans[0].Resolve(MapGenParams.CaptureState(target));
                            Check(firstAfter.fertilityOffset>firstBefore.fertilityOffset && State()==before,"generic fertility refinement changes only proposed state");
                            Invoke("ApplyRecommendation",1);Check(State()==MapStateCodec.Serialize(firstAfter),"generic refined option applies complete candidate");
                            Invoke("DoUndo");Check(State()==before && Metadata()==worldBefore,"generic refinement Undo restores original map");
                            dialog.PostClose();
                            if(refinementRound<refinementRounds)StartRefinement();else StartControls();
                        }
                        catch(Exception error){Finish(error);}
                    }).Catch(Finish);
                }
            }
            catch(Exception error) { Finish(error); }
        }
        static void StartRefinement()
        {
            refinementRound++;replayCalls=0;superseded=null;
            Trace("start refinement");
            reply=File.ReadAllText(Path.Combine(fixtures,"recommend-plain-response.json"));
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(fixtures,"recommend-plain-before.json"))),target);
            before=State();worldBefore=Metadata();dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);
            previews=(RecommendationPreviews)Field("_recommendationPreviews");Find.WindowStack.Add(dialog);
            stage=20;deadline=DateTime.UtcNow.AddSeconds(120);
        }
        static void SetField(string name,object value)=>AccessTools.Field(typeof(Dialog_TextToMap),name).SetValue(dialog,value);
        static string UndoState()=>SimpleJson.Serialize(((IEnumerable)Field("_paramStack")).Cast<TileMapState>().Select(s=>s==null?null:MapStateCodec.Serialize(s)).ToArray());
        static void ControlUnchanged(string label)
        {
            Check(State()==controlState && Metadata()==controlMetadata && UndoState()==controlUndo &&
                (bool)Field("_paramsReady")==controlReady && (string)Field("_inputText")==ControlDraft &&
                MapStateCodec.Serialize((TileMapState)Field("_initialSnapshot"))==controlInitial,
                label+": preserves applied map, metadata, Undo contents, initial snapshot, ready state and unsent draft");
        }
        static void ArmControl(string response,bool pending=false,bool delayed=false)
        {
            MapGenAIMod.Settings.useSimpleMode=true;MapGenAIMod.Settings.geminiApiKey="probe-placeholder";
            controlReplay=new ReplayScenario{Response=response,PendingEditor=pending,
                Delay=delayed?new System.Threading.Tasks.TaskCompletionSource<string>():null};
            armedReplay=controlReplay;
        }
        static void ControlsStage(int value)
        {stage=value;frame=Time.frameCount;deadline=DateTime.UtcNow.AddSeconds(120);}
        static void StartControls()
        {
            reply=File.ReadAllText(Path.Combine(fixtures,"recommend-plain-response.json"));
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(fixtures,"recommend-plain-before.json"))),target);
            before=State();worldBefore=Metadata();controlInitial=before;
            dialog=new Dialog_TextToMap();Find.WindowStack.Add(dialog);controlWindow=dialog.windowRect;
            Invoke("ApplyEdits",new List<MapParamsData>{MapParameterParser.Parse(SimpleJson.Parse(ControlEdit))});
            Check(((ICollection)Field("_paramStack")).Count==1,"controls fixture starts after an applied edit with a real Undo entry");
            controlState=State();controlMetadata=Metadata();controlUndo=UndoState();controlReady=(bool)Field("_paramsReady");
            SetField("_inputText",ControlDraft);Invoke("HandleResponse",reply);
            previews=(RecommendationPreviews)Field("_recommendationPreviews");controlCalls=replayCalls;
            ControlsStage(50);
        }
        static void TickControls()
        {
            if(stage==50)
            {
                Invoke("UpdateRecommendationPreviews");if(previews.Items.Any(i=>!i.Complete))return;
                Check(previews.Items.All(i=>i.Texture!=null && i.Error==null),"controls: all three initial previews rendered");
                ControlUnchanged("recommendation preparation after a prior applied edit");
                retainedTextures=previews.Items.Select(i=>i.Texture).ToArray();pixels=retainedTextures.Select(t=>t.GetPixels32()).ToArray();
                controlJobs=candidateJobs;ControlsStage(51);
            }
            else if(stage==51 && Time.frameCount-frame>=25)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,"controls-expanded-ui.png"));ControlsStage(52);
            }
            else if(stage==52 && Time.frameCount-frame>=4)
            {
                SetField("_previewsCollapsed",true);ControlsStage(53);
            }
            else if(stage==53 && Time.frameCount-frame>=25)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,"controls-folded-ui.png"));ControlsStage(54);
            }
            else if(stage==54 && Time.frameCount-frame>=4)
            {
                SetField("_previewsCollapsed",false);
                dialog.windowRect=new Rect(Math.Max(0,(Verse.UI.screenWidth-620)/2f),Math.Max(0,(Verse.UI.screenHeight-520)/2f),620,520);
                ControlsStage(55);
            }
            else if(stage==55 && Time.frameCount-frame>=25)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,"controls-small-620x520-ui.png"));ControlsStage(56);
            }
            else if(stage==56 && Time.frameCount-frame>=4)
            {
                dialog.windowRect=controlWindow;
                Check(ReferenceEquals(previews,Field("_recommendationPreviews")) && candidateJobs==controlJobs && replayCalls==controlCalls &&
                    previews.Items.Select((item,i)=>ReferenceEquals(item.Texture,retainedTextures[i]) && Hash(item.Texture.GetPixels32())==Hash(pixels[i])).All(x=>x),
                    "fold/unfold and small-window layout retain preview owner and every pixel without generation or API calls");
                var owner=previews;
                zoom=new Dialog_RecommendationPreview(previews.Items[0],1,()=>ReferenceEquals(Field("_recommendationPreviews"),owner) && owner.ContextMatches(),()=>Invoke("ApplyRecommendation",1));
                Find.WindowStack.Add(zoom);
                discarded=previews;Invoke("DismissRecommendations");
                Check(plans==null && Field("_recommendationPreviews")==null && !discarded.ContextMatches() && discarded.Items.All(i=>i.Texture==null),
                    "discard button removes choices and disposes rendered textures");
                ControlUnchanged("discard button");Check(replayCalls==controlCalls,"discard button is local and makes no API call");
                ControlsStage(57);
            }
            else if(stage==57 && Time.frameCount-frame>=25)
            {
                Check(!Find.WindowStack.Windows.Contains(zoom),"discard invalidates an already open enlarged preview");
                Invoke("DoUndo");Check(State()==controlInitial && ((ICollection)Field("_paramStack")).Count==0,"Undo after discard still undoes the prior real map edit");
                Invoke("ApplyEdits",new List<MapParamsData>{MapParameterParser.Parse(SimpleJson.Parse(ControlEdit))});
                ControlUnchanged("restored prior real edit");Invoke("HandleResponse",reply);
                discarded=(RecommendationPreviews)Field("_recommendationPreviews");Invoke("UpdateRecommendationPreviews");
                ArmControl("{\"action\":\"revise\",\"option\":3,\"params\":{\"fertility_offset\":0.17}}",true,true);
                Invoke("SendText","3번에 비옥한 땅을 조금 늘려 줘");ControlsStage(58);
            }
            else if(stage==58)
            {
                if(!controlReplay.Started)return;
                Check((bool)Field("_isWaiting") && plans!=null,"delayed revision keeps candidates until explicit discard");
                Invoke("DismissRecommendations");
                Check(controlReplay.Token.IsCancellationRequested && !(bool)Field("_isWaiting") && Field("_requestedCandidates")==null &&
                    Field("_repairRecommendations")==null && Field("_explainInvalidReply")==null,"discard cancels in-flight transport and candidate response handlers");
                ControlUnchanged("discard during provider and preview work");
                controlReplay.Delay.SetResult(controlReplay.Response);ControlsStage(59);
            }
            else if(stage==59)
            {
                if(!controlReplay.Delivered || Time.frameCount-frame<30 || MapPreview.MapPreviewGenerator.CurrentRequest!=null || !MapPreview.MapPreviewGenerator.Init().WaitUntilIdle(0))return;
                Check(plans==null && Field("_recommendationPreviews")==null && discarded.Items.All(i=>i.Texture==null),"late provider and preview completions cannot restore discarded candidates");
                ControlUnchanged("late completion after discard");
                Invoke("HandleResponse",reply);
                var gate=(RequestGate)Field("_requests");var ticket=gate.Begin();gate.Complete(ticket,OtherControlOptions,null);
                Invoke("DismissRecommendations");Invoke("PollResponse");
                Check(plans==null && gate.Take()==null,"discard also removes a provider reply already queued before polling");
                ControlUnchanged("queued reply discard");
                Invoke("HandleResponse",reply);discarded=(RecommendationPreviews)Field("_recommendationPreviews");
                ArmControl(ControlOptions);controlCalls=replayCalls;Invoke("RequestNewRecommendations");
                Check(plans==null && (bool)Field("_isWaiting") && (bool)Field("_recommendationsRequested") && Field("_requestedCandidates")==null && !discarded.ContextMatches(),
                    "reroll button starts a fresh required recommendation against the applied map");
                ControlUnchanged("reroll button while waiting");ControlsStage(60);
            }
            else if(stage==60)
            {
                if((bool)Field("_isWaiting"))return;
                Check(controlReplay.Delivered && controlReplay.Calls==1 && replayCalls==controlCalls+1 && plans?.Count==3,
                    "reroll button obtains exactly one replay response containing three new candidates");
                Check(!controlReplay.Prompt.Contains("PENDING RECOMMENDATION EDITOR:"),
                    "reroll transport does not enter the candidate revision protocol");
                Check(plans.All(p=>p.Resolve(MapGenParams.CaptureState(target)).animalDensity==.73f),"new candidates all preserve the already applied direct edit");
                File.WriteAllText(Path.Combine(folder,"controls-button-reroll-prompt.txt"),controlReplay.Prompt);
                File.WriteAllText(Path.Combine(folder,"controls-button-reroll-history.json"),controlReplay.History);
                ControlUnchanged("reroll button response");previews=(RecommendationPreviews)Field("_recommendationPreviews");
                ArmControl(OtherControlOptions);controlCalls=replayCalls;Invoke("SendText","다른 걸 추천해 줘");
                Check(plans==null && Field("_requestedCandidates")==null && (bool)Field("_recommendationsRequested") && !previews.ContextMatches(),
                    "typed reroll replaces the candidate batch and requires a fresh recommendation");ControlsStage(61);
            }
            else if(stage==61)
            {
                if((bool)Field("_isWaiting"))return;
                Check(controlReplay.Delivered && controlReplay.Calls==1 && replayCalls==controlCalls+1 && plans?.Count==3 &&
                    !controlReplay.Prompt.Contains("PENDING RECOMMENDATION EDITOR:"),"typed reroll reaches the fresh replay transport exactly once");
                File.WriteAllText(Path.Combine(folder,"controls-text-reroll-prompt.txt"),controlReplay.Prompt);
                ControlUnchanged("typed reroll response");previews=(RecommendationPreviews)Field("_recommendationPreviews");ControlsStage(62);
            }
            else if(stage==62)
            {
                Invoke("UpdateRecommendationPreviews");if(previews.Items.Any(i=>!i.Complete))return;
                Check(previews.Items.All(i=>i.Texture!=null && i.Error==null),"new candidate previews render after both reroll paths");
                var expected=plans[1].Resolve(MapGenParams.CaptureState(target));int calls=replayCalls;
                Invoke("ApplyRecommendation",2);
                Check(State()==MapStateCodec.Serialize(expected) && ((ICollection)Field("_paramStack")).Count==2 && plans==null && replayCalls==calls,
                    "rerolled candidate applies its exact stored state with one Undo entry and no extra API call");
                Invoke("DoUndo");ControlUnchanged("Undo of rerolled candidate");
                Invoke("HandleResponse",reply);calls=replayCalls;Invoke("SendText","추천 모두 취소");
                Check(plans==null && replayCalls==calls,"typed discard handles pending candidates locally");ControlUnchanged("typed discard");
                Invoke("DoUndo");Check(State()==controlInitial && ((ICollection)Field("_paramStack")).Count==0,"original pre-recommendation edit is still separately undoable");
                Check(unexpectedReplayCalls==0,"no unconfigured replay or unintended transport retry occurred");
                dialog.Close(false);ControlsStage(63);
            }
            else if(stage==63)
            {
                if(MapPreview.MapPreviewGenerator.CurrentRequest!=null || !MapPreview.MapPreviewGenerator.Init().WaitUntilIdle(0))return;
                if(GenCommandLine.TryGetCommandLineArg("mapgenAICandidateControlsOnly",out _))Finish();else StartCancellation();
            }
        }
        static void HoldPreview(MapPreview.MapPreviewRequest request)
        {
            if(!holdNext)return;holdNext=false;workerHeld=true;
            try{if(!releaseWorker.WaitOne(15000))throw new TimeoutException("Queue-loss control gate was not released");}
            finally{workerHeld=false;}
        }
        static void StartQueueLoss()
        {
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(before),target);worldBefore=Metadata();
            // Explicit delegate construction avoids compiler-cached fields referring to the
            // optional MapPreview assembly before Lunar has loaded it during startup.
            holdNext=true;MapPreview.MapPreviewGenerator.OnBeginGenerating+=new Action<MapPreview.MapPreviewRequest>(HoldPreview);
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,target,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(new Action<MapPreview.MapPreviewResult>(ControlReady));
            stage=40;deadline=DateTime.UtcNow.AddSeconds(60);
        }
        static void ControlReady(MapPreview.MapPreviewResult result){controlDone=true;}
        static void LateReady(MapPreview.MapPreviewResult result){lateDone=true;}
        static string ReadRevision(string name,string id,string roughness)
        {
            string path=Path.Combine(fixtures,"refine-"+name+"-response.json");
            return File.Exists(path)?File.ReadAllText(path):"{\"action\":\"revise\",\"option\":3,\"params\":{\"shape_ops\":[{\"op\":\"update\",\"id\":\""+id+"\",\"changes\":{\"edge_roughness\":\""+roughness+"\"}}]}}";
        }
        static void Revise(string response)
        {
            AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidates").SetValue(dialog,plans);Invoke("HandleResponse",response);
        }
        static void SendRevision(string response,string text)
        {
            // This marked disposable profile uses a replay transport, never the user's API config.
            MapGenAIMod.Settings.useSimpleMode=true;MapGenAIMod.Settings.geminiApiKey="probe-placeholder";
            replayReply=response;AccessTools.Field(typeof(Dialog_TextToMap),"_inputText").SetValue(dialog,text);Invoke("SendMessage");
            Check((bool)Field("_isWaiting") && plans!=null,"candidate request keeps choices visible while waiting");
        }
        static void StartCancellation()
        {
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(before),target); worldBefore=Metadata();
            dialog=new Dialog_TextToMap(); Invoke("HandleResponse",reply); discarded=(RecommendationPreviews)Field("_recommendationPreviews");
            Invoke("UpdateRecommendationPreviews"); dialog.PostClose(); frame=Time.frameCount;stage=4;deadline=DateTime.UtcNow.AddSeconds(60);
        }
        static string Hash(Color32[] colors)
        {
            var bytes=new byte[colors.Length*4];for(int i=0;i<colors.Length;i++){bytes[4*i]=colors[i].r;bytes[4*i+1]=colors[i].g;bytes[4*i+2]=colors[i].b;bytes[4*i+3]=colors[i].a;}
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        static void Finish(Exception error=null)
        {
            releaseWorker.Set();
            controlReplay?.Delay?.TrySetCanceled();
            active=false; File.WriteAllText(Path.Combine(folder,"result.json"), SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"tile",target},{"checks",checks},{"measurements",measurements},{"error",error?.ToString()}})); Application.Quit();
        }
    }
}
