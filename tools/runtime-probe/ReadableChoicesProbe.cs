using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class ReadableChoicesProbe
    {
        static string folder;static int target,frame;static bool active;static Dialog_TextToMap dialog;
        static readonly List<string> checks=new List<string>();static readonly List<string> errors=new List<string>();
        static object Invoke(string method,params object[] args)=>typeof(Dialog_TextToMap).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,args);
        static object Field(string name)=>typeof(Dialog_TextToMap).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
        static void Set(string name,object value)=>typeof(Dialog_TextToMap).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,value);
        static void Check(bool pass,string name){checks.Add((pass?"PASS: ":"FAIL: ")+name);if(!pass)throw new InvalidOperationException(name);}
        static void OnLog(string message,string stack,LogType type){if(type==LogType.Exception || message.Contains("GUIClip"))errors.Add(message);}
        static void Finish(Exception error=null){active=false;Application.logMessageReceived-=OnLog;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null && errors.Count==0},{"checks",checks},{"guiErrors",errors},{"error",error?.ToString()}}));Application.Quit();}
        public static void Run(string output,string replies)
        {
            folder=output;
            try
            {
                Application.runInBackground=true;var background=typeof(Prefs).GetProperty("RunInBackground",BindingFlags.Public|BindingFlags.Static);if(background?.CanWrite==true)background.SetValue(null,true,null);
                Application.logMessageReceived+=OnLog;
                var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="AridShrubland" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));target=tile.tile;
                Find.WorldSelector.SelectedTile=target;
                string response=File.ReadAllText(Path.Combine(replies,"recommend-plain-response.json"));var command=ProviderResponse.Command(response);var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(replies,"recommend-plain-before.json")));
                MapGenParams.RestoreSnapshot(before,target);dialog=new Dialog_TextToMap();string initial=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                Invoke("HandleResponse",response);var plans=(List<RecommendationPlan>)Field("_recommendations");Check(plans.Count==3,"recorded real three-option recommendation validates with new presentation");
                string text=((List<ChatMessage>)Field("_history")).Last().Content;
                foreach(string word in new[]{"온천","온수 샘","폐허","고대 정착지","비옥도","분지","광물 풍부","산맥","지하 동굴"})Check(text.Contains(word),"localized choice explains "+word);
                foreach(string code in new[]{"HotSprings","AncientRuins","MineralRich","UndergroundCave","radial","split","terrain_1","fertilityOffset","elevationShapes"})Check(!text.Contains(code),"choice does not expose internal code "+code);
                File.WriteAllText(Path.Combine(folder,"choices.txt"),text);
                for(int i=0;i<plans.Count;i++)
                {
                    Invoke("HandleResponse",response);var expected=MapStateEditor.Merge(before,MapParameterParser.Parse(RecommendationPlan.Options(command)[i].GetObject("params")));
                    Invoke("ApplyRecommendation",i+1);Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(expected),"option "+(i+1)+" applies unchanged command");
                    string applied=((List<ChatMessage>)Field("_history")).Last().Content;
                    Check(!new[]{"HotSprings","AncientRuins","MineralRich","UndergroundCave","radial","split","terrain_1","fertilityOffset","Tile features"}.Any(applied.Contains),"option "+(i+1)+" applied message also hides internal codes");
                    File.WriteAllText(Path.Combine(folder,"applied-"+(i+1)+".txt"),applied);Invoke("DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,"option "+(i+1)+" Undo preserves state");
                }
                // Actual loaded TerrainDef labels in an old oasis reply, without regeneration or API use.
                Invoke("HandleResponse",File.ReadAllText(Path.Combine(replies,"oasis-plain-response.json")));
                string oasis=((List<ChatMessage>)Field("_history")).Last().Content;
                Check(oasis.Contains(DefDatabase<TerrainDef>.GetNamed("SoilRich").label) && oasis.Contains(DefDatabase<TerrainDef>.GetNamed("WaterShallow").label),"oasis material names use current game translations");
                Check(!new[]{"SoilRich","WaterShallow","composite","oasis_soil","oasis_water"}.Any(oasis.Contains),"oasis description hides IDs and primitive codes");File.WriteAllText(Path.Combine(folder,"oasis-applied.txt"),oasis);Invoke("DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,"oasis presentation preserves Undo");
                dialog.PostClose();dialog=new Dialog_TextToMap();Invoke("HandleResponse",response);Find.WindowStack.Add(dialog);frame=Time.frameCount;active=true;
            }
            catch(Exception error){Finish(error);}
        }
        public static void Tick()
        {
            if(!active)return;
            try
            {
                int elapsed=Time.frameCount-frame;
                if(elapsed==15)Set("_scrollPos",Vector2.zero);
                if(elapsed==30)ScreenCapture.CaptureScreenshot(Path.Combine(folder,"choices-top.png"));
                if(elapsed==45)Set("_scrollPos",new Vector2(0,10000));
                if(elapsed==60)ScreenCapture.CaptureScreenshot(Path.Combine(folder,"choices-bottom.png"));
                if(elapsed<85)return;
                Check(errors.Count==0,"actual choice window renders without GUI errors");dialog.Close(false);Finish();
            }
            catch(Exception error){Finish(error);}
        }
    }
}
