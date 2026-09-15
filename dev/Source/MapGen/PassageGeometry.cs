using System;

namespace MapGenAI.MapGen
{
    // Explicit passages sweep a cell-width square along a four-connected centerline.
    // No randomness or contour warp: bends cannot shrink the requested clear footprint.
    public static class PassageGeometry
    {
        public static bool[] Mask(int cols,int rows,float[][] points,int width)
        {
            if(cols<1 || rows<1 || width<1 || width>64 || width>Math.Min(cols,rows))throw new ArgumentException("Invalid passage dimensions");
            if(points==null || points.Length<2 || points.Length>32)throw new ArgumentException("Invalid passage centerline");
            foreach(var p in points)ShapeEdits.ValidatePair(p);
            var result=new bool[checked(cols*rows)];var delta=new int[checked((cols+1)*(rows+1))];int stride=cols+1,lo=(width-1)/2,hi=width/2;
            // Mono can retain extended float precision before integer casts (.7*250 ->174).
            // Stabilize coordinates near exact cell boundaries across Unity/CLR runtimes.
            int X(float v)=>Math.Min(cols-1,(int)Math.Floor((double)v*cols+.0001));
            int Z(float v)=>Math.Min(rows-1,(int)Math.Floor((double)v*rows+.0001));
            void Stamp(int x,int z)
            {
                int left=Math.Max(0,x-lo),right=Math.Min(cols,x+hi+1),bottom=Math.Max(0,z-lo),top=Math.Min(rows,z+hi+1);
                delta[bottom*stride+left]++;delta[bottom*stride+right]--;delta[top*stride+left]--;delta[top*stride+right]++;
            }
            for(int i=1;i<points.Length;i++)
            {
                int x=X(points[i-1][0]),z=Z(points[i-1][1]),endX=X(points[i][0]),endZ=Z(points[i][1]);
                int dx=Math.Abs(endX-x),dz=Math.Abs(endZ-z),sx=Math.Sign(endX-x),sz=Math.Sign(endZ-z),error=dx-dz;Stamp(x,z);
                while(x!=endX || z!=endZ)
                {
                    int twice=2*error;
                    if(twice>-dz){error-=dz;x+=sx;Stamp(x,z);}
                    if(twice<dx){error+=dx;z+=sz;Stamp(x,z);}
                }
            }
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
            {
                int i=z*stride+x;
                if(x>0)delta[i]+=delta[i-1];if(z>0)delta[i]+=delta[i-stride];if(x>0 && z>0)delta[i]-=delta[i-stride-1];
                result[z*cols+x]=delta[i]>0;
            }
            return result;
        }
    }
}
