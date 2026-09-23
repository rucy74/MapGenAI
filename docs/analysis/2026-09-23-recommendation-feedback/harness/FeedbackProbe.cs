using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MapGenAI;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.GuidedDemo
{
    [StaticConstructorOnStartup]
    public static class Demo
    {
        static string output, initial;
        static string fixtures="C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/outputs/mapgenai-guided-recommendations/live-demo/native-01";
        static bool pendingChecked;
        static Dialog_RecommendationFeedback feedback;
        static TaskCompletionSource<string> delayedQuestion;
        static bool booting, active;
        static int stage, frame, mode, target, question, calls;
        static DateTime deadline;
        static Dialog_TextToMap dialog;
        static Dialog_RecommendationGuide guideWindow;
        static RecommendationGuide guide;
        static readonly string[] choices={"space","edge","small","mixed","subtle","layout"};
        static readonly List<string> checks=new List<string>();
        static string Prefix=>mode==0?"quick":"guided";
        static object Field(string name)=>AccessTools.Field(typeof(Dialog_TextToMap),name).GetValue(dialog);
        static object Invoke(string name,params object[] args)=>AccessTools.Method(typeof(Dialog_TextToMap),name).Invoke(dialog,args);
        static string State()=>MapStateCodec.Serialize(MapGenParams.CaptureState(target));
        static void Save(string name,string text)=>File.WriteAllText(Path.Combine(output,name),text);
        static void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);if(!ok)throw new Exception(text);}
        static void Next(int value){stage=value;frame=Time.frameCount;deadline=DateTime.UtcNow.AddMinutes(6);Save("progress.txt",Prefix+" stage="+stage+" calls="+calls+" utc="+DateTime.UtcNow.ToString("o"));}
        static void Capture(string name)=>Save(name+"-headless.txt","Headless test; this is not a UI screenshot.");

        static Demo()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAIProbe",out output))return;
            if(!GenCommandLine.TryGetCommandLineArg("savedatafolder",out string profile) || !File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))throw new Exception("Marked disposable profile required");
            
            var h=new Harmony("choco.mapgenai.guided-demo");
            h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(Demo),nameof(Seed)));
            h.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),postfix:new HarmonyMethod(typeof(Demo),nameof(Wrap)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString)=>seedString="mapgenai-guided-live-20260923";
        static void Wrap(ref ILLMClient __result){__result=new RecordedClient(__result,Prefix);}
        sealed class RecordedClient:ILLMClient,IContextBudgetClient
        {
            readonly string prefix;
            public RecordedClient(ILLMClient unused,string prefix){this.prefix=prefix;}
            public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)=>Task.FromResult(new ContextBudget{InputTokens=1000000,Known=true,Source="headless replay"});
            public Task<string> SendChatAsync(List<ChatMessage> history,string prompt,CancellationToken cancellationToken=default)
            {
                int n=Interlocked.Increment(ref calls);
                if(n>8)throw new Exception("Unexpected replay request");
                Save("outbound-"+n+".json",SimpleJson.Serialize(history.Select(m=>new Dictionary<string,object>{{"role",m.Role},{"content",m.Content}}).ToList()));
                if(prompt.StartsWith("Ask one or two"))return delayedQuestion?.Task ?? Task.FromResult("{\"questions\":[{\"question\":\"Which outline?\",\"options\":[\"Slight bends\",\"Broad curves\"]}]}");
                if(n>2)return Task.FromResult("{\"action\":\"generate\",\"params\":{\"fertility_offset\":0.7}}");
                return Task.FromResult(File.ReadAllText(Path.Combine(fixtures,prefix+(prefix=="quick"?"-call-1-response.json":"-call-2-response.json"))));
            }
        }
        static void Start()
        {
            try
            {
                Directory.CreateDirectory(output);Application.runInBackground=true;Prefs.RunInBackground=true;
                MapGenAIMod.Settings.useSimpleMode=true;
                MapGenAIMod.Settings.geminiApiKey="headless-fixture-no-network";
                Save("model.txt","No real provider calls; recorded September 23 results replayed.");
                booting=true;
                LongEventHandler.QueueLongEvent(()=>{
                    try{Root_Play.SetupForQuickTestPlay();Find.GameInitData.mapSize=100;Find.GameInitData.PrepForMapGen();Find.Scenario.PreMapGenerate();}
                    catch(Exception e){Finish(e);}
                },"Play","MapGenAI live recommendation comparison",true,null);
            }catch(Exception e){Finish(e);}
        }
        public static void Started()
        {
            if(!booting)return;booting=false;
            LongEventHandler.ExecuteWhenFinished(()=>{
                try
                {
                    var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="TemperateForest" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                    target=tile.tile;Find.WorldSelector.SelectedTile=target;Find.World.info.initialMapSize=new IntVec3(250,1,250);
                    MapGenParams.RestoreSnapshot(new TileMapState(),target);initial=State();
                    VerifyWaterAudit();
                    Save("initial-state.json",initial);
                    Save("tile.json",SimpleJson.Serialize(new Dictionary<string,object>{{"tile",target},{"biome",tile.PrimaryBiome.defName},{"hilliness",tile.hilliness.ToString()},{"river",FeaturePolicy.HasRiver(tile)},{"coast",FeaturePolicy.WaterNeighbors(tile).Count},{"seed",Find.World.info.seedString},{"size",250},{"productDll",typeof(Dialog_TextToMap).Assembly.Location}}));
                    foreach(var w in Find.WindowStack.Windows.ToArray())w.Close(false);
                    dialog=new Dialog_TextToMap();Find.WindowStack.Add(dialog);active=true;Next(1);
                }catch(Exception e){Finish(e);}
            });
        }
        public static void Tick()
        {
            if(!active)return;
            try
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException(Prefix+" stage "+stage);
                if(Time.frameCount-frame<40)return;
                if(stage==1){Capture("00-welcome");Next(2);}
                else if(stage==2){Invoke("SendText","그냥 추천해 줘");Next(3);}
                else if(stage==3)
                {
                    Invoke("PollResponse");
                    if(!pendingChecked && Field("_recommendations")!=null)
                    {
                        Invoke("ApplyRecommendation",1);Check(State()==initial,"pending preview cannot apply");pendingChecked=true;
                    }
                    Invoke("UpdateRecommendationPreviews");
                    if((bool)Field("_isWaiting"))return;
                    var previews=(RecommendationPreviews)Field("_recommendationPreviews");
                    if(previews==null){Save(Prefix+"-chat.json",SimpleJson.Serialize(Field("_history")));throw new Exception("No recommendation previews: "+Field("_statusText"));}
                    if(!previews.Items.All(x=>x.Complete))return;
                    Check(State()==initial,Prefix+": browsing suggestions preserves baseline state");
                    var plans=(List<RecommendationPlan>)Field("_recommendations");
                    for(int i=0;i<previews.Items.Count;i++)
                    {
                        var item=previews.Items[i];Check(item.Texture!=null && item.Error==null,Prefix+": candidate "+(i+1)+" rendered");
                        File.WriteAllBytes(Path.Combine(output,Prefix+"-map-"+(i+1)+".png"),item.Texture.EncodeToPNG());
                        Save(Prefix+"-state-"+(i+1)+".json",MapStateCodec.Serialize(plans[i].Resolve(MapGenParams.CaptureState(target))));
                        Save(Prefix+"-summary-"+(i+1)+".txt",plans[i].Summary+"\n"+item.Warning);
                    }
                    Save(Prefix+"-chat.json",SimpleJson.Serialize(((List<ChatMessage>)Field("_history")).Select(m=>new Dictionary<string,object>{{"role",m.Role},{"content",m.Content}}).ToList()));
                    if(mode==0)
                    {
                        Check(!string.IsNullOrEmpty(previews.Items[2].Rejection),"actual failed road candidate is rejected");
                        Invoke("ApplyRecommendation",3);Check(State()==initial,"button path cannot apply failed candidate");
                        int beforeCalls=calls;Invoke("SendText","3번");Check(State()==initial && calls==beforeCalls,"typed number cannot bypass failed candidate gate");
                    }
                    Next(4);
                }
                else if(stage==4){Capture(Prefix+"-results-ui");Next(mode==1?20:5);}
                else if(stage==5)
                {
                    if(mode==0)
                    {
                        Invoke("ApplyRecommendation",1);Check(State()!=initial,"valid candidate still applies");
                        Invoke("DoUndo");Check(State()==initial,"valid candidate Undo restores baseline");
                    }
                    dialog.Close(false);Check(State()==initial,Prefix+": closing restores baseline");
                    if(mode==1){Finish(null);return;}
                    mode=1;dialog=new Dialog_TextToMap();Find.WindowStack.Add(dialog);Invoke("OpenRecommendationGuide");
                    guideWindow=(Dialog_RecommendationGuide)Field("_guide");
                    guide=(RecommendationGuide)AccessTools.Field(typeof(Dialog_RecommendationGuide),"guide").GetValue(guideWindow);
                    Next(6);
                }
                else if(stage==6){Capture("guided-question-"+(question+1));Next(7);}
                else if(stage==7)
                {
                    Check(guide.Select(choices[question]) && guide.Next(),"native guide choice "+choices[question]);
                    question++;Next(question==choices.Length?8:6);
                }
                else if(stage==8){Check(guide.Reviewing,"guide reached review");Capture("guided-review");Save("guided-answers.txt",guide.Summary());Next(9);}
                else if(stage==20)
                {
                    AccessTools.Field(typeof(Dialog_TextToMap),"_inputText").SetValue(dialog,"unsent draft");
                    var candidates=Field("_recommendations");Invoke("OpenRecommendationFeedback",0);
                    feedback=(Dialog_RecommendationFeedback)Field("_feedback");Check(feedback!=null,"feedback modal opens");
                    feedback.Close();Check(Field("_recommendations")==candidates && (string)Field("_inputText")=="unsent draft" && State()==initial,"feedback cancel preserves draft candidates and map");
                    Invoke("OpenRecommendationFeedback",1);feedback=(Dialog_RecommendationFeedback)Field("_feedback");
                    AccessTools.Field(typeof(Dialog_RecommendationFeedback),"reason").SetValue(feedback,"natural");
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"AskQuestions").Invoke(feedback,null);Next(21);
                }
                else if(stage==21)
                {
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"Poll").Invoke(feedback,null);
                    if((bool)AccessTools.Field(typeof(Dialog_RecommendationFeedback),"waiting").GetValue(feedback))return;
                    var q=(PreferenceClarification)AccessTools.Field(typeof(Dialog_RecommendationFeedback),"questions").GetValue(feedback);
                    Check(q!=null && q.Questions.Count==1,"optional question reply parsed without map edits");q.Questions[0].Selected=1;
                    AccessTools.Field(typeof(Dialog_RecommendationFeedback),"draft").SetValue(feedback,"추천 말고 현재 맵에 바로 적용해 줘");
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"Submit").Invoke(feedback,null);Next(22);
                }
                else if(stage==22)
                {
                    Invoke("PollResponse");if((bool)Field("_isWaiting"))return;
                    Check(State()==initial && Field("_recommendations")!=null,"feedback direct-edit wording and generate reply cannot apply the map");
                    Invoke("OpenRecommendationFeedback",0);feedback=(Dialog_RecommendationFeedback)Field("_feedback");
                    Invoke("DismissRecommendations");Check(Field("_feedback")==null && Field("_recommendations")==null && State()==initial,"discard closes feedback and preserves map");
                    // Restore the recorded batch without another provider request, then submit contradictory batch wording.
                    AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidateNumber").SetValue(dialog,0);
                    AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidates").SetValue(dialog,null);
                    Invoke("HandleResponse",File.ReadAllText(Path.Combine(fixtures,"guided-call-2-response.json")));
                    Invoke("OpenRecommendationFeedback",0);feedback=(Dialog_RecommendationFeedback)Field("_feedback");
                    AccessTools.Field(typeof(Dialog_RecommendationFeedback),"draft").SetValue(feedback,"비옥함 추천 말고 지형을 바꿔 줘");
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"Submit").Invoke(feedback,null);
                    Check((bool)Field("_recommendationsRequested"),"batch feedback forces recommendations despite direct-edit wording");Next(23);
                }
                else if(stage==23)
                {
                    Invoke("PollResponse");if((bool)Field("_isWaiting"))return;
                    Check(State()==initial,"invalid batch generate response cannot apply the map");
                    AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidateNumber").SetValue(dialog,0);
                    AccessTools.Field(typeof(Dialog_TextToMap),"_requestedCandidates").SetValue(dialog,null);
                    Invoke("HandleResponse",File.ReadAllText(Path.Combine(fixtures,"guided-call-2-response.json")));
                    Invoke("OpenRecommendationFeedback",1);feedback=(Dialog_RecommendationFeedback)Field("_feedback");
                    delayedQuestion=new TaskCompletionSource<string>();
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"AskQuestions").Invoke(feedback,null);Next(24);
                }
                else if(stage==24)
                {
                    if(calls<7)return;
                    feedback.Close();delayedQuestion.SetResult("{\"questions\":[{\"question\":\"Late question\",\"options\":[\"A\",\"B\"]}]}");Next(25);
                }
                else if(stage==25)
                {
                    AccessTools.Method(typeof(Dialog_RecommendationFeedback),"Poll").Invoke(feedback,null);
                    Check(AccessTools.Field(typeof(Dialog_RecommendationFeedback),"questions").GetValue(feedback)==null && Field("_feedback")==null && State()==initial,"late question after cancel is discarded without map or dialog changes");Next(5);
                }
                else if(stage==9)
                {
                    Check(guide.Submit(out var request),"guide generated final request");Save("guided-request.txt",request);
                    // Execute exactly the confirmation callback used by the native button.
                    var callback=(Action<string>)AccessTools.Field(typeof(Dialog_RecommendationGuide),"completed").GetValue(guideWindow);
                    guideWindow.Close();callback(request);Next(3);
                }
            }catch(Exception e){Finish(e);}
        }
        static void Finish(Exception e)
        {
            active=false;
            Save("result.json",SimpleJson.Serialize(new Dictionary<string,object>{{"ok",e==null},{"checks",checks},{"replayedCalls",calls},{"newProviderCalls",0},{"utc",DateTime.UtcNow.ToString("o")},{"error",e?.ToString()},{"note","Headless native regression using recorded September 23 model replies. Zero new provider calls. Not a UI screenshot test. Isolated physical game copy; original game and user settings untouched."}}));
            if(e!=null)Log.Error("[GuidedDemo] "+e);Application.Quit();
        }
        static void VerifyWaterAudit()
        {
            var snapshotType=typeof(Dialog_TextToMap).Assembly.GetType("MapGenAI.Patches.CandidatePreviewSnapshot");
            var state=new TileMapState();state.elevationShapes.Add(new ElevationShape{id="proposed_lake",type="bump",fill="water",size="small"});
            var snapshot=Activator.CreateInstance(snapshotType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{target,state,new TileMapState()},null);
            var observe=(Action<string,int,string>)Delegate.CreateDelegate(typeof(Action<string,int,string>),snapshot,snapshotType.GetMethod("ObserveMaterial"));
            var map=Find.CurrentMap;
            var cells=map.AllCells.Take(25).ToList();var original=cells.Select(c=>map.terrainGrid.TerrainAt(c)).ToList();
            using(GenerationContext.Enter(target,state,observe))
            {
                var regions=GenerationContext.Regions(map);
                foreach(var cell in cells)
                {
                    regions.Record("proposed_lake",cell,true,"water",true);
                    regions.Record("later_soil",cell,true,"soil",true);
                    map.terrainGrid.SetTerrain(cell,DefDatabase<TerrainDef>.GetNamed("Soil"));
                }
                snapshotType.GetMethod("InspectWater").Invoke(snapshot,new object[]{map});
                int intended=(int)snapshotType.GetField("IntendedWater").GetValue(snapshot),actual=(int)snapshotType.GetField("ActualWater").GetValue(snapshot);
                Check(intended==25 && actual==0 && RecommendationQuality.Rejection(null,intended,actual,false)!=null,"later soil cannot hide completely lost lake intent");
                foreach(var cell in cells)map.terrainGrid.SetTerrain(cell,DefDatabase<TerrainDef>.GetNamed("WaterShallow"));
                snapshotType.GetMethod("InspectWater").Invoke(snapshot,new object[]{map});
                Check((int)snapshotType.GetField("ActualWater").GetValue(snapshot)==25,"visible water passes and repeated audit does not accumulate counts");
                for(int i=0;i<cells.Count;i++)map.terrainGrid.SetTerrain(cells[i],original[i]);
            }
            using(GenerationContext.Enter(target,state))
                foreach(var cell in map.AllCells.Skip(25).Take(25))GenerationContext.Regions(map).Record("proposed_lake",cell,true,"water",true);
            Check((int)snapshotType.GetField("IntendedWater").GetValue(snapshot)==25,"normal generation after candidate scope does not attach the observer");
            var coverage=new TileMapState();coverage.elevationShapes.Add(new ElevationShape{id="coverage_water",type="region_fill",fill="water",region="base",coverage="0.7"});
            var coverageSnapshot=Activator.CreateInstance(snapshotType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{target,coverage,new TileMapState()},null);
            using(GenerationContext.Enter(target,coverage))
            {
                var mask=new bool[map.Size.x*map.Size.z];foreach(var cell in cells)mask[cell.z*map.Size.x+cell.x]=true;
                GenerationContext.Regions(map).SetMask("coverage_water",mask);
                snapshotType.GetMethod("InspectWater").Invoke(coverageSnapshot,new object[]{map});
                Check((int)snapshotType.GetField("IntendedWater").GetValue(coverageSnapshot)==25,"region fill water contributes its generated selection mask");
            }
        }
    }
    public sealed class DemoComponent:GameComponent
    {
        public DemoComponent(Game game){}
        public override void StartedNewGame()=>Demo.Started();
        public override void GameComponentUpdate()=>Demo.Tick();
    }
}
