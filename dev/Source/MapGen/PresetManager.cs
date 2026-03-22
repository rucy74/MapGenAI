using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// 맵 파라미터 프리셋 저장/불러오기/삭제 관리.
    /// JSON 파일로 저장 (수동 직렬화 — net472에서 System.Text.Json 사용 불가).
    /// 저장 위치: RimWorld 설정 폴더/MapGenAI_Presets/
    /// </summary>
    public static class PresetManager
    {
        public static string PresetDir => Path.Combine(GenFilePaths.ConfigFolderPath, "MapGenAI_Presets");

        public static void Save(string name, MapParamsData data)
        {
            try
            {
                var dir = PresetDir;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, SanitizeFileName(name) + ".json");
                var json = SerializeToJson(data);
                File.WriteAllText(filePath, json, Encoding.UTF8);

                Log.Message($"[MapGenAI] 프리셋 저장 완료: {filePath}");
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 프리셋 저장 실패: {e.Message}");
            }
        }

        public static MapParamsData Load(string name)
        {
            try
            {
                var filePath = Path.Combine(PresetDir, SanitizeFileName(name) + ".json");
                if (!File.Exists(filePath))
                {
                    Log.Warning($"[MapGenAI] 프리셋 파일 없음: {filePath}");
                    return null;
                }

                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var data = DeserializeFromJson(json);

                Log.Message($"[MapGenAI] 프리셋 불러오기 완료: {name}");
                return data;
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 프리셋 불러오기 실패: {e.Message}");
                return null;
            }
        }

        public static List<string> ListPresets()
        {
            var list = new List<string>();
            try
            {
                var dir = PresetDir;
                if (!Directory.Exists(dir))
                    return list;

                var files = Directory.GetFiles(dir, "*.json");
                foreach (var f in files)
                    list.Add(Path.GetFileNameWithoutExtension(f));

                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 프리셋 목록 읽기 실패: {e.Message}");
            }
            return list;
        }

        public static void Delete(string name)
        {
            try
            {
                var filePath = Path.Combine(PresetDir, SanitizeFileName(name) + ".json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Log.Message($"[MapGenAI] 프리셋 삭제 완료: {name}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 프리셋 삭제 실패: {e.Message}");
            }
        }

        // --- 직렬화 (모든 필드 포함) ---

        private static string SerializeToJson(MapParamsData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"hills\": {Js(data.hills ?? "none")},");
            sb.AppendLine($"  \"hill_amount\": {F(data.hill_amount)},");
            sb.AppendLine($"  \"vegetation_density\": {F(data.vegetation_density)},");
            sb.AppendLine($"  \"animal_density\": {F(data.animal_density)},");
            sb.AppendLine($"  \"fertility_offset\": {F(data.fertility_offset)},");

            // river
            if (data.river != null)
            {
                sb.AppendLine("  \"river\": {");
                sb.AppendLine($"    \"present\": {B(data.river.present)},");
                sb.AppendLine($"    \"direction\": {Js(data.river.direction ?? "vertical")},");
                sb.AppendLine($"    \"direction_angle\": {F(data.river.direction_angle)},");
                sb.AppendLine($"    \"x_position\": {F(data.river.x_position)},");
                sb.AppendLine($"    \"z_position\": {F(data.river.z_position)}");
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"river\": null,");
            }

            sb.AppendLine($"  \"roads\": {B(data.roads)},");
            sb.AppendLine($"  \"caves\": {B(data.caves)},");
            sb.AppendLine($"  \"geysers\": {data.geysers},");
            sb.AppendLine($"  \"coast_direction\": {Js(data.coast_direction ?? "auto")},");
            sb.AppendLine($"  \"rock_count\": {data.rock_count},");
            sb.AppendLine($"  \"ore_density\": {F(data.ore_density)},");
            sb.AppendLine($"  \"ruin_density\": {F(data.ruin_density)},");
            sb.AppendLine($"  \"danger_density\": {F(data.danger_density)},");
            sb.AppendLine($"  \"rock_chunks\": {B(data.rock_chunks)},");
            sb.AppendLine($"  \"hill_size\": {F(data.hill_size)},");
            sb.AppendLine($"  \"hill_smoothness\": {F(data.hill_smoothness)},");
            sb.AppendLine($"  \"straight_river\": {B(data.straight_river)},");

            // rock_types
            sb.Append("  \"rock_types\": [");
            if (data.rock_types != null && data.rock_types.Count > 0)
                sb.Append(string.Join(", ", data.rock_types.Select(r => Js(r))));
            sb.AppendLine("],");

            // mutators
            sb.Append("  \"mutators\": [");
            if (data.mutators != null && data.mutators.Count > 0)
                sb.Append(string.Join(", ", data.mutators.Select(m => Js(m))));
            sb.AppendLine("],");

            // remove_mutators
            sb.Append("  \"remove_mutators\": [");
            if (data.remove_mutators != null && data.remove_mutators.Count > 0)
                sb.Append(string.Join(", ", data.remove_mutators.Select(m => Js(m))));
            sb.AppendLine("],");

            // elevation_shapes
            sb.AppendLine("  \"elevation_shapes\": [");
            if (data.elevation_shapes != null && data.elevation_shapes.Count > 0)
            {
                for (int i = 0; i < data.elevation_shapes.Count; i++)
                {
                    var s = data.elevation_shapes[i];
                    sb.Append("    {");
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(s.type))         parts.Add($"\"type\":{Js(s.type)}");
                    if (!string.IsNullOrEmpty(s.direction))    parts.Add($"\"direction\":{Js(s.direction)}");
                    if (!string.IsNullOrEmpty(s.strength))     parts.Add($"\"strength\":{Js(s.strength)}");
                    if (!string.IsNullOrEmpty(s.fade))         parts.Add($"\"fade\":{Js(s.fade)}");
                    if (!string.IsNullOrEmpty(s.noise_amount)) parts.Add($"\"noise_amount\":{Js(s.noise_amount)}");
                    if (!string.IsNullOrEmpty(s.position))     parts.Add($"\"position\":{Js(s.position)}");
                    if (!string.IsNullOrEmpty(s.size))         parts.Add($"\"size\":{Js(s.size)}");
                    if (!string.IsNullOrEmpty(s.gap))          parts.Add($"\"gap\":{Js(s.gap)}");
                    if (!string.IsNullOrEmpty(s.fill))         parts.Add($"\"fill\":{Js(s.fill)}");
                    sb.Append(string.Join(",", parts));
                    sb.Append(i < data.elevation_shapes.Count - 1 ? "},\n" : "}\n");
                }
            }
            sb.AppendLine("  ]");

            sb.Append("}");
            return sb.ToString();
        }

        private static MapParamsData DeserializeFromJson(string json)
        {
            var p = UI.SimpleJson.Parse(json);
            var data = new MapParamsData
            {
                hills = p.GetString("hills") ?? "none",
                hill_amount = p.GetFloat("hill_amount", 1f),
                vegetation_density = p.GetFloat("vegetation_density", 1f),
                animal_density = p.GetFloat("animal_density", 1f),
                fertility_offset = p.GetFloat("fertility_offset", 0f),
                roads = p.GetBool("roads"),
                caves = p.GetBool("caves"),
                geysers = p.GetInt("geysers", -1),
                coast_direction = p.GetString("coast_direction") ?? "auto",
                rock_count = p.GetInt("rock_count", -1),
                ore_density = p.GetFloat("ore_density", 1f),
                ruin_density = p.GetFloat("ruin_density", 1f),
                danger_density = p.GetFloat("danger_density", 1f),
                rock_chunks = p.GetString("rock_chunks") != null ? p.GetBool("rock_chunks") : true,
                hill_size = p.GetFloat("hill_size", 0f),
                hill_smoothness = p.GetFloat("hill_smoothness", 0f),
                straight_river = p.GetBool("straight_river")
            };

            // river
            var riverObj = p.GetObject("river");
            if (riverObj != null)
            {
                data.river = new RiverData
                {
                    present = riverObj.GetBool("present"),
                    direction = riverObj.GetString("direction") ?? "vertical",
                    direction_angle = riverObj.GetFloat("direction_angle", -1f),
                    x_position = riverObj.GetFloat("x_position", 0.5f),
                    z_position = riverObj.GetFloat("z_position", 0.5f)
                };
            }

            // rock_types
            var rockArr = p.GetArray("rock_types");
            if (rockArr != null)
            {
                data.rock_types = new List<string>();
                foreach (var item in rockArr)
                    if (!string.IsNullOrEmpty(item)) data.rock_types.Add(item);
            }

            // mutators
            var mutArr = p.GetArray("mutators");
            if (mutArr != null)
            {
                data.mutators = new List<string>();
                foreach (var item in mutArr)
                    if (!string.IsNullOrEmpty(item)) data.mutators.Add(item);
            }

            // remove_mutators
            var rmArr = p.GetArray("remove_mutators");
            if (rmArr != null)
            {
                data.remove_mutators = new List<string>();
                foreach (var item in rmArr)
                    if (!string.IsNullOrEmpty(item)) data.remove_mutators.Add(item);
            }

            // elevation_shapes
            var shapesArr = p.GetObjectArray("elevation_shapes");
            if (shapesArr != null)
            {
                data.elevation_shapes = new List<ElevationShape>();
                foreach (var s in shapesArr)
                {
                    data.elevation_shapes.Add(new ElevationShape
                    {
                        type = s.GetString("type"),
                        direction = s.GetString("direction"),
                        strength = s.GetString("strength"),
                        fade = s.GetString("fade"),
                        noise_amount = s.GetString("noise_amount"),
                        position = s.GetString("position"),
                        size = s.GetString("size"),
                        gap = s.GetString("gap"),
                        fill = s.GetString("fill")
                    });
                }
            }

            return data;
        }

        // --- 유틸 ---

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unnamed";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string Js(string value)
        {
            if (value == null) return "null";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string F(float value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        private static string B(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
