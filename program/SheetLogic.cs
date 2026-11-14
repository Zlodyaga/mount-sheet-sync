// Логика таблицы маунтов — та же, что в Code.gs (скрипте Google-таблицы), но внутри программы:
// так программа сама ставит «Да», сбрасывает неделю, отмечает «Собрано?» и собирает список
// «Осталось» через Google Sheets API, без скрипта в таблице. Правила совпадают с Code.gs 1.4
// построчно — на это есть тесты (те же, что для скрипта).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MountSheetSync
{
    /// <summary>Лист таблицы: имя и значения ячеек (строка 0 — заголовок).</summary>
    sealed class SheetEntry
    {
        public string Name = "";
        public long SheetId;
        /// <summary>Сколько колонок в сетке листа (0 — неизвестно). Нужна, чтобы дописывать колонки.</summary>
        public int GridColumns;
        /// <summary>Значения: string, double или bool; пустая ячейка — "". Строки одинаковой длины.</summary>
        public List<object[]> Data = new List<object[]>();
        /// <summary>Ячейки с формулами: ключ = строка * 100000 + колонка.</summary>
        public HashSet<long> Formulas = new HashSet<long>();
    }

    /// <summary>Таблица целиком: листы, запись ячеек и пара служебных значений.</summary>
    interface IBook
    {
        string Title { get; }
        string Url { get; }
        List<SheetEntry> Entries { get; }
        void Set(SheetEntry entry, int r, int c, object value);
        void Flush();
        string GetProperty(string key);
        void SetProperty(string key, string value);
    }

    static partial class SheetLogic
    {
        public const string Yes = "Да";
        public const string No = "Нет";
        public const string RaidPrefix = "Рейды";
        public const string DungeonPrefix = "Подземелья";
        public const string WorldSheet = "Мировые боссы";
        public const string RareSheet = "Редкие монстры";
        public const string ResetProperty = "lastWeekReset";

        static readonly string[,] Prefixes = { { "raid", RaidPrefix }, { "dungeon", DungeonPrefix } };

        /// <summary>«Имя в игре» → «имя как в названии листа». Обычно пусто.</summary>
        public static Dictionary<string, string> CharacterAliases = new Dictionary<string, string>();

        /// <summary>Босс в игре называется иначе, чем в колонке «Босс» (английский клиент и т. п.).</summary>
        public static readonly List<KeyValuePair<string, string>> BossAliases = Pairs(
            // Рейды
            "Alysrazor", "Алисразор",
            "Ultraxion", "Ультраксион",
            "Madness of Deathwing", "Безумие Смертокрыла",
            "N'Zoth the Corruptor", "Н'Зот Заразитель",
            "Sylvanas Windrunner", "Сильвана Ветрокрылая",
            "The Nine", "Девятка",
            "The Jailer", "Тюремщик",
            "Fyrakk the Blazing", "Фиракк",
            "Kael'thas Sunstrider", "Кель'тас Солнечный Скиталец",
            "Attumen the Huntsman", "Ловчий Аттумен",
            "Malygos", "Малигос",
            "Sartharion", "Сартарион",
            "Onyxia", "Ониксия",
            "Yogg-Saron", "Йогг-Сарон",
            "The Lich King", "Король-лич",
            "Al'Akir", "Ал'акир",
            "Ragnaros", "Рагнарос",
            "Elegon", "Элегон",
            "Horridon", "Хорридон",
            "Ji-Kun", "Цзи-Кунь",
            "Garrosh Hellscream", "Гаррош Адский Крик",
            "Blackhand", "Чернорук",
            "Archimonde", "Архимонд",
            "Gul'dan", "Гул'дан",
            "Mistress Sassz'ine", "Госпожа Сашж'ин",
            "Felhounds of Sargeras", "Гончие Саргераса",
            "Argus the Unmaker", "Аргус Порабощенный",
            "High Tinker Mekkatorque", "Главный механик Меггакрут",
            "Lady Jaina Proudmoore", "Леди Джайна Праудмур",
            "Queen Ansurek", "Королева Ансурек",
            "Chrome King Gallywix", "Хромовый король Галливикс",
            "Dimensius", "Дименсий",
            // Подземелья
            "Lord Aurius Rivendare", "Лорд Аурий Ривендер",
            "Anzu", "Анзу",
            "Infinite Corruptor", "Осквернитель из рода Бесконечности",
            "Skadi the Ruthless", "Скади Безжалостный",
            "Slabhide", "Камнедроб",
            "Altairus", "Альтаирий",
            "Bloodlord Mandokir", "Мандокир Повелитель Крови",
            "High Priestess Kilnara", "Верховная жрица Килнара",
            "Nightbane", "Ночная Погибель",
            "Dazar, The First King", "Король Дазар",
            "Unbound Abomination", "Неупокоенное чудовище",
            "Harlan Sweete", "Красавчик Харлан",
            "King Mechagon", "Король Мехагон",
            "HK-8 Aerial Oppression Unit", "Воздушное судно подавления HK-8",
            "Nalthor the Rimebinder", "Налтор Призыватель Зимы",
            "So'leah", "Со'лея",
            "Void Speaker Eirich", "Верховный оратор Эйрих",
            "The Darkness", "Тьма",
            // Мировые боссы
            "Sha of Anger", "Ша Злости",
            "Galleon", "Галеон",
            "Галеон", "Галлеон",   // так босс был записан в каталоге до 2.9.1 (игра пишет «Галеон»)
            "Галлеон", "Галеон",
            "Oondasta", "Ундаста",
            "Nalak", "Налак",
            "Rukhmar", "Рухмар");

        static List<KeyValuePair<string, string>> Pairs(params string[] items)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < items.Length; i += 2)
            {
                list.Add(new KeyValuePair<string, string>(items[i], items[i + 1]));
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Запрос → ответ (как doPost в Code.gs)
        // ------------------------------------------------------------------

        /// <summary>Тот же запрос, что программа шлёт скрипту: ping, status или список убийств.</summary>
        public static Dictionary<string, object> Handle(IBook book, Dictionary<string, object> req)
        {
            string action = J.Str(req, "action");
            if (action == "ping")
            {
                return Ping(book, req);
            }
            if (action == "status")
            {
                return Status(book);
            }
            List<object> results = new List<object>();
            bool reset = false;
            foreach (object o in J.Arr(J.Get(req, "kills")))
            {
                Dictionary<string, object> res = ProcessKill(book, J.Obj(o), J.Str(req, "client"));
                if (J.Str(res, "status") == "reset")
                {
                    reset = true;
                }
                results.Add(res);
            }
            if (!J.Truthy(req, "dryRun"))
            {
                book.Flush();
                if (reset)
                {
                    try
                    {
                        book.SetProperty(ResetProperty, UnixNow().ToString(CultureInfo.InvariantCulture));
                    }
                    catch (Exception swallowed)
                    {
                        Log.Quiet("SheetLogic: remember the week reset", swallowed);
                        // не страшно: аддон тогда смотрит на сбросы в своём журнале
                    }
                }
            }
            Dictionary<string, object> reply = new Dictionary<string, object>();
            reply["ok"] = true;
            reply["version"] = Program.Version;
            reply["results"] = results;
            return reply;
        }

        public static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        // ------------------------------------------------------------------
        // Одно убийство
        // ------------------------------------------------------------------

        sealed class MarkContext
        {
            public List<object> Cells = new List<object>();
            public List<string> WrongDifficulty = new List<string>();
        }

        static Dictionary<string, object> NewOut(Dictionary<string, object> kill)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            // как { id: kill && kill.id, … } в JS: поля, которых нет в записи, в ответ не попадают
            CopyField(o, kill, "id");
            o["status"] = "not_found";
            CopyField(o, kill, "char");
            CopyField(o, kill, "boss");
            o["cells"] = new List<object>();
            o["message"] = "";
            return o;
        }

        static void CopyField(Dictionary<string, object> to, Dictionary<string, object> from, string key)
        {
            if (from == null)
            {
                to[key] = null;
            }
            else if (from.ContainsKey(key))
            {
                to[key] = from[key];
            }
        }

        static double NowMs()
        {
            return Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
        }

        public static Dictionary<string, object> ProcessKill(IBook book, Dictionary<string, object> kill, string client)
        {
            Dictionary<string, object> o = NewOut(kill);
            if (kill == null || !Truthy(J.Get(kill, "id")) || !Truthy(J.Get(kill, "boss")) || !Truthy(J.Get(kill, "char")))
            {
                o["status"] = "invalid";
                o["message"] = Tr._("в записи нет id, персонажа или босса");
                return o;
            }
            string kind = Truthy(J.Get(kill, "kind")) ? JsString(J.Get(kill, "kind")) : "manual";
            if (kind == "mount")
            {
                return MarkCollected(book, kill, o);   // собранный маунт остаётся собранным и после сброса недели
            }
            if (kind == "plan")
            {
                return SetFarm(book, kill, o);   // «Не фармлю» из игры — не зависит от недели
            }
            double weekEnd = Number(J.Get(kill, "weekEnd"));
            if (weekEnd > 0 && weekEnd * 1000 <= NowMs())
            {
                if (kind != "reset")
                {
                    CountAttempts(book, kill, kind, KillNames(kill), o);   // отметка недели устарела, а попытка была
                }
                o["status"] = "stale";
                o["message"] = Tr._("убийство до еженедельного сброса — пропущено");
                return o;
            }
            if (kind == "reset")
            {
                if (IsOldClient(client))
                {
                    o["status"] = "needs_update";
                    o["message"] = Tr._("обновите программу синхронизации и нажмите «Сброс недели» ещё раз");
                    return o;
                }
                return ResetWeek(book, o);
            }
            HashSet<string> charKeys = CharacterKeys(kill);
            List<string> names = KillNames(kill);
            MarkContext ctx = new MarkContext();
            ctx.Cells = (List<object>)o["cells"];

            List<SheetEntry> lists = new List<SheetEntry>();
            if (kind == "raid" || kind == "manual")
            {
                lists.AddRange(OwnedBy(book, RaidPrefix, charKeys));
            }
            if (kind == "party" || kind == "scenario" || kind == "manual")
            {
                lists.AddRange(OwnedBy(book, DungeonPrefix, charKeys));
            }
            foreach (SheetEntry entry in lists)
            {
                MarkInList(book, entry, kill, names, ctx);
            }
            bool worldFound = false;
            if (kind == "world" || kind == "manual")
            {
                worldFound = MarkInWorld(book, kill, charKeys, names, ctx);
                worldFound = MarkInRare(book, kill, names, ctx) || worldFound;
            }
            CountAttempts(book, kill, kind, names, o);   // «Попыток» на общих листах (SheetAttempts.cs)

            if (ctx.Cells.Any(c => (bool)((Dictionary<string, object>)c)["changed"]))
            {
                o["status"] = "marked";
            }
            else if (ctx.Cells.Count > 0)
            {
                o["status"] = "already";
            }
            else if (lists.Count == 0 && !worldFound)
            {
                o["status"] = "no_sheet";
                o["message"] = NoSheetMessage(kill, kind);
            }
            else if (ctx.WrongDifficulty.Count > 0)
            {
                o["status"] = "wrong_difficulty";
                o["message"] = Tr.F("в таблице этот босс только для сложности: {0}", string.Join("; ", Unique(ctx.WrongDifficulty).ToArray()));
            }
            else
            {
                o["message"] = Tr._("такого босса нет на листах персонажа");
            }
            return o;
        }

        /// <summary>Имена босса записи и, по номеру боя, имена из каталога и журнала игры (таблица на другом языке, «Галеон» / «Галлеон»).</summary>
        static List<string> KillNames(Dictionary<string, object> kill)
        {
            List<string> names = BossNames(J.Get(kill, "boss"));
            foreach (string n in Encounters.NamesFor(J.Get(kill, "encounterID")))
            {
                if (!names.Contains(n))
                {
                    names.Add(n);
                }
            }
            return names;
        }

        /// <summary>«ps-1.0.0» и старше не поддерживают сброс недели.</summary>
        static bool IsOldClient(string client)
        {
            Match m = Regex.Match(client ?? "", @"^ps-(\d+)\.(\d+)");
            if (!m.Success)
            {
                return false;
            }
            int major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return major < 1 || (major == 1 && minor < 1);
        }

        /// <summary>
        /// Сброс недели: «Нет» в колонке «Пытался на этой неделе?» на всех листах персонажей
        /// и снятые галочки персонажей на «Мировых боссах».
        /// </summary>
        static Dictionary<string, object> ResetWeek(IBook book, Dictionary<string, object> o)
        {
            int count = 0;
            HashSet<string> known = new HashSet<string>();
            foreach (SheetEntry entry in book.Entries)
            {
                string owner = SheetOwner(entry.Name, RaidPrefix) ?? SheetOwner(entry.Name, DungeonPrefix);
                if (owner == null)
                {
                    continue;
                }
                known.Add(Norm(owner));
                List<object[]> data = entry.Data;
                int cStatus = data.Count > 0 ? FindCol(data[0], "пытался", -1) : -1;
                if (cStatus < 0)
                {
                    continue;   // без заголовка «Пытался…» лист не трогаем
                }
                int cParts = FindCol(data[0], PartsPattern, -1);
                for (int r = 1; r < data.Count; r++)
                {
                    count += ResetCell(book, entry, r, cStatus);
                    if (cParts >= 0 && CellText(Cell(entry, r, cParts)).Length > 0)
                    {
                        book.Set(entry, r, cParts, "");
                    }
                }
            }
            SheetEntry world = ByName(book, WorldSheet);
            if (world != null && world.Data.Count > 0)
            {
                object[] header = world.Data[0];
                for (int c = 0; c < header.Length; c++)
                {
                    if (!IsCharacterColumn(world.Data, c, known))
                    {
                        continue;
                    }
                    for (int r = 1; r < world.Data.Count; r++)
                    {
                        count += ResetCell(book, world, r, c);
                    }
                }
            }
            o["status"] = "reset";
            o["count"] = (double)count;
            o["message"] = Tr.F("«Нет» поставлено, ячеек: {0}", count);
            return o;
        }

        static int ResetCell(IBook book, SheetEntry entry, int r, int c)
        {
            object value = Cell(entry, r, c);
            if (value is bool && (bool)value)
            {
                book.Set(entry, r, c, false);
                return 1;
            }
            if (value is string && IsYes(value))
            {
                book.Set(entry, r, c, No);
                return 1;
            }
            return 0;
        }

        // ------------------------------------------------------------------
        // Новый маунт → «Собрано?»
        // ------------------------------------------------------------------

        static Dictionary<string, object> MarkCollected(IBook book, Dictionary<string, object> kill, Dictionary<string, object> o)
        {
            o["kind"] = "mount";
            string name = Truthy(J.Get(kill, "mount")) ? JsString(J.Get(kill, "mount")) : JsString(J.Get(kill, "boss"));
            string from = JsString(J.Get(kill, "from"));
            List<object> cells = (List<object>)o["cells"];
            foreach (SheetEntry entry in book.Entries)
            {
                List<object[]> data = entry.Data;
                if (data.Count == 0)
                {
                    continue;
                }
                object[] header = data[0];
                int cCollected = FindCol(header, "собрано", -1);
                int cMount = FindCol(header, "маунт", -1);
                if (cCollected < 0 || cMount < 0)
                {
                    continue;
                }
                int cBoss = FindCol(header, "босс", -1);
                // таблица программы 2.1+: скрытая колонка «ID в игре» — mountID точнее имени
                int cId = FindCol(header, "^id в игре$", -1);
                long wantId = MountIndex.IdOf(J.Get(kill, "mountID"));
                for (int r = 1; r < data.Count; r++)
                {
                    object[] row = data[r];
                    long rowId = cId >= 0 ? MountIndex.IdOf(row[cId]) : 0;
                    bool hit = wantId > 0 && rowId > 0
                        ? rowId == wantId
                        : MountMatches(name, row[cMount], from, cBoss >= 0 ? row[cBoss] : "");
                    if (!hit)
                    {
                        continue;
                    }
                    if (entry.Formulas.Contains(Key(r, cCollected)))
                    {
                        continue;   // значение считает формула — его не трогаем
                    }
                    object current = row[cCollected];
                    bool changed = false;
                    if (IsNo(current))
                    {
                        book.Set(entry, r, cCollected, current is bool ? (object)true : Yes);
                        changed = true;
                    }
                    else if (!IsYes(current))
                    {
                        continue;
                    }
                    cells.Add(CellInfo(entry, r, Truthy(row[cMount]) ? JsString(row[cMount]) : "", changed));
                }
            }
            if (cells.Any(c => (bool)((Dictionary<string, object>)c)["changed"]))
            {
                o["status"] = "collected";
            }
            else if (cells.Count > 0)
            {
                o["status"] = "already";
            }
            else
            {
                o["status"] = "not_found";
                o["message"] = Tr.F("маунта «{0}» нет в колонке «Маунт» — отметьте «Собрано?» вручную", name);
            }
            return o;
        }

        /// <summary>
        /// «Не фармлю» (или «фармлю») из игры: галочка «Фармлю» на листах персонажа. scope = "place" — все строки
        /// этого рейда / подземелья, иначе строка с тем же маунтом и боссом (и сложностью, если указана).
        /// </summary>
        static Dictionary<string, object> SetFarm(IBook book, Dictionary<string, object> kill, Dictionary<string, object> o)
        {
            o["kind"] = "plan";
            bool farm = Truthy(J.Get(kill, "farm"));
            string list = J.Str(kill, "list");
            bool byPlace = J.Str(kill, "scope") == "place";
            string mount = Norm(J.Get(kill, "mount"));
            string boss = Norm(J.Get(kill, "boss"));
            string diff = Norm(J.Get(kill, "diff"));
            string place = Norm(J.Get(kill, "place"));
            HashSet<string> charKeys = CharacterKeys(kill);
            List<SheetEntry> sheets = new List<SheetEntry>();
            if (list != "dungeon")
            {
                sheets.AddRange(OwnedBy(book, RaidPrefix, charKeys));
            }
            if (list != "raid")
            {
                sheets.AddRange(OwnedBy(book, DungeonPrefix, charKeys));
            }
            List<object> cells = (List<object>)o["cells"];
            bool noColumn = false;
            foreach (SheetEntry entry in sheets)
            {
                List<object[]> data = entry.Data;
                if (data.Count == 0)
                {
                    continue;
                }
                object[] header = data[0];
                int cFarm = FindCol(header, "^фармлю", -1);
                if (cFarm < 0)
                {
                    noColumn = true;
                    continue;
                }
                int cBoss = FindCol(header, "босс", 4);
                int cDiff = FindCol(header, "сложност|режим", 5);
                int cMount = FindCol(header, "маунт", 3);
                int cPlace = FindCol(header, "^рейд|подземел", 2);
                for (int r = 1; r < data.Count; r++)
                {
                    object[] row = data[r];
                    bool hit = byPlace
                        ? place.Length > 0 && Norm(At(row, cPlace)) == place
                        : (mount.Length > 0 || boss.Length > 0) && Norm(At(row, cMount)) == mount && Norm(At(row, cBoss)) == boss
                            && (diff.Length == 0 || Norm(At(row, cDiff)) == diff);
                    if (!hit || entry.Formulas.Contains(Key(r, cFarm)))
                    {
                        continue;
                    }
                    object current = At(row, cFarm);
                    bool now = !IsUnticked(current);
                    bool changed = now != farm;
                    if (changed)
                    {
                        book.Set(entry, r, cFarm, farm);
                    }
                    cells.Add(CellInfo(entry, r, CellText(At(row, cMount)), changed));
                }
            }
            if (cells.Any(c => (bool)((Dictionary<string, object>)c)["changed"]))
            {
                o["status"] = "planned";
            }
            else if (cells.Count > 0)
            {
                o["status"] = "already";
            }
            else
            {
                o["status"] = "not_found";
                o["message"] = noColumn
                    ? Tr._("на листе персонажа нет колонки «Фармлю»")
                    : Tr.F("строки «{0}» нет на листах персонажа", byPlace ? J.Str(kill, "place") : J.Str(kill, "boss"));
            }
            return o;
        }

        static Dictionary<string, object> CellInfo(SheetEntry entry, int r, string mount, bool changed)
        {
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["sheet"] = entry.Name;
            c["row"] = (double)(r + 1);
            c["mount"] = mount;
            c["changed"] = changed;
            return c;
        }

        /// <summary>Слова, по которым игра и таблица называют предмет («Поводья …») и сам маунт.</summary>
        static readonly HashSet<string> ItemWords = new HashSet<string> { "поводья", "повод", "уздечка", "свисток", "reins" };

        static readonly Regex Brackets = new Regex(@"\([^)]*\)|\[[^\]]*\]");

        /// <summary>Маунты, у которых в общих листах «Собрано?» = «Да» (по тексту ячейки «Маунт»).</summary>
        public static HashSet<string> CollectedMounts(IBook book)
        {
            HashSet<string> set = new HashSet<string>();
            foreach (SheetEntry entry in book.Entries)
            {
                List<object[]> data = entry.Data;
                if (data.Count == 0)
                {
                    continue;
                }
                int cCollected = FindCol(data[0], "собрано", -1);
                int cMount = FindCol(data[0], "маунт", -1);
                if (cCollected < 0 || cMount < 0)
                {
                    continue;
                }
                for (int r = 1; r < data.Count; r++)
                {
                    string key = Norm(At(data[r], cMount));
                    if (key.Length > 0 && IsYes(At(data[r], cCollected)))
                    {
                        set.Add(key);
                    }
                }
            }
            return set;
        }

        /// <summary>Как аддон найдёт своего персонажа: имя листа и имена из CharacterAliases.</summary>
        static List<string> OwnerKeys(string owner)
        {
            List<string> keys = new List<string> { Norm(owner) };
            foreach (KeyValuePair<string, string> kv in CharacterAliases)
            {
                if (Norm(StripNote(kv.Value)) == Norm(owner) && !keys.Contains(Norm(kv.Key)))
                {
                    keys.Add(Norm(kv.Key));
                }
            }
            return keys.Where(k => k.Length > 0).ToList();
        }

        // ------------------------------------------------------------------
        // Служебное
        // ------------------------------------------------------------------

        public static string CellText(object value)
        {
            return JsString(value).Trim();
        }

        public static SheetEntry ByName(IBook book, string name)
        {
            string n = Norm(name);
            return book.Entries.FirstOrDefault(e => Norm(e.Name) == n);
        }

        static List<SheetEntry> OwnedBy(IBook book, string prefix, HashSet<string> charKeys)
        {
            return book.Entries.Where(e =>
            {
                string owner = SheetOwner(e.Name, prefix);
                return owner != null && charKeys.Contains(Norm(owner));
            }).ToList();
        }

        // Колонка «Фармлю» на листе персонажа (-1 — нет).
        public static int FarmCol(object[] header)
        {
            return FindCol(header, "^фармлю", -1);
        }

        public static int FindCol(object[] header, string pattern, int fallback)
        {
            Regex re = new Regex(pattern);
            for (int i = 0; i < header.Length; i++)
            {
                if (re.IsMatch(Norm(header[i])))
                {
                    return i;
                }
            }
            return fallback;
        }

        static readonly Regex NoWord = new Regex("^(нет|no|false)$");
        static readonly Regex YesWord = new Regex("^(да|yes|true)$");

        public static bool IsNo(object value)
        {
            if (value is bool)
            {
                return !(bool)value;
            }
            if (value is string && (string)value == "")
            {
                return true;
            }
            return value != null && NoWord.IsMatch(Norm(value));
        }

        /// <summary>Галочка снята (FALSE или «Нет»); пустая ячейка — не снята.</summary>
        public static bool IsUnticked(object value)
        {
            if (value is bool)
            {
                return !(bool)value;
            }
            string s = value as string;
            return s != null && s.Trim().Length > 0 && NoWord.IsMatch(Norm(s));
        }

        public static bool IsYes(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }
            return YesWord.IsMatch(Norm(value));
        }

        static List<string> Unique(IEnumerable<string> list)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string s in list)
            {
                if (seen.Add(s))
                {
                    result.Add(s);
                }
            }
            return result;
        }

        static string NoSheetMessage(Dictionary<string, object> kill, string kind)
        {
            string who = JsString(J.Get(kill, "char"));
            if (kind == "raid")
            {
                return Tr.F("нет листа «{0}»", RaidPrefix + " " + who);
            }
            if (kind == "party" || kind == "scenario")
            {
                return Tr.F("нет листа «{0}»", DungeonPrefix + " " + who);
            }
            if (kind == "world")
            {
                return Tr.F("нет колонки «{0}» на листе «{1}»", who, WorldSheet);
            }
            return Tr.F("нет листов для персонажа «{0}»", who);
        }

        public static long Key(int r, int c)
        {
            return (long)r * 100000 + c;
        }

        static object Cell(SheetEntry entry, int r, int c)
        {
            return Cell(entry.Data, r, c);
        }

        static object Cell(List<object[]> data, int r, int c)
        {
            if (r < 0 || r >= data.Count)
            {
                return "";
            }
            return At(data[r], c);
        }

        public static object At(object[] row, int c)
        {
            // как row[c] в JS: за пределами строки — undefined
            return c >= 0 && c < row.Length ? row[c] : null;
        }

        /// <summary>Как String(value) в JavaScript: 25 → "25", true → "true", null → "".</summary>
        public static string JsString(object value)
        {
            if (value == null)
            {
                return "";
            }
            string s = value as string;
            if (s != null)
            {
                return s;
            }
            if (value is bool)
            {
                return (bool)value ? "true" : "false";
            }
            if (value is double || value is long || value is int || value is float || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d))
                {
                    return "NaN";
                }
                if (Math.Floor(d) == d && Math.Abs(d) < 1e15)
                {
                    return ((long)d).ToString(CultureInfo.InvariantCulture);
                }
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Как !!value в JavaScript.</summary>
        static bool Truthy(object value)
        {
            if (value == null)
            {
                return false;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            if (value is double)
            {
                double d = (double)value;
                return d != 0 && !double.IsNaN(d);
            }
            string s = value as string;
            return s == null || s.Length > 0;
        }

        /// <summary>Как Number(value) в JavaScript (для чисел и строк с числом); иначе NaN.</summary>
        public static double Number(object value)
        {
            if (value == null)
            {
                return double.NaN;
            }
            if (value is double || value is long || value is int)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            if (value is bool)
            {
                return (bool)value ? 1 : 0;
            }
            string s = (value as string ?? "").Trim();
            if (s.Length == 0)
            {
                return 0;
            }
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
        }
    }

