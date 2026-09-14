using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class FeatureFeedbackProbe
    {
        static readonly string[] Combo={"VEE_VolcanicRichSoil","VEE_FertileRains","HotSprings","VEE_SkygazingSpot","VEE_FrequentAuroras","SunnyMutator","VEE_PlantLife_Overgrown"};
        static readonly List<string> checks=new List<string>();static string folder;static bool pending;static DateTime deadline;static int previewTarget;
        static TileMapState previewState;
        static void Check(bool pass,string message){checks.Add((pass?"PASS: ":"FAIL: ")+message);if(!pass)throw new InvalidOperationException(message);Log.Message("[FeatureFeedbackProbe] "+message);}
        static void Finish(Exception error=null){pending=false;File.WriteAllText(Path.Combine(folder,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",error==null},{"checks",checks},{"error",error?.ToString()}}));UnityEngine.Application.Quit();}
        public static void Tick(){if(pending && DateTime.UtcNow>deadline)Finish(new TimeoutException("Hot spring preview timeout"));}
        static object Invoke(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        static void Prompt(int target,string id)
        {
            MapGenParams.ClearTile(target);Find.WorldSelector.SelectedTile=target;MapGenParams.LoadFromTile(target);
            string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{target});
            File.WriteAllText(Path.Combine(folder,id+"-prompt.txt"),prompt);File.WriteAllText(Path.Combine(folder,id+"-before.json"),MapStateCodec.Serialize(MapGenParams.CaptureState(target)));
        }
        public static void Run(string output,string responses)
        {
            folder=output;bool vle=DefDatabase<TileMutatorDef>.GetNamedSilentFail("VEE_VolcanicRichSoil")!=null;
            var tiles=Find.WorldGrid.Tiles.Where(t=>!t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 && !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).ToList();
            var temperate=tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest).Take(10).ToList();var tropical=tiles.Where(t=>t.PrimaryBiome.defName=="TropicalRainforest").Take(3).ToList();
            Check(temperate.Count==10 && tropical.Count==3,"real flat temperate/tropical fixture tiles");
            var hot=DefDatabase<TileMutatorDef>.GetNamed("HotSprings");
            Check(hot.minHilliness==Hilliness.Mountainous && !hot.biomeWhitelist.Contains(tropical[0].PrimaryBiome),"native natural-spawn preferences remain unchanged");
            Check(FeaturePolicy.UnavailableReason(hot,temperate[0])==null && FeaturePolicy.UnavailableReason(hot,tropical[0])==null,"explicit hot springs supported on flat temperate and tropical tiles");
            var river=Find.WorldGrid.Tiles.First(t=>FeaturePolicy.HasRiver(t));var coast=Find.WorldGrid.Tiles.First(t=>FeaturePolicy.WaterNeighbors(t).Count>0 && !t.WaterCovered);
            Check(FeaturePolicy.UnavailableReason(hot,river)!=null && FeaturePolicy.UnavailableReason(hot,coast)!=null,"hot springs keep river/shore conflict restrictions");
            Check(FeaturePolicy.UnavailableReason(DefDatabase<TileMutatorDef>.GetNamed("RiverDelta"),temperate[0])!=null,"riverless delta still rejects");
            foreach(string id in new[]{"hot-temperate","missing","followup","combo","rich-fill"})Prompt(temperate[0].tile,id);Prompt(tropical[0].tile,"hot-tropical");
            var definitions=Combo.Select(name=>{var def=DefDatabase<TileMutatorDef>.GetNamedSilentFail(name);return new Dictionary<string,object>{{"id",name},{"loaded",def!=null},{"label",def?.label},{"source",def?.modContentPack?.Name},{"reason",def==null?"not loaded":FeaturePolicy.UnavailableReason(def,temperate[0])}};}).ToList();
            File.WriteAllText(Path.Combine(folder,"feature-sources.json"),SimpleJson.Serialize(definitions));
            Check(definitions.Count(d=>(bool)d["loaded"])==(vle?7:2),"requested feature provenance follows active mod list");
            if(vle)Check(TerrainMaterials.Resolve("VEE_VolcanicSoilRich").defName=="VEE_VolcanicSoilRich","loaded rich volcanic soil is a supported fill material");
            var cases=new Dictionary<string,Tuple<int,TileMapState>>();
            var hotState=MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse("{\"mutators\":[\"HotSprings\"]}")));
            cases["hot-temperate"]=Tuple.Create((int)temperate[1].tile,hotState);cases["hot-tropical"]=Tuple.Create((int)tropical[1].tile,hotState);
            if(vle){var combo=new TileMapState();combo.mutators.AddRange(Combo);cases["combo"]=Tuple.Create((int)temperate[2].tile,combo);}
            int n=3;
            if(!string.IsNullOrEmpty(responses))foreach(var path in Directory.GetFiles(responses,"*-response.json").OrderBy(p=>p))
            {
                string id=Path.GetFileName(path).Replace("-response.json","");int target=id.Contains("tropical")?tropical[2].tile:temperate[n++].tile;
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-before.json")));var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-after.json")));
                MapGenParams.RestoreSnapshot(before,target);Find.WorldSelector.SelectedTile=target;string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var dialog=new Dialog_TextToMap();
                string response=File.ReadAllText(path);Invoke(dialog,"HandleResponse",response);var after=MapGenParams.CaptureState(target);var undo=(ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                bool generated=ProviderResponse.Command(response).GetString("action")=="generate";
                Check(MapStateCodec.Serialize(after)==MapStateCodec.Serialize(expected)&&undo.Count==(generated?1:0),id+": actual dialog agrees with provider result");
                if(generated){cases["model-"+id]=Tuple.Create(target,after);Invoke(dialog,"DoUndo");}
                Check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),id+": actual Undo/ask preserves previous state");dialog.PostClose();
            }
            foreach(var entry in cases)
            {
                int target=entry.Value.Item1;var state=entry.Value.Item2;var tile=Find.WorldGrid[target];var original=TileWorldSnapshot.Capture(tile);MapGenParams.RestoreSnapshot(state,target);
                string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                Check(state.mutators.All(name=>tile.Mutators.Any(d=>d.defName==name)),entry.Key+": requested feature IDs actually added together");
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="FeatureFeedbackCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                var counts=map.AllCells.GroupBy(c=>map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)).defName).ToDictionary(g=>g.Key,g=>g.Count());
                if(state.mutators.Contains("HotSprings"))Check(counts.TryGetValue("HotSpring",out int water)&&water>10,entry.Key+": actual native hot-spring water exists on flat land");
                if(state.mutators.Contains("VEE_VolcanicRichSoil")||entry.Key.Contains("rich-fill"))Check(counts.TryGetValue("VEE_VolcanicSoilRich",out int soil)&&soil>100,entry.Key+": actual rich volcanic soil exists");
                if(entry.Key.Contains("rich-fill"))
                {
                    var inner=map.AllCells.Where(c=>Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2)<.19*.19).ToList();
                    // Default compose falloff is .05 beyond the .20 radius, and later native ruins can lay floors.
                    var outer=map.AllCells.Where(c=>Math.Pow(c.x/250.0-.5,2)+Math.Pow(c.z/250.0-.5,2)>.26*.26).ToList();
                    int innerSoil=inner.Count(c=>map.terrainGrid.TerrainAt(c).defName=="VEE_VolcanicSoilRich"),outerSoil=outer.Count(c=>map.terrainGrid.TerrainAt(c).defName=="VEE_VolcanicSoilRich");
                    File.WriteAllText(Path.Combine(output,entry.Key+"-footprint.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"innerCells",inner.Count},{"innerSoil",innerSoil},{"outsideRadius026Soil",outerSoil},{"interpretation","Central radius .20 with default .05 falloff; later native ruins can replace soil with floors. Not an exact area-coverage claim."}}));
                    Check(innerSoil>inner.Count*.9 && outerSoil==0,entry.Key+": soil fills the center and stays within radius plus the default falloff");
                }
                if(state.mutators.Contains("VEE_SkygazingSpot"))Check(map.gameConditionManager.ActiveConditions.Any(c=>c.def.defName=="VEE_SkygazingSpot"),entry.Key+": skygazing game condition is active");
                File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"terrain",counts},{"features",tile.Mutators.Select(d=>d.defName).ToArray()},{"activeConditions",map.gameConditionManager.ActiveConditions.Select(c=>c.def.defName).ToArray()},{"biome",tile.PrimaryBiome.defName},{"hilliness",tile.hilliness.ToString()}}));
                Check(tile.hilliness==original.hilliness && saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target))&&!GenerationContext.Active,entry.Key+": world hilliness and saved plan preserved");
            }
            previewTarget=temperate[9].tile;previewState=vle?cases["combo"].Item2:hotState;MapGenParams.RestoreSnapshot(previewState,previewTarget);pending=true;deadline=DateTime.UtcNow.AddSeconds(60);
            StartPreview();
        }
        static void StartPreview()
        {
            // Capture a local, avoiding a static cached Action<MapPreviewResult> field.
            // Lunar loads MapPreview's component types after RimWorld's initial type scan.
            string previewOutput=folder;
            var request=new MapPreview.MapPreviewRequest(Find.World.info.seedString,previewTarget,new IntVec2(250,250)){UseMinimalMapComponents=true,UseTrueTerrainColors=true};
            MapPreview.MapPreviewGenerator.Init().QueuePreviewRequest(request).Then(result=>{
                try{Check(result.InvalidCells==0,"actual background preview completes for flat hot springs" );var texture=new UnityEngine.Texture2D(250,250);result.CopyToTexture(texture);texture.Apply();File.WriteAllBytes(Path.Combine(previewOutput,"hot-springs-background.png"),UnityEngine.ImageConversion.EncodeToPNG(texture));UnityEngine.Object.Destroy(texture);Check(previewState.mutators.All(n=>Find.WorldGrid[previewTarget].Mutators.Any(d=>d.defName==n)),"preview preserves entire feature combination");Finish();}catch(Exception error){Finish(error);}
            },error=>Finish(error));
        }
    }
}
