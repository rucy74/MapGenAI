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
    static class FeatureRemovalProbe
    {
        const string Lake = "{\"elevation_shapes\":[{\"id\":\"lake\",\"type\":\"composite\",\"shapes\":[{\"id\":\"c\",\"prim\":\"circle\",\"center\":[0.5,0.5],\"r\":0.16}],\"compose\":[{\"op\":\"add\",\"s\":\"c\",\"e\":-0.5,\"fill\":\"water\"}]}]}";
        static void Patch(int tile,string json) => MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse(json)),tile);
        static void Invoke(object target,string method,params object[] args) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);

        public static void Generate(string output,Action<bool,string> check)
        {
            // Only the explicitly marked disposable world. Never create or alter world river links.
            var candidates=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome?.canBuildBase==true && !t.WaterCovered && t.IsCoastal
                && t.Rivers?.Any(r=>!r.neighbor.Tile.WaterCovered)==true && !Find.WorldObjects.AnyMapParentAt(t.tile))
                .OrderByDescending(t=>t.Mutators.Any(d=>d.defName=="RiverDelta")).Take(5).ToList();
            check(candidates.Count==5,"five real coastal river tiles available in disposable world");
            var observations=new List<object>();
            string[] modes={"delta","plain-river","river-off","restored","coast-and-river-off"};
            for(int i=0;i<modes.Length;i++)
            {
                var tile=candidates[i];int target=tile.tile;
                bool forced=!tile.Mutators.Any(d=>d.defName=="RiverDelta");
                if(forced)tile.AddMutator(DefDatabase<TileMutatorDef>.GetNamed("RiverDelta"));
                var baseline=TileWorldSnapshot.Capture(tile);
                string riverLinks=Links(tile);
                var neighbors=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(target,neighbors);
                string neighborState=SimpleJson.Serialize(neighbors.Select(t=>new Dictionary<string,object>{{"tile",(int)t},{"features",Find.WorldGrid[t].Mutators.Select(d=>d.defName).ToList()}}).ToList());
                Patch(target,Lake);
                var before=MapGenParams.CaptureState(target);
                Find.WorldSelector.SelectedTile=target;MapGenParams.LoadFromTile(target);
                var dialog=new Dialog_TextToMap();
                string[] cases={"delta-only","all-rivers","restore-rivers","coast-only","lava-position","ruin-position"};
                foreach(string id in cases)
                {
                    if(i!=0)break;
                    if(id=="restore-rivers")Patch(target,"{\"river\":{\"present\":false}}");
                    CapturePrompt(target,output,id);
                    if(id=="restore-rivers")MapGenParams.RestoreSnapshot(before,target);
                }
                Invoke(dialog,"HandleResponse","{\"action\":\"generate\",\"params\":{\"river\":{\"present\":false}}}");
                check(!tile.Mutators.Any(d=>d.categories.Contains("River")),modes[i]+": actual dialog suppresses river family");
                check(MapGenParams.CaptureState(target).elevationShapes.Single().id=="lake",modes[i]+": dialog preserves authored lake");
                Invoke(dialog,"DoUndo");
                check(MapStateCodec.Serialize(before)==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && tile.Mutators.Any(d=>d.defName=="RiverDelta"),modes[i]+": actual dialog Undo restores delta and state");
                dialog.PostClose();
                if(i==1)Patch(target,"{\"remove_mutators\":[\"RiverDelta\"]}");
                if(i==2 || i==3 || i==4)Patch(target,"{\"river\":{\"present\":false}}");
                if(i==3)Patch(target,"{\"restore_categories\":[\"River\"]}");
                if(i==4)Patch(target,"{\"remove_categories\":[\"Coast\"]}");
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);
                generator.genSteps.Add(new GenStepDef{defName="FeatureProbeCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=modes[i]}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                int river=map.AllCells.Count(c=>map.terrainGrid.TerrainAt(c).IsRiver);
                int ocean=map.AllCells.Count(c=>map.terrainGrid.TerrainAt(c).IsOcean);
                int lake=0;for(int x=115;x<=135;x++)for(int z=115;z<=135;z++)if(map.terrainGrid.TerrainAt(new IntVec3(x,0,z)).IsWater)lake++;
                int nodes=map.waterInfo.riverGraph?.Count??0;
                check((i==2 || i==4)?river==0&&nodes==0:river>0&&nodes>0,modes[i]+": full generation has intended river terrain and graph");
                check(lake==441,modes[i]+": full generation retains all 441 central lake cells");
                if(i==4)check(ocean==0,"coast-and-river-off: no ocean terrain after generic Coast removal");
                if(i==1)check(tile.Mutators.Any(d=>d.defName=="River") && !tile.Mutators.Any(d=>d.defName=="RiverDelta"),"plain-river: only ordinary river remains");
                if(i==0 || i==3)check(nodes>1,modes[i]+": actual delta has multiple river segments");
                observations.Add(new Dictionary<string,object>{{"id",modes[i]},{"tile",target},{"deltaFixtureAdded",forced},{"baseline",baseline.mutators},{"applied",tile.Mutators.Select(d=>d.defName).ToList()},{"riverCells",river},{"riverNodes",nodes},{"oceanCells",ocean},{"lakeCells",lake}});
                File.WriteAllText(Path.Combine(output,"feature-observations.json"),SimpleJson.Serialize(observations));
                MapGenParams.ClearTile(target);
                check(new HashSet<string>(baseline.mutators).SetEquals(tile.Mutators.Select(d=>d.defName)),modes[i]+": Reset restores original fixture features");
                check(Links(tile)==riverLinks,modes[i]+": world river links unchanged");
                check(neighborState==SimpleJson.Serialize(neighbors.Select(t=>new Dictionary<string,object>{{"tile",(int)t},{"features",Find.WorldGrid[t].Mutators.Select(d=>d.defName).ToList()}}).ToList()),modes[i]+": neighboring features unchanged");
                check(!GenerationContext.Active,modes[i]+": generation scope closed");
            }
        }
        static string Links(SurfaceTile tile) => SimpleJson.Serialize(tile.Rivers.Select(r=>new Dictionary<string,object>{{"neighbor",(int)r.neighbor},{"river",r.river.defName}}).ToList());
        static void CapturePrompt(int tile,string output,string id)
        {
            string prompt=(string)typeof(Dialog_TextToMap).GetMethod("BuildSystemPrompt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{tile});
            File.WriteAllText(Path.Combine(output,id+"-prompt.txt"),prompt);
            File.WriteAllText(Path.Combine(output,id+"-before.json"),MapStateCodec.Serialize(MapGenParams.CaptureState(tile)));
        }
    }
}
