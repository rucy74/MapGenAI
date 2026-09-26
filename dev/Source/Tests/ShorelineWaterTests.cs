using System;
using MapGenAI.MapGen;
using static CoreRegressionTests;

static class ShorelineWaterTests
{
    public static void RunAll()
    {
        // Break: use all freshwater for the shore distance; a separate pool attracts the bank.
        Check("Banks follow connected feathered water without borrowing a separate pool",()=>
        {
            const int w=25,h=15;var sources=new bool[w*h];var water=new bool[w*h];var blocked=new bool[w*h];
            sources[7*w+5]=true;
            for(int z=6;z<=8;z++)for(int x=4;x<=7;x++)water[z*w+x]=true;
            var alone=new ShorelineWater(w,h,sources,water,blocked);
            water[7*w+10]=true;water[8*w+10]=true;
            var near=new ShorelineWater(w,h,sources,water,blocked);
            for(int i=0;i<w*h;i++)Equal(alone.WaterDistance.squared[i],near.WaterDistance.squared[i]);
            Equal(1f,near.WaterDistance.squared[7*w+8]); // Actual feather edge at x=7, not the authored x=5.
            Equal(4f,near.WaterDistance.squared[7*w+9]);
            Equal(false,near.ConnectedWater[7*w+10]);
            water[7*w+8]=true;water[7*w+9]=true;
            var joined=new ShorelineWater(w,h,sources,water,blocked);
            Equal(true,joined.ConnectedWater[7*w+10]);Equal(1f,joined.WaterDistance.squared[7*w+11]);
        });
        // Break: permit diagonal jumps, route through a protected pool or follow an entire river.
        Check("Protected and diagonal water cannot relay bank effects beyond local permission",()=>
        {
            const int w=30,h=20;var sources=new bool[w*h];var water=new bool[w*h];var blocked=new bool[w*h];
            sources[10*w+5]=true;for(int x=5;x<=25;x++)water[10*w+x]=true;
            water[11*w+4]=true;
            var open=new ShorelineWater(w,h,sources,water,blocked);
            Equal(true,open.ConnectedWater[10*w+11]);Equal(false,open.ConnectedWater[10*w+12]);
            Equal(false,open.ConnectedWater[11*w+4]);
            blocked[10*w+8]=true;
            var fenced=new ShorelineWater(w,h,sources,water,blocked);
            Equal(true,fenced.ConnectedWater[10*w+7]);Equal(false,fenced.ConnectedWater[10*w+9]);
            blocked[10*w+5]=true;
            var none=new ShorelineWater(w,h,sources,water,blocked);
            Equal(false,none.OriginDistance.HasTarget);Equal(false,none.WaterDistance.HasTarget);Equal(0f,none.Influence(0));
        });
        // Break: hard material cutoff or expansion of the permitted source radius.
        Check("Bank influence tapers near the local boundary without altering the inner shore",()=>
        {
            const int w=20,h=10;var sources=new bool[w*h];var water=new bool[w*h];
            sources[5*w+5]=water[5*w+5]=true;
            var shore=new ShorelineWater(w,h,sources,water,new bool[w*h]);
            Equal(1f,shore.Influence(5*w+9));Equal(.5f,shore.Influence(5*w+10));
            Equal(0f,shore.Influence(5*w+11));Equal(0f,shore.Influence(5*w+18));
            int normal=0,tapered=0,none=0;
            for(int seed=0;seed<20;seed++)for(int z=0;z<20;z++)for(int x=0;x<20;x++)
            {
                var field=new LandscapeBlendField(seed.ToString());
                if(field.SampleShore(x,z,2,20,true).ground!=LandscapeBlendField.Ground.Keep)normal++;
                if(field.SampleShore(x,z,2,20,true,.5f).ground!=LandscapeBlendField.Ground.Keep)tapered++;
                if(field.SampleShore(x,z,2,20,true,0).ground!=LandscapeBlendField.Ground.Keep)none++;
            }
            Equal(0,none);Equal(true,normal>tapered);Equal(true,tapered>0);
        });
        Check("Forest banks keep native soil while sand and rocky banks retain fitting materials",()=>
        {
            var sand=LandscapeBlendField.Ground.Sand;var gravel=LandscapeBlendField.Ground.Gravel;
            Equal(LandscapeBlendField.Ground.Keep,LandscapeBlendField.BankMaterial(sand,"Soil",false,false,20));
            Equal(sand,LandscapeBlendField.BankMaterial(sand,"Sand",false,false,20));
            Equal(sand,LandscapeBlendField.BankMaterial(sand,"Soil",true,false,20));
            Equal(LandscapeBlendField.Ground.Keep,LandscapeBlendField.BankMaterial(gravel,"Soil",false,false,20));
            Equal(gravel,LandscapeBlendField.BankMaterial(gravel,"Sand",true,false,2));
            Equal(gravel,LandscapeBlendField.BankMaterial(gravel,"Soil",false,true,20));
            Equal(LandscapeBlendField.Ground.Mud,LandscapeBlendField.BankMaterial(LandscapeBlendField.Ground.Mud,"Soil",false,false,20));
        });
    }
}
