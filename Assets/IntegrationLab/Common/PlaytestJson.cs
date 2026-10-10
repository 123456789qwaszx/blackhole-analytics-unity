using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntegrationLab
{
    // 테스트 도구가 쓰는 작은 JSON 도구. JsonUtility는 null·순서 있는 객체·사전을 다루지 못해, 메모(M3)와 AI 묶음(M4)은 이것으로 쓰고 읽는다.
    // - 쓰기: JsonObject(넣은 순서를 지키는 키·값 목록), IList, string, bool, 정수, 실수, null
    // - 읽기: 객체는 JsonObject, 배열은 List<object>, 숫자는 double, 그 밖은 string·bool·null
    internal static class PlaytestJson
    {
        public static string Write(object value, bool indented = false)
        {
            var text = new StringBuilder();
            Write(text, value, indented, 0);
            return text.ToString();
        }

        private static void Write(StringBuilder text, object value, bool indented, int depth)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    return;
                case string s:
                    WriteString(text, s);
                    return;
                case bool b:
                    text.Append(b ? "true" : "false");
                    return;
                case float f:
                    WriteNumber(text, f);
                    return;
                case double d:
                    WriteNumber(text, d);
                    return;
                case Enum e:
                    WriteString(text, e.ToString());
                    return;
                case IFormattable number when value.GetType().IsPrimitive:
                    text.Append(number.ToString(null, CultureInfo.InvariantCulture));
                    return;
                case JsonObject obj:
                    WriteObject(text, obj, indented, depth);
                    return;
                case IList list:
                    WriteArray(text, list, indented, depth);
                    return;
                default:
                    throw new ArgumentException($"JSON으로 쓸 수 없는 값이다: {value.GetType().Name}");
            }
        }

        private static void WriteNumber(StringBuilder text, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                text.Append("null");
                return;
            }

            text.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder text, JsonObject obj, bool indented, int depth)
        {
            if (obj.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append('{');

            for (int i = 0; i < obj.Count; i++)
            {
                if (i > 0)
                    text.Append(',');

                NewLine(text, indented, depth + 1);
                WriteString(text, obj[i].Key);
                text.Append(indented ? ": " : ":");
                Write(text, obj[i].Value, indented, depth + 1);
            }

            NewLine(text, indented, depth);
            text.Append('}');
        }

        private static void WriteArray(StringBuilder text, IList list, bool indented, int depth)
        {
            if (list.Count == 0)
            {
                text.Append("[]");
                return;
            }

            // 숫자·글만 든 짧은 배열은 들여쓰기 모드에서도 한 줄로 쓴다(읽기 쉽게).
            bool flat = !indented || IsFlat(list);
            text.Append('[');

            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    text.Append(flat && indented ? ", " : ",");

                if (!flat)
                    NewLine(text, indented, depth + 1);

                Write(text, list[i], indented, depth + 1);
            }

            if (!flat)
                NewLine(text, indented, depth);

            text.Append(']');
        }

        private static bool IsFlat(IList list)
        {
            if (list.Count > 16)
                return false;

            foreach (object item in list)
            {
                if (item is JsonObject || item is IList && !(item is string))
                    return false;
            }

            return true;
        }

        private static void NewLine(StringBuilder text, bool indented, int depth)
        {
            if (!indented)
                return;

            text.Append('\n');
            text.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            text.Append(c);
                        break;
                }
            }

            text.Append('"');
        }

        // 한 줄에 객체 하나인 파일(.ndjson)의 줄들 → schema가 맞는 객체(파일 순서). 읽지 못한 줄은 건너뛰고 센다.
        public static List<JsonObject> ParseLines(IEnumerable<string> lines, int schema, out int skipped)
        {
            var objects = new List<JsonObject>();
            skipped = 0;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    if (Parse(line) is JsonObject obj && obj.Int("schema") == schema)
                        objects.Add(obj);
                    else
                        skipped++;
                }
                catch (FormatException)
                {
                    skipped++;
                }
            }

            return objects;
        }

        // 읽기. 틀린 JSON이면 FormatException.
        public static object Parse(string json)
        {
            if (json == null)
                throw new FormatException("빈 JSON이다.");

            int at = 0;
            object value = ReadValue(json, ref at);
            SkipSpace(json, ref at);

            if (at != json.Length)
                throw new FormatException($"{at}번째 글자 뒤에 남은 글이 있다.");

            return value;
        }

        private static object ReadValue(string json, ref int at)
        {
            SkipSpace(json, ref at);

            if (at >= json.Length)
                throw new FormatException("값이 없다.");

            char c = json[at];

            switch (c)
            {
                case '{':
                    return ReadObject(json, ref at);
                case '[':
                    return ReadArray(json, ref at);
                case '"':
                    return ReadString(json, ref at);
                case 't':
                    Expect(json, ref at, "true");
                    return true;
                case 'f':
                    Expect(json, ref at, "false");
                    return false;
                case 'n':
                    Expect(json, ref at, "null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber(json, ref at);

                    throw new FormatException($"{at}번째 글자 '{c}'를 읽을 수 없다.");
            }
        }

        private static JsonObject ReadObject(string json, ref int at)
        {
            var obj = new JsonObject();
            at++;
            SkipSpace(json, ref at);

            if (at < json.Length && json[at] == '}')
            {
                at++;
                return obj;
            }

            while (true)
            {
                SkipSpace(json, ref at);

                if (at >= json.Length || json[at] != '"')
                    throw new FormatException($"{at}번째 글자에 키가 와야 한다.");

                string key = ReadString(json, ref at);
                SkipSpace(json, ref at);

                if (at >= json.Length || json[at] != ':')
                    throw new FormatException($"{at}번째 글자에 ':'가 와야 한다.");

                at++;
                obj.Add(key, ReadValue(json, ref at));
                SkipSpace(json, ref at);

                if (at < json.Length && json[at] == ',')
                {
                    at++;
                    continue;
                }

                if (at < json.Length && json[at] == '}')
                {
                    at++;
                    return obj;
                }

                throw new FormatException($"{at}번째 글자에 ',' 또는 '}}'가 와야 한다.");
            }
        }

        private static List<object> ReadArray(string json, ref int at)
        {
            var list = new List<object>();
            at++;
            SkipSpace(json, ref at);

            if (at < json.Length && json[at] == ']')
            {
                at++;
                return list;
            }

            while (true)
            {
                list.Add(ReadValue(json, ref at));
                SkipSpace(json, ref at);

                if (at < json.Length && json[at] == ',')
                {
                    at++;
                    continue;
                }

                if (at < json.Length && json[at] == ']')
                {
                    at++;
                    return list;
                }

                throw new FormatException($"{at}번째 글자에 ',' 또는 ']'가 와야 한다.");
            }
        }

        private static string ReadString(string json, ref int at)
        {
            var text = new StringBuilder();
            at++;

            while (at < json.Length)
            {
                char c = json[at++];

                if (c == '"')
                    return text.ToString();

                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }

                if (at >= json.Length)
                    break;

                char escape = json[at++];

                switch (escape)
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (at + 4 > json.Length)
                            throw new FormatException("\\u 뒤에 16진 네 자리가 와야 한다.");

                        text.Append((char)int.Parse(json.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default:
                        throw new FormatException($"알 수 없는 이스케이프 '\\{escape}'다.");
                }
            }

            throw new FormatException("글이 닫히지 않았다.");
        }

        private static double ReadNumber(string json, ref int at)
        {
            int start = at;

            while (at < json.Length && "+-0123456789.eE".IndexOf(json[at]) >= 0)
                at++;

            if (!double.TryParse(json.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"{start}번째 글자의 숫자를 읽을 수 없다.");

            return value;
        }

        private static void Expect(string json, ref int at, string word)
        {
            if (string.CompareOrdinal(json, at, word, 0, word.Length) != 0)
                throw new FormatException($"{at}번째 글자에 '{word}'가 와야 한다.");

            at += word.Length;
        }

        private static void SkipSpace(string json, ref int at)
        {
            while (at < json.Length && char.IsWhiteSpace(json[at]))
                at++;
        }
    }

    // 넣은 순서를 지키는 JSON 객체. 같은 키를 다시 넣으면 앞의 것을 바꾼다.
    internal sealed class JsonObject : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value)
        {
            int index = IndexOf(key);

            if (index >= 0)
                this[index] = new KeyValuePair<string, object>(key, value);
            else
                Add(new KeyValuePair<string, object>(key, value));
        }

        public object this[string key] => TryGet(key, out object value) ? value : null;

        public bool TryGet(string key, out object value)
        {
            int index = IndexOf(key);
            value = index >= 0 ? this[index].Value : null;
            return index >= 0;
        }

        public string Text(string key) => this[key] as string;

        public int? Int(string key) => this[key] is double d ? (int)Math.Round(d) : (int?)null;

        public double? Number(string key) => this[key] is double d ? d : (double?)null;

        public JsonObject Object(string key) => this[key] as JsonObject;

        public List<object> Array(string key) => this[key] as List<object>;

        private int IndexOf(string key)
        {
            for (int i = 0; i < Count; i++)
            {
                if (this[i].Key == key)
                    return i;
            }

            return -1;
        }
    }
}
