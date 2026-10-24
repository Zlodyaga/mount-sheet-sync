using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    /// <summary>Журнал работы: %APPDATA%\MountSheetSync\sync.log (больше 1 МБ — в sync.log.old).</summary>
    static class Log
    {
        static readonly object Gate = new object();
        static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        static string path;
        static bool toConsole;
        static int writes;

        public static string FilePath
        {
            get { return path; }
        }

        public static void Init(string file, bool console)
        {
            path = file;
            toConsole = console;
            Rotate();
        }

        /// <summary>Больше не писать в файл (папку настроек сейчас удалят).</summary>
        public static void Close()
        {
            lock (Gate)
            {
                path = null;
            }
        }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            lock (Gate)
            {
                if (path != null)
                {
                    try
                    {
                        if (++writes % 200 == 0)
                        {
                            Rotate();
                        }
                        File.AppendAllText(path, line + "\r\n", Utf8Bom);
                    }
                    catch (Exception)
                    {
                    }
                }
                if (toConsole)
                {
                    try
                    {
                        Console.WriteLine(message);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        static void Rotate()
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    string old = path + ".old";
                    if (File.Exists(old))
                    {
                        File.Delete(old);
                    }
                    File.Move(path, old);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    static class FileUtil
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Запись через временный файл, чтобы при сбое не остался обрезанный файл.</summary>
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Utf8NoBom);
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (Exception)
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }
    }

    static class Epoch
    {
        static readonly DateTime Zero = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long Now
        {
            get { return (long)(DateTime.UtcNow - Zero).TotalSeconds; }
        }

        public static DateTime ToLocal(long seconds)
        {
            return Zero.AddSeconds(seconds).ToLocalTime();
        }

        public static long FromLocal(DateTime local)
        {
            return (long)(local.ToUniversalTime() - Zero).TotalSeconds;
        }
    }

    static class TextUtil
    {
        /// <summary>«а, б, в и ещё 2» — первые max элементов списка.</summary>
        public static string ShortList(IList<string> items, int max)
        {
            if (items.Count <= max)
            {
                return string.Join(", ", items.ToArray());
            }
            return string.Join(", ", items.Take(max).ToArray()) + Tr.F(" и ещё {0}", items.Count - max);
        }

        public static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s ?? "";
            }
            return s.Substring(0, max - 1).TrimEnd() + "…";
        }

        /// <summary>С заглавной буквы: «нет связи…» → «Нет связи…».</summary>
        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s ?? "";
            }
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Фраза с точкой на конце — чтобы к ней можно было дописать следующую.</summary>
        public static string Sentence(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0)
            {
                return s;
            }
            char last = s[s.Length - 1];
            return last == '.' || last == '!' || last == '?' || last == '…' ? s : s + ".";
        }

        public static string When(DateTime t)
        {
            string time = t.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (t.Date == DateTime.Today)
            {
                return Tr.F("сегодня в {0}", time);
            }
            if (t.Date == DateTime.Today.AddDays(-1))
            {
                return Tr.F("вчера в {0}", time);
            }
            return Tr.F("{0} в {1}", t.ToString("dd.MM", CultureInfo.InvariantCulture), time);
        }

        /// <summary>Время события в меню: «21:14», «вчера 21:14» или «14.10 21:14».</summary>
        public static string Stamp(DateTime t)
        {
            string time = t.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (t.Date == DateTime.Today)
            {
                return time;
            }
            if (t.Date == DateTime.Today.AddDays(-1))
            {
                return Tr.F("вчера {0}", time);
            }
            return t.ToString("dd.MM", CultureInfo.InvariantCulture) + " " + time;
        }
    }

    /// <summary>
    /// Небольшой разбор и запись JSON. Объекты — Dictionary&lt;string, object&gt;,
    /// массивы — List&lt;object&gt;, числа — double, а также строки, true/false и null.
    /// </summary>
    static class Json
    {
        public static object Parse(string text)
        {
            Reader reader = new Reader(text ?? "");
            object value = reader.ReadValue();
            reader.SkipSpace();
            if (!reader.AtEnd)
            {
                throw new FormatException("extra characters after JSON");
            }
            return value;
        }

        public static string Write(object value, bool indented)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, indented, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value, bool indented, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            string s = value as string;
            if (s != null)
            {
                WriteString(sb, s);
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is int || value is long || value is short || value is byte
                || value is uint || value is ulong || value is ushort || value is sbyte)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is double || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                }
                return;
            }
            IDictionary<string, object> dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    NewLine(sb, indented, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(indented ? ": " : ":");
                    WriteValue(sb, kv.Value, indented, depth + 1);
                }
                if (!first)
                {
                    NewLine(sb, indented, depth);
                }
                sb.Append('}');
                return;
            }
            IEnumerable list = value as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    WriteValue(sb, item, false, depth + 1);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (indented)
            {
                sb.Append("\r\n");
                sb.Append(' ', depth * 2);
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
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
                        if (c < ' ' || c == (char)0x2028 || c == (char)0x2029)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text)
            {
                s = text;
            }

            public bool AtEnd
            {
                get { return i >= s.Length; }
            }

            public void SkipSpace()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                {
                    i++;
                }
            }

            Exception Error(string what)
            {
                return new FormatException(what + " (position " + i + ")");
            }

            public object ReadValue()
            {
                SkipSpace();
                if (i >= s.Length)
                {
                    throw Error("unexpected end of JSON");
                }
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw Error("unexpected character");
            }

            void Expect(string word)
            {
                if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                {
                    throw Error("expected " + word);
                }
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++;
                SkipSpace();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return d;
                }
                while (true)
                {
                    SkipSpace();
                    if (i >= s.Length || s[i] != '"')
                    {
                        throw Error("expected a key");
                    }
                    string key = ReadString();
                    SkipSpace();
                    if (i >= s.Length || s[i] != ':')
                    {
                        throw Error("expected a colon");
                    }
                    i++;
                    d[key] = ReadValue();
                    SkipSpace();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return d;
                    }
                    throw Error("expected a comma or }");
                }
            }

            List<object> ReadArray()
            {
                List<object> list = new List<object>();
                i++;
                SkipSpace();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipSpace();
                    if (i < s.Length && s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return list;
                    }
                    throw Error("expected a comma or ]");
                }
            }

            string ReadString()
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length)
                    {
                        throw Error("unterminated string");
                    }
                    char c = s[i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length)
                    {
                        throw Error("unterminated string");
                    }
                    char e = s[i++];
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
                            if (i + 4 > s.Length)
                            {
                                throw Error("incomplete \\u sequence");
                            }
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default:
                            throw Error("unknown escape sequence");
                    }
                }
            }

            double ReadNumber()
            {
                int start = i;
                if (s[i] == '-')
                {
                    i++;
                }
                while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e'
                    || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                {
                    i++;
                }
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Доступ к полям разобранного JSON и записей аддона.</summary>
    static class J
    {
        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> Arr(object o)
        {
            return (o as List<object>) ?? new List<object>();
        }

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null)
            {
                return "";
            }
            if (v is double)
            {
                return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            }
            if (v is bool)
            {
                return (bool)v ? "true" : "false";
            }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Truthy(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null)
            {
                return false;
            }
            if (v is bool)
            {
                return (bool)v;
            }
            if (v is double)
            {
                return (double)v != 0;
            }
            if (v is long)
            {
                return (long)v != 0;
            }
            string s = v as string;
            return s == null || s.Length > 0;
        }

        public static long Long(IDictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v is long)
            {
                return (long)v;
            }
            if (v is double)
            {
                return (long)(double)v;
            }
            long n;
            long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            return n;
        }
    }
}
