using System;
using System.Linq;
using MapGenAI.LLM;
using static CoreRegressionTests;

static class RecommendationFeedbackTests
{
    public static void RunAll()
    {
        Check("Feedback routes batches to new proposals and individual edits to pending candidates",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                foreach(var choice in RecommendationFeedback.Choices(ko,false))
                {
                    var request=RecommendationFeedback.Request(ko,0,choice.Id);
                    Equal(true,RecommendationPlan.RequestsNewOptions(request));Equal(true,RecommendationPlan.IsRequest(request));
                    Equal(false,RecommendationPlan.RequestsDirectEdit(request));Equal(-1,RecommendationPlan.Selection(request));
                }
                foreach(var choice in RecommendationFeedback.Choices(ko,true))
                {
                    var request=RecommendationFeedback.Request(ko,2,choice.Id);
                    Equal(false,RecommendationPlan.RequestsNewOptions(request));Equal(false,RecommendationPlan.IsDismissal(request));Equal(-1,RecommendationPlan.Selection(request));
                }
            }
        });
        Check("Feedback preserves custom wishes and carries observed placement failure only for repair",()=>
        {
            string request=RecommendationFeedback.Request(false,1,"repair","Keep the north entrance.","Road failed");
            Equal(true,request.Contains("Road failed"));Equal(true,request.Contains("Keep the north entrance."));
            Equal(false,RecommendationFeedback.Request(false,1,"natural",null,"Road failed").Contains("Road failed"));
            Reject<ArgumentException>(()=>RecommendationFeedback.Request(false,0,"repair"));Reject<ArgumentOutOfRangeException>(()=>RecommendationFeedback.Request(false,4,"natural"));
            Reject<ArgumentException>(()=>RecommendationFeedback.Request(false,1,null,new string('x',2002)));
        });
        Check("Candidate feedback cannot apply a map replace the batch or revise another candidate",()=>
        {
            foreach(string response in new[]{"{\"action\":\"generate\",\"params\":{\"hills\":\"left\"}}","{\"action\":\"recommend\",\"options\":[]}","{\"action\":\"revise\",\"option\":2,\"params\":{}}"})
                Equal(true,RecommendationFeedback.ResponseProblem(1,response)!=null);
            Equal(null,RecommendationFeedback.ResponseProblem(1,"{\"action\":\"revise\",\"option\":1,\"params\":{}}"));
            Equal(null,RecommendationFeedback.ResponseProblem(1,"{\"action\":\"ask\",\"message\":\"Which outline?\"}"));
            Equal(null,RecommendationFeedback.ResponseProblem(0,"{\"action\":\"generate\",\"params\":{}}"));
        });
        Check("Explicit batch feedback never accepts direct map edits from contradictory free text",()=>
        {
            string request=RecommendationFeedback.Request(true,0,"different","비옥함 추천 말고 지형을 바꿔 줘");
            Equal(true,RecommendationPlan.RequestsDirectEdit(request));
            Equal(true,RecommendationFeedback.ResponseProblem(-1,"{\"action\":\"generate\",\"params\":{}}")!=null);
            Equal(true,RecommendationFeedback.ResponseProblem(-1,"{\"action\":\"revise\",\"option\":1,\"params\":{}}")!=null);
            Equal(null,RecommendationFeedback.ResponseProblem(-1,"{\"action\":\"recommend\",\"options\":[]}"));
        });
        Check("Placement failure is not hidden by successful texture rendering",()=>
        {
            string failure=RecommendationQuality.Rejection(new[]{"Road has no path"},0,0,false);
            Equal("Road has no path",RecommendationQuality.SelectionProblem(true,null,failure,false));
            Equal(null,RecommendationQuality.Rejection(new string[0],200,34,false));
            Equal(true,RecommendationQuality.Rejection(new string[0],200,0,false)!=null);
            Equal(null,RecommendationQuality.Rejection(null,0,0,false));
            Equal(null,RecommendationQuality.Rejection(null,12,0,false));
        });
        Check("Pending checks wait while unavailable optional preview retains text-only selection",()=>
        {
            Equal(true,RecommendationQuality.SelectionProblem(false,null,null,false)!=null);
            Equal(null,RecommendationQuality.SelectionProblem(true,"Map Preview unavailable",null,false));
            Equal(null,RecommendationQuality.SelectionProblem(true,null,null,false));
        });
        Check("Titles use real geometry before feature bonus prose and bound their length",()=>
        {
            var headline=RecommendationFeedback.Headline("• 비옥함 추가 — 더 비옥해집니다.\n• 북서쪽 가장자리의 산맥 추가",true);
            Equal(true,headline.Contains("북서쪽"));Equal(false,headline.Contains("비옥함"));
            Equal(true,RecommendationFeedback.Headline(new string('x',200),false).Length<=88);
            Equal("",RecommendationFeedback.Headline(null,true));
        });
        Check("AI follow-up questions require explicit choices before producing a request",()=>
        {
            var questions=PreferenceClarification.Parse("{\"questions\":[{\"question\":\"어떤 굽이가 좋나요?\",\"options\":[\"잔잔하게\",\"크게 휘도록\"]},{\"question\":\"Where?\",\"options\":[\"Near the edge\",\"Near the center\"]}]}");
            Equal(false,questions.Complete);Reject<InvalidOperationException>(()=>questions.Answers());
            questions.Questions[0].Selected=1;Equal(false,questions.Complete);
            questions.Questions[1].Selected=0;Equal(true,questions.Complete);
            Equal(true,questions.Answers().Contains("크게 휘도록"));Equal(true,questions.Answers().Contains("Near the edge"));
        });
        Check("Follow-up parser rejects executable commands wrong types duplicate or oversized choices",()=>
        {
            foreach(var json in new[]{
                "{\"action\":\"generate\",\"params\":{}}",
                "{\"questions\":[]}",
                "{\"questions\":[{\"question\":\"Q\",\"options\":[\"A\"]}]}",
                "{\"questions\":[{\"question\":\"Q\",\"options\":[\"A\",\"a\"]}]}",
                "{\"questions\":[{\"question\":42,\"options\":[\"A\",\"B\"]}]}",
                "{\"questions\":[{\"question\":\"Q\",\"options\":[true,false]}]}",
                "{\"questions\":[{\"question\":\"Q\",\"options\":[\"A\",\"B\"],\"params\":{}}]}",
                "{\"questions\":[{\"question\":\"<size=99>Q\",\"options\":[\"A\",\"B\"]}]}",
                "{\"questions\":[{\"question\":\"Q\",\"options\":[\"A\",\""+new string('b',161)+"\"]}]}"})Throws(()=>PreferenceClarification.Parse(json));
        });
    }
    static void Reject<T>(Action action) where T:Exception
    {
        try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);
    }
}
