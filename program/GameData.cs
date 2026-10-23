// Чтение файла сохранений аддона целиком (формат SavedVariables WoW — это код Lua с одной таблицей)
// и данные для новой таблицы: персонажи и коллекция маунтов (Collection.lua в аддоне 2.0).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    /// <summary>
    /// Разбор SavedVariables: «Имя = значение» верхнего уровня. Таблица Lua становится
    /// List&lt;object&gt; (если в ней только элементы 1..n) или Dictionary&lt;string, object&gt;
    /// (числовые ключи — строками). Строки — string, числа — double, true/false — bool.
    /// </summary>
    static class LuaData
    {
        public static Dictionary<string, object> ParseFile(string path)
        {
            byte[] bytes;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            return Parse(bytes);
        }

        public static Dictionary<string, object> Parse(string text)
        {
            return Parse(new UTF8Encoding(false).GetBytes(text));
        }

        public static Dictionary<string, object> Parse(byte[] bytes)
        {
            Reader r = new Reader(bytes);
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            r.SkipSpace();
            if (r.Pos + 2 < bytes.Length && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                r.Pos = 3;
            }
            while (true)
            {
                r.SkipSpace();
                if (r.AtEnd)
                {
                    break;
                }
                string name = r.Name();
                r.SkipSpace();
                r.Expect((byte)'=');
                result[name] = r.Value();
            }
            return result;
        }

        sealed class Reader
        {
            readonly byte[] b;
            public int Pos;

            public Reader(byte[] bytes)
            {
                b = bytes;
            }

            public bool AtEnd
            {
                get { return Pos >= b.Length; }
            }

            FormatException Error(string what)
            {
                return new FormatException("SavedVariables: " + what + " (byte " + Pos + ")");
            }

            public void Expect(byte c)
            {
                if (Pos >= b.Length || b[Pos] != c)
                {
                    throw Error("expected '" + (char)c + "'");
                }
                Pos++;
            }

            public void SkipSpace()
            {
                while (Pos < b.Length)
                {
                    byte c = b[Pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v')
                    {
                        Pos++;
                    }
                    else if (c == '-' && Pos + 1 < b.Length && b[Pos + 1] == '-')
                    {
                        Pos += 2;
                        int level = LongBracket();
                        if (level >= 0)
                        {
                            SkipLong(level);
                        }
                        else
                        {
                            while (Pos < b.Length && b[Pos] != '\n')
                            {
                                Pos++;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            /// <summary>[[ или [==[ на текущей позиции: уровень (число «=»), иначе -1. Позицию сдвигает за скобку.</summary>
            int LongBracket()
            {
                if (Pos >= b.Length || b[Pos] != '[')
                {
                    return -1;
                }
                int p = Pos + 1;
                int level = 0;
                while (p < b.Length && b[p] == '=')
                {
                    level++;
                    p++;
                }
                if (p < b.Length && b[p] == '[')
                {
                    Pos = p + 1;
                    return level;
                }
                return -1;
            }

            string SkipLong(int level)
            {
                int start = Pos;
                while (Pos < b.Length)
                {
                    if (b[Pos] == ']')
                    {
                        int p = Pos + 1;
                        int n = 0;
                        while (p < b.Length && b[p] == '=')
                        {
                            n++;
                            p++;
                        }
                        if (n == level && p < b.Length && b[p] == ']')
                        {
                            string s = Encoding.UTF8.GetString(b, start, Pos - start);
                            Pos = p + 1;
                            return s;
                        }
                    }
                    Pos++;
                }
                throw Error("unterminated long string");
            }

            static bool IsNameStart(byte c)
            {
                return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
            }

            static bool IsNameChar(byte c)
            {
                return IsNameStart(c) || (c >= '0' && c <= '9');
            }

            public string Name()
            {
                if (Pos >= b.Length || !IsNameStart(b[Pos]))
                {
                    throw Error("expected a name");
                }
                int start = Pos;
                while (Pos < b.Length && IsNameChar(b[Pos]))
                {
                    Pos++;
                }
                return Encoding.ASCII.GetString(b, start, Pos - start);
            }

            public object Value()
            {
                SkipSpace();
                if (Pos >= b.Length)
                {
                    throw Error("expected a value");
                }
                byte c = b[Pos];
                if (c == '"' || c == '\'')
                {
                    return QuotedString();
                }
                if (c == '{')
                {
                    return Table();
                }
                if (c == '[')
                {
                    int level = LongBracket();
                    if (level >= 0)
                    {
                        return SkipLong(level);
                    }
                    throw Error("unexpected '['");
                }
                if (c == '-' || c == '.' || (c >= '0' && c <= '9'))
                {
                    return Number();
                }
                if (IsNameStart(c))
                {
                    string word = Name();
                    if (word == "true")
                    {
                        return true;
                    }
                    if (word == "false")
                    {
                        return false;
                    }
                    if (word == "nil")
                    {
                        return null;
                    }
                    if (word == "inf" || word == "math")
                    {
                        // math.huge и подобное — в данных аддона не встречается
                        throw Error("unsupported value " + word);
                    }
                    throw Error("unknown word " + word);
                }
                throw Error("unexpected character");
            }

            object Number()
            {
                int start = Pos;
                if (b[Pos] == '-')
                {
                    Pos++;
                }
                if (Pos + 1 < b.Length && b[Pos] == '0' && (b[Pos + 1] == 'x' || b[Pos + 1] == 'X'))
                {
                    Pos += 2;
                    int hs = Pos;
                    while (Pos < b.Length && Uri.IsHexDigit((char)b[Pos]))
                    {
                        Pos++;
                    }
                    double hex = (double)long.Parse(Encoding.ASCII.GetString(b, hs, Pos - hs), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return b[start] == '-' ? -hex : hex;
                }
                while (Pos < b.Length)
                {
                    byte c = b[Pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E'
                        || ((c == '+' || c == '-') && (b[Pos - 1] == 'e' || b[Pos - 1] == 'E')))
                    {
                        Pos++;
                    }
                    else
                    {
                        break;
                    }
                }
                string s = Encoding.ASCII.GetString(b, start, Pos - start);
                double d;
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    throw Error("bad number " + s);
                }
                return d;
            }

            string QuotedString()
            {
                byte quote = b[Pos++];
                List<byte> buf = new List<byte>();
                while (true)
                {
                    if (Pos >= b.Length)
                    {
                        throw Error("unterminated string");
                    }
                    byte c = b[Pos++];
                    if (c == quote)
                    {
                        break;
                    }
                    if (c != '\\')
                    {
                        buf.Add(c);
                        continue;
                    }
                    if (Pos >= b.Length)
                    {
                        throw Error("unterminated string");
                    }
                    byte e = b[Pos++];
                    switch ((char)e)
                    {
                        case 'n': buf.Add((byte)'\n'); break;
                        case 't': buf.Add((byte)'\t'); break;
                        case 'r': buf.Add((byte)'\r'); break;
                        case 'a': buf.Add(7); break;
                        case 'b': buf.Add(8); break;
                        case 'f': buf.Add(12); break;
                        case 'v': buf.Add(11); break;
                        case '\n': buf.Add((byte)'\n'); break;
                        case '\r':
                            buf.Add((byte)'\n');
                            if (Pos < b.Length && b[Pos] == '\n')
                            {
                                Pos++;
                            }
                            break;
                        default:
                            if (e >= '0' && e <= '9')
                            {
                                int code = e - '0';
                                for (int i = 0; i < 2 && Pos < b.Length && b[Pos] >= '0' && b[Pos] <= '9'; i++)
                                {
                                    code = code * 10 + (b[Pos++] - '0');
                                }
                                buf.Add((byte)(code & 0xFF));
                            }
                            else
                            {
                                buf.Add(e);   // \\ \" \' и прочее — сам символ
                            }
                            break;
                    }
                }
                return Encoding.UTF8.GetString(buf.ToArray());
            }

            object Table()
            {
                Expect((byte)'{');
                Dictionary<string, object> hash = new Dictionary<string, object>(StringComparer.Ordinal);
                List<object> array = new List<object>();
                bool onlyArray = true;
                while (true)
                {
                    SkipSpace();
                    if (Pos >= b.Length)
                    {
                        throw Error("unterminated table");
                    }
                    if (b[Pos] == '}')
                    {
                        Pos++;
                        break;
                    }
                    string key = null;
                    if (b[Pos] == '[' && LongBracketAhead() < 0)
                    {
                        Pos++;
                        object k = Value();
                        SkipSpace();
                        Expect((byte)']');
                        SkipSpace();
                        Expect((byte)'=');
                        key = KeyText(k);
                    }
                    else if (IsNameStart(b[Pos]) && NameFollowedByEquals())
                    {
                        key = Name();
                        SkipSpace();
                        Expect((byte)'=');
                    }
                    object v = Value();
                    if (key == null)
                    {
                        array.Add(v);
                    }
                    else
                    {
                        double n;
                        if (onlyArray && double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                            && n == array.Count + 1)
                        {
                            array.Add(v);
                        }
                        else
                        {
                            onlyArray = false;
                            if (v != null)
                            {
                                hash[key] = v;
                            }
                        }
                    }
                    SkipSpace();
                    if (Pos < b.Length && (b[Pos] == ',' || b[Pos] == ';'))
                    {
                        Pos++;
                    }
                }
                if (onlyArray)
                {
                    return array;
                }
                for (int i = 0; i < array.Count; i++)
                {
                    string k = (i + 1).ToString(CultureInfo.InvariantCulture);
                    if (!hash.ContainsKey(k) && array[i] != null)
                    {
                        hash[k] = array[i];
                    }
                }
                return hash;
            }

            int LongBracketAhead()
            {
                int save = Pos;
                int level = LongBracket();
                Pos = save;
                return level;
            }

            bool NameFollowedByEquals()
            {
                int save = Pos;
                Name();
                SkipSpace();
                bool yes = Pos < b.Length && b[Pos] == '=' && !(Pos + 1 < b.Length && b[Pos + 1] == '=');
                Pos = save;
                return yes;
            }

            static string KeyText(object k)
            {
                if (k is double)
                {
                    double d = (double)k;
                    if (Math.Floor(d) == d && Math.Abs(d) < 1e15)
                    {
                        return ((long)d).ToString(CultureInfo.InvariantCulture);
                    }
                    return d.ToString("R", CultureInfo.InvariantCulture);
                }
                if (k is bool)
                {
                    return (bool)k ? "true" : "false";
                }
                return Convert.ToString(k, CultureInfo.InvariantCulture) ?? "";
            }
        }
    }

    /// <summary>Персонаж, запомненный аддоном.</summary>
    sealed class GameCharacter
    {
        public string Name = "";
        public string Realm = "";
        public string Class = "";
        public string ClassFile = "";
        public int Level;
        public long Seen;
        /// <summary>Известен только по журналу убийств (аддон старше 2.0).</summary>
        public bool FromKills;

        public string Key
        {
            get { return Name + "-" + Realm; }
        }
    }

    /// <summary>Маунт из журнала маунтов: имя, откуда он (текст источника) и собран ли.</summary>
    sealed class GameDrop
    {
        public long Id;
        public string Name = "";
        public string From = "";
        /// <summary>sourceType журнала: 1 — добыча, 0 — неизвестно (данные аддона 2.0.0 — только добыча).</summary>
        public int Type;
        public bool Collected;
    }

    /// <summary>Бой из журнала приключений (аддон 2.4+).</summary>
    sealed class JournalEncounter
    {
        /// <summary>dungeonEncounterID — как в ENCOUNTER_END и в записях убийств.</summary>
        public long EncounterId;
        public long JournalId;
        public long InstanceId;
        public string Name = "";
    }

    /// <summary>Подземелье или рейд из журнала приключений.</summary>
    sealed class JournalInstance
    {
        public long Id;
        /// <summary>instance mapID (как в catalog.json, «ID места»).</summary>
        public long Map;
        public bool Raid;
        public string Name = "";
    }

    /// <summary>Всё, что аддоны всех WoW-аккаунтов знают о персонажах и коллекции.</summary>
    sealed class GameSnapshot
    {
        public List<GameCharacter> Characters = new List<GameCharacter>();
        public List<string> CollectedMounts = new List<string>();
        /// <summary>mountID собранных маунтов.</summary>
        public HashSet<long> CollectedIds = new HashSet<long>();
        /// <summary>Весь журнал маунтов (аддон 2.1+) или только маунты-добыча (аддон 2.0.0).</summary>
        public List<GameDrop> Mounts = new List<GameDrop>();
        /// <summary>Только маунты-добыча (sourceType 1).</summary>
        public List<GameDrop> Drops = new List<GameDrop>();
        /// <summary>Когда аддон последний раз читал журнал маунтов (0 — ни разу: аддон старше 2.0 или не было входа).</summary>
        public long CollectionUpdated;
        public int JournalSize;
        /// <summary>GetLocale() клиента, на котором читался журнал маунтов (аддон 2.4+; "" — неизвестно).</summary>
        public string CollectionLocale = "";
        /// <summary>Журнал приключений: язык, бои и подземелья (аддон 2.4+).</summary>
        public string JournalLocale = "";
        public long JournalUpdated;
        public List<JournalEncounter> JournalEncounters = new List<JournalEncounter>();
        public List<JournalInstance> JournalInstances = new List<JournalInstance>();
        public List<string> Problems = new List<string>();
    }

    static class GameData
    {
        public static GameSnapshot Read(IEnumerable<string> files)
        {
            GameSnapshot snap = new GameSnapshot();
            Dictionary<string, GameCharacter> chars = new Dictionary<string, GameCharacter>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> mounts = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<long, GameDrop> drops = new Dictionary<long, GameDrop>();
            foreach (string f in files)
            {
                Dictionary<string, object> root;
                try
                {
                    root = LuaData.ParseFile(f);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is FormatException))
                    {
                        throw;
                    }
                    snap.Problems.Add(f + ": " + ex.Message);
                    Log.Write(Tr.L("Не удалось прочитать {0}: {1}", f, ex.Message));
                    continue;
                }
                Dictionary<string, object> db = J.Obj(J.Get(root, "MountSheetSyncDB"));
                if (db == null)
                {
                    continue;
                }
                AddCharacters(chars, J.Get(db, "chars"));
                AddFromKills(chars, J.Get(db, "kills"));
                Dictionary<string, object> col = J.Obj(J.Get(db, "collection"));
                if (col != null)
                {
                    foreach (object n in Items(J.Get(col, "names")))
                    {
                        string name = (n as string ?? "").Trim();
                        if (name.Length > 0)
                        {
                            mounts.Add(name);
                        }
                    }
                    foreach (object o in Items(J.Get(col, "ids")))
                    {
                        long id = AsLong(o);
                        if (id > 0)
                        {
                            snap.CollectedIds.Add(id);
                        }
                    }
                    // аддон 2.1+: весь журнал (mounts); 2.0.0: только добыча (drops)
                    bool full = J.Get(col, "mounts") != null;
                    foreach (object o in Items(J.Get(col, full ? "mounts" : "drops")))
                    {
                        Dictionary<string, object> d = J.Obj(o);
                        if (d == null)
                        {
                            continue;
                        }
                        long id = J.Long(d, "id");
                        string name = J.Str(d, "name").Trim();
                        if (id <= 0 || name.Length == 0)
                        {
                            continue;
                        }
                        GameDrop drop;
                        if (!drops.TryGetValue(id, out drop))
                        {
                            drop = new GameDrop();
                            drop.Id = id;
                            drop.Name = name;
                            drops[id] = drop;
                        }
                        if (drop.From.Length == 0)
                        {
                            drop.From = J.Str(d, "from").Trim();
                        }
                        int type = full ? (int)J.Long(d, "type") : 1;
                        if (drop.Type == 0)
                        {
                            drop.Type = type;
                        }
                        drop.Collected = drop.Collected || J.Truthy(d, "got");
                        if (J.Truthy(d, "got"))
                        {
                            snap.CollectedIds.Add(id);
                        }
                    }
                    long updated = J.Long(col, "updated");
                    if (updated > snap.CollectionUpdated)
                    {
                        snap.CollectionUpdated = updated;
                        snap.JournalSize = (int)J.Long(col, "journal");
                        snap.CollectionLocale = J.Str(col, "locale").Trim();
                    }
                }
                ReadJournal(snap, J.Obj(J.Get(db, "journal")));
            }
            snap.Characters = chars.Values
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.Realm, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            snap.CollectedMounts = mounts.OrderBy(m => m, StringComparer.Ordinal).ToList();
            snap.Mounts = drops.Values.OrderBy(d => d.Id).ToList();
            foreach (GameDrop d in snap.Mounts)
            {
                if (snap.CollectedIds.Contains(d.Id))
                {
                    d.Collected = true;
                }
            }
            snap.Drops = snap.Mounts.Where(d => d.Type == 1).ToList();
            Encounters.Use(GameNames.From(snap));
            return snap;
        }

        /// <summary>db.journal (аддон 2.4+): берём самый свежий из всех аккаунтов.</summary>
        static void ReadJournal(GameSnapshot snap, Dictionary<string, object> journal)
        {
            if (journal == null)
            {
                return;
            }
            long updated = J.Long(journal, "updated");
            List<JournalEncounter> encounters = new List<JournalEncounter>();
            foreach (object o in Items(J.Get(journal, "encounters")))
            {
                Dictionary<string, object> e = J.Obj(o);
                if (e == null)
                {
                    continue;
                }
                JournalEncounter je = new JournalEncounter();
                je.EncounterId = J.Long(e, "id");
                je.JournalId = J.Long(e, "j");
                je.InstanceId = J.Long(e, "i");
                je.Name = J.Str(e, "name").Trim();
                if (je.Name.Length > 0 && (je.EncounterId > 0 || je.JournalId > 0))
                {
                    encounters.Add(je);
                }
            }
            if (encounters.Count == 0 || (snap.JournalEncounters.Count > 0 && updated <= snap.JournalUpdated))
            {
                return;
            }
            snap.JournalUpdated = updated;
            snap.JournalLocale = J.Str(journal, "locale").Trim();
            snap.JournalEncounters = encounters;
            snap.JournalInstances = new List<JournalInstance>();
            foreach (object o in Items(J.Get(journal, "instances")))
            {
                Dictionary<string, object> i = J.Obj(o);
                if (i == null)
                {
                    continue;
                }
                JournalInstance ji = new JournalInstance();
                ji.Id = J.Long(i, "id");
                ji.Map = J.Long(i, "map");
                ji.Raid = J.Truthy(i, "raid");
                ji.Name = J.Str(i, "name").Trim();
                if (ji.Name.Length > 0)
                {
                    snap.JournalInstances.Add(ji);
                }
            }
        }

        static long AsLong(object o)
        {
            if (o is double)
            {
                return (long)(double)o;
            }
            if (o is long)
            {
                return (long)o;
            }
            long n;
            return long.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        /// <summary>Элементы таблицы-массива (или значения таблицы с числовыми ключами) по порядку.</summary>
        static IEnumerable<object> Items(object table)
        {
            List<object> list = table as List<object>;
            if (list != null)
            {
                return list;
            }
            Dictionary<string, object> d = table as Dictionary<string, object>;
            if (d == null)
            {
                return new object[0];
            }
            List<KeyValuePair<double, object>> numbered = new List<KeyValuePair<double, object>>();
            foreach (KeyValuePair<string, object> kv in d)
            {
                double n;
                if (double.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out n))
                {
                    numbered.Add(new KeyValuePair<double, object>(n, kv.Value));
                }
            }
            return numbered.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }

        static void AddCharacters(Dictionary<string, GameCharacter> chars, object table)
        {
            Dictionary<string, object> d = table as Dictionary<string, object>;
            if (d == null)
            {
                return;
            }
            foreach (KeyValuePair<string, object> kv in d)
            {
                Dictionary<string, object> c = J.Obj(kv.Value);
                if (c == null)
                {
                    continue;
                }
                GameCharacter ch = new GameCharacter();
                ch.Name = J.Str(c, "name").Trim();
                ch.Realm = J.Str(c, "realm").Trim();
                if (ch.Name.Length == 0)
                {
                    continue;
                }
                ch.Class = J.Str(c, "class").Trim();
                ch.ClassFile = J.Str(c, "classFile").Trim();
                ch.Level = (int)J.Long(c, "level");
                ch.Seen = J.Long(c, "seen");
                GameCharacter old;
                if (!chars.TryGetValue(ch.Key, out old) || old.FromKills || old.Seen < ch.Seen)
                {
                    chars[ch.Key] = ch;
                }
            }
        }

        /// <summary>Аддон старше 2.0 персонажей не запоминал — берём их из журнала убийств.</summary>
        static void AddFromKills(Dictionary<string, GameCharacter> chars, object table)
        {
            foreach (object o in Items(table))
            {
                Dictionary<string, object> r = J.Obj(o);
                if (r == null)
                {
                    continue;
                }
                string name = J.Str(r, "char").Trim();
                string realm = J.Str(r, "realm").Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                string key = name + "-" + realm;
                if (chars.ContainsKey(key))
                {
                    continue;
                }
                // тот же персонаж, но без сервера в записи
                if (chars.Values.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                    && (realm.Length == 0 || string.Equals(c.Realm, realm, StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }
                GameCharacter ch = new GameCharacter();
                ch.Name = name;
                ch.Realm = realm;
                ch.Seen = J.Long(r, "time");
                ch.FromKills = true;
                chars[key] = ch;
            }
        }
    }
}
