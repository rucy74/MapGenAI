using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse.Noise;

namespace MapGenAI.MapGen
{
    // Local adapter for the engine's displacement noise. The same signed field drives
    // water, submerged shelves and the dry shore; no global lake worker or RNG state.
    public sealed class NativeWaterField
    {
        readonly ModuleBase field;
        readonly float cols, rows, cellScale;
        public readonly float Shelf, Shore;

        public NativeWaterField(Func<Vector2,float> sdf,List<ShapePrimitive> parts,float cols,float rows,float roughness,string identity)
        {
            this.cols=cols;this.rows=rows;cellScale=Mathf.Min(cols,rows);
            float width=MinimumWidth(parts)*cellScale;
            // Lake's .75/.5/.45 thresholds expressed as signed distance from its edge.
            Shelf=Mathf.Max(1f,width*.25f)/cellScale;
            Shore=Mathf.Clamp(width*.05f,1f,6f)/cellScale;
            uint hash=2166136261u;
            unchecked { foreach(char c in identity??"water")hash=(hash^c)*16777619u; }
            int seed=(int)(hash&0x7ffffffe);
            field=new SdfField(sdf,cols,rows);
            // Scale the native two displacement bands to the narrow dimension of the
            // requested feature, rather than applying a full-map lake's 40-cell warp.
            float scale=Mathf.Max(8f,width),amount=roughness/.65f;
            // At the engine lake's approximate 150-cell water diameter these are its
            // .006/40 and .015/15 bands. Medium roughness keeps that relative scale.
            float coarse=Mathf.Min(1f/12f,.9f/scale),fine=Mathf.Min(1f/12f,2.25f/scale);
            int coarseOctaves=1,fineOctaves=ResolvedOctaves(fine,2);
            // Compose small coordinate displacements rather than folding a narrow pool
            // in one large jump. Keep large bends, but resolve no wavelength finer
            // than twelve map cells: compression can otherwise alias tiny gaps.
            const int steps=4;
            for(int step=0;step<steps;step++)
            {
                field=MapNoiseUtility.AddDisplacementNoise(field,coarse,Mathf.Min(40f,width*(40f/150f))*amount/steps,coarseOctaves,seed);
                field=MapNoiseUtility.AddDisplacementNoise(field,fine,Mathf.Min(15f,width*.10f)*amount/steps,fineOctaves,seed);
            }
        }

        public float Sample(Vector2 p)=>(float)field.GetValue(p.x*cols,0,p.y*rows);

        static int ResolvedOctaves(float frequency,int maximum)
        {
            int result=1;
            while(result<maximum && frequency*2<=1f/12f){frequency*=2;result++;}
            return result;
        }

        static float MinimumWidth(List<ShapePrimitive> parts)
        {
            float width=float.MaxValue;
            foreach(var part in parts)
            {
                float span;
                switch(part.prim)
                {
                    case "circle": span=2*part.r;break;
                    case "star": span=2*(part.r2>0?part.r2:part.r*.4f);break;
                    case "ellipse": case "rect": span=Mathf.Min(part.w,part.h);break;
                    case "path": span=part.w;break;
                    case "heart": span=part.size>0?part.size:.3f;break;
                    default:
                        float x0=1f,x1=0f,z0=1f,z1=0f;
                        foreach(var p in part.verts){x0=Mathf.Min(x0,p[0]);x1=Mathf.Max(x1,p[0]);z0=Mathf.Min(z0,p[1]);z1=Mathf.Max(z1,p[1]);}
                        span=Mathf.Min(x1-x0,z1-z0);break;
                }
                width=Mathf.Min(width,span);
            }
            return Mathf.Max(.002f,width);
        }

        sealed class SdfField : ModuleBase
        {
            readonly Func<Vector2,float> sdf;readonly float cols,rows;
            public SdfField(Func<Vector2,float> sdf,float cols,float rows):base(0)
            {this.sdf=sdf;this.cols=cols;this.rows=rows;}
            public override double GetValue(double x,double y,double z)=>sdf(new Vector2((float)x/cols,(float)z/rows));
        }
    }
}
