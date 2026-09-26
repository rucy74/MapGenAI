using System;
using System.Linq;
using MapGenAI.MapGen;
using static CoreRegressionTests;

static class NativeWaterRasterTests
{
    public static void RunAll()
    {
        // Break: using the warped SDF as physical distance, skipping the shallow shelf,
        // or painting all water deep. Expected 5x5 core is derived from a 7x7 square.
        Check("Native water shelves use final raster distance and retain the water footprint",()=>
        {
            var water=new bool[121];for(int z=2;z<=8;z++)for(int x=2;x<=8;x++)water[z*11+x]=true;
            var original=(bool[])water.Clone();var deep=NativeWaterRaster.DeepMask(11,11,water,2f);
            Equal(25,deep.Count(x=>x));Equal(true,water.SequenceEqual(original));
            for(int z=0;z<11;z++)for(int x=0;x<11;x++)Equal(x>=3&&x<=7&&z>=3&&z<=7,deep[z*11+x]);
            // A shelf planned for a larger pre-warp lake scales to this actual 4-cell inradius.
            Equal(25,NativeWaterRaster.DeepMask(11,11,water,4f).Count(x=>x));
        });
        // Break: unbuffered deep tiles at a narrow channel or a protected island edge.
        Check("Native depth keeps thin water shallow and buffers dry islands on all eight sides",()=>
        {
            var water=new bool[225];for(int z=0;z<15;z++)for(int x=6;x<=7;x++)water[z*15+x]=true;
            Equal(0,NativeWaterRaster.DeepMask(15,15,water,1).Count(x=>x));
            for(int i=0;i<water.Length;i++)water[i]=true;water[7*15+7]=false;
            var deep=NativeWaterRaster.DeepMask(15,15,water,1);
            Equal(true,deep.Count(x=>x)>0);
            for(int z=6;z<=8;z++)for(int x=6;x<=8;x++)Equal(false,deep[z*15+x]);
            Equal(0,NativeWaterRaster.DeepMask(15,15,new bool[225],2).Count(x=>x));
        });
    }
}
