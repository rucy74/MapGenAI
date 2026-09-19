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
        static RecommendationPreviews.Item superseded;
        static readonly List<string> checks = new List<string>();
        static readonly List<object> measurements = new List<object>();
        static readonly string[] cases = { "recommend-plain", "recommend-existing", "native-features" };
        static string State() => MapStateCodec.Serialize(MapGenParams.CaptureState(target));
        static string Metadata() => SimpleJson.Serialize(new Dictionary<string, object> {
            {"mutators", original.Mutators.Select(d=>d.defName).ToArray()}, {"hilliness", original.hilliness.ToString()}, {"pollution", original.pollution},
            {"baseline", MapGenAIWorldComponent.Get().GetBaseline(target)}, {"last", MapGenAIWorldComponent.Get().GetLastApplied(target)} });
        static object Invoke(string name, params object[] args) => AccessTools.Method(typeof(Dialog_TextToMap), name).Invoke(dialog, args);
        static object Field(string name) => AccessTools.Field(typeof(Dialog_TextToMap), name).GetValue(dialog);
        static void Check(bool ok, string name) { checks.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) throw new Exception(name); }
        public static void Configure()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAICandidatePreviews", out _)) return;
            var h = new Harmony("choco.mapgenai.probe.candidate-previews");
            h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(CandidatePreviewProbe), nameof(WorldSeed)));
            h.Patch(AccessTools.Method(typeof(MapGenerator), "GenerateContentsIntoMap"), prefix: new HarmonyMethod(typeof(CandidatePreviewProbe), nameof(InspectWorker)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),prefix:new HarmonyMethod(typeof(CandidatePreviewProbe),nameof(ReplayFactory)));
        }
        static bool ReplayFactory(ref ILLMClient __result)
        { if(!active || replayReply==null)return true;__result=new ReplayClient();return false; }
        sealed class ReplayClient : ILLMClient
        {
            public System.Threading.Tasks.Task<string> SendChatAsync(List<ChatMessage> history,string prompt,System.Threading.CancellationToken token=default)
            {
                if(!prompt.Contains("PENDING RECOMMENDATION EDITOR:") || !prompt.Contains("Candidate 3 proposed state:"))throw new Exception("SendMessage omitted candidate context");
                string response=replayReply;replayReply=null;System.Threading.Interlocked.Increment(ref replayCalls);
                return System.Threading.Tasks.Task.FromResult(response);
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
            if (failNext) { failNext = false; throw new InvalidOperationException("Intentional candidate preview failure"); }
            // Ensure at least one UI tick can inspect the live world during this candidate job.
            System.Threading.Thread.Sleep(100);
        }
        public static void Run(string output, string input)
        {
            folder = output; fixtures = input; Application.runInBackground = true;
            typeof(Prefs).GetProperty("RunInBackground")?.SetValue(null, true, null);
            try
            {
                original = Find.WorldGrid.Tiles.First(t => t.PrimaryBiome.defName == "TemperateForest" && t.hilliness == Hilliness.Flat && t.Mutators.Count == 0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count == 0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                target = original.tile; Find.WorldSelector.SelectedTile = target;
                Find.World.info.initialMapSize = new IntVec3(250, 1, 250);
                var methods = new[] { AccessTools.PropertyGetter(typeof(Map), "TileInfo"), AccessTools.Method(typeof(MapGenerator), "GenerateContentsIntoMap"),
                    AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator"), "GeneratePreview") };
                File.WriteAllText(Path.Combine(folder, "patch-owners.txt"), string.Join("\n", methods.Select(m => m + ": " + string.Join(",", (IEnumerable<string>)Harmony.GetPatchInfo(m)?.Owners ?? new List<string>()))));
                active = true; StartCase();
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
                    Finish();
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
                            dialog.PostClose();StartCancellation();
                        }
                        catch(Exception error){Finish(error);}
                    }).Catch(Finish);
                }
            }
            catch(Exception error) { Finish(error); }
        }
        static void StartRefinement()
        {
            reply=File.ReadAllText(Path.Combine(fixtures,"recommend-plain-response.json"));
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(fixtures,"recommend-plain-before.json"))),target);
            before=State();worldBefore=Metadata();dialog=new Dialog_TextToMap();Invoke("HandleResponse",reply);
            previews=(RecommendationPreviews)Field("_recommendationPreviews");Find.WindowStack.Add(dialog);
            stage=20;deadline=DateTime.UtcNow.AddSeconds(120);
        }
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
            active=false; File.WriteAllText(Path.Combine(folder,"result.json"), SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"tile",target},{"checks",checks},{"measurements",measurements},{"error",error?.ToString()}})); Application.Quit();
        }
    }
}
