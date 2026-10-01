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
            SavePalette();
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
                GroundPass ground=null;
                WaterPass water=null;
                if (!string.IsNullOrEmpty(commandFile)) {
                    var command=SimpleJson.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest),commandFile))));
                    if(command.GetObject("ground_layer")!=null)ground=new GroundPass(command.GetObject("ground_layer"),id);
                    if(command.GetObject("water_layer")!=null)water=new WaterPass(command.GetObject("water_layer"),id);
                    if((ground!=null || water!=null) && command.GetObject("params")?.Keys.Any()==false) {
                        // Ground-only developer controls have no product edit. The
                        // product correctly refuses an empty recommendation.
                        Check(MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(before),"Ground-only control keeps product state unchanged: "+id);
                    }
                    else {
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
                if(id=="water-guard")generator.genSteps.Add(new GenStepDef{defName="MapLibraryWaterGuard",order=402,genStep=new WaterGuardFixture()});
                if(water!=null)generator.genSteps.Add(new GenStepDef{defName="MapLibraryWater",order=403,genStep=water});
                if(id=="ground-guard")generator.genSteps.Add(new GenStepDef{defName="MapLibraryGroundGuard",order=404,genStep=new GuardFixture()});
                if(ground!=null)generator.genSteps.Add(new GenStepDef{defName="MapLibraryGround_"+id,order=405,genStep=ground});
                var capture=new Capture{Id=id,Size=size,Target=target,State=state,IsSource=!string.IsNullOrEmpty(gl)};
                generator.genSteps.Add(new GenStepDef{defName="MapLibraryCapture_"+id,order=99999,genStep=capture});
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var map=MapGenerator.GenerateMap(new IntVec3(size,1,size),parent,generator);
                clock.Stop(); Check(capture.Captured,"Actual full map capture completed: "+id);
                results.Add(new Dictionary<string,object>{{"id",id},{"tile",target},{"biome",map.Biome.defName},{"size",size},{"gl_id",gl},{"world_seed",worldSeed},{"seconds",clock.Elapsed.TotalSeconds},{"mutators",tile.Mutators.Select(m=>m.defName).ToArray()},{"rainfall",tile.rainfall},{"temperature",tile.temperature},{"counts",capture.Counts},{"authoring_report_present",capture.AuthoringReportPresent},{"provider_calls",0}});
                Save("progress.json",new Dictionary<string,object>{{"results",results},{"checks",checks}});
            }
        }
        sealed class GuardFixture : GenStep
        {
            public override int SeedPart=>2739473;
            public override void Generate(Map map,GenStepParams parms)
            {
                var road=DefDatabase<TerrainDef>.AllDefsListForReading.First(d=>d.HasTag("Road"));
                var floor=DefDatabase<TerrainDef>.AllDefsListForReading.First(d=>d.designationCategory!=null && !d.IsWater && !d.dangerous);
                var fixtures=new[]{TerrainDefOf.WaterShallow,road,floor,TerrainDefOf.Soil};
                for(int i=0;i<fixtures.Length;i++) {
                    var c=new IntVec3(12+i,0,12);c.GetEdifice(map)?.Destroy();MapGenerator.Elevation[c]=.2f;map.terrainGrid.SetTerrain(c,fixtures[i]);
                    if(i==3)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named("BlocksGranite")),c,map);
                }
            }
        }
        sealed class WaterGuardFixture : GenStep
        {
            public override int SeedPart=>2739475;
            public override void Generate(Map map,GenStepParams parms)
            {
                var river=DefDatabase<TerrainDef>.AllDefsListForReading.First(t=>t.IsRiver);
                var sea=DefDatabase<TerrainDef>.AllDefsListForReading.First(t=>t.defName=="WaterOceanShallow");
                var special=DefDatabase<TerrainDef>.GetNamed("HotSpring");
                var road=DefDatabase<TerrainDef>.AllDefsListForReading.First(t=>t.HasTag("Road"));
                var floor=DefDatabase<TerrainDef>.AllDefsListForReading.First(t=>t.designationCategory!=null && !t.IsWater && !t.dangerous);
                var points=new[]{new IntVec3(12,0,12),new IntVec3(13,0,12),new IntVec3(20,0,20),new IntVec3(21,0,20),
                    new IntVec3(30,0,30),new IntVec3(31,0,30),new IntVec3(40,0,40),new IntVec3(41,0,40),
                    new IntVec3(48,0,50),new IntVec3(49,0,50),new IntVec3(50,0,50),new IntVec3(51,0,50)};
                var defs=new[]{TerrainDefOf.WaterShallow,TerrainDefOf.WaterDeep,river,TerrainDefOf.WaterShallow,
                    sea,TerrainDefOf.WaterShallow,special,TerrainDefOf.WaterShallow,road,floor,TerrainDefOf.Soil,TerrainDefOf.Soil};
                for(int i=0;i<points.Length;i++) {
                    var c=points[i];c.GetEdifice(map)?.Destroy();MapGenerator.Elevation[c]=.2f;map.terrainGrid.SetTerrain(c,defs[i]);
                    if(i==10)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named("BlocksGranite")),c,map);
                    if(i==11)map.terrainGrid.SetTerrain(c,TerrainDefOf.WaterDeep);
                }
                var unknown=new IntVec3(5,0,5);unknown.GetEdifice(map)?.Destroy();map.terrainGrid.SetTerrain(unknown,TerrainDefOf.WaterShallow);
            }
        }
        // Exact observed water layout for a complete library composition only.
        // Product editing and candidates without this sidecar remain unchanged.
        sealed class WaterPass : GenStep
        {
            readonly SimpleJsonObject layer;readonly string id;
            public WaterPass(SimpleJsonObject layer,string id){this.layer=layer;this.id=id;}
            public override int SeedPart=>2739474;
            static bool Ordinary(TerrainDef t)=>t.defName=="WaterShallow" || t.defName=="WaterDeep";
            static bool Special(TerrainDef t)=>t.IsRiver || (t.IsWater && !Ordinary(t) && t.defName!="Marsh");
            public override void Generate(Map map,GenStepParams parms)
            {
                int width=(int)layer.GetFloat("width"),height=(int)layer.GetFloat("height");
                if(layer.GetFloat("schema_version")!=1 || layer.GetString("mode")!="source-composition" || width<=0 || height<=0
                    || layer.GetString("row_order")!="south-first")throw new Exception("Invalid source water contract: "+id);
                var plane=new int[width*height];int end=0;
                foreach(var run in layer.GetObjectArray("runs")) {
                    int start=(int)run.GetFloat("start"),length=(int)run.GetFloat("length"),kind=(int)run.GetFloat("kind");
                    if(start<end || length<=0 || start+length>plane.Length || kind<1 || kind>3)throw new Exception("Invalid water RLE: "+id);
                    for(int n=start;n<start+length;n++)plane[n]=kind;end=start+length;
                }
                Check(true,"Observed water contract and RLE validated: "+id);
                int count=map.cellIndices.NumGridCells;
                var before=new TerrainDef[count];var elevations=new float[count];var blocked=new bool[count];var connected=new bool[count];
                var queue=new Queue<IntVec3>();
                foreach(var c in map.AllCells) {
                    int n=map.cellIndices.CellToIndex(c);var t=map.terrainGrid.TerrainAtIgnoreTemp(n);before[n]=t;elevations[n]=MapGenerator.Elevation[c];
                    var building=c.GetEdifice(map);
                    blocked[n]=Special(t) || t.dangerous || t.HasTag("Road") || t.bridge
                        || t.designationCategory!=null || (building!=null && building.def.building?.isNaturalRock!=true);
                    if(Special(t)) {connected[n]=true;queue.Enqueue(c);}
                }
                // A fresh-water-looking cell attached to a river/sea/special pool
                // is part of that protected water body, not an incidental pond.
                while(queue.Count>0) {
                    var c=queue.Dequeue();
                    foreach(var d in GenAdj.CardinalDirections) {
                        var at=c+d;if(!at.InBounds(map))continue;int n=map.cellIndices.CellToIndex(at);
                        if(!connected[n] && before[n].IsWater) {connected[n]=true;queue.Enqueue(at);}
                    }
                }
                for(int n=0;n<count;n++)blocked[n]|=connected[n];
                var dry=map.Biome.defName=="Desert" || map.Biome.defName=="ExtremeDesert"?TerrainDefOf.Sand:TerrainDefOf.Soil;
                int added=0,cleared=0,depthChanged=0,conflicts=0,unknown=0,protectedCells=0,rocksRemoved=0;
                var protections=new Dictionary<string,int>{{"river",0},{"sea",0},{"special_water",0},{"connected_water",0},{"road",0},{"floor",0},{"building",0}};
                foreach(var c in map.AllCells) {
                    int n=map.cellIndices.CellToIndex(c),kind=plane[Math.Min(height-1,c.z*height/map.Size.z)*width+Math.Min(width-1,c.x*width/map.Size.x)];
                    if(kind==0){unknown++;continue;}
                    var t=before[n];
                    if(blocked[n]) {
                        protectedCells++;if((kind==2 && t!=TerrainDefOf.WaterShallow) || (kind==3 && t!=TerrainDefOf.WaterDeep) || (kind==1 && Ordinary(t)))conflicts++;
                        if(t.IsRiver)protections["river"]++;
                        if(t.defName.StartsWith("WaterOcean",StringComparison.Ordinal))protections["sea"]++;
                        if(t.IsWater && !t.IsRiver && !Ordinary(t) && !t.defName.StartsWith("WaterOcean",StringComparison.Ordinal))protections["special_water"]++;
                        if(connected[n] && Ordinary(t))protections["connected_water"]++;
                        if(t.HasTag("Road"))protections["road"]++;
                        if(t.designationCategory!=null)protections["floor"]++;
                        if(c.GetEdifice(map)!=null)protections["building"]++;
                        continue;
                    }
                    if(kind==1) {
                        if(Ordinary(t)){map.terrainGrid.SetTerrain(c,dry);cleared++;}
                        continue;
                    }
                    var desired=kind==2?TerrainDefOf.WaterShallow:TerrainDefOf.WaterDeep;
                    if(!Ordinary(t))added++;else if(t!=desired)depthChanged++;
                    var rock=c.GetEdifice(map);
                    if(rock?.def.building?.isNaturalRock==true){rock.Destroy();rocksRemoved++;}
                    map.terrainGrid.SetTerrain(c,desired);MapGenerator.Elevation[c]=Math.Min(elevations[n],.3f);
                }
                int protectedChanged=0,unknownChanged=0,outsideWaterHeightChanged=0,knownMismatches=0;
                foreach(var c in map.AllCells) {
                    int n=map.cellIndices.CellToIndex(c),kind=plane[Math.Min(height-1,c.z*height/map.Size.z)*width+Math.Min(width-1,c.x*width/map.Size.x)];
                    var t=map.terrainGrid.TerrainAtIgnoreTemp(n);
                    if(blocked[n] && (t!=before[n] || MapGenerator.Elevation[c]!=elevations[n]))protectedChanged++;
                    if(kind==0 && t!=before[n])unknownChanged++;
                    if(kind<2 && MapGenerator.Elevation[c]!=elevations[n])outsideWaterHeightChanged++;
                    if(!blocked[n] && ((kind==1 && Ordinary(t)) || (kind==2 && t!=TerrainDefOf.WaterShallow) || (kind==3 && t!=TerrainDefOf.WaterDeep)))knownMismatches++;
                }
                Check(protectedChanged==0 && unknownChanged==0 && outsideWaterHeightChanged==0,"Water layout preserves special/connected water, structures and unknown cells: "+id);
                Check(knownMismatches==0,"Known source water depth and dry cells reproduced: "+id);
                if(id=="water-guard") {
                    Check(cleared>0 && added>0 && depthChanged>0,"Real pond removal, water addition and depth changes exercised");
                    Check(protections.Values.All(n=>n>0),"Real river, sea, special/connected water, road, floor and wall protected");
                }
                Save(id+"-water-application.json",new Dictionary<string,object>{{"added_water_cells",added},{"cleared_ordinary_pond_cells",cleared},
                    {"depth_changed_cells",depthChanged},{"natural_rocks_removed_inside_requested_water",rocksRemoved},{"protected_cells",protectedCells},
                    {"protected_changes",protectedChanged},{"unknown_cells",unknown},{"unknown_changes",unknownChanged},{"outside_water_height_changes",outsideWaterHeightChanged},
                    {"known_source_mismatches",knownMismatches},{"protected_conflicts",conflicts},{"protection_kinds",protections},{"stage",403},
                    {"scope","Complete observed inland composition only; conflicting protected cells remain intact and need candidate rejection"}});
            }
        }
        static int[] Rgb(Color color) {Color32 c=color;return new[]{(int)c.r,(int)c.g,(int)c.b};}
        static void SavePalette()
        {
            var rows=new List<object>();
            foreach(var def in DefDatabase<TerrainDef>.AllDefsListForReading.OrderBy(d=>d.defName)) {
                bool found=MapPreview.TrueTerrainColors.TrueColors.TryGetValue(def.defName,out var color);
                bool defaultFound=MapPreview.TrueTerrainColors.DefaultColors.TryGetValue(def.defName,out var defaultColor);
                rows.Add(new Dictionary<string,object>{{"def",def.defName},{"label",def.label},{"rgb",found?Rgb(color):null},
                    {"default_rgb",defaultFound?Rgb(defaultColor):null},{"default_has_preview_color",defaultFound},
                    {"has_preview_color",found},{"supported",TerrainMaterials.Supported(def)},{"water",def.IsWater},{"river",def.IsRiver},
                    {"temporary",def.temporary},{"dangerous",def.dangerous},{"fertility",def.fertility}});
            }
            var overlays=new List<object>();
            foreach(string name in new[]{"SolidStoneColor","SolidStoneHighlightColor","SolidStoneShadowColor","CaveColor","MissingTerrainColor"}) {
                var field=typeof(MapPreview.MapPreviewGenerator).GetField(name,BindingFlags.Static|BindingFlags.NonPublic);
                if(field!=null)overlays.Add(new Dictionary<string,object>{{"name",name},{"rgb",Rgb((Color)field.GetValue(null))}});
            }
            Save("native-terrain-palette.json",new Dictionary<string,object>{{"schema_version",2},{"renderer","MapPreview.TrueTerrainColors.TrueColors"},{"terrains",rows},{"overlays",overlays},
                {"policy","Read-only renderer catalog. Missing colors remain missing; same colors do not imply same terrain."}});
            Check(rows.Count>0,"Loaded terrain palette captured");
        }
        // Developer sidecar only: late ground paint, without flattening or replacing
        // native water/rock/roads/structures. Not yet connected to product state/UI.
        sealed class GroundPass : GenStep
        {
            readonly SimpleJsonObject layer;readonly string id;
            public GroundPass(SimpleJsonObject layer,string id){this.layer=layer;this.id=id;}
            public override int SeedPart=>2739472;
            public override void Generate(Map map,GenStepParams parms)
            {
                int width=(int)layer.GetFloat("width"),height=(int)layer.GetFloat("height");
                Check(width>0 && height>0 && layer.GetString("row_order")=="south-first","Ground coordinate system valid: "+id);
                var classes=layer.GetObjectArray("materials");var plane=new int[width*height];
                int end=0;
                foreach(var run in layer.GetObjectArray("runs")) {
                    int start=(int)run.GetFloat("start"),length=(int)run.GetFloat("length"),material=(int)run.GetFloat("material");
                    if(start<end || length<=0 || start+length>plane.Length || material<=0 || material>classes.Count)throw new Exception("Invalid ground RLE: "+id);
                    for(int n=start;n<start+length;n++)plane[n]=material;
                    end=start+length;
                }
                Check(true,"Ground runs and material indices validated: "+id);
                bool same=layer.GetString("source_biome")==map.Biome.defName;
                bool dry=map.Biome.defName=="Desert" || map.Biome.defName=="ExtremeDesert";
                var before=map.AllCells.Select(c=>map.terrainGrid.TerrainAt(c)).ToArray();
                var elevation=map.AllCells.Select(c=>MapGenerator.Elevation[c]).ToArray();
                var blocked=new bool[map.cellIndices.NumGridCells];
                foreach(var c in map.AllCells) {
                    int n=map.cellIndices.CellToIndex(c);var t=before[n];
                    blocked[n]=t.IsWater || t.IsRiver || t.dangerous || t.HasTag("Road") || !TerrainMaterials.Supported(t)
                        || c.GetEdifice(map)!=null || MapGenerator.Elevation[c]>=.7f;
                }
                int eligible=0,changed=0,protectedCells=0,missing=0,adapted=0;
                var protectionKinds=new Dictionary<string,int>{{"water",0},{"road",0},{"constructed_floor",0},{"edifice",0},{"high_elevation",0}};
                var counts=new Dictionary<string,int>();var resolutions=new List<object>();
                var resolved=new TerrainDef[classes.Count];
                for(int i=0;i<classes.Count;i++) {
                    string name=classes[i].GetString("def"),role=classes[i].GetString("role"),target=name,reason="same-biome exact material";
                    if(!same) {
                        reason="compatible natural material";
                        if(role=="base" || role=="rock-ground") {target=null;reason="preserve target native base ground";}
                        if(role=="ice") {target=null;reason="preserve target climate; no imported ice sheet";}
                        if(dry && role=="fertile") {target=null;reason="preserve dry target; no implicit oasis";}
                        if(dry && (role=="mud" || role=="marsh")) {target="Sand";reason="dry biome shore uses sand";}
                    }
                    var def=target==null?null:DefDatabase<TerrainDef>.GetNamedSilentFail(target);
                    if(def!=null && (!TerrainMaterials.Supported(def) || def.IsWater || def.IsRiver || def.dangerous))def=null;
                    resolved[i]=def;
                    resolutions.Add(new Dictionary<string,object>{{"source",name},{"role",role},{"target",def?.defName},{"reason",target!=null && def==null?"missing or unsupported loaded TerrainDef; preserved":reason}});
                }
                foreach(var c in map.AllCells) {
                    int sx=Math.Min(width-1,c.x*width/map.Size.x),sz=Math.Min(height-1,c.z*height/map.Size.z);
                    int m=plane[sz*width+sx];if(m==0)continue;
                    int n=map.cellIndices.CellToIndex(c);if(blocked[n]){
                        protectedCells++;var t=before[n];
                        if(t.IsWater || t.IsRiver)protectionKinds["water"]++;
                        if(t.HasTag("Road"))protectionKinds["road"]++;
                        if(t.designationCategory!=null)protectionKinds["constructed_floor"]++;
                        if(c.GetEdifice(map)!=null)protectionKinds["edifice"]++;
                        if(elevation[n]>=.7f)protectionKinds["high_elevation"]++;
                        continue;
                    }
                    eligible++;var def=resolved[m-1];if(def==null){missing++;continue;}
                    string role=classes[m-1].GetString("role");
                    // Wet ground is kept near actual water, including on arid tiles.
                    // Distant wet patches from another biome stay native.
                    if(!same && (role=="mud" || role=="marsh") && !NearWater(map,c)){missing++;continue;}
                    if(def.defName!=classes[m-1].GetString("def"))adapted++;
                    if(before[n]!=def){map.terrainGrid.SetTerrain(c,def);changed++;}
                    if(!counts.ContainsKey(def.defName))counts[def.defName]=0;counts[def.defName]++;
                }
                int protectedChanged=0,elevationChanged=0,unmapped=0,unmappedChanged=0;
                foreach(var c in map.AllCells) {
                    int n=map.cellIndices.CellToIndex(c);
                    if(blocked[n] && map.terrainGrid.TerrainAt(c)!=before[n])protectedChanged++;
                    if(MapGenerator.Elevation[c]!=elevation[n])elevationChanged++;
                    int m=plane[Math.Min(height-1,c.z*height/map.Size.z)*width+Math.Min(width-1,c.x*width/map.Size.x)];
                    if(m==0){unmapped++;if(map.terrainGrid.TerrainAt(c)!=before[n])unmappedChanged++;}
                }
                Check(protectedChanged==0 && elevationChanged==0 && unmappedChanged==0,"Ground pass preserves protected/unmapped terrain and elevations: "+id);
                if(id=="ground-guard")Check(protectionKinds.Values.All(n=>n>0),"Real water, road, floor, edifice and rock fixtures protected");
                Save(id+"-ground-application.json",new Dictionary<string,object>{{"source_biome",layer.GetString("source_biome")},{"target_biome",map.Biome.defName},{"same_biome",same},
                    {"eligible_cells",eligible},{"changed_cells",changed},{"protected_cells",protectedCells},{"preserved_unresolved_cells",missing},{"adapted_cells",adapted},
                    {"protected_changes",protectedChanged},{"elevation_changes",elevationChanged},{"applied_counts",counts},{"resolutions",resolutions},
                    {"protection_kinds",protectionKinds},{"unmapped_cells",unmapped},{"unmapped_changes",unmappedChanged},
                    {"stage",405},{"scope","Developer sidecar; ground only, no world/tile settings or RNG calls"}});
            }
            static bool NearWater(Map map,IntVec3 c)
            {
                for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++) {
                    var at=c+new IntVec3(x,0,z);if(at.InBounds(map) && map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(at)).IsWater)return true;
                }
                return false;
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
                var names=new List<string>();var indices=new int[Size*Size];var surfaces=new int[Size*Size];
                foreach(var c in map.AllCells) {
                    var terrain=map.terrainGrid.TerrainAt(c);string name=terrain.defName;
                    fertility[c.z*Size+c.x]=name=="SoilRich"?'R':name=="Soil"?'F':'N';
                    if(!terrainCounts.ContainsKey(name))terrainCounts[name]=0;terrainCounts[name]++;
                    string permanent=map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)).defName;
                    if(!names.Contains(permanent))names.Add(permanent);indices[c.z*Size+c.x]=names.IndexOf(permanent);
                    if(!names.Contains(name))names.Add(name);surfaces[c.z*Size+c.x]=names.IndexOf(name);
                }
                Save(Id+"-terrain.json",new Dictionary<string,object>{{"schema_version",2},{"biome",map.Biome.defName},{"width",Size},{"height",Size},{"cells",new string(labels)},{"fertile_cells",new string(fertility)},{"terrain_defs",terrainCounts},
                    {"terrain_table",names},{"terrain_indices",indices},{"surface_indices",surfaces},{"row_order","south-first"},{"note","Per-cell permanent TerrainDef and visible surface are separate; temporary ice is never imported as water from a color alone."}});
                var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,Target,new IntVec2(Size,Size));
                var result=new MapPreview.MapPreviewResult(request);
                var gt=typeof(MapPreview.MapPreviewGenerator);
                var step=(GenStep)Activator.CreateInstance(gt.GetNestedType("PreviewTextureGenStep",BindingFlags.NonPublic),new object[]{result,true});
                step.Generate(map,parms); gt.GetMethod("AddBevelToSolidStone",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{result});
                var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
                try {texture.SetPixels(result.Pixels);texture.Apply();File.WriteAllBytes(Path.Combine(output,Id+"-map.png"),ImageConversion.EncodeToPNG(texture));}
                finally {UnityEngine.Object.Destroy(texture);}
                var defaultResult=new MapPreview.MapPreviewResult(request);
                var defaultStep=(GenStep)Activator.CreateInstance(gt.GetNestedType("PreviewTextureGenStep",BindingFlags.NonPublic),new object[]{defaultResult,false});
                defaultStep.Generate(map,parms);gt.GetMethod("AddBevelToSolidStone",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{defaultResult});
                var defaultTexture=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
                try {defaultTexture.SetPixels(defaultResult.Pixels);defaultTexture.Apply();File.WriteAllBytes(Path.Combine(output,Id+"-map-default.png"),ImageConversion.EncodeToPNG(defaultTexture));}
                finally {UnityEngine.Object.Destroy(defaultTexture);}
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
