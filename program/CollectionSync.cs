// «Сверить с коллекцией» для таблицы, созданной программой (вход через Google):
//  1) в «Рейды» и «Подземелья» дописывается скрытая колонка «ID в игре» (если её ещё нет);
//  2) прежние названия маунтов из каталога меняются на названия «как в игре» (и на листах персонажей);
//  3) «Собрано?» = «Да» для маунтов, которые есть в коллекции (по mountID, без ID — по имени);
//     обратно в «Нет» программа ничего не ставит;
//  4) с листов «Рейды <Имя>» / «Подземелья <Имя>» убираются строки маунтов, которые уже собраны
//     («Собрано?» = «Да» на общем листе) — это же делается после каждой отправки записей.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MountSheetSync
{
    /// <summary>Таблица, в которой можно добавлять колонки и удалять строки.</summary>
    interface IStructuredBook : IBook
    {
        /// <summary>Колонка справа от последней: values[0] — заголовок, дальше по строке на каждую строку листа.</summary>
        void AppendColumn(SheetEntry entry, List<object> values, bool hidden);

        /// <summary>Удалить строки (номера с 0, заголовок — 0 — не удаляется) сразу на нескольких листах.</summary>
        void DeleteRows(Dictionary<SheetEntry, List<int>> rows);

        /// <summary>
        /// Запросы spreadsheets.batchUpdate одним пакетом (формулы — с запятыми, как в en_US: разделители
        /// поменяются по локали таблицы) и, в том же пакете, свойство таблицы propertyKey = propertyValue
        /// (null — без свойства). Копию в памяти вызывающий правит сам.
        /// </summary>
        void Apply(List<object> requests, string propertyKey, string propertyValue);

        /// <summary>Какая версия программы создала таблицу («2.0.0» и т. п.; "" — неизвестно).</summary>
        string CreatorApp { get; }
    }

    /// <summary>Те же правки в копии таблицы в памяти (после того как они ушли в Google).</summary>
    static class BookEdit
    {
        public static void AppendColumn(SheetEntry entry, List<object> values)
        {
            for (int r = 0; r < entry.Data.Count; r++)
            {
                object[] row = entry.Data[r];
                object[] wider = new object[row.Length + 1];
                Array.Copy(row, wider, row.Length);
                wider[row.Length] = r < values.Count && values[r] != null ? values[r] : "";
                entry.Data[r] = wider;
            }
        }

        public static void DeleteRows(SheetEntry entry, IEnumerable<int> rows)
        {
            HashSet<int> gone = new HashSet<int>(rows.Where(r => r >= 1 && r < entry.Data.Count));
            if (gone.Count == 0)
            {
                return;
            }
            int[] newIndex = new int[entry.Data.Count];
            List<object[]> kept = new List<object[]>();
            for (int r = 0; r < entry.Data.Count; r++)
            {
                newIndex[r] = gone.Contains(r) ? -1 : kept.Count;
                if (!gone.Contains(r))
                {
                    kept.Add(entry.Data[r]);
                }
            }
            HashSet<long> formulas = new HashSet<long>();
            foreach (long key in entry.Formulas)
            {
                int r = (int)(key / 100000);
                int c = (int)(key % 100000);
                if (r < newIndex.Length && newIndex[r] >= 0)
                {
                    formulas.Add(SheetLogic.Key(newIndex[r], c));
                }
                else if (r >= newIndex.Length)
                {
                    formulas.Add(SheetLogic.Key(r - gone.Count, c));
                }
            }
            entry.Data.Clear();
            entry.Data.AddRange(kept);
            entry.Formulas.Clear();
            entry.Formulas.UnionWith(formulas);
        }

        /// <summary>deleteDimension для строк листа: подряд идущие — одним запросом, снизу вверх.</summary>
        public static List<object> DeleteRequests(SheetEntry entry, IEnumerable<int> rows)
        {
            List<int> list = rows.Where(r => r >= 1).Distinct().OrderByDescending(r => r).ToList();
            List<object> req = new List<object>();
            int i = 0;
            while (i < list.Count)
            {
                int end = list[i] + 1;
                int start = list[i];
                while (i + 1 < list.Count && list[i + 1] == start - 1)
                {
                    i++;
                    start = list[i];
                }
                req.Add(SheetBuilder.O("deleteDimension", SheetBuilder.O("range", SheetBuilder.O(
                    "sheetId", entry.SheetId, "dimension", "ROWS", "startIndex", start, "endIndex", end))));
                i++;
            }
            return req;
        }
    }

    sealed class CollectionResult
    {
        /// <summary>Маунты, у которых «Собрано?» стало «Да».</summary>
        public List<string> Marked = new List<string>();
        /// <summary>Строк убрано с листов персонажей.</summary>
        public int Removed;
        /// <summary>Сколько ячеек получили название «как в игре».</summary>
        public int Renamed;
        /// <summary>Листы, где появилась колонка «ID в игре».</summary>
        public List<string> IdColumnsAdded = new List<string>();
        /// <summary>«Да» → «Нет»: таблица 2.0 спутала маунты с одним названием.</summary>
        public List<string> Unmarked = new List<string>();

        public bool Changed
        {
            get { return Marked.Count > 0 || Removed > 0 || Renamed > 0 || IdColumnsAdded.Count > 0 || Unmarked.Count > 0; }
        }

        public string Summary()
        {
            List<string> parts = new List<string>();
            if (Marked.Count > 0)
            {
                parts.Add(Tr.F("«Собрано?» = «Да»: {0} ({1})", Marked.Count, TextUtil.ShortList(Marked, 4)));
            }
            if (Unmarked.Count > 0)
            {
                parts.Add(Tr.F("исправлено на «Нет» (другой маунт с тем же названием): {0}", TextUtil.ShortList(Unmarked, 4)));
            }
            if (Removed > 0)
            {
                parts.Add(Tr.F("убрано строк с листов персонажей: {0}", Removed));
            }
            if (Renamed > 0)
            {
                parts.Add(Tr.F("названий «как в игре»: {0}", Renamed));
            }
            if (IdColumnsAdded.Count > 0)
            {
                parts.Add(Tr.F("колонка «ID в игре» на листах {0}", string.Join(", ", IdColumnsAdded.Select(n => "«" + n + "»").ToArray())));
            }
            return parts.Count == 0 ? Tr._("всё уже совпадает с коллекцией") : string.Join("; ", parts.ToArray());
        }
    }

    static class CollectionSync
    {
        /// <summary>Свойство таблицы: время снимка коллекции (collection.updated), с которым уже сверились.</summary>
        public const string CheckedProperty = "collectionChecked";

        static readonly string[] Masters = { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet, SheetBuilder.WorldSheet };

        public static CollectionResult Run(IStructuredBook book, GameSnapshot game, MountIndex index)
        {
            CollectionResult res = new CollectionResult();
            AddIdColumns(book, index, game, res);
            FixOldNameMixUps(book, index, game, res);
            Rename(book, index, res);
            Mark(book, game, index, res);
            book.Flush();
            res.Removed = RemoveCollected(book);
            return res;
        }

        static int IdCol(object[] header)
        {
            return SheetLogic.FindCol(header, "^id в игре$", -1);
        }

        /// <summary>Скрытая колонка «ID в игре» на «Рейдах» и «Подземельях» таблицы, созданной до версии 2.1.</summary>
        static void AddIdColumns(IStructuredBook book, MountIndex index, GameSnapshot game, CollectionResult res)
        {
            foreach (string name in new[] { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet })
            {
                SheetEntry entry = SheetLogic.ByName(book, name);
                if (entry == null || entry.Data.Count == 0)
                {
                    continue;
                }
                object[] header = entry.Data[0];
                int cMount = SheetLogic.FindCol(header, "маунт", -1);
                if (IdCol(header) >= 0 || cMount < 0 || SheetLogic.FindCol(header, "собрано", -1) < 0)
                {
                    continue;
                }
                int cBoss = SheetLogic.FindCol(header, "босс", -1);
                string kind = MountIndex.KindOfSheet(name);
                List<object> values = new List<object> { SheetBuilder.IdColumn };
                int known = 0;
                for (int r = 1; r < entry.Data.Count; r++)
                {
                    object[] row = entry.Data[r];
                    long id = index.Find(kind, row[cMount], cBoss >= 0 ? row[cBoss] : "");
                    values.Add(id > 0 ? (object)(double)id : "");
                    if (id > 0)
                    {
                        known++;
                    }
                }
                if (known == 0)
                {
                    continue;   // каталог без ID (или чужой лист) — колонка ничего не даст
                }
                book.AppendColumn(entry, values, true);
                res.IdColumnsAdded.Add(entry.Name);
                UndoNameMixUps(book, entry, values, cMount, index, game, res);
                Log.Write(Tr.L("Лист «{0}»: добавлена скрытая колонка «{1}» (узнано маунтов: {2} из {3}).",
                    entry.Name, SheetBuilder.IdColumn, known, entry.Data.Count - 1));
            }
        }

        /// <summary>Свойство таблицы: путаница 2.0 с одинаковыми названиями уже исправлена.</summary>
        public const string MixFixedProperty = "nameMixFixed";

        /// <summary>
        /// Таблица 2.0, которую уже обновила 2.1 (колонка «ID в игре» есть, а путаница с одинаковыми названиями
        /// осталась): исправить один раз. В таблицах, созданных 2.1 и новее, «Да» ставились по ID — их не трогаем.
        /// </summary>
        static void FixOldNameMixUps(IStructuredBook book, MountIndex index, GameSnapshot game, CollectionResult res)
        {
            if (game == null || game.CollectedIds.Count == 0 || !(book.CreatorApp ?? "").StartsWith("2.0", StringComparison.Ordinal))
            {
                return;
            }
            string done = null;
            try
            {
                done = book.GetProperty(MixFixedProperty);
            }
            catch (Exception)
            {
            }
            if (done != null)
            {
                return;
            }
            foreach (string name in new[] { SheetBuilder.RaidsSheet, SheetBuilder.DungeonsSheet })
            {
                SheetEntry entry = SheetLogic.ByName(book, name);
                if (entry == null || entry.Data.Count == 0 || res.IdColumnsAdded.Contains(entry.Name))
                {
                    continue;   // колонку только что добавили — исправлено вместе с ней
                }
                int cId = IdCol(entry.Data[0]);
                int cMount = SheetLogic.FindCol(entry.Data[0], "маунт", -1);
                if (cId < 0 || cMount < 0 || SheetLogic.FindCol(entry.Data[0], "собрано", -1) < 0)
                {
                    continue;
                }
                List<object> ids = entry.Data.Select(r => cId < r.Length ? r[cId] : "").ToList();
                UndoNameMixUps(book, entry, ids, cMount, index, game, res);
            }
            book.Flush();
            try
            {
                book.SetProperty(MixFixedProperty, "1");
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось запомнить исправление названий в таблице: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Таблица версии 2.0 ставила «Да» по совпадению названия — и путала разные маунты с одним именем
        /// (два «Аманийских боевых медведя»). Только при первом появлении колонки «ID в игре»: «Да» → «Нет»,
        /// если этого маунта (по ID) в коллекции нет, а маунт с тем же названием — есть.
        /// </summary>
        static void UndoNameMixUps(IStructuredBook book, SheetEntry entry, List<object> ids, int cMount, MountIndex index,
            GameSnapshot game, CollectionResult res)
        {
            if (game == null || game.CollectedIds.Count == 0)
            {
                return;
            }
            int cCollected = SheetLogic.FindCol(entry.Data[0], "собрано", -1);
            HashSet<string> collectedNames = new HashSet<string>(game.CollectedMounts.Select(n => SheetLogic.Norm(SheetLogic.StripNote(n))));
            for (int r = 1; r < entry.Data.Count && r < ids.Count; r++)
            {
                long id = MountIndex.IdOf(ids[r]);
                object current = entry.Data[r][cCollected];
                if (id == 0 || game.CollectedIds.Contains(id) || !SheetLogic.IsYes(current)
                    || entry.Formulas.Contains(SheetLogic.Key(r, cCollected)))
                {
                    continue;
                }
                string name = index.NameOf(id) ?? SheetLogic.CellText(entry.Data[r][cMount]);
                if (!collectedNames.Contains(SheetLogic.Norm(SheetLogic.StripNote(name))))
                {
                    continue;
                }
                book.Set(entry, r, cCollected, current is bool ? (object)false : SheetLogic.No);
                res.Unmarked.Add(SheetLogic.CellText(entry.Data[r][cMount]));
                Log.Write(Tr.L("«{0}»: «Собрано?» снова «Нет» — у вас другой маунт с тем же названием.", SheetLogic.CellText(entry.Data[r][cMount])));
            }
        }

        /// <summary>Прежние названия каталога → названия как в журнале маунтов игры.</summary>
        static void Rename(IStructuredBook book, MountIndex index, CollectionResult res)
        {
            foreach (string name in Masters)
            {
                SheetEntry entry = SheetLogic.ByName(book, name);
                if (entry == null || entry.Data.Count == 0)
                {
                    continue;
                }
                object[] header = entry.Data[0];
                int cMount = SheetLogic.FindCol(header, "маунт", -1);
                if (cMount < 0)
                {
                    continue;
                }
                int cBoss = SheetLogic.FindCol(header, "босс", -1);
                int cId = IdCol(header);
                string kind = MountIndex.KindOfSheet(name);
                // старое название → новое (null — одно старое название у разных маунтов: не трогаем)
                Dictionary<string, string> renames = new Dictionary<string, string>();
                for (int r = 1; r < entry.Data.Count; r++)
                {
                    object[] row = entry.Data[r];
                    object cell = row[cMount];
                    if (entry.Formulas.Contains(SheetLogic.Key(r, cMount)))
                    {
                        continue;
                    }
                    long id = cId >= 0 ? MountIndex.IdOf(row[cId]) : 0;
                    if (id == 0)
                    {
                        id = index.Find(kind, cell, cBoss >= 0 ? row[cBoss] : "");
                    }
                    string now = id > 0 ? index.NameOf(id) : null;
                    if (now == null || now == SheetLogic.CellText(cell) || !index.IsOldName(id, cell))
                    {
                        continue;
                    }
                    string oldKey = SheetLogic.Norm(cell);
                    string seen;
                    if (renames.TryGetValue(oldKey, out seen) && seen != now)
                    {
                        renames[oldKey] = null;
                    }
                    else
                    {
                        renames[oldKey] = now;
                    }
                    book.Set(entry, r, cMount, now);
                    res.Renamed++;
                }
                if (renames.Count == 0)
                {
                    continue;
                }
                string prefix = name == SheetBuilder.RaidsSheet ? SheetLogic.RaidPrefix
                    : name == SheetBuilder.DungeonsSheet ? SheetLogic.DungeonPrefix : null;
                if (prefix == null)
                {
                    continue;
                }
                foreach (SheetEntry e in book.Entries)
                {
                    if (SheetLogic.SheetOwner(e.Name, prefix) == null || e.Data.Count == 0)
                    {
                        continue;
                    }
                    int c = SheetLogic.FindCol(e.Data[0], "маунт", -1);
                    if (c < 0)
                    {
                        continue;
                    }
                    for (int r = 1; r < e.Data.Count; r++)
                    {
                        string now;
                        if (renames.TryGetValue(SheetLogic.Norm(e.Data[r][c]), out now) && now != null
                            && now != SheetLogic.CellText(e.Data[r][c]) && !e.Formulas.Contains(SheetLogic.Key(r, c)))
                        {
                            book.Set(e, r, c, now);
                            res.Renamed++;
                        }
                    }
                }
            }
        }

        /// <summary>«Собрано?» = «Да» по коллекции из игры. Только «Нет» → «Да», формулы не трогаем.</summary>
        static void Mark(IStructuredBook book, GameSnapshot game, MountIndex index, CollectionResult res)
        {
            if (game.CollectedIds.Count == 0 && game.CollectedMounts.Count == 0)
            {
                return;
            }
            foreach (SheetEntry entry in book.Entries)
            {
                if (entry.Data.Count == 0)
                {
                    continue;
                }
                object[] header = entry.Data[0];
                int cCollected = SheetLogic.FindCol(header, "собрано", -1);
                int cMount = SheetLogic.FindCol(header, "маунт", -1);
                if (cCollected < 0 || cMount < 0)
                {
                    continue;
                }
                int cBoss = SheetLogic.FindCol(header, "босс", -1);
                int cId = IdCol(header);
                string kind = MountIndex.KindOfSheet(entry.Name);
                for (int r = 1; r < entry.Data.Count; r++)
                {
                    object[] row = entry.Data[r];
                    object current = row[cCollected];
                    string mount = SheetLogic.CellText(row[cMount]);
                    if (mount.Length == 0 || !SheetLogic.IsNo(current) || entry.Formulas.Contains(SheetLogic.Key(r, cCollected)))
                    {
                        continue;
                    }
                    long id = cId >= 0 ? MountIndex.IdOf(row[cId]) : 0;
                    if (id == 0)
                    {
                        id = index.Find(kind, row[cMount], cBoss >= 0 ? row[cBoss] : "");
                    }
                    if (!SheetBuilder.IsCollected(id, row[cMount], game.CollectedIds, game.CollectedMounts))
                    {
                        continue;
                    }
                    book.Set(entry, r, cCollected, current is bool ? (object)true : SheetLogic.Yes);
                    if (!res.Marked.Contains(mount))
                    {
                        res.Marked.Add(mount);
                    }
                }
            }
        }

        /// <summary>
        /// Убрать с листов «Рейды &lt;Имя&gt;» / «Подземелья &lt;Имя&gt;» строки маунтов, у которых на общих
        /// листах «Собрано?» = «Да». Возвращает число убранных строк.
        /// </summary>
        public static int RemoveCollected(IStructuredBook book)
        {
            HashSet<string> collected = SheetLogic.CollectedMounts(book);
            if (collected.Count == 0)
            {
                return 0;
            }
            Dictionary<SheetEntry, List<int>> del = new Dictionary<SheetEntry, List<int>>();
            int total = 0;
            foreach (SheetEntry entry in book.Entries)
            {
                if (entry.Data.Count < 2
                    || (SheetLogic.SheetOwner(entry.Name, SheetLogic.RaidPrefix) == null
                        && SheetLogic.SheetOwner(entry.Name, SheetLogic.DungeonPrefix) == null))
                {
                    continue;
                }
                object[] header = entry.Data[0];
                int cMount = SheetLogic.FindCol(header, "маунт", -1);
                if (cMount < 0 || SheetLogic.FindCol(header, "пытался", -1) < 0 || SheetLogic.FindCol(header, "собрано", -1) >= 0)
                {
                    continue;   // не лист персонажа
                }
                List<int> rows = new List<int>();
                for (int r = 1; r < entry.Data.Count; r++)
                {
                    string key = SheetLogic.Norm(entry.Data[r][cMount]);
                    if (key.Length > 0 && collected.Contains(key))
                    {
                        rows.Add(r);
                    }
                }
                if (rows.Count > 0)
                {
                    del[entry] = rows;
                    total += rows.Count;
                }
            }
            if (total == 0)
            {
                return 0;
            }
            book.DeleteRows(del);
            Log.Write(Tr.L("Убраны строки собранных маунтов с листов персонажей: {0} ({1}).", total,
                string.Join(", ", del.Select(kv => "«" + kv.Key.Name + "» — " + kv.Value.Count).ToArray())));
            return total;
        }
    }
}
