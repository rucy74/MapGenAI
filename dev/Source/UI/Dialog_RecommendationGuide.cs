using System;
using MapGenAI.LLM;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    // A separate local dialog avoids consuming chat space or interpreting quiz numbers as candidate picks.
    public sealed class Dialog_RecommendationGuide : Window
    {
        private readonly RecommendationGuide guide;
        private readonly Action<string> completed;
        private readonly Action closed;
        private readonly bool korean;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(680f,Verse.UI.screenWidth-40f),Mathf.Min(700f,Verse.UI.screenHeight-40f));
        private string T(string ko,string en) => korean?ko:en;
        public Dialog_RecommendationGuide(bool korean,Action<string> completed,Action closed,bool hasAuthoredTerrain=false,bool hasExistingWater=false,bool hasAuthoredWater=false)
        {
            this.korean=korean; this.completed=completed; this.closed=closed;
            guide=new RecommendationGuide(korean,hasAuthoredTerrain,hasExistingWater,hasAuthoredWater);
            doCloseX=true; closeOnAccept=false; absorbInputAroundWindow=true; forcePause=false;
            preventCameraMotion=true; layer=WindowLayer.Super;
        }
        public override void OnAcceptKeyPressed() { }
        public override void PostClose()
        {
            guide.Cancel();
            closed?.Invoke();
            base.PostClose();
        }
        public override void DoWindowContents(Rect rect)
        {
            var font=Text.Font;
            var anchor=Text.Anchor;
            var enabled=GUI.enabled;
            try
            {
                Text.Font=GameFont.Small; Text.Anchor=TextAnchor.UpperLeft;
                Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,30f),new Color(0.15f,0.35f,0.55f));
                Widgets.Label(new Rect(rect.x+8f,rect.y+3f,rect.width-16f,24f),T("취향에 맞춰 추천받기","Suggestions for your preferences"));
                string note=T("준비된 질문 중 앞선 답에 맞는 항목을 보여줍니다. 문답 중 AI 호출 없이, 마지막에 취향을 확인하고 추천을 요청합니다.",
                    "Prepared questions adapt to earlier answers. No AI calls during the questions; review your preferences before requesting suggestions.");
                float noteHeight=Text.CalcHeight(note,rect.width);
                Widgets.Label(new Rect(rect.x,rect.y+38f,rect.width,noteHeight),note);
                float top=rect.y+46f+noteHeight;
                var content=new Rect(rect.x,top,rect.width,Mathf.Max(20f,rect.yMax-top-92f));
                float width=content.width-20f;
                string heading=guide.Reviewing?T("이 취향으로 추천받을까요?","Review your preferences"):
                    (guide.Position+1)+" / "+guide.Questions.Count+"  "+guide.Current.Title;
                string hint=guide.Reviewing?T("답변을 바꾸려면 ‘이전’. 아직 맵에는 적용하지 않았습니다.","Use Back to change answers. Nothing has been applied to your map."):guide.Current.Hint;
                float headingHeight=Text.CalcHeight(heading,width);
                float hintHeight=Text.CalcHeight(hint,width);
                float height=headingHeight+hintHeight+36f;
                if(guide.Reviewing) height+=Text.CalcHeight(guide.Summary(),width)+12f;
                else foreach(var option in guide.Current.Choices)
                    height+=Mathf.Max(54f,Text.CalcHeight(option.Label+"\n"+option.Detail,width-32f)+18f)+8f;
                var view=new Rect(0,0,width,Mathf.Max(content.height,height));
                Widgets.BeginScrollView(content,ref scroll,view);
                float y=0;
                Widgets.Label(new Rect(0,y,width,headingHeight),heading); y+=headingHeight+8f;
                Widgets.Label(new Rect(0,y,width,hintHeight),hint); y+=hintHeight+20f;
                if(guide.Reviewing) Widgets.Label(new Rect(0,y,width,Text.CalcHeight(guide.Summary(),width)),guide.Summary());
                else foreach(var option in guide.Current.Choices)
                {
                    float h=Mathf.Max(54f,Text.CalcHeight(option.Label+"\n"+option.Detail,width-32f)+18f);
                    var card=new Rect(0,y,width,h);
                    bool selected=guide.Selected==option.Id;
                    Widgets.DrawBoxSolid(card,selected?new Color(0.18f,0.38f,0.55f):new Color(0.16f,0.18f,0.20f));
                    if(selected) Widgets.DrawBox(card);
                    Widgets.Label(new Rect(card.x+12f,card.y+8f,card.width-24f,card.height-16f),option.Label+"\n"+option.Detail);
                    if(Widgets.ButtonInvisible(card)) guide.Select(option.Id);
                    y+=h+8f;
                }
                Widgets.EndScrollView();

                float footer=rect.yMax-82f;
                float buttonWidth=(rect.width-12f)/3f;
                GUI.enabled=enabled && guide.CanBack;
                if(Widgets.ButtonText(new Rect(rect.x,footer,buttonWidth,34f),T("이전","Back"))) { guide.Back();scroll=Vector2.zero; }
                GUI.enabled=enabled;
                if(Widgets.ButtonText(new Rect(rect.x+buttonWidth+6f,footer,buttonWidth,34f),T("취소","Cancel"))) { Close();return; }
                GUI.enabled=enabled && (guide.Reviewing || guide.Selected!=null);
                if(Widgets.ButtonText(new Rect(rect.x+2f*(buttonWidth+6f),footer,buttonWidth,34f),guide.Reviewing?T("추천받기","Get suggestions"):T("다음","Next")))
                {
                    if(guide.Reviewing)
                    {
                        if(guide.Submit(out var request)) { Close();completed?.Invoke(request);return; }
                    }
                    else { guide.Next();scroll=Vector2.zero; }
                }
                GUI.enabled=enabled;
                if(!guide.Reviewing)
                {
                    if(Widgets.ButtonText(new Rect(rect.x,footer+40f,(rect.width-6f)/2f,30f),T("이 질문 건너뛰기","Skip this question"))) { guide.Skip();scroll=Vector2.zero; }
                    if(Widgets.ButtonText(new Rect(rect.x+(rect.width+6f)/2f,footer+40f,(rect.width-6f)/2f,30f),T("지금까지 답으로 마무리","Finish with these answers"))) { guide.Review();scroll=Vector2.zero; }
                }
            }
            finally { Text.Font=font; Text.Anchor=anchor; GUI.enabled=enabled; }
        }
    }
}
