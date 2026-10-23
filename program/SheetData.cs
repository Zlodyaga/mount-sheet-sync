using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MountSheetSync
{
    /// <summary>Что осталось одному персонажу — для меню значка.</summary>
    sealed class CharLeft
    {
        public string Name = "";
        /// <summary>Строк «Нет» — сколько осталось.</summary>
        public int Total;
        /// <summary>Всех строк персонажа, вместе с «Да».</summary>
        public int All;
        public List<string> Lines = new List<string>();
    }

    /// <summary>
    /// Список «Осталось на этой неделе» для аддона: ответ скрипта таблицы (action "status")
    /// превращается в Lua-файл Interface\AddOns\MountSheetSync\SheetData.lua. Игра читает его
    /// при входе и /reload; аддон показывает список во вкладке «Осталось».
    /// </summary>
    static class SheetData
    {
        public const string FileName = "SheetData.lua";
        const int MenuLinesPerCharacter = 30;
        const long Week = 7 * 24 * 60 * 60;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static readonly HashSet<string> LuaKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        };

        /// <summary>Папка аддона в папке игры (_retail_).</summary>
        public static string AddonDir(string retailDir)
        {
            return Path.Combine(Path.Combine(Path.Combine(retailDir, "Interface"), "AddOns"), "MountSheetSync");
        }

        public static string Build(Dictionary<string, object> status)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in status)
            {
                if (kv.Key != "ok")
                {
                    data[kv.Key] = kv.Value;
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("-- Mount Sheet Sync: what is left this week, copied from the Google sheet.\r\n");
            sb.Append("-- MountSheetSync.exe rewrites this file; there is no need to edit it.\r\n");
            sb.Append("MountSheetSyncSheet = ");
            WriteValue(sb, data, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value, int depth)
        {
            if (value == null)
            {
                sb.Append("nil");
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
            if (value is double || value is long || value is int)
            {
                WriteNumber(sb, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }
            IDictionary<string, object> dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool any = false;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (kv.Value == null)
                    {
                        continue;
                    }
                    sb.Append(any ? ",\r\n" : "\r\n");
                    any = true;
                    sb.Append(' ', (depth + 1) * 2);
                    WriteKey(sb, kv.Key);
                    sb.Append(" = ");
                    WriteValue(sb, kv.Value, depth + 1);
                }
                if (any)
                {
                    sb.Append("\r\n").Append(' ', depth * 2);
                }
                sb.Append('}');
                return;
            }
            IList list = value as IList;
            if (list != null)
            {
                bool flat = true;
                foreach (object item in list)
                {
                    if (item is IDictionary<string, object> || item is IList)
                    {
                        flat = false;
                        break;
                    }
                }
                sb.Append('{');
                for (int i = 0; i < list.Count; i++)
                {
                    if (flat)
                    {
                        sb.Append(i == 0 ? "" : ", ");
                    }
                    else
                    {
                        sb.Append(i == 0 ? "\r\n" : ",\r\n").Append(' ', (depth + 1) * 2);
                    }
                    WriteValue(sb, list[i], depth + 1);
                }
                if (!flat && list.Count > 0)
                {
                    sb.Append("\r\n").Append(' ', depth * 2);
                }
                sb.Append('}');
                return;
            }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static void WriteKey(StringBuilder sb, string key)
        {
            bool identifier = key.Length > 0 && !LuaKeywords.Contains(key) && !(key[0] >= '0' && key[0] <= '9');
            foreach (char c in key)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    identifier = false;
                    break;
                }
            }
            if (identifier)
            {
                sb.Append(key);
            }
            else
            {
                sb.Append('[');
                WriteString(sb, key);
                sb.Append(']');
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                sb.Append('0');
            }
            else if (Math.Floor(d) == d && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Строка Lua в кавычках: \\, ", переводы строк и управляющие символы экранируются.</summary>
        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < ' ' || c == (char)127)
                        {
                            sb.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
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

        /// <summary>Персонажи и их строки «Нет» — для подменю «Осталось на неделе».</summary>
        public static List<CharLeft> Summarize(Dictionary<string, object> status)
        {
            List<CharLeft> result = new List<CharLeft>();
            foreach (object o in J.Arr(J.Get(status, "chars")))
            {
                Dictionary<string, object> ch = J.Obj(o);
                if (ch == null)
                {
                    continue;
                }
                CharLeft left = new CharLeft();
                left.Name = J.Str(ch, "name");
                foreach (object io in J.Arr(J.Get(ch, "items")))
                {
                    Dictionary<string, object> item = J.Obj(io);
                    if (item == null)
                    {
                        continue;
                    }
                    left.All++;
                    if (J.Truthy(item, "y"))
                    {
                        continue;   // в таблице уже «Да» (аддону такие строки нужны после «Сброса недели»)
                    }
                    left.Total++;
                    if (left.Lines.Count < MenuLinesPerCharacter)
                    {
                        left.Lines.Add(Describe(item));
                    }
                }
                if (left.Total > left.Lines.Count)
                {
                    left.Lines.Add(Tr.F("…и ещё {0}", left.Total - left.Lines.Count));
                }
                result.Add(left);
            }
            return result;
        }

        static string Describe(Dictionary<string, object> item)
        {
            StringBuilder sb = new StringBuilder(J.Str(item, "boss"));
            string diff = J.Str(item, "diff");
            if (diff.Length > 0)
            {
                sb.Append(" (").Append(diff).Append(')');
            }
            string mount = J.Str(item, "mount");
            if (mount.Length > 0)
            {
                sb.Append(" — ").Append(mount);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Напоминание, если на этой неделе в таблице ещё не было «Сброса недели». Неделю
        /// узнаём по меткам weekEnd в журнале аддона (сброс в игре — раз в 7 суток в одно время),
        /// сброс — по resetAt из таблицы или по записи «Сброс недели» этой недели в журнале.
        /// </summary>
        public static string WeekNote(Dictionary<string, object> status, IEnumerable<Dictionary<string, object>> records, DateTime utcNow)
        {
            long now = (long)(utcNow - Epoch).TotalSeconds;
            long latest = 0;
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>(records);
            foreach (Dictionary<string, object> r in list)
            {
                latest = Math.Max(latest, J.Long(r, "weekEnd"));
            }
            if (latest <= 0)
            {
                return "";   // неделя неизвестна
            }
            long weekEnd = latest > now ? latest : latest + Week * ((now - latest) / Week + 1);
            long weekStart = weekEnd - Week;
            foreach (Dictionary<string, object> r in list)
            {
                if (J.Str(r, "kind") == "reset" && Math.Abs(J.Long(r, "weekEnd") - weekEnd) < 3600)
                {
                    return "";
                }
            }
            long resetAt = J.Long(status, "resetAt");
            if (resetAt > 0 && resetAt >= weekStart - 3600)
            {
                return "";
            }
            return Tr._("На этой неделе «Сброс недели» не нажимали");
        }

        /// <summary>Сколько всего строк осталось — по персонажам («Эйра 4, Элтара 7»).</summary>
        public static string Counts(List<CharLeft> left)
        {
            List<string> parts = new List<string>();
            foreach (CharLeft c in left)
            {
                parts.Add(c.Name + " " + c.Total);
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