#if TESTS
    /// <summary>Таблица в памяти — для проверок (в выпуск программы не входит).</summary>
    sealed class MemoryBook : IStructuredBook
    {
        public string Creator = "";

        public string CreatorApp { get { return Creator; } }

        public SheetLang Lang { get { return SheetLang.Russian; } }

        public bool Reusable { get { return true; } }

        /// <summary>Правки структуры (для проверок): «column <лист>» / «delete <лист> <строки>».</summary>
        public readonly List<string> Structure = new List<string>();

        public void AppendColumn(SheetEntry entry, List<object> values, bool hidden)
        {
            Flush();
            Structure.Add("column " + entry.Name + (hidden ? " hidden" : ""));
            BookEdit.AppendColumn(entry, values);
        }

        /// <summary>Пакеты Apply (для проверок).</summary>
        public readonly List<List<object>> Applied = new List<List<object>>();

        public void Apply(List<object> requests, string propertyKey, string propertyValue)
        {
            Flush();
            Applied.Add(new List<object>(requests));
            Structure.Add("apply " + requests.Count.ToString(CultureInfo.InvariantCulture) + (propertyKey != null ? " " + propertyKey + "=" + propertyValue : ""));
            if (propertyKey != null)
            {
                Properties[propertyKey] = propertyValue;
            }
        }

        public void DeleteRows(Dictionary<SheetEntry, List<int>> rows)
        {
            Flush();
            foreach (KeyValuePair<SheetEntry, List<int>> kv in rows)
            {
                Structure.Add("delete " + kv.Key.Name + " " + string.Join(",", kv.Value.OrderBy(r => r).Select(r => (r + 1).ToString(CultureInfo.InvariantCulture)).ToArray()));
                BookEdit.DeleteRows(kv.Key, kv.Value);
            }
        }

        public string BookTitle = "";
        public string BookUrl = "";
        readonly List<SheetEntry> entries = new List<SheetEntry>();
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();
        public readonly List<KeyValuePair<SheetEntry, int[]>> Pending = new List<KeyValuePair<SheetEntry, int[]>>();
        /// <summary>Сколько раз пришлось бы писать в таблицу (подряд идущие ячейки одной колонки — один раз).</summary>
        public readonly List<int> Calls = new List<int>();
        public readonly List<Dictionary<string, object>> Writes = new List<Dictionary<string, object>>();
        public bool PropertiesBroken;

        public string Title { get { return BookTitle; } }
        public string Url { get { return BookUrl; } }
        public List<SheetEntry> Entries { get { return entries; } }

        public void Set(SheetEntry entry, int r, int c, object value)
        {
            entry.Data[r][c] = value;
            Pending.Add(new KeyValuePair<SheetEntry, int[]>(entry, new[] { r, c }));
        }

        public void Flush()
        {
            foreach (CellRun run in CellRun.Group(Pending.Select(p => new CellRun.Cell(p.Key, p.Value[0], p.Value[1]))))
            {
                Calls.Add(run.Rows);
                for (int i = 0; i < run.Rows; i++)
                {
                    Dictionary<string, object> w = new Dictionary<string, object>();
                    w["sheet"] = run.Entry.Name;
                    w["row"] = (double)(run.Row + i + 1);
                    w["col"] = (double)(run.Col + 1);
                    w["value"] = run.Entry.Data[run.Row + i][run.Col];
                    Writes.Add(w);
                }
            }
            Pending.Clear();
        }

        public string GetProperty(string key)
        {
            if (PropertiesBroken)
            {
                throw new InvalidOperationException("Service unavailable: Properties");
            }
            string v;
            return Properties.TryGetValue(key, out v) ? v : null;
        }

        public void SetProperty(string key, string value)
        {
            if (PropertiesBroken)
            {
                throw new InvalidOperationException("Service unavailable: Properties");
            }
            Properties[key] = value;
        }
    }
