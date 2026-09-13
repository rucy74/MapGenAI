using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MapGenAI.UI
{
    public class SimpleJsonObject
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        public IEnumerable<string> Keys => Values.Keys;
        public bool ContainsKey(string key) => Values.ContainsKey(key);
        public bool IsNull(string key) => Values.TryGetValue(key, out var value) && value == null;
        private static string Scalar(object value) => value is string text ? text : value is JsonNumber n ? n.Text : value is bool b ? (b ? "true" : "false") : null;
        public string GetString(string key) => Values.TryGetValue(key, out var value) ? Scalar(value) : null;
        public float GetFloat(string key, float fallback = 0f)
        {
            if (!ContainsKey(key)) return fallback;
            if (float.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !float.IsNaN(value) && !float.IsInfinity(value)) return value;
            throw new FormatException("Expected finite number: " + key);
        }
        public int GetInt(string key, int fallback = 0)
        {
            if (!ContainsKey(key)) return fallback;
            if (decimal.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value == decimal.Truncate(value) && value >= int.MinValue && value <= int.MaxValue) return (int)value;
            throw new FormatException("Expected integer: " + key);
        }
        public bool GetBool(string key)
        {
            if (!ContainsKey(key)) return false;
            string value = GetString(key);
            if (value == "true") return true;
            if (value == "false") return false;
            throw new FormatException("Expected boolean: " + key);
        }
        public SimpleJsonObject GetObject(string key) => Values.TryGetValue(key, out var value) ? value as SimpleJsonObject : null;
        private List<object> Array(string key) => Values.TryGetValue(key, out var value) ? value as List<object> : null;
        public List<string> GetArray(string key)
        {
            var array = Array(key);
            if (array == null || array.Any(v => Scalar(v) == null)) return null;
            return array.Select(Scalar).ToList();
        }
        public List<SimpleJsonObject> GetObjectArray(string key)
        {
            var array = Array(key);
            if (array == null || array.Any(v => !(v is SimpleJsonObject))) return null;
            return array.Cast<SimpleJsonObject>().ToList();
        }
        public List<List<string>> GetNestedArray(string key)
        {
            var array = Array(key);
            if (array == null || array.Any(v => !(v is List<object> row) || row.Any(x => Scalar(x) == null))) return null;
            return array.Cast<List<object>>().Select(row => row.Select(Scalar).ToList()).ToList();
        }
        private static float[] Numbers(List<string> values)
        {
            if (values == null) return null;
            return values.Select(text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsNaN(v) && !float.IsInfinity(v)) return v;
                throw new FormatException("Expected finite coordinate");
            }).ToArray();
        }
        public float[] GetFloatArray(string key) => Numbers(GetArray(key));
        public float[][] GetNestedFloatArray(string key) => GetNestedArray(key)?.Select(Numbers).ToArray();
        public void SetString(string key, string value) => Values[key] = value;
        public void SetObject(string key, SimpleJsonObject value) => Values[key] = value;
        public void SetArray(string key, List<string> value) => Values[key] = value?.Cast<object>().ToList();
        public void SetObjectArray(string key, List<SimpleJsonObject> value) => Values[key] = value?.Cast<object>().ToList();
        public void SetNestedArray(string key, List<List<string>> value) => Values[key] = value?.Select(row => (object)row.Cast<object>().ToList()).ToList();
    }
    internal sealed class JsonNumber
    {
        public readonly string Text;
        public JsonNumber(string text) { Text = text; }
    }

    // Bounded JSON grammar shared by provider envelopes, parameters and presets.
    // Every loop consumes a complete value or throws. No partial-response repair.
    public static class SimpleJson
    {
        public const int MaxLength = 1048576;
        public const int MaxDepth = 48;
        public static SimpleJsonObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxLength) throw new FormatException("Empty or oversized JSON response");
            return new Reader(json).ReadRoot();
        }
        public static string Serialize(object value)
        {
            var output = new StringBuilder();
            Write(output, value, 0);
            if (output.Length > MaxLength) throw new FormatException("JSON output too large");
            return output.ToString();
        }
        // Target type is chosen by our code, never by the payload. Unknown fields are ignored.
        public static T ConvertTo<T>(SimpleJsonObject obj) where T : new() => (T)ConvertValue(obj, typeof(T));
        private static object ConvertValue(object value, Type type)
        {
            if (value == null)
            {
                if (type.IsValueType) throw new FormatException("Null value for " + type.Name);
                return null;
            }
            if (type == typeof(string))
            {
                if (value is string text) return text;
                throw new FormatException("Expected string");
            }
            if (type == typeof(bool))
            {
                if (value is bool boolean) return boolean;
                throw new FormatException("Expected boolean");
            }
            if (type.IsPrimitive || type == typeof(decimal))
            {
                if (!(value is JsonNumber number)) throw new FormatException("Expected number");
                try
                {
                    object parsed = Convert.ChangeType(number.Text, type, CultureInfo.InvariantCulture);
                    if (parsed is float f && (float.IsNaN(f) || float.IsInfinity(f))) throw new FormatException("Non-finite number");
                    return parsed;
                }
                catch (OverflowException e) { throw new FormatException("Number out of range",e); }
            }
            if (type.IsArray)
            {
                if (!(value is List<object> items)) throw new FormatException("Expected array");
                var result=System.Array.CreateInstance(type.GetElementType(),items.Count);
                for(int i=0;i<items.Count;i++) result.SetValue(ConvertValue(items[i],type.GetElementType()),i);
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!(value is List<object> items)) throw new FormatException("Expected list");
                var result=(IList)Activator.CreateInstance(type);
                foreach(var item in items) result.Add(ConvertValue(item,type.GetGenericArguments()[0]));
                return result;
            }
            if (!(value is SimpleJsonObject data)) throw new FormatException("Expected object: " + type.Name);
            var instance=Activator.CreateInstance(type);
            foreach(var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if(data.Values.TryGetValue(field.Name,out var fieldValue)) field.SetValue(instance,ConvertValue(fieldValue,field.FieldType));
            return instance;
        }
        private static void Write(StringBuilder output, object value, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nesting limit exceeded");
            if (value == null) { output.Append("null"); return; }
            if (value is string text) { Quote(output,text); return; }
            if (value is bool boolean) { output.Append(boolean ? "true" : "false"); return; }
            if (value is JsonNumber number) { output.Append(number.Text); return; }
            if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)) || value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
                throw new FormatException("Non-finite JSON number");
            if (value is IConvertible && !(value is Enum))
            {
                output.Append(value is float fv ? fv.ToString("R",CultureInfo.InvariantCulture) : value is double dv ? dv.ToString("R",CultureInfo.InvariantCulture) : Convert.ToString(value,CultureInfo.InvariantCulture));
                return;
            }
            if (value is SimpleJsonObject obj) { WriteObject(output,obj.Values,depth); return; }
            if (value is IDictionary dictionary) { WriteObject(output,dictionary,depth); return; }
            if (value is IEnumerable sequence)
            {
                output.Append('['); bool first=true;
                foreach (var item in sequence) { if (!first) output.Append(','); first=false; Write(output,item,depth+1); }
                output.Append(']'); return;
            }
            // Caller-owned DTOs only; JSON never supplies CLR type names.
            var fields = new SortedDictionary<string,object>(StringComparer.Ordinal);
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)) fields.Add(field.Name,field.GetValue(value));
            WriteObject(output,fields,depth);
        }
        private static void WriteObject(StringBuilder output, IDictionary values, int depth)
        {
            output.Append('{'); bool first=true;
            foreach (DictionaryEntry item in values)
            {
                if (!first) output.Append(','); first=false;
                Quote(output,(string)item.Key); output.Append(':'); Write(output,item.Value,depth+1);
            }
            output.Append('}');
        }
        private static void Quote(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (char c in value)
            {
                switch(c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default: if (c < 32 || char.IsSurrogate(c)) output.Append("\\u" + ((int)c).ToString("x4")); else output.Append(c); break;
                }
            }
            output.Append('"');
        }

        private sealed class Reader
        {
            private readonly string json;
            private int pos;
            public Reader(string json) { this.json=json; }
            private FormatException Error(string reason) => new FormatException(reason + " (JSON offset " + pos + ")");
            private void Whitespace() { while (pos<json.Length && (json[pos]==' ' || json[pos]=='\t' || json[pos]=='\n' || json[pos]=='\r')) pos++; }
            private bool Take(char c) { Whitespace(); if(pos<json.Length && json[pos]==c) { pos++; return true; } return false; }
            private void Expect(char c) { if(!Take(c)) throw Error("Expected '" + c + "'"); }
            public SimpleJsonObject ReadRoot()
            {
                var root=Value(0) as SimpleJsonObject;
                if(root==null) throw Error("Expected JSON object");
                Whitespace(); if(pos!=json.Length) throw Error("Unexpected trailing data");
                return root;
            }
            private object Value(int depth)
            {
                if(depth>MaxDepth) throw Error("JSON nesting limit exceeded");
                Whitespace(); if(pos>=json.Length) throw Error("Incomplete JSON value");
                char c=json[pos];
                if(c=='{')
                {
                    pos++; var obj=new SimpleJsonObject();
                    if(Take('}')) return obj;
                    do
                    {
                        Whitespace(); string key=Text(); Expect(':');
                        if(obj.ContainsKey(key)) throw Error("Duplicate property: " + key);
                        obj.Values.Add(key,Value(depth+1));
                        if(Take('}')) return obj;
                        Expect(',');
                    } while(true);
                }
                if(c=='[')
                {
                    pos++; var array=new List<object>();
                    if(Take(']')) return array;
                    do { array.Add(Value(depth+1)); if(Take(']')) return array; Expect(','); } while(true);
                }
                if(c=='"') return Text();
                if(c=='t') { Literal("true"); return true; }
                if(c=='f') { Literal("false"); return false; }
                if(c=='n') { Literal("null"); return null; }
                if(c=='-' || Digit(c)) return Number();
                throw Error("Unexpected JSON value");
            }
            private void Literal(string text)
            {
                if(pos+text.Length>json.Length || string.CompareOrdinal(json,pos,text,0,text.Length)!=0) throw Error("Invalid JSON literal");
                pos+=text.Length;
            }
            private static bool Digit(char c) => c>='0' && c<='9';
            private void Digits()
            {
                int start=pos; while(pos<json.Length && Digit(json[pos])) pos++;
                if(pos==start) throw Error("Expected digit");
            }
            private JsonNumber Number()
            {
                int start=pos;
                if(json[pos]=='-') pos++;
                if(pos<json.Length && json[pos]=='0') pos++; else Digits();
                if(pos<json.Length && json[pos]=='.') { pos++; Digits(); }
                if(pos<json.Length && (json[pos]=='e' || json[pos]=='E'))
                { pos++; if(pos<json.Length && (json[pos]=='+' || json[pos]=='-')) pos++; Digits(); }
                string token=json.Substring(start,pos-start);
                if(!double.TryParse(token,NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || double.IsNaN(value) || double.IsInfinity(value)) throw Error("Non-finite number");
                return new JsonNumber(token);
            }
            private char HexChar()
            {
                if(pos+4>json.Length) throw Error("Incomplete Unicode escape");
                if(!ushort.TryParse(json.Substring(pos,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var code)) throw Error("Invalid Unicode escape");
                pos+=4; return (char)code;
            }
            private string Text()
            {
                if(pos>=json.Length || json[pos++]!='"') throw Error("Expected quoted string");
                var output=new StringBuilder();
                while(pos<json.Length)
                {
                    char c=json[pos++];
                    if(c=='"') return output.ToString();
                    if(c<32) throw Error("Unescaped control character");
                    if(c=='\\')
                    {
                        if(pos>=json.Length) throw Error("Incomplete escape");
                        switch(json[pos++])
                        {
                            case '"': c='"'; break; case '\\': c='\\'; break; case '/': c='/'; break;
                            case 'b': c='\b'; break; case 'f': c='\f'; break; case 'n': c='\n'; break; case 'r': c='\r'; break; case 't': c='\t'; break;
                            case 'u': c=HexChar(); break;
                            default: throw Error("Invalid escape");
                        }
                    }
                    if(char.IsHighSurrogate(c))
                    {
                        output.Append(c); char low;
                        if(pos<json.Length && json[pos]=='\\') { pos++; if(pos>=json.Length || json[pos++]!='u') throw Error("Expected low surrogate"); low=HexChar(); }
                        else { if(pos>=json.Length) throw Error("Incomplete surrogate"); low=json[pos++]; }
                        if(!char.IsLowSurrogate(low)) throw Error("Invalid surrogate pair");
                        output.Append(low);
                    }
                    else { if(char.IsLowSurrogate(c)) throw Error("Unexpected low surrogate"); output.Append(c); }
                }
                throw Error("Unterminated string");
            }
        }
    }
}
