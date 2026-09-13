# 이미지 구현 Fable 검토
실제 사용자 승인: 기존 특징 보존, 이미지 재현 우선/불가능하면 해석 명시, 영역 클릭+팔레트+채팅 수정. 이 dev 1차는 채팅으로 선택 영역 지형 종류 변경까지 구현하며 외곽 변형 미구현을 UI/보고서에 명시합니다. 원본과 분류도를 함께 보여주고 분류도는 실제 맵 미리보기가 아님을 명시, 적용 후 기존 Map Preview를 갱신해 실제 맵 확인. 실제 vision 합성이미지 실험과 게임 probe를 별도 수행할 예정이고 아직 정확도 주장 없음.
첨부 코드만 검토하세요. 추가 탐색/수정/위임 없이 한국어 700단어 이내, 실제 중요한 결함 최대 3개와 재현 조건 제시. 이전 Fable 지적의 자기교차 검증/가려진 영역 경고를 추가했습니다. RequestGate는 이전 검토 완료; Begin/Cancel가 버전 증가, Complete는 현재 버전만, Take한번만.
부모 Apply는 before clone→imageMap교체→RestoreSnapshot 성공→Undo push, no-op은 push없음. 부모 닫힘 이후 callback거부. 이미지→SDF순서로 생성, N은바닐라그대로.

