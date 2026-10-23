// Новая Google-таблица маунтов «как у автора»: общие листы «Рейды», «Подземелья», «Мировые боссы»
// и «Сводка» из встроенного каталога (catalog.json) плюс листы «Рейды <Имя>» / «Подземелья <Имя>»
// для каждого персонажа. Таблица собирается запросами Google Sheets API (spreadsheets.batchUpdate).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MountSheetSync
{
    /// <summary>Лист каталога: заголовки и строки (строки — текст, числа — double).</summary>
    sealed class CatalogSheet
    {
        public string Key = "";
        public string Name = "";
        public List<string> Columns = new List<string>();
        public List<object[]> Rows = new List<object[]>();

        public int Col(string name)
        {
            return Columns.IndexOf(name);
        }
    }

    /// <summary>Встроенный список маунтов (снят с таблицы автора).</summary>
    sealed class Catalog
    {
        public string Version = "";
        public CatalogSheet Raids;
        public CatalogSheet Dungeons;
        public CatalogSheet World;
        /// <summary>mountID → прежние названия маунта в каталоге (до названий «как в игре»).</summary>
        public Dictionary<long, List<string>> Aliases = new Dictionary<long, List<string>>();

        public static Catalog Parse(string json)
        {
            Dictionary<string, object> root = (Dictionary<string, object>)Json.Parse(json);
            Catalog c = new Catalog();
            c.Version = J.Str(root, "version");
            foreach (object o in J.Arr(J.Get(root, "sheets")))
            {
                Dictionary<string, object> s = J.Obj(o);
                CatalogSheet sheet = new CatalogSheet();
                sheet.Key = J.Str(s, "key");
                sheet.Name = J.Str(s, "sheet");
                sheet.Columns = J.Arr(J.Get(s, "columns")).Select(x => SheetLogic.JsString(x)).ToList();
                foreach (object r in J.Arr(J.Get(s, "rows")))
                {
                    List<object> cells = J.Arr(r);
                    object[] row = new object[sheet.Columns.Count];
                    for (int i = 0; i < row.Length; i++)
                    {
                        row[i] = i < cells.Count && cells[i] != null ? cells[i] : "";
                    }
                    sheet.Rows.Add(row);
                }
                if (sheet.Key == "raids")
                {
                    c.Raids = sheet;
                }
                else if (sheet.Key == "dungeons")
                {
                    c.Dungeons = sheet;
                }
                else if (sheet.Key == "world")
                {
                    c.World = sheet;
                }
            }
            if (c.Raids == null || c.Dungeons == null || c.World == null)
            {
                throw new FormatException("в каталоге нет листа рейдов, подземелий или мировых боссов");
            }
            Dictionary<string, object> aliases = J.Obj(J.Get(root, "aliases"));
            if (aliases != null)
            {
                foreach (KeyValuePair<string, object> kv in aliases)
                {
                    long id;
                    if (long.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
                    {
                        c.Aliases[id] = J.Arr(kv.Value).Select(x => SheetLogic.JsString(x)).Where(x => x.Length > 0).ToList();
                    }
                }
            }
            return c;
        }

        static Catalog embedded;

        public static Catalog Embedded()
        {
            if (embedded == null)
            {
                Assembly asm = typeof(Catalog).Assembly;
                using (Stream s = asm.GetManifestResourceStream("MountSheetSync.catalog.json"))
                {
                    if (s == null)
                    {
                        throw new InvalidOperationException("в программе нет каталога маунтов (catalog.json)");
                    }
                    using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                    {
                        embedded = Parse(reader.ReadToEnd());
                    }
                }
            }
            return embedded;
        }
    }

    /// <summary>Персонаж новой таблицы: для каких листов его заводить.</summary>
    sealed class BuildCharacter
    {
        public string Name = "";
        public bool Raids = true;
        public bool Dungeons = true;
        /// <summary>Колонка-галочка на «Мировых боссах».</summary>
        public bool World = true;

        public BuildCharacter(string name, bool raids, bool dungeons)
            : this(name, raids, dungeons, raids)
        {
        }

        public BuildCharacter(string name, bool raids, bool dungeons, bool world)
        {
            Name = name;
            Raids = raids;
            Dungeons = dungeons;
            World = world;
        }
    }

    sealed class BuildPlan
    {
        public string Title = "Маунты WoW";
        public List<BuildCharacter> Characters = new List<BuildCharacter>();
        /// <summary>Имена собранных маунтов из игры (журнал маунтов) — для строк без ID.</summary>
        public List<string> CollectedMounts = new List<string>();
        /// <summary>mountID собранных маунтов — по ним «Собрано?» = «Да» (точнее имён).</summary>
        public HashSet<long> CollectedIds = new HashSet<long>();
        public DateTime Today = DateTime.Today;
        /// <summary>Время снимка коллекции, по которому отмечено «Собрано?» (0 — нет снимка).</summary>
        public long CollectionStamp;
        public Catalog Catalog;
        /// <summary>Язык таблицы: названия листов, заголовки, списки; тексты каталога — по-английски, если не русский.</summary>
        public SheetLang Lang = SheetLang.Russian;
        /// <summary>Имена из игры (маунты, боссы, подземелья) — если игра на языке таблицы.</summary>
        public GameNames Game;
    }

    static class SheetBuilder
    {
        public const string RaidsSheet = "Рейды";
        public const string DungeonsSheet = "Подземелья";
        public const string WorldSheet = "Мировые боссы";
        public const string SummarySheet = "Сводка";
        public const string MetadataKey = "mountSheetSync";

        public const string Collected = "Собрано?";
        public const string Attempted = "Пытался на этой неделе?";
        public const string Left = "Осталось попыток выбить";
        /// <summary>Скрытая колонка с mountID (как в журнале маунтов игры): по нему узнаём маунт строки.</summary>
        public const string IdColumn = "ID в игре";
        /// <summary>Галочка на листе персонажа: эту строку персонаж фармит (снята — строки нет в «Осталось»).</summary>
        public const string FarmColumn = "Фармлю";

        static readonly string[] RaidMaster =
        {
            Collected, "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук", "Сложность / режим",
            "Сброс / частота", Left, "Скип есть?", "Как работает скип", "Совет по фарму", "Статус", "Источник", "Шанс выпадения",
            IdColumn,
        };
        static readonly int[] RaidWidths = { 79, 159, 223, 245, 191, 239, 159, 143, 153, 111, 335, 351, 159, 335, 120, 90 };

        static readonly string[] DungeonMaster =
        {
            Collected, "Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие",
            "Сложность / режим", "Сброс / частота", "Скип / маршрут", "Шанс выпадения", "Совет по фарму", "Статус", "Источник",
            IdColumn,
        };
        static readonly int[] DungeonWidths = { 79, 159, 239, 223, 223, 255, 175, 159, 271, 144, 351, 175, 335, 90 };

        static readonly string[] WorldMaster =
        {
            Collected, "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Сброс / частота", Left,
            "Совет по фарму", "Шанс выпадения", "Источник",
        };
        static readonly int[] WorldWidths = { 71, 134, 160, 216, 132, 97, 195, 185, 83, 279 };

        static readonly string[] RaidChar =
        {
            Attempted, "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук", "Сложность / режим", "Скип есть?", FarmColumn,
        };
        static readonly int[] RaidCharWidths = { 204, 161, 230, 273, 198, 156, 100, 80 };

        static readonly string[] DungeonChar =
        {
            Attempted, "Аддон", "Подземелье / активность", "Маунт", "Босс / достижение / условие", "Сложность / режим", "Скип / маршрут",
            FarmColumn,
        };
        static readonly int[] DungeonCharWidths = { 204, 161, 230, 273, 395, 214, 340, 80 };

        static readonly string[] RaidStatuses =
        {
            "Фармится", "Фармится / нужен контент-группинг", "Текущий контент", "Фармится через Timelost Saddle",
            "Проверь доступность", "Недоступен", "Недоступен / BMAH",
        };
        static readonly string[] DungeonStatuses =
        {
            "Фармится", "Текущий контент", "Фармится через Timelost Saddle", "Проверь доступность", "Недоступен",
        };

        // Цвета — как в таблице автора
        static readonly double[] HeaderBg = { 0.122, 0.306, 0.475 };
        static readonly double[] BandHeader = { 0.082, 0.376, 0.510 };
        static readonly double[] BandFirst = { 0.757, 0.894, 0.961 };
        static readonly double[] White = { 1, 1, 1 };
        static readonly double[] GreenBg = { 0.851, 0.918, 0.827 };
        static readonly double[] OrangeBg = { 0.988, 0.898, 0.804 };
        static readonly double[] RedBg = { 0.957, 0.8, 0.8 };
        static readonly double[] ChipNoBg = { 1, 0.812, 0.788 };
        static readonly double[] ChipNoFg = { 0.694, 0.008, 0.008 };
        static readonly double[] ChipYesBg = { 0.831, 0.929, 0.737 };
        static readonly double[] ChipYesFg = { 0.067, 0.451, 0.294 };
        static readonly double[] OffText = { 0.62, 0.62, 0.62 };
        static readonly double[] OffBg = { 0.937, 0.937, 0.937 };

        const string Font = "Carlito";
        const int RowHeight = 21;

        // ------------------------------------------------------------------
        // Что попадает на листы
        // ------------------------------------------------------------------

        /// <summary>Строка общего листа: значения по колонкам листа.</summary>
        sealed class MasterRow
        {
            public object[] Cells;
            public bool IsCollected;
        }

        sealed class Layout
        {
            public List<MasterRow> Raids = new List<MasterRow>();
            public List<MasterRow> Dungeons = new List<MasterRow>();
            public List<MasterRow> World = new List<MasterRow>();
            public List<BuildCharacter> RaidChars = new List<BuildCharacter>();
            public List<BuildCharacter> DungeonChars = new List<BuildCharacter>();
            public List<BuildCharacter> WorldChars = new List<BuildCharacter>();
        }

        /// <summary>Маунт из ячейки собран в игре (то же сравнение имён, что и для отметок «Собрано?»).</summary>
        public static bool IsCollected(object mountCell, IList<string> collected)
        {
            foreach (string name in collected)
            {
                if (SheetLogic.MountMatches(name, mountCell, "", ""))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Маунт строки собран: по mountID, если он известен, иначе по имени.</summary>
        public static bool IsCollected(long id, object mountCell, ICollection<long> ids, IList<string> names)
        {
            if (id > 0 && ids.Count > 0)
            {
                return ids.Contains(id);
            }
            return IsCollected(mountCell, names);
        }

        static List<MasterRow> MasterRows(CatalogSheet sheet, string[] header, BuildPlan plan)
        {
            List<MasterRow> rows = new List<MasterRow>();
            int cMount = sheet.Col("Маунт");
            int cId = sheet.Col(IdColumn);
            foreach (object[] catalogRow in sheet.Rows)
            {
                bool got = IsCollected(cId >= 0 ? MountIndex.IdOf(catalogRow[cId]) : 0, catalogRow[cMount], plan.CollectedIds, plan.CollectedMounts);
                object[] src = LocalRow(sheet, catalogRow, plan);
                object[] cells = new object[header.Length];
                for (int i = 0; i < header.Length; i++)
                {
                    if (header[i] == Collected)
                    {
                        cells[i] = got ? SheetLogic.Yes : SheetLogic.No;
                        continue;
                    }
                    int c = sheet.Col(header[i]);
                    cells[i] = c >= 0 ? src[c] : "";
                }
                rows.Add(new MasterRow { Cells = cells, IsCollected = got });
            }
            return rows;
        }

        /// <summary>
        /// Строка каталога на языке таблицы. Слова списков («Да», статусы…) остаются русскими — их переводит
        /// SheetLang.Outgoing вместе с заголовками; здесь — тексты каталога, маунты, боссы и подземелья.
        /// </summary>
        static object[] LocalRow(CatalogSheet sheet, object[] src, BuildPlan plan)
        {
            SheetLang lang = plan.Lang ?? SheetLang.Russian;
            if (lang.IsRussian)
            {
                return src;
            }
            GameNames game = plan.Game;
            bool journal = game != null && game.JournalIn(lang);
            bool mounts = game != null && game.MountsIn(lang);
            int cId = sheet.Col(IdColumn);
            int cJournal = sheet.Col("ID в журнале");
            int cMap = sheet.Col("ID места");
            object[] row = (object[])src.Clone();
            for (int c = 0; c < row.Length && c < sheet.Columns.Count; c++)
            {
                string text = src[c] as string;
                if (text == null || text.Length == 0)
                {
                    continue;
                }
                string column = sheet.Columns[c];
                if (column == "Маунт")
                {
                    long id = cId >= 0 ? MountIndex.IdOf(src[cId]) : 0;
                    string local;
                    if (!(mounts && game.Mounts.TryGetValue(id, out local)))
                    {
                        local = SheetLang.EnglishMount(id);
                    }
                    row[c] = local ?? lang.Content(text);
                }
                else if (column.StartsWith("Босс", StringComparison.Ordinal))
                {
                    string value = lang.Content(text);
                    List<long> journalIds = cJournal >= 0 ? Encounters.Ids(src[cJournal]) : new List<long>();
                    string local;
                    if (journal && journalIds.Count == 1 && game.Bosses.TryGetValue(journalIds[0], out local))
                    {
                        // «Король Дазар / Сундук претендента»: из игры — имя босса, остальное как в каталоге
                        string[] parts = value.Split(new[] { " / " }, StringSplitOptions.None);
                        parts[0] = local;
                        value = string.Join(" / ", parts);
                    }
                    row[c] = value;
                }
                else if (c == 1 && journal && cMap >= 0)
                {
                    List<long> maps = Encounters.Ids(src[cMap]);
                    string local;
                    if (maps.Count == 1 && game.Instances.TryGetValue(maps[0], out local))
                    {
                        // «Magisters' Terrace (Midnight)»: пометка в скобках остаётся
                        int paren = text.LastIndexOf(" (", StringComparison.Ordinal);
                        row[c] = local + (paren > 0 && text.EndsWith(")", StringComparison.Ordinal) ? text.Substring(paren) : "");
                    }
                    else
                    {
                        row[c] = lang.Content(text);
                    }
                }
                else
                {
                    row[c] = lang.Content(text);
                }
            }
            return row;
        }

        /// <summary>
        /// Строки листа персонажа из общего листа — как у автора: в «Рейды &lt;Имя&gt;» несобранные босс-дропы
        /// со статусом «Фармится», в «Подземелья &lt;Имя&gt;» все несобранные, кроме недоступных.
        /// </summary>
        public static List<object[]> CharacterRows(string[] masterHeader, IEnumerable<object[]> masterRows, bool raid)
        {
            string[] header = raid ? RaidChar : DungeonChar;
            int cCollected = Array.IndexOf(masterHeader, Collected);
            int cType = Array.IndexOf(masterHeader, "Тип источника");
            int cStatus = Array.IndexOf(masterHeader, "Статус");
            List<object[]> rows = new List<object[]>();
            foreach (object[] m in masterRows)
            {
                if (cCollected >= 0 && SheetLogic.IsYes(m[cCollected]))
                {
                    continue;
                }
                string status = SheetLogic.CellText(cStatus >= 0 ? m[cStatus] : "");
                if (raid)
                {
                    if (SheetLogic.CellText(m[cType]) != "Босс-дроп" || status != "Фармится")
                    {
                        continue;
                    }
                }
                else if (status.StartsWith("Недоступ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                object[] row = new object[header.Length];
                for (int i = 0; i < header.Length; i++)
                {
                    if (header[i] == Attempted)
                    {
                        row[i] = SheetLogic.No;
                        continue;
                    }
                    if (header[i] == FarmColumn)
                    {
                        row[i] = true;
                        continue;
                    }
                    int c = Array.IndexOf(masterHeader, header[i]);
                    row[i] = c >= 0 ? m[c] : "";
                }
                rows.Add(row);
            }
            return rows;
        }

        static Layout Plan(BuildPlan plan)
        {
            if (plan.Catalog == null)
            {
                plan.Catalog = Catalog.Embedded();
            }
            Layout l = new Layout();
            l.Raids = MasterRows(plan.Catalog.Raids, RaidMaster, plan);
            l.Dungeons = MasterRows(plan.Catalog.Dungeons, DungeonMaster, plan);
            l.World = MasterRows(plan.Catalog.World, WorldMaster, plan);
            l.RaidChars = plan.Characters.Where(c => c.Raids).ToList();
            l.DungeonChars = plan.Characters.Where(c => c.Dungeons).ToList();
            l.WorldChars = plan.Characters.Where(c => c.World).ToList();
            return l;
        }

        public static void Validate(BuildPlan plan)
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (BuildCharacter c in plan.Characters)
            {
                string name = (c.Name ?? "").Trim();
                if (name.Length == 0)
                {
                    throw new ArgumentException("пустое имя персонажа");
                }
                if (name.Length > 60 || name.IndexOfAny(new[] { '[', ']', '*', '?', '/', '\\', ':' }) >= 0)
                {
                    throw new ArgumentException("имя «" + name + "» не подходит для названия листа");
                }
                if (!seen.Add(SheetLogic.Norm(SheetLogic.StripNote(name))))
                {
                    throw new ArgumentException("персонаж «" + name + "» указан дважды");
                }
            }
            if (!plan.Characters.Any(c => c.Raids || c.Dungeons || c.World))
            {
                throw new ArgumentException("выберите хотя бы одного персонажа");
            }
            if (plan.Characters.Count(c => c.Raids) > 90 || plan.Characters.Count(c => c.Dungeons) > 90)
            {
                throw new ArgumentException("слишком много персонажей: не больше 90 на рейды и 90 на подземелья");
            }
        }

        // ------------------------------------------------------------------
        // Запросы
        // ------------------------------------------------------------------

        /// <summary>Тело spreadsheets.create: только название. Формулы пишем в en_US, потом ставим локаль языка таблицы.</summary>
        public static Dictionary<string, object> CreateBody(string title)
        {
            return O("properties", O("title", title, "locale", "en_US"));
        }

        /// <summary>
        /// Последний шаг: локаль языка таблицы (ru_RU — запятая в числах и «;» в формулах, en_US — точка и «,»)
        /// и метка программы (с языком таблицы — по нему программа читает таблицу).
        /// </summary>
        public static List<object> FinishRequests(Catalog catalog)
        {
            return FinishRequests(catalog, 0);
        }

        public static List<object> FinishRequests(Catalog catalog, long collectionStamp)
        {
            return FinishRequests(catalog, collectionStamp, SheetLang.Russian);
        }

        public static List<object> FinishRequests(Catalog catalog, long collectionStamp, SheetLang lang)
        {
            lang = lang ?? SheetLang.Russian;
            List<object> req = new List<object>
            {
                O("updateSpreadsheetProperties", O("properties", O("locale", lang.Locale), "fields", "locale")),
                O("createDeveloperMetadata", O("developerMetadata", O(
                    "metadataKey", MetadataKey,
                    "metadataValue", Json.Write(O("app", Program.Version, "catalog", catalog.Version, "lang", lang.Code), false),
                    "location", O("spreadsheet", true),
                    "visibility", "DOCUMENT"))),
            };
            // таблица сразу в нынешнем виде — SheetUpgrade её не трогает
            req.Add(O("createDeveloperMetadata", O("developerMetadata", O(
                "metadataKey", GoogleBook.PropertyPrefix + SheetUpgrade.Property,
                "metadataValue", SheetUpgrade.Current.ToString(CultureInfo.InvariantCulture),
                "location", O("spreadsheet", true),
                "visibility", "DOCUMENT"))));
            if (collectionStamp > 0)
            {
                // с этим снимком коллекции таблица уже сверена — программе не нужно сверять её сразу ещё раз
                req.Add(O("createDeveloperMetadata", O("developerMetadata", O(
                    "metadataKey", GoogleBook.PropertyPrefix + CollectionSync.CheckedProperty,
                    "metadataValue", collectionStamp.ToString(CultureInfo.InvariantCulture),
                    "location", O("spreadsheet", true),
                    "visibility", "DOCUMENT"))));
            }
            return req;
        }

        /// <summary>
        /// Все листы и оформление одним batchUpdate. defaultSheetId — лист, который Google создал
        /// вместе с таблицей («Лист1»); его удаляем.
        /// </summary>
        public static List<object> Requests(BuildPlan plan, long defaultSheetId)
        {
            Validate(plan);
            Layout l = Plan(plan);
            List<object> req = new List<object>();
            // Формулы со ссылками на другие листы пишем в самом конце: если лист в формуле ещё не создан,
            // Google не пересчитает её потом и навсегда покажет 0.
            List<object> late = new List<object>();
            int index = 0;
            int sheetId = 100;

            // Рейды
            List<object[]> raidRows = l.Raids.Select(r => r.Cells).ToList();
            AddTable(req, sheetId, RaidsSheet, index++, RaidMaster, raidRows, RaidWidths);
            string formula = RaidLeftFormula(l.RaidChars);
            if (formula != null)
            {
                late.Add(UpdateCells(sheetId, 1, Array.IndexOf(RaidMaster, Left), new List<object[]> { new object[] { new Formula(formula) } }));
            }
            MasterExtras(req, sheetId, RaidMaster, raidRows.Count, RaidStatuses, true);
            sheetId++;

            foreach (BuildCharacter c in l.RaidChars)
            {
                List<object[]> rows = CharacterRows(RaidMaster, raidRows, true);
                AddTable(req, sheetId, RaidsSheet + " " + c.Name.Trim(), index++, RaidChar, rows, RaidCharWidths);
                CharacterExtras(req, sheetId, RaidChar, rows.Count, true);
                sheetId++;
            }

            // Подземелья
            sheetId = 200;
            List<object[]> dungeonRows = l.Dungeons.Select(r => r.Cells).ToList();
            AddTable(req, sheetId, DungeonsSheet, index++, DungeonMaster, dungeonRows, DungeonWidths);
            MasterExtras(req, sheetId, DungeonMaster, dungeonRows.Count, DungeonStatuses, false);
            sheetId++;
            foreach (BuildCharacter c in l.DungeonChars)
            {
                List<object[]> rows = CharacterRows(DungeonMaster, dungeonRows, false);
                AddTable(req, sheetId, DungeonsSheet + " " + c.Name.Trim(), index++, DungeonChar, rows, DungeonCharWidths);
                CharacterExtras(req, sheetId, DungeonChar, rows.Count, false);
                sheetId++;
            }

            // Сводка
            AddSummary(req, late, 300, index++, plan.Today);

            // Мировые боссы: колонки персонажей с галочками
            AddWorld(req, 400, index++, l.World, l.WorldChars.Select(c => c.Name.Trim()).ToList());

            req.AddRange(late);
            req.Add(O("deleteSheet", O("sheetId", defaultSheetId)));
            (plan.Lang ?? SheetLang.Russian).Outgoing(req);
            return req;
        }

        /// <summary>«Осталось попыток выбить»: у скольких персонажей этот маунт ещё «Нет» на этой неделе.</summary>
        public static string RaidLeftFormula(IList<BuildCharacter> chars)
        {
            return RaidLeftFormulaForSheets(chars.Select(c => RaidsSheet + " " + c.Name.Trim()).ToList());
        }

        /// <summary>
        /// То же по именам листов персонажей («Рейды Эйра», …). farmColumns — буква колонки «Фармлю» листа
        /// (нет в словаре — H, как в новых листах; пустая строка — колонки нет).
        /// </summary>
        public static string RaidLeftFormulaForSheets(IList<string> sheets, IDictionary<string, string> farmColumns = null)
        {
            if (sheets.Count == 0)
            {
                return null;
            }
            List<string> parts = new List<string>();
            foreach (string sheet in sheets)
            {
                string q = Quote(sheet);
                string farm;
                if (farmColumns == null || !farmColumns.TryGetValue(sheet, out farm))
                {
                    farm = ColumnName(RaidChar.Length - 1);
                }
                parts.Add("COUNTIFS(" + q + "!D:D, m, " + q + "!A:A, \"Нет\"" + (farm.Length > 0 ? ", " + q + "!" + farm + ":" + farm + ", TRUE" : "") + ")");
            }
            return "=MAP(D2:D, LAMBDA(m, IF(m=\"\", \"\", SUM(" + string.Join(", ", parts.ToArray()) + "))))";
        }

        /// <summary>
        /// Формулы в запросах пишутся с запятыми (как в локали en_US). Google разбирает формулы по локали
        /// таблицы, поэтому для таблицы с десятичной запятой (ru_RU и т. п.) разделители меняются на «;».
        /// </summary>
        public static void LocalizeFormulas(object node, string locale)
        {
            if (!UsesSemicolons(locale))
            {
                return;
            }
            Localize(node);
        }

        public static bool UsesSemicolons(string locale)
        {
            if (string.IsNullOrEmpty(locale))
            {
                return false;
            }
            try
            {
                return CultureInfo.GetCultureInfo(locale.Replace('_', '-')).NumberFormat.NumberDecimalSeparator == ",";
            }
            catch (ArgumentException)
            {
                return locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
            }
        }

        static void Localize(object node)
        {
            Dictionary<string, object> d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (string key in d.Keys.ToList())
                {
                    string s = d[key] as string;
                    if (s != null && (key == "formulaValue" || (key == "userEnteredValue" && s.StartsWith("=", StringComparison.Ordinal))))
                    {
                        d[key] = SemicolonFormula(s);
                    }
                    else
                    {
                        Localize(d[key]);
                    }
                }
                return;
            }
            System.Collections.IList list = node as System.Collections.IList;
            if (list != null)
            {
                foreach (object o in list)
                {
                    Localize(o);
                }
            }
        }

        /// <summary>«,» вне строк в кавычках и имён листов в апострофах → «;».</summary>
        public static string SemicolonFormula(string f)
        {
            StringBuilder sb = new StringBuilder(f.Length);
            char quote = '\0';
            foreach (char ch in f)
            {
                if (quote != '\0')
                {
                    if (ch == quote)
                    {
                        quote = '\0';
                    }
                    sb.Append(ch);
                    continue;
                }
                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(ch == ',' ? ';' : ch);
                }
            }
            return sb.ToString();
        }

        /// <summary>Заголовки общих листов и листов персонажей — для «Добавить персонажей».</summary>
        public static string[] MasterHeader(bool raid)
        {
            return (string[])(raid ? RaidMaster : DungeonMaster).Clone();
        }

        /// <summary>Колонок на листе персонажа (последняя — «Фармлю»).</summary>
        public static int MasterHeaderLength(bool raid)
        {
            return (raid ? RaidChar : DungeonChar).Length;
        }

        public static int WorldFixedColumns
        {
            get { return WorldMaster.Length; }
        }

        /// <summary>Лист нового персонажа в готовой таблице: строки берутся из текущего общего листа.</summary>
        public static int AddCharacterSheet(List<object> req, int sheetId, int index, string name, bool raid,
            string[] masterHeader, List<object[]> masterRows)
        {
            List<object[]> rows = CharacterRows(masterHeader, masterRows, raid);
            string[] header = raid ? RaidChar : DungeonChar;
            AddTable(req, sheetId, (raid ? RaidsSheet : DungeonsSheet) + " " + name.Trim(), index, header, rows,
                raid ? RaidCharWidths : DungeonCharWidths);
            CharacterExtras(req, sheetId, header, rows.Count, raid);
            return rows.Count;
        }

        /// <summary>
        /// Колонки-галочки новых персонажей на «Мировых боссах» (сразу за последней заполненной колонкой)
        /// и новая формула «Осталось попыток». gridColumns — сколько колонок в сетке листа сейчас.
        /// </summary>
        public static void AddWorldColumns(List<object> req, int sheetId, int start, int gridColumns, int rowCount, int firstCharCol,
            int leftCol, IList<string> names)
        {
            if (names.Count == 0)
            {
                return;
            }
            int end = start + names.Count;
            if (end > gridColumns)
            {
                req.Add(O("appendDimension", O("sheetId", sheetId, "dimension", "COLUMNS", "length", end - gridColumns)));
            }
            List<object[]> rows = new List<object[]> { names.Cast<object>().ToArray() };
            for (int r = 0; r < rowCount; r++)
            {
                rows.Add(names.Select(n => (object)false).ToArray());
            }
            req.Add(UpdateCells(sheetId, 0, start, rows));
            req.Add(RepeatFormat(sheetId, 0, 1, start, end, HeaderFormat(11, "TOP"),
                "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy,userEnteredFormat.textFormat"));
            if (rowCount > 0)
            {
                req.Add(Validation(sheetId, 1, rowCount + 1, start, O("condition", O("type", "BOOLEAN"))));
                for (int c = start + 1; c < end; c++)
                {
                    req.Add(Validation(sheetId, 1, rowCount + 1, c, O("condition", O("type", "BOOLEAN"))));
                }
                req.Add(O("addBanding", O("bandedRange", O(
                    "range", Range(sheetId, 0, rowCount + 1, start, end),
                    "rowProperties", O(
                        "headerColor", Color(BandHeader),
                        "firstBandColor", Color(BandFirst),
                        "secondBandColor", Color(White))))));
            }
            for (int c = start; c < end; c++)
            {
                req.Add(O("updateDimensionProperties", O(
                    "range", O("sheetId", sheetId, "dimension", "COLUMNS", "startIndex", c, "endIndex", c + 1),
                    "properties", O("pixelSize", 100),
                    "fields", "pixelSize")));
            }
            if (leftCol >= 0)
            {
                req.Add(UpdateCells(sheetId, 1, leftCol, new List<object[]>
                {
                    new object[] { new Formula(WorldLeftFormula(firstCharCol, end - firstCharCol)) },
                }));
            }
        }

        /// <summary>«Осталось попыток» на «Мировых боссах»: сколько галочек персонажей ещё не стоит.</summary>
        public static string WorldLeftFormula(int firstCharCol, int count)
        {
            List<string> refs = new List<string> { "D2:D", "A2:A" };
            List<string> names = new List<string> { "mnt", "sob" };
            List<string> sum = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string col = ColumnName(firstCharCol + i);
                string n = "p" + LetterName(i);
                refs.Add(col + "2:" + col);
                names.Add(n);
                sum.Add("(" + n + "=FALSE)");
            }
            string body = count == 0 ? "0" : string.Join("+", sum.ToArray());
            return "=MAP(" + string.Join(", ", refs.ToArray()) + ", LAMBDA(" + string.Join(", ", names.ToArray())
                + ", IF(mnt=\"\", \"\", IF(sob=\"Да\", 0, " + body + "))))";
        }

        /// <summary>0 → a, 25 → z, 26 → ba … (имена параметров LAMBDA без цифр — их не спутать с адресом ячейки).</summary>
        static string LetterName(int i)
        {
            string s = "";
            do
            {
                s = (char)('a' + i % 26) + s;
                i /= 26;
            }
            while (i > 0);
            return s.Length == 1 ? s : s;
        }

        public static string ColumnName(int c)
        {
            string s = "";
            c++;
            while (c > 0)
            {
                int m = (c - 1) % 26;
                s = (char)('A' + m) + s;
                c = (c - 1) / 26;
            }
            return s;
        }

        /// <summary>'Рейды Эйра' — имя листа в формуле (апостроф удваивается).</summary>
        public static string Quote(string sheetName)
        {
            return "'" + sheetName.Replace("'", "''") + "'";
        }

        static void AddTable(List<object> req, int sheetId, string title, int index, string[] header, List<object[]> rows, int[] widths)
        {
            int columns = header.Length;
            req.Add(O("addSheet", O("properties", O(
                "sheetId", sheetId,
                "title", title,
                "index", index,
                "gridProperties", O("rowCount", Math.Max(rows.Count + 1 + 50, 100), "columnCount", columns, "frozenRowCount", 1)))));
            List<object[]> all = new List<object[]> { header.Cast<object>().ToArray() };
            all.AddRange(rows);
            req.Add(UpdateCells(sheetId, 0, 0, all));
            // заголовок
            req.Add(RepeatFormat(sheetId, 0, 1, 0, columns, HeaderFormat(11, "TOP"),
                "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy,userEnteredFormat.textFormat"));
            // строки
            if (rows.Count > 0)
            {
                req.Add(RepeatFormat(sheetId, 1, rows.Count + 1, 0, columns,
                    O("textFormat", O("fontFamily", Font, "fontSize", 11), "verticalAlignment", "TOP", "wrapStrategy", "WRAP"),
                    "userEnteredFormat.textFormat,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy"));
                req.Add(O("updateDimensionProperties", O(
                    "range", O("sheetId", sheetId, "dimension", "ROWS", "startIndex", 1, "endIndex", rows.Count + 1),
                    "properties", O("pixelSize", RowHeight),
                    "fields", "pixelSize")));
            }
            req.Add(O("updateDimensionProperties", O(
                "range", O("sheetId", sheetId, "dimension", "ROWS", "startIndex", 0, "endIndex", 1),
                "properties", O("pixelSize", 42),
                "fields", "pixelSize")));
            for (int c = 0; c < columns && c < widths.Length; c++)
            {
                req.Add(O("updateDimensionProperties", O(
                    "range", O("sheetId", sheetId, "dimension", "COLUMNS", "startIndex", c, "endIndex", c + 1),
                    "properties", O("pixelSize", widths[c]),
                    "fields", "pixelSize")));
            }
            req.Add(O("addBanding", O("bandedRange", O(
                "range", Range(sheetId, 0, rows.Count + 1, 0, columns),
                "rowProperties", O(
                    "headerColor", Color(BandHeader),
                    "firstBandColor", Color(BandFirst),
                    "secondBandColor", Color(White))))));
        }

        /// <summary>«Собрано?» и «Статус» с выпадающими списками, цвета статусов и скипа, фильтр.</summary>
        static void MasterExtras(List<object> req, int sheetId, string[] header, int rowCount, string[] statuses, bool raid)
        {
            int end = rowCount + 1;
            int cCollected = Array.IndexOf(header, Collected);
            int cStatus = Array.IndexOf(header, "Статус");
            req.Add(Validation(sheetId, 1, end, cCollected, ListRule(new[] { SheetLogic.No, SheetLogic.Yes }, null)));
            req.Add(Validation(sheetId, 1, end, cStatus, ListRule(statuses, null)));

            string statusCol = ColumnName(cStatus);
            req.Add(ConditionalFormula(sheetId, 1, end, cStatus, "=ISNUMBER(SEARCH(\"Недоступ\", " + statusCol + "2))", RedBg, null));
            req.Add(ConditionalFormula(sheetId, 1, end, cStatus, "=ISNUMBER(SEARCH(\"Текущий\", " + statusCol + "2))", GreenBg, null));
            if (raid)
            {
                int cSkip = Array.IndexOf(header, "Скип есть?");
                string skipCol = ColumnName(cSkip);
                req.Add(ConditionalFormula(sheetId, 1, end, cSkip, "=" + skipCol + "2=\"Да\"", GreenBg, null));
                req.Add(ConditionalFormula(sheetId, 1, end, cSkip, "=" + skipCol + "2=\"Нет\"", OrangeBg, null));
                // «Осталось попыток» и «Шанс» — по центру, на цвете полосы
                foreach (string name in new[] { Left, "Шанс выпадения" })
                {
                    int c = Array.IndexOf(header, name);
                    req.Add(RepeatFormat(sheetId, 1, end, c, c + 1, O("backgroundColor", Color(BandFirst), "horizontalAlignment", "CENTER"),
                        "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment"));
                }
            }
            req.Add(RepeatFormat(sheetId, 1, end, cCollected, cCollected + 1, O("horizontalAlignment", "CENTER"),
                "userEnteredFormat.horizontalAlignment"));
            int cId = Array.IndexOf(header, IdColumn);
            if (cId >= 0)
            {
                req.Add(HideColumn(sheetId, cId));
            }
            req.AddRange(YesNoColors(sheetId, cCollected));

            List<object> specs = new List<object>
            {
                O("columnIndex", cCollected, "filterCriteria", O("hiddenValues", new List<object> { SheetLogic.Yes })),
                O("columnIndex", cStatus, "filterCriteria", O("hiddenValues", statuses.Where(s => s.StartsWith("Недоступ", StringComparison.Ordinal)).Cast<object>().ToList())),
            };
            req.Add(O("setBasicFilter", O("filter", O(
                "range", O("sheetId", sheetId, "startRowIndex", 0, "startColumnIndex", 0, "endColumnIndex", header.Length),
                "filterSpecs", specs))));
        }

        /// <summary>Спрятать колонку (её можно показать: правый щелчок по заголовкам колонок → «Показать»).</summary>
        public static Dictionary<string, object> HideColumn(int sheetId, int col)
        {
            return O("updateDimensionProperties", O(
                "range", O("sheetId", sheetId, "dimension", "COLUMNS", "startIndex", col, "endIndex", col + 1),
                "properties", O("hiddenByUser", true),
                "fields", "hiddenByUser"));
        }

        /// <summary>Колонка «ID в игре» в готовой таблице: значения (строка 0 — заголовок), оформление, скрыта.</summary>
        public static List<object> AppendColumnRequests(int sheetId, int col, int gridColumns, List<object> values, bool hidden)
        {
            List<object> req = new List<object>();
            if (col >= gridColumns)
            {
                req.Add(O("appendDimension", O("sheetId", sheetId, "dimension", "COLUMNS", "length", col + 1 - gridColumns)));
            }
            req.Add(UpdateCells(sheetId, 0, col, values.Select(v => new object[] { v }).ToList()));
            req.Add(RepeatFormat(sheetId, 0, 1, col, col + 1, HeaderFormat(11, "TOP"),
                "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy,userEnteredFormat.textFormat"));
            if (hidden)
            {
                req.Add(HideColumn(sheetId, col));
            }
            return req;
        }

        /// <summary>«Пытался на этой неделе?» — «Нет»/«Да» с цветными метками; цвет «Скип есть?».</summary>
        static void CharacterExtras(List<object> req, int sheetId, string[] header, int rowCount, bool raid)
        {
            int end = rowCount + 1;
            req.AddRange(YesNoColors(sheetId, 0));
            int cFarm = Array.IndexOf(header, FarmColumn);
            if (rowCount > 0)
            {
                Dictionary<string, object> rule = ListRule(new[] { SheetLogic.No, SheetLogic.Yes },
                    new[] { new[] { ChipNoBg, ChipNoFg }, new[] { ChipYesBg, ChipYesFg } });
                req.Add(Validation(sheetId, 1, end, 0, rule));
                req.Add(RepeatFormat(sheetId, 1, end, 0, 1, O("horizontalAlignment", "CENTER"), "userEnteredFormat.horizontalAlignment"));
                if (cFarm >= 0)
                {
                    req.Add(Validation(sheetId, 1, end, cFarm, O("condition", O("type", "BOOLEAN"))));
                    req.Add(RepeatFormat(sheetId, 1, end, cFarm, cFarm + 1, O("horizontalAlignment", "CENTER"), "userEnteredFormat.horizontalAlignment"));
                }
            }
            if (raid)
            {
                int cSkip = Array.IndexOf(header, "Скип есть?");
                string skipCol = ColumnName(cSkip);
                req.Add(ConditionalFormula(sheetId, 1, end, cSkip, "=" + skipCol + "2=\"Да\"", GreenBg, null));
                req.Add(ConditionalFormula(sheetId, 1, end, cSkip, "=" + skipCol + "2=\"Нет\"", OrangeBg, null));
            }
            if (cFarm >= 0)
            {
                req.Add(NotFarmedGray(sheetId, cFarm, header.Length));   // последним — главнее остальных правил
            }
        }

        /// <summary>
        /// «Да» — зелёная ячейка, «Нет» — красная (как цвета выпадающего списка в таблице автора: их самих
        /// Google через API не сохраняет). Диапазон — вся колонка со 2-й строки, и для новых строк тоже.
        /// </summary>
        public static List<object> YesNoColors(int sheetId, int col)
        {
            List<object> req = new List<object>();
            if (col < 0)
            {
                return req;
            }
            foreach (KeyValuePair<string, double[][]> kv in new[]
            {
                new KeyValuePair<string, double[][]>(SheetLogic.No, new[] { ChipNoBg, ChipNoFg }),
                new KeyValuePair<string, double[][]>(SheetLogic.Yes, new[] { ChipYesBg, ChipYesFg }),
            })
            {
                req.Add(O("addConditionalFormatRule", O(
                    "rule", O(
                        "ranges", new List<object> { O("sheetId", sheetId, "startRowIndex", 1, "startColumnIndex", col, "endColumnIndex", col + 1) },
                        "booleanRule", O(
                            "condition", O("type", "TEXT_EQ", "values", new List<object> { O("userEnteredValue", kv.Key) }),
                            "format", O("backgroundColor", Color(kv.Value[0]), "textFormat", O("foregroundColor", Color(kv.Value[1]))))),
                    "index", 0)));
            }
            return req;
        }

        /// <summary>Строка, которую персонаж не фармит («Фармлю» снята), — серая целиком.</summary>
        public static Dictionary<string, object> NotFarmedGray(int sheetId, int farmCol, int width)
        {
            string f = "=$" + ColumnName(farmCol) + "2=FALSE";
            return O("addConditionalFormatRule", O(
                "rule", O(
                    "ranges", new List<object> { O("sheetId", sheetId, "startRowIndex", 1, "startColumnIndex", 0, "endColumnIndex", width) },
                    "booleanRule", O(
                        "condition", O("type", "CUSTOM_FORMULA", "values", new List<object> { O("userEnteredValue", f) }),
                        "format", O("backgroundColor", Color(OffBg), "textFormat", O("foregroundColor", Color(OffText))))),
                "index", 0));
        }

        /// <summary>Колонка «Фармлю» в готовом листе персонажа: заголовок, галочки, оформление.</summary>
        public static List<object> FarmColumnRequests(int sheetId, int col, int gridColumns, int dataRows, List<object> values)
        {
            List<object> req = AppendColumnRequests(sheetId, col, gridColumns, values, false);
            if (dataRows > 0)
            {
                req.Add(Validation(sheetId, 1, dataRows + 1, col, O("condition", O("type", "BOOLEAN"))));
                req.Add(RepeatFormat(sheetId, 1, dataRows + 1, col, col + 1, O("horizontalAlignment", "CENTER"), "userEnteredFormat.horizontalAlignment"));
            }
            req.Add(O("updateDimensionProperties", O(
                "range", O("sheetId", sheetId, "dimension", "COLUMNS", "startIndex", col, "endIndex", col + 1),
                "properties", O("pixelSize", 80),
                "fields", "pixelSize")));
            req.Add(NotFarmedGray(sheetId, col, col + 1));
            return req;
        }

        static void AddWorld(List<object> req, int sheetId, int index, List<MasterRow> rows, List<string> chars)
        {
            List<string> header = WorldMaster.ToList();
            header.AddRange(chars);
            int columns = header.Count;
            int firstChar = WorldMaster.Length;
            List<object[]> data = new List<object[]>();
            foreach (MasterRow r in rows)
            {
                object[] row = new object[columns];
                Array.Copy(r.Cells, row, r.Cells.Length);
                for (int c = firstChar; c < columns; c++)
                {
                    row[c] = false;
                }
                data.Add(row);
            }
            List<int> widths = WorldWidths.ToList();
            widths.AddRange(chars.Select(c => 100));
            AddTable(req, sheetId, WorldSheet, index, header.ToArray(), data, widths.ToArray());
            int end = data.Count + 1;
            int cLeft = header.IndexOf(Left);
            req.Add(UpdateCells(sheetId, 1, cLeft, new List<object[]> { new object[] { new Formula(WorldLeftFormula(firstChar, chars.Count)) } }));
            req.Add(Validation(sheetId, 1, end, 0, ListRule(new[] { SheetLogic.Yes, SheetLogic.No },
                new[] { new[] { ChipYesBg, ChipYesFg }, new[] { ChipNoBg, ChipNoFg } })));
            req.AddRange(YesNoColors(sheetId, 0));
            for (int c = firstChar; c < columns; c++)
            {
                req.Add(Validation(sheetId, 1, end, c, O("condition", O("type", "BOOLEAN"))));
            }
            foreach (string name in new[] { Collected, Left, "Шанс выпадения", "Сброс / частота" })
            {
                int c = header.IndexOf(name);
                req.Add(RepeatFormat(sheetId, 1, end, c, c + 1, O("horizontalAlignment", "CENTER"), "userEnteredFormat.horizontalAlignment"));
            }
            int cChance = header.IndexOf("Шанс выпадения");
            req.Add(RepeatFormat(sheetId, 1, end, cChance, cChance + 1, O("numberFormat", O("type", "PERCENT", "pattern", "0%")),
                "userEnteredFormat.numberFormat"));
        }

        static void AddSummary(List<object> req, List<object> late, int sheetId, int index, DateTime today)
        {
            List<object[]> rows = new List<object[]>
            {
                new object[] { "WoW Retail / Midnight — фарм маунтов в рейдах и подземельях", "", "", "", "" },
                new object[] { "Создано", (today - new DateTime(1899, 12, 30)).TotalDays, "", "", "" },
                new object[] { "Как отмечается", "Убили босса в игре — программа Mount Sheet Sync сама ставит «Да» на листе персонажа. Выпал маунт — «Собрано?» = «Да». Можно править и вручную.", "", "", "" },
                new object[] { "", "", "", "", "" },
                new object[] { "Лист", "Всего строк", "Собрано", "Осталось", "Недоступно" },
                SummaryRow(RaidsSheet, "M"),
                SummaryRow(DungeonsSheet, "L"),
                new object[] { WorldSheet, new Formula("=COUNTA('" + WorldSheet + "'!D2:D)"),
                    new Formula("=COUNTIF('" + WorldSheet + "'!A2:A, \"Да\")"),
                    new Formula("=COUNTIF('" + WorldSheet + "'!A2:A, \"Нет\")"), 0.0 },
                new object[] { "", "", "", "", "" },
                new object[] { "Статусы в таблице", "", "", "", "" },
                new object[] { "Фармится", "Можно целенаправленно фармить сейчас в Retail.", "", "", "" },
                new object[] { "Фармится / нужен контент-группинг", "Маунт доступен, но обычно нужен актуальный/не совсем legacy raid group.", "", "", "" },
                new object[] { "Текущий контент", "Midnight или свежий контент: обычно нужна группа и актуальные лут-правила.", "", "", "" },
                new object[] { "Фармится через Timelost Saddle", "M+ seasonal mount, покупается через Timelost Saddle после Keystone Myth (3400+). Седло одно за сезон на весь Warband.", "", "", "" },
                new object[] { "Недоступен", "Исторический/удалённый маунт, оставлен для проверки коллекции.", "", "", "" },
                new object[] { "", "", "", "", "" },
                new object[] { "Основные источники", "", "", "", "" },
                new object[] { "WarcraftMounts — Loot Mounts", "https://www.warcraftmounts.com/loot.php", "", "", "" },
                new object[] { "WarcraftMounts — Achievement Mounts", "https://www.warcraftmounts.com/achievement.php", "", "", "" },
                new object[] { "Wowhead — Legacy Raid Guide / Raid Skips", "https://www.wowhead.com/guide/legacy-raid-guide-loot-lockouts-raid-skips-lfr-access-legacy-loot-and-transmog-23879", "", "", "" },
                new object[] { "Wowhead — Midnight Mount Guide", "https://www.wowhead.com/guide/collections/midnight-mounts-locations-appearances", "", "", "" },
                new object[] { "Wowhead — The War Within Mount Guide", "https://www.wowhead.com/guide/collections/the-war-within/mounts", "", "", "" },
                new object[] { "Wowhead — Keystone Myth / Timelost Saddle", "https://www.wowhead.com/news/keystone-myth-mounts-now-live-381346", "", "", "" },
            };
            req.Add(O("addSheet", O("properties", O(
                "sheetId", sheetId,
                "title", SummarySheet,
                "index", index,
                "gridProperties", O("rowCount", 100, "columnCount", 6)))));
            // строки с формулами (6–8) — в конце пакета, когда все листы уже есть
            const int firstFormulaRow = 5;
            const int formulaRows = 3;
            List<object[]> plain = rows.Select((r, i) => i >= firstFormulaRow && i < firstFormulaRow + formulaRows
                ? r.Select(v => v is Formula ? (object)"" : v).ToArray() : r).ToList();
            req.Add(UpdateCells(sheetId, 0, 0, plain));
            late.Add(UpdateCells(sheetId, firstFormulaRow, 0, rows.GetRange(firstFormulaRow, formulaRows)));
            req.Add(RepeatFormat(sheetId, 0, rows.Count, 0, 5, O("textFormat", O("fontFamily", Font, "fontSize", 11), "wrapStrategy", "WRAP",
                "verticalAlignment", "TOP"), "userEnteredFormat.textFormat,userEnteredFormat.wrapStrategy,userEnteredFormat.verticalAlignment"));
            req.Add(O("mergeCells", O("range", Range(sheetId, 0, 1, 0, 5), "mergeType", "MERGE_ALL")));
            req.Add(RepeatFormat(sheetId, 0, 1, 0, 5, HeaderFormat(14, "MIDDLE"),
                "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy,userEnteredFormat.textFormat"));
            req.Add(RepeatFormat(sheetId, 1, 2, 1, 2, O("numberFormat", O("type", "DATE", "pattern", "yyyy-mm-dd"), "horizontalAlignment", "LEFT"),
                "userEnteredFormat.numberFormat,userEnteredFormat.horizontalAlignment"));
            foreach (int r in new[] { 4, 9, 16 })
            {
                int width = r == 4 ? 5 : 2;
                req.Add(RepeatFormat(sheetId, r, r + 1, 0, width, HeaderFormat(11, "MIDDLE"),
                    "userEnteredFormat.backgroundColor,userEnteredFormat.horizontalAlignment,userEnteredFormat.verticalAlignment,userEnteredFormat.wrapStrategy,userEnteredFormat.textFormat"));
            }
            req.Add(RepeatFormat(sheetId, 5, 8, 1, 5, O("horizontalAlignment", "CENTER"), "userEnteredFormat.horizontalAlignment"));
            int[] widths = { 271, 607, 110, 110, 110 };
            for (int c = 0; c < widths.Length; c++)
            {
                req.Add(O("updateDimensionProperties", O(
                    "range", O("sheetId", sheetId, "dimension", "COLUMNS", "startIndex", c, "endIndex", c + 1),
                    "properties", O("pixelSize", widths[c]),
                    "fields", "pixelSize")));
            }
            req.Add(O("updateDimensionProperties", O(
                "range", O("sheetId", sheetId, "dimension", "ROWS", "startIndex", 0, "endIndex", 1),
                "properties", O("pixelSize", 34),
                "fields", "pixelSize")));
        }

        static object[] SummaryRow(string sheet, string statusCol)
        {
            string q = Quote(sheet);
            return new object[]
            {
                sheet,
                new Formula("=COUNTA(" + q + "!D2:D)"),
                new Formula("=COUNTIF(" + q + "!A2:A, \"Да\")"),
                new Formula("=COUNTIFS(" + q + "!A2:A, \"Нет\", " + q + "!" + statusCol + "2:" + statusCol + ", \"<>Недоступ*\")"),
                new Formula("=COUNTIFS(" + q + "!A2:A, \"Нет\", " + q + "!" + statusCol + "2:" + statusCol + ", \"Недоступ*\")"),
            };
        }

        // ------------------------------------------------------------------
        // Кирпичики запросов
        // ------------------------------------------------------------------

        /// <summary>Значение ячейки — формула (а не текст, начинающийся с «=»).</summary>
        public sealed class Formula
        {
            public readonly string Text;
            public Formula(string text)
            {
                Text = text;
            }
        }

        public static Dictionary<string, object> O(params object[] kv)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                d[(string)kv[i]] = kv[i + 1];
            }
            return d;
        }

        static Dictionary<string, object> Color(double[] rgb)
        {
            return O("red", rgb[0], "green", rgb[1], "blue", rgb[2]);
        }

        static Dictionary<string, object> Range(int sheetId, int r0, int r1, int c0, int c1)
        {
            return O("sheetId", sheetId, "startRowIndex", r0, "endRowIndex", r1, "startColumnIndex", c0, "endColumnIndex", c1);
        }

        static Dictionary<string, object> HeaderFormat(int size, string valign)
        {
            return O(
                "backgroundColor", Color(HeaderBg),
                "horizontalAlignment", "CENTER",
                "verticalAlignment", valign,
                "wrapStrategy", "WRAP",
                "textFormat", O("bold", true, "fontFamily", Font, "fontSize", size, "foregroundColor", Color(White)));
        }

        static Dictionary<string, object> CellValue(object v)
        {
            Formula f = v as Formula;
            if (f != null)
            {
                return O("formulaValue", f.Text);
            }
            if (v is bool)
            {
                return O("boolValue", v);
            }
            if (v is double || v is int || v is long)
            {
                return O("numberValue", Convert.ToDouble(v, CultureInfo.InvariantCulture));
            }
            string s = SheetLogic.JsString(v);
            return s.Length == 0 ? null : O("stringValue", s);
        }

        public static Dictionary<string, object> UpdateCells(int sheetId, int row, int col, List<object[]> rows)
        {
            List<object> rowData = new List<object>();
            foreach (object[] r in rows)
            {
                List<object> values = new List<object>();
                foreach (object v in r)
                {
                    Dictionary<string, object> uv = CellValue(v);
                    values.Add(uv == null ? new Dictionary<string, object>() : O("userEnteredValue", uv));
                }
                rowData.Add(O("values", values));
            }
            return O("updateCells", O(
                "rows", rowData,
                "fields", "userEnteredValue",
                "start", O("sheetId", sheetId, "rowIndex", row, "columnIndex", col)));
        }

        static Dictionary<string, object> RepeatFormat(int sheetId, int r0, int r1, int c0, int c1, Dictionary<string, object> format, string fields)
        {
            return O("repeatCell", O(
                "range", Range(sheetId, r0, r1, c0, c1),
                "cell", O("userEnteredFormat", format),
                "fields", fields));
        }

        static Dictionary<string, object> Validation(int sheetId, int r0, int r1, int col, Dictionary<string, object> rule)
        {
            return O("setDataValidation", O("range", Range(sheetId, r0, r1, col, col + 1), "rule", rule));
        }

        /// <summary>Выпадающий список; styles — фон и цвет текста меток (как «чипы» в таблице автора).</summary>
        static Dictionary<string, object> ListRule(string[] values, double[][][] styles)
        {
            List<object> list = new List<object>();
            for (int i = 0; i < values.Length; i++)
            {
                Dictionary<string, object> v = O("userEnteredValue", values[i]);
                if (styles != null)
                {
                    v["dropdownStyle"] = O(
                        "backgroundColorStyle", O("rgbColor", Color(styles[i][0])),
                        "foregroundColorStyle", O("rgbColor", Color(styles[i][1])));
                }
                list.Add(v);
            }
            return O("condition", O("type", "ONE_OF_LIST", "values", list), "strict", true, "showCustomUi", true);
        }

        static Dictionary<string, object> ConditionalFormula(int sheetId, int r0, int r1, int col, string formula, double[] bg, double[] fg)
        {
            Dictionary<string, object> format = O("backgroundColor", Color(bg));
            if (fg != null)
            {
                format["textFormat"] = O("foregroundColor", Color(fg));
            }
            return O("addConditionalFormatRule", O(
                "rule", O(
                    "ranges", new List<object> { Range(sheetId, r0, r1, col, col + 1) },
                    "booleanRule", O(
                        "condition", O("type", "CUSTOM_FORMULA", "values", new List<object> { O("userEnteredValue", formula) }),
                        "format", format)),
                "index", 0));
        }
    }
}
