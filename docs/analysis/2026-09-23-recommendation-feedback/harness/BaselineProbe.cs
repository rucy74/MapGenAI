using System;
using System.Collections.Generic;
using System.IO;
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
        static void Capture(string name)=>Save(name+"-headless.txt","Headless baseline; not a UI screenshot.");

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
            public Task<ContextBudget> GetContextBudgetAsync(CancellationToken token)=>Task.FromResult(new ContextBudget{InputTokens=1000000,Known=true,Source="baseline headless replay"});
            public Task<string> SendChatAsync(List<ChatMessage> history,string prompt,CancellationToken cancellationToken=default)
            {
                if(Interlocked.Increment(ref calls)>2)throw new Exception("Unexpected replay request");
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
                Save("model.txt","Baseline DLL; zero new provider calls.");
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
                    Invoke("PollResponse");Invoke("UpdateRecommendationPreviews");
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
                    Save(Prefix+"-chat.json",SimpleJson.Serialize(Field("_history")));Next(4);
                }
                else if(stage==4){Capture(Prefix+"-results-ui");Next(5);}
                else if(stage==5)
                {
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
            Save("result.json",SimpleJson.Serialize(new Dictionary<string,object>{{"ok",e==null},{"checks",checks},{"replayedCalls",calls},{"newProviderCalls",0},{"utc",DateTime.UtcNow.ToString("o")},{"error",e?.ToString()},{"note","Baseline headless replay. Two recorded responses; zero new provider calls. No GUI assertions."}}));
            if(e!=null)Log.Error("[GuidedDemo] "+e);Application.Quit();
        }
    }
    public sealed class DemoComponent:GameComponent
    {
        public DemoComponent(Game game){}
        public override void StartedNewGame()=>Demo.Started();
        public override void GameComponentUpdate()=>Demo.Tick();
    }
}
