using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    public sealed class Dialog_RecommendationFeedback : Window
    {
        readonly bool korean;
        readonly int candidate;
        readonly string failure, context;
        readonly Func<bool> valid;
        readonly Action<string> completed;
        readonly Action closed;
        readonly RequestGate requests=new RequestGate();
        string reason, draft="", error;
        bool waiting;
        Vector2 scroll;
        PreferenceClarification questions;
        public override Vector2 InitialSize=>new Vector2(Math.Min(700,Verse.UI.screenWidth-40),Math.Min(720,Verse.UI.screenHeight-40));
        string T(string ko,string en)=>korean?ko:en;
        public Dialog_RecommendationFeedback(bool korean,int candidate,string failure,string context,Func<bool> valid,Action<string> completed,Action closed)
        {
            this.korean=korean;this.candidate=candidate;this.failure=failure;this.context=context;this.valid=valid;this.completed=completed;this.closed=closed;
            doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;preventCameraMotion=true;layer=WindowLayer.Super;
        }
        public override void OnAcceptKeyPressed(){}
        public override void PostClose(){requests.Cancel();closed?.Invoke();base.PostClose();}
        void AskQuestions()
        {
            if(waiting || !valid())return;
            try
            {
                var settings=MapGenAIMod.Settings;var config=settings.GetActiveConfig();
                if(config==null || !config.IsValid())throw new InvalidOperationException(T("API 설정을 먼저 확인해 주세요.","Check your API settings first."));
                var client=LLMClientFactory.Create(config,settings.localBaseUrl);
                string input=context+"\nFeedback: "+RecommendationFeedback.Request(korean,candidate,reason,draft,failure);
                var history=new List<ChatMessage>{new ChatMessage("user",input)};
                string prompt=PreferenceClarification.Instructions+(korean?" Use Korean for all questions and options.":" Use English for all questions and options.");
                var ticket=requests.Begin();waiting=true;error=null;
                Task.Run(async()=>
                {
                    try
                    {
                        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ticket.Token))
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(90));
                            var response=await client.SendChatAsync(history,prompt,timeout.Token);
                            requests.Complete(ticket,response,null);
                        }
                    }
                    catch(Exception e){requests.Complete(ticket,null,e is OperationCanceledException?T("질문 요청 시간이 초과됐습니다.","Question request timed out."):e.Message);}
                });
            }
            catch(Exception e){error=e.Message;waiting=false;}
        }
        void Poll()
        {
            var reply=requests.Take();if(reply==null)return;waiting=false;
            try{if(reply.Error!=null)throw new Exception(reply.Error);questions=PreferenceClarification.Parse(reply.Text);scroll=Vector2.zero;}
            catch(Exception e){error=T("추가 질문을 받지 못했어요. 아래 피드백으로 진행할 수 있습니다. ","Could not load questions. You can continue with the feedback below. ")+e.Message;}
        }
        void Submit()
        {
            if(waiting || !valid() || questions!=null && !questions.Complete)return;
            string extra=draft+(questions==null?"":"\n"+questions.Answers());
            string request=RecommendationFeedback.Request(korean,candidate,reason,extra,failure);
            Close();completed(request);
        }
        public override void DoWindowContents(Rect rect)
        {
            if(!valid()){Close(false);return;}Poll();
            var font=Text.Font;var enabled=GUI.enabled;
            try
            {
                Text.Font=GameFont.Small;
                Widgets.Label(new Rect(0,0,rect.width-25,28),candidate>0?T(candidate+"번 후보 다듬기","Refine option "+candidate):T("어떤 점을 바꿔서 추천할까요?","What should the next suggestions change?"));
                string note=T("확인하기 전에는 후보와 맵이 바뀌지 않습니다. 아래 이유를 고르거나 직접 적어 주세요.","Choose a reason or write your own. Candidates and the map stay unchanged until you confirm.");
                Widgets.Label(new Rect(0,32,rect.width,52),note);
                var area=new Rect(0,88,rect.width,Math.Max(20,rect.height-178));
                float width=area.width-20,height=0;
                if(!string.IsNullOrEmpty(failure))height+=Text.CalcHeight(failure,width)+34;
                if(questions!=null)foreach(var q in questions.Questions)
                {height+=Text.CalcHeight(q.Text,width)+12;foreach(var option in q.Options)height+=Math.Max(38,Text.CalcHeight(option,width-20)+12)+6;}
                else height+=RecommendationFeedback.Choices(korean,candidate>0).Count*42+132;
                if(!string.IsNullOrEmpty(error))height+=Text.CalcHeight(error,width)+12;
                Widgets.BeginScrollView(area,ref scroll,new Rect(0,0,width,Math.Max(area.height,height)));
                float y=0;
                if(!string.IsNullOrEmpty(failure))
                {
                    Widgets.Label(new Rect(0,y,width,24),T("이 후보에서 확인된 문제","Observed problem in this option"));y+=28;
                    float h=Text.CalcHeight(failure,width);Widgets.Label(new Rect(0,y,width,h),failure);y+=h+6;
                }
                GUI.enabled=enabled && !waiting;
                if(questions!=null)
                {
                    foreach(var q in questions.Questions)
                    {
                        float h=Text.CalcHeight(q.Text,width);Widgets.Label(new Rect(0,y,width,h),q.Text);y+=h+12;
                        for(int i=0;i<q.Options.Count;i++)
                        {
                            h=Math.Max(38,Text.CalcHeight(q.Options[i],width-20)+12);
                            var option=new Rect(0,y,width,h);
                            if(Widgets.ButtonText(option,(q.Selected==i?"● ":"○ ")+q.Options[i]))q.Selected=i;
                            y+=h+6;
                        }
                    }
                }
                else
                {
                    foreach(var choice in RecommendationFeedback.Choices(korean,candidate>0))
                    {
                        if(Widgets.ButtonText(new Rect(0,y,width,36),(reason==choice.Id?"● ":"○ ")+choice.Label))reason=reason==choice.Id?null:choice.Id;
                        y+=42;
                    }
                    Widgets.Label(new Rect(0,y,width,24),T("추가로 원하는 점 (선택)","Anything else? (optional)"));y+=26;
                    draft=Widgets.TextArea(new Rect(0,y,width,92),draft);if(draft.Length>1000)draft=draft.Substring(0,1000);y+=100;
                }
                GUI.enabled=enabled;
                if(!string.IsNullOrEmpty(error))Widgets.Label(new Rect(0,y,width,Text.CalcHeight(error,width)),error);
                Widgets.EndScrollView();
                float footer=rect.height-80;
                GUI.enabled=enabled && !waiting;
                if(Widgets.ButtonText(new Rect(0,footer,rect.width,32),questions==null?
                    T("AI에게 추가 질문 받기 · API 사용","Ask AI for follow-up questions · uses API"):
                    T("추가 질문 건너뛰고 피드백으로 돌아가기","Skip questions and return to feedback")))
                {if(questions==null)AskQuestions();else{questions=null;scroll=Vector2.zero;}}
                GUI.enabled=enabled;
                if(Widgets.ButtonText(new Rect(0,footer+42,(rect.width-6)/2,34),T("취소","Cancel"))){Close();return;}
                GUI.enabled=enabled && !waiting && (questions!=null?questions.Complete:candidate==0 || reason!=null || !string.IsNullOrWhiteSpace(draft));
                if(Widgets.ButtonText(new Rect((rect.width+6)/2,footer+42,(rect.width-6)/2,34),waiting?T("질문을 받고 있어요…","Loading questions…"):
                    candidate>0?T("이 후보 수정 요청","Request this revision"):T("다시 추천받기","Get new suggestions")))Submit();
            }
            finally{Text.Font=font;GUI.enabled=enabled;}
        }
    }
}
