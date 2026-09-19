using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;

static class RoadRoutingTests
{
    public static void RunAll() => Run(CoreRegressionTests.Check);

    public static void Run(Action<string,Action> test)
    {
        test("Road direct route visits exact ordered waypoints on empty ground", () =>
        {
            const int cols=41,rows=31;
            var ground=Open(cols,rows);
            var points=Points(cols,rows,2,2,9,2,9,13,25,13);
            var path=RoadRouting.Plan(cols,rows,ground,points,"direct",1.3f);
            AssertRoute(cols,rows,ground,path,1.3f);
            AssertWaypoints(path,2+2*cols,9+2*cols,9+13*cols,25+13*cols);
            Need(path.Count==35,"Axis-aligned legs must have exactly their combined length plus one cell");
            Need(path.All(i=>i/cols==2 && i%cols>=2 && i%cols<=9
                || i%cols==9 && i/cols>=2 && i/cols<=13
                || i/cols==13 && i%cols>=9 && i%cols<=25),"A direct leg left its requested line");
        });
        test("Road normalized endpoints and midpoint work on a rectangular map", () =>
        {
            const int cols=30,rows=20;
            var ground=Open(cols,rows);
            var path=RoadRouting.Plan(cols,rows,ground,new[]{new[]{0f,1f},new[]{.5f,.5f},new[]{1f,0f}},"direct",0f);
            AssertRoute(cols,rows,ground,path,0f);
            AssertWaypoints(path,19*cols,10*cols+15,29);
        });
        test("Road direct diagonal stays on its requested segment", () =>
        {
            const int cols=33,rows=21;
            var ground=Open(cols,rows);
            var path=RoadRouting.Plan(cols,rows,ground,Points(cols,rows,2,2,30,18),"direct",1f);
            AssertRoute(cols,rows,ground,path,1f);
            AssertWaypoints(path,2+2*cols,30+18*cols);
            Need(path.Count==29,"An unobstructed direct diagonal should not detour");
            foreach(int cell in path)
                Need(SegmentDistanceSquared(cell%cols,cell/cols,2,2,30,18)<=.251,"Direct raster deviates from the specified segment");
        });
        test("Road avoid routes around a mountain or water footprint while direct rejects it", () =>
        {
            const int cols=41,rows=31;
            var ground=Open(cols,rows);
            Block(ground,cols,18,7,22,23);
            var points=Points(cols,rows,3,15,37,15);
            Need(Reachable(cols,rows,ground,3+15*cols,37+15*cols,2.3f),"Fixture must have a broad dry detour");
            var path=RoadRouting.Plan(cols,rows,ground,points,"avoid",1.5f);
            AssertRoute(cols,rows,ground,path,1.5f);
            AssertWaypoints(path,3+15*cols,37+15*cols);
            Need(path.Any(i=>i%cols==20 && (i/cols<7 || i/cols>23)),"Route did not go around the obstacle");
            double length=Enumerable.Range(1,path.Count-1).Sum(i=>
            {
                int dx=path[i]%cols-path[i-1]%cols,dz=path[i]/cols-path[i-1]/cols;
                return Math.Sqrt(dx*dx+dz*dz);
            });
            Need(length>34.01,"Blocked centerline was silently used instead of a detour");
            Reject(()=>RoadRouting.Plan(cols,rows,ground,points,"direct",1.5f));
        });
        test("Road clearance rejects a passage too narrow for its full width", () =>
        {
            const int cols=41,rows=31;
            var ground=Open(cols,rows);
            for(int z=0;z<rows;z++) if(z<14 || z>16) ground[z*cols+20]=false;
            var points=Points(cols,rows,4,15,36,15);
            Need(Reachable(cols,rows,ground,4+15*cols,36+15*cols,.25f),"Narrow fixture must allow a thin path");
            Need(!Reachable(cols,rows,ground,4+15*cols,36+15*cols,2f),"Wide footprint must be separated by this gap");
            var narrow=RoadRouting.Plan(cols,rows,ground,points,"avoid",.25f);
            AssertRoute(cols,rows,ground,narrow,.25f);
            Reject(()=>RoadRouting.Plan(cols,rows,ground,points,"avoid",2f));
        });
        test("Road fully separated ground is rejected without changing inputs", () =>
        {
            const int cols=25,rows=19;
            var ground=Open(cols,rows);
            Block(ground,cols,12,0,12,rows-1);
            var before=(bool[])ground.Clone();
            var points=Points(cols,rows,3,9,21,9);
            var pointCopy=Copy(points);
            Need(!Reachable(cols,rows,ground,3+9*cols,21+9*cols,0),"Fixture is not actually disconnected");
            foreach(var mode in new[]{"direct","avoid"}) Reject(()=>RoadRouting.Plan(cols,rows,ground,points,mode,0));
            Need(before.SequenceEqual(ground),"Rejected planning mutated ground");
            SamePoints(pointCopy,points);
        });
        test("Road later failed leg does not expose a partially planned road", () =>
        {
            const int cols=31,rows=21;
            var ground=Open(cols,rows);
            Block(ground,cols,20,0,20,rows-1);
            var points=Points(cols,rows,3,4,12,4,27,14);
            var pointCopy=Copy(points);
            var before=(bool[])ground.Clone();
            foreach(var mode in new[]{"direct","avoid"})
            {
                var sentinel=new List<int>{-123};
                var returned=sentinel;
                Reject(()=>returned=RoadRouting.Plan(cols,rows,ground,points,mode,.5f));
                Need(ReferenceEquals(sentinel,returned),"A failed later leg returned a partial road");
            }
            Need(before.SequenceEqual(ground),"Failed later leg mutated ground");
            SamePoints(pointCopy,points);
        });
        test("Road blocked start end and intermediate waypoint are not silently relocated", () =>
        {
            const int cols=25,rows=21;
            var points=Points(cols,rows,3,4,12,10,21,16);
            foreach(int blocked in new[]{3+4*cols,12+10*cols,21+16*cols})
            {
                var ground=Open(cols,rows);ground[blocked]=false;
                foreach(var mode in new[]{"direct","avoid"}) Reject(()=>RoadRouting.Plan(cols,rows,ground,points,mode,0));
            }
        });
        test("Road avoid cannot cross diagonally touching obstacle corners", () =>
        {
            const int cols=3,rows=3;
            var ground=new bool[cols*rows];ground[0]=ground[4]=ground[8]=true;
            Need(!Reachable(cols,rows,ground,0,8,0),"Diagonal-only cells must not be cardinally connected");
            foreach(var mode in new[]{"direct","avoid"})
                Reject(()=>RoadRouting.Plan(cols,rows,ground,new[]{new[]{0f,0f},new[]{1f,1f}},mode,0));
        });
        test("Road avoid finds a safe winding corridor with multiple bends", () =>
        {
            const int cols=51,rows=35;
            var ground=new bool[cols*rows];
            OpenRect(ground,cols,0,0,24,8);
            OpenRect(ground,cols,16,0,24,32);
            OpenRect(ground,cols,16,24,50,32);
            var points=Points(cols,rows,3,4,46,28);
            Need(Reachable(cols,rows,ground,3+4*cols,46+28*cols,2),"Fixture must allow the requested road width");
            var path=RoadRouting.Plan(cols,rows,ground,points,"avoid",1f);
            AssertRoute(cols,rows,ground,path,1f);
            AssertWaypoints(path,3+4*cols,46+28*cols);
            Need(path.Any(i=>i/cols==16 && i%cols>=16 && i%cols<=24),"Route missed the sole vertical connector");
        });
        test("Road avoid preserves several requested waypoints instead of shortcutting them", () =>
        {
            const int cols=37,rows=29;
            var ground=Open(cols,rows);
            Block(ground,cols,15,10,21,18);
            var points=Points(cols,rows,3,4,30,4,30,24,3,24,3,14);
            var path=RoadRouting.Plan(cols,rows,ground,points,"avoid",1.3f);
            AssertRoute(cols,rows,ground,path,1.3f);
            AssertWaypoints(path,3+4*cols,30+4*cols,30+24*cols,3+24*cols,3+14*cols);
        });
        test("Road routing is reproducible and leaves ground and waypoints immutable", () =>
        {
            const int cols=41,rows=31;
            var ground=Open(cols,rows);
            Block(ground,cols,17,9,23,22);
            var points=Points(cols,rows,3,15,37,15,37,27);
            var before=(bool[])ground.Clone();var pointCopy=Copy(points);
            var first=RoadRouting.Plan(cols,rows,ground,points,"avoid",1.3f);
            AssertRoute(cols,rows,ground,first,1.3f);
            for(int i=0;i<8;i++)
                Need(first.SequenceEqual(RoadRouting.Plan(cols,rows,ground,points,"avoid",1.3f)),"Identical routing input produced different cells");
            Need(before.SequenceEqual(ground),"Planning mutated ground");
            SamePoints(pointCopy,points);
        });
        test("Road clearance matches independent nearest-obstacle distances", () =>
        {
            const int cols=19,rows=13;
            var ground=Open(cols,rows);
            foreach(int cell in new[]{0,18,6+4*cols,14+9*cols,12*cols}) ground[cell]=false;
            var before=(bool[])ground.Clone();
            foreach(float radius in new[]{0f,1f,1.3f,3.7f})
            {
                var actual=RoadRouting.Clearance(cols,rows,ground,radius);
                for(int cell=0;cell<ground.Length;cell++)
                    Need(actual[cell]==CenterHasClearance(cols,ground,cell,radius),"Clearance differs from geometric distance at "+cell+" radius "+radius);
            }
            Need(before.SequenceEqual(ground),"Clearance mutated source ground");
        });
    }

