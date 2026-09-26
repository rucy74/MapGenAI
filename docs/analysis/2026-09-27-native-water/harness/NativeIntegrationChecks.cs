using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using Verse;

namespace MapGenAI.NativeVisualProbe
{
    // Real Scribe and engine palette dispatch. Deliberately does not claim that the
    // complete SDF/AuthoringGeneration/LandscapeBlend pipeline painted these sentinels.
    public static class NativeIntegrationChecks
    {
        public sealed class Envelope:IExposable
        {
            public TileMapState state;
            public void ExposeData()=>Scribe_Deep.Look(ref state,"state");
        }
        public static bool? Run(Map map,string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            string path=Path.Combine(outputDirectory,"native-integration-checks.json");
            var profile=typeof(ElevationShape).GetField("water_profile",BindingFlags.Public|BindingFlags.Instance);
            if(profile==null)
            {
                Save(path,new {supported=false,skipped=true,reason="Loaded product predates water_profile; no integration checks executed."});
                return null;
            }
            int failed=0;var checks=new List<object>();
            Action<bool,string,object> check=(ok,name,evidence)=>
            {checks.Add(new Dictionary<string,object>{{"ok",ok},{"name",name},{"evidence",evidence}});if(!ok)failed++;};
            try { VerifyScribe(outputDirectory,profile,check); }
            catch(Exception error){check(false,"Scribe checks completed",error.ToString());}
            try { VerifyPalette(map,check); }
            catch(Exception error){check(false,"Palette helper checks completed",error.ToString());}
            Save(path,new Dictionary<string,object>{{"supported",true},{"skipped",false},{"productAssembly",typeof(ElevationShape).Assembly.Location},
                {"passed",checks.Count-failed},{"failed",failed},{"checks",checks},
                {"scope","Actual game Scribe roundtrip, legacy missing-field load, and MapGenUtility biome/mutator palette selection. Palette sentinels are not registered or painted; these checks do not test full SDF rasterization, water-depth appearance, shoreline placement or aesthetic quality. Biome fields and the exact tile mutator-list reference are restored in finally."}});
            return failed==0;
        }
        static void VerifyScribe(string output,FieldInfo profile,Action<bool,string,object> check)
        {
            if(Scribe.mode!=LoadSaveMode.Inactive)throw new InvalidOperationException("Scribe is already active; refuse to interrupt an existing save/load.");
            const string add="{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"native_pond\",\"type\":\"composite\",\"edge_roughness\":\"medium\",\"shapes\":[{\"id\":\"lake\",\"prim\":\"ellipse\",\"center\":[0.5,0.5],\"w\":0.3,\"h\":0.2}],\"compose\":[{\"op\":\"add\",\"s\":\"lake\",\"fill\":\"water\",\"e\":0.05}]}}]}";
            var natural=MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(add)));
            check((string)profile.GetValue(natural.elevationShapes.Single())=="native","Actual new rough freshwater addition opts into native profile",ShapeEdits.Describe(natural.elevationShapes));
            var original=MapStateCodec.Serialize(natural);
            var loaded=RoundTrip(natural,Path.Combine(output,"integration-native-natural.xml"));
            check(original==MapStateCodec.Serialize(loaded),"Actual Scribe preserves native profile and complete authored state",MapStateCodec.Serialize(loaded));
            var off=MapStateEditor.Merge(loaded,MapParameterParser.Parse(SimpleJson.Parse("{\"shape_ops\":[{\"op\":\"update\",\"id\":\"native_pond\",\"changes\":{\"details\":\"none\"}}]}")));
            var expected=ShapeEdits.ToObject(loaded.elevationShapes.Single());expected["details"]="none";
            check((string)profile.GetValue(off.elevationShapes.Single())=="native" && SimpleJson.Serialize(expected)==SimpleJson.Serialize(ShapeEdits.ToObject(off.elevationShapes.Single())),
                "details:none retains native profile and every authored shape field",ShapeEdits.Describe(off.elevationShapes));
            var offLoaded=RoundTrip(off,Path.Combine(output,"integration-native-none.xml"));
            check(MapStateCodec.Serialize(off)==MapStateCodec.Serialize(offLoaded),"Actual Scribe preserves surface-off state without changing water profile",ShapeEdits.Describe(offLoaded.elevationShapes));
            var legacy=natural.Clone();profile.SetValue(legacy.elevationShapes.Single(),"legacy");
            var legacyLoaded=RoundTrip(legacy,Path.Combine(output,"integration-explicit-legacy.xml"));
            check(MapStateCodec.Serialize(legacy)==MapStateCodec.Serialize(legacyLoaded),"Actual Scribe preserves explicit legacy opt-out",ShapeEdits.Describe(legacyLoaded.elevationShapes));

