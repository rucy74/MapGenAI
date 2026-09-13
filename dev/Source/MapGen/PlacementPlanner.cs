using System;
using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    public struct PlannedRect
    {
        public int x, z, width, height;
        public bool Contains(int cx,int cz) => cx >= x && cz >= z && cx < x+width && cz < z+height;
    }
    public static class PlacementPlanner
    {
        // Integral image makes every full-footprint feasibility check O(1), including holes.
        public static List<PlannedRect> Find(int cols, int rows, bool[] allowed, bool[] occupied,
            int width, int height, int count, float preferredX, float preferredZ)
        {
            if (allowed.Length != cols*rows || occupied.Length != allowed.Length) throw new ArgumentException("Mask dimensions differ");
            var reserved = (bool[])occupied.Clone();
            var result = new List<PlannedRect>();
            for (int n=0; n<count; n++)
            {
                var integral = new int[(cols+1)*(rows+1)]; int stride=cols+1;
                for (int z=0;z<rows;z++) for(int x=0;x<cols;x++)
                    integral[(z+1)*stride+x+1] = (!allowed[z*cols+x] || reserved[z*cols+x] ? 1 : 0)
                        + integral[(z+1)*stride+x] + integral[z*stride+x+1] - integral[z*stride+x];
                double best = double.MaxValue; PlannedRect? chosen=null;
                for(int z=1;z+height<rows;z++) for(int x=1;x+width<cols;x++)
                {
                    int blocked=integral[(z+height)*stride+x+width]-integral[z*stride+x+width]-integral[(z+height)*stride+x]+integral[z*stride+x];
                    if(blocked!=0)continue;
                    double dx=x+(width-1)*.5-preferredX, dz=z+(height-1)*.5-preferredZ, score=dx*dx+dz*dz;
                    if(score>=best)continue;
                    best=score; chosen=new PlannedRect{x=x,z=z,width=width,height=height};
                }
                if(!chosen.HasValue)return null; // Atomic batch: caller must not spawn a partial count.
                var r=chosen.Value;result.Add(r);
                for(int z=Math.Max(0,r.z-1);z<Math.Min(rows,r.z+r.height+1);z++)
                    for(int x=Math.Max(0,r.x-1);x<Math.Min(cols,r.x+r.width+1);x++) reserved[z*cols+x]=true;
            }
            Array.Copy(reserved,occupied,reserved.Length);
            return result;
        }
    }
}
