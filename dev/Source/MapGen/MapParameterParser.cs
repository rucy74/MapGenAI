using System;
using System.Collections.Generic;
using MapGenAI.UI;
using UnityEngine;

namespace MapGenAI.MapGen
{
    public static class MapParameterParser
    {
        public static MapParamsData Parse(SimpleJsonObject obj)
        {
            if (obj == null) throw new FormatException("Expected params object");
            ValidateTypes(obj);
            ValidateChoice(obj,"hills","left,right,center,edges,top,bottom,none");
            ValidateChoice(obj,"coast_direction","auto,north,east,south,west");
            ValidateChoice(obj,"hill_size","small,medium,large",true);
            ValidateChoice(obj,"hill_smoothness","rough,normal,smooth",true);
            ValidateChoice(obj,"river_direction","horizontal,vertical,left,right,up,down",true);
            ValidateChoice(obj,"river_position","left,right,up,down,top,bottom,center",true);
            if(obj.GetObject("river")!=null)ValidateChoice(obj.GetObject("river"),"direction","horizontal,vertical,left,right,up,down",true);
            var data = new MapParamsData();

            // --- explicitKeys 추적: JSON에 키가 존재하면 기록 ---
            void Track(string key) { data.explicitKeys.Add(key); }

            if (obj.GetString("hills") != null)            { data.hills = obj.GetString("hills"); Track("hills"); }
            if (obj.GetString("hill_amount") != null)      { data.hill_amount = obj.GetFloat("hill_amount", 1f); Track("hill_amount"); }
            if (obj.GetString("vegetation_density") != null){ data.vegetation_density = obj.GetFloat("vegetation_density", 1f); Track("vegetation_density"); }
            if (obj.GetString("animal_density") != null)   { data.animal_density = obj.GetFloat("animal_density", 1f); Track("animal_density"); }
            if (obj.GetString("fertility_offset") != null) { data.fertility_offset = obj.GetFloat("fertility_offset", 0f); Track("fertility_offset"); }
            if (obj.GetString("roads") != null)            { data.roads = obj.GetBool("roads"); Track("roads"); }
            if (obj.GetString("caves") != null)            { data.caves = obj.GetBool("caves"); data.caves_explicit = true; Track("caves"); }
            if (obj.GetString("geysers") != null)          { data.geysers = obj.GetInt("geysers", -1); Track("geysers"); }
            if (obj.GetString("coast_direction") != null)  { data.coast_direction = obj.GetString("coast_direction"); Track("coast_direction"); }
            if (obj.GetString("rock_count") != null)       { data.rock_count = obj.GetInt("rock_count", -1); Track("rock_count"); }
            if (obj.GetString("ore_density") != null)      { data.ore_density = obj.GetFloat("ore_density", 1f); Track("ore_density"); }
            if (obj.GetString("ruin_density") != null)     { data.ruin_density = obj.GetFloat("ruin_density", 1f); Track("ruin_density"); }
            if (obj.GetString("danger_density") != null)   { data.danger_density = obj.GetFloat("danger_density", 1f); Track("danger_density"); }
            if (obj.GetString("rock_chunks") != null)      { data.rock_chunks = obj.GetBool("rock_chunks"); Track("rock_chunks"); }
            if (obj.GetString("hill_size") != null)        { data.hill_size = ParseHillSize(obj.GetString("hill_size")); Track("hill_size"); }
            if (obj.GetString("hill_smoothness") != null)  { data.hill_smoothness = ParseHillSmoothness(obj.GetString("hill_smoothness")); Track("hill_smoothness"); }
            if (obj.GetString("straight_river") != null)   { data.straight_river = obj.GetBool("straight_river"); Track("straight_river"); }

            // rock_types 배열 파싱
            var rockTypesArr = obj.GetArray("rock_types");
            if (rockTypesArr != null)
            {
                data.rock_types = new System.Collections.Generic.List<string>();
                foreach (var item in rockTypesArr)
                    data.rock_types.Add(item);
                Track("rock_types");
            }

            // river 객체 파싱 — 세부 키별로 추적 (MDP: 방향만 보내도 위치 유지, 위치만 보내도 방향 유지)
            var riverObj = obj.GetObject("river");
            if (riverObj != null)
            {
                data.river = new RiverData();
                if (riverObj.GetString("present") != null) { data.river.present = riverObj.GetBool("present"); Track("river_present"); }
                if (riverObj.GetString("direction") != null) { data.river.direction = riverObj.GetString("direction"); Track("river_direction"); }
                if (riverObj.GetString("direction_angle") != null) { data.river.direction_angle = riverObj.GetFloat("direction_angle", -1f); Track("river_direction"); }
                if (riverObj.GetString("x_position") != null) { data.river.x_position = riverObj.GetFloat("x_position", 0.5f); Track("river_x"); }
                if (riverObj.GetString("z_position") != null) { data.river.z_position = riverObj.GetFloat("z_position", 0.5f); Track("river_z"); }
            }

            // river_direction / river_position 단축키 지원 (river 객체 없이 직접 지정 가능)
            {
                string rdStr = obj.GetString("river_direction");
                string rpStr = obj.GetString("river_position");
                if (rdStr != null)
                {
                    if (data.river == null) data.river = new RiverData();
                    data.river.present = true;
                    data.river.direction = rdStr;
                    Track("river_direction");
                    Track("river_present");
                }
                if (rpStr != null)
                {
                    if (data.river == null) data.river = new RiverData();
                    data.river.present = true;
                    string rp = rpStr.Trim().ToLower();
                    if (rp == "up" || rp == "top")
                        data.river.z_position = 0.8f;
                    else if (rp == "down" || rp == "bottom")
                        data.river.z_position = 0.2f;
                    else
                        data.river.x_position = ParseRiverPosition(rpStr);
                    Track(rp == "up" || rp == "top" || rp == "down" || rp == "bottom" ? "river_z" : "river_x");
                    Track("river_present");
                }
            }

            // mutators 배열 파싱
            var mutatorsArr = obj.GetArray("mutators");
            if (mutatorsArr != null)
            {
                data.mutators = new System.Collections.Generic.List<string>();
                foreach (var item in mutatorsArr)
                    data.mutators.Add(item);
                Track("mutators");
            }

            // remove_mutators 배열 파싱 (기존 특징 제거용)
            var removeArr = obj.GetArray("remove_mutators");
            if (removeArr != null)
            {
                data.remove_mutators = new System.Collections.Generic.List<string>();
                foreach (var item in removeArr)
                    data.remove_mutators.Add(item);
                Track("remove_mutators");
            }

            var removedCategories = obj.GetArray("remove_categories");
            if (removedCategories != null) { data.remove_categories = new List<string>(removedCategories); Track("remove_categories"); }
            var restoredCategories = obj.GetArray("restore_categories");
            if (restoredCategories != null) { data.restore_categories = new List<string>(restoredCategories); Track("restore_categories"); }

            // elevation_shapes 오브젝트 배열 파싱
            var shapesArr = obj.GetObjectArray("elevation_shapes");
            if (shapesArr != null)
            {
                data.elevation_shapes = new List<ElevationShape>();
                foreach (var shape in shapesArr) data.elevation_shapes.Add(ShapeEdits.ParseShape(shape));
                Track("elevation_shapes");
            }

            if (obj.ContainsKey("shape_ops")) { data.shape_ops = ShapeEdits.Parse(obj); Track("shape_ops"); }
            if (obj.ContainsKey("structure_ops")) { data.structure_ops = StructurePlans.Parse(obj); Track("structure_ops"); }
            if (obj.ContainsKey("road_ops")) { data.road_ops = RoadPlans.Parse(obj); Track("road_ops"); }
            if (obj.ContainsKey("replace_shapes")) { data.replace_shapes = obj.GetBool("replace_shapes"); Track("replace_shapes"); }
            if (data.shape_ops != null && data.elevation_shapes != null) throw new FormatException("Use shape_ops or elevation_shapes, not both");

            // 병합은 MapGenParams.Apply()에서 WorldComponent 기반으로 수행 (MDP)
            // ParseParams는 LLM이 보낸 것만 data에 넣고 explicitKeys로 추적

            return data;
        }

