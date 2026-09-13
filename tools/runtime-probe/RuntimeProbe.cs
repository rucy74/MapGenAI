using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.MapGen;
using MapGenAI.UI;
using MapGenAI.ImageInput;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace MapGenAI.RuntimeProbe
{
    // Loaded only by the distinct development probe mod in a newly created, marked profile.
    [StaticConstructorOnStartup]
    public static class RuntimeProbe
    {
        static string output;
        static readonly List<string> checks = new List<string>();
        static bool pending;
        static int captureFrame=-1;
        static bool injectFailure;
        static float observedDangerMin=-1;
        static RuntimeProbe()
        {
            if (!GenCommandLine.TryGetCommandLineArg("mapgenAIProbe", out var directory)) return;
            if (!Path.IsPathRooted(directory) || !GenCommandLine.TryGetCommandLineArg("savedatafolder", out var profile)
                || !Path.IsPathRooted(profile) || !File.Exists(Path.Combine(profile,"MAPGENAI_DISPOSABLE")))
                throw new InvalidOperationException("Probe requires an explicitly marked isolated profile");
            output = directory;
            LongEventHandler.ExecuteWhenFinished(Start);
        }

        static void Start()
        {
            Application.runInBackground=true;
            try
            {
                Directory.CreateDirectory(output);
                SaveLoadProbe();
                var catalog = DefDatabase<TileMutatorDef>.AllDefsListForReading.Select(d => new Dictionary<string,object> {
                    {"def",d.defName},{"categories",d.categories},{"overrideCategories",d.overrideCategories},{"priority",d.priority}
                }).ToList();
                File.WriteAllText(Path.Combine(output,"mutator-catalog.json"),SimpleJson.Serialize(catalog));
                pending = true;
                LongEventHandler.QueueLongEvent(() => {
                    try
                    {
                        Root_Play.SetupForQuickTestPlay();
                        Find.GameInitData.mapSize=100;
                        Find.GameInitData.PrepForMapGen();
                        Find.Scenario.PreMapGenerate();
                    }
                    catch(Exception error) {Fail(error);}
                },"Play","MapGenAI disposable test world",true,null);
            }
            catch(Exception error) { Fail(error); }
        }

        static void SaveLoadProbe()
        {
            Require(new MapGenAISettings().GetActiveConfig().SelectedModel=="gemini-3.8-flash","simple mode defaults to Gemini 3.8 Flash");
            var explicitConfig=new MapGenAISettings{useSimpleMode=false,cloudConfigs=new List<ApiConfig>{new ApiConfig{ApiKey="fixture-only",SelectedModel="custom-existing-model"}}};
            Require(explicitConfig.GetActiveConfig().SelectedModel=="custom-existing-model","advanced explicit model choice is preserved");
            var state = MapStateEditor.Merge(null,MapParameterParser.Parse(SimpleJson.Parse("{\"fertility_offset\":0.6,\"straight_river\":true,\"elevation_shapes\":[{\"id\":\"triangle\",\"type\":\"composite\",\"shapes\":[{\"id\":\"p\",\"prim\":\"poly\",\"verts\":[[0.1,0.2],[0.8,0.2],[0.5,0.8]]}],\"compose\":[{\"op\":\"add\",\"s\":\"p\",\"e\":0.8}]}]}")));
            state.imageMap=new ImageMapData{width=3,height=2,cells="MWNSGI",note="이미지 저장 😀"};
            var fixture = new ProbeEnvelope { state=state, component=new MapGenAIWorldComponent(null),settings=new MapGenAISettings{simpleGeminiModel="gemini-2.5-flash"} };
            fixture.component.SetState(42,state);
            fixture.component.SetBaseline(42,new TileWorldSnapshot {mutators=new List<string>{"Caves"},hilliness=Hilliness.LargeHills});
            fixture.component.SetLastApplied(42,new TileWorldSnapshot {mutators=new List<string>{"Caves"},hilliness=Hilliness.LargeHills});
            var path=Path.Combine(output,"state-roundtrip.xml");
            Scribe.saver.InitSaving(path,"MapGenAIProbe");
            Scribe_Deep.Look(ref fixture,"fixture");
            Scribe.saver.FinalizeSaving();
            ProbeEnvelope loaded=null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded,"fixture");
            Scribe.loader.FinalizeLoading();
            Require(loaded!=null && MapStateCodec.Serialize(state)==MapStateCodec.Serialize(loaded.state),"real Scribe composite/coordinates/scalars roundtrip");
            Require(MapStateCodec.Serialize(state)==MapStateCodec.Serialize(loaded.component.GetState(42)),"real WorldComponent state roundtrip");
            Require(loaded.component.GetBaseline(42)?.mutators.Single()=="Caves" && loaded.component.GetLastApplied(42)?.hilliness==Hilliness.LargeHills,"real per-tile baseline and applied metadata roundtrip");
            Require(loaded.settings.GetActiveConfig().SelectedModel=="gemini-2.5-flash","explicit simple model selection survives Scribe roundtrip");

            var legacy=new System.Xml.XmlDocument(); legacy.Load(path);
            foreach(System.Xml.XmlNode node in legacy.SelectNodes("//id|//autoHills|//compositeJson|//imageMap|//tileBaselines|//lastAppliedTiles")) node.ParentNode.RemoveChild(node);
            var legacyPath=Path.Combine(output,"legacy-optional-fields.xml"); legacy.Save(legacyPath);
            loaded=null; Scribe.loader.InitLoading(legacyPath); Scribe_Deep.Look(ref loaded,"fixture"); Scribe.loader.FinalizeLoading();
            Require(loaded.state.elevationShapes[0].id==null && loaded.component.GetBaseline(42)==null,"real Scribe missing optional fields compatibility fixture");
            var damaged=new System.Xml.XmlDocument();damaged.Load(path);
            foreach(System.Xml.XmlNode node in damaged.SelectNodes("//imageMap/cells"))node.InnerText="QWNSGI";
            var damagedPath=Path.Combine(output,"damaged-image.xml");damaged.Save(damagedPath);
            loaded=null;Scribe.loader.InitLoading(damagedPath);Scribe_Deep.Look(ref loaded,"fixture");Scribe.loader.FinalizeLoading();
            Require(loaded.state.imageMap.cells=="NWNSGI" && loaded.component.GetState(42).imageMap.cells=="NWNSGI","real Scribe invalid image label repairs without blocking world load");
        }

        public static void OnStartedNewGame()
        {
            if(!pending) return; pending=false;
            LongEventHandler.ExecuteWhenFinished(RunInWorld);
        }

        static void RunInWorld()
        {
            try
            {
                int tile = Find.CurrentMap.Tile;
                Find.WorldSelector.SelectedTile=tile;
                var dialog=new Dialog_TextToMap();
                string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{tile});
                File.WriteAllText(Path.Combine(output,"production-system-prompt.txt"),prompt);
                var contextState=MapStateEditor.Merge(null,MapParameterParser.Parse(SimpleJson.Parse("{\"elevation_shapes\":[{\"id\":\"west\",\"type\":\"ridge\",\"direction\":\"left\",\"strength\":\"strong\"},{\"id\":\"lake\",\"type\":\"bump\",\"position\":\"bottom_right\",\"size\":\"small\",\"strength\":\"negative_strong\",\"fill\":\"water\"}]}")));
                using(GenerationContext.Enter(tile,contextState))
                {
                    prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{tile});
                    var current=MapGenParams.BuildCurrentParamsText(L10n.IsKorean());
                    Require(prompt.Contains(current),"production prompt includes canonical current state");
                    File.WriteAllText(Path.Combine(output,"production-edit-prompt-template.txt"),prompt.Replace(current,"MAPGENAI_CURRENT_STATE_PLACEHOLDER"));
                }
                var history=(System.Collections.ICollection)typeof(Dialog_TextToMap).GetField("_paramStack",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
                Invoke(dialog,"HandleResponse","{\"action\":\"ask\",\"message\":\"어느 쪽인가요?\"}");
                Require(history.Count==0,"ask response creates no undo entry");
                Invoke(dialog,"HandleResponse","{\"action\":\"generate\",\"params\":{\"animal_density\":1.4}}");
                Require(history.Count==1 && MapGenParams.CaptureState(tile).animalDensity==1.4f,"actual dialog apply creates one undo entry");
                Invoke(dialog,"HandleResponse","{\"action\":\"generate\",\"params\":{}}");
                Invoke(dialog,"HandleResponse","{\"action\":\"generate\",\"params\":{\"elevation_shapes\":[}");
                Require(history.Count==1,"no-op and malformed replies preserve undo history");
                Invoke(dialog,"DoUndo");
                Require(history.Count==0 && !MapGenAIWorldComponent.Get().HasState(tile),"actual dialog undo restores absent initial state");
                var pixels=new ImageMapData{width=2,height=2,cells="MWGI",note="probe"};
                Invoke(dialog,"ApplyImageMap",pixels);
                Require(history.Count==1 && MapGenParams.CaptureState(tile).imageMap.cells=="MWGI","actual dialog image apply creates one undo entry");
                Invoke(dialog,"DoUndo");
                Require(!MapGenAIWorldComponent.Get().HasState(tile),"actual image undo restores absent state");
                var texture=ImageTextureCodec.Preview(pixels);
                try
                {
                    var texturePath=Path.Combine(output,"palette-fixture.png");File.WriteAllBytes(texturePath,ImageConversion.EncodeToPNG(texture));
                    var reloaded=ImageTextureCodec.Load(texturePath);
                    try {Require(ImageTextureCodec.FromPalette(reloaded).cells==pixels.cells,"real Unity PNG load and palette preserve orientation");Require(ImageTextureCodec.ForVision(reloaded).bytes.Length>24,"real Unity image encoding for vision");}
                    finally {UnityEngine.Object.Destroy(reloaded);}
                }
                finally {UnityEngine.Object.Destroy(texture);}
                var asymmetric=new ImageMapData{width=3,height=2,cells="MWGSDI"};var source=ImageTextureCodec.Preview(asymmetric);
                try
                {
                    // Exercise actual Unity transform inverses for every EXIF orientation.
                    for(int orientation=1;orientation<=8;orientation++)
                    {
                        var transformed=ImageTextureCodec.Orient(source,orientation);
                        try
                        {
                            int inverse=orientation==6?8:orientation==8?6:orientation;
                            var restored=ImageTextureCodec.Orient(transformed,inverse);
                            try {Require(ImageTextureCodec.FromPalette(restored).cells==asymmetric.cells,"Unity EXIF orientation "+orientation+" inverse preserves every pixel");}
                            finally {UnityEngine.Object.Destroy(restored);}
                        }
                        finally {UnityEngine.Object.Destroy(transformed);}
                    }
                }
                finally {UnityEngine.Object.Destroy(source);}
                dialog.PostClose();

                var neighbors=new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(tile,neighbors);
                var target=neighbors.First(t=>Find.WorldGrid[t].PrimaryBiome?.canBuildBase==true && !Find.WorldGrid[t].WaterCovered);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                parent.Tile=target; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                string lake="{\"hill_amount\":0.5,\"elevation_shapes\":[{\"id\":\"lake\",\"type\":\"composite\",\"shapes\":[{\"id\":\"c\",\"prim\":\"circle\",\"center\":[0.5,0.5],\"r\":0.2}],\"compose\":[{\"op\":\"add\",\"s\":\"c\",\"e\":-0.5,\"fill\":\"water\"}]}]}";
                MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse(lake)),target);
                var combined=MapGenParams.CaptureState(target);var imageCells=Enumerable.Repeat('N',100*100).ToArray();
                for(int x=10;x<30;x++)for(int z=10;z<30;z++)imageCells[z*100+x]='W';
                for(int x=17;x<23;x++)for(int z=17;z<23;z++)imageCells[z*100+x]='G';
                combined.imageMap=new ImageMapData{width=100,height=100,cells=new string(imageCells),note="lake with island"};combined.dangerDensity=1.8f;MapGenParams.RestoreSnapshot(combined,target);
                MapGenParams.LoadFromTile(tile); // Deliberately select another tile before generation.
                var generated=MapGenerator.GenerateMap(new IntVec3(100,1,100),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
                int inner=0,water=0;
                for(int x=42;x<=58;x++) for(int z=42;z<=58;z++) {inner++; if(generated.terrainGrid.TerrainAt(new IntVec3(x,0,z)).IsWater) water++;}
                Require(water>=inner*.95f,"actual generated lake fills central region despite different UI tile selection");
                Require(!GenerationContext.Active,"generation scope exits after full map generation");
                int imageMatches=0;
                for(int x=10;x<30;x++)for(int z=10;z<30;z++)
                    if(generated.terrainGrid.TerrainAt(new IntVec3(x,0,z)).IsWater==(imageCells[z*100+x]=='W'))imageMatches++;
                Require(imageMatches==400,"generated image lake and soil island match all 400 authored water/land cells");
                bool renderOnly=GenCommandLine.TryGetCommandLineArg("mapgenAIProbeRender",out _);
                if(!renderOnly)FailureCleanupProbe(parent,combined);
                File.WriteAllText(Path.Combine(output,"generated-map-observations.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"tile",(int)target},{"innerCells",inner},{"waterCells",water},{"imageMatches",imageMatches},{"imageTotal",400},{"size",100}}));
                File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",true},{"checks",checks},{"limitations","Disposable world only. Legacy compatibility is a fixture. No live AI calls or visual UI interaction."}}));
                Log.Message("[MapGenAI Probe] PASS "+checks.Count+" checks");
                if(!renderOnly){Application.Quit();return;}
                var imageDialog=new Dialog_ImageMap(combined.imageMap,combined.elevationShapes.Count,_=>false);
                Find.WindowStack.Add(imageDialog);
                typeof(Dialog_ImageMap).GetField("path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(imageDialog,Path.Combine(output,"palette-fixture.png"));
                Invoke(imageDialog,"Load");captureFrame=Time.frameCount;
            }
            catch(Exception error) {Fail(error);}
        }

        static void Invoke(object target,string method,params object[] args) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
        static void FailureCleanupProbe(MapParent parent,TileMapState state)
        {
            var nearby=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(parent.Tile,nearby);
            var testTile=nearby.First(t=>Find.WorldGrid[t].PrimaryBiome?.canBuildBase==true && !Find.WorldGrid[t].WaterCovered && !Find.WorldObjects.AnyMapParentAt(t));
            var fresh=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);fresh.Tile=testTile;fresh.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(fresh);parent=fresh;
            var biome=Find.WorldGrid[parent.Tile].PrimaryBiome;float originalPlants=biome.plantDensity,originalAnimals=biome.animalDensity;
            var danger=(GenStep_Scatterer)DefDatabase<GenStepDef>.GetNamed("ScatterShrines").genStep;var originalRange=danger.countPer10kCellsRange;
            var failedState=state.Clone();failedState.vegetationDensity=.5f;failedState.animalDensity=1.4f;MapGenParams.RestoreSnapshot(failedState,parent.Tile);
            var harmony=new Harmony("mapgenai.disposable.failure.probe");
            var method=AccessTools.Method(typeof(MapGenerator),"GenerateContentsIntoMap");
            var prefix=new HarmonyMethod(typeof(RuntimeProbe).GetMethod(nameof(ThrowInGeneration),BindingFlags.Static|BindingFlags.NonPublic)){priority=Priority.Last};
            harmony.Patch(method,prefix:prefix);injectFailure=true;
            try {MapGenerator.GenerateMap(new IntVec3(100,1,100),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));}
            catch(Exception error){File.WriteAllText(Path.Combine(output,"expected-generation-failure.txt"),error.ToString());}
            finally {harmony.Unpatch(method,HarmonyPatchType.Prefix,harmony.Id);}
            Require(!injectFailure,"failure was injected after generation prefixes");
            Require(!GenerationContext.Active && biome.plantDensity==originalPlants && biome.animalDensity==originalAnimals && danger.countPer10kCellsRange.Equals(originalRange),"actual Harmony finalizers restore generation context and shared definitions on failure");
            Require(Math.Abs(observedDangerMin-originalRange.min*(float)Math.Pow(1.8,4))<.001f,"danger 1.8 changes actual scatter range before restoration");
            MapGenParams.RestoreSnapshot(state,parent.Tile);
        }
        static void ThrowInGeneration()
        {
            if(!injectFailure)return;injectFailure=false;
            observedDangerMin=((GenStep_Scatterer)DefDatabase<GenStepDef>.GetNamed("ScatterShrines").genStep).countPer10kCellsRange.min;
            throw new InvalidOperationException("Expected disposable MapGenAI probe generation failure");
        }
        public static void Update()
        {
            if(captureFrame<0)return;
            int frames=Time.frameCount-captureFrame;
            if(frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(output,"image-dialog.png"));
            if(frames>100){captureFrame=-1;Application.Quit();}
        }
        static void Require(bool condition,string label) {if(!condition) throw new InvalidOperationException(label);checks.Add("PASS: "+label);}
        static void Fail(Exception error)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());
            File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",false},{"checks",checks},{"error",error.ToString()}}));
            Log.Error("[MapGenAI Probe] "+error); Application.Quit();
        }
    }

    public sealed class ProbeEnvelope : IExposable
    {
        public TileMapState state;
        public MapGenAIWorldComponent component;
        public MapGenAISettings settings;
        public void ExposeData() {Scribe_Deep.Look(ref state,"state"); Scribe_Deep.Look(ref component,"component",new object[]{null});Scribe_Deep.Look(ref settings,"settings");}
    }
    public sealed class RuntimeProbeComponent : GameComponent
    {
        public RuntimeProbeComponent(Game game) { }
        public override void StartedNewGame() => RuntimeProbe.OnStartedNewGame();
        public override void GameComponentUpdate() => RuntimeProbe.Update();
    }
}
