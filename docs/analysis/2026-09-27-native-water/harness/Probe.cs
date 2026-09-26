using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.NativeVisualProbe
{
    [StaticConstructorOnStartup]
    public static class Probe
    {
        const string WorldSeed="mapgenai-guided-live-20260923";
        static string output,fixture,biomeChoice,details,phase="preview";
        static string interactionLayout="single";
        static bool booting,active,finished,bypass;
        static int target,blockedProviders,blendCalls;
        static TileMapState state;
        static RecommendationPreviews preview;
        static DateTime deadline;
        static Snapshot before;
        static string statePath,waterProfile,waterFill,tileContext;
        static int nativeLakeCalls;
        static bool fieldChecks,integrationChecks;
        static Dictionary<int,string> nativeRiverLayers;

        static readonly List<object> checks=new List<object>();
        static readonly Dictionary<string,object> results=new Dictionary<string,object>();
        static readonly HashSet<string> OrdinaryWater=new HashSet<string>{"WaterDeep","WaterShallow","WaterMovingChestDeep","WaterMovingShallow"};
        static Probe()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeProbe",out output))return;
            if(!GenCommandLine.TryGetCommandLineArg("savedatafolder",out string profile)||!File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))throw new Exception("Disposable profile marker required");
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeCase",out fixture);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeBiome",out biomeChoice);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeDetails",out details);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeState",out statePath);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeWaterProfile",out waterProfile);
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeWaterFill",out waterFill))waterFill="WaterShallow";
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeTileContext",out tileContext))tileContext="inland";
            fieldChecks=GenCommandLine.TryGetCommandLineArg("mapgenAINativeFieldChecks",out string fieldFlag)&&fieldFlag=="true";
            integrationChecks=GenCommandLine.TryGetCommandLineArg("mapgenAINativeIntegrationChecks",out string integrationFlag)&&integrationFlag=="true";
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeLayout",out interactionLayout))interactionLayout="single";
            if(interactionLayout!="single"&&interactionLayout!="cardinal")throw new ArgumentException("Unknown interaction layout");
            bypass=GenCommandLine.TryGetCommandLineArg("mapgenAINativeBypass",out string value)&&value=="true";
            var harmony=new Harmony("choco.mapgenai.native-visual-audit");
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(Probe),nameof(Seed)));
            harmony.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoSimulation)));
            harmony.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoProvider)));
            harmony.Patch(AccessTools.Method(typeof(LandscapeBlendGeneration),"Apply"),prefix:new HarmonyMethod(typeof(Probe),nameof(BeforeBlend)),postfix:new HarmonyMethod(typeof(Probe),nameof(AfterBlend)));
            harmony.Patch(AccessTools.Method(typeof(PassageGeneration),"Check"),postfix:new HarmonyMethod(typeof(Probe),nameof(FinalMeasure)));
            harmony.Patch(AccessTools.Method(typeof(GenStep_Terrain),"Generate"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeTerrain)));
            harmony.Patch(AccessTools.Method(typeof(TileMutatorWorker_Lake),"GeneratePostTerrain"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeLake)));
            harmony.Patch(AccessTools.Method(typeof(TileMutatorWorker_River),"GeneratePostTerrain"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeRiver)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString)=>seedString=WorldSeed;
        static bool NoSimulation()=>false;
        static bool NoProvider(){blockedProviders++;throw new InvalidOperationException("Provider calls forbidden in shoreline audit");}
        static bool Ours(Map map)=>active&&map.Tile.tileId==target&&map.Size.x==250;
        static void Check(bool ok,string name)=>checks.Add(new {ok,name});
        static void Start()
        {
            try
            {
                Directory.CreateDirectory(output);RecordHarmony();Application.runInBackground=true;Prefs.RunInBackground=true;booting=true;
                LongEventHandler.QueueLongEvent(()=>{
                    try{Rand.Seed=902323;Root_Play.SetupForQuickTestPlay();Find.GameInitData.mapSize=100;Find.GameInitData.PrepForMapGen();Find.Scenario.PreMapGenerate();}
                    catch(Exception e){Finish(e);}
                },"Play","Isolated shoreline audit",true,null);
            }catch(Exception e){Finish(e);}
        }
        public static void Started()
        {
            if(!booting)return;booting=false;
            LongEventHandler.ExecuteWhenFinished(()=>{
                try
                {
                    var candidates=Find.WorldGrid.Tiles.Where(t=>!Find.WorldObjects.AnyMapParentAt(t.tile)&&FeaturePolicy.WaterNeighbors(t).Count==0&&FeaturePolicy.HasRiver(t)==(tileContext=="river")&&t.hilliness==Hilliness.Flat&&(t.Mutators.Count==0||tileContext=="river"&&t.Mutators.All(m=>m.defName=="River"))&&(!(t is SurfaceTile s)||s.Roads==null||s.Roads.Count==0)).ToList();
                    Tile tile;
                    if(biomeChoice=="cold")tile=candidates.FirstOrDefault(t=>t.PrimaryBiome.defName=="Tundra"&&t.temperature<=0)??candidates.First(t=>t.PrimaryBiome.defName=="IceSheet"&&t.temperature<=0);
                    else tile=candidates.First(t=>t.PrimaryBiome.defName==(biomeChoice=="desert"?"Desert":biomeChoice=="boreal"?"BorealForest":biomeChoice=="arid"?"AridShrubland":"TemperateForest"));
                    if(biomeChoice=="desert")tile=candidates.First(t=>t.PrimaryBiome.defName=="Desert"&&DefDatabase<TileMutatorDef>.GetNamed("Oasis").averageTemperatureRange.Includes(t.temperature));
                    target=tile.tile;Find.WorldSelector.SelectedTile=target;Find.World.info.initialMapSize=new IntVec3(250,1,250);Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Find.TickManager.DebugSetTicksGame(0);Find.TickManager.gameStartAbsTick=3600000;
                    state=CreateState();if(tileContext=="river")state.hasRiver=true;MapStateValidation.Validate(state);MapGenParams.RestoreSnapshot(state,target);
                    File.WriteAllText(Path.Combine(output,"state.json"),MapStateCodec.Serialize(state));
                    var biome=tile.PrimaryBiome;
                    var patchThresholdDefs=biome.terrainPatchMakers?.Where(p=>p?.thresholds!=null).SelectMany(p=>p.thresholds).Where(t=>t?.terrain!=null).Select(t=>t.terrain.defName).Distinct().OrderBy(name=>name).ToArray()??Array.Empty<string>();
                    Save("fixture.json",new {fixture,biomeChoice,details,bypass,tileContext,tile=target,worldSeed=WorldSeed,setupRandSeed=902323,mapSize=250,biome=biome.defName,temperature=tile.temperature,rainfall=tile.rainfall,biomeWildPlantsCareAboutLocalFertility=biome.wildPlantsCareAboutLocalFertility,biomeHasOrdinarySoil=biome.terrainsByFertility.Any(t=>t.terrain==TerrainDefOf.Soil),biomeNativePatchMud=patchThresholdDefs.Contains("Mud"),biomeTerrainPatchThresholdDefs=patchThresholdDefs,poolVariant=state.elevationShapes.FirstOrDefault(s=>s.id=="pond")?.variant,waterProfile,nativeFeature=state.mutators.FirstOrDefault(),groundLakeBeach=biome.lakeBeachTerrain?.defName,groundRiverbank=biome.riverbankTerrain?.defName,groundMud=biome.mudTerrain?.defName,ticksGame=Find.TickManager.TicksGame,ticksAbs=Find.TickManager.TicksAbs,note="pool/hotspring have only a water composite, no authored dry floor. protected adds explicit coverage, road and recorded protection controls."});

                    var parameters=new Dictionary<string,object>{{"elevation_shapes",ShapeEdits.Describe(state.elevationShapes)},{"ruin_density",state.ruinDensity},{"danger_density",state.dangerDensity}};
                    if(state.localRoads.Count>0)parameters["road_ops"]=state.localRoads.Select(r=>new Dictionary<string,object>{{"op","add"},{"road",r}}).ToArray();
                    var replayBaseline=state.Clone();replayBaseline.elevationShapes.Clear();replayBaseline.localRoads.Clear();
                    var command=SimpleJson.Serialize(new Dictionary<string,object>{{"action","generate"},{"params",parameters}});
                    File.WriteAllText(Path.Combine(output,"preview-command.json"),command);
                    var plan=(RecommendationPlan)Activator.CreateInstance(typeof(RecommendationPlan),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{command,"Controlled shoreline audit"},null);
                    Check(MapStateCodec.Serialize(plan.Resolve(replayBaseline))==MapStateCodec.Serialize(state),"Preview command resolves to exact recorded full state");
                    active=true;deadline=DateTime.UtcNow.AddMinutes(6);preview=new RecommendationPreviews(target,new[]{plan},replayBaseline);
                }catch(Exception e){Finish(e);}
            });
        }
        static TileMapState CreateState()
        {
            if(!string.IsNullOrEmpty(statePath))return MapStateCodec.Deserialize(File.ReadAllText(statePath));
            var result=new TileMapState{ruinDensity=0,dangerDensity=0};
            if(fixture=="native-ground")return result;
            if(fixture=="native-feature"){result.mutators.Add(biomeChoice=="desert"?"Oasis":"Lake");return result;}
            result.elevationShapes.Add(new ElevationShape{id="pond",type="composite",details=details,variant="739",edge_roughness=fixture=="exact"?null:"medium",
                compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="water",prim="ellipse",center=new[]{.5f,.5f},w=.32f,h=.20f}},
                compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="water",e=.05f,f=.008f,fill=fixture=="hotspring"?"HotSpring":waterFill}}});
            if(fixture=="protected")
            {
                result.elevationShapes.Add(new ElevationShape{id="reserved_floor",type="composite",details="none",
                    compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="floor",prim="rect",center=new[]{.69f,.5f},w=.22f,h=.30f}},
                    compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="floor",e=.05f,f=.008f}}});
                result.elevationShapes.Add(new ElevationShape{id="soil70",type="region_fill",region="reserved_floor",region_part="inside",coverage="0.7",fill="rich_soil"});
                result.localRoads.Add(new RoadPlan{id="shore_road",kind="DirtPath",route="avoid",seed=47,points=new[]{new[]{.20f,.63f},new[]{.83f,.63f}}});
            }
            if(!string.IsNullOrEmpty(waterProfile))
            {
                var field=AccessTools.Field(typeof(ElevationShape),"water_profile");
                if(field==null)throw new InvalidOperationException("This product DLL does not have water_profile");
                field.SetValue(result.elevationShapes.First(s=>s.id=="pond"),waterProfile);
            }
            return result;
        }
        public static void Tick()
        {
            if(!active||finished)return;
            try
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException("Shoreline preview timeout");
                preview.Update();var item=preview.Items[0];if(!item.Complete)return;
                Check(item.Texture!=null&&item.Error==null,"MapPreview completes");Check(item.Rejection==null,"MapPreview has no rejected placement");
                if(item.Texture!=null)File.WriteAllBytes(Path.Combine(output,"preview.png"),item.Texture.EncodeToPNG());
                results["previewStatus"]=new {item.Error,item.Rejection,item.Warning,item.Seconds};preview.Dispose();preview=null;
                phase="full";before=null;nativeRiverLayers=null;MapGenParams.RestoreSnapshot(state,target);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
                Save("full-complete-terrain.json",Snapshot.Read(map,false).Summary());CompareNativeRiver(map,false,"full-complete");
                var report=AuthoringGeneration.Latest(target,state);
                Check(report==null||report.issues.Count==0,"Full map has no authoring issue");
                if(fixture=="protected")
                {
                    Check(report!=null&&report.coverage.Count==1&&report.coverage[0].eligible>0&&Math.Abs(report.coverage[0].selected/(double)report.coverage[0].eligible-.7)<.001,"Actual 70 percent fill applied");
                    Check(report!=null&&report.roads.Count==1&&report.roads[0].footprint.Count>0,"Actual local road placed");
                }
                results["fullReport"]=report==null?null:new {report.issues,report.coverage,report.roads,report.surfaceBlendCells};
                if(fixture=="native-feature")Check(nativeLakeCalls>=2,"Native Lake worker observed in preview and full map for native feature fixture");
                Save("full-actual-features.json",Find.WorldGrid[map.Tile].Mutators.Select(m=>new{m.defName,worker=m.Worker.GetType().FullName}).ToArray());
                Save("full-native-palette.json",new {center=map.Center.ToString(),deep=MapGenUtility.DeepFreshWaterTerrainAt(map.Center,map).defName,shallow=MapGenUtility.ShallowFreshWaterTerrainAt(map.Center,map).defName,lakeBeach=MapGenUtility.LakeshoreTerrainAt(map.Center,map).defName,riverbank=MapGenUtility.RiverbankTerrainAt(map.Center,map).defName,mud=MapGenUtility.MudTerrainAt(map.Center,map).defName});
                if(fieldChecks){var fieldResult=NativeWaterChecks.Run(map,output);if(fieldResult.HasValue)Check(fieldResult.Value,"100 unfiltered variants for each of six native field fixtures");}
                if(integrationChecks){var integrationResult=NativeIntegrationChecks.Run(map,output);if(integrationResult.HasValue)Check(integrationResult.Value,"Native Scribe state and helper palette restoration checks");}
                Check(blockedProviders==0,"No provider factory calls");Finish(null);
            }catch(Exception e){Finish(e);}
        }
        static bool BeforeBlend(Map map)
        {
            if(!Ours(map))return true;blendCalls++;before=Snapshot.Read(map);return !bypass;
        }
        static void AfterBlend(Map map)
        {
            if(!Ours(map)||before==null)return;
            Save(phase+"-before-blend.json",before.Summary());Save(phase+"-after-blend.json",Snapshot.Read(map).Summary());
        }
        static void FinalMeasure(Map map){if(Ours(map)){Save(phase+"-phase-end-terrain.json",Snapshot.Read(map).Summary());CompareNativeRiver(map,true,phase+"-phase-end");}}
        static void NativeTerrain(Map map){if(Ours(map))Save(phase+"-native-terrain.json",Snapshot.Read(map).Summary());}
        static void NativeLake(Map map){if(Ours(map)){nativeLakeCalls++;Save(phase+"-native-lake.json",Snapshot.Read(map).Summary());}}
        static void NativeRiver(Map map)
        {
            if(!Ours(map)||tileContext!="river")return;
            var snapshot=Snapshot.Read(map);
            nativeRiverLayers=map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).IsRiver).ToDictionary(c=>c.z*map.Size.x+c.x,c=>snapshot.layers[c.z*map.Size.x+c.x]);
            Check(nativeRiverLayers.Count>0,phase+": actual native river worker produced river terrain");
            Save(phase+"-native-river.json",new {count=nativeRiverLayers.Count,width=map.Size.x,height=map.Size.z,cells=nativeRiverLayers.Select(p=>new{index=p.Key,layers=p.Value}).ToArray(),note="Observed immediately after actual TileMutatorWorker_River.GeneratePostTerrain; not injected water."});
        }
        static void CompareNativeRiver(Map map,bool grids,string name)
        {
            if(tileContext!="river")return;
            var snapshot=Snapshot.Read(map,grids);
            var changed=nativeRiverLayers==null?Array.Empty<object>():nativeRiverLayers.Where(p=>snapshot.layers[p.Key]!=p.Value).Select(p=>(object)new{index=p.Key,before=p.Value,after=snapshot.layers[p.Key]}).ToArray();
            Check(nativeRiverLayers!=null&&nativeRiverLayers.Count>0&&changed.Length==0,name+": native river terrain layers preserved");
            Save(name+"-river-preservation.json",new{observed=nativeRiverLayers?.Count??0,changed=changed.Length,rows=changed});
        }
        static void RecordHarmony()
        {
            var specifications=new[]{
                "RimWorld.GenStep_ElevationFertility:Generate","RimWorld.GenStep_Terrain:Generate",
                "RimWorld.MapGenUtility:TerrainFrom","RimWorld.MapGenUtility:DeepFreshWaterTerrainAt",
                "RimWorld.MapGenUtility:ShallowFreshWaterTerrainAt","RimWorld.MapGenUtility:LakeshoreTerrainAt",
                "RimWorld.MapGenUtility:RiverbankTerrainAt","RimWorld.MapGenUtility:MudTerrainAt",
                "RimWorld.TileMutatorWorker_Lake:Init","RimWorld.TileMutatorWorker_Lake:GeneratePostElevationFertility",
                "RimWorld.TileMutatorWorker_Lake:GeneratePostTerrain","RimWorld.TileMutatorWorker_Oasis:GeneratePostTerrain",
                "RimWorld.TileMutatorWorker_River:GeneratePostTerrain",
                "RimWorld.GenStep_Plants:Generate"};
            var rows=new List<object>();
            foreach(var spec in specifications)
            {
                var parts=spec.Split(':');var method=AccessTools.Method(AccessTools.TypeByName(parts[0]),parts[1]);
                if(method!=null)method=AccessTools.Method(method.DeclaringType,method.Name,method.GetParameters().Select(p=>p.ParameterType).ToArray());
                var info=method==null?null:Harmony.GetPatchInfo(method);
                object[] Describe(IEnumerable<Patch> patches)=>patches?.Select(p=>(object)new{owner=p.owner,priority=p.priority,method=p.PatchMethod.DeclaringType.FullName+"."+p.PatchMethod.Name}).ToArray()??Array.Empty<object>();
                rows.Add(new{requested=spec,found=method!=null,resolved=method==null?null:method.DeclaringType.FullName+"."+method.Name,
                    prefixes=Describe(info?.Prefixes),postfixes=Describe(info?.Postfixes),transpilers=Describe(info?.Transpilers),finalizers=Describe(info?.Finalizers)});
            }
            Save("harmony-patches.json",rows);
        }
        sealed class Snapshot
        {
            public int w,h;public string[] surface,layers;public float[] elevation,caves;public bool[] water,ordinaryWater,rock,road,foundation,roof,occupied;
            public string TerrainHash=>Hash(string.Join("\n",layers));
            public static Snapshot Read(Map map,bool generatorGridsAvailable=true)
            {
                int count=map.Size.x*map.Size.z;var s=new Snapshot{w=map.Size.x,h=map.Size.z,surface=new string[count],layers=new string[count],elevation=generatorGridsAvailable?new float[count]:null,caves=generatorGridsAvailable?new float[count]:null,water=new bool[count],ordinaryWater=new bool[count],rock=new bool[count],road=new bool[count],foundation=new bool[count],roof=new bool[count],occupied=new bool[count]};
                var regions=GenerationContext.Active?GenerationContext.Regions(map):null;
                foreach(var c in map.AllCells)
                {
                    int i=c.z*s.w+c.x;var terrain=map.terrainGrid.TerrainAt(c);var edifice=c.GetEdifice(map);s.surface[i]=terrain.defName;
                    s.layers[i]=string.Join("|",new[]{terrain.defName,map.terrainGrid.TopTerrainAt(c)?.defName,map.terrainGrid.UnderTerrainAt(c)?.defName,map.terrainGrid.FoundationAt(c)?.defName,map.terrainGrid.TempTerrainAt(c)?.defName});
                    if(generatorGridsAvailable){s.elevation[i]=MapGenerator.Elevation[c];s.caves[i]=MapGenerator.Caves[c];}s.water[i]=terrain.IsWater;
                    s.ordinaryWater[i]=OrdinaryWater.Contains(terrain.defName)&&!terrain.dangerous;
                    s.rock[i]=edifice?.def.building.isNaturalRock==true||generatorGridsAvailable&&s.elevation[i]>=.7f&&s.caves[i]<=0;
                    s.road[i]=terrain.HasTag("Road")||regions?.LocalRoadCells[i]==true;s.foundation[i]=map.terrainGrid.FoundationAt(c)!=null;s.roof[i]=map.roofGrid.RoofAt(c)!=null;
                    s.occupied[i]=c.GetThingList(map).Any(t=>t is Building||t is Pawn||t.def.category==ThingCategory.Item);
                }
                return s;
            }
            public object Summary()=>new {width=w,height=h,terrainHash=TerrainHash,generatorGridsAvailable=elevation!=null,elevationHash=elevation==null?null:HashFloats(elevation),cavesHash=caves==null?null:HashFloats(caves),groundCounts=surface.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count()),protectedCounts=new {water=water.Count(x=>x),rock=rock.Count(x=>x),road=road.Count(x=>x),foundation=foundation.Count(x=>x),roof=roof.Count(x=>x),occupied=occupied.Count(x=>x)},layerColumns="surface|top|under|foundation|temp",layers=Rle(layers)};
        }
        static object[] Rle(string[] values)
        {
            var rows=new List<object>();int start=0;while(start<values.Length){int end=start+1;while(end<values.Length&&values[end]==values[start])end++;rows.Add(new object[]{end-start,values[start]});start=end;}return rows.ToArray();
        }
        static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        static string HashFloats(float[] values){var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static void Save(string name,object value){using(var writer=new StreamWriter(Path.Combine(output,name)))WriteJson(writer,value);}
        static void WriteJson(TextWriter writer,object value)
        {
            if(value==null||value is string||value.GetType().IsPrimitive||value is decimal){writer.Write(SimpleJson.Serialize(value));return;}
            if(value is IDictionary dictionary){writer.Write('{');bool first=true;foreach(DictionaryEntry item in dictionary){if(!first)writer.Write(',');first=false;writer.Write(SimpleJson.Serialize(item.Key.ToString()));writer.Write(':');WriteJson(writer,item.Value);}writer.Write('}');return;}
            if(value is IEnumerable sequence){writer.Write('[');bool first=true;foreach(var item in sequence){if(!first)writer.Write(',');first=false;WriteJson(writer,item);}writer.Write(']');return;}
            var fields=new Dictionary<string,object>();foreach(var field in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))fields[field.Name]=field.GetValue(value);
            foreach(var property in value.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))if(property.GetIndexParameters().Length==0)fields[property.Name]=property.GetValue(value);WriteJson(writer,fields);
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;active=false;preview?.Dispose();preview=null;
            bool ok=error==null&&checks.All(c=>(bool)c.GetType().GetProperty("ok").GetValue(c));
            Save("result.json",new {ok,fixture,biomeChoice,details,bypass,checks,results,error=error?.ToString(),blendCalls,nativeLakeCalls,blockedProviderFactoryCalls=blockedProviders,note="Actual MapPreview PNG and full-map terrain. Native-feature uses a legal Lake in temperate or Oasis in desert. Feature geometry is native and differs from authored footprint. No provider or aesthetics verdict; not a screenshot of game UI. Native-ground still loads MapGenAI with empty authored shapes and ruin/danger density zero."});
            if(error!=null)Log.Error("[NativeVisualProbe] "+error);Application.Quit();
        }
    }
    public sealed class ProbeComponent:GameComponent
    {
        public ProbeComponent(Game game){}
        public override void StartedNewGame()=>Probe.Started();
        public override void GameComponentUpdate()=>Probe.Tick();
    }
}
