using System;
using System.Collections.Generic;
using System.Linq;

namespace MapGenAI.LLM
{
    // Local preferences only. This object cannot call a provider or mutate map state.
    public sealed class RecommendationGuide
    {
        public sealed class Choice
        {
            public readonly string Id, Label, Detail;
            public Choice(string id, string label, string detail) { Id=id; Label=label; Detail=detail; }
        }
        public sealed class Question
        {
            public readonly string Id, Title, Hint;
            public readonly IReadOnlyList<Choice> Choices;
            public Question(string id, string title, string hint, params Choice[] choices)
            { Id=id; Title=title; Hint=hint; Choices=choices; }
        }

        private readonly bool korean, hasAuthoredTerrain, hasTileWater, hasAuthoredWater;
        private readonly Dictionary<string,string> answers = new Dictionary<string,string>();
        private int position;
        private bool review, finished;
        public RecommendationGuide(bool korean,bool hasAuthoredTerrain=false,bool hasExistingWater=false,bool hasAuthoredWater=false)
        { this.korean=korean;this.hasAuthoredTerrain=hasAuthoredTerrain;hasTileWater=hasExistingWater;this.hasAuthoredWater=hasAuthoredWater; }
        private string T(string ko,string en) => korean?ko:en;
        private Choice C(string id,string ko,string en,string detailKo,string detailEn) => new Choice(id,T(ko,en),T(detailKo,detailEn));
        private Choice Any() => C("any","상관없음","No preference","이 부분은 타일에 맞춰 맡길게요.","Let the selected tile guide this part.");
        private string Answer(string id) => answers.TryGetValue(id,out var value)?value:null;

