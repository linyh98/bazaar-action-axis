using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BazaarActionAxis.Engine
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    public sealed class JsonValue
    {
        private readonly JsonKind _kind;
        private readonly bool _boolValue;
        private readonly double _numberValue;
        private readonly string _stringValue;
        private readonly List<JsonValue> _arrayValue;
        private readonly Dictionary<string, JsonValue> _objectValue;

        private JsonValue(JsonKind kind)
        {
            _kind = kind;
        }

        private JsonValue(bool value)
        {
            _kind = JsonKind.Bool;
            _boolValue = value;
        }

        private JsonValue(double value)
        {
            _kind = JsonKind.Number;
            _numberValue = value;
        }

        private JsonValue(string value)
        {
            _kind = JsonKind.String;
            _stringValue = value;
        }

        private JsonValue(List<JsonValue> value)
        {
            _kind = JsonKind.Array;
            _arrayValue = value;
        }

        private JsonValue(Dictionary<string, JsonValue> value)
        {
            _kind = JsonKind.Object;
            _objectValue = value;
        }

        public static JsonValue Null { get { return new JsonValue(JsonKind.Null); } }

        public static JsonValue FromBool(bool value) { return new JsonValue(value); }
        public static JsonValue FromNumber(double value) { return new JsonValue(value); }
        public static JsonValue FromString(string value) { return new JsonValue(value); }
        public static JsonValue FromArray(List<JsonValue> value) { return new JsonValue(value); }
        public static JsonValue FromObject(Dictionary<string, JsonValue> value) { return new JsonValue(value); }

        public JsonKind Kind { get { return _kind; } }

        public int Count
        {
            get
            {
                if (_kind == JsonKind.Array) return _arrayValue.Count;
                if (_kind == JsonKind.Object) return _objectValue.Count;
                return 0;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (_kind != JsonKind.Array) throw new InvalidOperationException("not an array");
                return _arrayValue[index];
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                if (_kind != JsonKind.Object) throw new InvalidOperationException("not an object");
                JsonValue value;
                return _objectValue.TryGetValue(key, out value) ? value : Null;
            }
        }

        public bool Has(string key)
        {
            return _kind == JsonKind.Object && _objectValue.ContainsKey(key);
        }

        public List<JsonValue> AsArray()
        {
            return _kind == JsonKind.Array ? _arrayValue : new List<JsonValue>();
        }

        public Dictionary<string, JsonValue> AsObject()
        {
            return _kind == JsonKind.Object
                ? _objectValue
                : new Dictionary<string, JsonValue>();
        }

        public string AsString()
        {
            return _kind == JsonKind.String ? _stringValue : null;
        }

        public double AsDouble()
        {
            return _kind == JsonKind.Number ? _numberValue : 0;
        }

        public int AsInt()
        {
            return (int)Math.Floor(AsDouble());
        }

        public bool AsBool()
        {
            return _kind == JsonKind.Bool && _boolValue;
        }
    }

    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int index = 0;
            JsonValue value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
                throw new FormatException("trailing characters at " + index);
            return value;
        }

        private static JsonValue ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length) throw new FormatException("unexpected end of input");
            char c = text[index];
            if (c == '{') return ParseObject(text, ref index);
            if (c == '[') return ParseArray(text, ref index);
            if (c == '"') return JsonValue.FromString(ParseString(text, ref index));
            if (c == 't') { Expect(text, ref index, "true"); return JsonValue.FromBool(true); }
            if (c == 'f') { Expect(text, ref index, "false"); return JsonValue.FromBool(false); }
            if (c == 'n') { Expect(text, ref index, "null"); return JsonValue.Null; }
            return JsonValue.FromNumber(ParseNumber(text, ref index));
        }

        private static JsonValue ParseObject(string text, ref int index)
        {
            index++;
            Dictionary<string, JsonValue> result = new Dictionary<string, JsonValue>();
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return JsonValue.FromObject(result);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                    throw new FormatException("expected object key at " + index);
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                    throw new FormatException("expected ':' at " + index);
                index++;
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("unterminated object");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == '}')
                {
                    index++;
                    return JsonValue.FromObject(result);
                }
                throw new FormatException("expected ',' or '}' at " + index);
            }
        }

        private static JsonValue ParseArray(string text, ref int index)
        {
            index++;
            List<JsonValue> result = new List<JsonValue>();
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return JsonValue.FromArray(result);
            }

            while (true)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("unterminated array");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == ']')
                {
                    index++;
                    return JsonValue.FromArray(result);
                }
                throw new FormatException("expected ',' or ']' at " + index);
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++;
            StringBuilder builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length) throw new FormatException("unterminated escape");
                char escape = text[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                            throw new FormatException("bad unicode escape at " + index);
                        string hex = text.Substring(index, 4);
                        index += 4;
                        ushort code;
                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("bad unicode escape at " + (index - 4));
                        builder.Append((char)code);
                        break;
                    default:
                        throw new FormatException("unknown escape at " + (index - 1));
                }
            }

            throw new FormatException("unterminated string");
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+')) index++;
            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    index++;
                    continue;
                }
                break;
            }

            if (start == index) throw new FormatException("expected number at " + start);
            string token = text.Substring(start, index - start);
            double value;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("bad number '" + token + "' at " + start);
            return value;
        }

        private static void Expect(string text, ref int index, string token)
        {
            if (index + token.Length > text.Length ||
                string.CompareOrdinal(text, index, token, 0, token.Length) != 0)
                throw new FormatException("expected '" + token + "' at " + index);
            index += token.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                    continue;
                }
                break;
            }
        }
    }
}
