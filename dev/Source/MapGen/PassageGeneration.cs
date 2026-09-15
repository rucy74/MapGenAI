using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MapGenAI.MapGen
{
    public static class PassageGeneration
    {
        public static void Apply(Map map,MapGenFloatGrid elevation)
        {
            var shapes=GenerationContext.State?.elevationShapes;if(shapes==null)return;
            foreach(var s in shapes.Where(s=>s.type=="passage"))
            {
                var mask=PassageGeometry.Mask(map.Size.x,map.Size.z,s.points,s.width);var regions=GenerationContext.Regions(map);
                foreach(var c in map.AllCells)if(mask[c.z*map.Size.x+c.x])
                {
                    elevation[c]=.05f;
                    if(MapGenerator.Fertility!=null)MapGenerator.Fertility[c]=.5f;
                    regions.Record(s.id,c,true,s.fill,true);regions.Flatten[regions.Index(c)]=true;
                }
            }
        }
        public static void Check(Map map)
        {
            var shapes=GenerationContext.State?.elevationShapes;if(shapes==null)return;
            foreach(var s in shapes.Where(s=>s.type=="passage"))
            {
                var mask=PassageGeometry.Mask(map.Size.x,map.Size.z,s.points,s.width);int blocked=0;
                foreach(var c in map.AllCells)if(mask[c.z*map.Size.x+c.x])
                {
                    var t=map.terrainGrid.TerrainAt(c);
                    if(t.IsWater || t.dangerous || !c.Walkable(map) || MapGenerator.Elevation[c]>=.7f)blocked++;
                }
                if(blocked>0)AuthoringGeneration.Fail(new InvalidOperationException("요청한 폭의 마른 통로에 장애물이 "+blocked+"칸 남았습니다. 위치/폭을 조정하세요. 기존 강·해안·건물을 강제로 지우지 않습니다. / The requested dry passage footprint has "+blocked+" obstructed cells; adjust its route/width. World water and buildings are preserved."));
            }
        }
        public static void Reserve(Map map)
        {
            var shapes=GenerationContext.State?.elevationShapes;if(shapes==null || !shapes.Any(s=>s.type=="passage"))return;
            var used=MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");var regions=GenerationContext.Regions(map);
            foreach(var s in shapes.Where(s=>s.type=="passage"))
            {
                var mask=regions.Mask(s.id);
                // Compact exact horizontal runs; native generators that honor UsedRects avoid the route.
                var previous=new Dictionary<string,CellRect>();
                for(int z=0;z<map.Size.z;z++)
                {
                    var current=new Dictionary<string,CellRect>();
                    for(int x=0;x<map.Size.x;x++)if(mask[z*map.Size.x+x])
                    {
                        int start=x;while(x+1<map.Size.x && mask[z*map.Size.x+x+1])x++;
                        string key=start+":"+x;
                        if(previous.TryGetValue(key,out var old)){current[key]=new CellRect(old.minX,old.minZ,old.Width,old.Height+1);previous.Remove(key);}
                        else current[key]=new CellRect(start,z,x-start+1,1);
                    }
                    used.AddRange(previous.Values);previous=current;
                }
                used.AddRange(previous.Values);
            }
        }
    }
}