## UI/Dialog_ImageMap.cs
```csharp
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
        Texture2D reference,preview;
        ImageMapData draft;
        List<ImageCandidate> candidates;
        List<int> selection=new List<int>();
        string path="",input="",status="";
        bool waiting,correcting;
        int outputWidth=128,outputHeight=128;
        Vector2 notesScroll;
        public override Vector2 InitialSize => new Vector2(1000,780);
        static string T(string ko,string en) => L10n.IsKorean()?ko:en;

        public Dialog_ImageMap(ImageMapData existing,int overlayCount,Func<ImageMapData,bool> onApply)
        {
            apply=onApply;overlays=overlayCount;draft=existing?.Clone();
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
                        candidates=ImageInterpretation.Parse(reply.Text,outputWidth,outputHeight);
                        SelectCandidate(0);status=T("AI 해석을 원본과 비교하고 필요한 영역을 고치세요.","Compare the AI interpretation with the reference and correct regions.");
                    }
                }
                catch(Exception e){status=e.Message;}
            }
            var oldFont=Text.Font;Text.Font=GameFont.Small;
            Widgets.Label(new Rect(0,0,rect.width,28),T("이미지 → 지형 (개발 기능)","Image → terrain (experimental)"));
            path=Widgets.TextField(new Rect(0,32,rect.width-130,28),path);
            if(Widgets.ButtonText(new Rect(rect.width-124,32,124,28),T("PNG/JPEG 열기","Load PNG/JPEG")))Load();
            Widgets.Label(new Rect(0,63,rect.width,24),T("위 칸에 이미지 파일 경로를 붙여넣으세요. 선택한 제공자·모델은 모드 설정을 따릅니다.","Paste an image file path above. Uses the provider and model selected in mod settings."));
            GUI.enabled=reference!=null && !waiting;
            if(Widgets.ButtonText(new Rect(0,92,180,30),T("AI로 해석","Interpret with AI")))Interpret();
            if(Widgets.ButtonText(new Rect(186,92,190,30),T("팔레트 색상 가져오기","Import palette colors")))
            {
                candidates=null;Edit(ImageTextureCodec.FromPalette(reference));status=draft.note;
            }
            GUI.enabled=true;
            if(waiting && Widgets.ButtonText(new Rect(382,92,120,30),T("요청 취소","Cancel request")))Cancel();
            if(candidates!=null && !waiting)
                for(int i=0;i<candidates.Count;i++)
                    if(Widgets.ButtonText(new Rect(382+i*150,92,144,30),T("후보 ","Candidate ")+(i+1)))SelectCandidate(i);
            float side=Math.Min(300,(rect.width-30)/2),left=(rect.width-2*side-20)/2;
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
            int n=0;float buttonW=(rect.width-4*5)/5;
            GUI.enabled=draft!=null && selection.Count>0 && !waiting;
            foreach(var entry in ImageMapData.Names)
            {
                var box=new Rect((n%5)*(buttonW+5),y+(n/5)*30,buttonW,26);var tint=GUI.color;
                GUI.color=ImageTextureCodec.Palette[entry.Value];
                if(Widgets.ButtonText(box,Label(entry.Key))) {Edit(draft.Relabel(selection,entry.Value));status=T("선택 영역의 지형을 바꿨습니다.","Changed terrain in the selected region.");}
                GUI.color=tint;n++;
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
            var note=status+(draft?.note==null?"":"\n"+draft.note);
            var view=new Rect(0,0,noteRect.width-20,Math.Max(noteRect.height,Text.CalcHeight(note,noteRect.width-20)));
            Widgets.BeginScrollView(noteRect,ref notesScroll,view);Widgets.Label(view,note);Widgets.EndScrollView();
            Text.Font=GameFont.Small;float bottom=rect.height-34;
            GUI.enabled=draft!=null && !waiting;
            if(Widgets.ButtonText(new Rect(rect.width-190,bottom,190,30),T("타일에 적용","Apply to tile")) && apply(draft.Clone()))Close();
            GUI.enabled=undo.Count>0 && !waiting;
            if(Widgets.ButtonText(new Rect(0,bottom,130,30),T("수정 되돌리기","Undo draft edit"))) {draft=undo.Pop();selection.Clear();Refresh();}
            GUI.enabled=!waiting;
            if(Widgets.ButtonText(new Rect(140,bottom,150,30),T("이미지 지형 제거","Remove image layer")) && apply(null))Close();
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
                var bytes=ImageTextureCodec.ForVision(reference);
                float ratio=128f/Math.Max(reference.width,reference.height);
                outputWidth=Math.Max(1,(int)(reference.width*ratio));outputHeight=Math.Max(1,(int)(reference.height*ratio));
                string prompt=ImageInterpretation.Prompt+(L10n.IsKorean()?"\nWrite titles and notes in Korean.":"\nWrite titles and notes in English.");
                Start(token=>client.SendImageAsync(bytes,"image/png",prompt,token),false);
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
            if(draft!=null && draft.cells==updated.cells && draft.width==updated.width && draft.height==updated.height && draft.note==updated.note)return;
            if(draft!=null)undo.Push(draft.Clone());
            if(draft==null || draft.width!=updated.width || draft.height!=updated.height)selection.Clear();
            draft=updated;Refresh();
        }
        void Refresh() {if(preview!=null)UnityEngine.Object.Destroy(preview);preview=draft==null?null:ImageTextureCodec.Preview(draft,selection);}
        void Cancel() {requests.Cancel();waiting=false;status=T("요청을 취소했습니다.","Request cancelled.");}
        public override void PostClose()
        {
            requests.Cancel();if(reference!=null)UnityEngine.Object.Destroy(reference);if(preview!=null)UnityEngine.Object.Destroy(preview);base.PostClose();
        }
    }
}

```

