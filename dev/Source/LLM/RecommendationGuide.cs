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

        private readonly bool korean;
        private readonly Dictionary<string,string> answers = new Dictionary<string,string>();
        private int position;
        private bool review, finished;
        public RecommendationGuide(bool korean) { this.korean=korean; }
        private string T(string ko,string en) => korean?ko:en;
        private Choice C(string id,string ko,string en,string detailKo,string detailEn) => new Choice(id,T(ko,en),T(detailKo,detailEn));
        private Choice Any() => C("any","상관없음","No preference","이 부분은 타일에 맞춰 맡길게요.","Let the selected tile guide this part.");
        private string Answer(string id) => answers.TryGetValue(id,out var value)?value:null;

        public IReadOnlyList<Question> Questions
        {
            get
            {
                var list = new List<Question>
                {
                    new Question("priority", T("가장 중요하게 생각하는 것은?","What matters most?"),
                        T("취향이 서로 충돌하면 이 답을 우선합니다.","If preferences conflict, this answer takes priority."),
                        C("space","넓은 건설 공간","Room to build","큰 기지와 농장, 나중의 확장 공간이 중요해요.","Space for a large base, farms and future expansion."),
                        C("defense","방어하기 편한 배치","Defensible layout","접근로를 살피기 쉽되, 기지 밖으로도 다닐 수 있으면 좋겠어요.","Manageable approaches, while keeping routes out of the base."),
                        C("scenery","보기 좋은 풍경","Scenery","생활 공간을 남기면서 자연스러운 풍경을 보고 싶어요.","Natural scenery with enough room to live."),
                        Any()),
                    new Question("mountains",T("산은 어떻게 놓였으면 좋겠나요?","How should mountains fit in?"),
                        T("지금 타일의 산과 기존에 만든 지형은 유지하면서 조정 가능한 범위로 추천합니다.","Suggestions work around this tile and terrain already in your map."),
                        C("open","탁 트인 생활 공간","Open living area","큰 산을 새로 늘리기보다 넓게 연결된 땅을 우선해요.","Favor connected open land over adding large mountains."),
                        C("edge","한쪽에 산, 반대쪽은 평지","Mountains to one side","산을 등지고, 바깥으로 트인 곳에 기지를 짓고 싶어요.","Build against mountains with open land in front."),
                        C("scattered","작은 산과 언덕이 흩어진 곳","Scattered hills","산 사이로 여러 방향을 오갈 수 있는 배치가 좋아요.","Small hills with routes between them in several directions."),
                        C("sheltered","산이 어느 정도 감싸는 곳","Partly sheltered by mountains","산 안쪽 생활 공간과 바깥으로 나가는 길이 필요해요.","A sheltered settlement area with usable routes out."),
                        Any()),
                    new Question("water",T("물을 더한다면 어느 정도가 좋나요?","Would you like more water?"),
                        T("기존 강·바다는 유지합니다. 없는 세계 강이나 해안을 새로 만들지는 않습니다.","Existing rivers and coast remain. This does not add a new world river or coast."),
                        C("existing","지금 있는 물만","Keep existing water only","호수나 연못을 추가하지 않고 땅을 넓게 쓰고 싶어요.","No additional lakes or ponds; keep the land usable."),
                        C("small","작은 연못 하나 정도","A small pond","생활 공간을 크게 줄이지 않는 작은 물가가 좋아요.","A modest waterside spot that leaves plenty of land."),
                        C("lakeside","호숫가에 자리 잡기","A lakeside settlement","물은 눈에 띄되, 한쪽에는 넓고 연결된 생활 공간을 남겨 주세요.","Noticeable water with a broad connected settlement area beside it."),
                        Any()),
                    new Question("distinctive",T("특별한 지형도 보고 싶나요?","How unusual should the landscape be?"),
                        T("특이한 모양을 골라도 건설 공간과 바깥으로 통하는 길을 남깁니다.","Distinctive terrain still needs buildable space and routes to the rest of the map."),
                        C("ordinary","익숙하고 자연스러운 풍경","Familiar, natural landscapes","특이한 모양보다는 현재 타일에 잘 어울리는 배치를 원해요.","A landscape that feels at home on this tile."),
                        C("mixed","평범한 안과 독특한 안을 함께","A mix of familiar and unusual","일반적인 배치와 눈에 띄는 지형을 비교해 보고 싶어요.","Compare an ordinary layout with something more distinctive."),
                        C("distinct","독특한 풍경 위주","Mostly distinctive landscapes","산에 둘러싸인 공간이나 굽은 물가처럼 기억에 남는 모습을 원해요.","Memorable shapes such as sheltered valleys or curved lakeshores."),
                        Any())
                };
                if (Answer("distinctive")=="mixed" || Answer("distinctive")=="distinct")
                {
                    var choices=new List<Choice>();
                    if(Answer("mountains")!="open")
                        choices.Add(C("mountain","산이 만드는 독특한 공간","Spaces shaped by mountains","골짜기, 산이 감싸는 생활 공간 등. 앞에서 고른 산 배치가 우선이에요.","Valleys or sheltered spaces, within your earlier mountain preference."));
                    if(Answer("water")!="existing")
                        choices.Add(C("water","물가가 만드는 독특한 풍경","Distinctive watersides","굽은 호숫가나 작은 만처럼, 앞에서 고른 물의 양에 맞춰요.","Curved lakeshores or small inlets, within your chosen water amount."));
                    choices.Add(C("subtle","탁 트인 땅에 작은 포인트","Small accents on open land","한쪽의 작은 능선이나 불규칙한 빈터처럼, 넓은 공간을 살려요.","A small ridge to one side or an irregular clearing, keeping open space."));
                    choices.Add(Any());
                    list.Add(new Question("focus",T("어떤 쪽의 특별함이 끌리나요?","What kind of distinctive scenery appeals?"),
                        T("앞에서 고른 취향과 맞는 방향만 보여줍니다.","These directions respect your previous answers."),choices.ToArray()));
                }
                list.Add(new Question("features",T("지형 특징도 함께 추천할까요?","Include special map features?"),
                    T("온천이나 유적 같은 특징입니다. 설치된 콘텐츠와 현재 타일에서 가능한 것만 검토합니다.","For example, hot springs or ruins. Only loaded content allowed on this tile will be considered."),
                    C("include","어울리는 특징도 제안해 줘","Suggest suitable features too","기존 특징과 충돌하지 않고, 고른 풍경에 어울리는 경우에만요.","Only when they fit the landscape and do not conflict with existing features."),
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
                if(position<=3) answers.Remove("focus");
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
            request=T("다음 취향으로 현재 타일에 어울리는 맵을 추천해 줘.","Recommend landscapes for the current tile using these preferences.")+"\n\n"+Summary()+"\n\n"+
                T("답하지 않은 항목은 현재 타일에 맞춰 정해 줘. 기존 맵 요소는 보존하고 가능한 추가·배치를 추천해 줘. 서로 맞지 않으면 첫 질문의 우선순위와 구체적으로 고른 산·물의 양을 먼저 지켜 줘. 특이한 지형 취향은 건설 공간이나 출입로를 포기하겠다는 뜻이 아니야. 세계지도 조건과 현재 특징의 제한을 지키고, 적용 전 후보 그림으로 비교하게 해 줘.",
                "Use the current tile for unanswered preferences. Preserve existing map elements and suggest compatible additions and layouts. Resolve conflicts using the first question's priority and the specific mountain/water preferences before the distinctive scenery preference. Unusual terrain does not authorize sacrificing buildable space or access. Respect world tile prerequisites and current feature constraints; show candidate previews before applying anything.");
            return true;
        }
        public void Cancel() { finished=true; }
    }
}
