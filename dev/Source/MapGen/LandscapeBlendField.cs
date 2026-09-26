using System;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // A small surface layer following actual generated water/rock boundaries. It never
    // changes height, water, paths or region masks, and consumes no global random state.
    public sealed class LandscapeBlendField
    {
        public enum Ground { Keep, Sand, Gravel, Soil, Mud }
        public struct Cell { public Ground ground; public float vegetation; }
        readonly uint seed;
        public LandscapeBlendField(string variant)
        {seed=2166136261u;unchecked{foreach(char c in variant??"0")seed=(seed^c)*16777619u;}}
        public Cell Sample(int x,int z,float waterDistance,float rockDistance,bool soilBiome)
        {
            float patches=.72f*ContourWarp.Noise(x*.055f+13.2f,z*.055f-7.6f,seed)+
                .28f*ContourWarp.Noise(x*.17f,z*.17f,seed^0x9e3779b9u);
            float shore=2.6f+1.1f*patches,foot=3.4f+2.0f*patches;
            float damp=Mathf.Clamp01(1-waterDistance/10f),stony=Mathf.Clamp01(1-rockDistance/10f);
            var ground=Ground.Keep;
            // Narrow mixed banks rather than a uniform broad ring or a rich-soil resource disk.
            if(waterDistance<=shore)ground=rockDistance<4 || patches<-.2f?Ground.Gravel:Ground.Sand;
            else if(rockDistance<=foot || rockDistance<9 && patches>.40f)ground=Ground.Gravel;
            else if(soilBiome && waterDistance<7+2*patches)ground=Ground.Soil;
            // Native biome still chooses plant species and viability; only initial local density varies.
            float plants=Mathf.Clamp(.86f+.32f*patches+.28f*damp-.28f*stony,.38f,1.22f);
            return new Cell{ground=ground,vegetation=plants};
        }
        // Dry-side banks only: narrow, interrupted patches leave buildable approaches to the water.
        // Mud has no heavy-building support in RimWorld, so it is restricted to damp soil at the edge.
        public Cell SampleShore(int x,int z,float waterDistance,float rockDistance,bool dampSoil)
        {
            var keep=new Cell{ground=Ground.Keep,vegetation=1f};
            if(waterDistance<=0 || waterDistance>6)return keep;
            float broad=ContourWarp.Noise(x*.045f+31.7f,z*.045f-11.2f,seed^0xa511e9b3u);
            float patches=ContourWarp.Noise(x*.12f-8.3f,z*.12f+5.1f,seed^0x63d83595u);
            float width=3.3f+2f*broad;
            if(waterDistance>width || broad>.48f || waterDistance>1.6f && patches>.28f)return keep;
            Ground ground;
            if(dampSoil && rockDistance>3.5f && waterDistance<=1.7f+.6f*broad && broad>-.15f && patches<.12f)
                ground=Ground.Mud;
            else if(rockDistance<3.5f || patches<-.3f)ground=Ground.Gravel;
            else ground=Ground.Sand;
            return new Cell{ground=ground,vegetation=Mathf.Clamp(.95f+.10f*broad, .8f,1.08f)};
        }
        public static bool DampSoilBank(bool soilBiome,bool localFertilityPlants,float temperature,float rainfall,string ground)
            =>soilBiome && localFertilityPlants && temperature>0 && rainfall>=600 && ground=="Soil";
        // Special waters (hot springs, marsh, lava and mod materials) keep their own banks.
        public static bool OrdinaryFreshwater(string name)=>name=="WaterDeep" || name=="WaterShallow" ||
            name=="WaterMovingShallow" || name=="WaterMovingChestDeep";
        // Native asphalt-road shoulders are untagged Gravel. Preserve all existing gravel
        // rather than guessing whether it came from a road or the biome.
        public static bool OrdinaryGround(string name)=>name=="Soil" || name=="Sand";
    }
}