        /// <summary>composite shapes[] 파싱</summary>
        public static List<ShapePrimitive> ParseCompositeShapes(SimpleJsonObject shapeObj)
        {
            var arr = shapeObj.GetObjectArray("shapes");
            if (arr == null) return null;

            var result = new List<ShapePrimitive>();
            foreach (var s in arr)
            {
                var fields=new HashSet<string>{"id","prim","r","r2","n","w","h","size","rot","center","verts"};
                foreach(var key in s.Keys)if(!fields.Contains(key))throw new FormatException("Unsupported primitive field: "+key);
                if(s.ContainsKey("center") && !s.IsNull("center") && s.GetFloatArray("center")==null)throw new FormatException("Invalid primitive center");
                if(s.ContainsKey("verts") && !s.IsNull("verts") && s.GetNestedFloatArray("verts")==null)throw new FormatException("Invalid primitive vertices");
                var sp = new ShapePrimitive
                {
                    id = s.GetString("id"),
                    prim = s.GetString("prim"),
                    r = s.GetFloat("r", 0f),
                    r2 = s.GetFloat("r2", 0f),
                    n = s.GetInt("n", 0),
                    w = s.GetFloat("w", 0f),
                    h = s.GetFloat("h", 0f),
                    size = s.GetFloat("size", 0f),
                    rot = s.GetFloat("rot", 0f)
                };

                // center: [x, z]
                var centerArr = s.GetFloatArray("center");
                if (centerArr != null)
                    sp.center = centerArr;

                // verts: [[x,z], [x,z], ...] — 중첩 배열
                var vertsData = s.GetNestedFloatArray("verts");
                if (vertsData != null)
                    sp.verts = vertsData;

                result.Add(sp);
            }
            return result;
        }

