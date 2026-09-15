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
    static class CoverageProbe
    {
        static string folder,caseId;static bool measuring,preview;static int target;
        static TileMapState state;static string[] terrainBefore;static float[] elevationBefore;static bool[] eligible;static bool[] rockBefore;
        static readonly List<string> checks=new List<string>();static readonly Dictionary<string,object> observations=new Dictionary<string,object>();
        static readonly Queue<KeyValuePair<string,TileMapState>> previews=new Queue<KeyValuePair<string,TileMapState>>();
        static DateTime deadline;static bool pending;
        static void Check(bool pass,string message){checks.Add((pass?"PASS: ":"FAIL: ")+message);if(!pass)throw new InvalidOperationException(message);}
        static void Finish(Exception error=null){pending=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"observations",observations},{"error",error?.ToString()}}));Application.Quit();}
        public static void Tick(){if(pending && DateTime.UtcNow>deadline)Finish(new TimeoutException("Coverage preview timed out"));}
        static object Invoke(object dialog,string name,params object[] args)=>typeof(Dialog_TextToMap).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,args);
        static TileMapState Apply(TileMapState before,string response)=>MapStateEditor.Merge(before,MapParameterParser.Parse(ProviderResponse.Command(response).GetObject("params")));
        static string Fill(float amount)=>"{\"action\":\"generate\",\"params\":{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"soil_fraction\",\"type\":\"region_fill\",\"region\":\"central_mountain_donut\",\"region_part\":\"enclosed\",\"coverage\":"+amount.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"fill\":\"SoilRich\"}}]}}";
        public static void Run(string output,string evidence,string replies)
        {
            folder=output;
            try
            {
                new Harmony("choco.mapgenai.probe.coverage").Patch(AccessTools.Method(typeof(AuthoringGeneration),"ApplyCoverage"),prefix:new HarmonyMethod(typeof(CoverageProbe),nameof(Before)),postfix:new HarmonyMethod(typeof(CoverageProbe),nameof(After)));
                var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(20).ToList();
                Check(tiles.Count==20,"isolated inland tiles available");
                var baseline=Apply(new TileMapState(),File.ReadAllText(Path.Combine(evidence,"line-0058-response.json")));
                // Ore/mutator details are unrelated; keep the captured mountain exactly as generated.
                baseline.mutators.Clear();baseline.oreDensity=1;
                target=tiles[0].tile;Find.WorldSelector.SelectedTile=target;
                var legacy=Apply(baseline,File.ReadAllText(Path.Combine(evidence,"line-0072-response.json")));
                var cases=new Dictionary<string,TileMapState>{{"legacy",legacy},{"coverage70",Apply(baseline,Fill(.7f))},{"coverage50",Apply(baseline,Fill(.5f))},{"coverage100",Apply(baseline,Fill(1))}};
                var moved=Apply(cases["coverage70"],"{\"action\":\"generate\",\"params\":{\"shape_ops\":[{\"op\":\"move\",\"id\":\"central_mountain_donut\",\"position\":[0.6,0.5]}]}}");cases["moved70"]=moved;
                var natural=cases["coverage70"].Clone();natural.elevationShapes[0].edge_roughness="high";cases["natural70"]=natural;
                var ellipse=cases["coverage70"].Clone();ellipse.elevationShapes[0].compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="ellipse",prim="ellipse",center=new[]{.5f,.5f},w=.5f,h=.3f}};ellipse.elevationShapes[0].compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="ellipse",e=.05f,fill="Soil"}};ellipse.elevationShapes[1].region_part="inside";cases["ellipse70"]=ellipse;
                var ruin=cases["coverage70"].Clone();ruin.structures.Add(new StructurePlan{id="inside_ruin",region="soil_fraction",width=9,height=11,count=1});cases["positioned-ruin70"]=ruin;
                foreach(var entry in new Dictionary<string,TileMapState>{{"baseline",baseline},{"filled70",cases["coverage70"]},{"legacy",legacy}})
                {
                    MapGenParams.RestoreSnapshot(entry.Value,target);
                    File.WriteAllText(Path.Combine(folder,entry.Key+"-before.json"),MapStateCodec.Serialize(entry.Value));
                    File.WriteAllText(Path.Combine(folder,entry.Key+"-prompt.txt"),(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{target}));
                }
                var dialogBase=cases["coverage70"];MapGenParams.RestoreSnapshot(baseline,target);var dialog=new Dialog_TextToMap();Invoke(dialog,"HandleResponse",Fill(.7f));
                Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(dialogBase),"actual Dialog adds coverage plan");
                var history=(List<ChatMessage>)typeof(Dialog_TextToMap).GetField("_history",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                Check(history.Last().Content.Contains("70%") && !history.Last().Content.Contains("soil_fraction"),"applied summary explains the percentage without internal IDs");
                File.WriteAllText(Path.Combine(folder,"coverage-applied.txt"),history.Last().Content);Invoke(dialog,"DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(baseline),"actual Undo restores mountain and removes only fill");dialog.PostClose();
                var fixture=new ProbeEnvelope{state=dialogBase};string saved=MapStateCodec.Serialize(fixture.state),scribe=Path.Combine(folder,"coverage-scribe.xml");
                Scribe.saver.InitSaving(scribe,"Coverage");Scribe_Deep.Look(ref fixture,"fixture");Scribe.saver.FinalizeSaving();fixture=null;
                Scribe.loader.InitLoading(scribe);Scribe_Deep.Look(ref fixture,"fixture");Scribe.loader.FinalizeLoading();Check(saved==MapStateCodec.Serialize(fixture.state),"real Scribe preserves source part and coverage fraction");
                if(!string.IsNullOrEmpty(replies))foreach(var path in Directory.GetFiles(replies,"*-response.json").OrderBy(p=>p))
                {
                    string id=Path.GetFileName(path).Replace("-response.json","");var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(replies,id+"-before.json")));
                    MapGenParams.RestoreSnapshot(before,target);var d=new Dialog_TextToMap();Invoke(d,"HandleResponse",File.ReadAllText(path));var actual=MapGenParams.CaptureState(target);
                    var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(replies,id+"-after.json")));Check(MapStateCodec.Serialize(actual)==MapStateCodec.Serialize(expected),id+": actual model response applies through Dialog");
                    Invoke(d,"DoUndo");Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(before),id+": Undo preserves source state");d.PostClose();cases[id]=actual;
                }
                int index=0;
                foreach(var entry in cases)
                {
                    caseId=entry.Key;state=entry.Value;target=tiles[index++].tile;MapGenParams.RestoreSnapshot(state,target);measuring=caseId!="legacy";preview=false;
                    var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                    var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);generator.genSteps=new List<GenStepDef>(source.genSteps);
                    generator.genSteps.Add(new GenStepDef{defName="CoverageCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=folder,id=caseId}});
                    var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                    var result=AuthoringGeneration.Latest(target,state);Check(result!=null && result.issues.Count==0,caseId+": complete map generation has no authoring errors");
                    if(caseId=="positioned-ruin70")Check(result.placements.Count==1 && result.placements[0].spawnedWalls>0,"ruin occupies the counted soil region and survives final area recount");
                    if(measuring)
                    {
                        int finalRich=map.AllCells.Count(c=>eligible[c.z*map.Size.x+c.x] && map.terrainGrid.TerrainAt(c).defName=="SoilRich");
                        double fraction=double.Parse(state.elevationShapes.Single(s=>s.type=="region_fill").coverage,System.Globalization.CultureInfo.InvariantCulture);
                        observations[caseId+"-final"]=new Dictionary<string,object>{{"richSoil",finalRich},{"eligible",eligible.Count(b=>b)},{"otherTerrain",map.AllCells.Where(c=>eligible[c.z*map.Size.x+c.x] && map.terrainGrid.TerrainAt(c).defName!="SoilRich").GroupBy(c=>map.terrainGrid.TerrainAt(c).defName).ToDictionary(g=>g.Key,g=>(object)g.Count())}};
                        Check(Math.Abs(finalRich/(double)eligible.Count(b=>b)-fraction)<.002,caseId+": percentage also survives the remaining full-map generation steps");
                    }
                    if(caseId=="legacy")
                    {
                        int all=0,rich=0;foreach(var c in map.AllCells)if((c.x-125)*(c.x-125)+(c.z-125)*(c.z-125)<35*35 && c.GetEdifice(map)==null){all++;if(map.terrainGrid.TerrainAt(c).defName=="SoilRich")rich++;}
                        Check(all>1000 && rich/(double)all>.95,"recorded response reproduces near-total rich soil in the central visible interior");observations[caseId]=new Dictionary<string,object>{{"centralCells",all},{"richSoilCells",rich}};
                    }
                    Check(!GenerationContext.Active,"generation scope disposed for "+caseId);
                }
                Check(!MapGenAI.ImageInput.ImageFeatureGate.Enabled,"image feature remains off");
                target=tiles[19].tile;previews.Enqueue(new KeyValuePair<string,TileMapState>("preview70",cases["coverage70"]));previews.Enqueue(new KeyValuePair<string,TileMapState>("preview50",cases["coverage50"]));NextPreview();
            }
            catch(Exception error){Finish(error);}
        }
        static void Before(Map map)
        {
            if(!measuring)return;
            int n=map.Size.x*map.Size.z;terrainBefore=new string[n];elevationBefore=new float[n];rockBefore=new bool[n];eligible=new bool[n];
            foreach(var c in map.AllCells)
            {
                int i=c.z*map.Size.x+c.x;var terrain=map.terrainGrid.TerrainAt(c);terrainBefore[i]=terrain.defName;elevationBefore[i]=MapGenerator.Elevation[c];rockBefore[i]=c.GetEdifice(map)!=null;
                // Independent known captured hole geometry, then actual generated cell exclusions.
                double x=c.x/(double)map.Size.x-(caseId=="moved70"?.6:.5),z=c.z/(double)map.Size.z-.5;
                bool geometric=caseId=="ellipse70"?x*x/(.25*.25)+z*z/(.15*.15)<=1:x*x+z*z<.18*.18;
                eligible[i]=(geometric || caseId=="natural70") && !rockBefore[i] && !c.GetThingList(map).Any(t=>t is Pawn) && !(elevationBefore[i]>=.7f && MapGenerator.Caves[c]<=0) && !terrain.IsRiver && !terrain.HasTag("Road") && !terrain.defName.Contains("Ocean") && terrain.designationCategory==null && (terrain.costList==null || terrain.costList.Count==0) && terrain.costStuffCount==0 && !terrain.temporary && !terrain.bridge && !terrain.isFoundation && terrain.defName!="Underwall";
            }
            if(caseId=="natural70")
            {
                // Independently flood actual walkable center, bounded by generated mountain rock.
                var inner=new bool[n];var q=new Queue<int>();int start=(map.Size.z/2)*map.Size.x+map.Size.x/2;inner[start]=true;q.Enqueue(start);
                while(q.Count>0){int i=q.Dequeue(),x=i%map.Size.x,z=i/map.Size.x;foreach(int next in new[]{x>0?i-1:-1,x+1<map.Size.x?i+1:-1,z>0?i-map.Size.x:-1,z+1<map.Size.z?i+map.Size.x:-1})if(next>=0 && elevationBefore[next]<.7f && !inner[next]){inner[next]=true;q.Enqueue(next);}}
                for(int i=0;i<n;i++)eligible[i]&=inner[i];
            }
        }
        static void After(Map map)
        {
            if(!measuring)return;var fill=state.elevationShapes.Single(s=>s.type=="region_fill");double fraction=double.Parse(fill.coverage,System.Globalization.CultureInfo.InvariantCulture);
            int all=eligible.Count(b=>b),rich=0,changed=0;bool outsideSame=true,rocksSame=true,heightsSame=true;
            foreach(var c in map.AllCells)
            {
                int i=c.z*map.Size.x+c.x;string terrain=map.terrainGrid.TerrainAt(c).defName;
                if(eligible[i] && terrain=="SoilRich")rich++;
                if(terrain!=terrainBefore[i]){changed++;if(!eligible[i])outsideSame=false;}
                rocksSame&=rockBefore[i]==(c.GetEdifice(map)!=null);heightsSame&=elevationBefore[i]==MapGenerator.Elevation[c];
            }
            Check(all>1000 && Math.Abs(rich/(double)all-fraction)<.002,caseId+": actual terrain cells match requested percentage");
            Check(outsideSame && rocksSame && heightsSame,caseId+": terrain outside hole, all rocks and all elevations preserved");
            observations[caseId]=new Dictionary<string,object>{{"eligible",all},{"richSoil",rich},{"changed",changed},{"ratio",rich/(double)all},{"preview",preview}};
        }
        static void NextPreview()
        {
            if(previews.Count==0){Finish();return;}
            var entry=previews.Dequeue();caseId=entry.Key;state=entry.Value;preview=true;measuring=true;pending=true;deadline=DateTime.UtcNow.AddSeconds(75);MapGenParams.RestoreSnapshot(state,target);
            string previewName=caseId; // Capture a local: no static delegate field to a late-loaded MapPreview type.
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,target,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try{Check(result.InvalidCells==0 && observations.ContainsKey(previewName),previewName+": real background Map Preview completed");var report=AuthoringGeneration.Latest(target,state);Check(report.issues.Count==0,previewName+": no preview authoring failure");var texture=new Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(folder,previewName+".png"),ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);pending=false;NextPreview();}
                catch(Exception error){Finish(error);}
            },error=>Finish(error));
        }
    }
}