#endif

    /// <summary>Подряд идущие изменённые ячейки одной колонки — их пишут одним куском.</summary>
    sealed class CellRun
    {
        public SheetEntry Entry;
        public int Row;
        public int Col;
        public int Rows;

        public sealed class Cell
        {
            public SheetEntry Entry;
            public int R;
            public int C;
            public Cell(SheetEntry entry, int r, int c)
            {
                Entry = entry;
                R = r;
                C = c;
            }
        }

        public static List<CellRun> Group(IEnumerable<Cell> cells)
        {
            // последняя запись в ячейку побеждает, порядок колонок — как в первой записи
            List<string> columnOrder = new List<string>();
            Dictionary<string, SortedSet<int>> columns = new Dictionary<string, SortedSet<int>>();
            Dictionary<string, Cell> sample = new Dictionary<string, Cell>();
            foreach (Cell c in cells)
            {
                string key = c.Entry.Name + "|" + c.C;
                SortedSet<int> rows;
                if (!columns.TryGetValue(key, out rows))
                {
                    rows = new SortedSet<int>();
                    columns[key] = rows;
                    columnOrder.Add(key);
                    sample[key] = c;
                }
                rows.Add(c.R);
            }
            List<CellRun> runs = new List<CellRun>();
            foreach (string key in columnOrder)
            {
                Cell s = sample[key];
                CellRun current = null;
                foreach (int r in columns[key])
                {
                    if (current != null && r == current.Row + current.Rows)
                    {
                        current.Rows++;
                        continue;
                    }
                    current = new CellRun { Entry = s.Entry, Row = r, Col = s.C, Rows = 1 };
                    runs.Add(current);
                }
            }
            return runs;
        }
    }
}
