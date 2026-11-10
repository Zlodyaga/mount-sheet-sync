// Готовая таблица программы — к виду новой версии (один раз, свойство таблицы «layout»):
//   2 — «Да»/«Нет» цветом в «Собрано?» и «Пытался на этой неделе?», колонка «Фармлю» на листах персонажей,
//       «Осталось попыток выбить» без строк, которые персонаж не фармит.
//   3 — счётчик попыток: «Попыток» и «Шанс за все попытки» справа от «Шанса выпадения» на общих листах (2.8).
//   5 — рары из каталога (Исхак) — с «Мировых боссов» на отдельный лист «Редкие монстры» (2.9.2).
//   6 — рары, которых нет на листе «Редкие монстры», дописываются из каталога; нет листа — он создаётся (2.10.1).
// Колонку «ID в игре» добавляет CollectionSync — ей нужна коллекция из игры.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MountSheetSync
{
    static class SheetUpgrade
    {
        public const int Current = 6;
        public const string Property = "layout";

        static readonly string[] Masters = { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet, SheetBuilder.WorldSheet };

        /// <summary>Вид таблицы по свойству (0 — таблица версии 2.0/2.1).</summary>
        public static int LayoutOf(IBook book)
        {
            int n;
            string v = null;
            try
            {
                v = book.GetProperty(Property);
            }
            catch (Exception)
            {
            }
            return int.TryParse(v ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        /// <summary>Лист персонажа «Рейды &lt;Имя&gt;» / «Подземелья &lt;Имя&gt;» с колонкой «Пытался…».</summary>
        public static bool IsCharacterSheet(SheetEntry e)
        {
            if (e.Data.Count == 0)
            {
                return false;
            }
            if (SheetLogic.SheetOwner(e.Name, SheetLogic.RaidPrefix) == null && SheetLogic.SheetOwner(e.Name, SheetLogic.DungeonPrefix) == null)
            {
                return false;
            }
            object[] header = e.Data[0];
            return SheetLogic.FindCol(header, "пытался", -1) >= 0 && SheetLogic.FindCol(header, "собрано", -1) < 0;
        }

        /// <summary>Буквы колонок «Фармлю» листов «Рейды &lt;Имя&gt;» (для формулы «Осталось попыток»).</summary>
        public static Dictionary<string, string> RaidFarmColumns(IBook book)
        {
            Dictionary<string, string> cols = new Dictionary<string, string>();
            foreach (SheetEntry e in book.Entries)
            {
                if (SheetLogic.SheetOwner(e.Name, SheetLogic.RaidPrefix) == null || !IsCharacterSheet(e))
                {
                    continue;
                }
                int c = SheetLogic.FindCol(e.Data[0], "^фармлю", -1);
                cols[e.Name] = c >= 0 ? SheetBuilder.ColumnName(c) : "";
            }
            return cols;
        }

        /// <summary>Обновить таблицу до текущего вида. true — что-то поменялось.</summary>
        public static bool Ensure(IStructuredBook book)
        {
            int layout = LayoutOf(book);
            if (layout >= Current)
            {
                return false;
            }
            List<object> req = new List<object>();
            List<Action> memory = new List<Action>();
            List<string> done = new List<string>();
            if (layout < 2)
            {
                done.Add(Layout2(book, req, memory));
            }
            int attempts = AttemptColumns(book, req, memory);
            if (attempts > 0)
            {
                done.Add(Tr.L("счётчик попыток на общих листах ({0})", attempts));
            }
            else if (layout == 3)
            {
                int fixedSheets = AttemptFormatsAgain(book, req);
                if (fixedSheets > 0)
                {
                    done.Add(Tr.L("формат «Попыток» и «Шанса за все попытки» в строках, скрытых фильтром ({0})", fixedSheets));
                }
            }
            string moved = null;
            if (layout < 5)
            {
                moved = MoveRares(book, req, memory);
                if (moved != null)
                {
                    done.Add(moved);
                }
            }
            if (layout < 6 && moved == null)   // перенос уже взял весь нынешний каталог рар
            {
                string added = AddRares(book, req, memory);
                if (added != null)
                {
                    done.Add(added);
                }
            }
            if (layout < 6)
            {
                string raids = AddRaidRows(book, req, memory);
                if (raids != null)
                {
                    done.Add(raids);
                }
            }
            book.Apply(req, Property, Current.ToString(CultureInfo.InvariantCulture));
            foreach (Action a in memory)
            {
                a();
            }
            book.Flush();
            if (done.Count > 0)
            {
                Log.Write(Tr.L("Таблица обновлена: {0}.", string.Join("; ", done.ToArray())));
            }
            return true;
        }

        /// <summary>
        /// Вид 5: строки рар каталога — на свой лист, с «Собрано?» и «Попыток». Галочки персонажей этих строк не переносим:
        /// откат у рара свой, его считает аддон. Строк рар на «Мировых боссах» нет (своя таблица) — листа не будет.
        /// </summary>
        static string MoveRares(IStructuredBook book, List<object> req, List<Action> memory)
        {
            Catalog catalog = Catalog.Embedded();
            SheetEntry world = SheetLogic.ByName(book, SheetBuilder.WorldSheet);
            if (catalog.Rare == null || world == null || world.Data.Count < 2 || SheetLogic.ByName(book, SheetBuilder.RareSheet) != null)
            {
                return null;
            }
            object[] wh = world.Data[0];
            int cMount = SheetLogic.FindCol(wh, "маунт", -1);
            int cCollected = SheetLogic.FindCol(wh, "собрано", -1);
            int cAttempts = SheetLogic.FindCol(wh, SheetLogic.AttemptsPattern, -1);
            int cWorldId = SheetLogic.FindCol(wh, "^id в игре$", -1);
            int cBoss = SheetLogic.FindCol(wh, "босс", -1);
            GoogleBook google = book as GoogleBook;
            BuildPlan plan = new BuildPlan { Catalog = catalog, Lang = google != null ? google.Lang : SheetLang.Russian };
            List<object[]> rows = SheetBuilder.RareRows(plan);
            string[] header = SheetBuilder.RareMaster;
            int rMount = Array.IndexOf(header, "Маунт");
            int rBoss = Array.IndexOf(header, "Босс / достижение / сундук");
            int rCollected = Array.IndexOf(header, SheetBuilder.Collected);
            int rAttempts = Array.IndexOf(header, SheetBuilder.AttemptsColumn);
            int rChance = Array.IndexOf(header, SheetBuilder.ChanceColumn);
            int rId = Array.IndexOf(header, SheetBuilder.IdColumn);
            List<int> remove = new List<int>();
            foreach (object[] row in rows)
            {
                long id = MountIndex.IdOf(row[rId]);
                for (int r = 1; r < world.Data.Count; r++)
                {
                    object[] w = world.Data[r];
                    bool sameId = id > 0 && cWorldId >= 0 && MountIndex.IdOf(w[cWorldId]) == id;
                    if (remove.Contains(r) || !(sameId || (cMount >= 0 && SheetLogic.Norm(w[cMount]) == SheetLogic.Norm(row[rMount]))))
                    {
                        continue;
                    }
                    remove.Add(r);
                    // названия — как в этой таблице: нерусская могла взять их из журнала игры, а не из каталога
                    row[rMount] = cMount >= 0 ? w[cMount] : row[rMount];
                    row[rBoss] = cBoss >= 0 ? w[cBoss] : row[rBoss];
                    if (cCollected >= 0)
                    {
                        row[rCollected] = w[cCollected];
                    }
                    double n;
                    if (cAttempts >= 0 && SheetLogic.AttemptCount(w[cAttempts], out n))
                    {
                        row[rAttempts] = n;
                    }
                }
            }
            if (remove.Count == 0)
            {
                return null;
            }
            long sheetId = book.Entries.Max(e => e.SheetId) + 1;
            int index = book.Entries.IndexOf(world) + 1;
            SheetBuilder.AddRare(req, (int)sheetId, index, rows);
            req.AddRange(BookEdit.DeleteRequests(world, remove));
            memory.Add(() =>
            {
                BookEdit.DeleteRows(world, remove);
                SheetEntry entry = new SheetEntry { Name = SheetBuilder.RareSheet, SheetId = sheetId, GridColumns = header.Length };
                entry.Data.Add(header.Cast<object>().ToArray());
                foreach (object[] row in rows)
                {
                    if (row[rChance] is SheetBuilder.Formula)
                    {
                        entry.Formulas.Add(SheetLogic.Key(entry.Data.Count, rChance));
                        row[rChance] = "";
                    }
                    entry.Data.Add(row);
                }
                book.Entries.Insert(index, entry);
            });
            return Tr.L("редкие монстры перенесены на отдельный лист ({0})", remove.Count);
        }

        /// <summary>
        /// Вид 6: рары каталога, которых нет на листе «Редкие монстры» (по mountID, иначе по названию маунта), — в конец
        /// листа, с «Нет» в «Собрано?» (собранные отметит CollectionSync) и счётчиком попыток с нуля. Листа нет, а
        /// «Мировые боссы» есть (таблица программы) — лист создаётся из всех рар каталога.
        /// </summary>
        static string AddRares(IStructuredBook book, List<object> req, List<Action> memory)
        {
            Catalog catalog = Catalog.Embedded();
            SheetEntry world = SheetLogic.ByName(book, SheetBuilder.WorldSheet);
            SheetEntry rare = SheetLogic.ByName(book, SheetBuilder.RareSheet);
            if (catalog.Rare == null || (rare == null && world == null))
            {
                return null;
            }
            string[] header = SheetBuilder.RareMaster;
            BuildPlan plan = new BuildPlan { Catalog = catalog, Lang = book.Lang ?? SheetLang.Russian };
            List<object[]> rows = SheetBuilder.RareRows(plan);
            int rChance = Array.IndexOf(header, SheetBuilder.ChanceColumn);
            if (rare == null)
            {
                long sheetId = book.Entries.Max(e => e.SheetId) + 1;
                int index = book.Entries.IndexOf(world) + 1;
                SheetBuilder.AddRare(req, (int)sheetId, index, rows);
                memory.Add(() =>
                {
                    SheetEntry entry = new SheetEntry { Name = SheetBuilder.RareSheet, SheetId = sheetId, GridColumns = header.Length };
                    entry.Data.Add(header.Cast<object>().ToArray());
                    AddRows(entry, rows, rChance);
                    book.Entries.Insert(index, entry);
                });
                return Tr.L("лист «Редкие монстры» ({0})", rows.Count);
            }
            if (rare.Data.Count == 0 || !SameHeader(rare.Data[0], header))
            {
                return null;   // лист с другими колонками (правили руками): строки встали бы не в свои колонки
            }
            int rId = Array.IndexOf(header, SheetBuilder.IdColumn);
            int rMount = Array.IndexOf(header, "Маунт");
            int rDrop = Array.IndexOf(header, "Шанс выпадения");
            int rAttempts = Array.IndexOf(header, SheetBuilder.AttemptsColumn);
            HashSet<long> ids = new HashSet<long>();
            HashSet<string> names = new HashSet<string>();
            for (int r = 1; r < rare.Data.Count; r++)
            {
                object[] row = rare.Data[r];
                long id = rId < row.Length ? MountIndex.IdOf(row[rId]) : 0;
                if (id > 0)
                {
                    ids.Add(id);
                }
                if (rMount < row.Length)
                {
                    names.Add(SheetLogic.Norm(row[rMount]));
                }
            }
            List<object[]> add = new List<object[]>();
            foreach (object[] row in rows)
            {
                long id = MountIndex.IdOf(row[rId]);
                if ((id > 0 && ids.Contains(id)) || names.Contains(SheetLogic.Norm(row[rMount])))
                {
                    continue;
                }
                object[] cells = (object[])row.Clone();
                object[] attempt = SheetLogic.AttemptCells(cells[rDrop], rAttempts, rare.Data.Count + add.Count);
                cells[rAttempts] = attempt[0];
                cells[rChance] = attempt[1];
                add.Add(cells);
            }
            if (add.Count == 0)
            {
                return null;
            }
            int start = rare.Data.Count;
            SheetBuilder.AppendRare(req, (int)rare.SheetId, start, add);
            memory.Add(() => AddRows(rare, add, rChance));
            return Tr.L("новые редкие монстры из каталога ({0})", add.Count);
        }

        /// <summary>
        /// Вид 6: на листы «Рейды &lt;Имя&gt;» — рейдовая добыча «Текущий контент» и «Фармится / нужен контент-группинг»
        /// (с 2.10.1 она тоже в «Осталось»). Только строки этих статусов, которых на листе ещё нет: убранное игроком не
        /// возвращаем. Собранные маунты отсеивает SheetBuilder.CharacterRows, как и у новой таблицы.
        /// </summary>
        static string AddRaidRows(IStructuredBook book, List<object> req, List<Action> memory)
        {
            SheetEntry master = SheetLogic.ByName(book, SheetBuilder.RaidsSheet);
            if (master == null || master.Data.Count < 2)
            {
                return null;
            }
            string[] mh = master.Data[0].Select(h => SheetLogic.CellText(h)).ToArray();
            int cStatus = Array.IndexOf(mh, "Статус");
            if (cStatus < 0)
            {
                return null;
            }
            List<object[]> fresh = master.Data.Skip(1)
                .Where(m => SheetBuilder.RaidStatusesAdded.Contains(SheetLogic.CellText(SheetLogic.At(m, cStatus)))).ToList();
            List<object[]> rows = SheetBuilder.CharacterRows(mh, fresh, true);
            if (rows.Count == 0)
            {
                return null;
            }
            string[] rc = SheetBuilder.RaidCharHeader;
            int iMount = Array.IndexOf(rc, "Маунт");
            int iBoss = Array.IndexOf(rc, "Босс / достижение / сундук");
            int iDiff = Array.IndexOf(rc, "Сложность / режим");
            int added = 0;
            foreach (SheetEntry e in book.Entries.ToList())
            {
                if (SheetLogic.SheetOwner(e.Name, SheetLogic.RaidPrefix) == null || !IsCharacterSheet(e))
                {
                    continue;
                }
                object[] h = e.Data[0];
                int cMount = SheetLogic.FindCol(h, "маунт", -1);
                int cBoss = SheetLogic.FindCol(h, "босс", -1);
                int cDiff = SheetLogic.FindCol(h, "сложност|режим", -1);
                HashSet<string> have = new HashSet<string>();
                for (int r = 1; r < e.Data.Count; r++)
                {
                    object[] row = e.Data[r];
                    have.Add(SheetLogic.Norm(SheetLogic.At(row, cMount)) + "|" + SheetLogic.Norm(SheetLogic.At(row, cBoss)) + "|"
                        + SheetLogic.Norm(SheetLogic.At(row, cDiff)));
                }
                List<object[]> add = new List<object[]>();
                foreach (object[] row in rows)
                {
                    string key = SheetLogic.Norm(row[iMount]) + "|" + SheetLogic.Norm(row[iBoss]) + "|" + SheetLogic.Norm(row[iDiff]);
                    if (!have.Add(key))
                    {
                        continue;
                    }
                    object[] cells = new object[h.Length];
                    for (int c = 0; c < h.Length; c++)
                    {
                        int i = Array.FindIndex(rc, x => SheetLogic.Norm(x) == SheetLogic.Norm(h[c]));
                        cells[c] = i >= 0 ? row[i] : "";
                    }
                    add.Add(cells);
                }
                if (add.Count == 0)
                {
                    continue;
                }
                SheetBuilder.AppendCharacterRows(req, (int)e.SheetId, e.Data.Count, add, h);
                SheetEntry target = e;
                memory.Add(() => target.Data.AddRange(add));
                added += add.Count;
            }
            return added > 0 ? Tr.L("рейдовые маунты текущего контента на листах персонажей ({0})", added) : null;
        }

        /// <summary>Строки в копию листа в памяти: формула «Шанса за все попытки» — пустая ячейка с отметкой в Formulas.</summary>
        static void AddRows(SheetEntry entry, List<object[]> rows, int rChance)
        {
            foreach (object[] source in rows)
            {
                object[] row = (object[])source.Clone();
                if (rChance >= 0 && row[rChance] is SheetBuilder.Formula)
                {
                    entry.Formulas.Add(SheetLogic.Key(entry.Data.Count, rChance));
                    row[rChance] = "";
                }
                entry.Data.Add(row);
            }
        }

        static bool SameHeader(object[] have, string[] want)
        {
            if (have.Length < want.Length)
            {
                return false;
            }
            for (int c = 0; c < want.Length; c++)
            {
                if (SheetLogic.Norm(have[c]) != SheetLogic.Norm(want[c]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Вид 2: «Да»/«Нет» цветом, «Фармлю» на листах персонажей, новая «Осталось попыток выбить».</summary>
        static string Layout2(IStructuredBook book, List<object> req, List<Action> memory)
        {
            int farmAdded = 0;
            foreach (SheetEntry e in book.Entries)
            {
                if (e.Data.Count == 0)
                {
                    continue;
                }
                object[] header = e.Data[0];
                int sid = (int)e.SheetId;
                if (Masters.Any(m => SheetLogic.Norm(m) == SheetLogic.Norm(e.Name)))
                {
                    req.AddRange(SheetBuilder.YesNoColors(sid, SheetLogic.FindCol(header, "собрано", -1)));
                    continue;
                }
                if (!IsCharacterSheet(e))
                {
                    continue;
                }
                req.AddRange(SheetBuilder.YesNoColors(sid, SheetLogic.FindCol(header, "пытался", -1)));
                if (SheetLogic.FindCol(header, "^фармлю", -1) >= 0)
                {
                    continue;
                }
                int col = header.Length;
                int grid = e.GridColumns > 0 ? e.GridColumns : col;
                int cMount = SheetLogic.FindCol(header, "маунт", 3);
                int cBoss = SheetLogic.FindCol(header, "босс", 4);
                List<object> values = new List<object> { SheetBuilder.FarmColumn };
                for (int r = 1; r < e.Data.Count; r++)
                {
                    bool filled = SheetLogic.CellText(e.Data[r].Length > cMount ? e.Data[r][cMount] : "").Length > 0
                        || SheetLogic.CellText(e.Data[r].Length > cBoss ? e.Data[r][cBoss] : "").Length > 0;
                    values.Add(filled ? (object)true : "");
                }
                req.AddRange(SheetBuilder.FarmColumnRequests(sid, col, grid, e.Data.Count - 1, values));
                SheetEntry entry = e;
                memory.Add(delegate
                {
                    BookEdit.AppendColumn(entry, values);
                    if (col >= grid)
                    {
                        entry.GridColumns = col + 1;
                    }
                });
                farmAdded++;
            }
            // «Осталось попыток выбить» — только по строкам, которые персонажи фармят
            Dictionary<string, string> farmCols = RaidFarmColumns(book);
            foreach (SheetEntry e in book.Entries)
            {
                if (SheetLogic.SheetOwner(e.Name, SheetLogic.RaidPrefix) != null && IsCharacterSheet(e)
                    && SheetLogic.FindCol(e.Data[0], "^фармлю", -1) < 0)
                {
                    farmCols[e.Name] = SheetBuilder.ColumnName(e.Data[0].Length);   // колонка появится в этом же пакете
                }
            }
            Dictionary<string, object> leftFormula = RaidLeftRequest(book, farmCols);
            if (leftFormula != null)
            {
                req.Add(leftFormula);
            }
            return farmAdded > 0
                ? Tr.L("«Да»/«Нет» цветом, колонка «Фармлю» на листах персонажей ({0})", farmAdded)
                : Tr.L("«Да»/«Нет» цветом");
        }

        /// <summary>
        /// Вид 3: на «Рейдах», «Подземельях» и «Мировых боссах» две колонки сразу за «Шансом выпадения» (на «Мировых
        /// боссах» — перед колонками персонажей, как в новой таблице). Возвращает, на скольких листах они появились.
        /// </summary>
        static int AttemptColumns(IStructuredBook book, List<object> req, List<Action> memory)
        {
            int count = 0;
            foreach (string name in Masters)
            {
                SheetEntry e = SheetLogic.ByName(book, name);
                if (e == null || e.Data.Count == 0)
                {
                    continue;
                }
                object[] header = e.Data[0];
                int cDrop = SheetLogic.FindCol(header, "^шанс выпадения", -1);
                if (cDrop < 0 || SheetLogic.FindCol(header, SheetLogic.AttemptsPattern, -1) >= 0)
                {
                    continue;
                }
                int at = cDrop + 1;
                int sid = (int)e.SheetId;
                int cMount = SheetLogic.FindCol(header, "маунт", 3);
                List<object[]> values = new List<object[]> { new object[] { SheetBuilder.AttemptsColumn, SheetBuilder.ChanceColumn } };
                for (int r = 1; r < e.Data.Count; r++)
                {
                    object[] row = e.Data[r];
                    bool filled = SheetLogic.CellText(row.Length > cMount ? row[cMount] : "").Length > 0;
                    values.Add(filled ? SheetLogic.AttemptCells(row[cDrop], at, r) : new object[] { "", "" });
                }
                req.Add(SheetBuilder.O("insertDimension", SheetBuilder.O(
                    "range", SheetBuilder.O("sheetId", sid, "dimension", "COLUMNS", "startIndex", at, "endIndex", at + 2),
                    "inheritFromBefore", true)));
                req.Add(SheetBuilder.UpdateCells(sid, 0, at, values));
                req.AddRange(SheetBuilder.AttemptFormats(sid, at, e.Data.Count, name == SheetBuilder.RaidsSheet));
                req.AddRange(SheetBuilder.ColumnWidths(sid, at, new[] { 90, 110 }));
                SheetEntry entry = e;
                memory.Add(delegate { BookEdit.InsertColumns(entry, at, values); });
                count++;
            }
            return count;
        }

        /// <summary>
        /// Вид 4: таблица, получившая счётчик в 2.8, оформила его через repeatCell — строки, скрытые фильтром (собранные
        /// маунты), остались без формата (шанс дробью 0,0394 вместо 3,9 %). Оформить ещё раз, по ячейкам.
        /// </summary>
        static int AttemptFormatsAgain(IStructuredBook book, List<object> req)
        {
            int count = 0;
            foreach (string name in Masters)
            {
                SheetEntry e = SheetLogic.ByName(book, name);
                if (e == null || e.Data.Count < 2)
                {
                    continue;
                }
                int at = SheetLogic.FindCol(e.Data[0], SheetLogic.AttemptsPattern, -1);
                if (at < 0)
                {
                    continue;
                }
                req.AddRange(SheetBuilder.AttemptFormats((int)e.SheetId, at, e.Data.Count, name == SheetBuilder.RaidsSheet));
                count++;
            }
            return count;
        }

        /// <summary>Новая формула «Осталось попыток выбить» на «Рейдах» (null — формулы там нет).</summary>
        public static Dictionary<string, object> RaidLeftRequest(IBook book, IDictionary<string, string> farmCols)
        {
            SheetEntry raids = SheetLogic.ByName(book, SheetBuilder.RaidsSheet);
            if (raids == null || raids.Data.Count < 2)
            {
                return null;
            }
            int cLeft = SheetLogic.FindCol(raids.Data[0], "^осталось попыток", -1);
            if (cLeft < 0 || (!raids.Formulas.Contains(SheetLogic.Key(1, cLeft)) && SheetLogic.CellText(raids.Data[1][cLeft]).Length > 0))
            {
                return null;   // там не наша формула, а что-то своё — не трогаем
            }
            List<string> sheets = book.Entries.Where(e => farmCols.ContainsKey(e.Name)).Select(e => e.Name).ToList();
            string f = SheetBuilder.RaidLeftFormulaForSheets(sheets, farmCols);
            if (f == null)
            {
                return SheetBuilder.UpdateCells((int)raids.SheetId, 1, cLeft, new List<object[]> { new object[] { "" } });
            }
            return SheetBuilder.UpdateCells((int)raids.SheetId, 1, cLeft, new List<object[]> { new object[] { new SheetBuilder.Formula(f) } });
        }
    }
}
