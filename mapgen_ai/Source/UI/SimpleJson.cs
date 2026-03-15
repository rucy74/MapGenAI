using System.Collections.Generic;
using System.Globalization;

namespace MapGenAI.UI
{
    /// <summary>
    /// 외부 라이브러리 없이 쓰는 최소 JSON 파서 (LLM 응답 파싱용)
    /// </summary>
    public class SimpleJsonObject
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private readonly Dictionary<string, SimpleJsonObject> _objects = new Dictionary<string, SimpleJsonObject>();
        private readonly Dictionary<string, List<string>> _arrays = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<SimpleJsonObject>> _objectArrays = new Dictionary<string, List<SimpleJsonObject>>();

        public string GetString(string key) =>
            _strings.TryGetValue(key, out var v) ? v : null;

        public float GetFloat(string key, float def = 0f) =>
            _strings.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;

        public int GetInt(string key, int def = 0) =>
            _strings.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : def;

        public bool GetBool(string key) =>
            _strings.TryGetValue(key, out var v) && v == "true";

        public SimpleJsonObject GetObject(string key) =>
            _objects.TryGetValue(key, out var v) ? v : null;

        public List<string> GetArray(string key) =>
            _arrays.TryGetValue(key, out var v) ? v : null;

        public List<SimpleJsonObject> GetObjectArray(string key) =>
            _objectArrays.TryGetValue(key, out var v) ? v : null;

        public void SetString(string key, string value) => _strings[key] = value;
        public void SetObject(string key, SimpleJsonObject obj) => _objects[key] = obj;
        public void SetArray(string key, List<string> arr) => _arrays[key] = arr;
        public void SetObjectArray(string key, List<SimpleJsonObject> arr) => _objectArrays[key] = arr;
    }

    public static class SimpleJson
    {
        public static SimpleJsonObject Parse(string json)
        {
            int pos = 0;
            SkipWhitespace(json, ref pos);
            return ParseObject(json, ref pos);
        }

        private static SimpleJsonObject ParseObject(string json, ref int pos)
        {
            var obj = new SimpleJsonObject();
            if (pos >= json.Length || json[pos] != '{') return obj;
            pos++; // skip {

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (json[pos] == '}') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                // 키
                var key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                SkipWhitespace(json, ref pos);

                // 값
                if (pos < json.Length && json[pos] == '{')
                {
                    obj.SetObject(key, ParseObject(json, ref pos));
                }
                else if (pos < json.Length && json[pos] == '[')
                {
                    // 배열 내부의 첫 비-공백 문자가 '{' 이면 오브젝트 배열로 파싱
                    int peekPos = pos + 1;
                    SkipWhitespace(json, ref peekPos);
                    if (peekPos < json.Length && json[peekPos] == '{')
                    {
                        obj.SetObjectArray(key, ParseObjectArray(json, ref pos));
                    }
                    else
                    {
                        obj.SetArray(key, ParseArray(json, ref pos));
                    }
                }
                else if (pos < json.Length && json[pos] == '"')
                {
                    obj.SetString(key, ParseString(json, ref pos));
                }
                else
                {
                    // number, bool, null
                    var val = ParsePrimitive(json, ref pos);
                    obj.SetString(key, val);
                }
            }
            return obj;
        }

        private static string ParseString(string json, ref int pos)
        {
            if (json[pos] != '"') return "";
            pos++; // skip "
            var sb = new System.Text.StringBuilder();
            while (pos < json.Length && json[pos] != '"')
            {
                if (json[pos] == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    switch (json[pos])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append(json[pos]); break;
                    }
                }
                else sb.Append(json[pos]);
                pos++;
            }
            if (pos < json.Length) pos++; // skip closing "
            return sb.ToString();
        }

        private static List<SimpleJsonObject> ParseObjectArray(string json, ref int pos)
        {
            var list = new List<SimpleJsonObject>();
            if (pos >= json.Length || json[pos] != '[') return list;
            pos++; // skip [

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (json[pos] == ']') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                if (json[pos] == '{')
                {
                    list.Add(ParseObject(json, ref pos));
                }
                else
                {
                    // 예상치 못한 값: 스킵
                    ParsePrimitive(json, ref pos);
                }
            }
            return list;
        }

        private static List<string> ParseArray(string json, ref int pos)
        {
            var list = new List<string>();
            if (pos >= json.Length || json[pos] != '[') return list;
            pos++; // skip [

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (json[pos] == ']') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                if (json[pos] == '"')
                    list.Add(ParseString(json, ref pos));
                else if (json[pos] == '{')
                {
                    ParseObject(json, ref pos); // skip nested objects
                }
                else
                {
                    list.Add(ParsePrimitive(json, ref pos));
                }
            }
            return list;
        }

        private static string ParsePrimitive(string json, ref int pos)
        {
            var sb = new System.Text.StringBuilder();
            while (pos < json.Length && json[pos] != ',' && json[pos] != '}' && json[pos] != ']')
            {
                sb.Append(json[pos++]);
            }
            return sb.ToString().Trim();
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t'))
                pos++;
        }
    }
}