        public IReadOnlyList<Question> Questions
        {
            get
            {
                bool addMountains=Answer("mountains")!="open",addWater=Answer("water")!="existing";
                bool replacing=Answer("scope")=="replace";
                // Tile water survives replacement; directly drawn water only survives refinement.
                bool retainedWater=hasTileWater || (!replacing && hasAuthoredWater);
                bool useWater=addWater || retainedWater;
                var spaces=new List<Choice>{
                    C("together","넓게 이어진 한 공간","One broad connected space","기지와 농장이 한 덩어리로 자라고, 풍경은 주로 가장자리에 있으면 좋겠어요.","Room for one growing base and farms, with landscape features mainly along its edges."),
                    C("linked","이어지는 여러 빈터","Several connected clearings","크기가 다른 생활 공간을 넓은 땅으로 연결하고, 사이사이에 풍경이 있으면 좋겠어요.","Unequal clearings linked by broad usable ground, with scenery between them.")};
                if(addMountains || useWater)
                    spaces.Add(!addWater && retainedWater?
                        replacing?
                        C("flowing","타일의 물가를 따라 이어진 공간","Space along the tile's water","타일의 강·해안·호수 특징을 따라 생활 공간을 이어 주세요. 그려 넣은 물은 이어 쓰지 않고 새 물도 추가하지 않아요.","Connect living space along the tile's rivers, coast and lake features. Do not carry over manually drawn water or add new water."):
                        C("flowing","기존 물가를 따라 이어진 공간","Space along existing water","새 물을 추가하지 않고, 지금 있는 물가를 따라 넓은 생활 공간을 이어 주세요.","Connect broad living space along the current water, without adding any water."):
                        Answer("water")=="small" && !addMountains?
                        C("flowing","작은 물가 주변으로 이어진 공간","Space around a small pond","작은 연못 주변의 생활 공간을 부드럽게 연결해 주세요. 물을 늘리거나 새 산을 만들지는 않아요.","Gently connect living space around a small pond, without more water or new mountains."):
                        C("flowing","지형을 따라 굽어 이어진 공간","Space following the landscape","앞에서 고른 지형을 따라 넓은 생활 공간이 굽어 이어지되, 길고 좁은 틈은 피하고 싶어요.","Broad living space bending along the chosen terrain, rather than narrow slits."));
                spaces.Add(Any());
                var list = new List<Question>
                {
                    new Question("priority", T("가장 중요하게 생각하는 것은?","What matters most?"),
                        T("취향이 서로 충돌하면 이 답을 우선합니다.","If preferences conflict, this answer takes priority."),
                        C("space","넓은 건설 공간","Room to build","큰 기지와 농장, 나중의 확장 공간이 중요해요.","Space for a large base, farms and future expansion."),
                        C("defense","방어하기 편한 배치","Defensible layout","접근로를 살피기 쉽되, 기지 밖으로도 다닐 수 있으면 좋겠어요.","Manageable approaches, while keeping routes out of the base."),
                        C("scenery","보기 좋은 풍경","Scenery","생활 공간을 남기면서 자연스러운 풍경을 보고 싶어요.","Natural scenery with enough room to live."),
                        Any()),
                    new Question("mountains",T("산은 어떻게 놓였으면 좋겠나요?","How should mountains fit in?"),
                        Answer("scope")=="replace"?
                            T("선택한 타일의 산악 정도에 맞는 새 구도를 비교합니다.","Compare new layouts suited to the selected tile's hilliness."):
                            T("지금 타일의 산과 기존에 만든 지형은 유지하면서 조정 가능한 범위로 추천합니다.","Suggestions work around this tile and terrain already in your map."),
                        replacing?
                        C("open","탁 트인 생활 공간","Open living area","새 산을 더하지 않고 넓게 연결된 땅을 우선해요. 타일의 원래 산은 살리고, 직접 그린 산은 새 구도에 맞춰 교체해요.","Favor connected open land without adding mountains. Keep the tile's original mountains; replace manually drawn mountains as part of the new layout."):
                        C("open","탁 트인 생활 공간","Open living area","새 산을 더하지 않고 넓게 연결된 땅을 우선해요. 기존 산은 임의로 지우지 않아요.","Favor connected open land without adding mountains. Keep existing mountains unless removal is requested."),
                        C("edge","한쪽에 산, 반대쪽은 평지","Mountains to one side","산을 등지고, 바깥으로 트인 곳에 기지를 짓고 싶어요.","Build against mountains with open land in front."),
                        C("scattered","작은 산과 언덕이 흩어진 곳","Scattered hills","산 사이로 여러 방향을 오갈 수 있는 배치가 좋아요.","Small hills with routes between them in several directions."),
                        C("sheltered","산이 어느 정도 감싸는 곳","Partly sheltered by mountains","산 안쪽 생활 공간과 바깥으로 나가는 길이 필요해요.","A sheltered settlement area with usable routes out."),
                        Any()),
                    new Question("water",T("물을 더한다면 어느 정도가 좋나요?","Would you like more water?"),
                        replacing?
                        T("그려 넣은 지형은 새 구도로 바뀌지만, 타일의 강·해안·호수 특징은 남습니다. 없는 세계 강이나 해안을 새로 만들지는 않습니다.","Drawn terrain is being replaced, while the tile's rivers, coast and lake features remain. This does not add a new world river or coast."):
                        T("기존 강·바다는 유지합니다. 없는 세계 강이나 해안을 새로 만들지는 않습니다.","Existing rivers and coast remain. This does not add a new world river or coast."),
                        replacing?
                        C("existing","타일의 물만 유지","Keep only the tile's water","타일의 강·해안·호수 특징만 유지해요. 그려 넣은 호수·연못은 이어 쓰지 않고 새 물은 추가하지 않아요.","Keep the tile's rivers, coast and lake features. Do not carry over manually drawn lakes or ponds, and add no new water."):
                        C("existing","지금 있는 물만","Keep existing water only","호수나 연못을 추가하지 않고 땅을 넓게 쓰고 싶어요.","No additional lakes or ponds; keep the land usable."),
                        C("small","작은 연못 하나 정도","A small pond","생활 공간을 크게 줄이지 않는 작은 물가가 좋아요.","A modest waterside spot that leaves plenty of land."),
                        C("lakeside","호숫가에 자리 잡기","A lakeside settlement","물은 눈에 띄되, 한쪽에는 넓고 연결된 생활 공간을 남겨 주세요.","Noticeable water with a broad connected settlement area beside it."),
                        Any()),
                    new Question("space",T("생활 공간은 어떻게 이어지면 좋나요?","How should living space connect?"),
                        T("특정 지형 이름을 몰라도 됩니다. 앞에서 고른 조건 안에서 생활 공간의 연결 방식을 정합니다.","No landform names needed. These connections stay within your earlier choices."),spaces.ToArray()),
                    new Question("distinctive",T("풍경의 모양은 어느 정도 개성 있으면 좋나요?","How distinctive should the layout look?"),
                        T("산·물의 양은 앞선 답대로입니다. 여기서는 공간의 모양과 배치만 고릅니다.","Mountain and water amounts still follow your earlier answers. This question concerns shape and arrangement only."),
                        C("ordinary","익숙하고 자연스러운 풍경","Familiar, natural landscapes","특이한 모양보다는 현재 타일에 잘 어울리는 배치를 원해요.","A landscape that feels at home on this tile."),
                        C("mixed","평범한 안과 독특한 안을 함께","A mix of familiar and unusual","일반적인 배치와 눈에 띄는 지형을 비교해 보고 싶어요.","Compare an ordinary layout with something more distinctive."),
                        C("distinct","독특한 풍경 위주","Mostly distinctive landscapes","고른 산·물의 양을 유지하면서, 공간의 연결과 윤곽이 개성 있으면 좋겠어요.","Distinctive connections and outlines within the chosen mountain and water amounts."),
                        Any())
                };
                if(hasAuthoredTerrain)
                    list.Insert(0,new Question("scope",T("지금 만든 지형을 어떻게 할까요?","What should happen to your current terrain?"),
                        T("어느 쪽이든 먼저 후보 그림만 보여줍니다. 선택하기 전에는 현재 맵이 바뀌지 않아요.","Both choices show previews first. Nothing changes until you select a candidate."),
                        C("refine","지금 구도를 다듬기","Refine this layout","이미 만든 산·물·평지는 살리고, 어울리는 작은 변화를 비교할게요.","Keep authored mountains, water and open ground; compare compatible local changes."),
                        C("replace","다른 구도를 새로 비교하기","Compare new layouts","직접 만든 지형은 새 후보로 바꿔 볼게요. 세계지도의 강·해안·특징은 유지해 주세요.","Replace authored terrain in the candidates, while retaining world rivers, coast and map features."),
                        Any()));
                if (Answer("distinctive")=="mixed" || Answer("distinctive")=="distinct")
                {
                    var choices=new List<Choice>();
                    if(addMountains)
                        choices.Add(C("mountain","산이 만드는 독특한 공간","Spaces shaped by mountains","골짜기, 산이 감싸는 생활 공간 등. 앞에서 고른 산 배치가 우선이에요.","Valleys or sheltered spaces, within your earlier mountain preference."));
                    if(useWater)
                        choices.Add(!addWater?
                            replacing?
                            C("water","타일의 물가와 어울리는 배치","Layout around the tile's water","타일의 강·해안·호수 특징과 생활 공간을 어울리게 해 주세요. 그려 넣은 물은 이어 쓰지 않고 새 물도 추가하지 않아요.","Fit living space around the tile's rivers, coast and lake features. Do not carry over manually drawn water or add new water."):
                            C("water","기존 물가와 어울리는 배치","Layout around existing water","지금 있는 물가와 생활 공간의 관계를 살리고, 새 연못이나 호수는 만들지 않아요.","Use the relationship between current water and living space, without new ponds or lakes."):
                            Answer("water")=="small"?
                            C("water","작은 연못 주변의 풍경","Scenery around a small pond","작은 연못의 윤곽과 주변 여백을 다듬고, 큰 호수나 만으로 키우지는 않아요.","Shape a small pond and its surrounding space, without enlarging it into a lake or inlet."):
                            C("water","물가가 만드는 독특한 풍경","Distinctive watersides","앞에서 고른 물의 양 안에서, 물가와 넓은 생활 공간이 어울리는 배치를 원해요.","Distinctive waterside layouts with broad living space, within the chosen water amount."));
                    choices.Add(C("subtle","탁 트인 땅에 작은 포인트","Small accents on open land","빈터의 윤곽과 연결로 넓은 공간을 살려요. 이를 위해 산이나 물을 더하지는 않아요.","Use clearing outlines and connections while keeping open space, without adding mountains or water for this accent."));
                    // A sole compatible focus is already implied by the previous answers.
                    if(choices.Count>1)
                    {
                        choices.Add(Any());
                        list.Add(new Question("focus",T("어떤 쪽의 특별함이 끌리나요?","What kind of distinctive scenery appeals?"),
                            T("앞에서 고른 취향과 맞는 방향만 보여줍니다.","These directions respect your previous answers."),choices.ToArray()));
                    }
                }
                list.Add(new Question("features",T("지형 특징도 함께 추천할까요?","Include special map features?"),
                    !addWater?
                        T("풍경 모양과 별개인 유적 등의 게임 특징입니다. 새 물은 추가하지 않고, 설치된 콘텐츠 중 현재 조건에 맞는 것만 검토합니다.","Game features such as ruins, separate from the landscape's shape. Keep the no-new-water preference and consider only loaded, compatible content."):
                        T("풍경 모양과 별개인 온천·유적 등의 게임 특징입니다. 앞서 고른 산·물의 양과 설치된 콘텐츠, 현재 타일 조건을 지킵니다.","Game features such as hot springs or ruins, separate from the landscape's shape. Respect the chosen mountain/water amounts, loaded content and tile conditions."),
                    C("include","어울리는 특징도 제안해 줘","Suggest suitable features too","앞서 고른 조건을 바꾸거나 기존 특징과 충돌하지 않는 경우에만요.","Only when they respect earlier preferences and do not conflict with existing features."),
                    C("layout","모양과 배치에 집중해 줘","Focus on terrain layout","기존 특징은 유지하고 새 특징을 추가하지 않아요.","Keep current features without adding new ones."),
                    Any()));
                return list;
            }
        }

