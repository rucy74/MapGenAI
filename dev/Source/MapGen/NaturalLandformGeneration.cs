using UnityEngine;
using Verse;

namespace MapGenAI.MapGen
{
    public static class NaturalLandformGeneration
    {
        public static void Apply(ElevationShape shape,Map map,MapGenFloatGrid grid)
        {
            var layout=new NaturalLandformGeometry(shape);
            var regions=GenerationContext.Regions(map);
            foreach(var cell in CellRect.WholeMap(map))
            {
                var sample=layout.Sample((cell.x+.5f)/map.Size.x,(cell.z+.5f)/map.Size.z);
                if(sample.influence<=0)continue;
                int index=cell.z*map.Size.x+cell.x;
                // Authored water and the native river/coast stages keep their usual precedence.
                string material=regions?.Materials[index];
                if(material!=null && DefDatabase<TerrainDef>.GetNamedSilentFail(material)?.IsWater==true)continue;
                grid[cell]=Mathf.Lerp(grid[cell],sample.elevation,sample.influence);
                if(regions!=null)
                {
                    regions.Flatten[index]=sample.floor;
                    regions.Record(shape.id,cell,sample.floor,null,false);
                }
            }
        }
    }
}
