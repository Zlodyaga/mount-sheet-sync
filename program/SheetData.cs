using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        /// <summary>Поля ответа таблицы, которые аддон не читает: в SheetData.lua это 15 % файла, а игра разбирает его при каждом входе.</summary>
        static readonly HashSet<string> NotForAddon = new HashSet<string> { "sheet", "row" };

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
            Dictionary<string, object> names = Names(data);
            if (names != null)
            {
                data["names"] = names;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("-- Mount Sheet Sync: what is left this week, made by the program.\r\n");
            sb.Append("-- MountSheetSync.exe rewrites this file; there is no need to edit it.\r\n");
            sb.Append("MountSheetSyncSheet = ");
            WriteValue(sb, data, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// Названия на другом языке для окна аддона: язык аддона может быть не тем, что у таблицы. Ключ — текст, как его
        /// прислала таблица, и только то, что есть в этом списке. Маунты и боссы по-русски и по-английски — из каталога;
        /// на языке игры аддон берёт названия у самой игры.
        /// </summary>
        public static Dictionary<string, object> Names(Dictionary<string, object> data)
        {
            HashSet<string> shown = new HashSet<string>(StringComparer.Ordinal);
            foreach (object ch in J.Arr(J.Get(data, "chars")))
            {
                foreach (object it in J.Arr(J.Get(J.Obj(ch), "items")))
                {
                    AddShown(shown, J.Obj(it));
                }
            }
            foreach (object m in J.Arr(J.Get(data, "mounts")))
            {
                AddShown(shown, J.Obj(m));
            }
            foreach (object m in J.Arr(J.Get(data, "rewards")))
            {
                AddShown(shown, J.Obj(m));
            }
            if (shown.Count == 0)
            {
                return null;
            }
            Dictionary<string, Dictionary<string, object>> byLang = new Dictionary<string, Dictionary<string, object>>();
            foreach (string lang in NameLangs)
            {
                byLang[lang] = new Dictionary<string, object>(StringComparer.Ordinal);
            }
            SheetLang english = SheetLang.Get("en");
            Catalog catalog = Catalog.Embedded();
            foreach (CatalogSheet sheet in new[] { catalog.Raids, catalog.Dungeons, catalog.World, catalog.Rare })
            {
                if (sheet == null)
                {
                    continue;
                }
                int cMount = sheet.Col("Маунт");
                int cId = sheet.Col(SheetBuilder.IdColumn);
                int cJournal = sheet.Col("ID в журнале");
                int cMap = sheet.Col("ID места");
                int cBoss = sheet.Columns.FindIndex(c => c.StartsWith("Босс", StringComparison.Ordinal));
                foreach (object[] row in sheet.Rows)
                {
                    long id = cId >= 0 && cId < row.Length ? MountIndex.IdOf(row[cId]) : 0;
                    // маунт: по ID из таблиц игры, английское — и из каталога
                    string mount = Cell(row, cMount);
                    if (mount.Length > 0)
                    {
                        Dictionary<string, string> names = SheetLang.GameName("mounts", id);
                        names["ru"] = mount;
                        if (!names.ContainsKey("en"))
                        {
                            names["en"] = SheetLang.EnglishMount(id) ?? english.Content(mount);
                        }
                        Add(shown, byLang, names, mount, names["en"]);
                    }
                    // босс: у награды — название достижения; иначе первая часть («Король Дазар / Сундук…») — по ID боя
                    string boss = Cell(row, cBoss);
                    if (boss.Length > 0)
                    {
                        string bossEn = english.Content(boss);
                        Dictionary<string, string> titles;
                        Dictionary<string, string> names;
                        if (id > 0 && SheetLang.RewardAchievement(id, out titles) > 0 && titles.Count > 0)
                        {
                            names = titles;
                        }
                        else
                        {
                            names = new Dictionary<string, string>();
                            List<long> journal = cJournal >= 0 && cJournal < row.Length ? Encounters.Ids(row[cJournal]) : new List<long>();
                            if (journal.Count == 1)
                            {
                                foreach (KeyValuePair<string, string> kv in SheetLang.GameName("bosses", journal[0]))
                                {
                                    names[kv.Key] = WithTail(kv.Value, kv.Key == "ru" ? boss : bossEn);
                                }
                            }
                        }
                        foreach (KeyValuePair<string, string> kv in SheetLang.GameText(boss))
                        {
                            names[kv.Key] = kv.Value;
                        }
                        names["ru"] = boss;
                        names["en"] = bossEn;
                        Add(shown, byLang, names, boss, bossEn);
                    }
                    // место: английское из каталога, остальные — по ID карты подземелья
                    string place = Cell(row, 1);
                    List<long> maps = cMap >= 0 && cMap < row.Length ? Encounters.Ids(row[cMap]) : new List<long>();
                    if (place.Length > 0 && maps.Count == 1)
                    {
                        Dictionary<string, string> names = new Dictionary<string, string>();
                        foreach (KeyValuePair<string, string> kv in SheetLang.GameName("places", maps[0]))
                        {
                            names[kv.Key] = WithTail(kv.Value, place, "(");
                        }
                        names["en"] = place;
                        Add(shown, byLang, names, place, place);
                    }
                    else if (place.Length > 0)
                    {
                        Add(shown, byLang, SheetLang.GameText(place), place);   // зона или общая строка: «Vol'dun», «Any Timewalking Dungeon»
                    }
                }
            }
            // условие достижения: игра отдаёт его только на языке клиента
            foreach (object m in J.Arr(J.Get(data, "rewards")))
            {
                long achievement = J.Long(J.Obj(m), "ach");
                foreach (KeyValuePair<string, string> kv in SheetLang.GameName("conditions", achievement))
                {
                    byLang[kv.Key]["ach:" + achievement.ToString(CultureInfo.InvariantCulture)] = kv.Value;
                }
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in byLang)
            {
                if (kv.Value.Count > 0)
                {
                    result[kv.Key] = kv.Value;
                }
            }
            return result.Count > 0 ? result : null;
        }

        static readonly string[] NameLangs = { "ru", "en", "de", "fr", "es", "pt" };

        /// <summary>Имя из игры и хвост строки каталога: «Король Дазар» + « / Сундук претендента», «Терраса» + « (Midnight)».</summary>
        static string WithTail(string name, string text, string mark = " / ")
        {
            if (mark == "(")
            {
                int paren = text.LastIndexOf(" (", StringComparison.Ordinal);
                return paren > 0 && text.EndsWith(")", StringComparison.Ordinal) ? name + text.Substring(paren) : name;
            }
            int cut = text.IndexOf(" / ", StringComparison.Ordinal);
            return cut > 0 ? name + text.Substring(cut) : name;
        }

        /// <summary>Перевод для каждого языка, где он есть: ключ — тот вид текста (русский или английский), что пришёл в списке.</summary>
        static void Add(HashSet<string> shown, Dictionary<string, Dictionary<string, object>> byLang, Dictionary<string, string> names, params string[] variants)
        {
            foreach (string text in variants.Distinct())
            {
                if (string.IsNullOrEmpty(text) || !shown.Contains(text))
                {
                    continue;
                }
                foreach (string lang in NameLangs)
                {
                    string value;
                    if (names.TryGetValue(lang, out value) && !string.IsNullOrEmpty(value) && value != text)
                    {
                        byLang[lang][text] = value;
                    }
                }
            }
        }

        static void AddShown(HashSet<string> shown, Dictionary<string, object> item)
        {
            foreach (string field in new[] { "boss", "mount", "place" })
            {
                string text = J.Str(item, field);
                if (text.Length > 0)
                {
                    shown.Add(text);
                }
            }
        }

        static string Cell(object[] row, int c)
        {
            return c >= 0 && c < row.Length && row[c] is string ? ((string)row[c]).Trim() : "";
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
                    if (NotForAddon.Contains(kv.Key))
                    {
                        continue;
                    }
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
            // шрифты игры не рисуют стрелку: в окне и в чате был бы квадратик
            s = s.Replace("\u2192", ">");
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
                    if (J.Str(item, "src") == "reward")
                    {
                        continue;   // достижения и награды не считаем: их не убить
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

        /// <summary>Напоминание для меню, если на этой неделе в таблице ещё не было «Сброса недели».</summary>
        public static string WeekNote(Dictionary<string, object> status, IEnumerable<Dictionary<string, object>> records, DateTime utcNow)
        {
            return WeekWithoutReset(status, records, utcNow) > 0 ? Tr._("На этой неделе таблицу ещё не сбрасывали") : "";
        }

        /// <summary>
        /// Конец текущей недели, если на ней в таблице ещё не было «Сброса недели»; 0 — сброс был или неделя неизвестна.
        /// Неделю узнаём по меткам weekEnd в журнале аддона (сброс в игре — раз в 7 суток в одно время),
        /// сброс — по resetAt из таблицы или по записи «Сброс недели» этой недели в журнале.
        /// </summary>
        public static long WeekWithoutReset(Dictionary<string, object> status, IEnumerable<Dictionary<string, object>> records, DateTime utcNow)
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
                return 0;   // неделя неизвестна
            }
            long weekEnd = latest > now ? latest : latest + Week * ((now - latest) / Week + 1);
            long weekStart = weekEnd - Week;
            foreach (Dictionary<string, object> r in list)
            {
                if (J.Str(r, "kind") == "reset" && Math.Abs(J.Long(r, "weekEnd") - weekEnd) < 3600)
                {
                    return 0;
                }
            }
            long resetAt = J.Long(status, "resetAt");
            if (resetAt > 0 && resetAt >= weekStart - 3600)
            {
                return 0;
            }
            return weekEnd;
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
