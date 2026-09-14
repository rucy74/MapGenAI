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
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class EditPreflightProbe
    {
        static string folder,invalid,initial;static int target,index;static bool pending;static DateTime deadline;
        static Dialog_TextToMap dialog;static FakeClient client;static readonly List<string> checks=new List<string>();
        static readonly string Ask="{\"action\":\"ask\",\"message\":\"온천과 연못은 함께 추가할 수 없습니다. 온천은 유지하고 물·모래 도형만 추가할까요?\"}";
        static readonly string Replace="{\"action\":\"generate\",\"params\":{\"mutators\":[\"Pond\"],\"remove_mutators\":[\"HotSprings\"]}}";
        static void Check(bool pass,string label){checks.Add((pass?"PASS: ":"FAIL: ")+label);if(!pass)throw new InvalidOperationException(label);}
        static object Invoke(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);
        static object Field(string name)=>typeof(Dialog_TextToMap).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
        static void Finish(Exception error=null){pending=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"error",error?.ToString()}}));UnityEngine.Application.Quit();}
        public static void Run(string output,string evidence,string replies)
        {
            folder=output;
            var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="AridShrubland" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
            target=tile.tile;MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse("{\"mutators\":[\"HotSprings\"]}")),target);Find.WorldSelector.SelectedTile=target;
            initial=MapStateCodec.Serialize(MapGenParams.CaptureState(target));invalid=File.ReadAllText(Path.Combine(evidence,"rejected-response.json"));
            try{MapGenParams.ValidatePatch(MapParameterParser.Parse(ProviderResponse.Command(invalid).GetObject("params")),target);Check(false,"recorded invalid command must reject");}
            catch(FormatException error){File.WriteAllText(Path.Combine(folder,"rejection-reason.txt"),error.Message);}
            string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{target});
            File.WriteAllText(Path.Combine(folder,"current-prompt.txt"),prompt);File.WriteAllText(Path.Combine(folder,"before.json"),initial);
            string additive=prompt.Split(new[]{"Replacement required"},StringSplitOptions.None)[0],replacements=prompt.Split(new[]{"Replacement required"},StringSplitOptions.None)[1].Split(new[]{"Unavailable additions"},StringSplitOptions.None)[0];
            Check(replacements.Contains("Pond (") && !additive.Contains("Pond ("),"Pond is replacement-required with actual current HotSprings");
            int checkedFeatures=0;
            foreach(var def in DefDatabase<TileMutatorDef>.AllDefsListForReading.Where(d=>!string.IsNullOrEmpty(d.label) && d.label!="none" && !new[]{"Mountain","Caves","River","Coast"}.Contains(d.defName)))
            {
                if(FeaturePolicy.UnavailableReason(def,tile)!=null || WorldTileEditor.Conflict(def,DefDatabase<TileMutatorDef>.GetNamed("HotSprings")))continue;
                bool pass=true;try{MapGenParams.ValidatePatch(MapParameterParser.Parse(SimpleJson.Parse(SimpleJson.Serialize(new Dictionary<string,object>{{"mutators",new[]{def.defName}}}))),target);}catch(FormatException){pass=false;}
                Check(additive.Contains(def.defName+" (")==pass,"catalog matches dry-run for "+def.defName);checkedFeatures++;
            }
            Check(checkedFeatures>10 && MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,"catalog exercises loaded definitions without changing current plan");
            var delta=DefDatabase<TileMutatorDef>.GetNamed("RiverDelta");
            var riverTile=Find.WorldGrid.Tiles.First(t=>FeaturePolicy.UnavailableReason(delta,t)==null);
            foreach(var old in riverTile.Mutators.ToList())riverTile.RemoveMutator(old);
            riverTile.AddMutator(DefDatabase<TileMutatorDef>.GetNamed("River"));riverTile.AddMutator(DefDatabase<TileMutatorDef>.GetNamed("Coast"));
            string riverPrompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{(int)riverTile.tile});
            string riverAdditive=riverPrompt.Split(new[]{"Replacement required"},StringSplitOptions.None)[0];
            Check(riverAdditive.Contains("RiverDelta ("),"delta can represent a world river without requesting removal of protected base connections");
            if(!string.IsNullOrEmpty(replies))foreach(var path in Directory.GetFiles(replies,"*-response.json"))
            {
                var before=MapStateCodec.Deserialize(initial);MapGenParams.RestoreSnapshot(before,target);dialog=new Dialog_TextToMap();string id=Path.GetFileName(path);
                Invoke(dialog,"HandleResponse",File.ReadAllText(path));
                var command=ProviderResponse.Command(File.ReadAllText(path));var data=command.GetString("action")=="generate"?MapParameterParser.Parse(command.GetObject("params")):null;
                var expected=data==null?before:MapStateEditor.Merge(before,data);
                Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(expected),id+": actual model response applied or asked as expected");
                if(data!=null)Invoke(dialog,"DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,id+": Undo or ask preserves prior features");dialog.PostClose();
            }
            index=0;Next();
        }
        static void Next()
        {
            if(index==4){Finish();return;}
            MapGenParams.RestoreSnapshot(MapStateCodec.Deserialize(initial),target);dialog=new Dialog_TextToMap();
            client=new FakeClient{first=index==3?Replace:invalid,second=index==1?Replace:Ask,holdSecond=index==2};
            pending=true;deadline=DateTime.UtcNow.AddSeconds(25);
            string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{target});
            Invoke(dialog,"StartChat",new List<ILLMClient>{client},new List<ChatMessage>{new ChatMessage("user",index==3?"온천을 없애고 연못으로 교체해 줘.":"그래")},prompt,false);
        }
        public static void Tick()
        {
            if(!pending)return;
            try
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException("Actual dialog preflight lifecycle timeout");
                Invoke(dialog,"PollResponse");
                if(index==2 && client.calls==2)
                {
                    dialog.PostClose();Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,"close during explanatory retry preserves state");
                    File.WriteAllText(Path.Combine(folder,"cancel-messages.json"),SimpleJson.Serialize(client.requests));pending=false;index++;Next();return;
                }
                if((bool)Field("_isWaiting"))return;
                var undo=(ICollection)Field("_paramStack");string state=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                if(index<3)
                {
                    Check(client.calls==2 && state==initial && undo.Count==0,"invalid edit case "+index+": one explanation, no mutation or Undo entry");
                    var history=(List<ChatMessage>)Field("_history");Check(history.Last().Content.Contains(index==1?"충돌 안내":"온천"),"invalid edit case "+index+": user receives explanation or refusal of a repair edit");
                }
                else
                {
                    Check(client.calls==1 && MapGenParams.CaptureState(target).mutators.SequenceEqual(new[]{"Pond"}) && undo.Count==1,"explicit replacement proceeds in one request");
                    Invoke(dialog,"DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==initial,"explicit replacement Undo restores HotSprings");
                }
                File.WriteAllText(Path.Combine(folder,"dialog-case-"+index+".json"),SimpleJson.Serialize(client.requests));dialog.PostClose();pending=false;index++;Next();
            }
            catch(Exception error){Finish(error);}
        }
        sealed class FakeClient:ILLMClient
        {
            public int calls;public string first,second;public bool holdSecond;public readonly List<object> requests=new List<object>();
            public async Task<string> SendChatAsync(List<ChatMessage> history,string prompt,CancellationToken token=default)
            {
                int n=Interlocked.Increment(ref calls);requests.Add(new Dictionary<string,object>{{"call",n},{"messages",history.Select(m=>new Dictionary<string,object>{{"role",m.Role},{"content",m.Content}}).ToList()}});
                if(n>2)throw new InvalidOperationException("Unbounded retry");if(n==2 && holdSecond)await Task.Delay(Timeout.Infinite,token);token.ThrowIfCancellationRequested();return n==1?first:second;
            }
            public Task<string> SendImageAsync(byte[] bytes,string mime,string prompt,CancellationToken token=default)=>throw new NotSupportedException();
        }
    }
}
