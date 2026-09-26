using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // A shared curved footprint for ridges, open ground and water. Material and height
    // remain ordinary composite operations; no named-landform recipes or global RNG.
    public sealed class LandscapePath
    {
        readonly Vector2[] line;
        readonly float radius;
        readonly float[] radii;
        public LandscapePath(Vector2[] controls,float width,float natural=0,string id=null)
        {
            radius=width*.5f;
            var points=new List<Vector2>{controls[0]};
            for(int i=0;i<controls.Length-1;i++)
            {
                var a=controls[Math.Max(0,i-1)];var b=controls[i];
                var c=controls[i+1];var d=controls[Math.Min(controls.Length-1,i+2)];
                // Hermite interpolation with bounded tangents keeps broad bends smooth.
                var m0=(c-a)*.35f;var m1=(d-b)*.35f;
                for(int step=1;step<=8;step++)
                {
                    float t=step/8f,t2=t*t,t3=t2*t;
                    points.Add(b*(2*t3-3*t2+1)+m0*(t3-2*t2+t)+c*(-2*t3+3*t2)+m1*(t3-t2));
                }
            }
            line=points.ToArray();
            if(natural>0)
            {
                uint seed=2166136261u;
                unchecked{foreach(char c in id??"path")seed=(seed^c)*16777619u;}
                var lengths=new float[line.Length];
                for(int i=1;i<line.Length;i++)lengths[i]=lengths[i-1]+(line[i]-line[i-1]).magnitude;
                radii=new float[line.Length];
                for(int i=0;i<line.Length;i++)
                {
                    float along=lengths[i]/Mathf.Max(.001f,width),t=lengths[i]/Mathf.Max(.001f,lengths[line.Length-1]);
                    float shoulder=.75f*ContourWarp.Noise(along*.9f+2.7f,5.3f,seed)+.25f*ContourWarp.Noise(along*2.3f,1.1f,seed^0x9e3779b9u);
                    float end=Mathf.Abs(2*t-1);end*=end;end*=end;
                    radii[i]=radius*(1+natural*(.7f*shoulder-.35f*end));
                }
            }
        }
        public float Sample(Vector2 p)
        {
            float best=float.MaxValue;
            for(int i=1;i<line.Length;i++)
            {
                var e=line[i]-line[i-1];float length=Vector2.Dot(e,e);
                float t=length<1e-12f?0:Mathf.Clamp01(Vector2.Dot(p-line[i-1],e)/length);
                var delta=p-(line[i-1]+e*t);
                float distance=Vector2.Dot(delta,delta);
                best=Mathf.Min(best,radii==null?distance:Mathf.Sqrt(distance)-Mathf.Lerp(radii[i-1],radii[i],t));
            }
            return radii==null?Mathf.Sqrt(best)-radius:best;
        }
    }
}
