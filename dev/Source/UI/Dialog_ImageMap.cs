using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MapGenAI.ImageInput;
using MapGenAI.LLM;
using UnityEngine;
using Verse;

namespace MapGenAI.UI
{
    public sealed class Dialog_ImageMap : Window
    {
        readonly Func<ImageMapData,bool> apply;
        readonly RequestGate requests=new RequestGate();
        readonly Stack<ImageMapData> undo=new Stack<ImageMapData>();
        readonly int overlays;
        readonly int gridLimit;
        readonly string worldFeatures;
        Texture2D reference,preview;
        ImageMapData draft;
        List<ImageCandidate> candidates;
        ColorTerrainPlan pendingColors;
        List<int> selection=new List<int>();
        string path="",input="",status="",referenceNotes="";
        bool waiting,correcting,inferContours;
        int outputWidth=128,outputHeight=128;
        Vector2 notesScroll;
        public override Vector2 InitialSize => new Vector2(1000,780);
        static string T(string ko,string en) => L10n.IsKorean()?ko:en;

        public Dialog_ImageMap(ImageMapData existing,int overlayCount,Func<ImageMapData,bool> onApply,string worldFeatureSummary=null)
        {
            ImageFeatureGate.RequireEnabled();
            apply=onApply;overlays=overlayCount;draft=existing?.Clone();
            worldFeatures=worldFeatureSummary;
            gridLimit=Math.Max(1,Math.Min(128,Find.GameInitData?.mapSize??128));
            doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=true;forcePause=false;layer=WindowLayer.Super;
            if(draft!=null)Refresh();
        }
        public override void OnAcceptKeyPressed() { }
        public override void DoWindowContents(Rect rect)
        {
            var reply=requests.Take();
            if(reply!=null)
            {
                waiting=false;
                try
                {
                    if(reply.Error!=null)throw new Exception(reply.Error);
                    if(correcting)
                    {
                        var updated=ImageRegionCorrection.Apply(draft,selection,reply.Text,out status);
                        Edit(updated);
                    }
                    else
                    {
                        candidates=pendingColors==null?ImageInterpretation.Parse(reply.Text,outputWidth,outputHeight):new List<ImageCandidate>{pendingColors.Interpret(reply.Text)};
                        SelectCandidate(0);status=T("AI 해석을 원본과 비교하고 필요한 영역을 고치세요.","Compare the AI interpretation with the reference and correct regions.");
                    }
                }
                catch(Exception e){status=e.Message;}
            }
            var oldFont=Text.Font;Text.Font=GameFont.Small;
            Widgets.Label(new Rect(0,0,rect.width,28),T("이미지 → 지형 (개발 기능)","Image → terrain (experimental)"));
            path=Widgets.TextField(new Rect(0,32,rect.width-130,28),path);
            if(Widgets.ButtonText(new Rect(rect.width-124,32,124,28),T("PNG/JPEG 열기","Load PNG/JPEG")))Load();
            TooltipHandler.TipRegion(new Rect(0,32,rect.width-130,28),T("PNG/JPEG 파일 경로를 붙여넣으세요.","Paste a PNG/JPEG file path."));
            Widgets.Label(new Rect(0,63,126,26),T("이미지 설명 (선택)","Image notes"));
            GUI.enabled=!waiting;referenceNotes=Widgets.TextField(new Rect(130,63,rect.width-130,26),referenceNotes);GUI.enabled=true;
            TooltipHandler.TipRegion(new Rect(130,63,rect.width-130,26),T("맵의 배치가 읽히는 참고 이미지를 사용하세요. 추가 설명은 없어도 됩니다. 글자·작은 삽입 그림은 지형으로 섞일 수 있습니다.","Use a reference with readable map layout. Notes are optional. Labels and inset pictures may be mixed with terrain."));
            GUI.enabled=reference!=null && !waiting;
            if(Widgets.ButtonText(new Rect(0,92,180,30),T("AI로 해석","Interpret with AI")))Interpret();
            if(Widgets.ButtonText(new Rect(186,92,190,30),T("팔레트 색상 가져오기","Import palette colors")))
            {
                candidates=null;Edit(ImageTextureCodec.FromPalette(reference,gridLimit));status=draft.note;
            }
            GUI.enabled=true;
            if(waiting && Widgets.ButtonText(new Rect(382,92,120,30),T("요청 취소","Cancel request")))Cancel();
            if(candidates!=null && !waiting)
                for(int i=0;i<candidates.Count;i++)
                    if(Widgets.ButtonText(new Rect(382+i*150,92,144,30),T("후보 ","Candidate ")+(i+1)))SelectCandidate(i);
            GUI.enabled=!waiting;Widgets.CheckboxLabeled(new Rect(rect.width-220,94,216,26),T("윤곽을 AI가 다시 추론","Ask AI to redraw contours"),ref inferContours);GUI.enabled=true;
            TooltipHandler.TipRegion(new Rect(rect.width-220,94,216,26),T("기본은 원본 픽셀의 배치를 유지합니다. 이 옵션은 더 단순한 도형으로 해석하므로 작은 지형을 놓칠 수 있습니다.","Default preserves sampled source contours. This option asks for simpler shapes and can miss small features."));
            float side=Math.Min(300,Math.Min((rect.width-30)/2,Math.Max(100,rect.height-416))),left=(rect.width-2*side-20)/2;
            Widgets.Label(new Rect(left,126,side,24),T("원본 (위쪽 = 북쪽)","Reference (top = north)"));
            Widgets.Label(new Rect(left+side+20,126,side,24),T("지형 분류도 · 클릭해서 영역 선택","Terrain labels · click a region"));
            var sourceRect=new Rect(left,151,side,side);var planRect=new Rect(left+side+20,151,side,side);
            Widgets.DrawBoxSolid(sourceRect,Color.black);Widgets.DrawBoxSolid(planRect,Color.black);
            if(reference!=null)GUI.DrawTexture(Fit(sourceRect,reference.width,reference.height),reference);
            if(draft!=null)
            {
                var fit=Fit(planRect,draft.width,draft.height);GUI.DrawTexture(fit,preview);
                if(!waiting && Event.current.type==EventType.MouseDown && Event.current.button==0 && fit.Contains(Event.current.mousePosition))
                {
                    int x=Math.Min(draft.width-1,(int)((Event.current.mousePosition.x-fit.x)/fit.width*draft.width));
                    int z=Math.Min(draft.height-1,(int)((fit.yMax-Event.current.mousePosition.y)/fit.height*draft.height));
                    selection=draft.RegionAt(x,z);Refresh();Event.current.Use();
                }
            }
            float y=sourceRect.yMax+5;
            Text.Font=GameFont.Tiny;
            Widgets.Label(new Rect(0,y,rect.width,34),T("분류도는 실제 맵 미리보기가 아닙니다. 적용 후 Map Preview에서 확인하세요. 기존 지형 도형 ","This is a terrain plan. After applying, inspect the generated Map Preview. Existing terrain shapes: ")+overlays+T("개가 그 위에 적용됩니다."," (applied on top)."));
            y+=36;Text.Font=GameFont.Small;
            if(draft!=null)
            {
                bool flatten=draft.replaceElevation;GUI.enabled=!waiting;
                Widgets.CheckboxLabeled(new Rect(0,y,rect.width,24),T("이미지·추가 도형의 높이를 월드 지형보다 우선","Prioritize image and added shapes over world elevation"),ref flatten);GUI.enabled=true;
                if(flatten!=draft.replaceElevation){var updated=draft.Clone();updated.replaceElevation=flatten;Edit(updated);}
            }
            y+=26;
            int n=0;float buttonW=(rect.width-4*5)/5;
            GUI.enabled=draft!=null && selection.Count>0 && !waiting;
            foreach(var entry in ImageMapData.Names)
            {
                var box=new Rect((n%5)*(buttonW+5),y+(n/5)*30,buttonW,26);
                if(Widgets.ButtonText(box,Label(entry.Key))) {Edit(draft.Relabel(selection,entry.Value));status=T("선택 영역의 지형을 바꿨습니다.","Changed terrain in the selected region.");}
                Widgets.DrawBoxSolid(new Rect(box.x+5,box.y+5,14,16),ImageTextureCodec.Palette[entry.Value]);n++;
            }
            GUI.enabled=true;y+=62;
            input=Widgets.TextField(new Rect(0,y,rect.width-126,28),input);
            GUI.enabled=draft!=null && selection.Count>0 && !waiting && !string.IsNullOrWhiteSpace(input);
            if(Widgets.ButtonText(new Rect(rect.width-120,y,120,28),T("영역 채팅 수정","Edit region")))Correct();
            GUI.enabled=true;y+=30;
            Text.Font=GameFont.Tiny;
            Widgets.Label(new Rect(0,y,rect.width,24),T("채팅은 선택 영역의 지형 종류를 바꿉니다. 외곽 이동·변형과 건물 재현은 아직 지원하지 않습니다.","Chat changes the selected region's terrain type. Moving/reshaping contours and reconstructing buildings are not supported yet."));
            y+=24;
            var noteRect=new Rect(0,y,rect.width,Math.Max(40,rect.height-y-40));
            var note=status+(draft?.note==null?"":"\n"+draft.note)+"\n"+T("높이 우선: 이미지의 평지에서 기존 산을 제거합니다. 자연 지형 칸은 월드 지형을 유지합니다. 옵션을 끄면 토양은 기존 높이를 유지하며, 월드 지형이 이미지의 산·물 높이도 바꿀 수 있습니다. 작은 맵은 가는 통로를 놓칠 수 있습니다.","Elevation priority clears old mountains from image ground; natural cells retain world terrain. With it off, ground keeps its height and world landforms can also change image mountain/water heights. Small maps may lose thin passages.");
            if(draft!=null && Find.GameInitData!=null)note+="\n"+draft.SamplingWarning(Find.GameInitData.mapSize,Find.GameInitData.mapSize);
            if(!string.IsNullOrWhiteSpace(worldFeatures))note+="\n"+T("월드 지형 특징도 결과를 바꿀 수 있습니다: ","World terrain features can also change the result: ")+worldFeatures+T(". 필요하면 메인 대화창에서 해당 특징을 수정하세요.",". Adjust these features in the main conversation if needed.");
            var view=new Rect(0,0,noteRect.width-20,Math.Max(noteRect.height,Text.CalcHeight(note,noteRect.width-20)));
            Widgets.BeginScrollView(noteRect,ref notesScroll,view);Widgets.Label(view,note);Widgets.EndScrollView();
            Text.Font=GameFont.Small;float bottom=rect.height-34;
            GUI.enabled=draft!=null && !waiting;
            if(Widgets.ButtonText(new Rect(rect.width-190,bottom,190,30),T("타일에 적용","Apply to tile")))Apply(draft.Clone());
            GUI.enabled=undo.Count>0 && !waiting;
            if(Widgets.ButtonText(new Rect(0,bottom,130,30),T("수정 되돌리기","Undo draft edit"))) {draft=undo.Pop();selection.Clear();Refresh();}
            GUI.enabled=!waiting;
            if(Widgets.ButtonText(new Rect(140,bottom,150,30),T("이미지 지형 제거","Remove image layer")))Apply(null);
            GUI.enabled=true;Text.Font=oldFont;
        }
        static Rect Fit(Rect rect,int w,int h)
        {
            float scale=Math.Min(rect.width/w,rect.height/h);
            return new Rect(rect.x+(rect.width-w*scale)/2,rect.y+(rect.height-h*scale)/2,w*scale,h*scale);
        }
        static string Label(string name)
        {
            if(!L10n.IsKorean())return name;
            var ko=new[]{"자연 지형","산","물","얕은 물","토양","비옥토","모래","습지","진흙","얼음"};
            return ko[ImageMapData.Names.Keys.ToList().IndexOf(name)];
        }
        void Load()
        {
            Cancel();
            try
            {
                var loaded=ImageTextureCodec.Load(path);
                if(reference!=null)UnityEngine.Object.Destroy(reference);reference=loaded;
                candidates=null;status=T("원본을 불러왔습니다. AI로 해석하거나 팔레트 색상을 가져오세요.","Reference loaded. Interpret with AI or import palette colors.");
            }
            catch(Exception e){status=e.Message;}
        }
        ILLMClient Client()
        {
            var settings=MapGenAIMod.Settings;var config=settings.GetActiveConfig();
            if(config==null || !config.IsValid())throw new Exception(T("모드 설정에서 API를 구성하세요.","Configure an API in mod settings."));
            return LLMClientFactory.Create(config,settings.localBaseUrl);
        }
        void Interpret()
        {
            try
            {
                var client=Client() as IVisionClient;if(client==null)throw new Exception("Selected provider does not support image input");
                pendingColors=inferContours?null:ImageTextureCodec.AnalyzeColors(reference,gridLimit);
                var payload=pendingColors==null?ImageTextureCodec.ForVision(reference):ImageTextureCodec.ForColorVision(reference,pendingColors);
                float ratio=(float)gridLimit/Math.Max(reference.width,reference.height);
                outputWidth=Math.Max(1,(int)(reference.width*ratio));outputHeight=Math.Max(1,(int)(reference.height*ratio));
                string prompt=pendingColors==null?ImageInterpretation.BuildPrompt(L10n.IsKorean(),referenceNotes):pendingColors.BuildPrompt(L10n.IsKorean(),referenceNotes);
                Start(token=>client.SendImageAsync(payload.bytes,payload.mimeType,prompt,token),false);
            }
            catch(Exception e){status=e.Message;}
        }
        void Correct()
        {
            try
            {
                var client=Client();string question=input;input="";
                var history=new List<ChatMessage>{new ChatMessage("user","Selected "+selection.Count+" cells, current terrain "+ImageMapData.Names.First(p=>p.Value==draft.cells[selection[0]]).Key+". Request: "+question)};
                Start(token=>client.SendChatAsync(history,ImageRegionCorrection.Prompt,token),true);
            }
            catch(Exception e){status=e.Message;}
        }
        void Start(Func<System.Threading.CancellationToken,Task<string>> operation,bool correction)
        {
            correcting=correction;waiting=true;status=T("AI 응답을 기다리는 중…","Waiting for AI…");var ticket=requests.Begin();
            Task.Run(async()=>
            {
                try {var result=await operation(ticket.Token);requests.Complete(ticket,result,null);}
                catch(OperationCanceledException) when(ticket.Token.IsCancellationRequested) { }
                catch(Exception e){requests.Complete(ticket,null,e is OperationCanceledException?"Request timed out":e.Message);}
            });
        }
        void SelectCandidate(int i) {Edit(candidates[i].map.Clone());selection.Clear();Refresh();}
        void Edit(ImageMapData updated)
        {
            if(draft!=null && draft.cells==updated.cells && draft.width==updated.width && draft.height==updated.height && draft.note==updated.note && draft.replaceElevation==updated.replaceElevation)return;
            if(draft!=null)undo.Push(draft.Clone());
            if(draft==null || draft.width!=updated.width || draft.height!=updated.height)selection.Clear();
            draft=updated;Refresh();
        }
        void Refresh() {if(preview!=null)UnityEngine.Object.Destroy(preview);preview=draft==null?null:ImageTextureCodec.Preview(draft,selection);}
        void Cancel() {requests.Cancel();waiting=false;status=T("요청을 취소했습니다.","Request cancelled.");}
        void Apply(ImageMapData image)
        {
            if(apply(image))Close();else status=T("타일에 적용하지 못했습니다. 부모 대화창의 오류를 확인하세요.","Could not apply to the tile. Check the parent dialog for details.");
        }
        public override void PostClose()
        {
            requests.Cancel();if(reference!=null)UnityEngine.Object.Destroy(reference);if(preview!=null)UnityEngine.Object.Destroy(preview);base.PostClose();
        }
    }
}
