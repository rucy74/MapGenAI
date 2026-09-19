using System;
using System.Collections.Generic;
using System.Linq;

namespace MapGenAI.MapGen
{
    // Pure deterministic routing. No world/map mutation, RNG or game pathfinder.
    public static class RoadRouting
    {
        public static List<int> Plan(int cols,int rows,bool[] ground,float[][] points,string mode,float radius)
        {
            if(cols<2 || rows<2 || ground==null || ground.Length!=cols*rows)throw new ArgumentException("Invalid road grid");
            // Diagonal centerline segments pass between cell centers; reserve that extra half diagonal.
            var safe=Clearance(cols,rows,ground,radius+.708f);
            var result=new List<int>();
            for(int n=1;n<points.Length;n++)
            {
                int a=Index(points[n-1]),b=Index(points[n]);
                if(a==b)throw new InvalidOperationException("도로 경유점이 같은 맵 칸에 있습니다. / Road waypoints resolve to the same map cell.");
                List<int> leg;
                if(mode=="direct")
                {
                    leg=Line(cols,a,b);
                    if(!Visible(cols,safe,a,b))throw Blocked();
                }
                else
                {
                    leg=Find(cols,rows,safe,a,b);
                    if(leg==null)throw Blocked();
                    // Keep broad straight portions; bends remain only where clearance requires them.
                    var simplified=new List<int>{leg[0]};int start=0;
                    while(start<leg.Count-1)
                    {
                        int next=start+1;
                        for(int end=leg.Count-1;end>start+1;end--)
                            if(Visible(cols,safe,leg[start],leg[end])){next=end;break;}
                        simplified.Add(leg[next]);start=next;
                    }
                    leg=new List<int>();
                    for(int i=1;i<simplified.Count;i++)Append(leg,Line(cols,simplified[i-1],simplified[i]));
                }
                Append(result,leg);
            }
            return result;
            int Index(float[] point)=>(int)Math.Round(point[1]*(rows-1),MidpointRounding.AwayFromZero)*cols+(int)Math.Round(point[0]*(cols-1),MidpointRounding.AwayFromZero);
        }
        static Exception Blocked()=>new InvalidOperationException("도로를 연결할 마른 공간이 부족합니다. 경유점을 옮기거나 우회 경로를 요청하세요. 강·바다·산·건물은 유지합니다. / No dry route with sufficient clearance. Move the waypoints or request an avoiding route; water, mountains and buildings are preserved.");
        static void Append(List<int> target,List<int> source){foreach(int cell in source)if(target.Count==0 || target[target.Count-1]!=cell)target.Add(cell);}
        public static bool[] Clearance(int cols,int rows,bool[] ground,float radius)
        {
            var safe=(bool[])ground.Clone();int extent=(int)Math.Ceiling(radius);float sq=radius*radius;
            for(int i=0;i<ground.Length;i++)if(!ground[i])
            {
                int x=i%cols,z=i/cols;
                for(int dz=-extent;dz<=extent;dz++)for(int dx=-extent;dx<=extent;dx++)
                    if(dx*dx+dz*dz<=sq && x+dx>=0 && x+dx<cols && z+dz>=0 && z+dz<rows)
                        safe[(z+dz)*cols+x+dx]=false;
            }
            return safe;
        }
        static bool Visible(int cols,bool[] safe,int a,int b)
        {
            var line=Line(cols,a,b);
            for(int i=0;i<line.Count;i++)
            {
                if(!safe[line[i]])return false;
                if(i==0)continue;
                int x=line[i]%cols,z=line[i]/cols,px=line[i-1]%cols,pz=line[i-1]/cols;
                if(x!=px && z!=pz && (!safe[pz*cols+x] || !safe[z*cols+px]))return false;
            }
            return true;
        }
        public static List<int> Line(int cols,int a,int b)
        {
            var result=new List<int>();int x=a%cols,z=a/cols,bx=b%cols,bz=b/cols;
            int dx=Math.Abs(bx-x),dz=Math.Abs(bz-z),sx=x<bx?1:-1,sz=z<bz?1:-1,error=dx-dz;
            while(true)
            {
                result.Add(z*cols+x);if(x==bx && z==bz)break;
                int twice=2*error;if(twice>-dz){error-=dz;x+=sx;}if(twice<dx){error+=dx;z+=sz;}
            }
            return result;
        }
        static List<int> Find(int cols,int rows,bool[] safe,int start,int end)
        {
            if(!safe[start] || !safe[end])return null;
            var costs=Enumerable.Repeat(int.MaxValue,safe.Length).ToArray();var previous=Enumerable.Repeat(-1,safe.Length).ToArray();
            var closed=new bool[safe.Length];var heap=new Heap();costs[start]=0;heap.Push(start,H(start));
            while(heap.Count>0)
            {
                int current=heap.Pop();if(closed[current])continue;closed[current]=true;
                if(current==end)
                {var path=new List<int>();for(int p=end;p!=-1;p=previous[p])path.Add(p);path.Reverse();return path;}
                int x=current%cols,z=current/cols;
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0 && dz==0 || x+dx<0 || x+dx>=cols || z+dz<0 || z+dz>=rows)continue;
                    int next=(z+dz)*cols+x+dx;
                    if(!safe[next] || closed[next])continue;
                    if(dx!=0 && dz!=0 && (!safe[z*cols+x+dx] || !safe[(z+dz)*cols+x]))continue;
                    int g=costs[current]+(dx!=0 && dz!=0?14:10);
                    if(g>=costs[next])continue;costs[next]=g;previous[next]=current;heap.Push(next,g+H(next));
                }
            }
            return null;
            int H(int i){int dx=Math.Abs(i%cols-end%cols),dz=Math.Abs(i/cols-end/cols);return 10*Math.Max(dx,dz)+4*Math.Min(dx,dz);}
        }
        sealed class Heap
        {
            readonly List<Tuple<int,int,long>> data=new List<Tuple<int,int,long>>();long serial;
            public int Count=>data.Count;
            static bool Before(Tuple<int,int,long>a,Tuple<int,int,long>b)=>a.Item2<b.Item2 || a.Item2==b.Item2 && a.Item3<b.Item3;
            public void Push(int index,int score)
            {
                var value=Tuple.Create(index,score,serial++);data.Add(value);int p=data.Count-1;
                while(p>0){int parent=(p-1)/2;if(!Before(value,data[parent]))break;data[p]=data[parent];p=parent;}data[p]=value;
            }
            public int Pop()
            {
                int result=data[0].Item1;var last=data[data.Count-1];data.RemoveAt(data.Count-1);if(data.Count==0)return result;
                int p=0;
                while(p*2+1<data.Count)
                {int child=p*2+1;if(child+1<data.Count && Before(data[child+1],data[child]))child++;if(!Before(data[child],last))break;data[p]=data[child];p=child;}
                data[p]=last;return result;
            }
        }
    }
}
