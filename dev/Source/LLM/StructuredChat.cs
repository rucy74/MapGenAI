using System;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    // A malformed answer never reaches map application. Retry formatting once, with the same request.
    public static class StructuredChat
    {
        public const string RepairInstruction = "Your previous answer did not match the required response format. Return exactly one complete JSON object: " +
            "{\"action\":\"ask\",\"message\":\"your explanation\"} or {\"action\":\"generate\",\"description\":\"summary\",\"params\":{...}}. " +
            "Answer the original user request using the same current state and feature/material catalogs. Explanations and unavailable-feature answers MUST use action ask, never plain prose. " +
            "Do not invent unavailable features or apply an alternative without the user's agreement. No text outside JSON. For recommendations or selectable alternatives use action recommend with options:[{params:{...}},...], 1..3 independent executable patches. Never offer unvalidated numbered concepts in ask.";

        // null = first attempt; otherwise the previous malformed answer, for a fresh repair history.
        public static async Task<string> SendAsync(Func<string,CancellationToken,Task<string>> send,CancellationToken token=default,bool requireRecommendations=false)
        {
            token.ThrowIfCancellationRequested();
            string response=await send(null,token);
            token.ThrowIfCancellationRequested();
            try{ValidateEnvelope(response,requireRecommendations);return response;}
            catch(FormatException){ }
            token.ThrowIfCancellationRequested();
            string repaired=await send(string.IsNullOrWhiteSpace(response)?"[empty response]":response,token);
            token.ThrowIfCancellationRequested();
            try{ValidateEnvelope(repaired,requireRecommendations);return repaired;}
            catch(FormatException error)
            {
                throw new FormatException("AI 응답 형식을 한 번 다시 요청했지만 올바른 JSON을 받지 못했습니다. 맵 설정은 변경하지 않았습니다. / The AI returned an invalid response after one format retry; no settings were changed.",error);
            }
        }
        public static void ValidateEnvelope(string response,bool requireRecommendations=false)
        {
            var command=ProviderResponse.Command(response);string action=command.GetString("action");
            if(action=="recommend"){RecommendationPlan.Options(command);return;}
            if(requireRecommendations)throw new FormatException("Return executable recommendation options, not prose or an immediate edit");
            if(action=="ask" && !string.IsNullOrWhiteSpace(command.GetString("message")) && !RecommendationPlan.IsNumberedOffer(command.GetString("message")))return;
            if(action=="generate" && command.GetObject("params")!=null)return;
            throw new FormatException("Expected ask with a question, generate with params, or recommend with executable options");
        }
    }
}
