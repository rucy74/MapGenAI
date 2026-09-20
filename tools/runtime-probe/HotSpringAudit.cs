using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MapGenAI.MapGen;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class HotSpringAudit
    {
        static Map lastMap;
        static Dictionary<IntVec3,TerrainDef> water;
        static string linksBefore,orderBefore;
        static int changedBeforeRestore;
        static Dictionary<string,object> audit;
        public static void Configure(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TileMutatorWorker_HotSprings),"GeneratePostTerrain"),
                prefix:new HarmonyMethod(typeof(HotSpringAudit),nameof(Before)){priority=Priority.Last},
                postfix:new HarmonyMethod(typeof(HotSpringAudit),nameof(RawResult)));
            harmony.Patch(AccessTools.Method(typeof(GenStep_MutatorPostTerrain),"Generate"),
                postfix:new HarmonyMethod(typeof(HotSpringAudit),nameof(AfterStep)));
        }
        static string Links(Map map)
        {
            var tile=map.TileInfo as SurfaceTile;
            return string.Join("|",(tile?.Rivers??new List<SurfaceTile.RiverLink>()).Select(r=>((int)r.neighbor)+":"+r.river.defName))
                +"/"+string.Join("|",FeaturePolicy.WaterNeighbors(map.TileInfo).Select(t=>((int)t.tile)+":"+t.PrimaryBiome.defName));
        }
        static string Order(Map map)=>string.Join("|",map.TileInfo.Mutators.Select(d=>d.defName+":"+d.genOrder));
        static void Before(Map map)
        {
            lastMap=map;audit=null;changedBeforeRestore=0;linksBefore=Links(map);orderBefore=Order(map);
            water=map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).IsWater || map.terrainGrid.TerrainAt(c).IsRiver)
                .ToDictionary(c=>c,c=>map.terrainGrid.TerrainAt(c));
        }
        static void RawResult(Map map)
        {
            if(lastMap==map)changedBeforeRestore=water.Count(p=>map.terrainGrid.TerrainAt(p.Key)!=p.Value);
        }
        static void AfterStep(Map map)
        {
            if(lastMap!=map)return;
            audit=new Dictionary<string,object>{
                {"waterBefore",water.Count},{"riverBefore",water.Count(p=>p.Value.IsRiver)},
                {"overwrittenBeforeRestore",changedBeforeRestore},
                {"waterChangesAfterStep",water.Count(p=>map.terrainGrid.TerrainAt(p.Key)!=p.Value)},
                {"hotSpringAfterStep",map.AllCells.Count(c=>map.terrainGrid.TerrainAt(c).defName=="HotSpring")},
                {"terrainAfterStepHash",Hash(string.Join("|",map.AllCells.Select(c=>map.terrainGrid.TerrainAt(c).defName)))},
                {"worldLinksUnchanged",linksBefore==Links(map)},{"mutatorOrderUnchanged",orderBefore==Order(map)},
                {"riverComponentsBefore",Components(map,water.Where(p=>p.Value.IsRiver).Select(p=>p.Key))},
                {"riverComponentsAfter",Components(map,map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).IsRiver))}
            };
        }
        static string Hash(string text){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        static int Components(Map map,IEnumerable<IntVec3> cells)
        {
            int w=map.Size.x,h=map.Size.z,components=0;
            var remaining=new HashSet<int>(cells.Select(c=>c.z*w+c.x));
            var q=new Queue<int>();
            while(remaining.Count>0)
            {
                int start=remaining.First();remaining.Remove(start);q.Enqueue(start);components++;
                while(q.Count>0)
                {
                    int at=q.Dequeue(),x=at%w,z=at/w;
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    {
                        int nx=x+dx,nz=z+dz;
                        if(nx>=0&&nx<w&&nz>=0&&nz<h&&remaining.Remove(nz*w+nx))q.Enqueue(nz*w+nx);
                    }
                }
            }
            return components;
        }
        public static object Measure(Map map)
        {
            if(lastMap!=map||audit==null)return null;
            var result=new Dictionary<string,object>(audit);
            result["hotSpringFinal"]=map.AllCells.Count(c=>map.terrainGrid.TerrainAt(c).defName=="HotSpring");
            result["worldLinksStillUnchanged"]=linksBefore==Links(map);
            return result;
        }
    }
}
