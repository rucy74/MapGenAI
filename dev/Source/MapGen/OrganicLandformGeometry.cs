using System;
using System.Globalization;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // Organic composition is opt-in in stored states and the default for NEW landform adds.
    // Large shoulders, related small gullies and a clear floor share one coordinate field.
    // No global random state, retries, terrain painting or model calls.
    public sealed class OrganicLandformGeometry
    {
        readonly string kind;
        readonly Vector2 center;
        readonly float span,gap,opening,cos,sin,phase,phase2;
        readonly uint seed;
        readonly float[] shoulders=new float[4],widths=new float[4],lengths=new float[4];
        readonly Gully[] gullies;
        struct Gully
        {
            public Vector2 start,first,second,end;
            public float width;
            public Gully(Vector2 start,Vector2 control,Vector2 end,float width)
            {
                this.start=start;this.end=end;this.width=width;
                first=Curve(start,control,end,1f/3);second=Curve(start,control,end,2f/3);
            }
            static Vector2 Curve(Vector2 a,Vector2 b,Vector2 c,float t)=>a*((1-t)*(1-t))+b*(2*(1-t)*t)+c*(t*t);
            public float Distance(float x,float z)=>SoftMin(SoftMin(Segment(x,z,start,first,width,width*.75f),Segment(x,z,first,second,width*.75f,width*.45f),.025f),Segment(x,z,second,end,width*.45f,.012f),.025f);
            static float Segment(float x,float z,Vector2 a,Vector2 b,float wa,float wb)
            {
                float dx=b.x-a.x,dz=b.y-a.y;
                float t=Mathf.Clamp01(((x-a.x)*dx+(z-a.y)*dz)/(dx*dx+dz*dz));
                float ox=x-a.x-dx*t,oz=z-a.y-dz*t;
                return(float)Math.Sqrt(ox*ox+oz*oz)-Mathf.Lerp(wa,wb,t);
            }
        }

        public OrganicLandformGeometry(ElevationShape shape)
        {
            kind=shape.landform;center=ElevationShape.ParsePosition(shape.position);
            span=NaturalLandformGeometry.Span(shape.size);gap=NaturalLandformGeometry.Gap(shape);
            opening=shape.opening==null?.14f:float.Parse(shape.opening,CultureInfo.InvariantCulture);
            float angle=ElevationShape.ParseDirection(shape.direction)*Mathf.Deg2Rad;
            cos=Mathf.Cos(angle);sin=Mathf.Sin(angle);
            seed=2166136261u;unchecked{foreach(char c in shape.variant??"0")seed=(seed^c)*16777619u;}
            phase=Pick(1)*6.2831853f;phase2=Pick(2)*6.2831853f;
            for(int i=0;i<4;i++)
            {
                shoulders[i]=-.31f+i*.205f+(Pick(3+i)-.5f)*.095f;
                widths[i]=.105f+Pick(7+i)*.10f;
                lengths[i]=.07f+Pick(11+i)*.12f;
            }
            gullies=new Gully[kind=="winding_valley"?4:kind=="foothills"?2:0];
            for(int i=0;i<gullies.Length;i++)
            {
                if(kind=="winding_valley")
                {
                    float root=shoulders[i],height=Bend(root),side=i%2==0?-1:1;
                    float length=.24f+.085f*Pick(20+i),sweep=(Pick(24+i)-.5f)*.15f;
                    gullies[i]=new Gully(new Vector2(root,height),new Vector2(root+sweep*.2f,height+side*length*.55f),new Vector2(root+sweep,height+side*length),.045f+.018f*Pick(28+i));
                }
                else
                {
                    float root=-.20f+i*.38f+(Pick(20+i)-.5f)*.07f,sweep=(i==0?-1:1)*(.10f+.08f*Pick(24+i));
                    gullies[i]=new Gully(new Vector2(-.42f,root),new Vector2(.10f,root-sweep*.35f),new Vector2(.25f+.10f*Pick(28+i),root+sweep),.04f+.02f*Pick(32+i));
                }
            }
        }

        float Pick(int index)
        {
            unchecked{uint h=seed^(uint)index*0x9e3779b9u;h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;h^=h>>16;return(h&0xffffffu)/16777215f;}
        }
        float Noise(float x,float z,float scale)=>ContourWarp.Noise(x*scale+13.7f,z*scale-8.3f,seed);
        float Bend(float along)=>.064f*Mathf.Sin(5*along+phase)+.024f*Mathf.Sin(10*along+phase2);
        static float Smooth(float a,float b,float x){float t=Mathf.Clamp01((x-a)/(b-a));return t*t*(3-2*t);}
        static float Bell(float x,float c,float w){float t=(x-c)/w;return(float)Math.Exp(-2*t*t);}
        // Rounded tributary junctions, instead of sharp capsule-shaped cuts in the main bank.
        // This only expands the floor; increasing gap still cannot remove existing floor cells.
        static float SoftMin(float a,float b,float radius)
        {
            float overlap=Mathf.Max(radius-Mathf.Abs(a-b),0)/radius;
            return Mathf.Min(a,b)-overlap*overlap*radius*.25f;
        }
        float Bays(float along,int side)
        {
            float result=0;
            for(int i=side;i<4;i+=2)result+=lengths[i]*.45f*Bell(along,shoulders[i],widths[i]);
            return result*(1-Smooth(.30f,.53f,Mathf.Abs(along)));
        }

        public NaturalLandformGeometry.Cell Sample(float x,float z)
        {
            float dx=(x-center.x)/span,dz=(z-center.y)/span;
            // The same deformation acts on the floor, banks and outer edge. Fine-scale
            // notches supplement a designed macro shape; they do not fill it with random bumps.
            for(int step=0;step<2;step++)
            {
                float a=dx,b=dz;
                dx+=.012f*(Noise(a,b,7)+.35f*Noise(a,b,17)+.12f*Noise(a,b,37))/1.47f;
                dz+=.012f*(Noise(a+3.1f,b-7.3f,7)+.35f*Noise(a+3.1f,b-7.3f,17)+.12f*Noise(a+3.1f,b-7.3f,37))/1.47f;
            }
            float u=dx*cos+dz*sin,v=-dx*sin+dz*cos;
            float distance,outer;
            if(kind=="open_basin")
            {
                // Wall contours stay in world orientation when only the exit moves.
                float angle=(float)Math.Atan2(dz,dx),radius=(float)Math.Sqrt(dx*dx+dz*dz);
                float contour=1+.07f*Mathf.Sin(2*angle+phase)+.05f*Mathf.Sin(3*angle+phase2)+.055f*Mathf.Sin(angle+phase);
                float bays=.018f*(1+Mathf.Sin(5*angle+phase2))+.006f*Noise(dx,dz,11);
                float inner=gap*contour+bays;
                float wall=.435f*contour+.036f*Noise(dx,dz,9)+.012f*Noise(dx,dz,23);
                // Reserve wall depth even at maximum supported floor width. This bound uses
                // that supported maximum, not current gap: widening never moves the outer wall.
                wall=Mathf.Max(wall,.32f*contour+bays+.11f);
                float exitCurve=.033f*(Mathf.Sin(u*7+phase2)-Mathf.Sin(phase2));
                float exitHalf=opening*.5f*(1+.65f*Smooth(.15f,.55f,u));
                float exitDistance=u>0?Mathf.Abs(v-exitCurve)-exitHalf:1;
                distance=Mathf.Min(radius-inner,exitDistance);outer=wall-radius;
            }
            else if(kind=="winding_valley")
            {
                float bend=Bend(u);
                int side=v>bend?1:0;
                // Independent shoulders open small side valleys instead of matching parallel walls.
                float half=gap*(1+.13f*Noise(u,side+2,5))+Bays(u,side)+.014f*Noise(u,side+4,13);
                distance=Mathf.Abs(v-bend)-half;
                float bankExtent=.41f+.065f*Noise(u,side+6,4)+.033f*Noise(u,v,11);
                outer=Mathf.Min(bankExtent-Mathf.Abs(v-bend),.85f-Mathf.Abs(u));
            }
            else
            {
                // Unequal, overlapping rounded shoulders avoid periodic sharp prongs.
                float fingers=0;
                for(int i=0;i<4;i++)fingers+=lengths[i]*Bell(v,shoulders[i],widths[i]);
                fingers*=1-Smooth(.23f,.48f,Mathf.Abs(v));
                float front=.19f+(gap-.25f)*1.25f-fingers+.035f*Noise(0,v,5)+.012f*Noise(0,v,15);
                distance=u-front;
                float side=(u-.27f)*.55f,along=v*.75f;
                outer=Mathf.Min(.62f-(float)Math.Sqrt(side*side+along*along),u+.49f);
            }
            foreach(var gully in gullies)distance=SoftMin(distance,gully.Distance(u,v),.10f);
            float influence=Smooth(0,.055f,outer);
            if(influence<=0)return default;
            float bank=Smooth(0,.075f,distance);
            float detail=.24f*Noise(dx,dz,8)+.18f*Noise(dx,dz,19)+.055f*Noise(dx,dz,43);
            return new NaturalLandformGeometry.Cell{influence=influence,elevation=.05f+bank*(1.02f+detail),floor=distance<=0 && influence>.999f};
        }
    }
}
