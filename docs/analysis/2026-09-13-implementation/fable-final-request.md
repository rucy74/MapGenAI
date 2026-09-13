# Fable 최종 국소 검토 및 실제 측정 논의
추가 탐색/위임/수정 없이 첨부 범위만 한국어 600단어 이내 검토해 주세요. 중요 결함 최대 3개와 재현 입력, 현재 측정에서 정당한 결론을 제시하세요.
사용자: 기존 특징을 유지하는 누적 대화 편집, 포기했던 이미지/그림→맵과 복잡한 자연어 모두 개선. 기존판 v1.6 보존, dev 작업. 목표를 "임의 사진 재현 완료"로 축소/과장하지 않음.
실제 실행: offline 51 pass 이후 검사 추가 중. RimWorld 1.6 격리probe 25pass: Scribe 복합도형+image+baseline/lastApplied roundtrip, 손상 라벨복구, Dialog 실제HandleResponse/Undo/imageApply, Unity PNG/palette/EXIF8방향inverse, 다른타일선택상태에서100x100맵 생성. 중앙SDF호수289/289물, image호수+섬400/400water/land일치. 위험 밀도1.80 실제패치 로그. 픽셀 클릭 자동화/전체UI렌더 QA는 아직 안 됨.
실provider Gemini 2.5 Flash: 첫자연어3회 중2회shape_ops가params밖으로나와거부. 아래ShapeEditPrompt예시를완전envelope로수정한새3회는모두통과:산추가+호수크기+고대위협1.8; 기존산/호수보존. 추가하트호수+그호수오른쪽이동2회도통과. 원문과상태파일저장.
이미지합성fixture 2장x2회: 기본thinking 물IoU .26~.55,산0(비옥토오인). 공식Google권장대로2.5Flash vision만thinkingBudget0:첫지도물.853/산.483로두회동일,둘째그림은자기중복edge거부1회, 물.141/산0 1회. 이를임의이미지성공으로주장하지않음. 현재자동해석은dev 실험기능,팔레트경로는기하보존/영역수동교정가능. 범례 없는어두운면의정답'mountain'은실험자부여라서의미평가에모호성있음. 따라서추가정답지도/실사진평가와코드기하+AIlabel분리후속필요. 현재설정은유지하고provider변경강요없음.
앞선Fable지적:EXIF1..8적용,PNG1MiB초과시동일해상도JPEG85fallback후축소,PostLoadInit무효image복구+명시note/log 구현. 새이미지/프리셋은strict. 마지막correction-client-fail입력상실지적은반증:Client()가inputclear보다먼저있었음.
아래에대해중요결함/과장결론을검토해주세요. 광범위재설계가아니라지금증거에맞는개발판결론과구체적인후속우선순위도제안하세요.


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
        public static int ExifOrientation(byte[] data)
        {
            if(data.Length<4 || data[0]!=255 || data[1]!=216)return 1;
            int p=2;
            while(p+4<=data.Length)
            {
                if(data[p++]!=255)return 1;while(p<data.Length && data[p]==255)p++;
                if(p+3>data.Length)return 1;int marker=data[p++];if(marker==218 || marker==217)return 1;
                if(marker==1 || (marker>=208 && marker<=215))continue;
                int length=Big16(data,p),end=p+length;if(length<2 || end>data.Length)return 1;
                if(marker==225 && length>=16 && data[p+2]==69 && data[p+3]==120 && data[p+4]==105 && data[p+5]==102 && data[p+6]==0 && data[p+7]==0)
                {
                    int start=p+8;bool little=data[start]==73 && data[start+1]==73;
                    if(!little && !(data[start]==77 && data[start+1]==77))return 1;
                    Func<int,int> u16=i=>little?data[i]|(data[i+1]<<8):Big16(data,i);
                    Func<int,long> u32=i=>little?(long)data[i]|((long)data[i+1]<<8)|((long)data[i+2]<<16)|((long)data[i+3]<<24):(uint)Big32(data,i);
                    if(u16(start+2)!=42)return 1;
                    long offset=start+u32(start+4);if(offset<start+8 || offset+2>end)return 1;
                    int entry=(int)offset+2,count=u16((int)offset);
                    for(int n=0;n<count && entry+12<=end;n++,entry+=12)
                        if(u16(entry)==274 && u16(entry+2)==3 && u32(entry+4)==1)
                        {int value=u16(entry+8);return value>=1 && value<=8?value:1;}
                }
                p=end;
            }
            return 1;
        }
        static int Big32(byte[] data,int p) => (data[p]<<24)|(data[p+1]<<16)|(data[p+2]<<8)|data[p+3];
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RepairLoadedData();
        }
        public void RepairLoadedData()
        {
            try {Validate();}
            catch(FormatException)
            {
                const string warning="저장된 이미지 지형이 손상되어 복구했습니다. 확인 후 다시 적용하세요. / Invalid saved image terrain was repaired; review before applying.";
                if(width<1 || height<1 || width>MaxSide || height>MaxSide || cells==null || cells.Length!=width*height)
                {width=height=1;cells="N";}
                else
                {
                    var repaired=cells.ToCharArray();for(int i=0;i<repaired.Length;i++)if(!Names.ContainsValue(repaired[i]))repaired[i]='N';cells=new string(repaired);
                }
                note=warning;Log.Warning("[MapGenAI] "+warning);
            }
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

## MapGen/ShapeEditPrompt.cs
```csharp
namespace MapGenAI.MapGen
{
    public static class ShapeEditPrompt
    {
        public static string Rules(bool korean) => korean ? @"
지형 편집 계약:
- 일반 대화에서는 params.shape_ops로 요청한 지형만 편집합니다. ID는 현재 상태의 id를 사용합니다. 다른 지형은 코드가 보존하므로 다시 출력하지 마세요.
- 추가: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""lake_south"",""type"":""bump"",""position"":[0.5,0.2],""strength"":""negative_strong"",""size"":""small"",""fill"":""water""}}]}}
- 수정: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""update"",""id"":""lake_south"",""changes"":{""size"":""medium""}}]}}. changes에 보낸 필드만 바뀝니다. fill:null은 지형 채우기를 해제합니다. ID/type은 변경 불가.
- 삭제: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""remove"",""id"":""lake_south""}]}}
- 이동: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""position"":[0.7,0.2]}]}}. bump/ring/composite만 이동 가능. 산맥 방향은 update의 direction을 사용합니다.
- 상대 이동: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""relative_to"":""island"",""relation"":""below"",""distance"":0.2}]}}. 두 지형 중심 기준, left_of/right_of/above/below와 정규화 거리. 포함·통로 연결 보장이 아닙니다.
- 첫 맵 전체 설계는 elevation_shapes 목록도 허용합니다. 이미 지형이 있는데 전체 재설계를 명시적으로 요청했을 때만 replace_shapes:true와 전체 elevation_shapes를 함께 출력합니다. elevation_shapes:[]는 모든 사용자 지형 제거입니다. shape_ops와 elevation_shapes를 한 응답에 혼용하지 마세요.
- hills는 간단한 산 추가 단축키입니다. hills:none은 단축키로 만든 산만 제거합니다. ID가 여러 개라 대상이 모호하면 action:ask로 먼저 구분합니다.
- composite 내부 필드는 shapes와 compose입니다. 좌표는 x=0 왼쪽/1 오른쪽, z=0 아래/1 위. 존재하지 않는 대상·연산, 범위 밖 좌표, 빈 도형, 효과 없는 연산은 적용되지 않습니다.
- 서로 다른 지형을 수십 개 나열해 그림을 흉내내지 마세요. 지형 최대32, composite당 primitive/operation 최대32. 표현 불가능한 건 구체적으로 설명하고 대안을 질문하세요.
" : @"
Terrain edit contract:
- Use params.shape_ops for conversational edits. Target the id shown in current state. Unmentioned terrain is preserved by code; do not re-list it.
- Add: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""lake_south"",""type"":""bump"",""position"":[0.5,0.2],""strength"":""negative_strong"",""size"":""small"",""fill"":""water""}}]}}
- Update: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""update"",""id"":""lake_south"",""changes"":{""size"":""medium""}}]}}. Only supplied fields change. fill:null clears fill. id/type cannot change.
- Remove: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""remove"",""id"":""lake_south""}]}}
- Move: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""position"":[0.7,0.2]}]}}. Only bump/ring/composite can move. Update direction for a ridge.
- Relative move: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""relative_to"":""island"",""relation"":""below"",""distance"":0.2}]}}. Center-relative left_of/right_of/above/below, normalized distance. Does not guarantee containment or connected passages.
- Initial layout may use elevation_shapes. On an existing layout, output replace_shapes:true plus the full elevation_shapes only when the user explicitly requests complete redesign. elevation_shapes:[] clears all custom terrain. Never combine elevation_shapes with shape_ops.
- hills is a simple mountain addition shortcut. hills:none removes only shortcut-created mountains. Ask if multiple IDs make the target ambiguous.
- Composite fields are shapes and compose. Coordinates x=0 left/1 right; z=0 bottom/1 top. Missing operands/targets, out-of-range positions and ineffective shapes are rejected.
- At most 32 terrain shapes, 32 primitives/operations per composite. Explain concrete representation limits and ask about alternatives when needed.
";
    }
}

```