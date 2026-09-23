using System;
using System.Globalization;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // One deterministic layout owns both mountain walls and usable ground. No global RNG,
    // provider call, material painting or world-feature changes are performed here.
    public sealed class NaturalLandformGeometry
    {
        public struct Cell
        {
            public float influence, elevation;
            public bool floor;
        }

        readonly string kind;
        readonly Vector2 center;
        readonly float span, gap, opening, cos, sin, phase, phase2;
        readonly uint seed;
        readonly OrganicLandformGeometry organic;

        public NaturalLandformGeometry(ElevationShape shape)
        {
            if(shape.layout=="organic"){organic=new OrganicLandformGeometry(shape);return;}
            kind=shape.landform; center=ElevationShape.ParsePosition(shape.position);
            span=Span(shape.size); gap=Gap(shape); opening=Value(shape.opening,.14f);
            float angle=ElevationShape.ParseDirection(shape.direction)*Mathf.Deg2Rad;
            cos=Mathf.Cos(angle); sin=Mathf.Sin(angle);
            // Persisted variant controls the layout; a later move/resize never re-rolls it.
            seed=2166136261u;
            unchecked { foreach(char c in shape.variant??"0")seed=(seed^c)*16777619u; }
            phase=(seed%997)/997f*6.2831853f;
            phase2=((seed>>10)%991)/991f*6.2831853f;
        }

        public static float Span(string size) => size=="small"?.55f:size=="medium"?.75f:size=="large"?.95f:Value(size,.9f);
        public static float Gap(ElevationShape s) => Value(s.gap,s.landform=="winding_valley"?.14f:.25f);
        static float Value(string value,float fallback) => value==null?fallback:float.Parse(value,CultureInfo.InvariantCulture);
        static float Smooth(float a,float b,float x) { float t=Mathf.Clamp01((x-a)/(b-a));return t*t*(3-2*t); }
        float Noise(float u,float v,float scale) => ContourWarp.Noise(u*scale+13.7f,v*scale-8.3f,seed);

        public Cell Sample(float x,float z)
        {
            if(organic!=null)return organic.Sample(x,z);
            float dx=(x-center.x)/span, dz=(z-center.y)/span;
            // Deform the whole field together, so a valley floor and its walls never use
            // mismatching edge noise. Two small smooth steps avoid folding the coordinates.
            for(int step=0;step<2;step++)
            {
                float a=dx,b=dz;
                dx+=.018f*(Noise(a,b,6)+.35f*Noise(a,b,13))/1.35f;
                dz+=.018f*(Noise(a+3.1f,b-7.3f,6)+.35f*Noise(a+3.1f,b-7.3f,13))/1.35f;
            }
            float u=dx*cos+dz*sin, v=-dx*sin+dz*cos;
            float floorDistance, outerDistance;
            if(kind=="open_basin")
            {
                // Basin direction changes only its exit. The asymmetric walls stay put.
                float angle=(float)Math.Atan2(dz,dx), radius=(float)Math.Sqrt(dx*dx+dz*dz);
                // Low-frequency asymmetry changes the whole bowl, rather than just its edge.
                float contour=1+.12f*Mathf.Sin(2*angle+phase)+.075f*Mathf.Sin(3*angle+phase2)+.025f*Mathf.Cos(5*angle-phase);
                float inner=gap*contour;
                float outer=.425f*contour+.025f*Noise(dx,dz,14);
                float exitCurve=.027f*(Mathf.Sin(u*9+phase2)-Mathf.Sin(phase2));
                float exitHalf=opening*.5f*(1+.55f*Smooth(.15f,.55f,u));
                float exitDistance=u>0?Mathf.Abs(v-exitCurve)-exitHalf:1;
                floorDistance=Mathf.Min(radius-inner,exitDistance);
                outerDistance=outer-radius;
            }
            else if(kind=="winding_valley")
            {
                float bend=.085f*Mathf.Sin(5*u+phase)+.035f*Mathf.Sin(10*u+phase2);
                float halfWidth=gap*(1+.14f*Mathf.Sin(7*u+phase2));
                floorDistance=Mathf.Abs(v-bend)-halfWidth;
                float bankWidth=.47f+.065f*Mathf.Sin(5*u+phase2+(v>0?1.6f:0))+.025f*Noise(u,v,12);
                outerDistance=Mathf.Min(bankWidth-Mathf.Abs(v-bend),.85f-Mathf.Abs(u));
            }
            else // foothills: a backbone with unequal spurs, beside a connected open plain.
            {
                float spur=Mathf.Max(0,Mathf.Cos(13*v+phase)); spur*=spur;
                // Taper outer spurs before the map edge. Long diagonal fingers can otherwise
                // cut off a floor pocket at a corner, even with a centered layout.
                float front=.23f+(gap-.25f)*1.25f-.28f*spur*(1-Smooth(.27f,.52f,Mathf.Abs(v)))+.025f*Noise(0,v,5);
                floorDistance=u-front;
                float side=(u-.27f)*.55f,along=v*.75f;
                outerDistance=.62f-(float)Math.Sqrt(side*side+along*along);
                // Only clear the planned plain; preserve terrain outside the footprint.
                outerDistance=Mathf.Min(outerDistance,u+.49f);
            }

            float influence=Smooth(0,.045f,outerDistance);
            if(influence<=0)return default;
            float bank=Smooth(0,.075f,floorDistance);
            float detail=.35f*Noise(dx,dz,8)+.15f*Noise(dx,dz,24);
            return new Cell
            {
                influence=influence,
                elevation=.05f+bank*(1.03f+detail),
                floor=floorDistance<=0 && influence>.999f
            };
        }
    }
}
