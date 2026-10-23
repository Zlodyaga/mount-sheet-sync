// Язык таблицы: названия листов, заголовки, «Да»/«Нет», статусы и другие значения списков на шести языках
// (sheet-i18n.json — его собирает tools/i18n/build_sheet.py). Внутри программы таблица всегда «русская»:
// GoogleBook при чтении переводит названия листов, заголовки и значения списков на русский, а при записи —
// обратно на язык таблицы. Поэтому SheetLogic, CollectionSync, SheetUpgrade и SheetCreator работают с таблицей
// на любом языке так же, как с русской. Тексты каталога (боссы, советы, скипы) для нерусских таблиц — английские;
// имена маунтов, боссов и подземелий — из игры, если игра на языке таблицы (журнал маунтов и журнал приключений,
// которые выписывает аддон), иначе английские.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    sealed class SheetLang
    {
        public static readonly string[] Codes = { "ru", "en", "de", "fr", "es", "pt" };

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "ru", "Русский" }, { "en", "English" }, { "de", "Deutsch" },
            { "fr", "Français" }, { "es", "Español" }, { "pt", "Português (Brasil)" },
        };

        /// <summary>Колонки, значения которых — слова из списков (их переводим и при чтении).</summary>
        public static readonly HashSet<string> WordColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            SheetBuilder.Collected, SheetBuilder.Attempted, "Статус", "Скип есть?", "Тип источника", "Сброс / частота",
            "Шанс выпадения",
        };

        static readonly string[] Masters = { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet, SheetBuilder.WorldSheet, SheetBuilder.SummarySheet };
        static readonly string[] Prefixes = { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet };
        public const string DefaultTitle = "Маунты WoW";

        public readonly string Code;
        public readonly string Locale;
        readonly Dictionary<string, string> to = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> from = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> patterns = new Dictionary<string, string>(StringComparer.Ordinal);

        SheetLang(string code, Texts t)
        {
            Code = code;
            string locale;
            Locale = t.Locales.TryGetValue(code, out locale) ? locale : "ru_RU";
            if (code == "ru")
            {
                return;
            }
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in t.Words)
            {
                string v;
                if (kv.Value.TryGetValue(code, out v) && v.Length > 0)
                {
                    to[kv.Key] = v;
                    if (!from.ContainsKey(v))
                    {
                        from[v] = kv.Key;
                    }
                }
            }
            foreach (KeyValuePair<string, Dictionary<string, string>> kv in t.Patterns)
            {
                string v;
                if (kv.Value.TryGetValue(code, out v) && v.Length > 0)
                {
                    patterns[kv.Key] = v;
                }
            }
        }

        public bool IsRussian
        {
            get { return Code == "ru"; }
        }

        public string Name
        {
            get { return Names[Code]; }
        }

        // ------------------------------------------------------------------
        // Тексты (sheet-i18n.json)
        // ------------------------------------------------------------------

        sealed class Texts
        {
            public Dictionary<string, string> Locales = new Dictionary<string, string>();
            public Dictionary<string, Dictionary<string, string>> Words = new Dictionary<string, Dictionary<string, string>>();
            public Dictionary<string, Dictionary<string, string>> Patterns = new Dictionary<string, Dictionary<string, string>>();
            public Dictionary<string, string> Content = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<long, string> Mounts = new Dictionary<long, string>();
        }

        static Texts texts;
        static readonly Dictionary<string, SheetLang> cache = new Dictionary<string, SheetLang>();
        static readonly object sync = new object();

        static Texts Load()
        {
            if (texts != null)
            {
                return texts;
            }
            Texts t = new Texts();
            using (Stream s = typeof(SheetLang).Assembly.GetManifestResourceStream("MountSheetSync.sheet-i18n.json"))
            {
                if (s == null)
                {
                    throw new InvalidOperationException("в программе нет переводов таблицы (sheet-i18n.json)");
                }
                using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                {
                    Dictionary<string, object> root = (Dictionary<string, object>)Json.Parse(reader.ReadToEnd());
                    foreach (KeyValuePair<string, object> kv in J.Obj(J.Get(root, "locales")) ?? new Dictionary<string, object>())
                    {
                        t.Locales[kv.Key] = SheetLogic.JsString(kv.Value);
                    }
                    t.Words = Table(J.Obj(J.Get(root, "words")));
                    t.Patterns = Table(J.Obj(J.Get(root, "patterns")));
                    foreach (KeyValuePair<string, object> kv in J.Obj(J.Get(root, "content")) ?? new Dictionary<string, object>())
                    {
                        t.Content[kv.Key] = SheetLogic.JsString(kv.Value);
                    }
                    foreach (KeyValuePair<string, object> kv in J.Obj(J.Get(root, "mounts")) ?? new Dictionary<string, object>())
                    {
                        long id;
                        if (long.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        {
                            t.Mounts[id] = SheetLogic.JsString(kv.Value);
                        }
                    }
                }
            }
            texts = t;
            return t;
        }

        static Dictionary<string, Dictionary<string, string>> Table(Dictionary<string, object> d)
        {
            Dictionary<string, Dictionary<string, string>> table = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (d == null)
            {
                return table;
            }
            foreach (KeyValuePair<string, object> kv in d)
            {
                Dictionary<string, string> tr = new Dictionary<string, string>();
                foreach (KeyValuePair<string, object> l in J.Obj(kv.Value) ?? new Dictionary<string, object>())
                {
                    tr[l.Key] = SheetLogic.JsString(l.Value);
                }
                table[kv.Key] = tr;
            }
            return table;
        }

        /// <summary>Язык по коду ("ru", "en", …); неизвестный — русский.</summary>
        public static SheetLang Get(string code)
        {
            code = Normalize(code);
            lock (sync)
            {
                SheetLang l;
                if (!cache.TryGetValue(code, out l))
                {
                    l = new SheetLang(code, Load());
                    cache[code] = l;
                }
                return l;
            }
        }

        public static SheetLang Russian
        {
            get { return Get("ru"); }
        }

        public static string Normalize(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            return Codes.Contains(code) ? code : "ru";
        }

        /// <summary>Язык игры по GetLocale(): ruRU → ru, enGB → en, esMX → es, ptBR → pt; другой — "".</summary>
        public static string FromGameLocale(string locale)
        {
            string two = (locale ?? "").Trim();
            two = two.Length >= 2 ? two.Substring(0, 2).ToLowerInvariant() : "";
            return Codes.Contains(two) ? two : "";
        }

        /// <summary>Язык новой таблицы по умолчанию: язык игры (если он есть среди языков таблицы), иначе язык программы.</summary>
        public static SheetLang DefaultFor(GameSnapshot game)
        {
            string gameLang = game == null ? ""
                : FromGameLocale(game.JournalLocale.Length > 0 ? game.JournalLocale : game.CollectionLocale);
            return Get(gameLang.Length > 0 ? gameLang : Tr.Lang);
        }

        /// <summary>
        /// Язык таблицы: из метки программы (таблицы 2.4+), иначе по названиям общих листов; таблица прошлых версий
        /// и чужая таблица — русские.
        /// </summary>
        public static SheetLang Detect(string metaLang, IEnumerable<string> sheetNames)
        {
            if (!string.IsNullOrEmpty(metaLang))
            {
                return Get(metaLang);
            }
            List<string> names = (sheetNames ?? new string[0]).Select(n => (n ?? "").Trim()).ToList();
            if (names.Any(n => Masters.Contains(n)))
            {
                return Russian;
            }
            SheetLang best = Russian;
            int bestCount = 1;
            foreach (string code in Codes.Where(c => c != "ru"))
            {
                SheetLang l = Get(code);
                int count = Masters.Count(m => names.Any(n => string.Equals(n, l.Word(m), StringComparison.OrdinalIgnoreCase)));
                if (count > bestCount)
                {
                    best = l;
                    bestCount = count;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Слова и названия листов
        // ------------------------------------------------------------------

        /// <summary>Русское слово списка → язык таблицы (не слово — как есть).</summary>
        public string Word(string ru)
        {
            string v;
            return ru != null && to.TryGetValue(ru, out v) ? v : ru;
        }

        /// <summary>Слово на языке таблицы → русское (не слово — как есть).</summary>
        public string WordToRu(string text)
        {
            if (IsRussian || text == null)
            {
                return text;
            }
            string v;
            return from.TryGetValue(text.Trim(), out v) ? v : text;
        }

        public string SheetName(string ru)
        {
            if (IsRussian || string.IsNullOrEmpty(ru))
            {
                return ru;
            }
            if (Masters.Contains(ru))
            {
                return Word(ru);
            }
            foreach (string p in Prefixes)
            {
                if (ru.StartsWith(p + " ", StringComparison.Ordinal))
                {
                    return Word(p) + ru.Substring(p.Length);
                }
            }
            return ru;
        }

        public string SheetNameToRu(string name)
        {
            if (IsRussian || string.IsNullOrEmpty(name))
            {
                return name;
            }
            string trimmed = name.Trim();
            foreach (string m in Masters)
            {
                if (string.Equals(trimmed, Word(m), StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            foreach (string p in Prefixes)
            {
                string local = Word(p);
                if (trimmed.Length > local.Length && trimmed.StartsWith(local, StringComparison.OrdinalIgnoreCase)
                    && char.IsWhiteSpace(trimmed[local.Length]))
                {
                    return p + trimmed.Substring(local.Length);
                }
            }
            return name;
        }

        /// <summary>Текст каталога (босс, совет, скип…): для нерусской таблицы — английский перевод.</summary>
        public string Content(string ru)
        {
            if (IsRussian || ru == null)
            {
                return ru;
            }
            string v;
            return Load().Content.TryGetValue(ru, out v) ? v : ru;
        }

        /// <summary>Английское название маунта по mountID (null — неизвестно).</summary>
        public static string EnglishMount(long id)
        {
            string v;
            return id > 0 && Load().Mounts.TryGetValue(id, out v) ? v : null;
        }

        /// <summary>Название новой таблицы по умолчанию.</summary>
        public string Title
        {
            get { return Word(DefaultTitle); }
        }

        // ------------------------------------------------------------------
        // Запись в таблицу
        // ------------------------------------------------------------------

        /// <summary>Строки в "…" и имена листов в '…' — на язык таблицы.</summary>
        public string Formula(string f)
        {
            if (IsRussian || string.IsNullOrEmpty(f))
            {
                return f;
            }
            StringBuilder sb = new StringBuilder(f.Length + 16);
            int i = 0;
            while (i < f.Length)
            {
                char ch = f[i];
                if (ch != '"' && ch != '\'')
                {
                    sb.Append(ch);
                    i++;
                    continue;
                }
                // до закрывающей кавычки; удвоенная кавычка — это кавычка внутри
                StringBuilder inner = new StringBuilder();
                int j = i + 1;
                while (j < f.Length)
                {
                    if (f[j] == ch)
                    {
                        if (j + 1 < f.Length && f[j + 1] == ch)
                        {
                            inner.Append(ch);
                            j += 2;
                            continue;
                        }
                        break;
                    }
                    inner.Append(f[j]);
                    j++;
                }
                string text = ch == '"' ? Literal(inner.ToString()) : SheetName(inner.ToString());
                string q = ch.ToString();
                sb.Append(q).Append(text.Replace(q, q + q)).Append(q);
                i = j + 1;
            }
            return sb.ToString();
        }

        /// <summary>"Да", "&lt;&gt;Недоступ*", "Недоступ" (часть статуса) → язык таблицы.</summary>
        string Literal(string s)
        {
            string prefix = s.StartsWith("<>", StringComparison.Ordinal) ? "<>" : "";
            string core = s.Substring(prefix.Length);
            string suffix = core.EndsWith("*", StringComparison.Ordinal) ? "*" : "";
            core = core.Substring(0, core.Length - suffix.Length);
            string v;
            if (patterns.TryGetValue(core, out v))
            {
                return prefix + v + suffix;
            }
            return prefix + Word(core) + suffix;
        }

        /// <summary>Значение ячейки при записи (values.batchUpdate).</summary>
        public object ValueOut(object value)
        {
            string s = value as string;
            if (IsRussian || s == null)
            {
                return value;
            }
            return s.StartsWith("=", StringComparison.Ordinal) ? Formula(s) : Word(s);
        }

        /// <summary>Запросы spreadsheets.batchUpdate: тексты ячеек, формулы, списки, фильтры, названия листов.</summary>
        public void Outgoing(object node)
        {
            if (IsRussian)
            {
                return;
            }
            Walk(node);
        }

        void Walk(object node)
        {
            Dictionary<string, object> d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (string key in d.Keys.ToList())
                {
                    object v = d[key];
                    string s = v as string;
                    if (s != null)
                    {
                        if (key == "stringValue")
                        {
                            d[key] = Word(s);
                        }
                        else if (key == "formulaValue")
                        {
                            d[key] = Formula(s);
                        }
                        else if (key == "userEnteredValue")
                        {
                            d[key] = s.StartsWith("=", StringComparison.Ordinal) ? Formula(s) : Word(s);
                        }
                        else if (key == "title")
                        {
                            d[key] = SheetName(s);
                        }
                        continue;
                    }
                    if (key == "hiddenValues")
                    {
                        IList list = v as IList;
                        if (list != null)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                string h = list[i] as string;
                                if (h != null)
                                {
                                    list[i] = Word(h);
                                }
                            }
                        }
                        continue;
                    }
                    Walk(v);
                }
                return;
            }
            IList items = node as IList;
            if (items != null)
            {
                foreach (object o in items)
                {
                    Walk(o);
                }
            }
        }

        /// <summary>Ответ аддону и окнам программы: названия листов («sheet», «sheets») — как в таблице.</summary>
        public void Response(object node)
        {
            if (IsRussian)
            {
                return;
            }
            Dictionary<string, object> d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (string key in d.Keys.ToList())
                {
                    object v = d[key];
                    if (key == "sheet" && v is string)
                    {
                        d[key] = SheetName((string)v);
                    }
                    else if (key == "sheets" && v is IList)
                    {
                        IList list = (IList)v;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] is string)
                            {
                                list[i] = SheetName((string)list[i]);
                            }
                        }
                    }
                    else
                    {
                        Response(v);
                    }
                }
                return;
            }
            IList items = node as IList;
            if (items != null)
            {
                foreach (object o in items)
                {
                    Response(o);
                }
            }
        }

        // ------------------------------------------------------------------
        // Чтение таблицы
        // ------------------------------------------------------------------

        /// <summary>Лист после чтения: заголовки и значения списков — по-русски (остальные ячейки не трогаем).</summary>
        public void ToRussian(SheetEntry entry)
        {
            if (IsRussian || entry.Data.Count == 0)
            {
                return;
            }
            object[] header = entry.Data[0];
            List<int> wordCols = new List<int>();
            for (int c = 0; c < header.Length; c++)
            {
                string h = header[c] as string;
                if (h == null)
                {
                    continue;
                }
                string ru = WordToRu(h);
                header[c] = ru;
                if (WordColumns.Contains(ru))
                {
                    wordCols.Add(c);
                }
            }
            for (int r = 1; r < entry.Data.Count; r++)
            {
                object[] row = entry.Data[r];
                foreach (int c in wordCols)
                {
                    string s = c < row.Length ? row[c] as string : null;
                    if (s != null && s.Length > 0)
                    {
                        row[c] = WordToRu(s);
                    }
                }
            }
        }
    }

    /// <summary>Имена из игры (их выписывает аддон): маунты по mountID, боссы и подземелья из журнала приключений.</summary>
    sealed class GameNames
    {
        /// <summary>Язык журнала маунтов ("ru", "en", …; "" — неизвестен).</summary>
        public string MountLang = "";
        /// <summary>Язык журнала приключений.</summary>
        public string JournalLang = "";
        public Dictionary<long, string> Mounts = new Dictionary<long, string>();
        /// <summary>journalEncounterID → имя босса.</summary>
        public Dictionary<long, string> Bosses = new Dictionary<long, string>();
        /// <summary>journalEncounterID → dungeonEncounterID.</summary>
        public Dictionary<long, long> EncounterIds = new Dictionary<long, long>();
        /// <summary>Карта подземелья (instance mapID) → название.</summary>
        public Dictionary<long, string> Instances = new Dictionary<long, string>();

        public static GameNames From(GameSnapshot game)
        {
            GameNames n = new GameNames();
            if (game == null)
            {
                return n;
            }
            n.MountLang = SheetLang.FromGameLocale(game.CollectionLocale);
            foreach (GameDrop d in game.Mounts)
            {
                if (d.Id > 0 && d.Name.Length > 0)
                {
                    n.Mounts[d.Id] = d.Name;
                }
            }
            n.JournalLang = SheetLang.FromGameLocale(game.JournalLocale);
            foreach (JournalEncounter e in game.JournalEncounters)
            {
                if (e.JournalId > 0 && e.Name.Length > 0 && !n.Bosses.ContainsKey(e.JournalId))
                {
                    n.Bosses[e.JournalId] = e.Name;
                    if (e.EncounterId > 0)
                    {
                        n.EncounterIds[e.JournalId] = e.EncounterId;
                    }
                }
            }
            foreach (JournalInstance i in game.JournalInstances)
            {
                if (i.Map > 0 && i.Name.Length > 0 && !n.Instances.ContainsKey(i.Map))
                {
                    n.Instances[i.Map] = i.Name;
                }
            }
            return n;
        }

        /// <summary>Язык журнала приключений — язык таблицы (боссы и подземелья можно взять из игры).</summary>
        public bool JournalIn(SheetLang lang)
        {
            return JournalLang.Length > 0 && JournalLang == lang.Code && Bosses.Count > 0;
        }

        public bool MountsIn(SheetLang lang)
        {
            return MountLang.Length > 0 && MountLang == lang.Code && Mounts.Count > 0;
        }
    }

    /// <summary>
    /// Боссы по номеру боя (dungeonEncounterID — его дают ENCOUNTER_END и BOSS_KILL): имена из каталога
    /// (русское и английское) и из журнала приключений игры. Так убийство находит строку таблицы, даже если
    /// игра называет босса иначе, чем таблица (другой язык, «Галеон» и «Галлеон»).
    /// </summary>
    static class Encounters
    {
        sealed class Index
        {
            public Dictionary<long, List<string>> Names = new Dictionary<long, List<string>>();
        }

        static Index index;
        static GameNames lastGame;

        /// <summary>Имена из журнала приключений последнего снимка игры.</summary>
        public static void Use(GameNames game)
        {
            lastGame = game;
            index = null;
        }

        static Index Current()
        {
            Index i = index;
            if (i != null)
            {
                return i;
            }
            i = Build(Catalog.Embedded(), lastGame);
            index = i;
            return i;
        }

        static Index Build(Catalog catalog, GameNames game)
        {
            Index i = new Index();
            SheetLang en = SheetLang.Get("en");
            foreach (CatalogSheet sheet in new[] { catalog.Raids, catalog.Dungeons, catalog.World })
            {
                int cBoss = sheet.Columns.FindIndex(c => c.StartsWith("Босс", StringComparison.Ordinal));
                int cEnc = sheet.Col("ID босса");
                int cJournal = sheet.Col("ID в журнале");
                if (cBoss < 0)
                {
                    continue;
                }
                foreach (object[] row in sheet.Rows)
                {
                    List<long> ids = Ids(cEnc >= 0 ? row[cEnc] : null);
                    List<long> journal = Ids(cJournal >= 0 ? row[cJournal] : null);
                    List<string> names = new List<string>();
                    string ru = SheetLogic.CellText(row[cBoss]);
                    // «Король Дазар / Сундук претендента»: номер боя — у первого имени
                    names.Add(First(ru));
                    names.Add(First(en.Content(ru)));
                    if (game != null)
                    {
                        foreach (long j in journal)
                        {
                            string local;
                            long enc;
                            if (game.Bosses.TryGetValue(j, out local))
                            {
                                names.Add(local);
                            }
                            if (game.EncounterIds.TryGetValue(j, out enc) && !ids.Contains(enc))
                            {
                                ids.Add(enc);
                            }
                        }
                    }
                    foreach (long id in ids)
                    {
                        List<string> list;
                        if (!i.Names.TryGetValue(id, out list))
                        {
                            list = new List<string>();
                            i.Names[id] = list;
                        }
                        foreach (string n in names)
                        {
                            if (n.Length > 0 && !list.Contains(n))
                            {
                                list.Add(n);
                            }
                        }
                    }
                }
            }
            return i;
        }

        static string First(string text)
        {
            return (text ?? "").Split('/')[0].Trim();
        }

        public static List<long> Ids(object cell)
        {
            List<long> ids = new List<long>();
            if (cell is double)
            {
                long one = (long)(double)cell;
                if (one > 0)
                {
                    ids.Add(one);
                }
                return ids;
            }
            foreach (string part in SheetLogic.JsString(cell).Split('/'))
            {
                long id;
                if (long.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0 && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        /// <summary>Имена босса по номеру боя из записи убийства (пусто — номер неизвестен).</summary>
        public static List<string> NamesFor(object encounterId)
        {
            double n = SheetLogic.Number(encounterId);
            List<string> list;
            if (double.IsNaN(n) || n <= 0 || !Current().Names.TryGetValue((long)n, out list))
            {
                return new List<string>();
            }
            return list;
        }

        /// <summary>Все имена боёв, одно из имён которых — tableName (для списка «Осталось» в аддоне).</summary>
        public static List<string> RelatedNames(string tableName)
        {
            List<string> result = new List<string>();
            if (SheetLogic.Norm(tableName).Length == 0)
            {
                return result;
            }
            foreach (List<string> names in Current().Names.Values)
            {
                if (names.Any(n => SheetLogic.Norm(n) == SheetLogic.Norm(tableName)))
                {
                    foreach (string n in names)
                    {
                        if (!result.Contains(n))
                        {
                            result.Add(n);
                        }
                    }
                }
            }
            return result;
        }
    }
}
