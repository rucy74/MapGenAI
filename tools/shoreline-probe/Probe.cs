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

namespace MapGenAI.ShorelineProbe
{
    [StaticConstructorOnStartup]
    public static class Probe
    {
        const string WorldSeed="mapgenai-guided-live-20260923";
        static string output,fixture,biomeChoice,details,phase="preview";
        static bool booting,active,finished,bypass;
        static int target,blockedProviders,blendCalls;
        static TileMapState state;
        static RecommendationPreviews preview;
        static DateTime deadline;
        static Snapshot before;
        static readonly List<object> checks=new List<object>();
        static readonly Dictionary<string,object> results=new Dictionary<string,object>();
        static readonly HashSet<string> OrdinaryWater=new HashSet<string>{"WaterDeep","WaterShallow","WaterMovingChestDeep","WaterMovingShallow"};
        static Probe()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAIShoreProbe",out output))return;
            if(!GenCommandLine.TryGetCommandLineArg("savedatafolder",out string profile)||!File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))throw new Exception("Disposable profile marker required");
            GenCommandLine.TryGetCommandLineArg("mapgenAIShoreCase",out fixture);
            GenCommandLine.TryGetCommandLineArg("mapgenAIShoreBiome",out biomeChoice);
            GenCommandLine.TryGetCommandLineArg("mapgenAIShoreDetails",out details);
            bypass=GenCommandLine.TryGetCommandLineArg("mapgenAIShoreBypass",out string value)&&value=="true";
            var harmony=new Harmony("choco.mapgenai.shoreline-audit");
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(Probe),nameof(Seed)));
            harmony.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoSimulation)));
            harmony.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoProvider)));
            harmony.Patch(AccessTools.Method(typeof(LandscapeBlendGeneration),"Apply"),prefix:new HarmonyMethod(typeof(Probe),nameof(BeforeBlend)),postfix:new HarmonyMethod(typeof(Probe),nameof(AfterBlend)));
            harmony.Patch(AccessTools.Method(typeof(PassageGeneration),"Check"),postfix:new HarmonyMethod(typeof(Probe),nameof(FinalMeasure)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString)=>seedString=WorldSeed;
        static bool NoSimulation()=>false;
        static bool NoProvider(){blockedProviders++;throw new InvalidOperationException("Provider calls forbidden in shoreline audit");}
        static bool Ours(Map map)=>active&&GenerationContext.Active&&GenerationContext.TileId==target&&map.Size.x==250;
        static void Check(bool ok,string name)=>checks.Add(new {ok,name});
        static void Start()
        {
            try
            {
                Directory.CreateDirectory(output);Application.runInBackground=true;Prefs.RunInBackground=true;booting=true;
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
                    var candidates=Find.WorldGrid.Tiles.Where(t=>!Find.WorldObjects.AnyMapParentAt(t.tile)&&FeaturePolicy.WaterNeighbors(t).Count==0&&!FeaturePolicy.HasRiver(t)&&t.hilliness==Hilliness.Flat&&t.Mutators.Count==0&&(!(t is SurfaceTile s)||s.Roads==null||s.Roads.Count==0)).ToList();
                    Tile tile;
                    if(biomeChoice=="cold")tile=candidates.FirstOrDefault(t=>t.PrimaryBiome.defName=="Tundra"&&t.temperature<=0)??candidates.First(t=>t.PrimaryBiome.defName=="IceSheet"&&t.temperature<=0);
                    else tile=candidates.First(t=>t.PrimaryBiome.defName==(biomeChoice=="desert"?"Desert":"TemperateForest"));
                    target=tile.tile;Find.WorldSelector.SelectedTile=target;Find.World.info.initialMapSize=new IntVec3(250,1,250);Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Find.TickManager.DebugSetTicksGame(0);Find.TickManager.gameStartAbsTick=3600000;
                    state=CreateState();MapStateValidation.Validate(state);MapGenParams.RestoreSnapshot(state,target);
                    File.WriteAllText(Path.Combine(output,"state.json"),MapStateCodec.Serialize(state));
                    var biome=tile.PrimaryBiome;
                    var patchThresholdDefs=biome.terrainPatchMakers?.Where(p=>p?.thresholds!=null).SelectMany(p=>p.thresholds).Where(t=>t?.terrain!=null).Select(t=>t.terrain.defName).Distinct().OrderBy(name=>name).ToArray()??Array.Empty<string>();
                    Save("fixture.json",new {fixture,biomeChoice,details,bypass,tile=target,worldSeed=WorldSeed,setupRandSeed=902323,mapSize=250,biome=biome.defName,temperature=tile.temperature,rainfall=tile.rainfall,biomeWildPlantsCareAboutLocalFertility=biome.wildPlantsCareAboutLocalFertility,biomeHasOrdinarySoil=biome.terrainsByFertility.Any(t=>t.terrain==TerrainDefOf.Soil),biomeNativePatchMud=patchThresholdDefs.Contains("Mud"),biomeTerrainPatchThresholdDefs=patchThresholdDefs,poolVariant=state.elevationShapes.First(s=>s.id=="pond").variant,ticksGame=Find.TickManager.TicksGame,ticksAbs=Find.TickManager.TicksAbs,note="pool/hotspring have only a water composite, no authored dry floor. protected adds explicit coverage, road and recorded protection controls."});
                    ValidateDetector();
                    var parameters=new Dictionary<string,object>{{"elevation_shapes",ShapeEdits.Describe(state.elevationShapes)},{"ruin_density",0},{"danger_density",0}};
                    if(state.localRoads.Count>0)parameters["road_ops"]=state.localRoads.Select(r=>new Dictionary<string,object>{{"op","add"},{"road",r}}).ToArray();
                    var command=SimpleJson.Serialize(new Dictionary<string,object>{{"action","generate"},{"params",parameters}});
                    File.WriteAllText(Path.Combine(output,"preview-command.json"),command);
                    var plan=(RecommendationPlan)Activator.CreateInstance(typeof(RecommendationPlan),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{command,"Controlled shoreline audit"},null);
                    active=true;deadline=DateTime.UtcNow.AddMinutes(6);preview=new RecommendationPreviews(target,new[]{plan},new TileMapState());
                }catch(Exception e){Finish(e);}
            });
        }
        static TileMapState CreateState()
        {
            var result=new TileMapState{ruinDensity=0,dangerDensity=0};
            result.elevationShapes.Add(new ElevationShape{id="pond",type="composite",details=details,variant="739",edge_roughness="medium",
                compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="water",prim="ellipse",center=new[]{.5f,.5f},w=.32f,h=.20f}},
                compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="water",e=.05f,f=.008f,fill=fixture=="hotspring"?"HotSpring":"WaterShallow"}}});
            if(fixture=="protected")
            {
                result.elevationShapes.Add(new ElevationShape{id="reserved_floor",type="composite",details="none",
                    compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="floor",prim="rect",center=new[]{.69f,.5f},w=.22f,h=.30f}},
                    compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="floor",e=.05f,f=.008f}}});
                result.elevationShapes.Add(new ElevationShape{id="soil70",type="region_fill",region="reserved_floor",region_part="inside",coverage="0.7",fill="rich_soil"});
                result.localRoads.Add(new RoadPlan{id="shore_road",kind="DirtPath",route="avoid",seed=47,points=new[]{new[]{.20f,.63f},new[]{.83f,.63f}}});
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
                phase="full";before=null;MapGenParams.RestoreSnapshot(state,target);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
                Save("full-complete-terrain.json",Snapshot.Read(map,false).Summary());
                var report=AuthoringGeneration.Latest(target,state);
                Check(report!=null&&report.issues.Count==0,"Full map has no authoring issue");
                if(fixture=="protected")
                {
                    Check(report!=null&&report.coverage.Count==1&&report.coverage[0].eligible>0&&Math.Abs(report.coverage[0].selected/(double)report.coverage[0].eligible-.7)<.001,"Actual 70 percent fill applied");
                    Check(report!=null&&report.roads.Count==1&&report.roads[0].footprint.Count>0,"Actual local road placed");
                }
                results["fullReport"]=report==null?null:new {report.issues,report.coverage,report.roads,report.surfaceBlendCells};
                Check(details=="none"?blendCalls==0:blendCalls==2,details=="none"?
                    "Explicit off schedules no blend stage in either generator":"Both preview and full-map blend stages observed");
                Check(blockedProviders==0,"No provider factory calls");Finish(null);
            }catch(Exception e){Finish(e);}
        }
        static bool BeforeBlend(Map map)
        {
            if(!Ours(map))return true;
            blendCalls++;
            if(fixture=="protected")SeedProtectionControls(map);
            before=Snapshot.Read(map);
            return !bypass;
        }
        static void AfterBlend(Map map)
        {
            if(!Ours(map)||before==null)return;
            var after=Snapshot.Read(map);var regions=GenerationContext.Regions(map);var waterDistance=DistancesWithinSix(before.w,before.h,before.ordinaryWater);
            var coverage=fixture=="protected"?regions.Mask("reserved_floor"):new bool[before.surface.Length];
            var rows=new List<object>();var violations=new List<object>();
            int eligibleShore=0,protectedShore=0,roadCount=0,foundationCount=0,coverageCount=0;
            for(int i=0;i<before.surface.Length;i++)
            {
                bool ordinary=before.surface[i]=="Soil"||before.surface[i]=="Sand";
                bool protectedCell=before.water[i]||before.rock[i]||!ordinary||before.foundation[i]||before.roof[i]||before.occupied[i]||before.road[i]||coverage[i]||regions.Materials[i]!=null;
                bool near=waterDistance[i]>=0&&waterDistance[i]<=6;
                if(near&&!before.water[i]){if(protectedCell)protectedShore++;else eligibleShore++;}
                if(before.road[i])roadCount++;if(before.foundation[i])foundationCount++;if(coverage[i])coverageCount++;
                var problem=Problems(before.surface[i],after.surface[i],before.layers[i],after.layers[i],before.elevation[i],after.elevation[i],before.caves[i],after.caves[i],protectedCell,waterDistance[i],biomeChoice=="desert"||biomeChoice=="cold",details=="none"||bypass||fixture=="hotspring");
                if(before.layers[i]!=after.layers[i])rows.Add(new {x=i%before.w,z=i/before.w,from=before.surface[i],to=after.surface[i],waterDistance=waterDistance[i],protectedCell,water=before.water[i],rock=before.rock[i],road=before.road[i],foundation=before.foundation[i],coverage=coverage[i],ordinary});
                if(problem!=null)violations.Add(new {x=i%before.w,z=i/before.w,problem});
            }
            Check(violations.Count==0,phase+": shoreline stays within six cells and preserves protected terrain, layers and heights");
            Check(before.surface.Any(s=>s==(fixture=="hotspring"?"HotSpring":"WaterShallow")),phase+": requested water material actually exists");
            if(details=="natural"&&!bypass&&fixture=="pool"&&biomeChoice=="temperate")
            {
                Check(eligibleShore>0&&rows.Count>0,phase+": water-only natural composite changes its eligible external dry shore");
                Check(Enumerable.Range(0,before.surface.Length).Any(i=>before.surface[i]=="Soil"&&after.surface[i]=="Mud"),
                    phase+": fixed warm wet soil fixture produces some mud at the actual feathered water edge");
            }
            if(fixture=="protected")
                Check(roadCount>0&&foundationCount>0&&coverageCount>0,phase+": road, foundation and whole fill source protection are exercised");
            Save(phase+"-before-blend.json",before.Summary());Save(phase+"-after-blend.json",after.Summary());
            Save(phase+"-changes.json",new {changed=rows.Count,eligibleShore,protectedShore,roadCount,foundationCount,coverageCount,rows,violations});
            results[phase+"Blend"]=new {changed=rows.Count,eligibleShore,protectedShore,roadCount,foundationCount,coverageCount,violations=violations.Count,beforeTerrainHash=before.TerrainHash,afterTerrainHash=after.TerrainHash};
        }
        static string Problems(string original,string current,string oldLayers,string newLayers,float oldHeight,float newHeight,float oldCave,float newCave,bool locked,double distance,bool dryOrCold,bool inactive)
        {
            if(oldHeight!=newHeight||oldCave!=newCave)return "height-or-caves-changed";
            if(oldLayers==newLayers)return null;
            if(inactive)return "inactive-or-special-water-changed";
            if(locked)return "protected-cell-changed";
            if(distance<0||distance>6)return "outside-six-cell-shore";
            if(oldLayers.Split('|').Skip(2).SequenceEqual(newLayers.Split('|').Skip(2))==false)return "under-foundation-or-temp-changed";
            if(original!="Soil"&&original!="Sand")return "special-base-changed";
            if(current=="Soil"&&original!="Soil")return "new-soil-created";
            if(dryOrCold&&(current=="Mud"||current=="Soil")&&current!=original)return "dry-or-frozen-soil-mud-created";
            if(current!="Soil"&&current!="Sand"&&current!="Gravel"&&current!="Mud")return "unexpected-shore-material";
            return null;
        }
        static void SeedProtectionControls(Map map)
        {
            var raw=Snapshot.Read(map);var distance=DistancesWithinSix(raw.w,raw.h,raw.ordinaryWater);var regions=GenerationContext.Regions(map);var coverage=regions.Mask("reserved_floor");
            var dry=Enumerable.Range(0,raw.surface.Length).Where(i=>distance[i]>=1&&distance[i]<=6&&(raw.surface[i]=="Soil"||raw.surface[i]=="Sand")&&!raw.rock[i]&&!raw.road[i]&&!raw.foundation[i]&&!raw.roof[i]&&!raw.occupied[i]&&!coverage[i]&&regions.Materials[i]==null).Take(2).ToArray();
            if(dry.Length!=2)throw new InvalidOperationException("No dry shore for explicit protection controls");
            map.terrainGrid.SetTerrain(new IntVec3(dry[0]%raw.w,0,dry[0]/raw.w),TerrainDefOf.Gravel);
            map.terrainGrid.SetTerrain(new IntVec3(dry[1]%raw.w,0,dry[1]/raw.w),DefDatabase<TerrainDef>.GetNamed("SoilRich"));
            int bridgeIndex=Enumerable.Range(0,raw.surface.Length).First(i=>raw.surface[i]=="WaterShallow"&&!raw.foundation[i]&&!raw.occupied[i]&&!raw.road[i]);
            var bridgeCell=new IntVec3(bridgeIndex%raw.w,0,bridgeIndex/raw.w);var bridge=DefDatabase<TerrainDef>.GetNamed("Bridge");
            if(AuthoringGeneration.Current?.preview==true)
                ((TerrainDef[])AccessTools.Field(typeof(TerrainGrid),"foundationGrid").GetValue(map.terrainGrid))[map.cellIndices.CellToIndex(bridgeCell)]=bridge;
            else map.terrainGrid.SetFoundation(bridgeCell,bridge);
            Check(map.terrainGrid.FoundationAt(bridgeCell)==bridge,phase+": explicit existing bridge foundation control exists");
            Save(phase+"-injected-controls.json",new {gravel=new[]{dry[0]%raw.w,dry[0]/raw.w},richSoil=new[]{dry[1]%raw.w,dry[1]/raw.w},bridge=new[]{bridgeCell.x,bridgeCell.z},note="Harness-injected protection controls before the measured stage, not generated content or a product feature. Preview uses its supported direct foundation storage, full map calls native SetFoundation."});
        }
        static void FinalMeasure(Map map){if(Ours(map))Save(phase+"-phase-end-terrain.json",Snapshot.Read(map).Summary());}
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
        // Independent bounded brute-force Euclidean distances; no product placement/blend helpers.
        static double[] DistancesWithinSix(int width,int height,bool[] water)
        {
            var distance=Enumerable.Repeat(-1d,width*height).ToArray();
            for(int z=0;z<height;z++)for(int x=0;x<width;x++)
            {
                int best=37;
                for(int dz=-6;dz<=6;dz++)for(int dx=-6;dx<=6;dx++)
                {
                    int squared=dx*dx+dz*dz,nx=x+dx,nz=z+dz;
                    if(squared>=best||nx<0||nx>=width||nz<0||nz>=height||!water[nz*width+nx])continue;
                    best=squared;
                }
                if(best<=36)distance[z*width+x]=Math.Sqrt(best);
            }
            return distance;
        }
        static void ValidateDetector()
        {
            var mask=new bool[81];mask[2*9+2]=true;var distance=DistancesWithinSix(9,9,mask);
            Check(distance[5*9+6]==5&&distance[8*9+8]<0,"Independent distance detects 3-4-5 and outside-six controls");
            Check(Problems("Soil","Sand","Soil|Soil|||","Sand|Sand|||",.2f,.2f,0,0,false,3,false,false)==null,"Detector permits an eligible shore change");
            Check(Problems("WaterShallow","Sand","WaterShallow|WaterShallow|||","Sand|Sand|||",0,0,0,0,true,0,false,false)=="protected-cell-changed","Detector catches protected-water mutation");
            Check(Problems("Soil","Mud","Soil|Soil|||","Mud|Mud|||",0,0,0,0,false,3,true,false)=="dry-or-frozen-soil-mud-created","Detector catches dry/frozen mud mutation");
            Check(Problems("Soil","Sand","Soil|Soil|||","Sand|Sand|||",0,0,0,0,false,-1,false,false)=="outside-six-cell-shore","Detector catches distant change");
            Check(Problems("Soil","Sand","Soil|Soil|||","Sand|Sand|||",0,0,0,0,false,3,false,true)=="inactive-or-special-water-changed","Detector catches bypass/off change");
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
            Save("result.json",new {ok,fixture,biomeChoice,details,bypass,checks,results,error=error?.ToString(),blendCalls,blockedProviderFactoryCalls=blockedProviders,note="Actual MapPreview and full-map surface generation; no provider calls or plant/visual-quality verdict. Bypass skips only LandscapeBlendGeneration.Apply. Injected protection controls are recorded separately."});
            if(error!=null)Log.Error("[ShorelineProbe] "+error);Application.Quit();
        }
    }
    public sealed class ProbeComponent:GameComponent
    {
        public ProbeComponent(Game game){}
        public override void StartedNewGame()=>Probe.Started();
        public override void GameComponentUpdate()=>Probe.Tick();
    }
}
