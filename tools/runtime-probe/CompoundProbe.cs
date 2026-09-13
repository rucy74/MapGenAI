using System;
using System.Collections;
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
    static class CompoundProbe
    {
        static object Invoke(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
        public static void Run(string output,string responses,Action<bool,string> check)
        {
            var tiles=Find.WorldGrid.Tiles.Where(t=>t.PrimaryBiome==BiomeDefOf.TemperateForest && !t.WaterCovered && t.hilliness==Hilliness.Flat && t.Mutators.Count==0 &&
                !FeaturePolicy.HasRiver(t) && FeaturePolicy.WaterNeighbors(t).Count==0 && !Find.WorldObjects.AnyMapParentAt(t.tile)).Take(8).ToList();
            if(tiles.Count<8)throw new InvalidOperationException("Need eight flat compound fixtures");
            int target=tiles[0].tile;Find.WorldSelector.SelectedTile=target;
            var states=new Dictionary<string,TileMapState>();
            foreach(var path in Directory.GetFiles(responses,"compound-*-response.json").OrderBy(p=>p))
            {
                string id=Path.GetFileName(path).Replace("-response.json","");
                var before=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-before.json")));
                var expected=MapStateCodec.Deserialize(File.ReadAllText(Path.Combine(responses,id+"-after.json")));
                MapGenParams.RestoreSnapshot(before,target);string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));
                var dialog=new Dialog_TextToMap();string response=File.ReadAllText(path);
                var undo=(ICollection)dialog.GetType().GetField("_paramStack",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialog);
                Invoke(dialog,"HandleResponse",response);
                bool generate=MapGenAI.LLM.ProviderResponse.Command(response).GetString("action")=="generate";
                check(MapStateCodec.Serialize(expected)==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && undo.Count==(generate?1:0),id+": actual dialog matches sequential provider state");
                if(generate){states[id]=MapGenParams.CaptureState(target);Invoke(dialog,"DoUndo");}
                check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)),id+": actual Undo/ask preserves previous state");dialog.PostClose();
            }
            check(states.Count==6,"six sequential changes and two policy explanations replayed");
            int index=0;
            foreach(var entry in states)
            {
                var tile=tiles[index++];target=tile.tile;MapGenParams.RestoreSnapshot(entry.Value,target);
                string saved=MapStateCodec.Serialize(MapGenParams.CaptureState(target));var baseline=TileWorldSnapshot.Capture(tile);
                var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
                var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
                var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,null);
                generator.genSteps=new List<GenStepDef>(source.genSteps);generator.genSteps.Add(new GenStepDef{defName="CompoundCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id=entry.Key}});
                var map=MapGenerator.GenerateMap(new IntVec3(250,1,250),parent,generator);
                var result=AuthoringGeneration.Latest(target,MapGenParams.CaptureState(target));
                var counts=map.AllCells.GroupBy(c=>map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(c)).defName).ToDictionary(g=>g.Key,g=>g.Count());
                int step=int.Parse(entry.Key.Substring(entry.Key.Length-2));string material=step<=3?"LavaDeep":"CooledLava";
                check(result!=null && result.issues.Count==0 && result.placements.Count==entry.Value.structures.Sum(p=>p.count),entry.Key+": full-map requested count without placement failure");
                check(counts.TryGetValue(material,out var filled) && filled>2000 && (step<=3 || !counts.ContainsKey("LavaDeep")),entry.Key+": actual requested material after cumulative edits");
                foreach(var p in result.placements)
                {
                    float cx=step==1?.5f:.62f,cz=step==1?.5f:.58f;bool inside=true;
                    for(int z=p.rect.z;z<p.rect.z+p.rect.height;z++)for(int x=p.rect.x;x<p.rect.x+p.rect.width;x++)
                        inside &= Math.Pow(x/250f-cx,2)+Math.Pow(z/250f-cz,2)<=.14f*.14f && !map.terrainGrid.TerrainAt(new IntVec3(x,0,z)).dangerous;
                    check(inside && p.spawnedWalls==p.walls && p.walls>0 && p.wallCells.All(c=>new IntVec3(c[0],0,c[1]).GetEdifice(map)?.def==ThingDefOf.Wall),entry.Key+": full footprint and actual walls follow island");
                }
                check(saved==MapStateCodec.Serialize(MapGenParams.CaptureState(target)) && baseline.mutators.SequenceEqual(tile.Mutators.Select(m=>m.defName)) && !GenerationContext.Active,entry.Key+": plan/world preserved and scope disposed");
                File.WriteAllText(Path.Combine(output,entry.Key+"-observation.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"result",result},{"terrain",counts}}));
            }
        }
    }
}