## ImageInput/ImageMapData.cs
```csharp
using System;
using System.Collections.Generic;
using Verse;

namespace MapGenAI.ImageInput
{
    // Row zero is south/bottom, matching RimWorld z and Unity's GetPixels32 order.
    public sealed class ImageMapData : IExposable
    {
        public int width, height;
        public string cells;
        public string note;
        public const int MaxSide = 256;
        static readonly int[][] Neighbors = {new[]{-1,0},new[]{1,0},new[]{0,-1},new[]{0,1}};
        public static readonly Dictionary<string,char> Names = new Dictionary<string,char> {
            {"natural",'N'},{"mountain",'M'},{"water",'W'},{"shallow_water",'S'},
            {"soil",'G'},{"rich_soil",'R'},{"sand",'B'},{"marsh",'H'},{"mud",'D'},{"ice",'I'}
        };
        public void Validate()
        {
            if (width < 1 || height < 1 || width > MaxSide || height > MaxSide || cells == null || cells.Length != width * height)
                throw new FormatException("Invalid image terrain dimensions");
            foreach(char label in cells) if (!Names.ContainsValue(label)) throw new FormatException("Unknown image terrain label");
            if (note != null && note.Length > 4096) throw new FormatException("Image interpretation note is too long");
        }
        public ImageMapData Clone() => new ImageMapData {width=width,height=height,cells=cells,note=note};
        public void ExposeData()
        {
            Scribe_Values.Look(ref width,"width",0); Scribe_Values.Look(ref height,"height",0);
            Scribe_Values.Look(ref cells,"cells"); Scribe_Values.Look(ref note,"note");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Validate();
        }
        public char At(int x,int z) => cells[z*width+x];
        public static char Label(string name)
        {
            if (name == null || !Names.TryGetValue(name,out var result)) throw new FormatException("Unknown terrain label: " + name);
            return result;
        }

        public List<int> RegionAt(int x,int z)
        {
            Validate();
            if (x<0 || z<0 || x>=width || z>=height) throw new ArgumentOutOfRangeException();
            char label=At(x,z); var seen=new bool[cells.Length]; var queue=new Queue<int>(); var region=new List<int>();
            int start=z*width+x; seen[start]=true; queue.Enqueue(start);
            while(queue.Count>0)
            {
                int current=queue.Dequeue(); region.Add(current);
                int cx=current%width,cz=current/width;
                foreach(var offset in Neighbors)
                {
                    int nx=cx+offset[0],nz=cz+offset[1];
                    if(nx<0 || nz<0 || nx>=width || nz>=height) continue;
                    int next=nz*width+nx;
                    if(!seen[next] && cells[next]==label) {seen[next]=true;queue.Enqueue(next);}
                }
            }
            return region;
        }
        public ImageMapData Relabel(IEnumerable<int> region,char label)
        {
            if(!Names.ContainsValue(label)) throw new FormatException("Unknown terrain label");
            var copy=cells.ToCharArray();
            foreach(int index in region) {if(index<0 || index>=copy.Length) throw new FormatException("Invalid region cell");copy[index]=label;}
            var result=Clone(); result.cells=new string(copy); return result;
        }

        // Reuses the archived GridBuilder's label/elevation contract and nearest-neighbor scaling.
        // No boundary dilation: narrow corridors and islands must retain their authored footprint.
        public void Apply(Map map,MapGenFloatGrid elevation,MapGenFloatGrid fertility)
        {
            Validate();
            foreach(var cell in CellRect.WholeMap(map))
            {
                int x=Math.Min(width-1,cell.x*width/map.Size.x),z=Math.Min(height-1,cell.z*height/map.Size.z);
                char label=At(x,z);
                if(label=='N') continue;
                elevation[cell]=label=='M' ? .85f : .2f;
                switch(label)
                {
                    case 'W': fertility[cell]=-2005; break;
                    case 'S': fertility[cell]=-2025; break;
                    case 'G': fertility[cell]=-2085; break;
                    case 'R': fertility[cell]=-2095; break;
                    case 'B': fertility[cell]=-2075; break;
                    case 'H': fertility[cell]=-2045; break;
                    case 'D': fertility[cell]=-2055; break;
                    case 'I': fertility[cell]=-2065; break;
                }
            }
        }
    }
}

```

## ImageInput/ImageInterpretation.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.LLM;
using MapGenAI.UI;

