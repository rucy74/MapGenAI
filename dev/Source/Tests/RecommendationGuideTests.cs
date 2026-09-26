using System;
using System.Linq;
using MapGenAI.LLM;
using static CoreRegressionTests;

static class RecommendationGuideTests
{
    static void Pick(RecommendationGuide guide,string value) { Equal(true,guide.Select(value)); Equal(true,guide.Next()); }
    public static void RunAll()
    {
        Check("Guide includes spatial preferences and never submits without review",()=>
        {
            var guide=new RecommendationGuide(true);
            Equal(6,guide.Questions.Count); Equal(false,guide.Next());
            Equal(false,guide.Submit(out _)); Equal(false,guide.Select("1"));
            Pick(guide,"space");Pick(guide,"open");Pick(guide,"existing");Pick(guide,"together");Pick(guide,"ordinary");Pick(guide,"layout");
            Equal(true,guide.Reviewing); Equal(false,guide.Finished);
            Equal(true,guide.Submit(out var request)); Equal(true,RecommendationPlan.IsRequest(request));
            Equal(false,guide.Submit(out _)); Equal(false,guide.Next());
        });
        Check("Unusual scenery choices respect earlier open-land and water preferences",()=>
        {
            foreach(bool existingWater in new[]{false,true})
            foreach(var mountain in new[]{"open","edge","scattered","sheltered","any"})
            foreach(var water in new[]{"existing","small","lakeside","any"})
            foreach(var unusual in new[]{"ordinary","mixed","distinct","any"})
            {
                var guide=new RecommendationGuide(false,false,existingWater);
                Pick(guide,"any");Pick(guide,mountain);Pick(guide,water);Pick(guide,"any");Pick(guide,unusual);
                bool branch=(unusual=="mixed" || unusual=="distinct") && (mountain!="open" || water!="existing" || existingWater);
                Equal(branch?7:6,guide.Questions.Count);
                Equal(branch?"focus":"features",guide.Current.Id);
                if(branch)
                {
                    Equal(mountain!="open",guide.Current.Choices.Any(c=>c.Id=="mountain"));
                    Equal(water!="existing" || existingWater,guide.Current.Choices.Any(c=>c.Id=="water"));
                    Pick(guide,"subtle");
                }
                Pick(guide,"layout");
                Equal(true,guide.Reviewing);Equal(true,guide.Submit(out _));
            }
        });
        Check("Changed earlier answer discards incompatible later answers",()=>
        {
            var guide=new RecommendationGuide(false);
            Pick(guide,"scenery");Pick(guide,"sheltered");Pick(guide,"lakeside");Pick(guide,"linked");Pick(guide,"distinct");Pick(guide,"mountain");Pick(guide,"include");
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
            Pick(guide,"space");Pick(guide,"edge");Pick(guide,"small");Pick(guide,"flowing");Pick(guide,"mixed");Pick(guide,"water");Pick(guide,"include");
            var summary=guide.Summary();
            guide.Back();guide.Back();
            Equal("focus",guide.Current.Id);Equal("water",guide.Selected);
            Pick(guide,"water");Equal("include",guide.Selected);guide.Next();Equal(summary,guide.Summary());
            guide.Back();Pick(guide,"layout");Equal(true,guide.Summary().Contains("Scenery around a small pond"));
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
                Equal(6,guide.Questions.Count);Equal(true,guide.Submit(out var request));
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
            foreach(bool authored in new[]{true,false})
            foreach(bool existingWater in new[]{true,false})
            foreach(bool authoredWater in new[]{true,false})
            for(int trial=0;trial<50;trial++)
            {
                var guide=new RecommendationGuide(ko,authored,existingWater,authoredWater);
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
        Check("Living-space answers reach the submitted request without relaxing mountain or water preferences",()=>
        {
            foreach(bool ko in new[]{true,false})
            foreach(var space in new[]{"together","linked","flowing"})
            {
                var guide=new RecommendationGuide(ko,false,true);
                Pick(guide,"space");Pick(guide,"open");Pick(guide,"existing");
                var selected=guide.Current.Choices.Single(c=>c.Id==space);
                Pick(guide,space);guide.Review();Equal(true,guide.Submit(out var request));
                Equal(true,request.Contains(selected.Label));Equal(true,request.Contains(selected.Detail));
                Equal(true,request.Contains(ko?"탁 트인 생활 공간":"Open living area"));
                Equal(true,request.Contains(ko?"지금 있는 물만":"Keep existing water only"));
                Equal(false,guide.Select("any"));
            }
        });
        Check("Only an explicit replacement answer authorizes new authored layouts",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                var fresh=new RecommendationGuide(ko);
                Equal("priority",fresh.Current.Id);Equal(false,fresh.Select("replace"));
                foreach(var answer in new[]{"refine","replace","any"})
                {
                    var guide=new RecommendationGuide(ko,true);
                    Equal("scope",guide.Current.Id);Pick(guide,answer);guide.Review();
                    Equal(true,guide.Submit(out var request));
                    Equal(answer=="replace",request.Contains("replace_shapes:true"));
                    Equal(true,RecommendationPlan.IsRequest(request));
                    Equal(false,RecommendationPlan.RequestsDirectEdit(request));
                }
            }
        });
        Check("No new mountains or water suppresses incompatible space choices and redundant focus questions",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                var guide=new RecommendationGuide(ko);
                Pick(guide,"space");Pick(guide,"open");Pick(guide,"existing");
                Equal("space",guide.Current.Id);
                Equal(false,guide.Current.Choices.Any(c=>c.Id=="flowing"));Equal(false,guide.Select("flowing"));
                Pick(guide,"together");Pick(guide,"distinct");
                Equal("features",guide.Current.Id);Equal(false,guide.Questions.Any(q=>q.Id=="focus"));
                Pick(guide,"layout");Equal(true,guide.Submit(out var request));
                Equal(true,request.Contains(ko?"새 산을 더하지 않고":"without adding mountains"));
                Equal(true,request.Contains(ko?"호수나 연못을 추가하지 않고":"No additional lakes or ponds"));
                Equal(true,request.Contains(ko?"새 특징을 추가하지 않아요":"without adding new ones"));
            }
        });
        Check("Existing water permits waterside preferences without authorizing additional water",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                var guide=new RecommendationGuide(ko,false,true);
                Pick(guide,"scenery");Pick(guide,"open");Pick(guide,"existing");
                var flowing=guide.Current.Choices.Single(c=>c.Id=="flowing");
                Equal(true,flowing.Label.Contains(ko?"기존":"existing"));Pick(guide,"flowing");Pick(guide,"distinct");
                Equal("focus",guide.Current.Id);Equal(false,guide.Current.Choices.Any(c=>c.Id=="mountain"));
                var water=guide.Current.Choices.Single(c=>c.Id=="water");
                Equal(true,water.Label.Contains(ko?"기존":"existing"));Pick(guide,"water");Pick(guide,"include");
                Equal(true,guide.Submit(out var request));
                Equal(true,request.Contains(water.Detail));Equal(true,request.Contains(flowing.Detail));
                Equal(true,request.Contains(ko?"새 연못이나 호수는 만들지":"without new ponds or lakes"));
            }
        });
        Check("Small-pond choices remain small and earlier water changes remove stale follow-ups",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                var guide=new RecommendationGuide(ko);
                Pick(guide,"scenery");Pick(guide,"open");Pick(guide,"small");Pick(guide,"flowing");Pick(guide,"mixed");
                var water=guide.Current.Choices.Single(c=>c.Id=="water");
                Equal(true,water.Label.Contains(ko?"작은 연못":"small pond"));Pick(guide,"water");Pick(guide,"layout");
                Equal(true,guide.Summary().Contains(water.Detail));
                guide.Back();guide.Back();guide.Back();guide.Back();guide.Back();
                Equal("water",guide.Current.Id);Pick(guide,"existing");
                Equal("space",guide.Current.Id);Equal(null,guide.Selected);Equal(false,guide.Current.Choices.Any(c=>c.Id=="flowing"));
                Equal(false,guide.Summary().Contains(water.Label));
                Pick(guide,"together");Pick(guide,"ordinary");Equal("features",guide.Current.Id);Equal(null,guide.Selected);
                Pick(guide,"layout");Equal(true,guide.Submit(out var request));Equal(false,request.Contains(water.Label));
            }
        });
        Check("Changing layout scope clears later preferences rather than retaining stale authorization",()=>
        {
            var guide=new RecommendationGuide(false,true);
            Pick(guide,"replace");var chosenDetail=guide.Current.Choices.Single(c=>c.Id=="scenery").Detail;
            Pick(guide,"scenery");guide.Review();guide.Back();guide.Back();guide.Back();
            Equal("scope",guide.Current.Id);Pick(guide,"refine");Equal(null,guide.Selected);
            guide.Review();Equal(true,guide.Submit(out var request));
            Equal(false,request.Contains("replace_shapes:true"));
            Equal(false,guide.Summary().Contains("Scenery"));Equal(false,request.Contains(chosenDetail));
        });
        Check("Replacement water choices use tile water rather than authored water being replaced",()=>
        {
            foreach(bool ko in new[]{true,false})
            foreach(bool tileWater in new[]{true,false})
            foreach(bool authoredWater in new[]{true,false})
            foreach(var scope in new[]{"refine","replace","any"})
            {
                var guide=new RecommendationGuide(ko,true,tileWater,authoredWater);
                Pick(guide,scope);Pick(guide,"scenery");
                var open=guide.Current.Choices.Single(c=>c.Id=="open");
                Equal(true,open.Detail.Contains(ko?"새 산을 더하지 않고":"without adding mountains"));
                if(scope=="replace")
                {
                    Equal(true,open.Detail.Contains(ko?"타일의 원래 산은 살리고":"Keep the tile's original mountains"));
                    Equal(true,open.Detail.Contains(ko?"직접 그린 산":"manually drawn mountains"));
                }
                Pick(guide,"open");
                var existing=guide.Current.Choices.Single(c=>c.Id=="existing");
                if(scope=="replace")
                {
                    Equal(true,existing.Detail.Contains(ko?"그려 넣은":"manually drawn"));
                    Equal(true,existing.Detail.Contains(ko?"새 물은 추가하지":"add no new water"));
                }
                Pick(guide,"existing");
                bool retainedWater=tileWater || (authoredWater && scope!="replace");
                Equal(retainedWater,guide.Current.Choices.Any(c=>c.Id=="flowing"));
                Pick(guide,retainedWater?"flowing":"together");Pick(guide,"distinct");
                Equal(retainedWater,guide.Current.Id=="focus");
                if(retainedWater) Pick(guide,"water");
                Pick(guide,"layout");Equal(true,guide.Submit(out var request));
                Equal(true,request.Contains(existing.Detail));
                Equal(true,request.Contains(open.Detail));
                Equal(true,request.Contains(ko?"어떤 우선순위도 명시한 금지":"any other priority must not override explicit restrictions"));
                Equal(true,request.Contains(ko?"기존 특징 보존 조건을 무효화하지 않아":"requirements to preserve existing features"));
                Equal(scope=="replace",request.Contains("replace_shapes:true"));
                Equal(true,RecommendationPlan.IsRequest(request));
            }
        });
        Check("Changing refine to replace drops authored-water follow-ups and Back can restore them",()=>
        {
            foreach(bool ko in new[]{true,false})
            {
                var guide=new RecommendationGuide(ko,true,false,true);
                Pick(guide,"refine");Pick(guide,"scenery");Pick(guide,"open");Pick(guide,"existing");
                var flowing=guide.Current.Choices.Single(c=>c.Id=="flowing");
                Pick(guide,"flowing");Pick(guide,"distinct");Pick(guide,"water");Pick(guide,"layout");
                Equal(true,guide.Reviewing);guide.Back();
                while(guide.Position>0)guide.Back();
                Pick(guide,"replace");
                Equal(null,guide.Selected);Equal(false,guide.Summary().Contains(flowing.Detail));
                Pick(guide,"scenery");Pick(guide,"open");Pick(guide,"existing");
                Equal(false,guide.Select("flowing"));
                Pick(guide,"together");Pick(guide,"distinct");Equal("features",guide.Current.Id);Equal(null,guide.Selected);
                while(guide.Position>0)guide.Back();
                Pick(guide,"refine");Pick(guide,"scenery");Pick(guide,"open");Pick(guide,"existing");
                Equal(true,guide.Select("flowing"));Equal(true,guide.Next());
                guide.Review();Equal(true,guide.Submit(out var request));
                Equal(false,request.Contains("replace_shapes:true"));Equal(true,request.Contains(flowing.Detail));
            }
        });
        Check("New recommendation briefs vary independent composition axes and remain reproducible",()=>
        {
            var compositions=new System.Collections.Generic.HashSet<string>();
            var positions=new System.Collections.Generic.HashSet<string>();
            var concreteCompositions=new System.Collections.Generic.HashSet<string>();
            foreach(int seed in Enumerable.Range(-20,60).Concat(new[]{int.MinValue,int.MaxValue}))
            {
                var directions=RecommendationVariation.Directions(seed);
                Equal(3,directions.Count);
                Equal(3,directions.Select(d=>d.Mass).Distinct().Count());
                Equal(3,directions.Select(d=>d.Space).Distinct().Count());
                Equal(3,directions.Select(d=>d.Relation).Distinct().Count());
                Equal(3,directions.Select(d=>d.Sector).Distinct().Count());
                Equal(3,directions.Select(d=>d.OccupiedPercent).Distinct().Count());
                Equal(3,directions.Select(d=>d.OpenPercent).Distinct().Count());
                Equal(3,directions.Select(d=>d.Variant).Distinct().Count());
                string prompt=RecommendationVariation.Build(seed);
                Equal(prompt,RecommendationVariation.Build(seed));
                foreach(var direction in directions)
                {
                    Equal(true,direction.Variant>=0 && direction.Variant<=999999);
                    Equal(true,prompt.Contains(direction.Mass) && prompt.Contains(direction.Space) && prompt.Contains(direction.Relation));
                    Equal(true,prompt.Contains("variant hint: "+direction.Variant+"."));
                    Equal(true,direction.CenterXPercent>=17 && direction.CenterXPercent<=83);
                    Equal(true,direction.CenterZPercent>=17 && direction.CenterZPercent<=83);
                    Equal(true,direction.OccupiedPercent>=10 && direction.OccupiedPercent<=23);
                    Equal(true,direction.OpenPercent>=45 && direction.OpenPercent<=65);
                    string coordinates="["+(direction.CenterXPercent/100.0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+","+(direction.CenterZPercent/100.0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"]";
                    Equal(true,prompt.Contains("in the "+direction.Sector+" around "+coordinates));
                    Equal(true,prompt.Contains("occupy about "+direction.OccupiedPercent+"%"));
                    Equal(true,prompt.Contains("at least "+direction.OpenPercent+"%"));
                    compositions.Add(direction.Mass+"|"+direction.Space+"|"+direction.Relation);
                    positions.Add(direction.Sector);
                    concreteCompositions.Add(direction.Sector+"|"+direction.Mass+"|"+direction.OccupiedPercent);
                }
            }
            // A fixed menu or one coupled axis cannot pass this across these fixed seeds.
            Equal(true,compositions.Count>40);
            Equal(8,positions.Count);
            Equal(true,concreteCompositions.Count>65);
        });
    }
}
