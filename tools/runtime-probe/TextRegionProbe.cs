using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.Fixtures;
using MapGenAI.MapGen;
using MapGenAI.ImageInput;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class TextRegionProbe
    {
        static readonly List<object> checks=new List<object>();
        static bool ok=true;
        static bool previewPending;
        static DateTime previewDeadline;
        static string previewOutput;
        static int mapCount;
        public static void Tick()
        {
            if(previewPending && DateTime.UtcNow>previewDeadline){Check(false,"actual Map Preview timeout");Complete();}
        }
        static void Complete()
        {
            previewPending=false;
            string json=SimpleJson.Serialize(new Dictionary<string,object>{{"ok",ok},{"maps",mapCount},{"checks",checks}});
            File.WriteAllText(Path.Combine(previewOutput,"text-region-result.json"),json);
            File.WriteAllText(Path.Combine(previewOutput,"result.json"),json);
            UnityEngine.Application.Quit();
        }
        static void Check(bool pass,string name){checks.Add(new Dictionary<string,object>{{"pass",pass},{"name",name}});ok &= pass;Log.Message("[TextRegionProbe] "+pass+" "+name);}
        static object Invoke(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        static TerrainDef Terrain(Map m,IntVec3 c)=>m.terrainGrid.TerrainAtIgnoreTemp(m.cellIndices.CellToIndex(c));
        static void Patch(int tile,string json)=>MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse(json)),tile);
        public static void Run(string output,Action<bool,string> require)
        {
            File.WriteAllText(Path.Combine(output,"world-seed.txt"),Find.World.info.seedString);
            var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 &&
                !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).ToList();
            if(tiles.Count<12)throw new Exception("Need twelve unmodified flat temperate fixtures");
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIPreviewOnly",out _)){StartPreview(output,tiles[0].tile);return;}
            int target=tiles[0].tile;Find.WorldSelector.SelectedTile=target;
            var water=TextRegionFixtures.Island("water",false);MapGenParams.RestoreSnapshot(water,target);
            var state=MapGenParams.CaptureState(target);var before=MapStateCodec.Serialize(state);
            var dialog=new Dialog_TextToMap();
            bool applied=(bool)Invoke(dialog,"ApplyImageMap",new ImageMapData{width=1,height=1,cells="W"});
            Check(!applied && before==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),"paused actual dialog image application rejects without state changes");
            bool blocked=false;try{new Dialog_ImageMap(null,0,_=>true);}catch(InvalidOperationException){blocked=true;}
            Check(blocked,"paused image dialog cannot start import or API work");dialog.PostClose();
            var scribe=new StructureFixture{state=TextRegionFixtures.Island()};scribe.state.imageMap=new ImageMapData{width=2,height=1,cells="MW",replaceElevation=true};
            string path=Path.Combine(output,"structures-scribe.xml");Scribe.saver.InitSaving(path,"TextRegionFixture");Scribe_Deep.Look(ref scribe,"fixture");Scribe.saver.FinalizeSaving();
            var json=MapStateCodec.Serialize(scribe.state);scribe=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref scribe,"fixture");Scribe.loader.FinalizeLoading();
            Check(json==MapStateCodec.Serialize(scribe.state),"real Scribe preserves structures, CSG and dormant image data");
            var old=new System.Xml.XmlDocument();old.Load(path);foreach(System.Xml.XmlNode node in old.SelectNodes("//structures"))node.ParentNode.RemoveChild(node);
            string oldPath=Path.Combine(output,"structures-legacy.xml");old.Save(oldPath);scribe=null;Scribe.loader.InitLoading(oldPath);Scribe_Deep.Look(ref scribe,"fixture");Scribe.loader.FinalizeLoading();
            Check(scribe.state.structures.Count==0 && scribe.state.imageMap.cells=="MW","old Scribe without structures remains loadable and retains images");
            File.WriteAllText(Path.Combine(output,"material-catalog.txt"),TerrainMaterials.Catalog());
            foreach(string name in new[]{"lava","cooled_lava","volcanic_rock","gravel"})Check(TerrainMaterials.Resolve(name)!=null,"active native material "+name);
            var lavaDef=TerrainMaterials.Resolve("lava");Check(lavaDef.heatPerTick>0 && lavaDef.burnDamage>0 && lavaDef.passability==Traversability.Impassable,"lava uses native heat, burn damage and impassable terrain properties");
            foreach(string name in new[]{"LavaShallow","UnknownPlanetMatter","WaterOceanDeep"})
            {bool rejected=false;try{TerrainMaterials.Resolve(name);}catch(FormatException){rejected=true;}Check(rejected,"unsupported material rejects: "+name);}

            var promptCases=new Dictionary<string,TileMapState>{
                {"lava-fill",water},{"ruin-island",TextRegionFixtures.Island("lava",false)},
                {"move-island",TextRegionFixtures.Island()},{"ruin-count",TextRegionFixtures.Island()},
                {"remove-area",TextRegionFixtures.Island()},{"material-volcanic",water},
                {"unknown-material",water},{"ancient-danger-position",water},{"image-paused",water},{"lava-from-scratch",new TileMapState()}};
            var replayStates=new Dictionary<string,TileMapState>();
            foreach(var entry in promptCases)
            {
                MapGenParams.RestoreSnapshot(entry.Value,target);Find.WorldSelector.SelectedTile=target;
                string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{target});
                File.WriteAllText(Path.Combine(output,entry.Key+"-prompt.txt"),prompt);
                File.WriteAllText(Path.Combine(output,entry.Key+"-before.json"),MapStateCodec.Serialize(MapGenParams.CaptureState(target)));
                if(GenCommandLine.TryGetCommandLineArg("mapgenAITextResponses",out var responses))
                {
                    string response=File.ReadAllText(Path.Combine(responses,entry.Key+"-response.json"));
                    var cmd=MapGenAI.LLM.ProviderResponse.Command(response);dialog=new Dialog_TextToMap();before=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                    var undo=(System.Collections.ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                    Invoke(dialog,"HandleResponse",response);
                    if(cmd.GetString("action")=="generate")
                    {
                        Check(undo.Count==1 && before!=MapStateCodec.Serialize(MapGenParams.CaptureState(target)),"actual model dialog apply "+entry.Key);
                        replayStates[entry.Key]=MapGenParams.CaptureState(target);Invoke(dialog,"DoUndo");
                    }
                    else Check(cmd.GetString("action")=="ask" && undo.Count==0,"actual model explanation "+entry.Key);
                    Check(before==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),"model Undo/ask preserves entire state "+entry.Key);dialog.PostClose();
                }
            }
            var cases=new Dictionary<string,TileMapState>();
            cases["lava-island"]=TextRegionFixtures.Island();
            var moved=MapStateEditor.Merge(cases["lava-island"],MapParameterParser.Parse(SimpleJson.Parse(@"{""shape_ops"":[{""op"":""move"",""id"":""island"",""position"":[0.68,0.65]}]}")));cases["moved-island"]=moved;
            var converted=water.Clone();converted.elevationShapes[0].fill="lava";cases["water-to-lava"]=converted;
            var natural=TextRegionFixtures.Island();natural.elevationShapes[0].edge_roughness="medium";cases["natural-lava"]=natural;
            var materials=TextRegionFixtures.Island("water",false);materials.elevationShapes.Clear();
            materials.elevationShapes.Add(TextRegionFixtures.Circle("cooled","cooled_lava",.25f,.75f,.13f));materials.elevationShapes.Add(TextRegionFixtures.Circle("volcanic","volcanic_rock",.75f,.75f,.13f));
            materials.elevationShapes.Add(new ElevationShape{id="gravel",type="bump",fill="gravel",position="bottom_left",size="large"});
            materials.elevationShapes.Add(new ElevationShape{id="lava_ring",type="ring",fill="lava",position="bottom_right",size="small"});cases["materials"]=materials;
            var impossible=TextRegionFixtures.Island();impossible.elevationShapes.Add(TextRegionFixtures.Circle("tiny","soil",.1f,.1f,.012f));
            impossible.structures.Add(new StructurePlan{id="cannot_fit",region="tiny",width=11,height=9});cases["capacity-failure"]=impossible;
            var paused=TextRegionFixtures.Island();paused.imageMap=new ImageMapData{width=2,height=2,cells="WWWW",replaceElevation=true};cases["image-paused"]=paused;
            var northeast=TextRegionFixtures.Island("water",false);northeast.elevationShapes.Clear();
            northeast.structures.Add(new StructurePlan{id="northeast",position=new[]{.78f,.78f},bounds=new[]{.65f,.65f,.92f,.92f},width=13,height=9,count=3});cases["northeast-position"]=northeast;
            var raisedIsland=TextRegionFixtures.Island("lava",false);var hill=TextRegionFixtures.Circle("new_hill",null,.5f,.5f,.06f);hill.compositeOps[0].e=2;
            raisedIsland.elevationShapes.Add(hill);cases["raised-island"]=raisedIsland;
            foreach(var entry in replayStates.Where(e=>e.Key=="lava-from-scratch" || e.Key=="ruin-island"))cases["model-"+entry.Key]=entry.Value;
            int index=0;
            foreach(var entry in cases)
            {
                var tile=tiles[index++];target=tile.tile;MapGenParams.RestoreSnapshot(entry.Value,target);
                var snapshot=TileWorldSnapshot.Capture(tile);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="TextRegionCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                var result=AuthoringGeneration.Latest(target,MapGenParams.CaptureState(target));
                var counts=map.AllCells.GroupBy(c=>Terrain(map,c).defName).ToDictionary(g=>g.Key,g=>(object)g.Count());
                File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"id",entry.Key},{"tile",target},{"terrain",counts},{"result",result},{"state",entry.Value}}));
                Check(result!=null,entry.Key+": result tied to exact generation state");
                if(entry.Key=="capacity-failure")Check(result.issues.Count==1 && result.placements.Count==0,"capacity failure reports all-or-none placement");
                else
                {
                    Check(result.issues.Count==0,entry.Key+": no authored generation errors");
                    Check(result.placements.Count==entry.Value.structures.Sum(p=>p.count),entry.Key+": requested ruin count generated");
                    foreach(var placement in result.placements)
                    {
                        var rect=placement.rect;
                        bool fits=true;for(int z=rect.z;z<rect.z+rect.height;z++)for(int x=rect.x;x<rect.x+rect.width;x++)
                        {
                            var terrain=Terrain(map,new IntVec3(x,0,z));fits &= !terrain.dangerous && !terrain.IsWater;
                            if(entry.Key=="northeast-position")fits &= (x+.5f)/250>=.65f && (x+.5f)/250<=.92f && (z+.5f)/250>=.65f && (z+.5f)/250<=.92f;
                            else if(entry.Key!="model-lava-from-scratch")
                            {
                                var center=entry.Key=="moved-island"?new[]{.68f,.65f}:new[]{.5f,.5f};
                                fits &= Math.Pow(x/250f-center[0],2)+Math.Pow(z/250f-center[1],2)<=.14f*.14f;
                            }
                        }
                        Check(fits && placement.walls>0 && placement.floors>0 && placement.spawnedWalls==placement.walls &&
                            placement.wallCells.All(c=>new IntVec3(c[0],0,c[1]).GetEdifice(map)?.def==ThingDefOf.Wall),entry.Key+": full footprint and actual walls/floors inside safe region");
                    }
                }
                if(entry.Key!="materials" && entry.Key!="capacity-failure" && entry.Key!="northeast-position")Check(counts.ContainsKey("LavaDeep") && (int)counts["LavaDeep"]>2000,entry.Key+": actual lava rather than substituted water");
                if(entry.Key=="materials")foreach(string name in new[]{"LavaDeep","CooledLava","VolcanicRock","Gravel"})Check(counts.ContainsKey(name)&&(int)counts[name]>50,"actual fill generated "+name);
                if(entry.Key=="raised-island")Check(new IntVec3(125,0,125).GetEdifice(map)?.def.building.isNaturalRock==true,"later requested mountain survives earlier island flattening in a full map");
                if(entry.Key=="image-paused")Check(!Terrain(map,new IntVec3(10,0,10)).IsWater && MapGenParams.CaptureState(target).imageMap.cells=="WWWW","saved all-water image has no generation effect and remains saved");
                Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && snapshot.mutators.SequenceEqual(tile.Mutators.Select(m=>m.defName)),entry.Key+": generation does not mutate plan/world features");
                Check(!GenerationContext.Active,entry.Key+": scope disposed");
            }
            File.WriteAllText(Path.Combine(output,"text-region-result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",ok},{"maps",cases.Count},{"checks",checks}}));
            require(ok,"text regions native generation and dialog checks");
            mapCount=cases.Count;StartPreview(output,tiles[11].tile);
        }
        static void StartPreview(string output,int target)
        {
            previewOutput=output;previewPending=true;previewDeadline=DateTime.UtcNow.AddSeconds(60);
            var previewState=TextRegionFixtures.Island();MapGenParams.RestoreSnapshot(previewState,target);
            int previewTile=target;
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,target,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>
            {
                try
                {
                    var observed=AuthoringGeneration.Latest(previewTile,previewState);
                    Check(result.InvalidCells==0 && observed!=null && observed.preview && observed.issues.Count==0 && observed.placements.Count==2 && observed.placements.All(p=>p.spawnedWalls==0),"actual background Map Preview plans both ruins without unsupported Thing spawning");
                    var texture=new UnityEngine.Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();
                    File.WriteAllBytes(Path.Combine(output,"actual-background-preview.png"),UnityEngine.ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);
                    File.WriteAllText(Path.Combine(output,"actual-background-preview-result.json"),SimpleJson.Serialize(observed));
                    int walls=result.Pixels.Count(c=>Math.Abs(c.r-.7f)<.001 && Math.Abs(c.g-.68f)<.001 && Math.Abs(c.b-.6f)<.001);
                    Check(walls>30,"actual Map Preview texture includes positioned wall overlay");
                    Check(!GenerationContext.Active,"background preview leaves main-thread generation scope clear");
                }
                catch(Exception e){Check(false,"background preview callback: "+e);}
                Complete();
            }).Catch(error=>{Check(false,"background preview error: "+error);Complete();});
        }
    }
    public sealed class StructureFixture:IExposable
    {
        public TileMapState state;
        public void ExposeData()=>Scribe_Deep.Look(ref state,"state");
    }
}
