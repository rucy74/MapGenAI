using System;
using System.Linq;

namespace MapGenAI.MapGen
{
    public static class NativeWaterRaster
    {
        // Displacement preserves a scalar boundary but distorts its distance metric.
        // Measure the submerged shelf on the final raster, so compressed banks cannot
        // place deep, impassable water directly against a dry cell.
        public static bool[] DeepMask(int cols,int rows,bool[] water,float shelfCells)
        {
            var dry=new SpatialDistance(cols,rows,water.Select(w=>!w).ToArray());
            var result=new bool[water.Length];float largest=0;
            if(dry.HasTarget)
                for(int i=0;i<water.Length;i++)if(water[i])largest=Math.Max(largest,dry.squared[i]);
            // A compressed lake also needs a scaled shelf: cap the planned width at
            // half its actual inradius, like the native lake's shallow/deep relation.
            float minimum=Math.Max(4f,Math.Min(shelfCells*shelfCells,largest*.25f));
            for(int i=0;i<result.Length;i++)result[i]=water[i] && (!dry.HasTarget || dry.squared[i]>=minimum);
            return result;
        }
    }
}
