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

        public static bool Save(string name, TileMapState data)
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
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[MapGenAI] 프리셋 저장 실패: {e.Message}");
                return false;
            }
        }

        public static TileMapState Load(string name)
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

        private static string SerializeToJson(TileMapState state) => MapStateCodec.Serialize(state);
        private static TileMapState DeserializeFromJson(string json) => MapStateCodec.Deserialize(json);

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
