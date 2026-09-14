using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class RecommendationProbe
    {
        static string folder,initial,bad;static int target,stage,frame;static bool active;static DateTime deadline;
        static Dialog_TextToMap dialog;static FakeClient client;static readonly List<string> checks=new List<string>();
        const string Good="{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.2}},{\"params\":{\"vegetation_density\":1.3}}]}";
        static object Invoke(string name,params object[] args)=>typeof(Dialog_TextToMap).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,args);
        static object Field(string name)=>typeof(Dialog_TextToMap).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
        static void Set(string name,object value)=>typeof(Dialog_TextToMap).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,value);
        static string State()=>MapStateCodec.Serialize(MapGenParams.CaptureState(target));
        static void Check(bool pass,string name){checks.Add((pass?"PASS: ":"FAIL: ")+name);if(!pass)throw new InvalidOperationException(name);}
        static void New(){MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(initial),target);dialog=new Dialog_TextToMap();}
        static string Prompt()=>(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{target});
        static void SavePrompt(string id){File.WriteAllText(Path.Combine(folder,id+"-prompt.txt"),Prompt());File.WriteAllText(Path.Combine(folder,id+"-before.json"),State());}
        static void Finish(Exception error=null){active=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"error",error?.ToString()}}));Application.Quit();}
        public static void Run(string output,string evidence,string replies)
        {
            folder=output;
            try
            {
                // Quick-test startup can reapply Prefs after RuntimeProbe.Start set this.
                Application.runInBackground=true;
                var background=typeof(Prefs).GetProperty("RunInBackground",BindingFlags.Public|BindingFlags.Static);
                if(background?.CanWrite==true)background.SetValue(null,true,null);
                var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="AridShrubland" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                target=tile.tile;Find.WorldSelector.SelectedTile=target;MapGenParams.LoadFromTile(target);SavePrompt("plain");
                MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse("{\"mutators\":[\"HotSprings\"]}")),target);initial=State();SavePrompt("hot");
                if(GenCommandLine.TryGetCommandLineArg("mapgenAIRecommendationScreen",out _)){stage=4;Next();return;}
                var rejected=ProviderResponse.Command(File.ReadAllText(Path.Combine(evidence,"rejected-selection.json")));
                var opt=new SimpleJsonObject();opt.SetObject("params",rejected.GetObject("params"));var rec=new SimpleJsonObject();rec.SetString("action","recommend");rec.SetObjectArray("options",new List<SimpleJsonObject>{opt});bad=SimpleJson.Serialize(rec);
                New();Invoke("HandleResponse",bad);Check(State()==initial && Field("_recommendations")==null && ((ICollection)Field("_paramStack")).Count==0,"recorded incompatible HotSprings+Pond plan is never displayed or applied");dialog.PostClose();
                New();Invoke("HandleResponse",File.ReadAllText(Path.Combine(evidence,"unchecked-recommendations.json")));Check(Field("_recommendations")==null && !((List<ChatMessage>)Field("_history")).Last().Content.Contains("오아시스 온천 정착지"),"recorded prose recommendation is not shown as an executable offer");dialog.PostClose();
                New();Invoke("HandleResponse",Good);Check(State()==initial && ((ICollection)Field("_recommendations")).Count==2,"validated independent options do not mutate state");
                Set("_inputText","그래");Invoke("SendMessage");Check(State()==initial && ((ICollection)Field("_recommendations")).Count==2,"ambiguous acceptance asks for selection with no API call");
                Set("_inputText","1번");Invoke("SendMessage");Check(MapGenParams.CaptureState(target).fertilityOffset==.2f && ((ICollection)Field("_paramStack")).Count==1,"typed number applies stored first option without API configuration");Invoke("DoUndo");Check(State()==initial && Field("_recommendations")==null,"Undo restores HotSprings and invalidates proposals");
                Invoke("HandleResponse",Good);MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse("{\"fertility_offset\":0.4}")),target);string changed=State();Invoke("ApplyRecommendation",1);Check(State()==changed && Field("_recommendations")==null,"external state change prevents stale option application");dialog.PostClose();
                New();Invoke("HandleResponse",Good);Invoke("DoReset");Check(Field("_recommendations")==null && State()==initial,"reset clears pending selection");dialog.PostClose();
                New();Invoke("HandleResponse","{\"action\":\"recommend\",\"options\":[{\"params\":{\"fertility_offset\":0.2}}]}");Set("_inputText","그래");Invoke("SendMessage");Check(MapGenParams.CaptureState(target).fertilityOffset==.2f && Field("_recommendations")==null,"yes accepts the sole validated option without an unnecessary extra question");Invoke("DoUndo");dialog.PostClose();
                if(!string.IsNullOrEmpty(replies))Replay(replies);
                stage=0;Next();
            }
            catch(Exception error){Finish(error);}
        }
        static void Replay(string replies)
        {
            foreach(string path in Directory.GetFiles(replies,"*-response.json").OrderBy(p=>p))
            {
                string id=Path.GetFileName(path).Replace("-response.json","");
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(replies,id+"-before.json")));
                MapGenParams.RestoreSnapshot(before,target);dialog=new Dialog_TextToMap();string saved=State(),reply=File.ReadAllText(path);var cmd=ProviderResponse.Command(reply);
                if(cmd.GetString("action")=="recommend")
                {
                    int count=RecommendationPlan.Options(cmd).Count;
                    for(int i=1;i<=count;i++)
                    {
                        Invoke("HandleResponse",reply);Check(Field("_recommendations")!=null && State()==saved,id+": all model options pass native dry-run before display");
                        var expected=MapStateEditor.Merge(before,MapParameterParser.Parse(RecommendationPlan.Options(cmd)[i-1].GetObject("params")));
                        Invoke("ApplyRecommendation",i);Check(State()==MapStateCodec.Serialize(expected),id+": selected option "+i+" applies exactly its stored patch");Invoke("DoUndo");Check(State()==saved,id+": option "+i+" Undo preserves existing features");
                    }
                }
                else if(cmd.GetString("action")=="generate")
                {
                    var expected=MapStateEditor.Merge(before,MapParameterParser.Parse(cmd.GetObject("params")));Invoke("HandleResponse",reply);Check(State()==MapStateCodec.Serialize(expected),id+": model terrain response applies through actual dialog");
                    if(id.StartsWith("oasis"))GenerateOasis(id,expected);
                    Invoke("DoUndo");Check(State()==saved,id+": terrain Undo restores original features");
                }
                else throw new InvalidOperationException("Expected executable options or accepted oasis-like edit: "+id);
                dialog.PostClose();
            }
        }
        static void GenerateOasis(string id,TileMapState state)
        {
            // Use a fresh tile for each full map, keeping the dialog target free for subsequent cases.
            var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="AridShrubland" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile) && t.tile!=target);
            MapGenParams.RestoreSnapshot(state,tile.tile);var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=tile.tile;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
            var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
            generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="RecommendationCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=folder,id=id}});
            var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
            var counts=map.AllCells.GroupBy(c=>map.terrainGrid.TerrainAt(c).defName).ToDictionary(g=>g.Key,g=>g.Count());
            Check(counts.TryGetValue("WaterShallow",out int water) && water>100 && water<6250,id+": full map has a modest pool under 10 percent of map area");
            var center=map.AllCells.Where(c=>Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2)<.2*.2).ToList();
            int soil=center.Count(c=>map.terrainGrid.TerrainAt(c).defName=="SoilRich");
            Check(soil>200,id+": localized rich soil exists around the central oasis");
            Check(tile.PrimaryBiome.defName=="AridShrubland" && !tile.Mutators.Any(d=>d.defName=="Pond" || d.defName=="Oasis"),id+": biome and native-feature policy preserved without duplicate Pond");
            File.WriteAllText(Path.Combine(folder,id+"-observation.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"terrain",counts},{"centralRichSoil",soil},{"features",tile.Mutators.Select(d=>d.defName).ToList()}}));
        }
        static void Next()
        {
            New();
            if(stage==4){Invoke("HandleResponse",Good);Find.WindowStack.Add(dialog);frame=Time.frameCount;active=true;return;}
            client=new FakeClient{first=stage==2?"{\"action\":\"ask\",\"message\":\"추천합니다.\\n1. 온천과 연못\\n2. 협곡\"}":bad,second=stage==1?bad:Good,hold=stage==3};
            Set("_recommendationsRequested",true);active=true;deadline=DateTime.UtcNow.AddSeconds(35);
            Invoke("StartChat",new List<ILLMClient>{client},new List<ChatMessage>{new ChatMessage("user","그냥 추천해 봐")},Prompt(),false);
        }
        public static void Tick()
        {
            if(!active)return;
            try
            {
                if(stage==4)
                {
                    if(Time.frameCount-frame==30)ScreenCapture.CaptureScreenshot(Path.Combine(folder,"recommendations-ui.png"));
                    if(Time.frameCount-frame<65)return;
                    Check(!MapGenAI.ImageInput.ImageFeatureGate.Enabled,"image feature remains disabled; visible title inspected in screenshot");
                    File.WriteAllText(Path.Combine(folder,"displayed-options.txt"),((List<ChatMessage>)Field("_history")).Last().Content);dialog.Close(false);Finish();return;
                }
                if(DateTime.UtcNow>deadline)throw new TimeoutException("Recommendation retry timeout");
                Invoke("PollResponse");
                if(stage==3 && client.calls==2){dialog.PostClose();Check(State()==initial,"close cancels recommendation repair without applying");stage++;Next();return;}
                if((bool)Field("_isWaiting"))return;
                Check(client.calls==2 && State()==initial && ((ICollection)Field("_paramStack")).Count==0,"retry case "+stage+": exactly two calls and no settings applied");
                Check((Field("_recommendations")!=null)==(stage!=1),"retry case "+stage+": only a fully validated batch is displayed");
                File.WriteAllText(Path.Combine(folder,"retry-"+stage+".json"),SimpleJson.Serialize(client.requests));dialog.PostClose();stage++;Next();
            }
            catch(Exception error){Finish(error);}
        }
        sealed class FakeClient:ILLMClient
        {
            public int calls;public string first,second;public bool hold;public readonly List<object> requests=new List<object>();
            public async Task<string> SendChatAsync(List<ChatMessage> history,string prompt,CancellationToken token=default)
            {
                int n=Interlocked.Increment(ref calls);requests.Add(history.Select(m=>new Dictionary<string,object>{{"role",m.Role},{"content",m.Content}}).ToList());
                if(n>2)throw new InvalidOperationException("Unbounded retry");if(n==2 && hold)await Task.Delay(Timeout.Infinite,token);return n==1?first:second;
            }
            public Task<string> SendImageAsync(byte[] b,string m,string p,CancellationToken t=default)=>throw new NotSupportedException();
        }
    }
}
