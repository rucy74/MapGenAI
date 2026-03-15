using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        /// <summary>
        /// 현재 MapParamsData를 JSON 파일로 저장.
        /// </summary>
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

        /// <summary>
        /// JSON 파일에서 MapParamsData를 불러옴.
        /// </summary>
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

        /// <summary>
        /// 저장된 프리셋 이름 목록 반환 (확장자 제외).
        /// </summary>
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

        /// <summary>
        /// 프리셋 삭제.
        /// </summary>
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

        // --- 직렬화 ---

        private static string SerializeToJson(MapParamsData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"hills\": {JsonString(data.hills ?? "none")},");
            sb.AppendLine($"  \"hill_amount\": {F(data.hill_amount)},");
            sb.AppendLine($"  \"vegetation_density\": {F(data.vegetation_density)},");
            sb.AppendLine($"  \"animal_density\": {F(data.animal_density)},");

            // river 객체
            if (data.river != null)
            {
                sb.AppendLine("  \"river\": {");
                sb.AppendLine($"    \"present\": {B(data.river.present)},");
                sb.AppendLine($"    \"direction\": {JsonString(data.river.direction ?? "vertical")},");
                sb.AppendLine($"    \"x_position\": {F(data.river.x_position)}");
                sb.AppendLine("  },");
            }
            else
            {
                sb.AppendLine("  \"river\": null,");
            }

            sb.AppendLine($"  \"roads\": {B(data.roads)},");
            sb.AppendLine($"  \"caves\": {B(data.caves)},");
            sb.AppendLine($"  \"geysers\": {data.geysers},");
            sb.AppendLine($"  \"coast_direction\": {JsonString(data.coast_direction ?? "auto")},");
            sb.AppendLine($"  \"rock_count\": {data.rock_count},");
            sb.AppendLine($"  \"ore_density\": {F(data.ore_density)},");

            // mutators 배열
            sb.Append("  \"mutators\": [");
            if (data.mutators != null && data.mutators.Count > 0)
            {
                for (int i = 0; i < data.mutators.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(JsonString(data.mutators[i]));
                }
            }
            sb.AppendLine("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static MapParamsData DeserializeFromJson(string json)
        {
            var parsed = UI.SimpleJson.Parse(json);
            var data = new MapParamsData
            {
                hills = parsed.GetString("hills") ?? "none",
                hill_amount = parsed.GetFloat("hill_amount", 1f),
                vegetation_density = parsed.GetFloat("vegetation_density", 1f),
                animal_density = parsed.GetFloat("animal_density", 1f),
                roads = parsed.GetBool("roads"),
                caves = parsed.GetBool("caves"),
                geysers = parsed.GetInt("geysers", -1),
                coast_direction = parsed.GetString("coast_direction") ?? "auto",
                rock_count = parsed.GetInt("rock_count", -1),
                ore_density = parsed.GetFloat("ore_density", 1f)
            };

            var riverObj = parsed.GetObject("river");
            if (riverObj != null)
            {
                data.river = new RiverData
                {
                    present = riverObj.GetBool("present"),
                    direction = riverObj.GetString("direction") ?? "vertical",
                    x_position = riverObj.GetFloat("x_position", 0.5f)
                };
            }

            var mutatorsArr = parsed.GetArray("mutators");
            if (mutatorsArr != null)
            {
                data.mutators = new List<string>();
                foreach (var item in mutatorsArr)
                {
                    if (!string.IsNullOrEmpty(item))
                        data.mutators.Add(item);
                }
            }

            return data;
        }

        // --- 유틸 ---

        /// <summary>
        /// 파일명에 사용 불가한 문자를 언더스코어로 치환.
        /// </summary>
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

        private static string JsonString(string value)
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
