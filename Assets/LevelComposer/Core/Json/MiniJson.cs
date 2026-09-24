using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RythmRPG.LevelComposer.Json
{
    /// <summary>
    /// Small, dependency-free JSON reader/writer. Objects become <see cref="Dictionary{TKey,TValue}"/> (string, object),
    /// arrays become <see cref="List{T}"/> (object), numbers become double, plus string / bool / null.
    /// Used for level files and note-type files because JsonUtility cannot store free-form note parameters.
    /// Always culture-invariant.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("Unexpected text after the JSON value");
            return value;
        }

        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ writer

        private static void Write(StringBuilder sb, object value, bool pretty, int indent)
        {
            if (value == null) { sb.Append("null"); return; }
            string s = value as string;
            if (s != null) { WriteString(sb, s); return; }
            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
            if (value is double || value is float || value is int || value is long || value is short || value is byte || value is decimal || value is uint || value is ulong)
            {
                WriteNumber(sb, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is Enum) { WriteString(sb, value.ToString()); return; }

            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, pretty, indent + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(pretty ? ": " : ":");
                    Write(sb, kv.Value, pretty, indent + 1);
                }

                NewLine(sb, pretty, indent);
                sb.Append('}');
                return;
            }

            var list = value as System.Collections.IList;
            if (list != null)
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                bool simple = true;
                for (int i = 0; i < list.Count; i++)
                {
                    object item = list[i];
                    if (item is IDictionary<string, object> || item is System.Collections.IList) { simple = false; break; }
                }

                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(simple && pretty ? ", " : ",");
                    if (!simple) NewLine(sb, pretty, indent + 1);
                    Write(sb, list[i], pretty, indent + 1);
                }

                if (!simple) NewLine(sb, pretty, indent);
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            if (Math.Abs(d - Math.Round(d)) < 1e-12 && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                return;
            }

            // Round-trip precision without noisy trailing digits for values typed by people (e.g. 0.1).
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            string g = d.ToString("0.##########", CultureInfo.InvariantCulture);
            sb.Append(double.Parse(g, CultureInfo.InvariantCulture) == d ? g : r);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }

        // ------------------------------------------------------------------ reader

        private sealed class Parser
        {
            private readonly string text;
            private int pos;

            public Parser(string text)
            {
                this.text = text;
                // Tolerate a UTF-8 byte order mark.
                if (text.Length > 0 && text[0] == '﻿') pos = 1;
            }

            public bool AtEnd { get { return pos >= text.Length; } }

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int i = 0; i < pos && i < text.Length; i++)
                {
                    if (text[i] == '\n') { line++; col = 1; }
                    else col++;
                }

                return new FormatException(message + " (line " + line + ", column " + col + ")");
            }

            public void SkipWhitespace()
            {
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { pos++; continue; }
                    // Allow // line comments so hand-written note-type files can be annotated.
                    if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                    {
                        while (pos < text.Length && text[pos] != '\n') pos++;
                        continue;
                    }

                    break;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = text[pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                pos += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                pos++; // {
                SkipWhitespace();
                if (pos < text.Length && text[pos] == '}') { pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"') throw Error("Expected a property name");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || text[pos] != ':') throw Error("Expected ':'");
                    pos++;
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (text[pos] == ',')
                    {
                        pos++;
                        SkipWhitespace();
                        if (pos < text.Length && text[pos] == '}') { pos++; return result; } // trailing comma
                        continue;
                    }

                    if (text[pos] == '}') { pos++; return result; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                pos++; // [
                SkipWhitespace();
                if (pos < text.Length && text[pos] == ']') { pos++; return result; }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (text[pos] == ',')
                    {
                        pos++;
                        SkipWhitespace();
                        if (pos < text.Length && text[pos] == ']') { pos++; return result; }
                        continue;
                    }

                    if (text[pos] == ']') { pos++; return result; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                pos++; // opening quote
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length) throw Error("Bad unicode escape");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = pos;
                if (text[pos] == '-') pos++;
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') pos++;
                    else break;
                }

                double d;
                if (!double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Error("Bad number");
                return d;
            }
        }
    }

    /// <summary>Typed accessors over parsed JSON objects, tolerant of missing keys and wrong types.</summary>
    public static class JsonRead
    {
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static string Str(IDictionary<string, object> d, string key, string fallback = "")
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            string s = v as string;
            if (s != null) return s;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return fallback;
        }

        public static double Num(IDictionary<string, object> d, string key, double fallback = 0d)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            return ToDouble(v, fallback);
        }

        public static int Int(IDictionary<string, object> d, string key, int fallback = 0)
        {
            double n = Num(d, key, fallback);
            return (int)Math.Round(n);
        }

        public static bool Bool(IDictionary<string, object> d, string key, bool fallback = false)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0d;
            string s = v as string;
            if (s != null)
            {
                bool b;
                if (bool.TryParse(s, out b)) return b;
            }

            return fallback;
        }

        public static double ToDouble(object v, double fallback)
        {
            if (v is double) return (double)v;
            if (v is bool) return (bool)v ? 1d : 0d;
            if (v is int || v is long || v is float || v is decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            string s = v as string;
            double d;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }
    }
}