namespace MapGenAI.ImageInput
{
    public sealed class ImageCandidate
    {
        public string title, notes;
        public ImageMapData map;
    }
    public static class ImageInterpretation
    {
        public const string Prompt = @"Interpret the supplied reference as a RimWorld terrain layout. Return one complete JSON object only.
Schema: {""view"":""top_down|oblique"",""candidates"":[{""title"":""short title"",""notes"":""uncertainties and substitutions"",""background"":""soil"",""regions"":[{""id"":""lake"",""label"":""water"",""vertices"":[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8]]}]}]}.
Preserve a top-down map/sketch as closely as possible, producing ONE candidate. For an oblique photo, produce TWO distinct plausible top-down interpretations, explaining missing depth and assumptions. These are editable terrain plans, not exact reconstructions. Do not claim completion of a game map.
Coordinates x=0 left, x=1 right, z=0 BOTTOM, z=1 TOP. Vertices describe closed polygons; do NOT repeat the first vertex at the end. Later regions overwrite earlier regions. Put a soil island AFTER its containing water region. Preserve narrow passages. At most 32 regions per candidate, 3..64 vertices per polygon, each coordinate in [0,1]. Avoid self-intersections. Do not output character grids or repeated rows.
Labels: natural (leave existing generated terrain), mountain, water, shallow_water, soil, rich_soil, sand, marsh, mud, ice. Trees/forests and buildings are not direct terrain labels: use soil and disclose that vegetation comes from the biome and structures are not reproduced. Never invent missing geometry silently. Notes must explain interpretation limits and any omitted features, in the user's language. Include at least one nonempty region. Output only JSON.";

        public static List<ImageCandidate> Parse(string response,int width=128,int height=128)
        {
            if(width<1 || height<1 || width>ImageMapData.MaxSide || height>ImageMapData.MaxSide) throw new FormatException("Invalid output dimensions");
            var root=ProviderResponse.Command(response); string view=root.GetString("view");
            var candidates=root.GetObjectArray("candidates");
            if(view!="top_down" && view!="oblique") throw new FormatException("Image view must be top_down or oblique");
            if(candidates==null || candidates.Count!=(view=="top_down"?1:2)) throw new FormatException("Top-down images require one candidate; oblique images require two");
            var result=new List<ImageCandidate>();
            foreach(var candidate in candidates)
            {
                var regions=candidate.GetObjectArray("regions");
                if(regions==null || regions.Count<1 || regions.Count>32) throw new FormatException("Image interpretation requires 1..32 regions");
                string title=candidate.GetString("title"),notes=candidate.GetString("notes");
                if(string.IsNullOrWhiteSpace(title) || title.Length>120 || string.IsNullOrWhiteSpace(notes) || notes.Length>4096) throw new FormatException("Image interpretation requires a title and explanation");
                char background=ImageMapData.Label(candidate.GetString("background"));
                var cells=Enumerable.Repeat(background,width*height).ToArray();
                var ids=new HashSet<string>();
                var owners=Enumerable.Repeat(-1,cells.Length).ToArray();
                var regionNames=new List<string>();
                foreach(var region in regions)
                {
                    string id=region.GetString("id");
                    if(string.IsNullOrEmpty(id) || id.Length>64 || !ids.Add(id)) throw new FormatException("Missing or duplicate image region id");
                    char label=ImageMapData.Label(region.GetString("label"));
                    int owner=regionNames.Count; regionNames.Add(id);
                    var polygon=region.GetNestedFloatArray("vertices");
                    if(polygon==null || polygon.Length<3 || polygon.Length>64) throw new FormatException("Invalid region polygon");
                    // Share degenerate-coordinate/edge/area validation with terrain primitives.
                    ShapeValidation.Validate(new ElevationShape {type="composite",compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="region",prim="poly",verts=polygon}},compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="region",e=1}}});
                    int count=0;
                    for(int z=0;z<height;z++) for(int x=0;x<width;x++)
                        if(Contains(polygon,(x+.5f)/width,(z+.5f)/height)) {cells[z*width+x]=label;owners[z*width+x]=owner;count++;}
                    if(count==0) throw new FormatException("Image region is smaller than a terrain cell: "+id);
                }
                for(int i=0;i<regionNames.Count;i++) if(!owners.Contains(i))
                    notes+="\n겹침으로 사라진 영역 / Fully covered region: "+regionNames[i];
                if(cells.Distinct().Count()==1) notes+="\n단일 지형으로 해석되었습니다. 원본과 비교한 뒤 적용하세요. / Only one terrain class; compare with the reference before applying.";
                var map=new ImageMapData {width=width,height=height,cells=new string(cells),note=notes}; map.Validate();
                result.Add(new ImageCandidate {title=title,notes=notes,map=map});
            }
            return result;
        }
        static bool Contains(float[][] vertices,float x,float z)
        {
            bool inside=false;
            for(int i=0,j=vertices.Length-1;i<vertices.Length;j=i++)
            {
                var a=vertices[i];var b=vertices[j];
                if((a[1]>z)!=(b[1]>z) && x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]) inside=!inside;
            }
            return inside;
        }
    }
}

