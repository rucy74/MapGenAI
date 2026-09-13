using System;
using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    public sealed class SpatialRelation
    {
        public string target, side="any";
        public int min_distance=0,max_distance=12;
        public SpatialRelation Clone()=>new SpatialRelation{target=target,side=side,min_distance=min_distance,max_distance=max_distance};
    }

    // Exact squared Euclidean distance transform, including nearest target coordinates.
    // Two linear passes avoid scanning every river/mountain cell for every candidate.
    public sealed class SpatialDistance
    {
        public readonly int cols,rows;
        public readonly float[] squared;
        public readonly int[] nearestX,nearestZ;
        public bool HasTarget {get;private set;}
        public SpatialDistance(int cols,int rows,bool[] target)
        {
            if(cols<1 || rows<1 || target.Length!=cols*rows)throw new ArgumentException("Distance mask dimensions differ");
            this.cols=cols;this.rows=rows;squared=new float[target.Length];nearestX=new int[target.Length];nearestZ=new int[target.Length];
            int n=Math.Max(cols,rows);var f=new float[n];var d=new float[n];var nearest=new int[n];var horizontal=new float[target.Length];var sourceX=new int[target.Length];
            for(int z=0;z<rows;z++)
            {
                for(int x=0;x<cols;x++){bool on=target[z*cols+x];HasTarget|=on;f[x]=on?0:1e12f;}
                Transform(f,cols,d,nearest);
                for(int x=0;x<cols;x++){horizontal[z*cols+x]=d[x];sourceX[z*cols+x]=nearest[x];}
            }
            for(int x=0;x<cols;x++)
            {
                for(int z=0;z<rows;z++)f[z]=horizontal[z*cols+x];
                Transform(f,rows,d,nearest);
                for(int z=0;z<rows;z++){int i=z*cols+x,nz=nearest[z];squared[i]=d[z];nearestZ[i]=nz;nearestX[i]=sourceX[nz*cols+x];}
            }
        }
        static void Transform(float[] f,int n,float[] d,int[] nearest)
        {
            var sites=new int[n];var borders=new double[n+1];int k=0;sites[0]=0;borders[0]=double.NegativeInfinity;borders[1]=double.PositiveInfinity;
            for(int q=1;q<n;q++)
            {
                double cross;
                while(true)
                {
                    int v=sites[k];cross=((double)f[q]+q*q-f[v]-v*v)/(2.0*(q-v));
                    if(cross>borders[k] || k==0)break;k--;
                }
                k++;sites[k]=q;borders[k]=cross;borders[k+1]=double.PositiveInfinity;
            }
            k=0;
            for(int q=0;q<n;q++){while(borders[k+1]<q)k++;int delta=q-sites[k];d[q]=delta*delta+f[sites[k]];nearest[q]=sites[k];}
        }
        public static bool[] InteriorEdge(int cols,int rows,bool[] region)
        {
            var edge=new bool[region.Length];
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
            {
                int i=z*cols+x;
                edge[i]=region[i] && (x==0 || z==0 || x==cols-1 || z==rows-1 || !region[i-1] || !region[i+1] || !region[i-cols] || !region[i+cols]);
            }
            return edge;
        }
        public Func<PlannedRect,bool> Constrain(bool[] allowed,SpatialRelation relation)
        {
            if(!HasTarget)throw new InvalidOperationException("배치 기준 지형이 없습니다 / Placement target is absent: "+relation.target);
            var near=new int[(cols+1)*(rows+1)];int stride=cols+1;
            float min=relation.min_distance*relation.min_distance,max=relation.max_distance*relation.max_distance;
            for(int z=0;z<rows;z++)for(int x=0;x<cols;x++)
            {
                int i=z*cols+x;
                allowed[i] &= squared[i]>=min;
                near[(z+1)*stride+x+1]=(squared[i]<=max?1:0)+near[(z+1)*stride+x]+near[z*stride+x+1]-near[z*stride+x];
            }
            return r=>
            {
                int close=near[(r.z+r.height)*stride+r.x+r.width]-near[r.z*stride+r.x+r.width]-near[(r.z+r.height)*stride+r.x]+near[r.z*stride+r.x];
                if(close==0)return false;
                int x=r.x+(r.width-1)/2,z=r.z+(r.height-1)/2,i=z*cols+x;
                switch(relation.side)
                {
                    case "east":return x>nearestX[i];
                    case "west":return x<nearestX[i];
                    case "north":return z>nearestZ[i];
                    case "south":return z<nearestZ[i];
                    default:return true;
                }
            };
        }
    }
}

