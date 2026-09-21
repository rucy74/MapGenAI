using System;
using System.Linq;
using MapGenAI.MapGen;
using static CoreRegressionTests;

static class RoadBridgeRoutingTests
{
    public static void RunAll()
    {
        const int w=41,h=31;
        var points=new[]{new[]{.075f,.5f},new[]{.925f,.5f}};
        Check("Bridge fallback crosses a river that separates the two banks",()=>
        {
            var ground=Enumerable.Repeat(true,w*h).ToArray();var water=new bool[w*h];
            for(int z=0;z<h;z++)for(int x=18;x<=22;x++){ground[z*w+x]=false;water[z*w+x]=true;}
            var saved=(bool[])ground.Clone();
            foreach(var mode in new[]{"avoid","direct"})
            {
                Reject(()=>RoadRouting.Plan(w,h,ground,points,mode,1.9f));
                var path=RoadRouting.PlanWithBridges(w,h,ground,water,points,mode,1.9f);
                Equal(true,path.SequenceEqual(Enumerable.Range(3,35).Select(x=>15*w+x)));
            }
            Equal(true,saved.SequenceEqual(ground));
        });
        Check("Existing dry detours remain unchanged even when a shorter bridge is possible",()=>
        {
            var ground=Enumerable.Repeat(true,w*h).ToArray();var water=new bool[w*h];
            for(int z=7;z<=23;z++)for(int x=18;x<=22;x++){ground[z*w+x]=false;water[z*w+x]=true;}
            var old=RoadRouting.Plan(w,h,ground,points,"avoid",1.9f);
            var current=RoadRouting.PlanWithBridges(w,h,ground,water,points,"avoid",1.9f);
            Equal(true,current.SequenceEqual(old));Equal(true,current.All(i=>!water[i]));
            Equal(true,RoadRouting.PlanWithBridges(w,h,ground,water,points,"direct",1.9f).Any(i=>water[i]));
        });
        Check("Bridge fallback cannot turn unsupported barriers into a route",()=>
        {
            var ground=Enumerable.Repeat(true,w*h).ToArray();var water=new bool[w*h];
            for(int z=0;z<h;z++){ground[z*w+12]=false;water[z*w+12]=true;ground[z*w+28]=false;}
            foreach(var mode in new[]{"avoid","direct"})Reject(()=>RoadRouting.PlanWithBridges(w,h,ground,water,points,mode,1.9f));
        });
        Check("Bridge roads need supported land endpoints instead of ending in water",()=>
        {
            var ground=Enumerable.Repeat(true,w*h).ToArray();var water=new bool[w*h];
            for(int z=0;z<h;z++)for(int x=30;x<w;x++){ground[z*w+x]=false;water[z*w+x]=true;}
            foreach(var mode in new[]{"avoid","direct"})Reject(()=>RoadRouting.PlanWithBridges(w,h,ground,water,points,mode,1.9f));
        });
        Check("Bridge fallback still respects mountain clearance and all ordered waypoints",()=>
        {
            var ground=Enumerable.Repeat(true,w*h).ToArray();var water=new bool[w*h];
            for(int z=0;z<h;z++){ground[z*w+10]=false;water[z*w+10]=true;}
            for(int z=11;z<=19;z++)for(int x=23;x<=28;x++)ground[z*w+x]=false;
            var path=RoadRouting.PlanWithBridges(w,h,ground,water,points,"avoid",1.9f);
            Equal(15*w+3,path.First());Equal(15*w+37,path.Last());
            Equal(true,path.Any(i=>water[i]));
            foreach(int i in path)
            for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
            {
                int x=i%w+dx,z=i/w+dz;if(x<0||x>=w||z<0||z>=h||dx*dx+dz*dz>4)continue;
                Equal(true,ground[z*w+x]||water[z*w+x]);
            }
        });
    }
    static void Reject(Action action)
    {try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected a blocked route");}
}
