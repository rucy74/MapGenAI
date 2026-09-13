using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.Fixtures;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class SpatialProbe
    {
        static readonly List<string> results=new List<string>();
        static bool pending;static DateTime deadline;static string folder;
        static void Check(bool pass,string text){results.Add((pass?"PASS: ":"FAIL: ")+text);if(!pass)throw new InvalidOperationException(text);Log.Message("[SpatialProbe] "+text);}
        static void Finish(Exception error=null)
        {
            pending=false;
            File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",results},{"error",error?.ToString()}}));
            UnityEngine.Application.Quit();
        }
        public static void Tick(){if(pending && DateTime.UtcNow>deadline)Finish(new TimeoutException("Spatial background preview timeout"));}
        static TileMapState Clear()=>new TileMapState{hillAmount=.1f,ruinDensity=0,dangerDensity=0,vegetationDensity=0,animalDensity=0,geyserCount=0,hasRockChunks=false};
        public static void Run(string output)
        {
            folder=output;
            var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 &&
                FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).ToList();
            // Rivers themselves carry native mutators; the dry-tile no-mutator filter must not exclude them.
            var river=Find.WorldGrid.Tiles.First(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && FeaturePolicy.HasRiver(t) &&
                FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile));
            var dry=tiles.Where(t=>!FeaturePolicy.HasRiver(t)).Take(12).ToList();
            var cases=new Dictionary<string,TileMapState>();
            var bank=Clear();
            foreach(string side in new[]{"east","west"})bank.structures.Add(new StructurePlan{id=side,relation=new SpatialRelation{target="river",side=side,min_distance=2,max_distance=12},width=11,height=7,rotation=90,spacing=18,count=2});
            cases["river-banks"]=bank;
            var mountain=Clear();mountain.hillAmount=.4f;var hill=TextRegionFixtures.Circle("mountain",null,.5f,.5f,.15f);hill.compositeOps[0].e=2;mountain.elevationShapes.Add(hill);
            mountain.structures.Add(new StructurePlan{id="foothills",width=9,height=7,count=2,spacing=10,relation=new SpatialRelation{target="mountain",side="west",min_distance=2,max_distance=10}});cases["foothills"]=mountain;
            var edge=TextRegionFixtures.Island();edge.structures[0].width=9;edge.structures[0].height=7;edge.structures[0].count=3;edge.structures[0].spacing=8;
            edge.structures[0].relation=new SpatialRelation{target="region_edge",min_distance=2,max_distance=5};cases["island-edge"]=edge;
            var water=Clear();water.elevationShapes.Add(TextRegionFixtures.Circle("pond","water",.5f,.5f,.17f));
            water.structures.Add(new StructurePlan{id="waterside",relation=new SpatialRelation{target="water",side="north",min_distance=2,max_distance=8},width=9,height=7,spacing=10,count=2});cases["water-side"]=water;
            var missing=Clear();missing.structures.Add(new StructurePlan{id="first",position=new[]{.2f,.2f}});
            missing.structures.Add(new StructurePlan{id="missing",relation=new SpatialRelation{target="river"}});cases["missing-river"]=missing;
            foreach(int rotation in new[]{0,90,180,270}){var s=Clear();s.structures.Add(new StructurePlan{id="rotated",position=new[]{.5f,.5f},width=13,height=7,rotation=rotation});cases["rotation-"+rotation]=s;}
            var rotations=new Dictionary<int,HashSet<string>>();int index=0;
            foreach(var entry in cases)
            {
                var tile=entry.Key=="river-banks"?river:dry[index++];int target=tile.tile;
                var promptState=entry.Value.Clone();promptState.structures.Clear();MapGenParams.RestoreSnapshot(promptState,target);Find.WorldSelector.SelectedTile=target;
                var prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{target});
                File.WriteAllText(Path.Combine(output,entry.Key+"-prompt.txt"),prompt);File.WriteAllText(Path.Combine(output,entry.Key+"-before.json"),MapStateCodec.Serialize(promptState));
                var generationState=entry.Value;
                if(GenCommandLine.TryGetCommandLineArg("mapgenAISpatialResponses",out var responses) && File.Exists(Path.Combine(responses,entry.Key+"-response.json")))
                {
                    string response=File.ReadAllText(Path.Combine(responses,entry.Key+"-response.json"));var dialog=new Dialog_TextToMap();
                    string beforeReply=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                    typeof(Dialog_TextToMap).GetMethod("HandleResponse",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,new object[]{response});
                    if(MapGenAI.LLM.ProviderResponse.Command(response).GetString("action")=="generate")
                    {
                        generationState=MapGenParams.CaptureState(target);
                        var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,entry.Key+"-after.json")));
                        Check(MapStateCodec.Serialize(expected)==MapStateCodec.Serialize(generationState),entry.Key+": actual model relation response applied");
                        typeof(Dialog_TextToMap).GetMethod("DoUndo",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,null);
                    }
                    else Check(entry.Key=="missing-river",entry.Key+": model explains missing target");
                    Check(beforeReply==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),entry.Key+": actual model Undo/ask preserves state");dialog.PostClose();
                }
                MapGenParams.RestoreSnapshot(generationState,target);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var baseline=TileWorldSnapshot.Capture(tile);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="SpatialCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);var report=AuthoringGeneration.Latest(target,MapGenParams.CaptureState(target));
                if(entry.Key=="missing-river")Check(report.issues.Count>0 && report.placements.Count==0,"missing river rejects all positioned jobs before spawning");
                else
                {
                    Check(report!=null && report.issues.Count==0 && report.placements.Count==generationState.structures.Sum(p=>p.count),entry.Key+": full-map count and success");
                    foreach(var placed in report.placements)
                    {
                        var plan=generationState.structures.Single(p=>p.id==placed.id);var rect=placed.rect;
                        Check(rect.width==(plan.rotation%180==0?plan.width:plan.height) && rect.height==(plan.rotation%180==0?plan.height:plan.width),entry.Key+": rotated footprint dimensions");
                        Check(placed.spawnedWalls==placed.walls && placed.walls>0 && placed.wallCells.All(c=>new IntVec3(c[0],0,c[1]).GetEdifice(map)?.def==ThingDefOf.Wall),entry.Key+": actual walls spawned");
                        if(plan.relation!=null)Check(VerifyRelation(map,plan,rect),entry.Key+": independent full-footprint distance/side/region verification");
                        if(entry.Key.StartsWith("rotation-"))rotations[plan.rotation]=new HashSet<string>(placed.wallCells.Select(c=>(c[0]-rect.x)+","+(c[1]-rect.z)));
                    }
                    for(int a=0;a<report.placements.Count;a++)for(int b=a+1;b<report.placements.Count;b++)
                    {
                        var x=report.placements[a];var y=report.placements[b];int gap=Math.Max(generationState.structures.Single(p=>p.id==x.id).spacing,generationState.structures.Single(p=>p.id==y.id).spacing);
                        Check(x.rect.x>=y.rect.x+y.rect.width+gap || y.rect.x>=x.rect.x+x.rect.width+gap || x.rect.z>=y.rect.z+y.rect.height+gap || y.rect.z>=x.rect.z+x.rect.height+gap,entry.Key+": minimum inter-structure gap");
                    }
                }
                Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && baseline.mutators.SequenceEqual(tile.Mutators.Select(m=>m.defName)) && !GenerationContext.Active,entry.Key+": state/world preserved and scope closed");
                File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(report));
            }
            foreach(int rotation in new[]{90,180,270})
            {
                var expected=new HashSet<string>(rotations[0].Select(text=>{var c=text.Split(',').Select(int.Parse).ToArray();int x=c[0],z=c[1];return rotation==90?(6-z)+","+x:rotation==180?(12-x)+","+(6-z):z+","+(12-x);}));
                Check(expected.SetEquals(rotations[rotation]),"actual ruin wall pattern rotates "+rotation+" degrees");
            }
            // Real Scribe for the new nested relation, rotation and spacing fields.
            var fixture=new Envelope{state=bank};string file=Path.Combine(output,"spatial-scribe.xml");string original=MapStateCodec.Serialize(bank);
            Scribe.saver.InitSaving(file,"Spatial");Scribe_Deep.Look(ref fixture,"fixture");Scribe.saver.FinalizeSaving();fixture=null;
            Scribe.loader.InitLoading(file);Scribe_Deep.Look(ref fixture,"fixture");Scribe.loader.FinalizeLoading();Check(original==MapStateCodec.Serialize(fixture.state),"real Scribe relation/spacing/rotation roundtrip");
            int previewTile=dry.Last().tile;MapGenParams.RestoreSnapshot(edge,previewTile);pending=true;deadline=DateTime.UtcNow.AddSeconds(60);
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,previewTile,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try
                {
                    var observed=AuthoringGeneration.Latest(previewTile,edge);
                    Check(result.InvalidCells==0 && observed.preview && observed.issues.Count==0 && observed.placements.Count==3 && observed.placements.All(p=>p.spawnedWalls==0),"real background preview enforces island-edge relation and retains plans only");
                    foreach(var p in observed.placements)
                    {
                        bool inside=true; // Analytic circle containment, independent from RegionGrid.
                        for(int z=p.rect.z;z<p.rect.z+p.rect.height;z++)for(int x=p.rect.x;x<p.rect.x+p.rect.width;x++)
                            inside &= Math.Pow(x/250.0-.5,2)+Math.Pow(z/250.0-.5,2)<=.14*.14;
                        Check(inside,"preview full footprint stays inside island");
                    }
                    var texture=new UnityEngine.Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(output,"spatial-preview.png"),UnityEngine.ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);
                    File.WriteAllText(Path.Combine(output,"spatial-preview-result.json"),SimpleJson.Serialize(observed));Finish();
                }
                catch(Exception e){Finish(e);}
            },e=>Finish(e));
        }
        static bool VerifyRelation(Map map,StructurePlan plan,PlannedRect r)
        {
            var rel=plan.relation;var targets=new List<IntVec3>();
            foreach(var cell in map.AllCells)
            {
                var terrain=map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(cell));
                bool target=rel.target=="river"?terrain.IsRiver:rel.target=="water"?terrain.IsWater:rel.target=="mountain"?cell.GetEdifice(map)?.def.building.isNaturalRock==true:false;
                if(rel.target=="region_edge")
                {
                    Func<int,int,bool> inside=(x,z)=>Math.Pow(x/250.0-.5,2)+Math.Pow(z/250.0-.5,2)<=.14*.14;
                    target=inside(cell.x,cell.z) && (!inside(cell.x+1,cell.z)||!inside(cell.x-1,cell.z)||!inside(cell.x,cell.z+1)||!inside(cell.x,cell.z-1));
                }
                if(target)targets.Add(cell);
            }
            if(targets.Count==0)return false;double min=double.MaxValue;int cx=r.x+(r.width-1)/2,cz=r.z+(r.height-1)/2;double nearest=double.MaxValue;IntVec3 closest=IntVec3.Invalid;
            foreach(var c in targets)
            {
                int dx=c.x<r.x?r.x-c.x:c.x>=r.x+r.width?c.x-r.x-r.width+1:0,dz=c.z<r.z?r.z-c.z:c.z>=r.z+r.height?c.z-r.z-r.height+1:0;
                min=Math.Min(min,dx*dx+dz*dz);double center=(c.x-cx)*(c.x-cx)+(c.z-cz)*(c.z-cz);
                if(center<nearest){nearest=center;closest=c;}
            }
            bool side=rel.side=="any" || rel.side=="east"&&cx>closest.x || rel.side=="west"&&cx<closest.x || rel.side=="north"&&cz>closest.z || rel.side=="south"&&cz<closest.z;
            return min>=rel.min_distance*rel.min_distance && min<=rel.max_distance*rel.max_distance && side;
        }
        public class Envelope:IExposable {public TileMapState state;public void ExposeData(){Scribe_Deep.Look(ref state,"state");}}
    }
}
