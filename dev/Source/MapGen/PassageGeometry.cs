using System;

namespace MapGenAI.MapGen
{
    // Explicit passages sweep a cell-width square along a four-connected centerline.
    // Optional roughness only expands the edges; bends never shrink the clear core.
    public static class PassageGeometry
    {
        public static void RestrictToMountains(bool[] mask,float[] beforeElevation)
        {
            if(mask==null || beforeElevation==null || mask.Length!=beforeElevation.Length)throw new ArgumentException("Mismatched passage elevation grid");
            for(int i=0;i<mask.Length;i++)mask[i] &= beforeElevation[i]>=.7f;
        }

        public static bool[] Mask(int cols,int rows,float[][] points,int width,string roughness=null,string id=null)
        {
            if(cols<1 || rows<1 || width<1 || width>64 || width>Math.Min(cols,rows))throw new ArgumentException("Invalid passage dimensions");
            if(points==null || points.Length<2 || points.Length>32)throw new ArgumentException("Invalid passage centerline");
            foreach(var p in points)ShapeEdits.ValidatePair(p);
            float amount=ContourWarp.Amount(roughness);
            if(float.IsNaN(amount) || amount<0 || amount>1)throw new ArgumentException("Invalid passage roughness");
            float amplitude=Math.Min(12f,Math.Max(3f,width*.65f))*amount;
            float wavelength=Math.Max(10f,width*1.7f);
            uint seed=2166136261u;
            unchecked { foreach(char c in id ?? "passage")seed=(seed^c)*16777619u; }
            int Margin(int x,int z,uint side)=>amount==0?0:(int)Math.Floor(amplitude*(.5f+.5f*ContourWarp.Noise(x/wavelength,z/wavelength,seed^side)));
            var result=new bool[checked(cols*rows)];var delta=new int[checked((cols+1)*(rows+1))];int stride=cols+1,lo=(width-1)/2,hi=width/2;
            // Mono can retain extended float precision before integer casts (.7*250 ->174).
            // Stabilize coordinates near exact cell boundaries across Unity/CLR runtimes.
            int X(float v)=>Math.Min(cols-1,(int)Math.Floor((double)v*cols+.0001));
            int Z(float v)=>Math.Min(rows-1,(int)Math.Floor((double)v*rows+.0001));
            void Stamp(int x,int z)
            {
                int left=Math.Max(0,x-lo-Margin(x,z,0x1234u)),right=Math.Min(cols,x+hi+1+Margin(x,z,0x5678u)),
                    bottom=Math.Max(0,z-lo-Margin(x,z,0x9abcu)),top=Math.Min(rows,z+hi+1+Margin(x,z,0xdef0u));
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
