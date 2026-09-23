using System.Collections.Generic;
using System.Linq;

namespace MapGenAI.LLM
{
    public static class RecommendationQuality
    {
        // A successfully drawn image is not proof that its requested placements succeeded.
        public static string Rejection(IEnumerable<string> placementFailures,int intendedWater,int actualWater,bool korean)
        {
            var failures=(placementFailures??new string[0]).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if(intendedWater>=25 && actualWater==0)failures.Add(korean?
                "새 물 영역을 요청했지만 해당 영역에 물이 생성되지 않았습니다.":
                "The proposed water area contains no generated water.");
            return failures.Count==0?null:string.Join("\n",failures);
        }
        public static string SelectionProblem(bool complete,string renderingError,string rejection,bool korean)
        {
            if(!complete)return korean?"미리보기 배치 확인이 끝나면 선택할 수 있습니다.":"Wait for the preview placement check before selecting.";
            // An unavailable renderer is not an observed invalid plan; preserve description-only selection.
            return rejection;
        }
    }
}
