using System;
using System.Collections.Generic;
using System.Collections;
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

namespace MapGenAI.LandscapeBlendProbe
{
    // Deliberately references only APIs present before landscape blending. New fields/types use reflection.
    [StaticConstructorOnStartup]
    public static class Probe
    {
        const string WorldSeed="mapgenai-guided-live-20260923";
        static string output,fixture,details,phase="preview";static bool booting,active,finished,quietStructures;
        static int target,blockedProviders;static DateTime deadline;static TileMapState state;
        static RecommendationPreviews preview;static Snapshot beforeBlend;
        static readonly List<object> checks=new List<object>();
        static readonly Dictionary<string,object> results=new Dictionary<string,object>();
        static readonly MethodInfo weight=AccessTools.Method(typeof(TileMapState).Assembly.GetType("MapGenAI.Patches.LandscapeVegetationScope"),"Weight");
        static readonly FieldInfo weights=typeof(MapGenAI.MapGen.RegionGrid).GetField("VegetationWeights");
        static readonly FieldInfo detailField=typeof(ElevationShape).GetField("details");
        static void Save(string name,object value){using(var writer=new StreamWriter(Path.Combine(output,name)))WriteJson(writer,JsonValue(value));}
        // Stream collections; do not subject audit files to the product's intentional response-size cap.
        static void WriteJson(TextWriter writer,object value)
        {
            if(value is IDictionary dictionary)
            {
                writer.Write('{');bool first=true;foreach(DictionaryEntry entry in dictionary){if(!first)writer.Write(',');first=false;writer.Write(SimpleJson.Serialize(entry.Key.ToString()));writer.Write(':');WriteJson(writer,entry.Value);}writer.Write('}');
            }
            else if(value is IEnumerable sequence&&!(value is string))
            {
                writer.Write('[');bool first=true;foreach(var item in sequence){if(!first)writer.Write(',');first=false;WriteJson(writer,item);}writer.Write(']');
            }
            else writer.Write(SimpleJson.Serialize(value));
        }
        // Product serializer deliberately supports public fields only; audit rows use anonymous properties.
        static object JsonValue(object value)
        {
            if(value==null||value is string||value.GetType().IsPrimitive||value is decimal)return value;
            if(value is SimpleJsonObject)return value;
            if(value is IDictionary dictionary){var result=new Dictionary<string,object>();foreach(DictionaryEntry entry in dictionary)result[entry.Key.ToString()]=JsonValue(entry.Value);return result;}
            if(value is IEnumerable sequence){var result=new List<object>();foreach(var item in sequence)result.Add(JsonValue(item));return result;}
            var fields=new Dictionary<string,object>();foreach(var field in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))fields[field.Name]=JsonValue(field.GetValue(value));
            foreach(var property in value.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))if(property.GetIndexParameters().Length==0)fields[property.Name]=JsonValue(property.GetValue(value));return fields;
        }
        static void Check(bool ok,string name){checks.Add(new {ok,name});}
        static Probe()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAIBlendProbe",out output))return;
            if(!GenCommandLine.TryGetCommandLineArg("savedatafolder",out string profile)||!File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))throw new Exception("Disposable profile marker required");
            GenCommandLine.TryGetCommandLineArg("mapgenAIBlendCase",out fixture);
            GenCommandLine.TryGetCommandLineArg("mapgenAIBlendDetails",out details);
            quietStructures=GenCommandLine.TryGetCommandLineArg("mapgenAIBlendQuietStructures",out string quiet)&&quiet=="true";
            var h=new Harmony("choco.mapgenai.landscape-blend-audit");
            h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(Probe),nameof(Seed)));
            h.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoSimulation)));
            h.Patch(AccessTools.Method(typeof(LLMClientFactory),"Create"),prefix:new HarmonyMethod(typeof(Probe),nameof(NoProvider)));
            h.Patch(AccessTools.Method(typeof(PassageGeneration),"Check"),postfix:new HarmonyMethod(typeof(Probe),nameof(FinalMeasure)));
            var blend=typeof(TileMapState).Assembly.GetType("MapGenAI.MapGen.LandscapeBlendGeneration");
            if(blend!=null)h.Patch(AccessTools.Method(blend,"Apply"),prefix:new HarmonyMethod(typeof(Probe),nameof(BeforeBlend)),postfix:new HarmonyMethod(typeof(Probe),nameof(AfterBlend)));
            h.Patch(AccessTools.Method(typeof(GenStep_Plants),"Generate"),prefix:new HarmonyMethod(typeof(Probe),nameof(BeforePlants)){priority=Priority.Last},postfix:new HarmonyMethod(typeof(Probe),nameof(AfterPlants)));
            LongEventHandler.ExecuteWhenFinished(Start);
        }
        static void Seed(ref string seedString)=>seedString=WorldSeed;
        static bool NoSimulation()=>false;
        static bool NoProvider(){blockedProviders++;throw new InvalidOperationException("Provider calls are forbidden in this controlled native probe");}
        static bool Ours(Map map)=>active && GenerationContext.Active && GenerationContext.TileId==target && map.Size.x==250;
        static void Start()
        {
            try
            {
                Directory.CreateDirectory(output);Application.runInBackground=true;Prefs.RunInBackground=true;booting=true;
                LongEventHandler.QueueLongEvent(()=>{
                    try{Rand.Seed=902323;Root_Play.SetupForQuickTestPlay();Find.GameInitData.mapSize=100;Find.GameInitData.PrepForMapGen();Find.Scenario.PreMapGenerate();}
                    catch(Exception e){Finish(e);}
                },"Play","Isolated landscape terrain and vegetation audit",true,null);
            }catch(Exception e){Finish(e);}
        }
        public static void Started()
        {
            if(!booting)return;booting=false;
            LongEventHandler.ExecuteWhenFinished(()=>{
                try
                {
                    var candidates=Find.WorldGrid.Tiles.Where(t=>!Find.WorldObjects.AnyMapParentAt(t.tile)&&FeaturePolicy.WaterNeighbors(t).Count==0 && (!(t is SurfaceTile s)||s.Roads==null||s.Roads.Count==0));
                    Tile tile;
                    if(fixture=="B")tile=candidates.First(t=>t.PrimaryBiome.defName=="TemperateForest"&&FeaturePolicy.HasRiver(t)&&!t.Mutators.Any(m=>m.categories.Contains("Lake")));
                    else tile=candidates.First(t=>t.PrimaryBiome.defName==(fixture=="D"?"Desert":"TemperateForest")&&t.hilliness==Hilliness.Flat&&!FeaturePolicy.HasRiver(t)&&t.Mutators.Count==0);
                    target=tile.tile;Find.WorldSelector.SelectedTile=target;Find.World.info.initialMapSize=new IntVec3(250,1,250);Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    // World seed alone does not fix QuickTest starting season or player setup.
                    // This owned fixture explicitly fixes those inputs; older r3/r4 retain the uncontrolled evidence.
                    Find.TickManager.DebugSetTicksGame(0);Find.TickManager.gameStartAbsTick=3600000;
                    state=new TileMapState{hasRiver=FeaturePolicy.HasRiver(tile)};
                    if(quietStructures){state.ruinDensity=0;state.dangerDensity=0;}
                    string kind=fixture=="B"?"winding_valley":fixture=="D"||fixture=="protected"||fixture=="road"?"foothills":"open_basin";
                    var land=new ElevationShape{id="landscape",type="landform",landform=kind,layout="organic",variant="23",gap="0.28",direction=kind=="foothills"?"left":kind=="winding_valley"?"top":"bottom"};
                    if(detailField==null&&details!="null")throw new Exception("This DLL has no details field; baseline requires null mode");
                    detailField?.SetValue(land,details=="null"?null:details);state.elevationShapes.Add(land);
                    if(fixture=="A")state=Edit(state,@"{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""shallow_pond"",""type"":""composite"",""shapes"":[{""id"":""pool"",""prim"":""circle"",""center"":[0.58,0.5],""r"":0.075}],""compose"":[{""op"":""add"",""s"":""pool"",""e"":0,""fill"":""WaterShallow""}]}}]}");
                    // Explicit user-supported local feature addition, NOT an unreported world tile mutation.
                    if(fixture=="C")state.mutators.Add("HotSprings");
                    if(fixture=="protected")state=Edit(state,@"{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""soil70"",""type"":""region_fill"",""region"":""landscape"",""region_part"":""inside"",""coverage"":0.7,""fill"":""rich_soil""}}]}");
                    if(fixture=="protected"||fixture=="road")state.localRoads.Add(new RoadPlan{id="protected_road",kind="DirtPath",route="avoid",seed=47,points=new[]{new[]{.82f,.12f},new[]{.82f,.88f}}});
                    MapGenParams.RestoreSnapshot(state,target);
                    File.WriteAllText(Path.Combine(output,"state.json"),MapStateCodec.Serialize(state));
                    Save("fixture.json",new {fixture,details,quietStructures,tile=target,worldSeed=WorldSeed,setupRandSeed=902323,ticksGame=Find.TickManager.TicksGame,ticksAbs=Find.TickManager.TicksAbs,gameStartAbsTick=Find.TickManager.gameStartAbsTick,mapSize=250,biome=tile.PrimaryBiome.defName,hilliness=tile.hilliness.ToString(),meanTileTemperature=tile.temperature,nativeFeatures=tile.Mutators.Select(m=>m.defName).ToArray(),requestedFeatures=state.mutators,river=FeaturePolicy.HasRiver(tile),careAboutFertility=tile.PrimaryBiome.wildPlantsCareAboutLocalFertility,worldLinks=WorldLinks(tile),detailsFieldPresent=detailField!=null,blendHookPresent=typeof(TileMapState).Assembly.GetType("MapGenAI.MapGen.LandscapeBlendGeneration")!=null});
                    ValidateDetectors();VerifyScribe();
                    var parameters=new Dictionary<string,object>{{"elevation_shapes",ShapeEdits.Describe(state.elevationShapes)},{"river",new Dictionary<string,object>{{"present",state.hasRiver}}}};
                    if(state.mutators.Count>0)parameters["mutators"]=state.mutators;
                    if(quietStructures){parameters["ruin_density"]=0;parameters["danger_density"]=0;}
                    if(state.localRoads.Count>0)parameters["road_ops"]=state.localRoads.Select(r=>new Dictionary<string,object>{{"op","add"},{"road",r}}).ToArray();
                    var command=SimpleJson.Serialize(new Dictionary<string,object>{{"action","generate"},{"params",parameters}});
                    File.WriteAllText(Path.Combine(output,"preview-command.json"),command);
                    var plan=(RecommendationPlan)Activator.CreateInstance(typeof(RecommendationPlan),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{command,"Controlled landscape blend audit"},null);
                    active=true;deadline=DateTime.UtcNow.AddMinutes(5);preview=new RecommendationPreviews(target,new[]{plan},new TileMapState());
                }catch(Exception e){Finish(e);}
            });
        }
        static TileMapState Edit(TileMapState s,string json)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(json)));
        public sealed class Envelope:IExposable {public TileMapState state;public void ExposeData()=>Scribe_Deep.Look(ref state,"state");}
        static void VerifyScribe()
        {
            foreach(string mode in detailField==null?new[]{"null"}:new[]{"null","none","natural"})
            {
                var copy=state.Clone();detailField?.SetValue(copy.elevationShapes[0],mode=="null"?null:mode);
                string expected=MapStateCodec.Serialize(copy),path=Path.Combine(output,"scribe-"+mode+".xml");var envelope=new Envelope{state=copy};
                Scribe.saver.InitSaving(path,"LandscapeBlendAudit");Scribe_Deep.Look(ref envelope,"fixture");Scribe.saver.FinalizeSaving();envelope=null;
                Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref envelope,"fixture");Scribe.loader.FinalizeLoading();
                Check(MapStateCodec.Serialize(envelope.state)==expected,"Native Scribe preserves details "+mode+" and other authored state");
            }
        }
        static object WorldLinks(Tile tile)
        {
            var s=tile as SurfaceTile;return new {roads=s?.Roads?.Select(r=>new {neighbor=(int)r.neighbor,def=r.road.defName}).ToArray(),rivers=s?.Rivers?.Select(r=>new {neighbor=(int)r.neighbor,def=r.river.defName}).ToArray()};
        }
        public static void Tick()
        {
            if(!active||finished)return;
            try
            {
                if(DateTime.UtcNow>deadline)throw new TimeoutException("Native preview timeout");
                preview.Update();var item=preview.Items[0];if(!item.Complete)return;
                Check(item.Texture!=null&&item.Error==null,"MapPreview completed");Check(item.Rejection==null,"MapPreview has no placement rejection");
                if(item.Texture!=null)File.WriteAllBytes(Path.Combine(output,"preview.png"),item.Texture.EncodeToPNG());
                results["previewStatus"]=new {item.Error,item.Rejection,item.Warning,item.Seconds};preview.Dispose();preview=null;
                phase="full";beforeBlend=null;MapGenParams.RestoreSnapshot(state,target);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
                var report=AuthoringGeneration.Latest(target,state);Check(report!=null&&report.issues.Count==0,"Full map has no authoring failure");
                results["fullReport"]=Report(report);Save("full-plants-complete.json",PlantAudit(map,null,null));
                if(weight!=null)Check((float)weight.Invoke(null,new object[]{map,new IntVec3(125,0,125)})==1,"Vegetation weighting is inactive after generation");
                if(fixture=="protected")Check(report!=null&&report.coverage.Count==1&&Math.Abs(report.coverage[0].selected/(double)report.coverage[0].eligible-.7)<.001,"Explicit fill selects 70 percent of eligible cells");
                if(fixture=="protected"||fixture=="road")Check(report!=null&&report.roads.Count==1&&report.roads[0].footprint.Count>0,"Protected road fixture actually places a road");
                Check(blockedProviders==0,"No provider factory calls");Finish(null);
            }catch(Exception e){Finish(e);}
        }
        static object Report(AuthoringResult report)=>report==null?null:new {report.issues,report.coverage,report.roads,surfaceBlendCells=ReadInt(report,"surfaceBlendCells"),vegetationPlanCells=ReadInt(report,"vegetationPlanCells")};
        static int? ReadInt(object o,string field)=>(int?)o.GetType().GetField(field)?.GetValue(o);
        static void BeforeBlend(Map map){if(Ours(map))beforeBlend=Snapshot.Read(map);}
        static void AfterBlend(Map map)
        {
            if(!Ours(map)||beforeBlend==null)return;var after=Snapshot.Read(map);var region=GenerationContext.Regions(map);
            var floor=region.Mask("landscape");var fill=fixture=="protected"?region.Mask("landscape","inside"):new bool[floor.Length];
            var changes=new List<object>();int waterChanges=0,rockChanges=0,roadChanges=0,fillChanges=0,outsideChanges=0,specialChanges=0,heightChanges=0,gravelChanges=0,foundationChanges=0;
            for(int i=0;i<floor.Length;i++)
            {
                if(beforeBlend.elevation[i]!=after.elevation[i]||beforeBlend.caves[i]!=after.caves[i])heightChanges++;
                if(beforeBlend.layers[i]==after.layers[i])continue;
                changes.Add(new object[]{i%map.Size.x,i/map.Size.x,beforeBlend.surface[i],after.surface[i]});
                if(beforeBlend.water[i])waterChanges++;if(beforeBlend.rock[i])rockChanges++;
                if(beforeBlend.surface[i]=="Gravel")gravelChanges++;
                if(beforeBlend.layers[i].Split('|')[3]!="")foundationChanges++;
                if(region.LocalRoadCells[i])roadChanges++;if(fill[i])fillChanges++;if(!floor[i])outsideChanges++;
                if(!new[]{"Soil","Sand","Gravel"}.Contains(beforeBlend.surface[i]))specialChanges++;
            }
            Check(waterChanges+rockChanges+roadChanges+fillChanges+outsideChanges+specialChanges+heightChanges+gravelChanges+foundationChanges==0,phase+": blend preserves water, rocks, road, full fill area, special terrain, existing gravel/foundation, outer area and height");
            results[phase+"Blend"]=new {changed=changes.Count,waterChanges,rockChanges,roadChanges,fillChanges,outsideChanges,specialChanges,heightChanges,gravelChanges,foundationChanges,protectedWaterCells=beforeBlend.water.Count(x=>x),protectedRockCells=beforeBlend.rock.Count(x=>x),protectedGravelCells=beforeBlend.surface.Count(x=>x=="Gravel"),protectedFoundationCells=beforeBlend.layers.Count(x=>x.Split('|')[3]!=""),protectedRoadCells=region.LocalRoadCells.Count(x=>x),protectedFillCells=fill.Count(x=>x)};
            Save(phase+"-blend-changes.json",new {columns=new[]{"x","z","before","after"},changes});
            Save(phase+"-before-blend.json",beforeBlend.Export());
            Save(phase+"-after-blend.json",after.Export());
        }
        static void BeforePlants(Map map)
        {
            if(!Ours(map))return;var s=Snapshot.Read(map);Save(phase+"-before-plants.json",s.Export());
            var planned=weights?.GetValue(GenerationContext.Regions(map)) as float[];
            int activeWeights=0,outsideMismatch=0,desertMismatch=0;
            if(planned!=null&&weight!=null)foreach(var c in map.AllCells)
            {
                int i=c.z*map.Size.x+c.x;float actual=(float)weight.Invoke(null,new object[]{map,c});
                if(actual!=1)activeWeights++;
                if(actual!=(map.BiomeAt(c).wildPlantsCareAboutLocalFertility?planned[i]:1f))outsideMismatch++;
                if(!map.BiomeAt(c).wildPlantsCareAboutLocalFertility&&actual!=1)desertMismatch++;
            }
            Check(outsideMismatch==0&&desertMismatch==0,phase+": actual Plants900 weighting matches plan and preserves no-fertility biomes");
            results[phase+"PlantsScope"]=new {planned=planned!=null,activeWeights,outsideMismatch,desertMismatch,min=planned?.Min(),max=planned?.Max()};
            if(planned!=null&&activeWeights>0)
            {
                var samples=new List<object>();bool correct=true;
                foreach(var c in map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c)==TerrainDefOf.Soil&&map.fertilityGrid.FertilityAt(c)>0&&Math.Abs(planned[c.z*map.Size.x+c.x]-1)>.05f).Take(5))
                {
                    float w=planned[c.z*map.Size.x+c.x],fertility=map.fertilityGrid.FertilityAt(c),density=map.BiomeAt(c).plantDensity,baseCount=map.wildPlantSpawner.GetBaseDesiredPlantsCountAt(c);
                    float actual=map.wildPlantSpawner.GetDesiredPlantsCountAt(c,.25f),expected=Math.Min(baseCount*density*.25f*fertility*w,1),unpatched=Math.Min(baseCount*density*.25f*fertility,1);
                    bool ok=Math.Abs(actual-expected)<.00001f&&Math.Abs(actual-unpatched)>.00001f;correct&=ok;
                    samples.Add(new {x=c.x,z=c.z,weight=w,fertility,density,baseCount,actual,expected,unpatched,ok});
                }
                Check(samples.Count>0&&correct,phase+": actual native GetDesiredPlantsCountAt consumes density weight");
                results[phase+"NativeDensitySamples"]=samples;
            }
        }
        static void AfterPlants(Map map){if(Ours(map))Save(phase+"-plants900.json",PlantAudit(map,null,null));}
        static void FinalMeasure(Map map)
        {
            if(!Ours(map))return;var s=Snapshot.Read(map);var regions=GenerationContext.Regions(map);
            var floor=regions.Mask("landscape");var waterDist=DistanceSquared(map.Size.x,map.Size.z,s.sourceWater);var rockDist=DistanceSquared(map.Size.x,map.Size.z,s.rock);
            var plantAudit=PlantAudit(map,waterDist,rockDist);Save(phase+"-plants.json",plantAudit);
            Save(phase+"-terrain.json",s.Export());
            Save(phase+"-floor-mask.json",Enumerable.Range(0,floor.Length).Where(i=>floor[i]).ToArray());
            var planned=weights?.GetValue(regions) as float[];
            Save(phase+"-spatial.json",SpatialRows(map,s,floor,waterDist,rockDist,planned));
            Save(phase+"-vegetation-plan.json",new {columns=new[]{"x","z","terrain","weight","waterDistance","rockDistance"},rows=Enumerable.Range(0,floor.Length).Where(i=>floor[i]&&!s.water[i]&&!s.rock[i]).Select(i=>new object[]{i%map.Size.x,i/map.Size.x,s.surface[i],planned==null?1f:planned[i],FiniteDistance(waterDist[i]),FiniteDistance(rockDist[i])}).ToArray()});
            var report=AuthoringGeneration.Current;
            results[phase+"Final"]=new {water=s.water.Count(x=>x),sourceWaterIncludingIce=s.sourceWater.Count(x=>x),rocks=s.rock.Count(x=>x),floor=floor.Count(x=>x),plants=map.listerThings.AllThings.OfType<Plant>().Count(),outdoorTemperature=map.mapTemperature.OutdoorTemp,ticksAbs=Find.TickManager.TicksAbs,report=Report(report),worldLinks=WorldLinks(map.Tile.Tile)};
            Check(floor.Count(x=>x)>1000,phase+": landform floor is present");
            if(fixture=="A")Check(s.surface.Count(t=>t=="WaterShallow")>100,phase+": explicit shallow pond is present");
            if(fixture=="B")Check(s.layers.Any(t=>t.Split('|').Any(layer=>layer.StartsWith("WaterMoving"))),phase+": native river is present including water below temporary ice");
            if(fixture=="C")Check(s.surface.Any(t=>t=="HotSpring"),phase+": native HotSprings feature is present");
            if(fixture=="D"&&planned!=null)Check(planned.All(p=>p==1),phase+": desert has no vegetation density weighting");
        }
        // Full coordinates and species, not inference from MapPreview colors. Viability is audited, not replaced.
        static object PlantAudit(Map map,double[] waterDist,double[] rockDist)
        {
            var rows=new List<object>();var unexpected=new List<object>();var lowFertility=new List<object>();
            foreach(var p in map.listerThings.AllThings.OfType<Plant>().OrderBy(p=>p.Position.z).ThenBy(p=>p.Position.x).ThenBy(p=>p.def.defName))
            {
                var c=p.Position;int i=c.z*map.Size.x+c.x;var biome=map.BiomeAt(c);var t=map.terrainGrid.TerrainAt(c);
                bool native=biome.AllWildPlants.Contains(p.def)||p.def.plant.cavePlant;
                var row=new object[]{c.x,c.z,p.def.defName,p.Growth,t.defName,biome.defName,map.fertilityGrid.FertilityAt(c),p.def.plant.fertilityMin,native,waterDist==null?null:(object)FiniteDistance(waterDist[i]),rockDist==null?null:(object)FiniteDistance(rockDist[i])};rows.Add(row);
                if(!native)unexpected.Add(row);
                if(map.fertilityGrid.FertilityAt(c)+.0001f<p.def.plant.fertilityMin)lowFertility.Add(row);
            }
            var identity=rows.Select(r=>((object[])r).Take(4).ToArray()).ToArray();
            return new {columns=new[]{"x","z","def","growth","terrain","biome","fertility","fertilityMin","nativeBiomeSpeciesOrCave","waterDistance","rockDistance"},count=rows.Count,identityHash=Hash(SimpleJson.Serialize(identity)),rows,unexpectedSpecies=unexpected,belowNativeFertilityMinimum=lowFertility};
        }
        static object SpatialRows(Map map,Snapshot s,bool[] floor,double[] waterDist,double[] rockDist,float[] planned)
        {
            var groups=new Dictionary<string,List<int>>();var plants=new HashSet<int>(map.listerThings.AllThings.OfType<Plant>().Select(p=>p.Position.z*map.Size.x+p.Position.x));
            for(int i=0;i<floor.Length;i++)if(floor[i]&&!s.water[i]&&!s.rock[i])
            {
                string key="water:"+Bin(waterDist[i])+"/rock:"+Bin(rockDist[i]);if(!groups.TryGetValue(key,out var ids))groups[key]=ids=new List<int>();ids.Add(i);
            }
            return groups.OrderBy(g=>g.Key).Select(g=>new {bin=g.Key,cells=g.Value.Count,plants=g.Value.Count(plants.Contains),meanWeight=g.Value.Average(i=>planned==null?1d:planned[i]),terrain=g.Value.GroupBy(i=>s.surface[i]).ToDictionary(x=>x.Key,x=>x.Count())}).ToArray();
        }
        static string Bin(double square)=>square<=9?"0-3":square<=81?"3-9":square<=400?"9-20":"20+";
        static double FiniteDistance(double square)=>square>=1e12?-1:Math.Sqrt(square);
        sealed class Snapshot
        {
            public int w,h;public string[] surface,layers;public float[] elevation,caves;public bool[] water,sourceWater,rock;
            public static Snapshot Read(Map map)
            {
                int count=map.Size.x*map.Size.z;var s=new Snapshot{w=map.Size.x,h=map.Size.z,surface=new string[count],layers=new string[count],elevation=new float[count],caves=new float[count],water=new bool[count],sourceWater=new bool[count],rock=new bool[count]};
                foreach(var c in map.AllCells)
                {
                    int i=c.z*s.w+c.x;var t=map.terrainGrid.TerrainAt(c);s.surface[i]=t.defName;
                    s.layers[i]=string.Join("|",new[]{t.defName,map.terrainGrid.TopTerrainAt(c)?.defName,map.terrainGrid.UnderTerrainAt(c)?.defName,map.terrainGrid.FoundationAt(c)?.defName,map.terrainGrid.TempTerrainAt(c)?.defName});
                    s.elevation[i]=MapGenerator.Elevation[c];s.caves[i]=MapGenerator.Caves[c];s.water[i]=t.IsWater;
                    s.sourceWater[i]=s.water[i]||map.terrainGrid.TopTerrainAt(c)?.IsWater==true||map.terrainGrid.UnderTerrainAt(c)?.IsWater==true;
                    s.rock[i]=c.GetEdifice(map)?.def.building.isNaturalRock==true||s.elevation[i]>=.7f&&s.caves[i]<=0;
                }return s;
            }
            public object Export()=>new {width=w,height=h,layerColumns="surface|top|under|foundation|temp",layers=Rle(layers),terrainHash=Hash(string.Join("\n",layers)),elevationHash=HashFloats(elevation),cavesHash=HashFloats(caves),groundCounts=surface.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count())};
        }
        static object[] Rle(string[] values)
        {
            var result=new List<object>();int start=0;while(start<values.Length){int end=start+1;while(end<values.Length&&values[end]==values[start])end++;result.Add(new object[]{end-start,values[start]});start=end;}return result.ToArray();
        }
        static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        static string HashFloats(float[] values){var bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        // Independent exact squared Euclidean distance: separable lower envelope of parabolas.
        // No calls to product SpatialDistance or blending noise/threshold helpers.
        static double[] DistanceSquared(int w,int h,bool[] seeds)
        {
            const double inf=1e12;var tmp=new double[w*h];var result=new double[w*h];
            for(int z=0;z<h;z++){var row=new double[w];for(int x=0;x<w;x++)row[x]=seeds[z*w+x]?0:inf;var d=Distance1D(row);for(int x=0;x<w;x++)tmp[z*w+x]=d[x];}
            for(int x=0;x<w;x++){var col=new double[h];for(int z=0;z<h;z++)col[z]=tmp[z*w+x];var d=Distance1D(col);for(int z=0;z<h;z++)result[z*w+x]=d[z];}return result;
        }
        static double[] Distance1D(double[] f)
        {
            int n=f.Length,k=0;var v=new int[n];var cuts=new double[n+1];cuts[0]=double.NegativeInfinity;cuts[1]=double.PositiveInfinity;
            for(int q=1;q<n;q++)
            {
                double cross;do{int p=v[k];cross=((f[q]+q*q)-(f[p]+p*p))/(2d*(q-p));if(cross<=cuts[k])k--;else break;}while(k>=0);
                k++;v[k]=q;cuts[k]=cross;cuts[k+1]=double.PositiveInfinity;
            }
            k=0;var result=new double[n];for(int q=0;q<n;q++){while(cuts[k+1]<q)k++;int d=q-v[k];result[q]=d*d+f[v[k]];}return result;
        }
        static void ValidateDetectors()
        {
            var seeds=new bool[49];seeds[3*7+2]=true;var d=DistanceSquared(7,7,seeds);Check(d[6*7+6]==25&&d[3*7+2]==0,"Independent Euclidean distance positive control (3-4-5 triangle)");
            var a=new[]{"Soil|Soil|||","WaterShallow|WaterShallow|||"};var b=(string[])a.Clone();b[1]="Gravel|Gravel|||";
            Check(Hash(string.Join("\n",a))!=Hash(string.Join("\n",b)),"Terrain comparison catches a synthetic protected-water mutation");
        }
        static void Finish(Exception error)
        {
            if(finished)return;finished=true;active=false;preview?.Dispose();preview=null;
            bool ok=error==null&&checks.All(c=>(bool)c.GetType().GetProperty("ok").GetValue(c));
            Save("result.json",new {ok,fixture,details,checks,results,error=error?.ToString(),blockedProviderFactoryCalls=blockedProviders,newProviderCalls=0,note="Native controlled same-world fixture. Plants are measured in actual full maps separately from MapPreview terrain. This does not prove all biomes or visual preference."});
            if(error!=null)Log.Error("[LandscapeBlendProbe] "+error);Application.Quit();
        }
    }
    public sealed class ProbeComponent:GameComponent
    {
        public ProbeComponent(Game game){}
        public override void StartedNewGame()=>Probe.Started();
        public override void GameComponentUpdate()=>Probe.Tick();
    }
}
