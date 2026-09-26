using System;
using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    // Permission stays tied to authored water. Material distance may follow its feathered
    // edge, but never a disconnected pond or water behind an explicit protected area.
    public sealed class ShorelineWater
    {
        public readonly SpatialDistance OriginDistance, WaterDistance;
        public readonly bool[] ConnectedWater;
        public ShorelineWater(int cols,int rows,bool[] sources,bool[] freshwater,bool[] excluded)
        {
            int count=cols*rows;
            if(cols<1 || rows<1 || sources==null || freshwater==null || excluded==null ||
                sources.Length!=count || freshwater.Length!=count || excluded.Length!=count)
                throw new ArgumentException("Shore masks must match the map dimensions");
            var origin=new bool[count];
            for(int i=0;i<count;i++)origin[i]=sources[i] && freshwater[i] && !excluded[i];
            OriginDistance=new SpatialDistance(cols,rows,origin);
            ConnectedWater=new bool[count];var queue=new Queue<int>();
            for(int i=0;i<count;i++)if(origin[i]){ConnectedWater[i]=true;queue.Enqueue(i);}
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),x=i%cols,z=i/cols;
                if(x>0)Visit(i-1);if(x+1<cols)Visit(i+1);
                if(z>0)Visit(i-cols);if(z+1<rows)Visit(i+cols);
            }
            WaterDistance=new SpatialDistance(cols,rows,ConnectedWater);
            void Visit(int i)
            {
                if(ConnectedWater[i] || !freshwater[i] || excluded[i] || OriginDistance.squared[i]>36)return;
                ConnectedWater[i]=true;queue.Enqueue(i);
            }
        }
        // Taper only the outer permission boundary; the water's own edge stays distinct.
        public float Influence(int index)
        {
            if(!OriginDistance.HasTarget)return 0;
            double distance=Math.Sqrt(OriginDistance.squared[index]);
            return distance<=4?1:distance>=6?0:(float)((6-distance)/2);
        }
    }
}
