using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.UI;
using Verse;

namespace MapGenAI.MapGen
{
    public sealed class RoadPlan : IExposable
    {
        public string id, kind = "DirtRoad", route = "avoid";
        public float[][] points;
        public int seed;
        public RoadPlan Clone() => new RoadPlan { id=id,kind=kind,route=route,seed=seed,points=points?.Select(p=>(float[])p?.Clone()).ToArray() };
        public void ExposeData()
        {
            string json=SimpleJson.Serialize(this);
            Scribe_Values.Look(ref json,"plan");
            if(Scribe.mode==LoadSaveMode.LoadingVars && !string.IsNullOrEmpty(json))
            {
                var value=SimpleJson.ConvertTo<RoadPlan>(SimpleJson.Parse(json));
                id=value.id;kind=value.kind;route=value.route;points=value.points;seed=value.seed;
            }
        }
    }
    public sealed class RoadEdit { public string op,id; public SimpleJsonObject values; }
    public static class RoadPlans
    {
        public const int MaxRoads=8;
        public static readonly string[] Kinds={"DirtPath","DirtRoad","StoneRoad","AncientAsphaltRoad","AncientAsphaltHighway"};
        static readonly HashSet<string> Fields=new HashSet<string>{"id","kind","route","points","seed"};
        static void Id(string id)
        {
            if(string.IsNullOrEmpty(id)||id.Length>64||id.Any(c=>!(char.IsLetterOrDigit(c)||c=='_'||c=='-')))
                throw new FormatException("Invalid road ID");
        }
        public static List<RoadEdit> Parse(SimpleJsonObject root)
        {
            var items=root.GetObjectArray("road_ops");
            if(items==null || items.Count>16)throw new FormatException("road_ops requires at most 16 operations");
            var result=new List<RoadEdit>();
            foreach(var item in items)
            {
                var edit=new RoadEdit{op=item.GetString("op"),id=item.GetString("id")};
                if(edit.op!="add" && edit.op!="update" && edit.op!="remove")throw new FormatException("Unknown road operation");
                if(edit.op=="add" && item.ContainsKey("id"))throw new FormatException("Put the new road ID inside road, not on the operation");
                string dataKey=edit.op=="add"?"road":"changes";
                foreach(var key in item.Keys)
                    if(key!="op" && key!="id" && (edit.op=="remove" || key!=dataKey))throw new FormatException("Unknown road operation field: "+key);
                if(edit.op!="add")
                {
                    if(!item.Values.TryGetValue("id",out var rawId) || !(rawId is string))throw new FormatException("Road operation ID must be a string");
                    Id(edit.id);
                }
                if(edit.op!="remove")
                {
                    edit.values=item.GetObject(dataKey)??throw new FormatException("Missing "+dataKey);
                    foreach(var key in edit.values.Keys)
                    {
                        if(!Fields.Contains(key))throw new FormatException("Unsupported road field: "+key);
                        if(edit.values.IsNull(key))throw new FormatException("Road fields cannot be null");
                        if(key!="points" && edit.values.GetString(key)==null)throw new FormatException("Invalid road field: "+key);
                        if(key=="seed" && !int.TryParse(edit.values.GetString(key),out _))throw new FormatException("Road seed must be an integer");
                        if(key=="points")
                        {
                            var points=edit.values.GetNestedFloatArray(key)??throw new FormatException("Road points must be numeric coordinate pairs");
                            foreach(var point in points)ShapeEdits.ValidatePair(point);
                        }
                    }
                    if(edit.op=="update" && edit.values.ContainsKey("id"))throw new FormatException("Road update cannot change ID");
                    if(edit.op=="add")ValidateOne(SimpleJson.ConvertTo<RoadPlan>(edit.values));
                }
                result.Add(edit);
            }
            return result;
        }
        public static void Apply(TileMapState state,List<RoadEdit> edits)
        {
            if(edits==null)return;
            foreach(var edit in edits)
            {
                if(edit.op=="add")state.localRoads.Add(SimpleJson.ConvertTo<RoadPlan>(edit.values));
                else
                {
                    int index=state.localRoads.FindIndex(p=>p.id==edit.id);
                    if(index<0)throw new FormatException("Road ID does not exist: "+edit.id);
                    if(edit.op=="remove")state.localRoads.RemoveAt(index);
                    else
                    {
                        var value=SimpleJson.Parse(SimpleJson.Serialize(state.localRoads[index]));
                        foreach(var key in edit.values.Keys)value.Values[key]=edit.values.Values[key];
                        state.localRoads[index]=SimpleJson.ConvertTo<RoadPlan>(value);
                    }
                }
            }
        }
        public static void ValidateOne(RoadPlan road)
        {
            if(road==null)throw new FormatException("Null road plan");
            Id(road.id);
            if(!Kinds.Contains(road.kind))throw new FormatException("Supported road types: "+string.Join(", ",Kinds));
            if(road.route!="avoid" && road.route!="direct")throw new FormatException("Road route must be avoid or direct");
            if(road.points==null || road.points.Length<2 || road.points.Length>16)throw new FormatException("Road points requires 2..16 [x,z] waypoints");
            foreach(var point in road.points)ShapeEdits.ValidatePair(point);
            for(int i=1;i<road.points.Length;i++)
                if(road.points[i][0]==road.points[i-1][0] && road.points[i][1]==road.points[i-1][1])
                    throw new FormatException("Consecutive road waypoints must differ");
        }
        public static void Validate(TileMapState state)
        {
            if(state.localRoads==null || state.localRoads.Count>MaxRoads)throw new FormatException("At most eight local roads are supported");
            var ids=new HashSet<string>();
            foreach(var road in state.localRoads)
            {
                ValidateOne(road);
                if(!ids.Add(road.id))throw new FormatException("Duplicate road ID");
            }
        }
        public static string Label(string kind,bool ko)
        {
            int i=Array.IndexOf(Kinds,kind);
            var names=ko?new[]{"흙길","흙도로","돌길","고대 아스팔트 도로","고대 아스팔트 고속도로"}
                :new[]{"Dirt path","Dirt road","Stone road","Ancient asphalt road","Ancient asphalt highway"};
            return i<0?kind:names[i];
        }
        public static string NativePreviewNote(bool ko)=>ko?
            "이 타일의 기존 세계도로·도로 시설물은 미리보기에서 생략됩니다. 겹치는 구간은 실제 맵에서 기존 포장을 유지하고 장애물을 다시 검사합니다.":
            "Existing world roads and their roadside structures are omitted from this preview. Overlaps preserve the existing surface and check obstacles again in the full map.";
    }
}