```

## ImageInput/ImageRegionCorrection.cs
```csharp
using System;
using System.Collections.Generic;
using MapGenAI.LLM;

namespace MapGenAI.ImageInput
{
    public static class ImageRegionCorrection
    {
        public const string Prompt = @"The user selected a connected region in an editable RimWorld terrain plan. You may change only its terrain label. Preserve all other regions. Reply with one JSON object: {""action"":""relabel"",""label"":""soil""} or {""action"":""ask"",""message"":""explain limitation or ask a question""}. Allowed labels: natural,mountain,water,shallow_water,soil,rich_soil,sand,marsh,mud,ice. If asked to move, reshape, resize, add buildings, or add forests, use ask and explain those edits are not supported by this region tool. Never claim those changes happened. Reply in the user's language.";
        public static ImageMapData Apply(ImageMapData map,IEnumerable<int> selection,string response,out string message)
        {
            var root=ProviderResponse.Command(response);
            if(root.GetString("action")=="ask") {message=root.GetString("message")??"No terrain label was changed.";return map.Clone();}
            if(root.GetString("action")!="relabel") throw new FormatException("Unsupported image region correction");
            char label=ImageMapData.Label(root.GetString("label"));
            message="영역 지형 / Region terrain → "+root.GetString("label");
            return map.Relabel(selection,label);
        }
    }
}

```

## ImageInput/ImageHeader.cs
```csharp
using System;

namespace MapGenAI.ImageInput
{
    // Inspect dimensions before asking Unity to allocate decoded pixels.
    public static class ImageHeader
    {
        public static void Validate(byte[] data)
        {
            if(data==null || data.Length<24 || data.Length>12*1024*1024) throw new FormatException("PNG/JPEG must be at most 12 MiB");
            int w=0,h=0;
            if(data[0]==137 && data[1]==80 && data[2]==78 && data[3]==71 && data[4]==13 && data[5]==10 && data[6]==26 && data[7]==10)
            {
                if(data[12]!=73 || data[13]!=72 || data[14]!=68 || data[15]!=82) throw new FormatException("Missing PNG IHDR");
                w=Big32(data,16); h=Big32(data,20);
            }
            else if(data[0]==255 && data[1]==216)
            {
                int p=2;
                while(p<data.Length)
                {
                    if(data[p++]!=255) throw new FormatException("Invalid JPEG marker");
                    while(p<data.Length && data[p]==255) p++;
                    if(p>=data.Length) break;
                    int marker=data[p++];
                    if(marker==217 || marker==218) break;
                    if(marker==1 || (marker>=208 && marker<=215)) continue;
                    if(p+2>data.Length) break;
                    int length=Big16(data,p);
                    if(length<2 || p+length>data.Length) throw new FormatException("Truncated JPEG segment");
                    if((marker>=192 && marker<=195) || (marker>=197 && marker<=199) || (marker>=201 && marker<=203) || (marker>=205 && marker<=207))
                    {
                        if(length<8) throw new FormatException("Invalid JPEG size segment");
                        h=Big16(data,p+3);w=Big16(data,p+5);break;
                    }
                    p+=length;
                }
            }
            if(w<1 || h<1 || w>8192 || h>8192 || (long)w*h>16000000) throw new FormatException("Use a PNG/JPEG up to 8192 per side and 16 million pixels");
        }
        static int Big16(byte[] data,int p) => (data[p]<<8)|data[p+1];
        static int Big32(byte[] data,int p) => (data[p]<<24)|(data[p+1]<<16)|(data[p+2]<<8)|data[p+3];
    }
}

