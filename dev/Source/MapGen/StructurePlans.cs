using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.UI;
using Verse;

namespace MapGenAI.MapGen
{
    public sealed class StructurePlan : IExposable
    {
        public string id, kind = "ruin", region, region_part;
        public float[] position, bounds;
        public int width = 11, height = 9, count = 1, seed;
        public int rotation, spacing=1;
        public SpatialRelation relation;
        public StructurePlan Clone() => new StructurePlan { id=id, kind=kind, region=region, region_part=region_part,
            position=(float[])position?.Clone(), bounds=(float[])bounds?.Clone(), width=width, height=height, count=count, seed=seed,
            rotation=rotation,spacing=spacing,relation=relation?.Clone() };
        public void ExposeData()
        {
            string json = SimpleJson.Serialize(this);
            Scribe_Values.Look(ref json, "plan");
            if (Scribe.mode == LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(json))
            {
                var p = SimpleJson.ConvertTo<StructurePlan>(SimpleJson.Parse(json));
                id=p.id; kind=p.kind; region=p.region; region_part=p.region_part; position=p.position; bounds=p.bounds;
                width=p.width; height=p.height; count=p.count; seed=p.seed;
                rotation=p.rotation;spacing=p.spacing;relation=p.relation;
            }
        }
    }
    public sealed class StructureEdit { public string op, id; public SimpleJsonObject values; }
    public static class StructurePlans
    {
        static readonly HashSet<string> Fields = new HashSet<string> { "id","kind","region","region_part","position","bounds","width","height","count","seed","rotation","spacing","relation" };
        public static List<StructureEdit> Parse(SimpleJsonObject root)
        {
            var items = root.GetObjectArray("structure_ops");
            if (items == null || items.Count > 16) throw new FormatException("structure_ops requires at most 16 operations");
            var result = new List<StructureEdit>();
            foreach (var item in items)
            {
                var e = new StructureEdit {op=item.GetString("op"),id=item.GetString("id")};
                string dataKey = e.op == "add" ? "structure" : "changes";
                if (e.op != "add" && e.op != "update" && e.op != "remove") throw new FormatException("Unknown structure operation: " + e.op);
                foreach (var key in item.Keys)
                    if (key != "op" && key != "id" && (e.op == "remove" || key != dataKey)) throw new FormatException("Unknown structure operation field: " + key);
                if (e.op != "remove")
                {
                    e.values = item.GetObject(dataKey) ?? throw new FormatException("Missing " + dataKey);
                    CheckFields(e.values);
                    if (e.op == "add") ValidateOne(SimpleJson.ConvertTo<StructurePlan>(e.values));
                    else if (e.values.ContainsKey("id") || e.values.ContainsKey("kind")) throw new FormatException("Structure update cannot change ID/kind");
                }
                if (e.op != "add") Id(e.id);
                result.Add(e);
            }
            return result;
        }
        static void CheckFields(SimpleJsonObject obj)
        {
            foreach (var key in obj.Keys)
            {
                if (!Fields.Contains(key)) throw new FormatException("Unsupported structure field: " + key);
                if(key=="relation")
                {
                    if(obj.IsNull(key))continue;
                    var value=obj.GetObject(key)??throw new FormatException("relation must be an object or null");
                    foreach(var field in value.Keys)
                    {
                        if(field!="target" && field!="side" && field!="min_distance" && field!="max_distance")throw new FormatException("Unsupported relation field: "+field);
                        if(value.GetString(field)==null)throw new FormatException("Invalid relation "+field);
                        if(field=="min_distance" || field=="max_distance")
                            if(!int.TryParse(value.GetString(field),out _))throw new FormatException("Relation distance must be integer cells");
                    }
                    continue;
                }
                if ((key == "position" || key == "bounds") && !obj.IsNull(key))
                { if (obj.GetFloatArray(key) == null) throw new FormatException("Invalid structure " + key); }
                else if (key != "region" && key != "region_part" && key != "position" && key != "bounds" && obj.GetString(key) == null)
                    throw new FormatException("Invalid structure " + key);
                if (key == "width" || key == "height" || key == "count" || key == "seed" || key=="rotation" || key=="spacing")
                    if (!int.TryParse(obj.GetString(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                        throw new FormatException("Structure " + key + " must be an integer");
            }
        }
        public static void Apply(TileMapState state, List<StructureEdit> edits)
        {
            if (edits == null) return;
            foreach (var e in edits)
            {
                if (e.op == "add") state.structures.Add(SimpleJson.ConvertTo<StructurePlan>(e.values));
                else
                {
                    int index = state.structures.FindIndex(p => p.id == e.id);
                    if (index < 0) throw new FormatException("Structure ID does not exist: " + e.id);
                    if (e.op == "remove") state.structures.RemoveAt(index);
                    else
                    {
                        var obj = SimpleJson.Parse(SimpleJson.Serialize(state.structures[index]));
                        foreach (var key in e.values.Keys) obj.Values[key] = e.values.Values[key];
                        state.structures[index] = SimpleJson.ConvertTo<StructurePlan>(obj);
                    }
                }
            }
        }
        static void Id(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
                throw new FormatException("Invalid structure/region ID");
        }
        static void ValidateOne(StructurePlan p)
        {
            if (p == null) throw new FormatException("Null structure plan");
            Id(p.id);
            if (p.kind != "ruin" && p.kind!="ancient_danger") throw new FormatException("현재 위치 지정 지원 구조물: ruin / ancient_danger. 임의 모드 건물은 별도 생성기가 필요합니다. / Supported kinds: ruin and ancient_danger; other structures require an adapter.");
            if (p.region != null) Id(p.region);
            if(p.region_part!=null && p.region_part!="inside" && p.region_part!="enclosed")throw new FormatException("Structure region_part must be inside or enclosed");
            if(p.region_part!=null && p.region==null)throw new FormatException("Structure region_part requires a region ID; clear both when unbinding");
            if (p.position != null) ShapeEdits.ValidatePair(p.position);
            if (p.bounds != null)
            {
                if (p.bounds.Length != 4 || p.bounds[0] >= p.bounds[2] || p.bounds[1] >= p.bounds[3]) throw new FormatException("bounds must be [west,south,east,north]");
                foreach (var v in p.bounds) ShapeValidation.Range(v,0,1,"structure bounds");
            }
            if(p.relation!=null)
            {
                var r=p.relation;
                if(r.target!="river" && r.target!="water" && r.target!="mountain" && r.target!="region_edge")throw new FormatException("Relation target must be river/water/mountain/region_edge");
                if(r.side!="any" && r.side!="north" && r.side!="south" && r.side!="east" && r.side!="west")throw new FormatException("Relation side must be any/north/south/east/west");
                ShapeValidation.Range(r.min_distance,0,64,"minimum distance");ShapeValidation.Range(r.max_distance,r.min_distance,64,"maximum distance");
                if(r.target=="region_edge" && p.region==null)throw new FormatException("region_edge requires a region ID");
            }
            if (p.position == null && p.region == null && p.bounds == null && p.relation==null) throw new FormatException("Specify structure position, region, bounds or relation");
            if(p.rotation!=0 && p.rotation!=90 && p.rotation!=180 && p.rotation!=270)throw new FormatException("Structure rotation must be 0/90/180/270 degrees");
            ShapeValidation.Range(p.spacing,1,60,"structure spacing");
            ShapeValidation.Range(p.width,5,31,"structure width"); ShapeValidation.Range(p.height,5,31,"structure height");
            ShapeValidation.Range(p.count,1,8,"structure count");
            if(p.kind=="ancient_danger")
            {
                ShapeValidation.Range(p.width,15,20,"ancient danger width");ShapeValidation.Range(p.height,15,20,"ancient danger height");
                ShapeValidation.Range(p.count,1,2,"ancient danger count per plan");
                if(p.rotation!=0)throw new FormatException("고대 위협은 내부 배치를 게임 생성기가 결정하며 회전 지정은 아직 지원하지 않습니다. / Native ancient danger rotation is not supported.");
            }
        }
        public static void Validate(TileMapState state)
        {
            if (state.structures == null || state.structures.Count > 16) throw new FormatException("Maximum 16 structure plans");
            var ids = new HashSet<string>(); int total = 0;
            foreach (var p in state.structures)
            {
                ValidateOne(p); total += p.count;
                if (!ids.Add(p.id)) throw new FormatException("Duplicate structure ID: " + p.id);
                if (p.region != null)
                {
                    var shape = state.elevationShapes.Find(s => s.id == p.region);
                    if (shape == null) throw new FormatException("유적이 참조하는 영역이 없습니다. 함께 제거하거나 다시 지정하세요. / Missing structure region; remove or rebind the structure too: " + p.region);
                    if (shape.type != "composite" && shape.type != "bump" && shape.type != "ring" && shape.type != "region_fill") throw new FormatException("Structure region requires composite, bump, ring or region_fill geometry");
                    if(p.region_part=="enclosed" && shape.type=="region_fill")throw new FormatException("For an enclosed interior, reference the original ring, not its partial material fill");
                }
            }
            if (total > 24) throw new FormatException("Maximum 24 positioned structures");
            if(state.structures.Where(p=>p.kind=="ancient_danger").Sum(p=>p.count)>4)throw new FormatException("Maximum 4 positioned ancient dangers");
        }
    }
}
