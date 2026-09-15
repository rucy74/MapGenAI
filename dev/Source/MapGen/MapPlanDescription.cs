using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MapGenAI.UI;

namespace MapGenAI.MapGen
{
    public sealed class PlanDefinition
    {
        public string name, description;
        public PlanDefinition(string name,string description=null){this.name=name;this.description=description;}
    }

    // Player-facing explanations derived from the validated changes, never from model prose.
    // Definition lookup is supplied on the game UI thread so installed translations are authoritative.
    public sealed class MapPlanDescription
    {
        readonly bool korean;
        readonly Func<string,string,PlanDefinition> lookup;
        public MapPlanDescription(bool korean,Func<string,string,PlanDefinition> lookup=null){this.korean=korean;this.lookup=lookup;}
        string T(string ko,string en)=>korean?ko:en;
        static string Clean(string text)=>Regex.Replace(Regex.Replace(text??"",@"<[^>]*>",""),@"\s+"," ").Trim();
        public string Name(string kind,string id)
        {
            var text=lookup?.Invoke(kind,id);string name=Clean(text?.name);
            return name.Length>0?name:kind=="terrain"?T("지형 재료","terrain material"):kind=="rock"?T("석재","stone"):T("지형 특징","map feature");
        }
        string Feature(string id,bool added)
        {
            string help=Clean(lookup?.Invoke("feature",id)?.description);
            // Keep the source explanation available, without making an enormous choice card.
            if(help.Length>280){int end=help.LastIndexOfAny(new[]{'.','。'},Math.Min(279,help.Length-1));help=help.Substring(0,end>80?end+1:277)+"…";}
            return Name("feature",id)+T(added?" 추가":" 제거",added?" added":" removed")+(added && help.Length>0?" — "+help:"");
        }
        static bool Same(ElevationShape a,ElevationShape b)=>SimpleJson.Serialize(ShapeEdits.ToObject(a))==SimpleJson.Serialize(ShapeEdits.ToObject(b));
        public string Describe(TileMapState before,TileMapState after)
        {
            var lines=new List<string>();
            foreach(string id in after.mutators.Except(before.mutators))lines.Add(Feature(id,true));
            foreach(string id in before.mutators.Except(after.mutators).Union(after.removeMutators.Except(before.removeMutators)))lines.Add(Feature(id,false));
            foreach(string id in before.removeMutators.Except(after.removeMutators).Except(after.mutators))lines.Add(Name("feature",id)+T("의 제외 설정을 해제합니다."," is no longer excluded."));
            foreach(var shape in after.elevationShapes)
            {
                var old=before.elevationShapes.FirstOrDefault(s=>s.id==shape.id);
                if(old!=null && Same(old,shape))continue;
                if(old==null)lines.Add(Shape(shape)+T(" 추가", " added"));
                else
                {
                    string description=Shape(shape);
                    var changes=new List<string>();
                    if(old.position!=shape.position || SimpleJson.Serialize(old.compositeShapes)!=SimpleJson.Serialize(shape.compositeShapes))changes.Add(T("모양·위치 조정","shape/location adjusted"));
                    if(old.size!=shape.size || old.gap!=shape.gap)changes.Add(T("크기·폭 조정","size/width adjusted"));
                    if(old.strength!=shape.strength)changes.Add(T("높낮이 조정","height adjusted"));
                    if(old.fill!=shape.fill || old.coverage!=shape.coverage || old.region!=shape.region || old.region_part!=shape.region_part || SimpleJson.Serialize(old.compositeOps)!=SimpleJson.Serialize(shape.compositeOps))changes.Add(T("영역의 높이·채움 조정","region height/fill adjusted"));
                    if(old.edge_roughness!=shape.edge_roughness)changes.Add(T("윤곽 조정","outline adjusted"));
                    if(old.scope!=shape.scope)changes.Add(T("통로 적용 범위 조정","passage scope adjusted"));
                    if(old.direction!=shape.direction)changes.Add(T("방향 조정","direction adjusted"));
                    if(old.fade!=shape.fade || old.noise_amount!=shape.noise_amount)changes.Add(T("산맥의 폭·굴곡 조정","ridge width/irregularity adjusted"));
                    lines.Add(description+" — "+(changes.Count==0?T("설정 조정","settings adjusted"):string.Join(", ",changes)));
                }
            }
            foreach(var shape in before.elevationShapes.Where(s=>after.elevationShapes.All(a=>a.id!=s.id)))lines.Add(Shape(shape)+T(" 편집을 제거해 원래 지형으로 되돌립니다."," edit removed, restoring the underlying terrain."));
            foreach(var structure in after.structures)
            {
                var old=before.structures.FirstOrDefault(s=>s.id==structure.id);
                if(old!=null && SimpleJson.Serialize(old)==SimpleJson.Serialize(structure))continue;
                string where=structure.position!=null?Position(structure.position[0],structure.position[1]):T("지정한 영역","the chosen area");
                if(structure.region!=null)
                {
                    var region=after.elevationShapes.FirstOrDefault(s=>s.id==structure.region);
                    where=region==null?where:Shape(region)+T(" 안쪽"," interior");
                }
                string kind=structure.kind=="ancient_danger"?T("고대 위협","ancient danger"):T("폐허","ruins");
                string text=where+" — "+kind+" "+structure.count+T("개, "," instance(s), ")+structure.width+"×"+structure.height+T("칸 배치 계획"," cells each");
                if(structure.relation!=null)text+="; "+Relation(structure.relation);
                if(structure.rotation!=0)text+=T("; 회전 ","; rotation ")+structure.rotation+"°";
                if(structure.spacing>1)text+=T("; 사이 간격 최소 ","; minimum gap ")+structure.spacing+T("칸"," cells");
                lines.Add(text);
            }
            foreach(var old in before.structures.Where(s=>after.structures.All(a=>a.id!=s.id)))lines.Add((old.kind=="ancient_danger"?T("고대 위협","Ancient danger"):T("폐허","Ruins"))+T(" 배치 계획 제거"," placement plan removed"));
            foreach(string field in MapStateCodec.ChangedFields(before,after))
            {
                switch(field)
                {
                    case "mutators":case "removeMutators":case "elevationShapes":case "structures":break;
                    case "cavesExplicitlySet":if(before.hasCaves==after.hasCaves)lines.Add(T(after.cavesExplicitlySet?(after.hasCaves?"동굴 생성을 명시적으로 허용합니다.":"동굴 생성을 명시적으로 차단합니다."):"동굴 생성을 기본 규칙에 맡깁니다.",after.cavesExplicitlySet?(after.hasCaves?"Explicitly allow caves.":"Explicitly disable caves."):"Use default cave generation rules."));break;
                    case "fertilityOffset":lines.Add(after.fertilityOffset>before.fertilityOffset?T("토양의 비옥도를 높입니다.","Increase soil fertility."):T("토양의 비옥도를 낮춥니다.","Decrease soil fertility."));break;
                    case "vegetationDensity":lines.Add(More(after.vegetationDensity,before.vegetationDensity,"식물","plants"));break;
                    case "animalDensity":lines.Add(More(after.animalDensity,before.animalDensity,"야생동물","wild animals"));break;
                    case "oreDensity":lines.Add(More(after.oreDensity,before.oreDensity,"광석","ore"));break;
                    case "ruinDensity":lines.Add(More(after.ruinDensity,before.ruinDensity,"자연 생성 폐허","naturally generated ruins"));break;
                    case "dangerDensity":lines.Add(More(after.dangerDensity,before.dangerDensity,"자연 생성 고대 위협","naturally generated ancient dangers"));break;
                    case "hillAmount":lines.Add(More(after.hillAmount,before.hillAmount,"산과 언덕","mountains and hills"));break;
                    case "hills":break; // Represented by the corresponding generated shape.
                    case "hillSize":lines.Add(after.hillSize<before.hillSize?T("산맥을 더 넓은 덩어리로 만듭니다.","Make broader mountain formations."):T("산맥을 더 잘게 나눕니다.","Make smaller mountain formations."));break;
                    case "hillSmoothness":lines.Add(T("산맥의 표면 굴곡을 조정합니다.","Adjust mountain surface irregularity."));break;
                    case "hasRiver":lines.Add(T(after.hasRiver?"강 생성을 켭니다.":"강 생성 설정을 끕니다.",after.hasRiver?"Enable river generation.":"Disable the river generation setting."));break;
                    case "hasRoads":lines.Add(T(after.hasRoads?"도로 생성을 켭니다.":"도로 생성을 끕니다.",after.hasRoads?"Enable roads.":"Disable roads."));break;
                    case "hasCaves":lines.Add(T(after.hasCaves?"동굴 생성을 켭니다.":"동굴 생성을 끕니다.",after.hasCaves?"Enable caves.":"Disable caves."));break;
                    case "hasRockChunks":lines.Add(T(after.hasRockChunks?"돌덩어리가 흩어져 있는 지형으로 만듭니다.":"흩어진 돌덩어리 생성을 끕니다.",after.hasRockChunks?"Include scattered rock chunks.":"Disable scattered rock chunks."));break;
                    case "straightRiver":lines.Add(T(after.straightRiver?"강줄기를 직선으로 만듭니다.":"강줄기의 자연스러운 굽이를 사용합니다.",after.straightRiver?"Straighten the river.":"Use the river's natural bends."));break;
                    case "riverDirectionAngle":lines.Add(after.riverDirectionAngle<0?T("강 방향을 원래 지형에 맞춥니다.","Use the original river direction."):T("강의 흐름 방향: ","River flow direction: ")+Direction(after.riverDirectionAngle));break;
                    case "riverXPosition":lines.Add(T("강의 좌우 위치를 ","Move the river toward the ")+T(after.riverXPosition>before.riverXPosition?"오른쪽으로 옮깁니다.":"왼쪽으로 옮깁니다.",after.riverXPosition>before.riverXPosition?"east.":"west."));break;
                    case "riverZPosition":lines.Add(T("강의 상하 위치를 ","Move the river toward the ")+T(after.riverZPosition>before.riverZPosition?"북쪽으로 옮깁니다.":"남쪽으로 옮깁니다.",after.riverZPosition>before.riverZPosition?"north.":"south."));break;
                    case "coastDirection":lines.Add(T("해안 방향: ","Coast direction: ")+Word(after.coastDirection));break;
                    case "geyserCount":lines.Add(after.geyserCount<0?T("간헐천 수를 기본값으로 되돌립니다.","Restore the default geyser count."):T("간헐천 ","Geysers: ")+after.geyserCount+T("개를 요청합니다."," requested."));break;
                    case "rockCount":lines.Add(after.rockCount<0?T("석재 종류 수를 기본값으로 되돌립니다.","Restore the default stone variety."):T("석재 종류를 ","Use ")+after.rockCount+T("종으로 설정합니다."," types of stone."));break;
                    case "rockTypes":lines.Add(after.rockTypes.Count==0?T("석재 종류를 자동으로 고릅니다.","Choose stone types automatically."):T("사용할 석재: ","Stone types: ")+string.Join(", ",after.rockTypes.Select(id=>Name("rock",id))));break;
                    case "removeFeatureCategories":
                        foreach(var c in after.removeFeatureCategories.Except(before.removeFeatureCategories))lines.Add(Category(c)+T(" 종류를 생성에서 제외합니다."," features excluded from generation."));
                        foreach(var c in before.removeFeatureCategories.Except(after.removeFeatureCategories))lines.Add(Category(c)+T(" 종류의 생성을 다시 허용합니다."," features allowed again."));break;
                    case "imageMap":lines.Add(T("보관된 이미지 설정을 변경합니다. 이미지 생성 기능은 계속 중단 상태입니다.","Stored image settings changed. Image generation remains paused."));break;
                    default:lines.Add(T("추가 지형 설정을 조정합니다.","Adjust an additional terrain setting."));break;
                }
            }
            return string.Join("\n",lines.Select(line=>"• "+line));
        }
        string More(float value,float old,string ko,string en)=>korean?ko+" 생성량을 "+(value>old?"늘립니다.":"줄입니다."):(value>old?"Increase ":"Decrease ")+en+" generation.";
        string Direction(float angle)
        {
            string[] ko={"동쪽","북동쪽","북쪽","북서쪽","서쪽","남서쪽","남쪽","남동쪽"};
            string[] en={"east","northeast","north","northwest","west","southwest","south","southeast"};
            int index=(int)Math.Floor(((angle%360+360)%360+22.5f)/45)%8;return korean?ko[index]:en[index];
        }
        public string Word(string value)
        {
            switch(value)
            {
                case "left":case "west":return T("서쪽","west");case "right":case "east":return T("동쪽","east");
                case "top":case "north":return T("북쪽","north");case "bottom":case "south":return T("남쪽","south");
                case "top_left":return T("북서쪽","northwest");case "top_right":return T("북동쪽","northeast");
                case "bottom_left":return T("남서쪽","southwest");case "bottom_right":return T("남동쪽","southeast");
                case "center":return T("중앙","center");case "auto":case null:return T("원래 지형에 맞춤","follow the original terrain");
                default:return T("지정한 방향","the chosen direction");
            }
        }
        string Position(float x,float z)
        {
            string side=z>.65f?(x<.35f?"top_left":x>.65f?"top_right":"top"):z<.35f?(x<.35f?"bottom_left":x>.65f?"bottom_right":"bottom"):(x<.35f?"left":x>.65f?"right":"center");
            return T("지도 ","map ")+Word(side);
        }
        public string Shape(ElevationShape s)
        {
            if(s.type=="passage")return T("폭 ","Dry passage, ")+s.width+T("칸의 마른 통로 — "," cells wide — ")+Name("terrain",s.fill)+", "+Position(s.points[0][0],s.points[0][1])+" → "+Position(s.points.Last()[0],s.points.Last()[1])+(s.scope=="mountains"?T(" (산 부분만, 평지 유지)"," (mountains only; open ground preserved)"):T(" (전체 경로)"," (entire route)"));
            if(s.type=="region_fill")return T(s.region_part=="enclosed"?"둘러싸인 내부의 채울 수 있는 땅":"지정 영역의 채울 수 있는 땅",s.region_part=="enclosed"?"usable area enclosed by the terrain":"usable area within the region")+" "+(float.Parse(s.coverage,CultureInfo.InvariantCulture)*100).ToString("0.#",CultureInfo.InvariantCulture)+"% — "+Name("terrain",s.fill)+T(" 채움"," fill");
            float strength=ElevationShape.ParseStrength(s.strength);bool raised=strength>=0;
            if(s.type=="radial")return raised?T("가장자리가 높고 중앙이 낮은 분지","basin with raised edges and a lower center"):T("중앙이 높고 바깥으로 낮아지는 산","mountain with a raised center and lower edges");
            if(s.type=="split")return raised?T("양쪽이 높고 가운데가 낮은 협곡","canyon between raised sides"):T("지도를 가로지르는 산맥","mountain range across the map");
            if(s.type=="slope")return Direction(ElevationShape.ParseDirection(s.direction))+T(raised?"으로 높아지는 경사":"으로 낮아지는 경사",raised?"-rising slope":"-descending slope");
            if(s.type=="ridge")return Direction(ElevationShape.ParseDirection(s.direction))+T(raised?" 가장자리의 산맥":" 가장자리의 낮아진 지형",raised?" edge mountain range":" edge lowered terrain");
            if(s.type=="noise")return T(raised?"곳곳에 흩어진 언덕":"곳곳에 낮게 패인 지형",raised?"scattered hills":"scattered depressions");
            var fills=new List<string>();
            if(!string.IsNullOrEmpty(s.fill))fills.Add(s.fill);
            else if(s.compositeOps!=null)fills.AddRange(s.compositeOps.Where(o=>o.outId==null && !string.IsNullOrEmpty(o.fill)).Select(o=>o.fill));
            string material=fills.Count>0?string.Join("·",fills.Distinct().Select(f=>Name("terrain",f))):s.type=="composite"?T("높낮이를 조정한 지형","height-adjusted terrain"):T(raised?"높인 지형":"낮춘 지형",raised?"raised terrain":"lowered terrain");
            string form=s.type=="ring"?T("고리 모양 ","ring-shaped "):"";string where;
            if(s.type=="composite")
            {
                var parts=s.compositeShapes;
                if(parts?.Count==1)form=Primitive(parts[0].prim)+" ";
                else if(parts?.Count==2 && parts.All(p=>p.prim=="circle") && s.compositeOps.Any(o=>o.op=="sub"))form=T("고리 모양 ","ring-shaped ");
                else form=T("여러 모양을 합친 ","combined-shape ");
                var centers=parts?.Select(p=>p.GetCenter()).ToList();
                where=centers?.Count>0?Position(centers.Average(c=>c.x),centers.Average(c=>c.y)):T("지정한 영역","the chosen area");
                bool irregular=!string.IsNullOrEmpty(s.edge_roughness) && s.edge_roughness!="none" && s.edge_roughness!="0";
                return where+" — "+form+material+T(" 영역"," area")+(irregular?T(" (불규칙한 가장자리)"," (irregular outline)"):"");
            }
            var pos=ElevationShape.ParsePosition(s.position);where=Position(pos.x,pos.y);
            return where+" — "+form+material+T(" 영역"," area");
        }
        string Primitive(string prim)
        {
            switch(prim){case "circle":return T("둥근","round");case "ellipse":return T("타원형","oval");case "rect":return T("직사각형","rectangular");case "tri":return T("삼각형","triangular");case "star":return T("별 모양","star-shaped");case "heart":return T("하트 모양","heart-shaped");default:return T("다각형","polygonal");}
        }
        string Category(string category)
        {
            switch(category){case "Lake":return T("호수","lake");case "River":return T("강","river");case "Coast":return T("해안","coast");case "Caves":return T("동굴","cave");case "PlantLife":return T("식물 관련 특징","plant-life");case "Groundwater":return T("지하수","groundwater");default:return Name("category",category);}
        }
        string Relation(SpatialRelation r)
        {
            string target=r.target=="river"?T("강","river"):r.target=="water"?T("물가","water"):r.target=="mountain"?T("산","mountain"):T("영역 안쪽 경계","inner region boundary");
            return target+(r.side=="any"?"":" "+Word(r.side))+T("에서 "," at ")+r.min_distance+"~"+r.max_distance+T("칸 거리"," cells distance");
        }
    }
}