        public int Position => position;
        public bool Reviewing => review;
        public bool Finished => finished;
        public Question Current => Questions[position];
        public string Selected => Answer(Current.Id);
        public bool CanBack => !finished && (review || position>0);
        public bool Select(string choice)
        {
            if(finished || review || !Current.Choices.Any(c=>c.Id==choice)) return false;
            if(Selected!=choice)
            {
                // A changed earlier answer invalidates subsequent answers, including hidden branches.
                foreach(var question in Questions.Skip(position+1)) answers.Remove(question.Id);
                if(Current.Id!="focus" && Current.Id!="features") answers.Remove("focus");
                answers[Current.Id]=choice;
            }
            return true;
        }
        public bool Next()
        {
            if(finished || review || Selected==null) return false;
            if(position+1<Questions.Count) position++; else review=true;
            return true;
        }
        public void Skip() { if(Select("any")) Next(); }
        public void Back()
        {
            if(!CanBack) return;
            if(review) review=false; else position--;
        }
        public void Review() { if(!finished) review=true; }
        public string Summary()
        {
            var lines=new List<string>();
            foreach(var question in Questions)
            {
                var choice=question.Choices.FirstOrDefault(c=>c.Id==Answer(question.Id));
                if(choice!=null && choice.Id!="any") lines.Add(question.Title+"\n  "+choice.Label+" — "+choice.Detail);
            }
            return lines.Count==0?T("지정한 취향이 없습니다. 현재 타일에 맞춰 추천합니다.","No preferences selected. Suggestions will follow the current tile."):string.Join("\n\n",lines);
        }
        public bool Submit(out string request)
        {
            request=null;
            if(finished || !review) return false;
            finished=true;
            string scope=Answer("scope")=="replace"?
                T("이번에는 기존에 직접 만든 지형을 바꾸는 새로운 구도 후보를 원해. replace_shapes:true로 새 지형 전체를 제안해도 돼. 실제 맵은 후보를 선택할 때만 바꿔 줘. 세계지도의 강·해안·특징, 기존 도로·구조물과 관련 없는 설정은 유지해 줘. 기존 영역을 참조하는 채움이나 구조물이 있다면 관계를 유효하게 유지하고 조용히 삭제하지 마.",
                  "I explicitly want replacement layouts for the authored terrain. You may propose a complete new terrain set with replace_shapes:true, applied only when I select a candidate. Retain world rivers, coast, features, existing roads/structures and unrelated settings. Keep dependent fills/structure references valid; do not silently delete them."):
                T("기존 맵 요소는 보존하고 가능한 추가·배치를 추천해 줘. 지금 구도를 다른 구도로 교체하지 마.",
                  "Preserve existing map elements and suggest compatible additions and layouts. Do not replace the current composition.");
            request=T("다음 취향으로 현재 타일에 어울리는 맵을 추천해 줘.","Recommend landscapes for the current tile using these preferences.")+"\n\n"+Summary()+"\n\n"+
                scope+"\n"+T("답하지 않은 항목은 현재 타일에 맞춰 정해 줘. 서로 맞지 않으면 중요하게 고른 우선순위와 구체적으로 고른 산·물의 양, 생활 공간의 연결을 먼저 지켜 줘. 풍경이나 방어 등 어떤 우선순위도 명시한 금지(새 산·물 추가 금지 등)나 기존 특징 보존 조건을 무효화하지 않아. 특이한 지형 취향은 건설 공간이나 출입로를 포기하겠다는 뜻이 아니야. 질문의 예시는 정해진 맵 목록이 아니야. 같은 취향 안에서 공간의 위치, 지형과 물의 관계가 서로 다른 구도를 비교하게 해 줘. 세계지도 조건과 현재 특징의 제한을 지키고, 적용 전 후보 그림으로 비교하게 해 줘.",
                "Use the current tile for unanswered preferences. Resolve conflicts using the chosen priority and specific mountain/water amounts and living-space connections before the distinctive scenery preference. Scenery, defense or any other priority must not override explicit restrictions, such as no new mountains or water, or requirements to preserve existing features. Unusual terrain does not authorize sacrificing buildable space or access. Examples in the questions are not a fixed map menu. Compare distinct spatial arrangements and land/water relationships within these preferences. Respect world tile prerequisites and current feature constraints; show candidate previews before applying anything.");
            return true;
        }
        public void Cancel() { finished=true; }
    }
}
