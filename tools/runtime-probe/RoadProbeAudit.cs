using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class RoadProbeAudit
    {
        static Map lastMap;
        static string[] before;
        static float[] elevation;
        static string worldBefore;
        static bool[] protectedBefore;
        static Dictionary<string,object> immediate;
        static List<RoadPlacement> placed;
        public static void Configure(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(LocalRoadGeneration),nameof(LocalRoadGeneration.Apply)),prefix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(Before)),finalizer:new HarmonyMethod(typeof(RoadProbeAudit),nameof(After)));
            h.Patch(AccessTools.Method(typeof(LocalRoadGeneration),nameof(LocalRoadGeneration.Check)),postfix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(CaptureElevation)));
        }
        static void CaptureElevation(Map map)
        {
            if(lastMap!=map)return;
            foreach(var cell in map.AllCells)elevation[cell.z*map.Size.x+cell.x]=MapGenerator.Elevation[cell];
        }
        static string WorldRoads(Map map)
        {
            var tile=map.TileInfo as SurfaceTile;
            return tile?.Roads==null?"":string.Join("|",tile.Roads.Select(r=>((int)r.neighbor)+":"+r.road.defName).OrderBy(s=>s));
        }
        static void Before(Map map)
        {
            lastMap=map;before=map.AllCells.Select(c=>map.terrainGrid.TerrainAt(c).defName).ToArray();
            elevation=map.AllCells.Select(c=>MapGenerator.Elevation[c]).ToArray();
            protectedBefore=map.AllCells.Select(c=>{var t=map.terrainGrid.TerrainAt(c);return t.HasTag("Road") || t.designationCategory!=null || t.costList?.Count>0 || t.costStuffCount>0;}).ToArray();
            worldBefore=WorldRoads(map);placed=null;immediate=null;
        }
        static void After(Map map)
        {
            int i=0,outside=0,height=0,changes=0,protectedChanges=0;
            var cells=GenerationContext.Regions(map).LocalRoadCells;
            foreach(var cell in map.AllCells)
            {
                if(before[i]!=map.terrainGrid.TerrainAt(cell).defName){changes++;if(!cells[cell.z*map.Size.x+cell.x])outside++;if(protectedBefore[i])protectedChanges++;}
                if(elevation[i]!=MapGenerator.Elevation[cell])height++;i++;
            }
            placed=AuthoringGeneration.Current?.roads.ToList()??new List<RoadPlacement>();
            immediate=new Dictionary<string,object>{{"changedCells",changes},{"outsideFootprintChanges",outside},{"elevationChanges",height},{"worldRoadsUnchanged",worldBefore==WorldRoads(map)},{"worldRoadLinks",worldBefore},{"protectedFloorChanges",protectedChanges},{"protectedFloorCells",protectedBefore.Count(p=>p)}};
        }
        public static object Measure(Map map)
        {
            if(lastMap!=map || immediate==null)return null;
            var roads=new List<object>();
            foreach(var road in placed)
            {
                var path=road.path;int w=map.Size.x,h=map.Size.z;
                var passable=new bool[w*h];
                foreach(var cell in map.AllCells)
                {
                    var t=map.terrainGrid.TerrainAt(cell);
                    passable[cell.z*w+cell.x]=!t.IsWater && !t.IsRiver && !t.dangerous && cell.GetEdifice(map)==null && elevation[cell.z*w+cell.x]<.7f;
                }
                int blocked=path.Count(p=>!passable[p[1]*w+p[0]]);
                // Independent four-neighbor connectivity through the actual road footprint.
                var allowed=new bool[w*h];
                foreach(var p in road.footprint)allowed[p[1]*w+p[0]]=passable[p[1]*w+p[0]];
                var seen=new HashSet<int>();var q=new Queue<int>();int start=path[0][1]*w+path[0][0],end=path.Last()[1]*w+path.Last()[0];
                if(allowed[start]){seen.Add(start);q.Enqueue(start);}
                while(q.Count>0)
                {
                    int at=q.Dequeue(),x=at%w,z=at/w;
                    foreach(int n in new[]{x>0?at-1:-1,x<w-1?at+1:-1,z>0?at-w:-1,z<h-1?at+w:-1})
                        if(n>=0 && allowed[n] && seen.Add(n))q.Enqueue(n);
                }
                var terrain=road.footprint.Select(p=>map.terrainGrid.TerrainAt(new IntVec3(p[0],0,p[1])).defName).ToArray();
                roads.Add(new Dictionary<string,object>{{"id",road.id},{"kind",road.kind},{"path",path},{"paintedCells",road.paintedCells},{"protectedCells",road.protectedCells},{"blockedPathCells",blocked},{"connectedWithinFootprint",seen.Contains(end)},{"pathHash",Hash(SimpleJson.Serialize(path))},{"terrainHash",Hash(string.Join("|",terrain))},{"terrains",terrain.GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count())}});
            }
            return new Dictionary<string,object>{{"immediate",immediate},{"worldRoadsStillUnchanged",worldBefore==WorldRoads(map)},{"roads",roads}};
        }
        static string Hash(string text){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