```

## ImageInput/ImageTextureCodec.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MapGenAI.ImageInput
{
    public static class ImageTextureCodec
    {
        public static readonly Dictionary<char,Color32> Palette = new Dictionary<char,Color32> {
            {'N',new Color32(150,150,150,255)}, {'M',new Color32(80,70,60,255)},
            {'W',new Color32(30,90,210,255)}, {'S',new Color32(70,170,235,255)},
            {'G',new Color32(125,170,80,255)}, {'R',new Color32(65,105,45,255)},
            {'B',new Color32(230,205,125,255)}, {'H',new Color32(85,140,130,255)},
            {'D',new Color32(120,85,60,255)}, {'I',new Color32(220,245,255,255)}
        };
        public static Texture2D Load(string path)
        {
            var info=new FileInfo(path.Trim().Trim('"'));
            if(!info.Exists || info.Length>12*1024*1024) throw new FormatException("Image file missing or larger than 12 MiB");
            var data=File.ReadAllBytes(info.FullName); ImageHeader.Validate(data);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!ImageConversion.LoadImage(texture,data,false)) throw new FormatException("Could not decode PNG/JPEG");
                texture.filterMode=FilterMode.Point;
                return texture;
            }
            catch {UnityEngine.Object.Destroy(texture);throw;}
        }
        public static byte[] ForVision(Texture2D source)
        {
            for(int limit=768;limit>=192;limit/=2)
            {
                var resized=Resize(source,limit);
                try {var data=ImageConversion.EncodeToPNG(resized);if(data.Length<=1024*1024)return data;}
                finally {UnityEngine.Object.Destroy(resized);}
            }
            throw new FormatException("Could not reduce image to 1 MiB");
        }
        static Texture2D Resize(Texture2D source,int limit)
        {
            float ratio=Math.Min(1f,(float)limit/Math.Max(source.width,source.height));
            int w=Math.Max(1,(int)(source.width*ratio)),h=Math.Max(1,(int)(source.height*ratio));
            var old=source.GetPixels32();var pixels=new Color32[w*h];
            for(int z=0;z<h;z++)for(int x=0;x<w;x++) pixels[z*w+x]=old[(z*source.height/h)*source.width+x*source.width/w];
            return Make(w,h,pixels);
        }
        public static ImageMapData FromPalette(Texture2D source)
        {
            var small=Resize(source,128);
            try
            {
                var pixels=small.GetPixels32();var cells=new char[pixels.Length];int unknown=0;
                for(int i=0;i<pixels.Length;i++)
                {
                    int best=int.MaxValue;char label='N';
                    foreach(var entry in Palette)
                    {
                        var color=entry.Value;var pixel=pixels[i];
                        int distance=(pixel.r-color.r)*(pixel.r-color.r)+(pixel.g-color.g)*(pixel.g-color.g)+(pixel.b-color.b)*(pixel.b-color.b);
                        if(distance<best){best=distance;label=entry.Key;}
                    }
                    if(pixels[i].a<128 || best>3600) {label='N';unknown++;}
                    cells[i]=label;
                }
                return new ImageMapData {width=small.width,height=small.height,cells=new string(cells),note="팔레트 색상 매칭 / Palette color matching. 인식되지 않은 픽셀은 자연 지형으로 유지 / Unmatched pixels use natural terrain: "+unknown+" / "+cells.Length};
            }
            finally {UnityEngine.Object.Destroy(small);}
        }
        public static Texture2D Preview(ImageMapData map,IEnumerable<int> selection=null)
        {
            map.Validate();var pixels=new Color32[map.cells.Length];
            for(int i=0;i<pixels.Length;i++)pixels[i]=Palette[map.cells[i]];
            if(selection!=null)foreach(int i in selection)
            {
                var c=pixels[i];pixels[i]=new Color32((byte)((c.r+255)/2),(byte)((c.g+235)/2),(byte)(c.b/2),255);
            }
            return Make(map.width,map.height,pixels);
        }
        static Texture2D Make(int width,int height,Color32[] pixels)
        {
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            texture.SetPixels32(pixels);texture.Apply();return texture;
        }
    }
}

```