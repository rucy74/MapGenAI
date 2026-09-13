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
        private readonly Dictionary<string, List<List<string>>> _nestedArrays = new Dictionary<string, List<List<string>>>();

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

        /// <summary>float 배열 반환 (예: "center": [0.5, 0.3])</summary>
        public float[] GetFloatArray(string key)
        {
            if (_arrays.TryGetValue(key, out var arr))
            {
                var result = new float[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    if (!float.TryParse(arr[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                        return null;
                }
                return result;
            }
            return null;
        }

        /// <summary>중첩 배열 반환 (예: "verts": [[0.3,0.7],[0.5,0.8]]). 각 내부 배열은 문자열 리스트.</summary>
        public List<List<string>> GetNestedArray(string key) =>
            _nestedArrays.TryGetValue(key, out var v) ? v : null;

        /// <summary>중첩 배열을 float[][] 로 변환 (예: verts)</summary>
        public float[][] GetNestedFloatArray(string key)
        {
            var nested = GetNestedArray(key);
            if (nested == null) return null;
            var result = new float[nested.Count][];
            for (int i = 0; i < nested.Count; i++)
            {
                result[i] = new float[nested[i].Count];
                for (int j = 0; j < nested[i].Count; j++)
                {
                    if (!float.TryParse(nested[i][j], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i][j]))
                        return null;
                }
            }
            return result;
        }

        public void SetString(string key, string value) => _strings[key] = value;
        public void SetObject(string key, SimpleJsonObject obj) => _objects[key] = obj;
        public void SetArray(string key, List<string> arr) => _arrays[key] = arr;
        public void SetObjectArray(string key, List<SimpleJsonObject> arr) => _objectArrays[key] = arr;
        public void SetNestedArray(string key, List<List<string>> arr) => _nestedArrays[key] = arr;
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
                if (pos >= json.Length) break;
                if (json[pos] == '}') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                // 키
                var key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length) break;

                // 값
                if (json[pos] == '{')
                {
                    obj.SetObject(key, ParseObject(json, ref pos));
                }
                else if (json[pos] == '[')
                {
                    // 배열 내부의 첫 비-공백 문자로 타입 결정
                    int peekPos = pos + 1;
                    SkipWhitespace(json, ref peekPos);
                    if (peekPos < json.Length && json[peekPos] == '{')
                    {
                        obj.SetObjectArray(key, ParseObjectArray(json, ref pos));
                    }
                    else if (peekPos < json.Length && json[peekPos] == '[')
                    {
                        // 중첩 배열: [[...], [...]]
                        obj.SetNestedArray(key, ParseNestedArray(json, ref pos));
                    }
                    else
                    {
                        obj.SetArray(key, ParseArray(json, ref pos));
                    }
                }
                else if (json[pos] == '"')
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
            if (pos >= json.Length || json[pos] != '"') return "";
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
                if (pos >= json.Length) break;
                if (json[pos] == ']') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                if (json[pos] == '{')
                {
                    list.Add(ParseObject(json, ref pos));
                }
                else
                {
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
                if (pos >= json.Length) break;
                if (json[pos] == ']') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                if (json[pos] == '"')
                    list.Add(ParseString(json, ref pos));
                else if (json[pos] == '{')
                {
                    ParseObject(json, ref pos); // skip nested objects
                }
                else if (json[pos] == '[')
                {
                    // 중첩 배열 안의 배열: 스킵
                    SkipValue(json, ref pos);
                }
                else
                {
                    list.Add(ParsePrimitive(json, ref pos));
                }
            }
            return list;
        }

        /// <summary>중첩 배열 파싱: [[1,2], [3,4]]</summary>
        private static List<List<string>> ParseNestedArray(string json, ref int pos)
        {
            var result = new List<List<string>>();
            if (pos >= json.Length || json[pos] != '[') return result;
            pos++; // skip [

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length) break;
                if (json[pos] == ']') { pos++; break; }
                if (json[pos] == ',') { pos++; continue; }

                if (json[pos] == '[')
                {
                    result.Add(ParseArray(json, ref pos));
                }
                else
                {
                    ParsePrimitive(json, ref pos);
                }
            }
            return result;
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

        /// <summary>값을 타입에 관계없이 스킵 (배열/오브젝트/문자열/숫자)</summary>
        private static void SkipValue(string json, ref int pos)
        {
            if (pos >= json.Length) return;
            if (json[pos] == '{') { ParseObject(json, ref pos); }
            else if (json[pos] == '[')
            {
                int depth = 1;
                pos++;
                while (pos < json.Length && depth > 0)
                {
                    if (json[pos] == '[') depth++;
                    else if (json[pos] == ']') depth--;
                    else if (json[pos] == '"') { ParseString(json, ref pos); continue; }
                    pos++;
                }
            }
            else if (json[pos] == '"') { ParseString(json, ref pos); }
            else { ParsePrimitive(json, ref pos); }
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t'))
                pos++;
        }
    }
}