        /// <summary>composite compose[] 파싱. LLM이 다양한 필드명을 쓸 수 있으므로 방어적으로 처리.</summary>
        public static List<ComposeOp> ParseCompositeOps(SimpleJsonObject shapeObj)
        {
            var arr = shapeObj.GetObjectArray("compose");
            if (arr == null) return null;

            var result = new List<ComposeOp>();
            foreach (var c in arr)
            {
                var fields=new HashSet<string>{"op","s","s1","s2","a","b","from","out","outId","id","k","e","f","fill"};
                foreach(var key in c.Keys)if(!fields.Contains(key))throw new FormatException("Unsupported composite operation field: "+key);
                var op = new ComposeOp
                {
                    op = c.GetString("op"),
                    s = c.GetString("s") ?? c.GetString("s1"),        // LLM이 s1으로 보낼 수 있음
                    a = c.GetString("a") ?? c.GetString("s1"),        // sub에서 a 대신 s1
                    b = c.GetString("b") ?? c.GetString("s2"),        // union에서 b 대신 s2
                    from = c.GetString("from") ?? c.GetString("s1"),  // sub에서 from 대신 s1
                    outId = c.GetString("out") ?? c.GetString("outId") ?? c.GetString("id"),  // out 대신 id
                    k = c.GetFloat("k", 0f),
                    e = c.GetFloat("e", 0f),
                    f = c.GetFloat("f", 0.05f),
                    fill = c.GetString("fill")
                };

                // sub 연산: LLM이 {op:"sub", s:"c1", s2:"c2"} 형태로 보내면
                // a=c2(빼는 도형), from=c1(빼기 대상)으로 매핑
                if (op.op == "sub" && c.GetString("s2") != null)
                {
                    op.from = c.GetString("s") ?? c.GetString("s1");  // 큰 도형 (빼기 대상)
                    op.a = c.GetString("s2");                          // 빼는 도형
                }

                result.Add(op);
            }
            return result;
        }

        /// <summary>hill_size 시맨틱 파싱. small/medium/large 또는 숫자.</summary>
        private static float ParseHillSize(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0f; // 0 = 기본값 사용
            val = val.Trim().ToLower();
            switch (val)
            {
                case "small":  return 0.035f; // 큰 산맥 (frequency 높음 = 작은 패턴이지만, Map Designer에서는 반대 해석)
                case "medium": return 0.021f; // 바닐라 기본
                case "large":  return 0.012f; // 거대한 산맥 (낮은 frequency = 큰 패턴)
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
            }
        }

        /// <summary>hill_smoothness 시맨틱 파싱. rough/normal/smooth 또는 숫자.</summary>
        private static float ParseHillSmoothness(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0f; // 0 = 기본값 사용
            val = val.Trim().ToLower();
            switch (val)
            {
                case "rough":  return 1.0f; // 매우 거친
                case "normal": return 2.0f; // 바닐라 기본
                case "smooth": return 3.5f; // 매끄러운
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0f;
            }
        }

        /// <summary>river_position 시맨틱 파싱. left/center/right 또는 0.0-1.0 숫자.</summary>
        private static float ParseRiverPosition(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0.5f;
            val = val.Trim().ToLower();
            switch (val)
            {
                case "left":   return 0.2f;
                case "center": return 0.5f;
                case "right":  return 0.8f;
                default:
                    return float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)
                        ? Mathf.Clamp(f, 0f, 1f) : 0.5f;
            }
        }


        private static void ValidateTypes(SimpleJsonObject obj)
        {
            var arrays = new HashSet<string> { "rock_types", "mutators", "remove_mutators", "remove_categories", "restore_categories" };
            var scalars = new HashSet<string> { "hills", "hill_amount", "vegetation_density", "animal_density", "fertility_offset", "roads", "caves", "geysers", "coast_direction", "rock_count", "ore_density", "ruin_density", "danger_density", "rock_chunks", "hill_size", "hill_smoothness", "straight_river", "river_direction", "river_position" };
            foreach (string key in obj.Keys)
            {
                bool invalid = scalars.Contains(key) && obj.GetString(key) == null
                    || arrays.Contains(key) && obj.GetArray(key) == null
                    || (key == "elevation_shapes" || key == "shape_ops" || key == "structure_ops" || key == "road_ops") && obj.GetObjectArray(key) == null
                    || key == "river" && obj.GetObject(key) == null;
                if (invalid) throw new FormatException("Invalid or null parameter: " + key);
            }
            var river = obj.GetObject("river");
            if (river != null)
                foreach (string key in river.Keys)
                    if (river.GetString(key) == null) throw new FormatException("Invalid river parameter: " + key);
        }
        private static void ValidateChoice(SimpleJsonObject obj,string key,string names,bool allowNumber=false)
        {
            if(!obj.ContainsKey(key))return;string value=obj.GetString(key);
            if(value!=null && Array.IndexOf(names.Split(','),value.ToLowerInvariant())>=0)return;
            if(allowNumber && float.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number) && !float.IsNaN(number) && !float.IsInfinity(number))return;
            throw new FormatException("Unsupported value for "+key+": "+value);
        }
    }
}
