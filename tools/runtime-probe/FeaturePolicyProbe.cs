using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class FeaturePolicyProbe
    {
        static Action<bool,string> verify;
        static void Patch(int tile,string json) => MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse(json)),tile);
        static TileMutatorDef Def(string name) => DefDatabase<TileMutatorDef>.GetNamed(name);
        static object Invoke(object obj,string method,params object[] args) => obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);
        static string Features(Tile tile) => string.Join(",",tile.Mutators.Select(d=>d.defName).OrderBy(n=>n));
        // FinalizeInit can freeze the generated river. A ThinIce surface does not remove its water/flow.
        static TerrainDef GeneratedTerrain(Map map,IntVec3 cell) => map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(cell));
        static string Links(SurfaceTile tile) => tile.Rivers == null ? "" : string.Join(",",tile.Rivers.Select(r=>(int)r.neighbor+":"+r.river.defName));
        static void Reject(int target,string json,Action<bool,string> check,string label)
        {
            string before=MapStateCodec.Serialize(MapGenParams.CaptureState(target));string features=Features(Find.WorldGrid[target]);
            bool rejected=false;try{Patch(target,json);}catch(FormatException){rejected=true;}
            check(rejected && before==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && features==Features(Find.WorldGrid[target]),label+": atomic rejection");
        }
        static void Prompt(int target,string output,string id)
        {
            MapGenParams.LoadFromTile(target);
            string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{target});
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(MapGenParams.CaptureState(target)));
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIFeatureResponses",out var responses))
            {
                string response=File.ReadAllText(Path.Combine(responses,id+"-response.json"));
                var command=MapGenAI.LLM.ProviderResponse.Command(response);string action=command.GetString("action");
                string before=MapStateCodec.Serialize(MapGenParams.CaptureState(target)),features=Features(Find.WorldGrid[target]);
                Find.WorldSelector.SelectedTile=target;var dialog=new Dialog_TextToMap();
                var history=(System.Collections.ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                Invoke(dialog,"HandleResponse",response);
                var after=MapGenParams.CaptureState(target);
                if(action=="generate")
                {
                    bool intended=id=="delta-only"?Find.WorldGrid[target].Mutators.Any(d=>d.defName=="River")&&!Find.WorldGrid[target].Mutators.Contains(Def("RiverDelta")):
                        id=="restore-delta"?Find.WorldGrid[target].Mutators.Contains(Def("RiverDelta")):
                        id=="inland-lake"?Find.WorldGrid[target].Mutators.Contains(Def("Lake")):
                        id=="desert-oasis"&&Find.WorldGrid[target].Mutators.Contains(Def("Oasis"));
                    verify(history.Count==1 && intended,"live Gemini replay "+id+": actual dialog and world features match request");
                    Invoke(dialog,"DoUndo");
                }
                else verify(action=="ask" && history.Count==0,"live Gemini replay "+id+": explanation without applying changes");
                verify(before==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && features==Features(Find.WorldGrid[target]),"live Gemini replay "+id+": Undo or ask preserves initial state");
                dialog.PostClose();
            }
        }

        public static void Generate(string output,Action<bool,string> check)
        {
            verify=check;
            var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome?.canBuildBase==true && !t.WaterCovered && !Find.WorldObjects.AnyMapParentAt(t.tile)).ToList();
            File.WriteAllText(Path.Combine(output,"world-seed.txt"),Find.World.info.seedString);
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIDeltaDiagnostics",out _))
            {
                var rows=new List<object>();bool all=true,sawFrozenRiver=false;
                foreach(var t in tiles.Where(t=>FeaturePolicy.UnavailableReason(Def("RiverDelta"),t)==null).Take(12))
                {
                    int id=t.tile;var original=TileWorldSnapshot.Capture(t);
                    float originalTemperature=t.temperature;bool coldFixture=rows.Count==0;if(coldFixture)t.temperature=-100f;
                    Patch(id,"{\"mutators\":[\"RiverDelta\"],\"coast_direction\":\"east\",\"river\":{\"direction_angle\":90}}");
                    var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=id;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                    var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,DefDatabase<MapGeneratorDef>.GetNamed("Base_Player"));
                    var terrain=map.AllCells.GroupBy(c=>map.terrainGrid.TerrainAt(c).defName).ToDictionary(g=>g.Key,g=>(object)g.Count());
                    int riverCount=map.AllCells.Count(c=>GeneratedTerrain(map,c).IsRiver),nodes=map.waterInfo.riverGraph?.Count??0;
                    bool ok=riverCount>0&&nodes>1;all&=ok;
                    int surfaceRiver=map.AllCells.Count(c=>map.terrainGrid.TerrainAt(c).IsRiver);sawFrozenRiver|=riverCount>surfaceRiver;
                    rows.Add(new Dictionary<string,object>{{"tile",id},{"biome",t.PrimaryBiome.defName},{"coldFixture",coldFixture},{"features",t.Mutators.Select(d=>d.defName).ToList()},{"terrain",terrain},{"riverCells",riverCount},{"surfaceRiverCells",surfaceRiver},{"nodes",nodes},{"ok",ok}});
                    File.WriteAllText(Path.Combine(output,"delta-diagnostics.json"),SimpleJson.Serialize(rows));MapGenParams.ClearTile(id);
                    t.temperature=originalTemperature;
                }
                check(all && rows.Count==12,"twelve coastal river delta generations with direction overrides");
                check(sawFrozenRiver,"frozen river fixture retains underlying river terrain and flow");return;
            }
            var inland=tiles.First(t=>!FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && FeaturePolicy.UnavailableReason(Def("Lake"),t)==null);
            var river=tiles.First(t=>FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0);
            var delta=tiles.First(t=>FeaturePolicy.UnavailableReason(Def("RiverDelta"),t)==null);
            var oasis=tiles.First(t=>FeaturePolicy.WaterNeighbors(t).Count==0 && FeaturePolicy.UnavailableReason(Def("Oasis"),t)==null);
            bool vle=DefDatabase<TileMutatorDef>.GetNamedSilentFail("VEE_CraterLake")!=null;
            var sample=new[]{inland,river,delta,oasis};
            var matrix=new List<object>();
            foreach(var tile in sample)
                foreach(var def in DefDatabase<TileMutatorDef>.AllDefsListForReading)
                    matrix.Add(new Dictionary<string,object>{{"tile",(int)tile.tile},{"biome",tile.PrimaryBiome.defName},{"hilliness",tile.hilliness.ToString()},
                        {"riverLinks",tile.Rivers?.Count??0},{"coastalSides",FeaturePolicy.WaterNeighbors(tile).Count},{"def",def.defName},{"categories",def.categories},
                        {"reason",FeaturePolicy.UnavailableReason(def,tile)}});
            File.WriteAllText(Path.Combine(output,"resolved-policy-matrix.json"),SimpleJson.Serialize(matrix));
            check(matrix.Count==sample.Length*DefDatabase<TileMutatorDef>.AllDefsListForReading.Count,"every loaded definition evaluated on four real world contexts");
            check(FeaturePolicy.UnavailableReason(Def("RiverDelta"),inland)!=null,"delta without river is unavailable");
            check(FeaturePolicy.UnavailableReason(Def("RiverDelta"),river)!=null,"inland river delta lacks coastal outlet");
            check(FeaturePolicy.UnavailableReason(Def("Oasis"),inland)!=null,"oasis outside supported biome is unavailable");
            check(FeaturePolicy.UnavailableReason(Def("Lake"),inland)==null && FeaturePolicy.UnavailableReason(Def("Oasis"),oasis)==null,"inland lake and desert oasis available");
            int target=inland.tile;
            Reject(target,"{\"mutators\":[\"Coast\"],\"animal_density\":1.6}",check,"inland coast");
            Reject(target,"{\"mutators\":[\"RiverDelta\"]}",check,"riverless delta");
            Reject(river.tile,"{\"mutators\":[\"RiverDelta\"]}",check,"inland delta");
            Reject(target,"{\"mutators\":[\"Oasis\"]}",check,"wrong-biome oasis");
            var singleRiver=tiles.First(t=>t.Rivers?.Count==1);
            Reject(singleRiver.tile,"{\"mutators\":[\"RiverConfluence\"]}",check,"confluence on single-link river");
            var throughRiver=tiles.First(t=>t.Rivers?.Count>1);
            Reject(throughRiver.tile,"{\"mutators\":[\"Headwater\"]}",check,"headwater would discard extra world links");
            Reject(target,"{\"coast_direction\":\"east\"}",check,"inland shore rotation");
            Reject(delta.tile,"{\"river\":{\"present\":false},\"animal_density\":1.7}",check,"world river removal");
            Reject(delta.tile,"{\"remove_categories\":[\"Coast\"]}",check,"world shore removal");
            var invalidPreset=new TileMapState();invalidPreset.mutators.Add("RiverDelta");bool rejectedPreset=false;
            try{MapGenParams.RestoreSnapshot(invalidPreset,target);}catch(FormatException){rejectedPreset=true;}
            check(rejectedPreset && !MapGenAIWorldComponent.Get().HasState(target),"preset cannot bypass delta requirements");
            rejectedPreset=false;try{MapGenParams.RestoreSnapshot(new TileMapState{riverDirectionAngle=90},target);}catch(FormatException){rejectedPreset=true;}
            check(rejectedPreset && !MapGenAIWorldComponent.Get().HasState(target),"direction-only preset cannot bypass world river requirement");
            Find.WorldSelector.SelectedTile=target;MapGenParams.LoadFromTile(target);
            var dialog=new Dialog_TextToMap();
            var history=(System.Collections.ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
            Invoke(dialog,"HandleResponse","{\"action\":\"generate\",\"params\":{\"mutators\":[\"Coast\"],\"animal_density\":1.6}}");
            check(history.Count==0 && !MapGenAIWorldComponent.Get().HasState(target),"actual dialog rejects entire incompatible request without undo entry");
            dialog.PostClose();
            var oldBaseline=TileWorldSnapshot.Capture(river);
            foreach(var d in river.Mutators.Where(d=>d.categories.Contains("River")).ToList())river.RemoveMutator(d);
            var oldState=new TileMapState();oldState.removeFeatureCategories.Add("River");oldState.animalDensity=1.3f;
            var component=MapGenAIWorldComponent.Get();component.SetBaseline(river.tile,oldBaseline);component.SetLastApplied(river.tile,TileWorldSnapshot.Capture(river));component.SetState(river.tile,oldState);
            MapGenParams.LoadFromTile(river.tile);
            check(river.Mutators.Any(d=>d.defName=="River") && !MapGenParams.CaptureState(river.tile).removeFeatureCategories.Contains("River") && MapGenParams.AnimalDensity==1.3f,"actual legacy stored suppression migrates without losing other edits");
            MapGenParams.ClearTile(river.tile);
            check(new HashSet<string>(oldBaseline.mutators).SetEquals(river.Mutators.Select(d=>d.defName)),"legacy migration Reset restores original river variant");
            Prompt(target,output,"inland-coast");Prompt(target,output,"inland-lake");Prompt(target,output,"wrong-oasis");
            Prompt(river.tile,output,"inland-delta");Prompt(oasis.tile,output,"desert-oasis");
            foreach(string id in new[]{"all-rivers","coast-only","lava-position","ruin-position"})Prompt(delta.tile,output,id);
            if(vle)
            {
                var riverOnly=DefDatabase<TileMutatorDef>.AllDefsListForReading.First(d=>d.defName=="VEE_AlluvialFan");
                check(FeaturePolicy.UnavailableReason(riverOnly,inland)!=null,"VLE worker river prerequisite included");
                Reject(target,"{\"mutators\":[\"VEE_AlluvialFan\"]}",check,"VLE river feature on riverless tile");
            }
            var observations=new List<object>();
            string[] names=vle?new[]{"RiverDelta","plain-river","Lake","Oasis","VEE_CraterLake","coast-variant-removed"}:new[]{"RiverDelta","plain-river","Lake","Oasis","coast-variant-removed"};
            foreach(string name in names)
            {
                string defName=name=="plain-river"?"RiverDelta":name=="coast-variant-removed"?"Fjord":name;
                var tile=tiles.First(t=>!Find.WorldObjects.AnyMapParentAt(t.tile) && FeaturePolicy.UnavailableReason(Def(defName),t)==null &&
                    ((name=="Lake" || name=="Oasis" || name=="VEE_CraterLake")? !FeaturePolicy.HasRiver(t)&&FeaturePolicy.WaterNeighbors(t).Count==0 : true));
                target=tile.tile;var before=TileWorldSnapshot.Capture(tile);string links=Links(tile);
                var neighbors=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(target,neighbors);
                string neighborBefore=string.Join(";",neighbors.Select(n=>Features(n.Tile)));
                // Replace original variants explicitly where their priority would otherwise prevent the edit.
                var removed=tile.Mutators.Where(d=>d!=Def(defName) && (d.categories.Any(Def(defName).categories.Contains)||d.categories.Any(Def(defName).overrideCategories.Contains)))
                    .Select(d=>d.defName).Where(n=>n!="River"&&n!="Coast"&&n!="Lakeshore").ToList();
                Patch(target,SimpleJson.Serialize(new Dictionary<string,object>{{"mutators",new[]{defName}},{"remove_mutators",removed}}));
                var added=MapGenParams.CaptureState(target);
                if(name=="RiverDelta")Prompt(target,output,"delta-only");
                if(name=="coast-variant-removed")
                {
                    Patch(target,"{\"remove_mutators\":[\"Fjord\"]}");
                    check(tile.Mutators.Any(d=>d.defName=="Coast")&&!tile.Mutators.Contains(Def("Fjord")),"removing fjord retains ordinary ocean coast");
                }
                if(name=="plain-river")
                {
                    Patch(target,"{\"remove_mutators\":[\"RiverDelta\"]}");
                    check(tile.Mutators.Any(d=>d.defName=="River")&&!tile.Mutators.Any(d=>d.defName=="RiverDelta"),"delta-only deletion retains ordinary river");
                    MapGenParams.RestoreSnapshot(added,target);check(tile.Mutators.Contains(Def("RiverDelta")),"Undo restores delta on eligible tile");
                    Patch(target,"{\"remove_mutators\":[\"RiverDelta\"]}");
                    Prompt(target,output,"restore-delta");
                }
                if(name=="RiverDelta")
                {
                    var neighborCoast=tiles.First(t=>(int)t.tile!=target && t.IsCoastal);
                    float? oldAngle=Find.World.CoastAngleAt(neighborCoast.tile,BiomeDefOf.Ocean);
                    Patch(target,"{\"coast_direction\":\"east\",\"river\":{\"direction_angle\":90}}");
                    check(Find.World.CoastAngleAt(neighborCoast.tile,BiomeDefOf.Ocean)==oldAngle,"coast rotation leaves other world tile queries unchanged");
                }
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);
                generator.genSteps.Add(new GenStepDef{defName="FeaturePolicyCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=name}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                int water=map.AllCells.Count(c=>GeneratedTerrain(map,c).IsWater),riverCells=map.AllCells.Count(c=>GeneratedTerrain(map,c).IsRiver);
                int ocean=map.AllCells.Count(c=>GeneratedTerrain(map,c).IsOcean),nodes=map.waterInfo.riverGraph?.Count??0;
                File.WriteAllText(Path.Combine(output,name+"-terrain-observation.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"tile",target},{"biome",tile.PrimaryBiome.defName},{"water",water},{"river",riverCells},{"ocean",ocean},{"nodes",nodes},
                    {"terrain",map.AllCells.GroupBy(c=>map.terrainGrid.TerrainAt(c).defName).ToDictionary(g=>g.Key,g=>(object)g.Count())},{"params",MapStateCodec.Serialize(MapGenParams.CaptureState(target))}}));
                if(name=="RiverDelta" || name=="plain-river")check(riverCells>0 && nodes>0,"full generation "+name+" has river terrain and flow");
                else if(name=="coast-variant-removed")check(ocean>0,"full generation retains ocean after fjord removal");
                else check(water>300 && ocean==0 && riverCells==0,"full generation "+name+" has inland water without ocean/world river");
                if(name=="RiverDelta")check(nodes>1,"actual delta has branching flow");
                observations.Add(new Dictionary<string,object>{{"id",name},{"tile",target},{"biome",tile.PrimaryBiome.defName},{"baseline",before.mutators},{"applied",tile.Mutators.Select(d=>d.defName).ToList()},{"waterCells",water},{"riverCells",riverCells},{"oceanCells",ocean},{"riverNodes",nodes}});
                File.WriteAllText(Path.Combine(output,"generation-observations.json"),SimpleJson.Serialize(observations));
                MapGenParams.ClearTile(target);
                check(new HashSet<string>(before.mutators).SetEquals(tile.Mutators.Select(d=>d.defName)),name+": Reset restores baseline");
                check(links==Links(tile) && neighborBefore==string.Join(";",neighbors.Select(n=>Features(n.Tile))),name+": world river links and neighboring features unchanged");
                check(!GenerationContext.Active,name+": generation scope closed");
            }
        }
    }
}
