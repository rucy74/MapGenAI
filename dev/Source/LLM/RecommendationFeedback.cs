using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;

namespace MapGenAI.LLM
{
    public static class RecommendationFeedback
    {
        public sealed class Choice
        {
            public readonly string Id, Label;
            public Choice(string id,string label){Id=id;Label=label;}
        }
        public static IReadOnlyList<Choice> Choices(bool korean,bool candidate)
        {
            var result=new List<Choice>{
                new Choice("natural",korean?"너무 인공적이에요":"More natural shapes"),
                new Choice("mountains",korean?"산이 너무 많아요":"Fewer mountains"),
                new Choice("more_water",korean?"물이 더 있었으면 해요":"More water"),
                new Choice("less_water",korean?"물이 너무 많아요":"Less water")};
            result.Add(candidate?new Choice("repair",korean?"실패한 배치를 고쳐 주세요":"Fix failed placement"):
                new Choice("different",korean?"후보들이 너무 비슷해요":"More variety"));
            return result;
        }
        public static string Request(bool korean,int candidate,string reason,string custom=null,string failure=null)
        {
            if(candidate<0 || candidate>3)throw new ArgumentOutOfRangeException(nameof(candidate));
            var options=Choices(korean,candidate>0);
            if(reason!=null && !options.Any(c=>c.Id==reason))throw new ArgumentException("Unknown feedback choice");
            string detail;
            switch(reason)
            {
                case "natural":detail=korean?"전체 배치와 필요한 통로 폭을 유지하면서, 도장 같은 원이나 사각형 대신 비대칭 윤곽과 자연스러운 굽이를 만들어 줘.":"Keep the overall layout and required passage width; use asymmetric outlines and natural bends instead of stamped circles or rectangles.";break;
                case "mountains":detail=korean?"새로 제안한 산의 면적을 줄이고 넓게 연결된 건설 공간을 늘려 줘. 기존 맵의 산은 임의로 지우지 마.":"Reduce the proposed mountain footprint and leave more connected building space. Do not remove existing mountains without permission.";break;
                case "more_water":detail=korean?"생활 공간을 남기면서 눈에 보이는 물가를 더해 줘. 세계지도에 없는 강이나 해안을 만들지는 마.":"Add noticeable waterside space while leaving room to build. Do not invent a world river or coast.";break;
                case "less_water":detail=korean?"새로 제안한 물 영역을 줄이고 연결된 육지를 늘려 줘. 기존 강과 바다는 유지해 줘.":"Reduce proposed water areas and leave more connected land. Preserve existing rivers and coast.";break;
                case "different":detail=korean?"방향만 돌린 비슷한 후보 대신, 건설 공간과 지형의 배치가 서로 다른 대안을 보여줘. 앞서 고른 필수 취향은 유지해 줘.":"Vary the layout of settlement space and landscape features, keeping prior requirements rather than just rotating similar options.";break;
                case "repair":detail=korean?"미리보기에서 실패한 배치를 고쳐 줘. 요구한 기능을 조용히 삭제하지 말고, 공간이 되는 위치나 경로로 조정해 줘.":"Fix the placement that failed in the preview. Keep the requested feature and adjust its location or route; do not silently delete it.";break;
                default:detail="";break;
            }
            string prefix=candidate>0?(korean?candidate+"번 후보만 수정해 줘. ":"Revise candidate "+candidate+" only. "):
                (korean?"현재 맵에 맞게 다른 후보로 다시 추천해 줘. ":"Recommend different options for the current map. ");
            string scope=korean?" 아직 맵에 적용하지 말고 후보 그림으로 보여줘. 다른 조건과 기존 맵 요소는 유지해 줘.":
                " Show candidate previews without applying them. Preserve other requirements and existing map elements.";
            string extra=string.IsNullOrWhiteSpace(custom)?"":"\n"+custom.Trim();
            if(extra.Length>2001)throw new ArgumentException("Feedback is too long");
            if(reason=="repair" && !string.IsNullOrWhiteSpace(failure))extra+="\n"+(korean?"미리보기에서 확인한 문제: ":"Observed preview issue: ")+failure;
            return prefix+detail+extra+scope;
        }
        // Derived from the validated plan, not an unverified model-written title.
        public static string Headline(string summary,bool korean)
        {
            var lines=(summary??"").Split('\n').Select(s=>s.Trim().TrimStart('•').Trim()).Where(s=>s.Length>0).ToList();
            var spatial=lines.Where(s=>s.Contains(" — ") && !s.Contains(" 추가 — ") && !s.Contains("added —") || s.Contains("산맥") || s.Contains("mountain range") || s.Contains("도로 ·") || s.Contains("road ·")).Take(2).ToList();
            string text=string.Join(" / ",spatial.Count>0?spatial:lines.Take(1));
            return text.Length<=88?text:text.Substring(0,85)+"…";
        }
        public static string ResponseProblem(int target,string response)
        {
            if(target==0)return null;
            var cmd=ProviderResponse.Command(response);
            if(target<0)return cmd.GetString("action")=="recommend"?null:
                "Return recommendation candidates without applying the map.";
            return cmd.GetString("action")=="ask" || cmd.GetString("action")=="revise" && cmd.GetInt("option")==target?null:
                "Revise only candidate "+target+" or ask a clarification. Do not replace other candidates or apply the map.";
        }
    }
}
