using System;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // A small surface layer following actual generated water/rock boundaries. It never
    // changes height, water, paths or region masks, and consumes no global random state.
    public sealed class LandscapeBlendField
    {
        public enum Ground { Keep, Sand, Gravel, Soil }
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
        // Native asphalt-road shoulders are untagged Gravel. Preserve all existing gravel
        // rather than guessing whether it came from a road or the biome.
        public static bool OrdinaryGround(string name)=>name=="Soil" || name=="Sand";
    }
}
