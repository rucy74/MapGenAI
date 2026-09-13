using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;
using MapGenAI.UI;

namespace MapGenAI.MapGen
{
    public static class MapStateDescription
    {
        private static readonly Dictionary<string,string> Korean = new Dictionary<string,string>
        {
            {"hills","산 방향"},{"hillAmount","산 양"},{"elevationShapes","지형 도형"},{"imageMap","이미지 지형"},
            {"vegetationDensity","식생 밀도"},{"fertilityOffset","비옥도"},{"animalDensity","동물 밀도"},
            {"hasRiver","강 설정"},{"riverDirectionAngle","강 각도"},{"riverXPosition","강 좌우 위치"},{"riverZPosition","강 상하 위치"},{"straightRiver","직선 강"},
            {"hasRoads","도로"},{"hasCaves","동굴"},{"cavesExplicitlySet","동굴 지정"},{"geyserCount","간헐천 수"},
            {"hasRockChunks","돌덩어리"},{"hillSize","산 크기"},{"hillSmoothness","산 부드러움"},
            {"mutators","추가 특징"},{"removeMutators","제거 특징"},{"removeFeatureCategories","생성에서 제외할 특징 종류"},{"coastDirection","해안 방향"},
            {"rockCount","석재 종류 수"},{"oreDensity","광석 밀도"},{"rockTypes","석재 종류"},
            {"ruinDensity","폐허 밀도"},{"dangerDensity","고대 위협 밀도"}
        };
        public static string Describe(TileMapState before, TileMapState after, bool korean)
        {
            var text=new StringBuilder(korean ? "설정 변경 내용:" : "Settings changed:");
            foreach(string key in MapStateCodec.ChangedFields(before,after))
            {
                if(key=="cavesExplicitlySet") continue;
                if(key=="structures")
                {
                    var a=before.structures.ToDictionary(p=>p.id);var b=after.structures.ToDictionary(p=>p.id);
                    foreach(var id in a.Keys.Union(b.Keys))
                    {
                        if(a.ContainsKey(id) && b.ContainsKey(id) && SimpleJson.Serialize(a[id])==SimpleJson.Serialize(b[id]))continue;
                        string verb=!a.ContainsKey(id)?(korean?"추가":"added"):!b.ContainsKey(id)?(korean?"삭제":"removed"):(korean?"변경":"changed");
                        var kind=b.ContainsKey(id)?b[id].kind:a[id].kind;
                        text.Append("\n• ").Append(kind=="ancient_danger"?(korean?"고대 위협 ":"Ancient danger "):(korean?"폐허 ":"Ruin ")).Append(id).Append(": ").Append(verb);
                        if(b.TryGetValue(id,out var p))text.Append(" · ").Append(p.width).Append('×').Append(p.height).Append(" · ").Append(p.count).Append(korean?"개":" instances")
                            .Append(p.region==null?"":" · "+p.region).Append(p.position==null?"":" · ["+string.Join(",",p.position.Select(n=>n.ToString("0.###",CultureInfo.InvariantCulture)))+"]");
                        if(p!=null)
                        {
                            if(p.rotation!=0)text.Append(" · ").Append(p.rotation).Append('°');
                            if(p.spacing>1)text.Append(korean?" · 최소 간격 ":" · minimum gap ").Append(p.spacing);
                            if(p.relation!=null)text.Append(" · ").Append(p.relation.target).Append(" ").Append(p.relation.min_distance).Append('~').Append(p.relation.max_distance).Append(korean?"칸 ":" cells ").Append(p.relation.side);
                        }
                    }
                    text.Append(korean?"\n실제 배치 여부는 미리보기 생성 결과에서 확인합니다.":"\nPlacement is checked during preview generation.");
                    continue;
                }
                if(key=="elevationShapes")
                {
                    var a=ShapeEdits.Describe(before.elevationShapes).ToDictionary(s=>(string)s["id"]);var b=ShapeEdits.Describe(after.elevationShapes).ToDictionary(s=>(string)s["id"]);
                    foreach(string id in a.Keys.Union(b.Keys))
                    {
                        string oldShape=a.ContainsKey(id)?SimpleJson.Serialize(a[id]):null,newShape=b.ContainsKey(id)?SimpleJson.Serialize(b[id]):null;
                        if(oldShape==newShape)continue;
                        text.Append("\n• ").Append(id).Append(": ")
                            .Append(oldShape==null?(korean?"추가":"added"):newShape==null?(korean?"제거":"removed"):(korean?"변경":"changed"));
                        if(oldShape!=null && newShape!=null)
                            foreach(string property in a[id].Keys.Union(b[id].Keys))
                            {
                                a[id].TryGetValue(property,out var av);b[id].TryGetValue(property,out var bv);
                                if(SimpleJson.Serialize(av)==SimpleJson.Serialize(bv))continue;
                                text.Append(" · ").Append(property);
                                if(property!="shapes" && property!="compose")text.Append(' ').Append(Value(av,korean)).Append(" → ").Append(Value(bv,korean));
                            }
                    }
                    continue;
                }
                if(key=="imageMap" && before.imageMap!=null && after.imageMap!=null && before.imageMap.width==after.imageMap.width && before.imageMap.height==after.imageMap.height)
                {
                    int cells=before.imageMap.cells.Zip(after.imageMap.cells,(a,b)=>a==b?0:1).Sum();
                    text.Append("\n• ").Append(korean?"이미지 지형: ":"Image terrain: ").Append(cells).Append(korean?"칸 변경":" cells changed");
                    if(before.imageMap.replaceElevation!=after.imageMap.replaceElevation)text.Append(korean?" · 이미지 높이 우선 ":" · image elevation priority ").Append(Value(before.imageMap.replaceElevation,korean)).Append(" → ").Append(Value(after.imageMap.replaceElevation,korean));
                    if(before.imageMap.note!=after.imageMap.note)text.Append(korean?" · 해석 설명 변경":" · interpretation notes changed");
                    continue;
                }
                var field=typeof(TileMapState).GetField(key);
                string label=korean && Korean.TryGetValue(key,out var name) ? name : key;
                text.Append("\n• ").Append(label).Append(": ").Append(Value(field.GetValue(before),korean)).Append(" → ").Append(Value(field.GetValue(after),korean));
            }
            return text.ToString();
        }
        private static string Value(object value, bool korean)
        {
            if(value is bool boolean) return korean ? (boolean ? "켬" : "끔") : (boolean ? "on" : "off");
            if(value is MapGenAI.ImageInput.ImageMapData image) return image.width + "×" + image.height;
            if(value is List<ElevationShape> shapes) return shapes.Count + (korean ? "개" : " shapes");
            if(value is List<string> list) return list.Count==0 ? "—" : string.Join(", ",list);
            if(value is float number) return number.ToString("0.###",CultureInfo.InvariantCulture);
            return value?.ToString() ?? "—";
        }
    }
}
