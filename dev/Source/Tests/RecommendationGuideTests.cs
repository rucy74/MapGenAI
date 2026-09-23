using System;
using System.Linq;
using MapGenAI.LLM;
using static CoreRegressionTests;

static class RecommendationGuideTests
{
    static void Pick(RecommendationGuide guide,string value) { Equal(true,guide.Select(value)); Equal(true,guide.Next()); }
    public static void RunAll()
    {
        Check("Guide asks 5 or 6 local questions and never submits without review",()=>
        {
            var guide=new RecommendationGuide(true);
            Equal(5,guide.Questions.Count); Equal(false,guide.Next());
            Equal(false,guide.Submit(out _)); Equal(false,guide.Select("1"));
            Pick(guide,"space");Pick(guide,"open");Pick(guide,"existing");Pick(guide,"ordinary");Pick(guide,"layout");
            Equal(true,guide.Reviewing); Equal(false,guide.Finished);
            Equal(true,guide.Submit(out var request)); Equal(true,RecommendationPlan.IsRequest(request));
            Equal(false,guide.Submit(out _)); Equal(false,guide.Next());
        });
        Check("Unusual scenery choices respect earlier open-land and water preferences",()=>
        {
            foreach(var mountain in new[]{"open","edge","scattered","sheltered","any"})
            foreach(var water in new[]{"existing","small","lakeside","any"})
            foreach(var unusual in new[]{"ordinary","mixed","distinct","any"})
            {
                var guide=new RecommendationGuide(false);
                Pick(guide,"any");Pick(guide,mountain);Pick(guide,water);Pick(guide,unusual);
                bool branch=unusual=="mixed" || unusual=="distinct";
                Equal(branch?6:5,guide.Questions.Count);
                Equal(branch?"focus":"features",guide.Current.Id);
                if(branch)
                {
                    Equal(mountain!="open",guide.Current.Choices.Any(c=>c.Id=="mountain"));
                    Equal(water!="existing",guide.Current.Choices.Any(c=>c.Id=="water"));
                    Pick(guide,"subtle");
                }
                Pick(guide,"layout");
                Equal(true,guide.Reviewing);Equal(true,guide.Submit(out _));
            }
        });
        Check("Changed earlier answer discards incompatible later answers",()=>
        {
            var guide=new RecommendationGuide(false);
            Pick(guide,"scenery");Pick(guide,"sheltered");Pick(guide,"lakeside");Pick(guide,"distinct");Pick(guide,"mountain");Pick(guide,"include");
            Equal(true,guide.Summary().Contains("Spaces shaped by mountains"));
            guide.Back();guide.Back();guide.Back();
            Equal("distinctive",guide.Current.Id);
            Pick(guide,"ordinary"); Equal("features",guide.Current.Id); Equal(null,guide.Selected);
            Equal(false,guide.Summary().Contains("Spaces shaped by mountains"));
            Equal(false,guide.Summary().Contains("Suggest suitable features too"));
        });
        Check("Back without changing preserves answers and final feature choice preserves scenery focus",()=>
        {
            var guide=new RecommendationGuide(false);
            Pick(guide,"space");Pick(guide,"edge");Pick(guide,"small");Pick(guide,"mixed");Pick(guide,"water");Pick(guide,"include");
            var summary=guide.Summary();
            guide.Back();guide.Back();
            Equal("focus",guide.Current.Id);Equal("water",guide.Selected);
            Pick(guide,"water");Equal("include",guide.Selected);guide.Next();Equal(summary,guide.Summary());
            guide.Back();Pick(guide,"layout");Equal(true,guide.Summary().Contains("Distinctive watersides"));
        });
        Check("Early finish reviews only actual answers and Back resumes the same question",()=>
        {
            var guide=new RecommendationGuide(true);
            Pick(guide,"defense");guide.Review();Equal(true,guide.Reviewing);
            Equal(true,guide.Summary().Contains("방어하기 편한 배치"));
            Equal(false,guide.Summary().Contains("산은 어떻게"));
            guide.Back();Equal("mountains",guide.Current.Id);Equal(null,guide.Selected);
            guide.Review();Equal(true,guide.Submit(out _));
        });
        Check("Skip and no-preference remain unconstrained and can complete without guessing",()=>
        {
            foreach(bool ko in new[]{false,true})
            {
                var guide=new RecommendationGuide(ko);
                while(!guide.Reviewing) guide.Skip();
                Equal(5,guide.Questions.Count);Equal(true,guide.Submit(out var request));
                Equal(true,request.Contains(ko?"지정한 취향이 없습니다":"No preferences selected"));
                Equal(true,RecommendationPlan.IsRequest(request));
                Equal(false,RecommendationPlan.RequestsDirectEdit(request));
                Equal(-1,RecommendationPlan.Selection(request));
            }
        });
        Check("Cancel is terminal from questions and review, without returning a request",()=>
        {
            foreach(bool review in new[]{true,false})
            {
                var guide=new RecommendationGuide(false);
                Pick(guide,"space");if(review)guide.Review();guide.Cancel();
                Equal(false,guide.Submit(out var request));Equal(null,request);
                Equal(false,guide.Select("any"));Equal(false,guide.Next());Equal(false,guide.CanBack);
            }
        });
        Check("Guide changes neither localized recommendation selection nor existing direct edits",()=>
        {
            Equal(2,RecommendationPlan.Selection("2번"));Equal(3,RecommendationPlan.Selection("option 3"));
            Equal(false,RecommendationPlan.IsRequest("중앙에 호수 추가해 줘"));
            Equal(true,RecommendationPlan.IsRequest("그냥 추천해 줘"));
            Equal(true,RecommendationPlan.RequestsNewOptions("다시 추천해 줘"));
            Equal(true,RecommendationPlan.IsDismissal("선택 안 함"));
        });
        Check("Guide lifecycle handles repeated navigation and stale hidden choices",()=>
        {
            var random=new Random(3205);
            foreach(bool ko in new[]{true,false})
            for(int trial=0;trial<50;trial++)
            {
                var guide=new RecommendationGuide(ko);
                for(int i=0;i<80;i++)
                {
                    switch(random.Next(5))
                    {
                        case 0:guide.Back();break;
                        case 1:guide.Skip();break;
                        case 2:guide.Review();break;
                        default:
                            var choices=guide.Current.Choices;
                            if(guide.Select(choices[random.Next(choices.Count)].Id)) guide.Next();
                            break;
                    }
                    Equal(true,guide.Position>=0 && guide.Position<guide.Questions.Count);
                    if(guide.Selected!=null)Equal(true,guide.Current.Choices.Any(c=>c.Id==guide.Selected));
                    guide.Summary();
                }
                guide.Review();Equal(true,guide.Submit(out var request));Equal(true,RecommendationPlan.IsRequest(request));
            }
        });
    }
}
