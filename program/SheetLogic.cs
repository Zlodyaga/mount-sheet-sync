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
            "Galleon", "Галлеон",
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
                    catch (Exception)
                    {
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
                for (int r = 1; r < data.Count; r++)
                {
                    count += ResetCell(book, entry, r, cStatus);
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

        public static List<string> MountWords(object value)
        {
            string text = Brackets.Replace(JsString(value), " ");
            return Norm(text).Split(' ').Where(w => w.Length > 0 && !ItemWords.Contains(w)).ToList();
        }

        /// <summary>Одно слово в разных падежах: «синий» ↔ «синего».</summary>
        static bool SameStem(string a, string b)
        {
            if (a == b)
            {
                return true;
            }
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i])
            {
                i++;
            }
            return i >= 3 && a.Length - i <= 3 && b.Length - i <= 3;
        }

        /// <summary>Каждому слову из xs нашлось своё слово в ys.</summary>
        static bool CoversWords(List<string> xs, List<string> ys)
        {
            bool[] used = new bool[ys.Count];
            foreach (string x in xs)
            {
                int j = -1;
                for (int k = 0; k < ys.Count; k++)
                {
                    if (!used[k] && SameStem(x, ys[k]))
                    {
                        j = k;
                        break;
                    }
                }
                if (j < 0)
                {
                    return false;
                }
                used[j] = true;
            }
            return true;
        }

        /// <summary>Имя маунта из игры ↔ ячейка «Маунт» (с учётом падежей и источника).</summary>
        public static bool MountMatches(string gameName, object cell, string sourceText, object bossCell)
        {
            List<string> a = MountWords(gameName);
            List<string> b = MountWords(cell);
            if (a.Count == 0 || b.Count == 0)
            {
                return false;
            }
            if (a.Count == b.Count && CoversWords(a, b))
            {
                return true;
            }
            return BossNamed(sourceText, bossCell) && (CoversWords(b, a) || CoversWords(a, b));
        }

        /// <summary>Текст источника («Добыча: Скади Безжалостный / …») называет босса строки.</summary>
        static bool BossNamed(string sourceText, object bossCell)
        {
            HashSet<string> words = new HashSet<string>(Norm(sourceText).Split(' ').Where(w => w.Length > 0));
            if (words.Count == 0)
            {
                return false;
            }
            foreach (string alt in JsString(bossCell).Split('/'))
            {
                string[] need = Norm(alt).Split(' ').Where(w => w.Length > 0).ToArray();
                if (need.Length > 0 && need.All(w => words.Contains(w)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Колонка персонажа на «Мировых боссах»: по имени из листов или колонка из одних галочек.</summary>
        static bool IsCharacterColumn(List<object[]> data, int c, HashSet<string> known)
        {
            string head = Norm(StripNote(Cell(data, 0, c)));
            if (head.Length > 0 && known.Contains(head))
            {
                return true;
            }
            int checkboxes = 0;
            for (int r = 1; r < data.Count; r++)
            {
                object value = Cell(data, r, c);
                if (value == null || (value is string && (string)value == ""))
                {
                    continue;
                }
                if (!(value is bool))
                {
                    return false;
                }
                checkboxes++;
            }
            return checkboxes > 0;
        }

        /// <summary>Листы «Рейды &lt;Имя&gt;» / «Подземелья &lt;Имя&gt;».</summary>
        static void MarkInList(IBook book, SheetEntry entry, Dictionary<string, object> kill, List<string> names, MarkContext ctx)
        {
            List<object[]> data = entry.Data;
            if (data.Count == 0)
            {
                return;
            }
            object[] header = data[0];
            int cStatus = FindCol(header, "пытался", 0);
            int cBoss = FindCol(header, "босс", 4);
            int cDiff = FindCol(header, "сложност|режим", 5);
            int cMount = FindCol(header, "маунт", 3);
            int cPlace = FindCol(header, "^рейд|подземел", 2);
            for (int r = 1; r < data.Count; r++)
            {
                object[] row = data[r];
                Match3 match = RowMatch(kill, names, At(row, cBoss), At(row, cDiff), At(row, cPlace));
                if (match == Match3.Difficulty)
                {
                    ctx.WrongDifficulty.Add(JsString(At(row, cDiff)));
                    continue;
                }
                if (match != Match3.Yes)
                {
                    continue;
                }
                object current = At(row, cStatus);
                bool changed = false;
                if (IsNo(current))
                {
                    book.Set(entry, r, cStatus, current is bool ? (object)true : Yes);
                    changed = true;
                }
                else if (!IsYes(current))
                {
                    continue;   // «Недоступно» и прочие значения не трогаем
                }
                ctx.Cells.Add(CellInfo(entry, r, Truthy(At(row, cMount)) ? JsString(At(row, cMount)) : "", changed));
            }
        }

        /// <summary>Лист «Мировые боссы»: галочка в колонке персонажа.</summary>
        static bool MarkInWorld(IBook book, Dictionary<string, object> kill, HashSet<string> charKeys, List<string> names, MarkContext ctx)
        {
            SheetEntry entry = ByName(book, WorldSheet);
            if (entry == null || entry.Data.Count == 0)
            {
                return false;
            }
            List<object[]> data = entry.Data;
            object[] header = data[0];
            int cBoss = FindCol(header, "босс", 4);
            int cMount = FindCol(header, "маунт", 3);
            int cChar = -1;
            for (int i = 0; i < header.Length; i++)
            {
                if (charKeys.Contains(Norm(StripNote(header[i]))))
                {
                    cChar = i;
                    break;
                }
            }
            if (cChar < 0)
            {
                return false;
            }
            bool loose = J.Str(kill, "kind") == "manual";
            for (int r = 1; r < data.Count; r++)
            {
                object[] row = data[r];
                string text = JsString(At(row, cBoss));
                bool hit = text.Trim().Length > 0 && text.Split('/').Any(alt => names.Any(n => NameMatches(n, alt, loose)));
                if (!hit)
                {
                    continue;
                }
                object current = At(row, cChar);
                bool changed = false;
                if (IsNo(current))
                {
                    book.Set(entry, r, cChar, current is bool ? (object)true : Yes);
                    changed = true;
                }
                else if (!IsYes(current))
                {
                    continue;
                }
                ctx.Cells.Add(CellInfo(entry, r, Truthy(At(row, cMount)) ? JsString(At(row, cMount)) : "", changed));
            }
            return true;
        }

        enum Match3 { No, Yes, Difficulty }

        /// <summary>Yes — строка подходит; Difficulty — босс тот, сложность другая; No — нет.</summary>
        static Match3 RowMatch(Dictionary<string, object> kill, List<string> names, object bossCell, object diffCell, object placeCell)
        {
            string text = JsString(bossCell).Trim();
            if (text.Length == 0)
            {
                return Match3.No;
            }
            bool manual = J.Str(kill, "kind") == "manual";
            bool named = false;
            bool anyBoss = false;
            foreach (string alt in text.Split('/'))
            {
                if (IsAnyBoss(alt))
                {
                    if (!manual && AnyBossApplies(kill, alt, diffCell, placeCell))
                    {
                        anyBoss = true;
                    }
                }
                else if (names.Any(n => NameMatches(n, alt, manual)))
                {
                    named = true;
                }
            }
            if (!named && !anyBoss)
            {
                return Match3.No;
            }
            if (manual)
            {
                return Match3.Yes;   // отметка вручную — сложность не проверяем
            }
            if (named && DifficultyMatches(J.Get(kill, "difficultyID"), diffCell, false))
            {
                return Match3.Yes;
            }
            // «Любой босс …» засчитываем только при точно известной подходящей сложности
            if (anyBoss && DifficultyMatches(J.Get(kill, "difficultyID"), diffCell, true))
            {
                return Match3.Yes;
            }
            return named ? Match3.Difficulty : Match3.No;
        }

        // ------------------------------------------------------------------
        // Сравнение имён
        // ------------------------------------------------------------------

        static readonly Regex Apostrophes = new Regex("['’‘`ʼ´]");
        static readonly Regex NonWord = new Regex(@"[^\p{L}\p{N}]+");
        static readonly Regex Spaces = new Regex(@"\s+");
        static readonly Regex Parens = new Regex(@"\([^)]*\)");

        public static string Norm(object value)
        {
            string s = JsString(value).ToLowerInvariant().Replace('ё', 'е');
            s = Apostrophes.Replace(s, "");
            return NonWord.Replace(s, " ").Trim();
        }

        public static string StripNote(object value)
        {
            return Spaces.Replace(Parens.Replace(JsString(value), " "), " ").Trim();
        }

        /// <summary>«Фиракк» в таблице совпадает с «Фиракк Пылающий»; loose — хватает одного слова.</summary>
        public static bool NameMatches(object gameName, object tableName, bool loose)
        {
            string a = Norm(gameName);
            string b = Norm(tableName);
            if (a.Length == 0 || b.Length == 0)
            {
                return false;
            }
            if (a == b)
            {
                return true;
            }
            string[] wa = a.Split(' ');
            string[] wb = b.Split(' ');
            HashSet<string> sa = new HashSet<string>(wa);
            HashSet<string> sb = new HashSet<string>(wb);
            if (b.Length >= 4 && wb.All(w => sa.Contains(w)))
            {
                return true;
            }
            int minWords = loose && a.Length >= 4 ? 1 : 2;
            return wa.Length >= minWords && wa.All(w => sb.Contains(w));
        }

        static List<string> BossNames(object boss)
        {
            List<string> names = new List<string> { JsString(boss) };
            foreach (KeyValuePair<string, string> kv in BossAliases)
            {
                if (NameMatches(boss, kv.Key, false))
                {
                    names.Add(kv.Value);
                }
            }
            return names;
        }

        public static HashSet<string> CharacterKeys(Dictionary<string, object> kill)
        {
            string name = JsString(J.Get(kill, "char")).Trim();
            string realm = JsString(J.Get(kill, "realm")).Trim();
            HashSet<string> keys = new HashSet<string>();
            Action<string> add = delegate(string v)
            {
                string n = Norm(v);
                if (n.Length > 0)
                {
                    keys.Add(n);
                }
            };
            add(name);
            if (name.Length > 0 && realm.Length > 0)
            {
                add(name + "-" + realm);
                add(name + "-" + Spaces.Replace(realm, ""));
            }
            foreach (KeyValuePair<string, string> kv in CharacterAliases)
            {
                if (keys.Contains(Norm(kv.Key)))
                {
                    add(kv.Value);
                }
            }
            return keys;
        }

        /// <summary>«Рейды Каэлия (Лок)» → «Каэлия»; общий лист «Рейды» → null.</summary>
        public static string SheetOwner(string sheetName, string prefix)
        {
            string name = (sheetName ?? "").Trim();
            if (name.Length < prefix.Length || name.Substring(0, prefix.Length).ToLowerInvariant() != prefix.ToLowerInvariant())
            {
                return null;
            }
            string rest = name.Substring(prefix.Length);
            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            {
                return null;
            }
            string owner = StripNote(rest);
            return owner.Length > 0 ? owner : null;
        }

        static readonly Regex AnyBoss = new Regex("^(любой|любые|все) босс|^any boss|^боссы ");

        /// <summary>«Любой босс …» и «Боссы …» — строки, которые подходят к любому боссу места.</summary>
        static bool IsAnyBoss(string text)
        {
            return AnyBoss.IsMatch(Norm(text));
        }

        static readonly Regex TimewalkingWord = new Regex("путешеств|timewalking");

        static bool AnyBossApplies(Dictionary<string, object> kill, string text, object diffCell, object placeCell)
        {
            if (ParseDifficulty(diffCell).Kinds.Contains("timewalking") || TimewalkingWord.IsMatch(Norm(text)))
            {
                return true;   // «Любой босс подземелий путешествий во времени» — проверим только режим
            }
            object instance = J.Get(kill, "instance");
            return PlaceMentioned(instance, text) || PlaceMentioned(instance, placeCell);
        }

        static readonly HashSet<string> PlaceStopWords = new HashSet<string>
        {
            "любой", "любые", "все", "всех", "босс", "боссы", "боссов", "any", "boss", "bosses", "the",
        };

        public static List<string> PlaceWords(object text)
        {
            return Norm(text).Split(' ').Where(w => w.Length >= 3 && !PlaceStopWords.Contains(w)).ToList();
        }

        /// <summary>Подземелье названо в тексте (с учётом падежей).</summary>
        static bool PlaceMentioned(object place, object text)
        {
            List<string> a = PlaceWords(place);
            List<string> b = PlaceWords(text);
            if (a.Count == 0 || b.Count == 0)
            {
                return false;
            }
            return a.All(x => b.Any(y => SameWord(x, y))) || b.All(x => a.Any(y => SameWord(x, y)));
        }

        static bool SameWord(string a, string b)
        {
            if (a == b)
            {
                return true;
            }
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i])
            {
                i++;
            }
            return i >= Math.Max(4, Math.Min(a.Length, b.Length) - 2);
        }

        // ------------------------------------------------------------------
        // Сложность
        // ------------------------------------------------------------------

        sealed class Diff
        {
            public string Kind;
            public int Size;
            public Diff(string kind, int size)
            {
                Kind = kind;
                Size = size;
            }
        }

        /// <summary>DifficultyID из игры → вид и размер рейда (по возрастанию ID, как Object.keys в JS).</summary>
        static readonly SortedDictionary<int, Diff> Difficulty = new SortedDictionary<int, Diff>
        {
            { 1, new Diff("normal", 0) },
            { 2, new Diff("heroic", 0) },
            { 3, new Diff("normal", 10) },
            { 4, new Diff("normal", 25) },
            { 5, new Diff("heroic", 10) },
            { 6, new Diff("heroic", 25) },
            { 7, new Diff("lfr", 0) },
            { 8, new Diff("mythicplus", 0) },
            { 9, new Diff("normal", 40) },
            { 14, new Diff("normal", 0) },
            { 15, new Diff("heroic", 0) },
            { 16, new Diff("mythic", 0) },
            { 17, new Diff("lfr", 0) },
            { 23, new Diff("mythic", 0) },
            { 24, new Diff("timewalking", 0) },
            { 33, new Diff("timewalking", 0) },
            { 150, new Diff("normal", 0) },
            { 151, new Diff("timewalking", 0) },
            { 205, new Diff("follower", 0) },
            { 220, new Diff("story", 0) },
        };

        public static List<object> KnownDifficultyIds()
        {
            return Difficulty.Keys.Select(k => (object)(double)k).ToList();
        }

        sealed class DiffReq
        {
            public bool Any;
            public HashSet<string> Kinds = new HashSet<string>();
            public HashSet<int> Sizes = new HashSet<int>();
        }

        static readonly Regex AnyDiff = new Regex("любая|любой|any(?![a-z])|все сложности");
        static readonly Regex MythicPlus = new Regex(@"(mythic|мифик|эпохал[а-я]*)\s*\+|(^|[^a-z])m\+|ключ|keystone|challenge");
        static readonly Regex MythicPlusA = new Regex(@"(mythic|мифик|эпохал[а-я]*)\s*\+");
        static readonly Regex MythicPlusB = new Regex(@"(^|[^a-z])m\+");
        static readonly Regex MythicWord = new Regex("mythic|мифик|эпохал");
        static readonly Regex HeroicWord = new Regex("heroic|героич");
        static readonly Regex NormalWord = new Regex("normal|обычн|нормал");
        static readonly Regex LfrWord = new Regex("(^|[^a-z])lfr([^a-z]|$)|поиск рейда|raid finder");
        static readonly Regex TimewalkingDiff = new Regex("timewalking|путешеств");

        /// <summary>Разбор текста «Сложность / режим»: Normal/Heroic, Mythic, Heroic 25, 10/25, Любая…</summary>
        static DiffReq ParseDifficulty(object text)
        {
            string s = JsString(text).ToLowerInvariant().Replace('ё', 'е');
            DiffReq req = new DiffReq();
            if (s.Trim().Length == 0)
            {
                return req;
            }
            if (AnyDiff.IsMatch(s))
            {
                req.Any = true;
            }
            if (MythicPlus.IsMatch(s))
            {
                req.Kinds.Add("mythicplus");
            }
            string rest = MythicPlusB.Replace(MythicPlusA.Replace(s, " "), " ");
            if (MythicWord.IsMatch(rest))
            {
                req.Kinds.Add("mythic");
            }
            if (HeroicWord.IsMatch(s))
            {
                req.Kinds.Add("heroic");
            }
            if (NormalWord.IsMatch(s))
            {
                req.Kinds.Add("normal");
            }
            if (LfrWord.IsMatch(s))
            {
                req.Kinds.Add("lfr");
            }
            if (TimewalkingDiff.IsMatch(s))
            {
                req.Kinds.Add("timewalking");
            }
            foreach (int n in new[] { 10, 25, 40 })
            {
                if (Regex.IsMatch(s, @"(^|\D)" + n + @"(\D|$)"))
                {
                    req.Sizes.Add(n);
                }
            }
            return req;
        }

        /// <summary>strict: неизвестная сложность не подходит (для строк «Любой босс …»).</summary>
        static bool DifficultyMatches(object difficultyId, object diffCell, bool strict)
        {
            DiffReq req = ParseDifficulty(diffCell);
            if (req.Any || (req.Kinds.Count == 0 && req.Sizes.Count == 0))
            {
                return true;
            }
            double id = Number(difficultyId);
            Diff d;
            if (double.IsNaN(id) || id != Math.Floor(id) || !Difficulty.TryGetValue((int)id, out d))
            {
                return !strict;   // неизвестная сложность — обычную строку не блокируем
            }
            if (req.Kinds.Count > 0 && !req.Kinds.Contains(d.Kind) && !(d.Kind == "mythicplus" && req.Kinds.Contains("mythic")))
            {
                return false;
            }
            return !(req.Sizes.Count > 0 && d.Size != 0 && !req.Sizes.Contains(d.Size));
        }

        // ------------------------------------------------------------------
        // Проверка связи
        // ------------------------------------------------------------------

        static Dictionary<string, object> Ping(IBook book, Dictionary<string, object> req)
        {
            List<string> order = new List<string>();
            Dictionary<string, Dictionary<string, object>> chars = new Dictionary<string, Dictionary<string, object>>();
            foreach (SheetEntry e in book.Entries)
            {
                foreach (string prefix in new[] { RaidPrefix, DungeonPrefix })
                {
                    string owner = SheetOwner(e.Name, prefix);
                    if (owner == null)
                    {
                        continue;
                    }
                    string key = Norm(owner);
                    if (!chars.ContainsKey(key))
                    {
                        Dictionary<string, object> c = new Dictionary<string, object>();
                        c["name"] = owner;
                        c["sheets"] = new List<object>();
                        chars[key] = c;
                        order.Add(key);
                    }
                    ((List<object>)chars[key]["sheets"]).Add(e.Name);
                }
            }
            SheetEntry world = ByName(book, WorldSheet);
            if (world != null && world.Data.Count > 0)
            {
                HashSet<string> known = new HashSet<string>(order);
                int cCollected = FindCol(world.Data[0], "собрано", -1);
                for (int i = 0; i < world.Data[0].Length; i++)
                {
                    object h = world.Data[0][i];
                    string key = Norm(StripNote(h));
                    if (key.Length == 0)
                    {
                        continue;
                    }
                    if (!chars.ContainsKey(key))
                    {
                        // персонаж только с колонкой на «Мировых боссах» (без листов рейдов и подземелий)
                        if (i == cCollected || !IsCharacterColumn(world.Data, i, known))
                        {
                            continue;
                        }
                        Dictionary<string, object> c = new Dictionary<string, object>();
                        c["name"] = StripNote(h);
                        c["sheets"] = new List<object>();
                        chars[key] = c;
                        order.Add(key);
                    }
                    ((List<object>)chars[key]["sheets"]).Add(world.Name);
                }
            }
            List<object> list = order.Select(k => (object)chars[k]).ToList();
            List<object> checkedList = new List<object>();
            foreach (object o in J.Arr(J.Get(req, "chars")))
            {
                Dictionary<string, object> c = J.Obj(o) ?? new Dictionary<string, object>();
                HashSet<string> keys = CharacterKeys(c);
                List<object> sheets = new List<object>();
                bool found = false;
                foreach (string k in order)
                {
                    if (keys.Contains(Norm(chars[k]["name"])))
                    {
                        found = true;
                        sheets.AddRange((List<object>)chars[k]["sheets"]);
                    }
                }
                Dictionary<string, object> x = new Dictionary<string, object>();
                CopyField(x, J.Obj(o), "char");
                CopyField(x, J.Obj(o), "realm");
                x["found"] = found;
                x["sheets"] = sheets;
                checkedList.Add(x);
            }
            Dictionary<string, object> reply = new Dictionary<string, object>();
            reply["ok"] = true;
            reply["version"] = Program.Version;
            reply["spreadsheet"] = book.Title;
            reply["url"] = book.Url;
            reply["characters"] = list;
            reply["checked"] = checkedList;
            return reply;
        }

        // ------------------------------------------------------------------
        // «Осталось на этой неделе»
        // ------------------------------------------------------------------

        public static Dictionary<string, object> Status(IBook book)
        {
            HashSet<string> collected = CollectedMounts(book);
            List<string> order = new List<string>();
            Dictionary<string, Dictionary<string, object>> chars = new Dictionary<string, Dictionary<string, object>>();
            Func<string, Dictionary<string, object>> charFor = delegate(string owner)
            {
                string key = Norm(owner);
                Dictionary<string, object> ch;
                if (!chars.TryGetValue(key, out ch))
                {
                    ch = new Dictionary<string, object>();
                    ch["name"] = owner;
                    ch["keys"] = OwnerKeys(owner).Cast<object>().ToList();
                    ch["items"] = new List<object>();
                    chars[key] = ch;
                    order.Add(key);
                }
                return ch;
            };

            foreach (SheetEntry entry in book.Entries)
            {
                for (int p = 0; p < Prefixes.GetLength(0); p++)
                {
                    string kind = Prefixes[p, 0];
                    string owner = SheetOwner(entry.Name, Prefixes[p, 1]);
                    if (owner == null)
                    {
                        continue;
                    }
                    Dictionary<string, object> ch = charFor(owner);
                    List<object[]> data = entry.Data;
                    if (data.Count == 0)
                    {
                        continue;
                    }
                    object[] header = data[0];
                    int cStatus = FindCol(header, "пытался", -1);
                    if (cStatus < 0)
                    {
                        continue;
                    }
                    int cBoss = FindCol(header, "босс", 4);
                    int cDiff = FindCol(header, "сложност|режим", 5);
                    int cMount = FindCol(header, "маунт", 3);
                    int cPlace = FindCol(header, "^рейд|подземел", 2);
                    int cFarm = FindCol(header, "^фармлю", -1);
                    for (int r = 1; r < data.Count; r++)
                    {
                        object[] row = data[r];
                        string boss = CellText(At(row, cBoss));
                        string mount = CellText(At(row, cMount));
                        bool yes = IsYes(At(row, cStatus));
                        if ((boss.Length == 0 && mount.Length == 0) || (!yes && !IsNo(At(row, cStatus))) || collected.Contains(Norm(mount)))
                        {
                            continue;
                        }
                        if (cFarm >= 0 && IsUnticked(At(row, cFarm)))
                        {
                            continue;   // «Фармлю» снята: этот персонаж эту строку не фармит
                        }
                        ((List<object>)ch["items"]).Add(StatusItem(kind, entry.Name, r, At(row, cPlace), boss, At(row, cDiff), mount, yes));
                    }
                }
            }

            SheetEntry world = ByName(book, WorldSheet);
            if (world != null)
            {
                List<object[]> data = world.Data;
                object[] header = data.Count > 0 ? data[0] : new object[0];
                int cBoss = FindCol(header, "босс", 4);
                int cMount = FindCol(header, "маунт", 3);
                int cPlace = FindCol(header, "локац", 2);
                int cCollected = FindCol(header, "собрано", -1);
                HashSet<string> known = new HashSet<string>(order);
                for (int c = 0; c < header.Length; c++)
                {
                    if (c == cCollected || !IsCharacterColumn(data, c, known))
                    {
                        continue;
                    }
                    string owner = StripNote(header[c]);
                    if (owner.Length == 0)
                    {
                        continue;
                    }
                    Dictionary<string, object> ch = charFor(owner);
                    for (int r = 1; r < data.Count; r++)
                    {
                        object[] row = data[r];
                        string boss = CellText(At(row, cBoss));
                        string mount = CellText(At(row, cMount));
                        bool yes = IsYes(At(row, c));
                        if ((boss.Length == 0 && mount.Length == 0) || (!yes && !IsNo(At(row, c))))
                        {
                            continue;
                        }
                        if ((cCollected >= 0 && IsYes(At(row, cCollected))) || collected.Contains(Norm(mount)))
                        {
                            continue;
                        }
                        ((List<object>)ch["items"]).Add(StatusItem("world", world.Name, r, At(row, cPlace), boss, "", mount, yes));
                    }
                }
            }

            Dictionary<string, object> reply = new Dictionary<string, object>();
            reply["ok"] = true;
            reply["version"] = Program.Version;
            reply["spreadsheet"] = book.Title;
            reply["generated"] = (double)UnixNow();
            string resetAt = null;
            try
            {
                resetAt = book.GetProperty(ResetProperty);
            }
            catch (Exception)
            {
            }
            double reset = Number(resetAt);
            if (reset > 0)
            {
                reply["resetAt"] = reset;
            }
            reply["known"] = KnownDifficultyIds();
            reply["chars"] = order.Select(k => (object)chars[k]).ToList();
            List<object> mounts = StatusMounts(book, collected);
            if (mounts.Count > 0)
            {
                reply["mounts"] = mounts;   // счётчик попыток (таблица программы 2.8+)
            }
            return reply;
        }

        static Dictionary<string, object> StatusItem(string kind, string sheet, int r, object place, string boss, object diff, string mount, bool yes)
        {
            List<string> alts = boss.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            string anyAlt = alts.FirstOrDefault(alt => IsAnyBoss(alt));
            List<string> names = new List<string>();
            foreach (string alt in alts)
            {
                if (IsAnyBoss(alt))
                {
                    continue;
                }
                names.Add(Norm(alt));
                foreach (KeyValuePair<string, string> kv in BossAliases)
                {
                    if (NameMatches(kv.Value, alt, false))
                    {
                        names.Add(Norm(kv.Key));
                    }
                }
                // тот же бой на других языках (аддон узнаёт убийство по имени из игры)
                foreach (string n in Encounters.RelatedNames(alt))
                {
                    names.Add(Norm(n));
                }
            }
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["kind"] = kind;
            item["boss"] = boss;
            item["diff"] = CellText(diff);
            item["mount"] = mount;
            item["place"] = CellText(place);
            item["sheet"] = sheet;
            item["row"] = (double)(r + 1);
            item["names"] = Unique(names.Where(n => n.Length > 0)).Cast<object>().ToList();
            item["d"] = DifficultyIds(diff);
            if (yes)
            {
                item["y"] = true;
            }
            if (anyAlt != null)
            {
                item["any"] = true;
                item["tw"] = ParseDifficulty(diff).Kinds.Contains("timewalking") || TimewalkingWord.IsMatch(Norm(anyAlt));
                item["aw"] = new List<object>
                {
                    PlaceWords(anyAlt).Cast<object>().ToList(),
                    PlaceWords(place).Cast<object>().ToList(),
                };
            }
            return item;
        }

        /// <summary>DifficultyID, при которых строка засчитывается; пустой список — любая сложность.</summary>
        static List<object> DifficultyIds(object diff)
        {
            List<object> ok = new List<object>();
            foreach (int id in Difficulty.Keys)
            {
                if (DifficultyMatches((double)id, diff, false))
                {
                    ok.Add((double)id);
                }
            }
            return ok.Count == Difficulty.Count ? new List<object>() : ok;
        }

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

        static object At(object[] row, int c)
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