            var xml=new XmlDocument();xml.Load(Path.Combine(output,"integration-native-natural.xml"));
            var nodes=xml.SelectNodes("//water_profile");int removed=nodes.Count;
            foreach(XmlNode node in nodes)node.ParentNode.RemoveChild(node);
            string oldPath=Path.Combine(output,"integration-missing-profile.xml");xml.Save(oldPath);
            var missing=Load(oldPath);var expectedMissing=natural.Clone();profile.SetValue(expectedMissing.elevationShapes.Single(),null);
            check(removed==1 && MapStateCodec.Serialize(expectedMissing)==MapStateCodec.Serialize(missing),"Actual Scribe missing-profile save remains legacy without upgrading",new {removed,state=MapStateCodec.Serialize(missing)});
            check(original==MapStateCodec.Serialize(natural),"Scribe and surface-toggle checks preserve their input state",null);
        }
        static TileMapState RoundTrip(TileMapState state,string path)
        {
            var box=new Envelope{state=state};
            try{Scribe.saver.InitSaving(path,"NativeWaterIntegration");Scribe_Deep.Look(ref box,"fixture");Scribe.saver.FinalizeSaving();}
            finally{if(Scribe.mode!=LoadSaveMode.Inactive)Scribe.ForceStop();}
            return Load(path);
        }
        static TileMapState Load(string path)
        {
            Envelope box=null;
            try{Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref box,"fixture");Scribe.loader.FinalizeLoading();return box.state;}
            finally{if(Scribe.mode!=LoadSaveMode.Inactive)Scribe.ForceStop();}
        }
        static void VerifyPalette(Map map,Action<bool,string,object> check)
        {
            if(map==null)throw new ArgumentNullException(nameof(map));
            var cell=map.Center;var biome=map.BiomeAt(cell);var tile=map.TileInfo;
            var savedDeep=biome.waterDeepTerrain;var savedShallow=biome.waterShallowTerrain;
            var savedLake=biome.lakeBeachTerrain;var savedMud=biome.mudTerrain;var savedRiver=biome.riverbankTerrain;
            var savedMutators=tile.mutatorsNullable;var savedTerrain=map.terrainGrid.TerrainAt(cell);
            TerrainDef deep=Sentinel("Deep"),shallow=Sentinel("Shallow"),lake=Sentinel("LakeBeach"),mud=Sentinel("Mud"),river=Sentinel("Riverbank");
            TerrainDef overrideLake=Sentinel("OverrideLakeBeach"),overrideMud=Sentinel("OverrideMud"),overrideRiver=Sentinel("OverrideRiverbank");
            try
            {
                // Controlled post-generation probe only: no OnAddedToTile callbacks, workers,
                // registry changes or modification of existing mutator definitions.
                tile.mutatorsNullable=new List<TileMutatorDef>();
                biome.waterDeepTerrain=deep;biome.waterShallowTerrain=shallow;biome.lakeBeachTerrain=lake;biome.mudTerrain=mud;biome.riverbankTerrain=river;
                check(MapGenUtility.DeepFreshWaterTerrainAt(cell,map)==deep && MapGenUtility.ShallowFreshWaterTerrainAt(cell,map)==shallow,
                    "Native freshwater helpers select custom biome deep and shallow definitions",new {deep=MapGenUtility.DeepFreshWaterTerrainAt(cell,map).defName,shallow=MapGenUtility.ShallowFreshWaterTerrainAt(cell,map).defName});
                check(MapGenUtility.LakeshoreTerrainAt(cell,map)==lake && MapGenUtility.MudTerrainAt(cell,map)==mud && MapGenUtility.RiverbankTerrainAt(cell,map)==river,
                    "Native bank helpers select dedicated biome fields outside fertility-patch lists",new {lake=MapGenUtility.LakeshoreTerrainAt(cell,map).defName,mud=MapGenUtility.MudTerrainAt(cell,map).defName,riverbank=MapGenUtility.RiverbankTerrainAt(cell,map).defName});
                tile.mutatorsNullable.Add(new TileMutatorDef{defName="MapGenAI_ProbeOnly_Palette",overrideLakeBeachTerrain=overrideLake,overrideMudTerrain=overrideMud,overrideRiverbankTerrain=overrideRiver});
                check(MapGenUtility.LakeshoreTerrainAt(cell,map)==overrideLake && MapGenUtility.MudTerrainAt(cell,map)==overrideMud && MapGenUtility.RiverbankTerrainAt(cell,map)==overrideRiver,
                    "Native bank helpers honor mutator overrides ahead of biome defaults",new {lake=MapGenUtility.LakeshoreTerrainAt(cell,map).defName,mud=MapGenUtility.MudTerrainAt(cell,map).defName,riverbank=MapGenUtility.RiverbankTerrainAt(cell,map).defName});
                check(MapGenUtility.DeepFreshWaterTerrainAt(cell,map)==deep && MapGenUtility.ShallowFreshWaterTerrainAt(cell,map)==shallow,
                    "Bank override does not replace biome water palette",null);
                tile.mutatorsNullable.Clear();biome.waterDeepTerrain=null;biome.waterShallowTerrain=null;biome.lakeBeachTerrain=null;biome.mudTerrain=null;biome.riverbankTerrain=null;
                check(MapGenUtility.DeepFreshWaterTerrainAt(cell,map)==TerrainDefOf.WaterDeep && MapGenUtility.ShallowFreshWaterTerrainAt(cell,map)==TerrainDefOf.WaterShallow && MapGenUtility.LakeshoreTerrainAt(cell,map)==TerrainDefOf.Sand && MapGenUtility.MudTerrainAt(cell,map)==TerrainDefOf.Mud && MapGenUtility.RiverbankTerrainAt(cell,map)==TerrainDefOf.Riverbank,
                    "Native helpers retain their actual missing-field fallbacks",null);
            }
            finally
            {
                biome.waterDeepTerrain=savedDeep;biome.waterShallowTerrain=savedShallow;biome.lakeBeachTerrain=savedLake;biome.mudTerrain=savedMud;biome.riverbankTerrain=savedRiver;
                tile.mutatorsNullable=savedMutators;
            }
            check(biome.waterDeepTerrain==savedDeep && biome.waterShallowTerrain==savedShallow && biome.lakeBeachTerrain==savedLake && biome.mudTerrain==savedMud && biome.riverbankTerrain==savedRiver && ReferenceEquals(tile.mutatorsNullable,savedMutators) && map.terrainGrid.TerrainAt(cell)==savedTerrain,
                "Palette helper probe restores biome fields and exact tile mutator list without painting terrain",null);
        }
        static TerrainDef Sentinel(string name)=>new TerrainDef{defName="MapGenAI_ProbeOnly_"+name};
        static void Save(string path,object data)=>File.WriteAllText(path,SimpleJson.Serialize(data));
    }
}
