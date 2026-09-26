using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MapGenAI;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.NaturalProbe
{
    [StaticConstructorOnStartup]
    public static class Probe
    {
        sealed class Job { public string name;public TileMapState state;public int tile; }
        static string output,group,layout;static bool booting,active;static int target;static DateTime deadline;
        static readonly Queue<Job> jobs=new Queue<Job>();static Job current;static RecommendationPreviews preview;
        static readonly List<string> checks=new List<string>();static readonly List<object> results=new List<object>();
        static Dictionary<string,object> audit;static double geometryMs;
        const string WorldSeed="mapgenai-guided-live-20260923";
        static void Save(string name,string text)=>File.WriteAllText(Path.Combine(output,name),text);
        static void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);if(!ok)throw new Exception(text);}
        static Probe()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAIProbe",out output))return;
            GenCommandLine.TryGetCommandLineArg("mapgenAIProbeSet",out group);
            GenCommandLine.TryGetCommandLineArg("mapgenAIProbeLayout",out layout);
            if(!GenCommandLine.TryGetCommandLineArg("savedatafolder",out string profile) || !File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))throw new Exception("Marked disposable profile required");
            var h=new Harmony("choco.mapgenai.natural-probe");
            h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(Probe),nameof(Seed)));
            h.Patch(AccessTools.Method(typeof(PassageGeneration),"Check"),postfix:new HarmonyMethod(typeof(Probe),nameof(Measure)));
            h.Patch(AccessTools.Method(typeof(NaturalLandformGeneration),"Apply"),prefix:new HarmonyMethod(typeof(Probe),nameof(BeforeGeometry)),postfix:new HarmonyMethod(typeof(Probe),nameof(AfterGeometry)));
            h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoSimulation)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString)=>seedString=WorldSeed;
        static bool NoSimulation()=>false; // Only this disposable rendering harness; no colony simulation.
        static void BeforeGeometry(out Stopwatch __state)=>__state=Stopwatch.StartNew();
        static void AfterGeometry(Stopwatch __state)=>geometryMs+=__state.Elapsed.TotalMilliseconds;
        static void Start()
        {
            try
            {
                Directory.CreateDirectory(output);Application.runInBackground=true;Prefs.RunInBackground=true;
                booting=true;LongEventHandler.QueueLongEvent(()=>{
                    try{Root_Play.SetupForQuickTestPlay();Find.GameInitData.mapSize=100;Find.GameInitData.PrepForMapGen();Find.Scenario.PreMapGenerate();}
                    catch(Exception e){Finish(e);}
                },"Play","MapGenAI isolated natural terrain verification",true,null);
            }catch(Exception e){Finish(e);}
        }
        static TileMapState State(string kind,int variant)
        {
            var s=new TileMapState();s.elevationShapes.Add(new ElevationShape{id="landscape",type="landform",landform=kind,layout=layout,direction=kind=="foothills"?"left":kind=="winding_valley"?"top":"bottom",variant=variant.ToString()});return s;
        }
        static TileMapState Edit(TileMapState s,string text)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(text)));
        public static void Started()
        {
            if(!booting)return;booting=false;
            LongEventHandler.ExecuteWhenFinished(()=>{
                try
                {
                    var tile=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="TemperateForest" && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));target=tile.tile;
                    Find.WorldSelector.SelectedTile=target;Find.World.info.initialMapSize=new IntVec3(250,1,250);
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Save("tile.json",SimpleJson.Serialize(new Dictionary<string,object>{{"tile",target},{"biome",tile.PrimaryBiome.defName},{"hilliness",tile.hilliness.ToString()},{"worldSeed",WorldSeed},{"mapSize",250}}));
                    MapGenParams.RestoreSnapshot(new TileMapState(),target);
                    Save("system-prompt.txt",(string)AccessTools.Method(typeof(Dialog_TextToMap),"BuildSystemPrompt").Invoke(null,new object[]{target}));
                    if(GenCommandLine.TryGetCommandLineArg("mapgenAIProbeStates",out string states))
                    {
                        foreach(string path in Directory.GetFiles(states,"*.json").OrderBy(p=>p,StringComparer.Ordinal))
                        {
                            var replay=MapStateCodec.Deserialize(File.ReadAllText(path));MapStateValidation.Validate(replay);
                            jobs.Enqueue(new Job{name=Path.GetFileNameWithoutExtension(path),state=replay,tile=target});
                        }
                        Check(jobs.Count>0,"Recorded provider states supplied");
                        VerifyPersistence(jobs.Peek().state);
                        active=true;Next();return;
                    }
                    if(group=="composition")
                    {
                        foreach(string kind in new[]{"lakeside","winding_valley","branching_ridges","open_basin"})
                            jobs.Enqueue(new Job{name=kind,state=MapGenAI.TestFixtures.LandscapeCompositionFixtures.Create(kind),tile=target});
                        VerifyPersistence(jobs.Peek().state);active=true;Next();return;
                    }
                    jobs.Enqueue(new Job{name="baseline",state=new TileMapState(),tile=target});
                    foreach(string kind in new[]{"open_basin","winding_valley","foothills"})foreach(int variant in new[]{0,11,23,47,89})jobs.Enqueue(new Job{name=kind+"-"+variant,state=State(kind,variant),tile=target});
                    var basis=State("open_basin",23);
                    var wide=Edit(basis,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"landscape\",\"changes\":{\"gap\":0.3}}]}");
                    jobs.Enqueue(new Job{name="basin-wider",state=wide,tile=target});
                    var soil=Edit(wide,"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"interior_soil\",\"type\":\"region_fill\",\"region\":\"landscape\",\"region_part\":\"inside\",\"coverage\":0.7,\"fill\":\"rich_soil\"}}]}");
                    var ruin=Edit(soil,"{\"structure_ops\":[{\"op\":\"add\",\"structure\":{\"id\":\"interior_ruin\",\"kind\":\"ruin\",\"region\":\"landscape\",\"region_part\":\"inside\",\"width\":9,\"height\":7}}]}");
                    jobs.Enqueue(new Job{name="basin-soil-ruin",state=ruin,tile=target});
                    VerifyPersistence(ruin);
                    var river=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="TemperateForest" && FeaturePolicy.HasRiver(t) && !t.Mutators.Any(m=>m.categories.Contains("Lake")) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
                    jobs.Enqueue(new Job{name="river-baseline",state=new TileMapState{hasRiver=true},tile=river.tile});
                    var r=State("winding_valley",23);r.hasRiver=true;jobs.Enqueue(new Job{name="valley-existing-river",state=r,tile=river.tile});
                    var coast=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome.defName=="TemperateForest" && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Any(n=>n.PrimaryBiome==BiomeDefOf.Ocean) && !Find.WorldObjects.AnyMapParentAt(t.tile));
                    jobs.Enqueue(new Job{name="coast-baseline",state=new TileMapState(),tile=coast.tile});
                    jobs.Enqueue(new Job{name="foothills-existing-coast",state=State("foothills",23),tile=coast.tile});
                    var all=jobs.ToArray();jobs.Clear();
                    foreach(var job in all)
                    {
                        bool basic=job.name=="baseline" || job.name.StartsWith(group+"-") && !job.name.Contains("existing");
                        bool extra=job.name.StartsWith("basin-") || job.name.Contains("baseline") || job.name.Contains("existing");
                        if(group=="followup"?extra:basic)jobs.Enqueue(job);
                    }
                    active=true;Next();
                }catch(Exception e){Finish(e);}
            });
        }
        static void VerifyPersistence(TileMapState state)
        {
            var box=new Envelope{state=state};string path=Path.Combine(output,"scribe.xml"),expected=MapStateCodec.Serialize(state);
            Scribe.saver.InitSaving(path,"NaturalLandformProbe");Scribe_Deep.Look(ref box,"fixture");Scribe.saver.FinalizeSaving();box=null;
            Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref box,"fixture");Scribe.loader.FinalizeLoading();Check(MapStateCodec.Serialize(box.state)==expected,"Native Scribe preserves landform fields and dependent fill/ruin");
            Check(MapStateCodec.Serialize(MapStateCodec.Deserialize(expected))==expected,"Preset roundtrip preserves landform");
            string before=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var dialog=new Dialog_TextToMap();
            var parameters=new Dictionary<string,object>{{"elevation_shapes",ShapeEdits.Describe(state.elevationShapes)},{"structure_ops",state.structures.Select(s=>new Dictionary<string,object>{{"op","add"},{"structure",s}}).ToList()}};
            var data=MapParameterParser.Parse(SimpleJson.Parse(SimpleJson.Serialize(parameters)));
            AccessTools.Method(typeof(Dialog_TextToMap),"ApplyEdits").Invoke(dialog,new object[]{new List<MapParamsData>{data},null});
            Check(MapGenParams.CaptureState(target).elevationShapes.Count==state.elevationShapes.Count,"Dialog applies all fixture areas through normal edit pipeline");
            AccessTools.Method(typeof(Dialog_TextToMap),"DoUndo").Invoke(dialog,null);
            Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==before,"Dialog Undo restores the original state");dialog.PostClose();
        }
        public sealed class Envelope:IExposable {public TileMapState state;public void ExposeData()=>Scribe_Deep.Look(ref state,"state");}
        static void Next()
        {
            preview?.Dispose();preview=null;
            if(jobs.Count==0){if(group=="followup")FullMap();else if(group=="composition")FullReplay(Path.Combine(output,"winding_valley-state.json"));else if(GenCommandLine.TryGetCommandLineArg("mapgenAIProbeFullState",out string fullState))FullReplay(fullState);Finish(null);return;}
            current=jobs.Dequeue();audit=null;geometryMs=0;deadline=DateTime.UtcNow.AddMinutes(3);
            Save("progress.txt",current.name);Save(current.name+"-state.json",MapStateCodec.Serialize(current.state));
            // Retain every scalar/feature/road setting in the baseline, and replay areas as changes
            // so the production preview's new-water loss detector also observes their footprints.
            var baseline=current.state.Clone();baseline.elevationShapes.Clear();baseline.structures.Clear();
            var parameters=new Dictionary<string,object>{{"elevation_shapes",ShapeEdits.Describe(current.state.elevationShapes)}};
            if(current.state.structures.Count>0)parameters["structure_ops"]=current.state.structures.Select(s=>new Dictionary<string,object>{{"op","add"},{"structure",s}}).ToList();
            string command=SimpleJson.Serialize(new Dictionary<string,object>{{"action","generate"},{"params",parameters}});
            // Also renders an intentional no-op baseline, which the suggestion UI does not offer.
            var plan=(RecommendationPlan)Activator.CreateInstance(typeof(RecommendationPlan),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{command,"controlled native fixture"},null);
            Check(MapStateCodec.Serialize(plan.Resolve(baseline))==MapStateCodec.Serialize(current.state),current.name+": replay preserves the complete provider state");
            preview=new RecommendationPreviews(current.tile,new[]{plan},baseline);
        }
        public static void Tick()
        {
            if(!active)return;
            try
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException(current?.name);
                preview.Update();var item=preview.Items[0];if(!item.Complete)return;
                Check(item.Texture!=null && item.Error==null,current.name+": native preview rendered");
                Check(item.Rejection==null,current.name+": no known placement rejection");
                File.WriteAllBytes(Path.Combine(output,current.name+".png"),item.Texture.EncodeToPNG());
                var row=new Dictionary<string,object>{{"id",current.name},{"seconds",item.Seconds},{"geometryMs",geometryMs},{"audit",audit},{"warning",item.Warning}};results.Add(row);
                Save("results-in-progress.json",SimpleJson.Serialize(results));Next();
            }catch(Exception e){Finish(e);}
        }
        static void Measure(Map map)
        {
            if(current==null || GenerationContext.State==null)return;
            var regions=GenerationContext.Regions(map);var source=GenerationContext.State.elevationShapes.FirstOrDefault(s=>s.type=="landform");
            int water=0,mountains=0,floor=0,floorRocks=0,selectedSoil=0;var mask=source==null?new bool[map.Size.x*map.Size.z]:regions.Mask(source.id);
            foreach(var shape in GenerationContext.State.elevationShapes.Where(s=>s.type=="composite" && s.fill==null && s.compositeOps.All(o=>o.fill==null) && s.compositeOps.Any(o=>o.e>0 && o.e<.1f)))
            {var floorMask=regions.Mask(shape.id);for(int i=0;i<mask.Length;i++)mask[i]|=floorMask[i];}
            foreach(var c in map.AllCells)
            {
                int i=c.z*map.Size.x+c.x;var terrain=map.terrainGrid.TerrainAt(c);if(terrain.IsWater)water++;if(MapGenerator.Elevation[c]>=.7f)mountains++;
                if(mask[i]){floor++;if(MapGenerator.Elevation[c]>=.7f)floorRocks++;}
                if(regions.Contains("interior_soil",c.x,c.z) && terrain.defName=="SoilRich")selectedSoil++;
            }
            var report=AuthoringGeneration.Current;
            string rivers=string.Join(",",map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).IsRiver).Select(c=>c.z*map.Size.x+c.x));
            string oceans=string.Join(",",map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).defName.Contains("Ocean")).Select(c=>c.z*map.Size.x+c.x));
            audit=new Dictionary<string,object>{{"water",water},{"mountains",mountains},{"plannedFloor",floor},{"rocksInPlannedFloor",floorRocks},{"actualSelectedSoil",selectedSoil},{"coverage",report?.coverage},{"placements",report?.placements},{"issues",report?.issues},{"riverCells",rivers},{"oceanCells",oceans}};
            var relations=new List<object>();
            foreach(var shape in GenerationContext.State.elevationShapes.Where(s=>s.anchor!=null))
            {
                var child=regions.Mask(shape.id);var parent=regions.Mask(shape.anchor);int cells=0,violations=0;
                for(int i=0;i<child.Length;i++)if(child[i]){cells++;if(parent[i]==(shape.placement=="beside"))violations++;}
                relations.Add(new Dictionary<string,object>{{"id",shape.id},{"anchor",shape.anchor},{"placement",shape.placement},{"cells",cells},{"violations",violations}});
            }
            audit["relationships"]=relations;
        }
        static void FullReplay(string path)
        {
            current=new Job{name="fullmap-recorded",state=MapStateCodec.Deserialize(File.ReadAllText(path)),tile=target};
            MapGenParams.RestoreSnapshot(current.state,target);
            var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
            var timer=Stopwatch.StartNew();var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
            var report=AuthoringGeneration.Latest(target,current.state);
            Check(report!=null && report.issues.Count==0,"Recorded composition full map has no authoring failure");
            Check(map.AllCells.Any(c=>map.terrainGrid.TerrainAt(c).IsWater),"Recorded full map retains actual water");
            results.Add(new Dictionary<string,object>{{"id",current.name},{"seconds",timer.Elapsed.TotalSeconds},{"audit",audit},{"report",report}});
        }
        static void FullMap()
        {
            current=new Job{name="fullmap-basin-soil-ruin",state=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(output,"basin-soil-ruin-state.json"))),tile=target};
            MapGenParams.RestoreSnapshot(current.state,target);geometryMs=0;
            var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
            var timer=Stopwatch.StartNew();var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
            var report=AuthoringGeneration.Latest(target,current.state);Check(report!=null && report.issues.Count==0,"Full map has no authoring failure");
            Check(report.coverage.Count==1 && Math.Abs(report.coverage[0].selected/(double)report.coverage[0].eligible-.7)<.001,"Full map counts 70% of eligible floor");
            Check(report.placements.Count==1 && report.placements[0].spawnedWalls>0,"Full map spawns the requested ruin");
            Check((int)audit["actualSelectedSoil"]==report.coverage[0].selected,"Full map actual soil matches the counted selection");
            Check((int)audit["rocksInPlannedFloor"]==0,"Full map keeps the planned floor free of mountains");
            results.Add(new Dictionary<string,object>{{"id",current.name},{"seconds",timer.Elapsed.TotalSeconds},{"geometryMs",geometryMs},{"audit",audit}});
        }
        static void Finish(Exception error)
        {
            active=false;preview?.Dispose();preview=null;
            Save("result.json",SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"results",results},{"error",error?.ToString()},{"newProviderCalls",0},{"note","Actual native MapPreview textures in an owned disposable headless copy. A fullmap result row is present only when that stage ran. Inputs may be controlled fixtures or recorded provider states; these are not GUI screenshots."}}));
            if(error!=null)Log.Error("[NaturalProbe] "+error);Application.Quit();
        }
    }
    public sealed class ProbeComponent:GameComponent
    {
        public ProbeComponent(Game game){}
        public override void StartedNewGame()=>Probe.Started();
        public override void GameComponentUpdate()=>Probe.Tick();
    }
}
