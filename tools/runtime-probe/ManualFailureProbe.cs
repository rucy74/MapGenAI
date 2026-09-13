using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // Replay actual user responses. Expected geometry/material checks use map cells, not the response text.
    static class ManualFailureProbe
    {
        static string folder;static readonly List<string> checks=new List<string>();
        static readonly Queue<KeyValuePair<string,TileMapState>> previews=new Queue<KeyValuePair<string,TileMapState>>();
        static bool pending;static DateTime deadline;static int previewTile;static string previewId;
        static Dictionary<string,object> previewObservation;
        static void Check(bool pass,string message){checks.Add((pass?"PASS: ":"FAIL: ")+message);if(!pass)throw new InvalidOperationException(message);Log.Message("[ManualFailureProbe] "+message);}
        static void Finish(Exception error=null){pending=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"error",error?.ToString()}}));Application.Quit();}
        public static void Tick(){if(pending && DateTime.UtcNow>deadline)Finish(new TimeoutException("Manual regression preview timeout"));}
        static object Invoke(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        static TileMapState Apply(TileMapState state,string response)=>MapStateEditor.Merge(state,MapParameterParser.Parse(ProviderResponse.Command(response).GetObject("params")));
        static string Read(string evidence,int line)=>File.ReadAllText(Path.Combine(evidence,"line-"+line.ToString("0000")+"-response.json"));
        public static void Run(string output,string evidence,string responses)
        {
            folder=output;
            var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(24).ToList();
            Check(tiles.Count==24,"isolated flat inland fixture tiles available");
            var d=new TileMapState();foreach(int line in new[]{216,220,253})d=Apply(d,Read(evidence,line));
            var f=Apply(new TileMapState(),Read(evidence,481));
            var originalF=Apply(Apply(new TileMapState(),Read(evidence,351)),Read(evidence,420));
            var baseline=new Dictionary<string,TileMapState>{{"d04",d},{"f03",f},{"f03-original",originalF}};
            int target=tiles[0].tile;Find.WorldSelector.SelectedTile=target;
            foreach(var entry in baseline)
            {
                MapGenParams.RestoreSnapshot(entry.Value,target);
                string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{target});
                File.WriteAllText(Path.Combine(output,entry.Key+"-prompt.txt"),prompt);
                File.WriteAllText(Path.Combine(output,entry.Key+"-before.json"),MapStateCodec.Serialize(MapGenParams.CaptureState(target)));
            }
            if(string.IsNullOrEmpty(responses)){Finish();return;}
            var cases=new Dictionary<string,TileMapState>();
            cases["d03-baseline"]=d;
            cases["d04-recorded-water"]=Apply(d,Read(evidence,257));
            cases["f03-recorded-lava"]=Apply(f,Read(evidence,576));
            foreach(var path in Directory.GetFiles(responses,"*-response.json").OrderBy(p=>p))
            {
                string id=Path.GetFileName(path).Replace("-response.json","");
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-before.json")));
                var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-after.json")));
                MapGenParams.RestoreSnapshot(before,target);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var dialog=new Dialog_TextToMap();
                Invoke(dialog,"HandleResponse",File.ReadAllText(path));
                var undo=(ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                var actual=MapGenParams.CaptureState(target);
                Check(undo.Count==1 && MapStateCodec.Serialize(actual)==MapStateCodec.Serialize(expected),id+": actual dialog applies the first response");
                Invoke(dialog,"DoUndo");Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),id+": actual Undo restores all prior terrain and structures");
                dialog.PostClose();cases[id]=actual;
            }
            int index=0;
            foreach(var entry in cases)
            {
                var tile=tiles[index++];target=tile.tile;MapGenParams.RestoreSnapshot(entry.Value,target);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="ManualFailureCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                var observation=Observe(map,entry.Key);Validate(entry.Key,observation);
                var result=AuthoringGeneration.Latest(target,entry.Value);
                Check(result!=null && result.issues.Count==0 && result.placements.Count==entry.Value.structures.Sum(p=>p.count),entry.Key+": all requested ruins generated without failure");
                Check(result.placements.All(p=>p.spawnedWalls==p.walls && p.walls>0 && p.wallCells.All(c=>new IntVec3(c[0],0,c[1]).GetEdifice(map)?.def==ThingDefOf.Wall)),entry.Key+": recorded ruin walls exist on the map");
                observation["result"]=result;File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(observation));
                Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && !GenerationContext.Active,entry.Key+": generation preserves saved plan and disposes scope");
            }
            Check(!MapGenAI.ImageInput.ImageFeatureGate.Enabled,"image generation remains paused");
            foreach(string id in new[]{"d04-1","f03-1"})
            {
                var fixture=new ProbeEnvelope{state=cases[id]};string saved=MapStateCodec.Serialize(fixture.state),path=Path.Combine(output,id+"-scribe.xml");
                Scribe.saver.InitSaving(path,"ManualFailure");Scribe_Deep.Look(ref fixture,"fixture");Scribe.saver.FinalizeSaving();fixture=null;
                Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref fixture,"fixture");Scribe.loader.FinalizeLoading();Check(saved==MapStateCodec.Serialize(fixture.state),id+": real Scribe preserves corrected state");
                previews.Enqueue(new KeyValuePair<string,TileMapState>(id,cases[id]));
            }
            previewTile=tiles[23].tile;
            new Harmony("choco.mapgenai.probe.manual-observer").Patch(AccessTools.Method(AccessTools.TypeByName("MapPreview.MapPreviewGenerator+PreviewTextureGenStep"),"Generate"),postfix:new HarmonyMethod(typeof(ManualFailureProbe),nameof(ObservePreview)));
            NextPreview();
        }
        static Dictionary<string,object> Observe(Map map,string id,bool preview=false)
        {
            if(id.StartsWith("d",StringComparison.Ordinal))
            {
                var strip=new CellRect(1,117,100,16).Cells.ToList();
                return new Dictionary<string,object>{{"waterInPass",strip.Count(c=>map.terrainGrid.TerrainAt(c).IsWater)},{"rocksInPass",strip.Count(c=>SolidRock(map,c,preview))},
                    {"dryConnected",Connected(map,preview)},{"lakeStillWater",map.terrainGrid.TerrainAt(new IntVec3(187,0,62)).IsWater},{"northeastStillMountain",SolidRock(map,new IntVec3(187,0,187),preview)},
                    {"rockObservation",preview?"elevation>=0.7 and caves<=0; preview suppresses spawned rock Things":"actual natural rock edifices"}};
            }
            var annulus=map.AllCells.Where(c=>{double r=Math.Sqrt(Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2));return r>=.20 && r<=.30;}).ToList();
            var island=map.AllCells.Where(c=>Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2)<.1*.1).ToList();
            return new Dictionary<string,object>{{"annulusCells",annulus.Count},{"lavaInAnnulus",annulus.Count(c=>map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)).defName=="LavaDeep")},
                {"waterInAnnulus",annulus.Count(c=>map.terrainGrid.TerrainAt(c).IsWater)},{"islandDry",island.All(c=>!map.terrainGrid.TerrainAt(c).IsWater && map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)).defName!="LavaDeep")}};
        }
        static bool SolidRock(Map map,IntVec3 c,bool preview)=>preview?MapGenerator.Elevation[c]>=.7f && MapGenerator.Caves[c]<=0:c.GetEdifice(map)?.def.building?.isNaturalRock==true;
        static bool Connected(Map map,bool preview)
        {
            var seen=new HashSet<IntVec3>();var queue=new Queue<IntVec3>();
            for(int z=117;z<133;z++){var c=new IntVec3(1,0,z);if(c.Standable(map)&&!SolidRock(map,c,preview)&&!map.terrainGrid.TerrainAt(c).IsWater){queue.Enqueue(c);seen.Add(c);}}
            while(queue.Count>0){var c=queue.Dequeue();if(c.x==100)return true;foreach(var delta in GenAdj.CardinalDirections){var n=c+delta;if(n.x<1||n.x>100||n.z<117||n.z>=133||seen.Contains(n)||!n.Standable(map)||SolidRock(map,n,preview)||map.terrainGrid.TerrainAt(n).IsWater)continue;seen.Add(n);queue.Enqueue(n);}}
            return false;
        }
        static void Validate(string id,Dictionary<string,object> o)
        {
            if(id.StartsWith("d",StringComparison.Ordinal))
            {
                if(id=="d03-baseline")Check((int)o["rocksInPass"]>0 && !(bool)o["dryConnected"],id+": original mountain blocks east-west crossing");
                else if(id=="d04-recorded-water")Check((int)o["waterInPass"]>1000 && !(bool)o["dryConnected"],id+": captured negative/no-fill response reproduces the lake bug");
                else Check((int)o["waterInPass"]==0 && (int)o["rocksInPass"]==0 && (bool)o["dryConnected"],id+": dry walkable east-west crossing without water or mountain rocks");
                Check((bool)o["lakeStillWater"] && (bool)o["northeastStillMountain"],id+": southeast lake and northeast mountain survive");
            }
            else Check((int)o["annulusCells"]>9000 && (int)o["annulusCells"]==(int)o["lavaInAnnulus"] && (int)o["waterInAnnulus"]==0 && (bool)o["islandDry"],id+": actual LavaDeep throughout moat interior, zero water, dry central island");
        }
        static void ObservePreview(Map map){if(pending)previewObservation=Observe(map,previewId,true);}
        static void NextPreview()
        {
            if(previews.Count==0){Finish();return;}
            var entry=previews.Dequeue();previewId=entry.Key;previewObservation=null;MapGenParams.RestoreSnapshot(entry.Value,previewTile);pending=true;deadline=DateTime.UtcNow.AddSeconds(60);
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,previewTile,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try
                {
                    File.WriteAllText(Path.Combine(folder,previewId+"-background-observation.json"),SimpleJson.Serialize(previewObservation));
                    Check(result.InvalidCells==0 && previewObservation!=null,previewId+": real background preview produced observable map cells");Validate(previewId,previewObservation);
                    var report=AuthoringGeneration.Latest(previewTile,entry.Value);Check(report.preview && report.issues.Count==0 && report.placements.Count==entry.Value.structures.Sum(s=>s.count),previewId+": background preview preserves ruin plan");
                    var texture=new Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(folder,previewId+"-background-preview.png"),ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);
                    File.WriteAllText(Path.Combine(folder,previewId+"-background-observation.json"),SimpleJson.Serialize(previewObservation));pending=false;NextPreview();
                }
                catch(Exception error){Finish(error);}
            },error=>Finish(error));
        }
    }
}
