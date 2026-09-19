using System;
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

namespace MapGenAI.RuntimeProbe
{
    // Measurement harness: a completed run is not a claim that every requested landform passed.
    static class LandformSuiteProbe
    {
        static int seedOffset;
        static readonly Queue<Tuple<string,string,int,TileMapState>> previews=new Queue<Tuple<string,string,int,TileMapState>>();
        static string previewFolder,previewId,previewKind;static bool previewPending;static DateTime previewDeadline;
        static readonly List<object> previewResults=new List<object>();
        public static void Tick(){if(previewPending && DateTime.UtcNow>previewDeadline)PreviewFinish(new TimeoutException("Landform preview timed out"));}
        public static void Configure()
        {
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAILandformSuite",out _))return;
            var h=new Harmony("choco.mapgenai.probe.landform-suite");
            h.Patch(AccessTools.Method(typeof(WorldGenerator),"GenerateWorld"),prefix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(WorldSeed)));
            h.Patch(AccessTools.Method(typeof(MapGenerator),"GenerateContentsIntoMap"),prefix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(MapSeed)));
            h.Patch(AccessTools.Method(typeof(PassageGeneration),"Apply"),prefix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(BeforePassages)),postfix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(AfterPassages)));
            h.Patch(AccessTools.Method(typeof(PassageGeneration),"Check"),postfix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(MeasureCompound)));
            Rand.Seed=1750915;
        }
        static void WorldSeed(ref string seedString){seedString="mapgenai-landforms-v1";}
        static void MapSeed(ref int seed){seed=Gen.HashCombineInt(seed,seedOffset);}
        static object Invoke(object dialog,string name,params object[] args)=>typeof(Dialog_TextToMap).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dialog,args);
        static Dictionary<string,object> passageAudit;
        static Dictionary<string,object> compoundAudit;
        static float[] passageElevation,passageFertility;static string[] passageMaterials;static bool[] passageFlatten;
        static void BeforePassages(Map map,MapGenFloatGrid elevation)
        {
            passageAudit=null;int n=map.Size.x*map.Size.z;passageElevation=new float[n];passageFertility=new float[n];var regions=GenerationContext.Regions(map);
            passageMaterials=(string[])regions.Materials.Clone();passageFlatten=(bool[])regions.Flatten.Clone();
            foreach(var c in map.AllCells){int i=c.z*map.Size.x+c.x;passageElevation[i]=elevation[c];passageFertility[i]=MapGenerator.Fertility[c];}
        }
        static void AfterPassages(Map map,MapGenFloatGrid elevation)
        {
            var regions=GenerationContext.Regions(map);var edited=new bool[passageElevation.Length];var details=new List<object>();
            foreach(var s in GenerationContext.State.elevationShapes.Where(s=>s.type=="passage"))
            {
                var mask=regions.Mask(s.id);int flat=0,count=0;
                foreach(var c in map.AllCells){int i=c.z*map.Size.x+c.x;if(!mask[i])continue;edited[i]=true;count++;if(passageElevation[i]<.7f)flat++;}
                details.Add(Obj("id",s.id,"scope",typeof(ElevationShape).GetField("scope")?.GetValue(s),"selectedCells",count,"selectedOpenGround",flat));
            }
            int outside=0,uncleared=0;
            foreach(var c in map.AllCells)
            {
                int i=c.z*map.Size.x+c.x;
                if(!edited[i] && (elevation[c]!=passageElevation[i] || MapGenerator.Fertility[c]!=passageFertility[i] || regions.Materials[i]!=passageMaterials[i] || regions.Flatten[i]!=passageFlatten[i]))outside++;
                if(edited[i] && elevation[c]>=.7f)uncleared++;
            }
            passageAudit=Obj("outsideChanges",outside,"unclearedSelectedCells",uncleared,"passages",details);
        }
        static void MeasureCompound(Map map)
        {
            int w=map.Size.x,h=map.Size.z;var state=GenerationContext.State;var grid=GenerationContext.Regions(map);var report=AuthoringGeneration.Current;
            var fills=new List<object>();var structures=new List<object>();var routes=new List<object>();var enclosures=new List<object>();
            // Independent outside flood: intentionally does not call RegionCoverage.Enclosed/Select.
            bool[] Area(string id,string part)
            {
                var mask=grid.Mask(id);if(part!="enclosed")return mask;
                var open=mask.Select(b=>!b).ToArray();var seeds=Enumerable.Range(0,w).Concat(Enumerable.Range((h-1)*w,w)).Concat(Enumerable.Range(0,h).SelectMany(z=>new[]{z*w,z*w+w-1}));
                var outside=Flood(open,w,h,seeds);return open.Select((b,i)=>b&&!outside[i]).ToArray();
            }
            var dry=new bool[w*h];foreach(var c in map.AllCells){var t=map.terrainGrid.TerrainAt(c);dry[c.z*w+c.x]=!t.IsWater&&!t.dangerous&&c.Walkable(map)&&MapGenerator.Elevation[c]<.7f;}
            foreach(var s in state.elevationShapes.Where(s=>s.type=="composite" || s.type=="ring" || s.type=="bump"))
            {
                var area=Area(s.id,"enclosed");int cells=area.Count(b=>b);if(cells==0)continue;
                int water=0,rock=0,clear=0;
                foreach(var c in map.AllCells)if(area[c.z*w+c.x])
                {if(map.terrainGrid.TerrainAt(c).IsWater)water++;if(MapGenerator.Elevation[c]>=.7f)rock++;if(dry[c.z*w+c.x])clear++;}
                enclosures.Add(Obj("id",s.id,"cells",cells,"dryWalkableCells",clear,"waterCells",water,"mountainCells",rock));
            }
            foreach(var s in state.elevationShapes.Where(s=>s.type=="passage"))
            {
                int sx=Math.Min(w-1,(int)(s.points[0][0]*w+.0001f)),sz=Math.Min(h-1,(int)(s.points[0][1]*h+.0001f));
                var last=s.points.Last();int ex=Math.Min(w-1,(int)(last[0]*w+.0001f)),ez=Math.Min(h-1,(int)(last[1]*h+.0001f));
                // Endpoints on the map border cannot center an N-wide pawn; clamp to an interior footprint.
                int lo=(s.width-1)/2,hi=s.width/2;sx=Math.Max(lo,Math.Min(w-1-hi,sx));ex=Math.Max(lo,Math.Min(w-1-hi,ex));sz=Math.Max(lo,Math.Min(h-1-hi,sz));ez=Math.Max(lo,Math.Min(h-1-hi,ez));
                var reached=Flood(Footprint(dry,w,h,s.width),w,h,new[]{sz*w+sx});
                var cut=grid.Mask(s.id);int blocked=0;for(int i=0;i<cut.Length;i++)if(cut[i]&&!dry[i])blocked++;
                routes.Add(Obj("id",s.id,"width",s.width,"dryEndpointConnection",reached[ez*w+ex],"cutCells",cut.Count(b=>b),"blockedCutCells",blocked));
            }
            foreach(var s in state.elevationShapes.Where(s=>s.type=="region_fill"))
            {
                var area=Area(s.region,s.region_part);int eligible=0,painted=0,water=0,rock=0;
                foreach(var c in map.AllCells)if(area[c.z*w+c.x])
                {
                    var t=map.terrainGrid.TerrainAt(c);bool solid=MapGenerator.Elevation[c]>=.7f && MapGenerator.Caves[c]<=0;
                    if(t.IsWater)water++;if(solid)rock++;
                    bool natural=t.designationCategory==null&&(t.costList==null||t.costList.Count==0)&&t.costStuffCount==0&&!t.temporary&&!t.bridge&&!t.isFoundation&&t.defName!="Underwall";
                    if(!solid&&c.GetEdifice(map)==null&&!c.GetThingList(map).Any(tg=>tg is Pawn)&&!t.IsRiver&&!t.HasTag("Road")&&!t.defName.Contains("Ocean")&&natural)
                    {eligible++;if(t.defName==TerrainMaterials.DefName(s.fill))painted++;}
                }
                fills.Add(Obj("id",s.id,"eligible",eligible,"painted",painted,"waterInInterior",water,"rockInInterior",rock));
            }
            if(report!=null)foreach(var p in state.structures)
            {
                // Reflection lets the same instrument load the tagged pre-feature DLL.
                string part=typeof(StructurePlan).GetField("region_part")?.GetValue(p) as string;var area=p.region==null?null:Area(p.region,part);int outside=0,routeOverlap=0;
                foreach(var placed in report.placements.Where(r=>r.id==p.id))
                for(int z=placed.rect.z;z<placed.rect.z+placed.rect.height;z++)for(int x=placed.rect.x;x<placed.rect.x+placed.rect.width;x++)
                {
                    if(area!=null&&!area[z*w+x])outside++;
                    // Compound fixtures have axis-aligned routes; use independent interval arithmetic.
                    foreach(var s in state.elevationShapes.Where(s=>s.type=="passage"))
                    {
                        var a=s.points[0];var b=s.points.Last();int ax=Math.Min(w-1,(int)(a[0]*w+.0001f)),az=Math.Min(h-1,(int)(a[1]*h+.0001f)),bx=Math.Min(w-1,(int)(b[0]*w+.0001f)),bz=Math.Min(h-1,(int)(b[1]*h+.0001f));int lo=(s.width-1)/2,hi=s.width/2;
                        if((ax==bx||az==bz)&&x>=Math.Min(ax,bx)-lo&&x<=Math.Max(ax,bx)+hi&&z>=Math.Min(az,bz)-lo&&z<=Math.Max(az,bz)+hi)routeOverlap++;
                    }
                }
                structures.Add(Obj("id",p.id,"outsideRegionCells",outside,"routeOverlapCells",routeOverlap));
            }
            compoundAudit=Obj("fills",fills,"structures",structures,"routes",routes,"enclosures",enclosures);
        }
        static string Hash(string data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data))).Replace("-","").ToLowerInvariant();}
        static Dictionary<string,object> Obj(params object[] pairs){var d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d;}
        public static void Run(string output,string manifest,string replies,int sample)
        {
            var results=new List<object>();var tiles=new List<object>();bool complete=false;string fatal=null;seedOffset=sample;
            try
            {
                var cases=SimpleJson.Parse(File.ReadAllText(manifest)).GetObjectArray("cases");
                var inland=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(24).ToArray();
                var coast=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && t.hilliness==Hilliness.Flat && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Any(n=>n.PrimaryBiome==BiomeDefOf.Ocean) && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(6).ToArray();
                if(inland.Length<24 || coast.Length<6)throw new InvalidOperationException("Insufficient fixed-world fixture tiles");
                int insideIndex=0,coastIndex=0;
                foreach(var c in cases)
                {
                    string id=c.GetString("id"),kind=c.GetString("kind");var tile=c.GetString("tile")=="coast"?coast[coastIndex++]:inland[insideIndex++];int target=tile.tile;
                    var before=c.GetString("beforeFile")==null ? MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(c.GetObject("beforeParams"))) : MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifest),c.GetString("beforeFile"))));
                    Find.WorldSelector.SelectedTile=target;MapGenParams.RestoreSnapshot(before,target);
                    File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(before));
                    File.WriteAllText(Path.Combine(output,id+"-request.txt"),c.GetString("request"));
                    File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{target}));
                    tiles.Add(Obj("id",id,"tile",target,"biome",tile.PrimaryBiome.defName,"hills",tile.hilliness.ToString(),"mutators",tile.Mutators.Select(m=>m.defName).ToArray(),"sample",sample));
                    if(string.IsNullOrEmpty(replies))continue;
                    try
                    {
                        string response=File.ReadAllText(Path.Combine(replies,id+"-response.json"));var command=ProviderResponse.Command(response);
                        MapGenParams.RestoreSnapshot(before,target);var dialog=new Dialog_TextToMap();Invoke(dialog,"HandleResponse",response);
                        var after=MapGenParams.CaptureState(target);string serialized=MapStateCodec.Serialize(after);
                        var history=(List<ChatMessage>)typeof(Dialog_TextToMap).GetField("_history",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                        string message=history.LastOrDefault()?.Content;
                        if(command.GetString("action")=="recommend")
                        {
                            string original=MapStateCodec.Serialize(before);
                            var offered=(List<RecommendationPlan>)typeof(Dialog_TextToMap).GetField("_recommendations",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                            if(offered==null || serialized!=original)throw new InvalidOperationException("Recommendation rejected or mutated the map before selection: "+message);
                            File.WriteAllText(Path.Combine(output,id+"-choices.txt"),message);
                            var selections=new List<object>();
                            for(int i=0;i<offered.Count;i++)
                            {
                                if(i>0)Invoke(dialog,"HandleResponse",response);
                                var expected=MapStateEditor.Merge(before,MapParameterParser.Parse(ProviderResponse.Command(offered[i].Command).GetObject("params")));
                                typeof(Dialog_TextToMap).GetField("_inputText",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,(i+1).ToString());
                                Invoke(dialog,"SendMessage");
                                string chosen=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                                if(chosen!=MapStateCodec.Serialize(expected))throw new InvalidOperationException("Stored selection changed: "+id+"/"+(i+1));
                                File.WriteAllText(Path.Combine(output,id+"-option-"+(i+1)+"-after.json"),chosen);
                                Invoke(dialog,"DoUndo");
                                if(MapStateCodec.Serialize(MapGenParams.CaptureState(target))!=original)throw new InvalidOperationException("Recommendation Undo changed source state");
                                selections.Add(Obj("option",i+1,"appliedStoredCommand",true,"undo",true));
                            }
                            results.Add(Obj("id",id,"action","recommend","options",offered.Count,"unchangedBeforeSelection",true,"selections",selections));dialog.PostClose();Write();continue;
                        }
                        if(command.GetString("action")!="generate" || serialized==MapStateCodec.Serialize(before) && !c.GetBool("generateUnchanged"))
                        {results.Add(Obj("id",id,"action",command.GetString("action"),"generated",false,"message",message));dialog.PostClose();Write();continue;}
                        bool preserved=before.elevationShapes.All(s=>after.elevationShapes.Any(a=>a.id==s.id && SimpleJson.Serialize(s)==SimpleJson.Serialize(a)));
                        Invoke(dialog,"DoUndo");bool undo=MapStateCodec.Serialize(MapGenParams.CaptureState(target))==MapStateCodec.Serialize(before);dialog.PostClose();
                        if(!undo)throw new InvalidOperationException("Dialog Undo changed the source state");
                        File.WriteAllText(Path.Combine(output,id+"-after.json"),serialized);MapGenParams.RestoreSnapshot(after,target);
                        var fixture=new ProbeEnvelope{state=after};string scribe=Path.Combine(output,id+"-scribe.xml");
                        Scribe.saver.InitSaving(scribe,"LandformSuite");Scribe_Deep.Look(ref fixture,"fixture");Scribe.saver.FinalizeSaving();fixture=null;
                        Scribe.loader.InitLoading(scribe);Scribe_Deep.Look(ref fixture,"fixture");Scribe.loader.FinalizeLoading();
                        if(MapStateCodec.Serialize(fixture.state)!=serialized || MapStateCodec.Serialize(MapStateCodec.Deserialize(serialized))!=serialized)throw new InvalidOperationException("Scribe/preset roundtrip changed the accepted state");
                        var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                        var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);generator.genSteps=new List<GenStepDef>(source.genSteps);
                        if(c.GetBool("omitAmbientRuins"))generator.genSteps.RemoveAll(s=>s.defName.StartsWith("Ancient",StringComparison.Ordinal) || new[]{"ScatterRuinsSimple","ScatterShrines","ScatterRoadDebris","ScatterCaveDebris","MechanoidRemains"}.Contains(s.defName));
                        File.WriteAllText(Path.Combine(output,id+"-steps.json"),SimpleJson.Serialize(generator.genSteps.Select(s=>Obj("name",s.defName,"type",s.genStep.GetType().Name,"order",s.order)).ToList()));
                        generator.genSteps.Add(new GenStepDef{defName="LandformSuiteCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=id}});
                        var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);var report=AuthoringGeneration.Latest(target,after);
                        var measured=Measure(map,kind);measured["id"]=id;measured["generated"]=true;measured["action"]="generate";measured["preservedSourceShapes"]=preserved;measured["undo"]=undo;measured["issues"]=report?.issues;
                        measured["coverage"]=report?.coverage;measured["placements"]=report?.placements;
                        string cells=Sample(map);File.WriteAllText(Path.Combine(output,id+"-cells.json"),cells);measured["cellHash"]=Hash(cells);results.Add(measured);Write();
                        if(c.GetBool("preview"))previews.Enqueue(Tuple.Create(id,kind,target,after));
                    }
                    catch(Exception error){results.Add(Obj("id",id,"error",error.ToString()));Write();}
                }
                complete=true;
            }
            catch(Exception error){fatal=error.ToString();}
            Write();
            if(complete && previews.Count>0)
            {
                previewFolder=output;new Harmony("choco.mapgenai.probe.landform-preview").Patch(AccessTools.Method(AccessTools.TypeByName("MapGenAI.MapGen.PassageGeneration"),"Check"),postfix:new HarmonyMethod(typeof(LandformSuiteProbe),nameof(PreviewMeasured)));NextPreview();
            }
            else Application.Quit();
            void Write(){File.WriteAllText(Path.Combine(output,"suite-result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"complete",complete},{"fatal",fatal},{"sample",sample},{"worldSeed",Find.World.info.seedString},{"tiles",tiles},{"results",results},{"scope","Measured actual full maps. Individual semantic metrics and visual review determine acceptance; completion alone is not PASS."}}));}
        }
        static void PreviewMeasured(Map map)
        {
            if(!previewPending)return;MeasureCompound(map);var measured=Measure(map,previewKind);measured["id"]=previewId;measured["issues"]=AuthoringGeneration.Current?.issues.ToArray();previewResults.Add(measured);
        }
        static void PreviewFinish(Exception error=null)
        {
            previewPending=false;File.WriteAllText(Path.Combine(previewFolder,"preview-result.json"),SimpleJson.Serialize(Obj("ok",error==null,"results",previewResults,"error",error?.ToString())));Application.Quit();
        }
        static void NextPreview()
        {
            if(previews.Count==0){PreviewFinish();return;}var entry=previews.Dequeue();previewId=entry.Item1;previewKind=entry.Item2;previewPending=true;previewDeadline=DateTime.UtcNow.AddSeconds(75);MapGenParams.RestoreSnapshot(entry.Item4,entry.Item3);
            string id=previewId;var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,entry.Item3,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try{
                    if(result.InvalidCells!=0 || !previewResults.Any(r=>((Dictionary<string,object>)r)["id"].Equals(id)))throw new InvalidOperationException("Preview failed to capture actual terrain");
                    var texture=new Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(previewFolder,id+"-background-preview.png"),ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);previewPending=false;NextPreview();
                }catch(Exception e){PreviewFinish(e);}
            },error=>PreviewFinish(error));
        }
        static string Sample(Map map)
        {
            var terrain=new List<string>();var edifice=new List<string>();var roofs=new List<string>();
            foreach(var c in map.AllCells){terrain.Add(map.terrainGrid.TerrainAt(c).defName);edifice.Add(c.GetEdifice(map)?.def.defName);roofs.Add(map.roofGrid.RoofAt(c)?.defName);}
            return SimpleJson.Serialize(Obj("width",map.Size.x,"height",map.Size.z,"terrain",Runs(terrain),"edifice",Runs(edifice),"roofs",Runs(roofs)));
        }
        // Lossless run-length encoding keeps full-cell evidence below the product JSON envelope cap.
        static object Runs(List<string> cells)
        {
            var names=cells.Distinct().OrderBy(s=>s,StringComparer.Ordinal).ToList();var data=new StringBuilder();
            for(int i=0;i<cells.Count;){int end=i+1;while(end<cells.Count && cells[end]==cells[i])end++;if(data.Length>0)data.Append(',');data.Append(names.IndexOf(cells[i])).Append(':').Append(end-i);i=end;}
            return Obj("names",names,"runs",data.ToString());
        }
        static Dictionary<string,object> Measure(Map map,string kind)
        {
            int w=map.Size.x,h=map.Size.z;var dry=new bool[w*h];var walk=new bool[w*h];var water=new bool[w*h];var mountain=new bool[w*h];var roof=new bool[w*h];int rich=0,sand=0;
            foreach(var c in map.AllCells){int i=c.z*w+c.x;var t=map.terrainGrid.TerrainAt(c);water[i]=t.IsWater;walk[i]=c.Walkable(map);mountain[i]=c.GetEdifice(map)?.def.building?.isNaturalRock==true || (AuthoringGeneration.Current?.preview==true && MapGenerator.Elevation[c]>=.7f && MapGenerator.Caves[c]<=0);dry[i]=walk[i] && !water[i] && !mountain[i];roof[i]=map.roofGrid.RoofAt(c)?.isThickRoof==true;if(t.defName=="SoilRich")rich++;if(t.defName=="Sand")sand++;}
            var result=new Dictionary<string,object>{{"kind",kind},{"dryCells",dry.Count(v=>v)},{"waterCells",water.Count(v=>v)},{"mountainCells",mountain.Count(v=>v)},{"richSoilCells",rich},{"sandCells",sand},{"thickRoofCells",roof.Count(v=>v)}};
            result["passageScopeAudit"]=passageAudit;
            result["compoundAudit"]=compoundAudit;
            int center=(h/2)*w+w/2;var centerGround=Flood(dry,w,h,new[]{center});
            result["centerDry"]=dry[center];result["centerReachesSouth"]=Enumerable.Range(0,w).Any(i=>centerGround[i]);result["centerReachesAnyEdge"]=Edge(centerGround,w,h);result["centerGroundCells"]=centerGround.Count(v=>v);
            if(kind=="valley-exit" || kind=="straight-canyon" || kind=="bent-canyon")
            {
                int[] starts=kind=="valley-exit"?new[]{center}:Enumerable.Range(h/2-3,7).Select(z=>z*w+4).ToArray();
                foreach(int width in new[]{1,8})
                {
                    var wide=Footprint(dry,w,h,width);var reached=Flood(wide,w,h,starts);
                    result["dryConnectionWidth"+width]=kind=="valley-exit"?Enumerable.Range(0,w).Any(x=>reached[4*w+x]):Enumerable.Range(h/2-5,11).Any(z=>reached[z*w+w-5]);
                    if(kind=="bent-canyon")result["waypointsReachedWidth"+width]=new[]{(int)(.7*h)*w+(int)(.35*w),(int)(.3*h)*w+(int)(.65*w)}.All(i=>reached[i]);
                }
            }
            if(kind=="cave-entrance")result["centralCoveredOpenCells"]=Enumerable.Range(110,30).Sum(z=>Enumerable.Range(110,30).Count(x=>dry[z*w+x] && roof[z*w+x]));
            return result;
        }
        // Independent rectangular pawn-footprint reachability; does not use production distance/mask helpers.
        static bool[] Footprint(bool[] dry,int w,int h,int size)
        {
            if(size==1)return dry;var result=new bool[dry.Length];int lo=(size-1)/2,hi=size/2;
            for(int z=lo;z<h-hi;z++)for(int x=lo;x<w-hi;x++){bool ok=true;for(int dz=-lo;dz<=hi && ok;dz++)for(int dx=-lo;dx<=hi;dx++)if(!dry[(z+dz)*w+x+dx]){ok=false;break;}result[z*w+x]=ok;}
            return result;
        }
        static bool[] Flood(bool[] allowed,int w,int h,IEnumerable<int> seeds)
        {
            var visited=new bool[allowed.Length];var q=new Queue<int>();foreach(int i in seeds)if(i>=0 && i<allowed.Length && allowed[i]){visited[i]=true;q.Enqueue(i);}
            while(q.Count>0){int i=q.Dequeue(),x=i%w,z=i/w;foreach(int next in new[]{x>0?i-1:-1,x+1<w?i+1:-1,z>0?i-w:-1,z+1<h?i+w:-1})if(next>=0 && allowed[next] && !visited[next]){visited[next]=true;q.Enqueue(next);}}
            return visited;
        }
        static bool Edge(bool[] mask,int w,int h)=>Enumerable.Range(0,w).Any(x=>mask[x] || mask[(h-1)*w+x]) || Enumerable.Range(0,h).Any(z=>mask[z*w] || mask[z*w+w-1]);
    }
}
