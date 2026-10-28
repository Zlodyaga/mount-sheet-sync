// Готовая таблица программы — к виду новой версии (один раз, свойство таблицы «layout»):
//   2 — «Да»/«Нет» цветом в «Собрано?» и «Пытался на этой неделе?», колонка «Фармлю» на листах персонажей,
//       «Осталось попыток выбить» без строк, которые персонаж не фармит.
//   3 — счётчик попыток: «Попыток» и «Шанс за все попытки» справа от «Шанса выпадения» на общих листах (2.8).
// Колонку «ID в игре» добавляет CollectionSync — ей нужна коллекция из игры.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MountSheetSync
{
    static class SheetUpgrade
    {
        public const int Current = 4;
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
            book.Apply(req, Property, Current.ToString(CultureInfo.InvariantCulture));
            foreach (Action a in memory)
            {
                a();
            }
            if (done.Count > 0)
            {
                Log.Write(Tr.L("Таблица обновлена: {0}.", string.Join("; ", done.ToArray())));
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
