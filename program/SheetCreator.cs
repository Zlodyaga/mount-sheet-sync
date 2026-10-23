// «Создать таблицу» и «Добавить персонажей»: запросы SheetBuilder уходят в Google Sheets API.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MountSheetSync
{
    sealed class CreatedSheet
    {
        public string Id = "";
        public string Url = "";
        public string Title = "";
    }

    static class SheetCreator
    {
        public static string DriveBase = "https://www.googleapis.com/drive/v3/files";

        /// <summary>Новая таблица по плану. progress — короткие сообщения для окна.</summary>
        public static CreatedSheet Create(BuildPlan plan, Action<string> progress)
        {
            SheetBuilder.Validate(plan);
            if (plan.Catalog == null)
            {
                plan.Catalog = Catalog.Embedded();
            }
            progress(Tr._("Создаю таблицу в Google…"));
            Dictionary<string, object> created = GoogleSheets.Create(SheetBuilder.CreateBody(plan.Title));
            CreatedSheet result = new CreatedSheet();
            result.Id = J.Str(created, "spreadsheetId");
            result.Url = J.Str(created, "spreadsheetUrl");
            result.Title = J.Str(J.Obj(J.Get(created, "properties")), "title");
            if (result.Id.Length == 0)
            {
                throw new SyncException(Tr._("Google не вернул номер новой таблицы"));
            }
            if (result.Url.Length == 0)
            {
                result.Url = "https://docs.google.com/spreadsheets/d/" + result.Id + "/edit";
            }
            if (result.Title.Length == 0)
            {
                result.Title = plan.Title;
            }
            long firstSheet = 0;
            List<object> sheets = J.Arr(J.Get(created, "sheets"));
            if (sheets.Count > 0)
            {
                firstSheet = J.Long(J.Obj(J.Get(J.Obj(sheets[0]), "properties")), "sheetId");
            }
            Log.Write(Tr.L("Создана таблица «{0}»: {1}", result.Title, result.Url));
            try
            {
                progress(Tr._("Заполняю листы…"));
                GoogleSheets.BatchUpdate(result.Id, SheetBuilder.Requests(plan, firstSheet));
                progress(Tr._("Последние штрихи…"));
                GoogleSheets.BatchUpdate(result.Id, SheetBuilder.FinishRequests(plan.Catalog, plan.CollectionStamp, plan.Lang));
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Заполнить новую таблицу не вышло: {0}", ex.Message));
                Trash(result.Id);
                throw;
            }
            return result;
        }

        /// <summary>Недоделанную таблицу — в корзину Google Диска (оттуда её можно вернуть).</summary>
        static void Trash(string id)
        {
            try
            {
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["trashed"] = true;
                GoogleHttp.Send("PATCH", DriveBase + "/" + Uri.EscapeDataString(id), body);
                Log.Write(Tr.L("Недоделанная таблица отправлена в корзину Google Диска."));
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось убрать недоделанную таблицу в корзину: {0}", ex.Message));
            }
        }

        /// <summary>Какие из персонажей уже есть в таблице (по листам «Рейды Имя» / «Подземелья Имя»).</summary>
        public static HashSet<string> ExistingCharacters(GoogleBook book, bool raid)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterInTable c in TableCharacters(book))
            {
                if (raid ? c.RaidSheet != null : c.DungeonSheet != null)
                {
                    names.Add(c.Name);
                }
            }
            return names;
        }

        /// <summary>
        /// Персонажи таблицы: листы «Рейды Имя», «Подземелья Имя» и колонки на «Мировых боссах».
        /// Имя — как в названии листа («Каэлия (Лок)»).
        /// </summary>
        public static List<CharacterInTable> TableCharacters(IBook book)
        {
            List<CharacterInTable> list = new List<CharacterInTable>();
            Func<string, CharacterInTable> get = delegate(string name)
            {
                CharacterInTable c = list.FirstOrDefault(x => SheetLogic.Norm(x.Name) == SheetLogic.Norm(name));
                if (c == null)
                {
                    c = new CharacterInTable { Name = name };
                    list.Add(c);
                }
                return c;
            };
            foreach (SheetEntry e in book.Entries)
            {
                foreach (bool raid in new[] { true, false })
                {
                    string prefix = (raid ? SheetBuilder.RaidsSheet : SheetBuilder.DungeonsSheet) + " ";
                    if (!e.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || e.Name.Length <= prefix.Length)
                    {
                        continue;
                    }
                    CharacterInTable c = get(e.Name.Substring(prefix.Length).Trim());
                    if (raid)
                    {
                        c.RaidSheet = e;
                    }
                    else
                    {
                        c.DungeonSheet = e;
                    }
                }
            }
            SheetEntry world = book.Entries.FirstOrDefault(e => e.Name == SheetBuilder.WorldSheet);
            if (world != null && world.Data.Count > 0)
            {
                object[] wh = world.Data[0];
                for (int i = SheetBuilder.WorldFixedColumns; i < wh.Length; i++)
                {
                    string name = SheetLogic.CellText(wh[i]);
                    if (name.Length > 0)
                    {
                        get(name).WorldColumn = i;
                    }
                }
            }
            return list;
        }

        /// <summary>Добавить персонажей (листы, которых ещё нет); ничего не удаляет. Возвращает число новых листов.</summary>
        public static int AddCharacters(string spreadsheetId, IList<BuildCharacter> chars, Action<string> progress)
        {
            progress(Tr._("Читаю таблицу…"));
            GoogleBook book = GoogleBook.Load(spreadsheetId);
            GoogleSheetService.Upgrade(book);
            List<BuildCharacter> desired = new List<BuildCharacter>();
            foreach (CharacterInTable c in TableCharacters(book))
            {
                desired.Add(new BuildCharacter(c.Name, c.RaidSheet != null, c.DungeonSheet != null, c.WorldColumn >= 0));
            }
            foreach (BuildCharacter c in chars)
            {
                BuildCharacter have = desired.FirstOrDefault(d => SheetLogic.Norm(d.Name) == SheetLogic.Norm(c.Name));
                if (have == null)
                {
                    desired.Add(new BuildCharacter(c.Name.Trim(), c.Raids, c.Dungeons, c.World));
                    continue;
                }
                have.Raids |= c.Raids;
                have.Dungeons |= c.Dungeons;
                have.World |= c.World;
            }
            CharacterChanges ch = Apply(book, desired, progress);
            return ch.AddedSheets.Count;
        }

        /// <summary>
        /// Привести персонажей таблицы к списку desired: каких листов и колонок на «Мировых боссах» не хватает —
        /// добавить, лишние — удалить (персонажа нет в списке или все галочки сняты — он уходит из таблицы).
        /// Формулы «Осталось попыток выбить» пересчитываются.
        /// </summary>
        public static CharacterChanges UpdateCharacters(string spreadsheetId, IList<BuildCharacter> desired, Action<string> progress)
        {
            progress(Tr._("Читаю таблицу…"));
            GoogleBook book = GoogleBook.Load(spreadsheetId);
            GoogleSheetService.Upgrade(book);
            return Apply(book, desired, progress);
        }

        /// <summary>Что поменяется (без записи в таблицу).</summary>
        public static CharacterChanges Diff(IBook book, IList<BuildCharacter> desired)
        {
            CharacterChanges ch = new CharacterChanges();
            List<CharacterInTable> have = TableCharacters(book);
            foreach (CharacterInTable c in have)
            {
                BuildCharacter want = desired.FirstOrDefault(d => SheetLogic.Norm(d.Name) == SheetLogic.Norm(c.Name));
                if (c.RaidSheet != null && (want == null || !want.Raids))
                {
                    ch.RemovedSheets.Add(c.RaidSheet.Name);
                }
                if (c.DungeonSheet != null && (want == null || !want.Dungeons))
                {
                    ch.RemovedSheets.Add(c.DungeonSheet.Name);
                }
                if (c.WorldColumn >= 0 && (want == null || !want.World))
                {
                    ch.RemovedWorld.Add(c.Name);
                }
                if (want == null || (!want.Raids && !want.Dungeons && !want.World))
                {
                    ch.RemovedCharacters.Add(c.Name);
                }
            }
            foreach (BuildCharacter d in desired)
            {
                string name = d.Name.Trim();
                CharacterInTable c = have.FirstOrDefault(x => SheetLogic.Norm(x.Name) == SheetLogic.Norm(name));
                if (d.Raids && (c == null || c.RaidSheet == null))
                {
                    ch.AddedSheets.Add(SheetBuilder.RaidsSheet + " " + name);
                }
                if (d.Dungeons && (c == null || c.DungeonSheet == null))
                {
                    ch.AddedSheets.Add(SheetBuilder.DungeonsSheet + " " + name);
                }
                if (d.World && (c == null || c.WorldColumn < 0))
                {
                    ch.AddedWorld.Add(name);
                }
            }
            return ch;
        }

        static CharacterChanges Apply(GoogleBook book, IList<BuildCharacter> desired, Action<string> progress)
        {
            foreach (BuildCharacter d in desired)
            {
                string name = (d.Name ?? "").Trim();
                if (name.Length == 0 || name.Length > 60 || name.IndexOfAny(new[] { '[', ']', '*', '?', '/', '\\', ':' }) >= 0)
                {
                    throw new SyncException(Tr.F("имя «{0}» не подходит для названия листа", name));
                }
            }
            CharacterChanges ch = Diff(book, desired);
            if (!ch.Any)
            {
                return ch;
            }
            List<object> req = new List<object>();
            List<object> late = new List<object>();
            List<CharacterInTable> have = TableCharacters(book);

            // 1. удаления: колонки «Мировых боссов» (справа налево), затем листы
            SheetEntry world = book.Entries.FirstOrDefault(e => e.Name == SheetBuilder.WorldSheet);
            List<int> dropCols = have.Where(c => c.WorldColumn >= 0 && ch.RemovedWorld.Contains(c.Name))
                .Select(c => c.WorldColumn).OrderByDescending(c => c).ToList();
            foreach (int col in dropCols)
            {
                req.Add(SheetBuilder.O("deleteDimension", SheetBuilder.O("range", SheetBuilder.O(
                    "sheetId", world.SheetId, "dimension", "COLUMNS", "startIndex", col, "endIndex", col + 1))));
            }
            foreach (string sheet in ch.RemovedSheets)
            {
                req.Add(SheetBuilder.O("deleteSheet", SheetBuilder.O("sheetId", book.Entries.First(e => e.Name == sheet).SheetId)));
            }
            List<string> order = book.Entries.Select(e => e.Name).Where(n => !ch.RemovedSheets.Contains(n)).ToList();

            // 2. новые листы — после последнего листа этого вида (или сразу после общего листа)
            long nextId = book.Entries.Count == 0 ? 1000 : book.Entries.Max(e => e.SheetId) + 1;
            foreach (bool raid in new[] { true, false })
            {
                string masterName = raid ? SheetBuilder.RaidsSheet : SheetBuilder.DungeonsSheet;
                string prefix = masterName + " ";
                List<string> wanted = ch.AddedSheets.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                if (wanted.Count == 0)
                {
                    continue;
                }
                SheetEntry master = book.Entries.FirstOrDefault(e => e.Name == masterName);
                if (master == null || master.Data.Count == 0)
                {
                    throw new SyncException(Tr.F("в таблице нет листа «{0}» — персонажей добавить не к чему", masterName));
                }
                string[] header = master.Data[0].Select(v => SheetLogic.CellText(v)).ToArray();
                int cMount = Array.IndexOf(header, "Маунт");
                List<object[]> rows = master.Data.Skip(1).Where(r => SheetLogic.CellText(r.Length > cMount && cMount >= 0 ? r[cMount] : "").Length > 0).ToList();
                foreach (string title in wanted)
                {
                    int index = order.FindLastIndex(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        index = order.IndexOf(masterName);
                    }
                    index++;
                    SheetBuilder.AddCharacterSheet(req, (int)nextId++, index, title.Substring(prefix.Length), raid, header, rows);
                    order.Insert(index, title);
                }
            }

            // 3. «Осталось попыток выбить» на «Рейдах»
            if (ch.AddedSheets.Concat(ch.RemovedSheets).Any(n => n.StartsWith(SheetBuilder.RaidsSheet + " ", StringComparison.Ordinal)))
            {
                Dictionary<string, string> farm = SheetUpgrade.RaidFarmColumns(book);
                List<string> raidSheets = order.Where(n => n.StartsWith(SheetBuilder.RaidsSheet + " ", StringComparison.OrdinalIgnoreCase)).ToList();
                Dictionary<string, string> cols = raidSheets.ToDictionary(n => n, n => farm.ContainsKey(n) ? farm[n]
                    : SheetBuilder.ColumnName(SheetBuilder.MasterHeaderLength(true) - 1));
                SheetEntry raids = book.Entries.FirstOrDefault(e => e.Name == SheetBuilder.RaidsSheet);
                if (raids != null && raids.Data.Count > 0)
                {
                    int cLeft = Array.IndexOf(raids.Data[0].Select(v => SheetLogic.CellText(v)).ToArray(), SheetBuilder.Left);
                    if (cLeft >= 0)
                    {
                        string formula = SheetBuilder.RaidLeftFormulaForSheets(raidSheets, cols);
                        late.Add(SheetBuilder.UpdateCells((int)raids.SheetId, 1, cLeft,
                            new List<object[]> { new object[] { formula != null ? (object)new SheetBuilder.Formula(formula) : "" } }));
                    }
                }
            }

            // 4. «Мировые боссы»: новые колонки справа и формула «Осталось попыток»
            if (world != null && world.Data.Count > 0 && (ch.AddedWorld.Count > 0 || dropCols.Count > 0))
            {
                string[] wh = world.Data[0].Select(v => SheetLogic.CellText(v)).ToArray();
                int width = wh.Length;
                while (width > SheetBuilder.WorldFixedColumns && wh[width - 1].Length == 0)
                {
                    width--;
                }
                width -= dropCols.Count(c => c < width);
                int rowCount = world.Data.Skip(1).Count(r => SheetLogic.CellText(r.Length > 3 ? r[3] : "").Length > 0);
                int grid = (world.GridColumns > 0 ? world.GridColumns : world.Data[0].Length) - dropCols.Count;
                int cLeft = Array.IndexOf(wh, SheetBuilder.Left);
                SheetBuilder.AddWorldColumns(late, (int)world.SheetId, width, grid, rowCount,
                    SheetBuilder.WorldFixedColumns, -1, ch.AddedWorld);
                if (cLeft >= 0)
                {
                    int count = width + ch.AddedWorld.Count - SheetBuilder.WorldFixedColumns;
                    late.Add(SheetBuilder.UpdateCells((int)world.SheetId, 1, cLeft, new List<object[]>
                    {
                        new object[] { new SheetBuilder.Formula(SheetBuilder.WorldLeftFormula(SheetBuilder.WorldFixedColumns, count)) },
                    }));
                }
            }

            req.AddRange(late);
            book.Lang.Outgoing(req);
            SheetBuilder.LocalizeFormulas(req, book.Locale);
            progress(Tr._("Меняю листы…"));
            GoogleSheets.BatchUpdate(book.Id, req);
            Log.Write(Tr.L("Персонажи в таблице: {0}.", ch.Summary(book.Lang)));
            return ch;
        }
    }

    /// <summary>Персонаж в таблице: его листы и колонка на «Мировых боссах» (-1 — нет).</summary>
    sealed class CharacterInTable
    {
        public string Name = "";
        public SheetEntry RaidSheet;
        public SheetEntry DungeonSheet;
        public int WorldColumn = -1;
    }

    sealed class CharacterChanges
    {
        public List<string> AddedSheets = new List<string>();
        public List<string> RemovedSheets = new List<string>();
        public List<string> AddedWorld = new List<string>();
        public List<string> RemovedWorld = new List<string>();
        /// <summary>Персонажи, которые уходят из таблицы совсем.</summary>
        public List<string> RemovedCharacters = new List<string>();

        public bool Any
        {
            get { return AddedSheets.Count + RemovedSheets.Count + AddedWorld.Count + RemovedWorld.Count > 0; }
        }

        public bool RemovesSomething
        {
            get { return RemovedSheets.Count + RemovedWorld.Count > 0; }
        }

        public string Summary()
        {
            return Summary(SheetLang.Russian);
        }

        /// <summary>Что поменялось; названия листов — как в таблице (lang — её язык).</summary>
        public string Summary(SheetLang lang)
        {
            List<string> parts = new List<string>();
            if (AddedSheets.Count > 0)
            {
                parts.Add(Tr.F("новые листы: {0}", string.Join(", ", AddedSheets.Select(n => lang.SheetName(n)).ToArray())));
            }
            if (AddedWorld.Count > 0)
            {
                parts.Add(Tr.F("колонки на «Мировых боссах»: {0}", string.Join(", ", AddedWorld.ToArray())));
            }
            if (RemovedSheets.Count > 0)
            {
                parts.Add(Tr.F("удалены листы: {0}", string.Join(", ", RemovedSheets.Select(n => lang.SheetName(n)).ToArray())));
            }
            if (RemovedWorld.Count > 0)
            {
                parts.Add(Tr.F("убраны колонки на «Мировых боссах»: {0}", string.Join(", ", RemovedWorld.ToArray())));
            }
            return parts.Count == 0 ? Tr._("без изменений") : string.Join("; ", parts.ToArray());
        }
    }
}
