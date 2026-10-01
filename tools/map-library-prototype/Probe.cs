using System;
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

namespace MapGenAI.MapLibraryProbe
{
    // Experiment assembly only. Never installed into the ordinary/DEV product mod.
    [StaticConstructorOnStartup]
    public static class Probe
    {
        static string output, manifest, worldSeed;
        static bool pending, finished;
        static readonly List<object> results = new List<object>();
        static readonly List<object> checks = new List<object>();
        static Probe()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAILibraryProbe", out output)) return;
            if (!GenCommandLine.TryGetCommandLineArg("savedatafolder", out var profile)
                || !File.Exists(Path.Combine(profile, "MAPGENAI_DISPOSABLE"))) throw new Exception("Disposable profile required");
            GenCommandLine.TryGetCommandLineArg("mapgenAILibraryManifest", out manifest);
            worldSeed = SimpleJson.Parse(File.ReadAllText(manifest)).GetString("world_seed");
            var h = new Harmony("choco.mapgenai.map-library-prototype");
            h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix:new HarmonyMethod(typeof(Probe), nameof(Seed)));
            h.Patch(AccessTools.Method(typeof(TickManager), "DoSingleTick"), prefix:new HarmonyMethod(typeof(Probe), nameof(NoSimulation)));
            h.Patch(AccessTools.Method(typeof(LLMClientFactory), "Create"), prefix:new HarmonyMethod(typeof(Probe), nameof(NoProvider)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString) => seedString = worldSeed;
        static bool NoSimulation() => false;
        static bool NoProvider() => throw new InvalidOperationException("Paid provider calls forbidden in map-library prototype");
        static void Check(bool ok, string name) { checks.Add(new Dictionary<string,object>{{"ok",ok},{"name",name}}); if (!ok) throw new Exception(name); }
        static void Start()
        {
            try {
                Directory.CreateDirectory(output); Application.runInBackground=true; Prefs.RunInBackground=true; pending=true;
                LongEventHandler.QueueLongEvent(() => {
                    try { Rand.Seed=902323; Root_Play.SetupForQuickTestPlay(); Find.GameInitData.mapSize=100; Find.GameInitData.PrepForMapGen(); Find.Scenario.PreMapGenerate(); }
                    catch(Exception e) { Finish(e); }
                }, "Play", "Map library disposable experiment", true, null);
            } catch(Exception e) { Finish(e); }
        }
        public static void Started()
        {
            if (!pending || finished) return; pending=false;
            LongEventHandler.ExecuteWhenFinished(() => { try { Run(); Finish(null); } catch(Exception e) { Finish(e); } });
        }
        static void Run()
        {
            var root=SimpleJson.Parse(File.ReadAllText(manifest));
            foreach (var item in root.GetObjectArray("cases")) {
                string id=item.GetString("id"), biome=item.GetString("biome"), gl=item.GetString("gl_id");
                int size=(int)item.GetFloat("size",250);
                var tile=Find.WorldGrid.Tiles.First(t => t.PrimaryBiome?.defName==biome && t.hilliness==Hilliness.Flat
                    && !Find.WorldObjects.AnyMapParentAt(t.tile) && FeaturePolicy.WaterNeighbors(t).Count==0 && !FeaturePolicy.HasRiver(t)
                    && (!(t is SurfaceTile st) || st.Roads==null || st.Roads.Count==0));
                int target=tile.tile; Find.WorldSelector.SelectedTile=target; Find.World.info.initialMapSize=new IntVec3(size,1,size);
                if (!string.IsNullOrEmpty(gl)) {
                    var manager=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("GeologicalLandforms.GraphEditor.LandformManager")).FirstOrDefault(t=>t!=null);
                    Check(manager!=null,"GL runtime loaded: "+id);
                    var landform=manager.GetMethod("FindById",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{gl});
                    Check(landform!=null,"Original graph found: "+gl);
                    var def=(TileMutatorDef)landform.GetType().GetProperty("TileMutatorDef").GetValue(landform,null);
                    Check(def!=null,"Original graph worker available: "+gl);
                    Check(!ModsConfig.OdysseyActive,"Source profile really excludes overlapping Odyssey graphs");
                    // Use GL's Terrain-tab editor contract: an explicit TileData
                    // commit. AddMutator selection can be disabled by GL settings.
                    var info=manager.Assembly.GetType("GeologicalLandforms.WorldTileInfo");
                    var tileInfo=info.GetMethod("Get",new[]{typeof(int),typeof(bool)}).Invoke(null,new object[]{target,false});
                    var dataType=manager.Assembly.GetType("GeologicalLandforms.LandformData");
                    var data=Activator.CreateInstance(dataType.GetNestedType("TileData"),new[]{tileInfo});
                    data.GetType().GetField("Landforms").SetValue(data,new List<string>{gl});
                    var components=(System.Collections.IEnumerable)AccessTools.Field(typeof(World),"components").GetValue(Find.World);
                    var component=components.Cast<object>().Single(c=>c.GetType()==dataType);
                    dataType.GetMethod("CommitDirectly").Invoke(component,new object[]{target,data});
                    info.GetMethod("InvalidateCache",Type.EmptyTypes).Invoke(null,null);
                    Check(tile.Mutators.Any(m=>m.defName=="GL_"+gl),"GL graph registered on experiment tile: "+gl);
                }
                var before=new TileMapState(); MapGenParams.RestoreSnapshot(before,target);
                string commandFile=item.GetString("command"); TileMapState state=before;
                if (!string.IsNullOrEmpty(commandFile)) {
                    var command=SimpleJson.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest),commandFile))));
                    var options=SimpleJson.Parse("{\"action\":\"recommend\",\"options\":[{\"params\":"+SimpleJson.Serialize(command.GetObject("params"))+"}]}");
                    string untouched=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                    var plans=RecommendationPlan.Validate(options,before,data=>MapGenParams.ValidatePatch(data,target),false);
                    state=plans[0].Resolve(before);
                    Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==untouched,"Private candidate leaves actual state unchanged: "+id);
                    MapStateValidation.Validate(state);
                    string serialized=MapStateCodec.Serialize(state);
                    Check(MapStateCodec.Serialize(MapStateCodec.Deserialize(serialized))==serialized,"Recipe state codec roundtrip: "+id);
                    MapGenParams.ApplyPatches(plans[0].Edits(),target);
                    Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==serialized,"Existing ApplyPatches reproduces resolved candidate: "+id);
                    MapGenParams.RestoreSnapshot(before,target);
                    Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==untouched,"Existing snapshot restore returns to base: "+id);
                    MapGenParams.RestoreSnapshot(state,target);
                }
                File.WriteAllText(Path.Combine(output,id+"-state.json"),MapStateCodec.Serialize(state));
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile=target; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                var original=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                if(!string.IsNullOrEmpty(commandFile)) {
                    // Paired baseline on the same tile/seed. Native ponds and rocks
                    // outside the recipe must not be mistaken for converter spill.
                    MapGenParams.RestoreSnapshot(before,target);
                    var nativeGenerator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(original,null);
                    nativeGenerator.genSteps=new List<GenStepDef>(original.genSteps);
                    var nativeCapture=new Capture{Id=id+"-baseline",Size=size,Target=target,State=before,IsSource=true};
                    nativeGenerator.genSteps.Add(new GenStepDef{defName="MapLibraryBaseline_"+id,order=99999,genStep=nativeCapture});
                    var baselineMap=MapGenerator.GenerateMap(new IntVec3(size,1,size),parent,nativeGenerator);
                    Check(nativeCapture.Captured,"Paired native baseline captured on same tile: "+id);
                    // Full native disposal requires initialized mesh sections;
                    // this synchronous batch runs before the first map GUI frame.
                    baselineMap.mapDrawer.RegenerateEverythingNow();
                    Current.Game.DeinitAndRemoveMap(baselineMap,false);
                    if(!parent.Destroyed)parent.Destroy();
                    parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                    MapGenParams.RestoreSnapshot(state,target);
                }
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(original,null);
                generator.genSteps=new List<GenStepDef>(original.genSteps);
                var capture=new Capture{Id=id,Size=size,Target=target,State=state,IsSource=!string.IsNullOrEmpty(gl)};
                generator.genSteps.Add(new GenStepDef{defName="MapLibraryCapture_"+id,order=99999,genStep=capture});
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var map=MapGenerator.GenerateMap(new IntVec3(size,1,size),parent,generator);
                clock.Stop(); Check(capture.Captured,"Actual full map capture completed: "+id);
                results.Add(new Dictionary<string,object>{{"id",id},{"tile",target},{"biome",map.Biome.defName},{"size",size},{"gl_id",gl},{"world_seed",worldSeed},{"seconds",clock.Elapsed.TotalSeconds},{"mutators",tile.Mutators.Select(m=>m.defName).ToArray()},{"rainfall",tile.rainfall},{"temperature",tile.temperature},{"counts",capture.Counts},{"authoring_report_present",capture.AuthoringReportPresent},{"provider_calls",0}});
                Save("progress.json",new Dictionary<string,object>{{"results",results},{"checks",checks}});
            }
        }
        sealed class Capture : GenStep
        {
            public string Id; public int Size,Target; public bool IsSource,Captured,AuthoringReportPresent; public TileMapState State;
            public Dictionary<string,int> Counts;
            public override int SeedPart=>2739471;
            public override void Generate(Map map,GenStepParams parms)
            {
                var labels=new char[Size*Size]; Counts=new Dictionary<string,int>{{"mountain",0},{"water",0},{"shallow",0},{"ground",0}};
                foreach(var c in map.AllCells) {
                    // Observe permanent topology beneath seasonal ThinIce, while
                    // the PNG and terrain_defs still show the actual frozen surface.
                    var terrain=map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)); bool rock=c.GetEdifice(map)?.def.building?.isNaturalRock==true;
                    bool ordinary=terrain.defName.StartsWith("Water",StringComparison.Ordinal);
                    char v=ordinary ? (terrain.passability==Traversability.Impassable?'W':'S') : rock?'M':'G';
                    labels[c.z*Size+c.x]=v; Counts[v=='M'?"mountain":v=='W'?"water":v=='S'?"shallow":"ground"]++;
                }
                var fertility=new char[Size*Size];var terrainCounts=new Dictionary<string,int>();
                foreach(var c in map.AllCells) {
                    var terrain=map.terrainGrid.TerrainAt(c);string name=terrain.defName;
                    fertility[c.z*Size+c.x]=name=="SoilRich"?'R':name=="Soil"?'F':'N';
                    if(!terrainCounts.ContainsKey(name))terrainCounts[name]=0;terrainCounts[name]++;
                }
                Save(Id+"-terrain.json",new Dictionary<string,object>{{"width",Size},{"height",Size},{"cells",new string(labels)},{"fertile_cells",new string(fertility)},{"terrain_defs",terrainCounts},{"row_order","south-first"},{"note","Final named ordinary Water terrain/passability beneath temporary seasonal ice; actual surface names in terrain_defs and PNG. Soil/SoilRich and natural-rock edifices. Marsh/hot springs are not silently imported as ordinary ponds."}});
                var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,Target,new IntVec2(Size,Size));
                var result=new MapPreview.MapPreviewResult(request);
                var gt=typeof(MapPreview.MapPreviewGenerator);
                var step=(GenStep)Activator.CreateInstance(gt.GetNestedType("PreviewTextureGenStep",BindingFlags.NonPublic),new object[]{result,true});
                step.Generate(map,parms); gt.GetMethod("AddBevelToSolidStone",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{result});
                var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
                try {texture.SetPixels(result.Pixels);texture.Apply();File.WriteAllBytes(Path.Combine(output,Id+"-map.png"),ImageConversion.EncodeToPNG(texture));}
                finally {UnityEngine.Object.Destroy(texture);}
                if(!IsSource) {
                    var report=AuthoringGeneration.Latest(Target,State);
                    // Shape-only recipes need no structure/passage authoring pass.
                    // A missing report is recorded, never counted as placement proof.
                    AuthoringReportPresent=report!=null;
                    if(report!=null)Check(report.issues.Count==0,"Reported authoring placements succeeded: "+Id);
                }
                Captured=true;
            }
        }
        static void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),SimpleJson.Serialize(value));
        static void Finish(Exception e)
        {
            if(finished)return; finished=true;
            Save("result.json",new Dictionary<string,object>{{"ok",e==null},{"error",e?.ToString()},{"checks",checks},{"results",results},{"scope","Actual full generated maps rendered using native Map Preview colors. Execution/placement checks do not certify aesthetic quality."}});
            if(e!=null)Log.Error(e.ToString()); Application.Quit();
        }
    }
    public sealed class ProbeComponent : GameComponent
    {
        public ProbeComponent(Game game) { }
        public override void StartedNewGame()=>Probe.Started();
    }
}
