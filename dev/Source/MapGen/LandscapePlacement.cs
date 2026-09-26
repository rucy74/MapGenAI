using System;
using System.Collections.Generic;
using System.Linq;

namespace MapGenAI.MapGen
{
    // Persist relationships, resolve against the source's generated mask each time.
    // Conservative rectangular footprint keeps feathered shore cells inside too.
    public static class LandscapePlacement
    {
        public static void ValidateReferences(TileMapState state)
        {
            var all=state.elevationShapes;
            foreach(var shape in all.Where(s=>s.anchor!=null))
            {
                var visited=new HashSet<string>{shape.id};var current=shape;
                while(current.anchor!=null)
                {
                    if(!visited.Add(current.anchor))throw new FormatException("Cyclic landscape relationship");
                    current=all.FirstOrDefault(s=>s.id==current.anchor);
                    if(current==null || !(current.type=="composite" || current.type=="landform" || current.type=="bump" || current.type=="ring"))
                        throw new FormatException("Landscape anchor must reference an existing area");
                }
            }
        }
        public static List<ElevationShape> Order(IEnumerable<ElevationShape> shapes)
        {
            // Preserve the exact old non-water / water order when no relationships exist.
            var old=shapes.Where(s=>s.fill!="water").Concat(shapes.Where(s=>s.fill=="water")).ToList();
            var result=new List<ElevationShape>();var visiting=new HashSet<ElevationShape>();
            foreach(var shape in old.Where(s=>s.anchor==null))Add(shape);
            foreach(var shape in old.Where(s=>s.anchor!=null))Add(shape);
            return result;
            void Add(ElevationShape shape)
            {
                if(result.Contains(shape))return;
                if(!visiting.Add(shape))throw new FormatException("Cyclic landscape relationship");
                if(shape.anchor!=null)
                {
                    var parent=old.FirstOrDefault(s=>s.id==shape.anchor);
                    if(parent==null)throw new FormatException("Missing landscape anchor");
                    Add(parent);
                }
                result.Add(shape);visiting.Remove(shape);
            }
        }
        // Return a translation in cells. No cropping, silent shrinking, or state mutation.
        public static bool TryPlace(int cols,int rows,bool[] source,bool[] footprint,string placement,string direction,
            out int dx,out int dz,bool[] occupiedCells=null)
        {
            dx=dz=0;
            int minX=cols,minZ=rows,maxX=-1,maxZ=-1,count=0;double cx=0,cz=0;
            for(int i=0;i<footprint.Length;i++)if(footprint[i])
            {minX=Math.Min(minX,i%cols);maxX=Math.Max(maxX,i%cols);minZ=Math.Min(minZ,i/cols);maxZ=Math.Max(maxZ,i/cols);}
            for(int i=0;i<source.Length;i++)if(source[i]){cx+=i%cols;cz+=i/cols;count++;}
            if(count==0 || maxX<0)return false;
            cx/=count;cz/=count;
            int width=maxX-minX+1,height=maxZ-minZ+1,stride=cols+1;
            var blocked=new int[stride*(rows+1)];bool beside=placement=="beside";
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
                blocked[(z+1)*stride+x+1]=(source[z*cols+x]==beside || occupiedCells!=null && occupiedCells[z*cols+x]?1:0)+blocked[z*stride+x+1]+blocked[(z+1)*stride+x]-blocked[z*stride+x];
            var distance=new SpatialDistance(cols,rows,beside?source:SpatialDistance.InteriorEdge(cols,rows,source));
            double best=double.MaxValue;float angle=ElevationShape.ParseDirection(direction)*(float)Math.PI/180;
            double vx=Math.Cos(angle),vz=Math.Sin(angle),mapSize=Math.Max(cols,rows);
            for(int z=0;z<=rows-height;z++)for(int x=0;x<=cols-width;x++)
            {
                int occupied=blocked[(z+height)*stride+x+width]-blocked[z*stride+x+width]-blocked[(z+height)*stride+x]+blocked[z*stride+x];
                if(occupied!=0)continue;
                int midX=x+(width-1)/2,midZ=z+(height-1)/2;
                double sx=midX-cx,sz=midZ-cz,dist=Math.Sqrt(distance.squared[midZ*cols+midX]);
                if(beside && dist>Math.Max(width,height)*.65+8)continue;
                double side=direction==null?0:(sx*vx+sz*vz)/mapSize;
                // Explicit side is a constraint, not a hint that can fall back to the opposite side.
                if(direction!=null && side<.01)continue;
                double score=(placement=="inside"?-dist:dist)+.025*(sx*sx+sz*sz)/mapSize-side*mapSize*.2;
                if(score<best){best=score;dx=x-minX;dz=z-minZ;}
            }
            return best<double.MaxValue;
        }
    }
}
