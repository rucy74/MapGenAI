using System;
using System.Collections.Generic;
using System.Linq;

namespace MapGenAI.MapGen
{
    // Count actual eligible cells. Geometry size and a feathered edge are not area percentages.
    public static class RegionCoverage
    {
        public static void ValidateReferences(TileMapState state)
        {
            foreach(var fill in state.elevationShapes.Where(s=>s.type=="region_fill"))
            {
                var source=state.elevationShapes.Find(s=>s.id==fill.region);
                if(source==null || (source.type!="composite" && source.type!="bump" && source.type!="ring" && source.type!="landform"))
                    throw new FormatException("채움의 기준 영역이 없습니다. 함께 제거하거나 다시 지정하세요. / Missing fill source; remove or rebind the fill too: "+fill.region);
                if(source.type=="landform" && fill.region_part!="inside")throw new FormatException("자연지형의 평지는 inside로 지정하세요. / Use inside for a landform's planned floor.");
            }
        }
        public static bool[] Enclosed(int cols,int rows,bool[] boundary)
        {
            if(boundary.Length!=cols*rows)throw new ArgumentException("Region dimensions differ");
            var outside=new bool[boundary.Length];var queue=new Queue<int>();
            void Visit(int i){if(!boundary[i] && !outside[i]){outside[i]=true;queue.Enqueue(i);}}
            for(int x=0;x<cols;x++){Visit(x);Visit((rows-1)*cols+x);}
            for(int z=0;z<rows;z++){Visit(z*cols);Visit(z*cols+cols-1);}
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),x=i%cols,z=i/cols;
                if(x>0)Visit(i-1);if(x+1<cols)Visit(i+1);if(z>0)Visit(i-cols);if(z+1<rows)Visit(i+cols);
            }
            return boundary.Select((b,i)=>!b && !outside[i]).ToArray();
        }
        public static bool[] Select(int cols,int rows,bool[] eligible,bool[] existing,float fraction,string direction=null)
        {
            if(eligible.Length!=cols*rows || existing.Length!=eligible.Length)throw new ArgumentException("Coverage dimensions differ");
            ShapeValidation.Range(fraction,0,1,"coverage");
            var candidates=Enumerable.Range(0,eligible.Length).Where(i=>eligible[i]).ToList();
            if(candidates.Count==0)throw new InvalidOperationException("채울 수 있는 영역이 없습니다. 닫힌 내부 공간과 지형 조건을 확인하세요. / No eligible region cells; check the enclosed area and terrain conditions.");
            int count=(int)Math.Round(candidates.Count*(double)fraction,MidpointRounding.AwayFromZero);
            if(candidates.Count(i=>existing[i])>count)
                throw new InvalidOperationException("원래 지형의 해당 재료가 요청 비율보다 많습니다. 남은 부분을 어떤 재료로 바꿀지 지정하세요. / Existing material exceeds the requested fraction; specify a replacement for the remainder.");
            var depth=new SpatialDistance(cols,rows,SpatialDistance.InteriorEdge(cols,rows,eligible));
            float Score(int i)=>direction=="left"?-(i%cols):direction=="right"?i%cols:direction=="top"?i/cols:direction=="bottom"?-(i/cols):depth.squared[i];
            candidates.Sort((a,b)=>{int prior=existing[b].CompareTo(existing[a]);if(prior!=0)return prior;int order=Score(b).CompareTo(Score(a));return order!=0?order:a.CompareTo(b);});
            var selected=new bool[eligible.Length];for(int n=0;n<count;n++)selected[candidates[n]]=true;
            return selected;
        }
    }
}
