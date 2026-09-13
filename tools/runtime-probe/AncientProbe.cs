using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.Fixtures;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class AncientProbe
    {
        static readonly List<string> checks=new List<string>();static bool pending;static DateTime deadline;static string folder;
        static void Check(bool pass,string message){checks.Add((pass?"PASS: ":"FAIL: ")+message);if(!pass)throw new InvalidOperationException(message);Log.Message("[AncientProbe] "+message);}
        static void Finish(Exception error=null){pending=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"error",error?.ToString()}}));UnityEngine.Application.Quit();}
        public static void Tick(){if(pending && DateTime.UtcNow>deadline)Finish(new TimeoutException("Ancient preview timeout"));}
        static StructurePlan Temple(string id="temple")=>new StructurePlan{id=id,kind="ancient_danger",width=18,height=18,count=1};
        static TileMapState Clear()=>new TileMapState{hillAmount=.1f,ruinDensity=0,dangerDensity=0,vegetationDensity=0,animalDensity=0,geyserCount=0,hasRockChunks=false};
        static string Prompt(int tile)=>(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{tile});
        public static void Run(string output)
        {
            folder=output;var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 &&
                !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(9).ToList();
            var cases=new Dictionary<string,TileMapState>();
            var island=TextRegionFixtures.Island("lava",false);var temple=Temple();temple.region="island";island.structures.Add(temple);cases["ancient-island"]=island;
            var foothill=Clear();foothill.hillAmount=.4f;var mountain=TextRegionFixtures.Circle("mountain",null,.5f,.5f,.15f);mountain.compositeOps[0].e=2;foothill.elevationShapes.Add(mountain);
            temple=Temple();temple.height=16;temple.relation=new SpatialRelation{target="mountain",side="west",min_distance=2,max_distance=12};foothill.structures.Add(temple);cases["ancient-foothill"]=foothill;
            var pair=Clear();temple=Temple();temple.bounds=new[]{.2f,.2f,.8f,.8f};temple.count=2;temple.spacing=15;pair.structures.Add(temple);cases["ancient-pair"]=pair;
            var peaceful=Clear();temple=Temple();temple.position=new[]{.5f,.5f};peaceful.structures.Add(temple);cases["ancient-peaceful"]=peaceful;
            var failure=Clear();failure.elevationShapes.Add(TextRegionFixtures.Circle("tiny","soil",.5f,.5f,.015f));
            failure.structures.Add(new StructurePlan{id="first",position=new[]{.2f,.2f}});temple=Temple();temple.region="tiny";failure.structures.Add(temple);cases["ancient-capacity"]=failure;
            int index=0;
            foreach(var entry in cases)
            {
                var tile=tiles[index++];int target=tile.tile;Find.WorldSelector.SelectedTile=target;
                var before=entry.Value.Clone();before.structures.Clear();MapGenParams.RestoreSnapshot(before,target);
                File.WriteAllText(Path.Combine(output,entry.Key+"-before.json"),MapStateCodec.Serialize(before));File.WriteAllText(Path.Combine(output,entry.Key+"-prompt.txt"),Prompt(target));
                var state=entry.Value;
                if(GenCommandLine.TryGetCommandLineArg("mapgenAIAncientResponses",out var replies) && File.Exists(Path.Combine(replies,entry.Key+"-response.json")))state=Replay(target,replies,entry.Key,before);
                MapGenParams.RestoreSnapshot(state,target);string stored=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var original=TileWorldSnapshot.Capture(tile);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="AncientCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var global=BaseGen.globalSettings;var stack=BaseGen.symbolStack;bool oldPeace=Find.Storyteller.difficulty.peacefulTemples;Map map;
                try{Find.Storyteller.difficulty.peacefulTemples=entry.Key=="ancient-peaceful";map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);}
                finally{Find.Storyteller.difficulty.peacefulTemples=oldPeace;}
                var result=AuthoringGeneration.Latest(target,MapGenParams.CaptureState(target));
                File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(result));
                if(entry.Key=="ancient-capacity")Check(result.issues.Count>0 && result.placements.Count==0,"ancient capacity failure spawns no positioned jobs");
                else
                {
                    Check(result!=null && result.issues.Count==0 && result.placements.Count==state.structures.Sum(p=>p.count),entry.Key+": native generation completed requested count");
                    foreach(var p in result.placements)
                    {
                        var rect=new CellRect(p.rect.x,p.rect.z,p.rect.width,p.rect.height);
                        Check(p.kind=="ancient_danger" && p.spawnedWalls>0 && p.roofCells>0 && p.floors>0 && p.warningThings==2,entry.Key+": actual temple walls floors roof and warning signals");
                        Check(p.caskets>0 && p.containedThings>0 && p.lootThings>0,entry.Key+": native cryptosleep contents and loot");
                        Check(p.wallCells.All(c=>new IntVec3(c[0],0,c[1]).GetEdifice(map)?.def==ThingDefOf.Wall),entry.Key+": recorded walls exist in full map");
                        var caskets=map.listerThings.ThingsOfDef(ThingDefOf.AncientCryptosleepCasket).Where(t=>rect.Contains(t.Position)).ToList();
                        Check(caskets.Count==p.caskets && caskets.All(t=>t.OccupiedRect().FullyContainedWithin(rect)),entry.Key+": whole casket footprints stay reserved");
                        Check(caskets.Cast<IThingHolder>().Sum(h=>h.GetDirectlyHeldThings().Count)==p.containedThings && rect.Cells.Count(c=>map.roofGrid.RoofAt(c)!=null)==p.roofCells,entry.Key+": independent native container and roof counts");
                        if(entry.Key=="ancient-island")Check(rect.Cells.All(c=>Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2)<=.14*.14 && !map.terrainGrid.TerrainAt(c).dangerous),entry.Key+": whole native temple stays on soil island outside lava");
                        if(entry.Key=="ancient-peaceful")Check(p.defenders==0 && caskets.Cast<IThingHolder>().SelectMany(h=>h.GetDirectlyHeldThings()).OfType<Pawn>().All(pawn=>!pawn.HostileTo(Faction.OfPlayer)),"peaceful difficulty retains non-hostile temple occupants");
                    }
                }
                Check(ReferenceEquals(global,BaseGen.globalSettings) && ReferenceEquals(stack,BaseGen.symbolStack) && stack.Empty,entry.Key+": BaseGen shared state restored");
                Check(stored==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && original.mutators.SequenceEqual(tile.Mutators.Select(m=>m.defName)) && !GenerationContext.Active,entry.Key+": plan/world/difficulty scope preserved");
            }
            int promptTile=tiles[6].tile;Find.WorldSelector.SelectedTile=promptTile;
            foreach(var id in new[]{"ancient-rotation","ancient-unknown","ancient-density"})
            {
                MapGenParams.RestoreSnapshot(island,promptTile);File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(island));File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),Prompt(promptTile));
                if(GenCommandLine.TryGetCommandLineArg("mapgenAIAncientResponses",out var replies) && File.Exists(Path.Combine(replies,id+"-response.json")))Replay(promptTile,replies,id,island);
            }
            var fixture=new Envelope{state=island};string path=Path.Combine(output,"ancient-scribe.xml");string saved=MapStateCodec.Serialize(island);
            Scribe.saver.InitSaving(path,"Ancient");Scribe_Deep.Look(ref fixture,"fixture");Scribe.saver.FinalizeSaving();fixture=null;
            Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref fixture,"fixture");Scribe.loader.FinalizeLoading();Check(saved==MapStateCodec.Serialize(fixture.state),"actual Scribe retains ancient kind, size and region");
            int previewTile=tiles[8].tile;MapGenParams.RestoreSnapshot(island,previewTile);pending=true;deadline=DateTime.UtcNow.AddSeconds(60);
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,previewTile,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try
                {
                    var report=AuthoringGeneration.Latest(previewTile,island);
                    Check(result.InvalidCells==0 && report.preview && report.issues.Count==0 && report.placements.Count==1,"actual ancient preview completes without native pawn generation");
                    var p=report.placements.Single();Check(p.kind=="ancient_danger" && p.spawnedWalls==0 && p.caskets==0 && p.defenders==0 && p.wallCells.Count==68,"ancient preview is an explicit reservation outline only");
                    Check(result.Pixels.Count(c=>Math.Abs(c.r-.9f)<.001 && Math.Abs(c.g-.6f)<.001 && Math.Abs(c.b-.25f)<.001)==68,"actual preview displays orange ancient reservation");
                    var texture=new UnityEngine.Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(output,"ancient-preview.png"),UnityEngine.ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);
                    File.WriteAllText(Path.Combine(output,"ancient-preview-result.json"),SimpleJson.Serialize(report));Finish();
                }
                catch(Exception error){Finish(error);}
            },error=>Finish(error));
        }
        static TileMapState Replay(int target,string replies,string id,TileMapState before)
        {
            MapGenParams.RestoreSnapshot(before,target);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
            var dialog=new Dialog_TextToMap();string response=File.ReadAllText(Path.Combine(replies,id+"-response.json"));
            var action=MapGenAI.LLM.ProviderResponse.Command(response).GetString("action");
            typeof(Dialog_TextToMap).GetMethod("HandleResponse",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{response});
            var after=MapGenParams.CaptureState(target);
            if(action=="generate")
            {
                var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(replies,id+"-after.json")));Check(MapStateCodec.Serialize(expected)==MapStateCodec.Serialize(after),id+": actual model state matches");
                typeof(Dialog_TextToMap).GetMethod("DoUndo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,null);
            }
            else Check(action=="ask" && (id=="ancient-rotation" || id=="ancient-unknown"),id+": model explains supported limits");
            Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),id+": actual Undo/ask preserves state");dialog.PostClose();return after;
        }
        public class Envelope:IExposable {public TileMapState state;public void ExposeData(){Scribe_Deep.Look(ref state,"state");}}
    }
}