    static bool[] Open(int cols,int rows)=>Enumerable.Repeat(true,cols*rows).ToArray();
    static void Block(bool[] ground,int cols,int x0,int z0,int x1,int z1)
    {for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)ground[z*cols+x]=false;}
    static void OpenRect(bool[] ground,int cols,int x0,int z0,int x1,int z1)
    {for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)ground[z*cols+x]=true;}
    static float[][] Points(int cols,int rows,params int[] coordinates)
    {
        var result=new float[coordinates.Length/2][];
        for(int i=0;i<result.Length;i++)result[i]=new[]{coordinates[2*i]/(float)(cols-1),coordinates[2*i+1]/(float)(rows-1)};
        return result;
    }
    static float[][] Copy(float[][] points)=>points.Select(p=>(float[])p.Clone()).ToArray();
    static void SamePoints(float[][] expected,float[][] actual)
    {Need(expected.Length==actual.Length,"Waypoint count changed");for(int i=0;i<expected.Length;i++)Need(expected[i].SequenceEqual(actual[i]),"Waypoint coordinates changed");}
    static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Reject(Action action)
    {
        try{action();}catch(InvalidOperationException){return;}
        throw new Exception("Expected a blocked-road InvalidOperationException");
    }
    static void AssertWaypoints(List<int> path,params int[] waypoints)
    {
        Need(path.Count>0,"Road was empty");
        Need(path[0]==waypoints[0] && path[path.Count-1]==waypoints[waypoints.Length-1],"Road endpoints moved");
        int next=0;
        foreach(int waypoint in waypoints)
        {
            while(next<path.Count && path[next]!=waypoint)next++;
            Need(next<path.Count,"Ordered waypoint missing: "+waypoint);next++;
        }
    }
    static void AssertRoute(int cols,int rows,bool[] ground,List<int> path,float radius)
    {
        Need(path.Count>1,"Road has no connected length");
        var obstacles=Enumerable.Range(0,ground.Length).Where(i=>!ground[i]).ToArray();
        for(int i=0;i<path.Count;i++)
        {
            int cell=path[i];Need(cell>=0 && cell<cols*rows,"Road left the map");
            Need(ground[cell],"Road occupies blocked ground");
            if(i==0)continue;
            int a=path[i-1],ax=a%cols,az=a/cols,bx=cell%cols,bz=cell/cols;
            int dx=Math.Abs(ax-bx),dz=Math.Abs(az-bz);
            Need(dx<=1 && dz<=1 && dx+dz>0,"Road has a gap or duplicate step");
            if(dx!=0 && dz!=0)
                Need(ground[az*cols+bx] && ground[bz*cols+ax],"Road cuts through a blocked diagonal corner");
            foreach(int blocked in obstacles)
                Need(SegmentDistanceSquared(blocked%cols,blocked/cols,ax,az,bx,bz)>radius*radius-0.000001,
                    "Road width intersects an obstacle beside a segment");
        }
    }
    // Independent geometry oracle: closest point on the output segment, not the router's dilation or rasterizer.
    static double SegmentDistanceSquared(double px,double pz,double ax,double az,double bx,double bz)
    {
        double dx=bx-ax,dz=bz-az,denominator=dx*dx+dz*dz;
        double t=denominator==0?0:Math.Max(0,Math.Min(1,((px-ax)*dx+(pz-az)*dz)/denominator));
        double ex=px-(ax+t*dx),ez=pz-(az+t*dz);return ex*ex+ez*ez;
    }
    static bool CenterHasClearance(int cols,bool[] ground,int cell,float radius)
    {
        if(!ground[cell])return false;
        int x=cell%cols,z=cell/cols;
        for(int obstacle=0;obstacle<ground.Length;obstacle++)if(!ground[obstacle])
        {
            int dx=x-obstacle%cols,dz=z-obstacle/cols;
            if(dx*dx+dz*dz<=radius*radius)return false;
        }
        return true;
    }
    // Independent cardinal BFS establishes whether the fixture offers a physically wide connected route.
    static bool Reachable(int cols,int rows,bool[] ground,int start,int end,float radius)
    {
        var safe=Enumerable.Range(0,ground.Length).Select(i=>CenterHasClearance(cols,ground,i,radius)).ToArray();
        if(!safe[start] || !safe[end])return false;
        var visited=new bool[ground.Length];var queue=new Queue<int>();queue.Enqueue(start);visited[start]=true;
        while(queue.Count>0)
        {
            int cell=queue.Dequeue();if(cell==end)return true;
            int x=cell%cols,z=cell/cols;
            if(x>0)Visit(cell-1);if(x+1<cols)Visit(cell+1);if(z>0)Visit(cell-cols);if(z+1<rows)Visit(cell+cols);
        }
        return false;
        void Visit(int next){if(safe[next] && !visited[next]){visited[next]=true;queue.Enqueue(next);}}
    }
}
