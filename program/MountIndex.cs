// Каталог маунтов как справочник: какой mountID у строки таблицы (по названию маунта, прежним
// названиям каталога и боссу) и какое у маунта название сейчас — «как в игре».

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MountSheetSync
{
    sealed class MountIndex
    {
        sealed class Item
        {
            public string Kind = "";
            public string Name = "";
            public string NameKey = "";
            public string BossKey = "";
            public long Id;
            public List<string> AliasKeys = new List<string>();
        }

        readonly List<Item> items = new List<Item>();
        readonly Dictionary<long, string> names = new Dictionary<long, string>();
        readonly Dictionary<long, HashSet<string>> aliasKeys = new Dictionary<long, HashSet<string>>();
        /// <summary>«вид:mountID» → статистики убийств игры, в которых считаются попытки (колонка каталога «ID статистики»).</summary>
        readonly Dictionary<string, List<long>> statistics = new Dictionary<string, List<long>>();

        public const string StatisticsColumn = "ID статистики";

        public static MountIndex For(Catalog catalog)
        {
            MountIndex index = new MountIndex();
            foreach (CatalogSheet sheet in new[] { catalog.Raids, catalog.Dungeons, catalog.World })
            {
                int cMount = sheet.Col("Маунт");
                int cBoss = sheet.Columns.FindIndex(x => x.StartsWith("Босс", StringComparison.Ordinal));
                int cId = sheet.Col(SheetBuilder.IdColumn);
                int cStats = sheet.Col(StatisticsColumn);
                if (cMount < 0)
                {
                    continue;
                }
                foreach (object[] row in sheet.Rows)
                {
                    Item it = new Item();
                    it.Kind = sheet.Key;
                    it.Name = SheetLogic.CellText(row[cMount]);
                    it.NameKey = SheetLogic.Norm(it.Name);
                    it.BossKey = cBoss >= 0 ? SheetLogic.Norm(row[cBoss]) : "";
                    it.Id = cId >= 0 ? IdOf(row[cId]) : 0;
                    List<string> old;
                    if (it.Id > 0 && catalog.Aliases.TryGetValue(it.Id, out old))
                    {
                        it.AliasKeys = old.Select(o => SheetLogic.Norm(o)).Where(o => o.Length > 0).ToList();
                    }
                    if (it.NameKey.Length == 0)
                    {
                        continue;
                    }
                    if (it.Id > 0 && cStats >= 0)
                    {
                        List<long> stats = Encounters.Ids(row[cStats]);
                        if (stats.Count > 0)
                        {
                            index.statistics[sheet.Key + ":" + it.Id.ToString(CultureInfo.InvariantCulture)] = stats;
                        }
                    }
                    index.items.Add(it);
                    if (it.Id > 0)
                    {
                        if (!index.names.ContainsKey(it.Id))
                        {
                            index.names[it.Id] = it.Name;
                        }
                        HashSet<string> keys;
                        if (!index.aliasKeys.TryGetValue(it.Id, out keys))
                        {
                            keys = new HashSet<string>();
                            index.aliasKeys[it.Id] = keys;
                        }
                        keys.UnionWith(it.AliasKeys);
                    }
                }
            }
            return index;
        }

        /// <summary>Статистики убийств для маунта на листе этого вида (raids / dungeons / world); пусто — нет.</summary>
        public List<long> StatisticsOf(string kind, long id)
        {
            List<long> list;
            if (kind == null || id <= 0 || !statistics.TryGetValue(kind + ":" + id.ToString(CultureInfo.InvariantCulture), out list))
            {
                return new List<long>();
            }
            return list;
        }

        /// <summary>mountID из ячейки (число или текст с числом); 0 — нет.</summary>
        public static long IdOf(object value)
        {
            if (value is double)
            {
                double d = (double)value;
                return d > 0 && d < 1e12 && Math.Floor(d) == d ? (long)d : 0;
            }
            if (value is long)
            {
                return (long)value > 0 ? (long)value : 0;
            }
            if (value is int)
            {
                return (int)value > 0 ? (int)value : 0;
            }
            long n;
            string s = SheetLogic.CellText(value);
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n : 0;
        }

        /// <summary>Вид листа для поиска в каталоге: raids / dungeons / world или null (любой).</summary>
        public static string KindOfSheet(string sheetName)
        {
            string n = SheetLogic.Norm(sheetName);
            if (n == SheetLogic.Norm(SheetBuilder.RaidsSheet))
            {
                return "raids";
            }
            if (n == SheetLogic.Norm(SheetBuilder.DungeonsSheet))
            {
                return "dungeons";
            }
            if (n == SheetLogic.Norm(SheetBuilder.WorldSheet))
            {
                return "world";
            }
            return null;
        }

        /// <summary>
        /// mountID строки по названию (нынешнему или прежнему) и боссу; 0 — не узнали или неоднозначно.
        /// kind = null — искать во всём каталоге.
        /// </summary>
        public long Find(string kind, object mountCell, object bossCell)
        {
            string key = SheetLogic.Norm(mountCell);
            if (key.Length == 0)
            {
                return 0;
            }
            List<Item> hits = items.Where(i => (kind == null || i.Kind == kind) && i.Id > 0
                && (i.NameKey == key || i.AliasKeys.Contains(key))).ToList();
            if (hits.Count == 0)
            {
                return 0;
            }
            if (hits.Select(h => h.Id).Distinct().Count() == 1)
            {
                return hits[0].Id;
            }
            string boss = SheetLogic.Norm(bossCell);
            List<Item> byBoss = hits.Where(h => h.BossKey == boss).ToList();
            if (byBoss.Count > 0 && byBoss.Select(h => h.Id).Distinct().Count() == 1)
            {
                return byBoss[0].Id;
            }
            return 0;
        }

        /// <summary>Название маунта в каталоге сейчас (null — маунта нет в каталоге).</summary>
        public string NameOf(long id)
        {
            string n;
            return names.TryGetValue(id, out n) ? n : null;
        }

        /// <summary>Текст ячейки — прежнее название этого маунта в каталоге (его можно заменить новым).</summary>
        public bool IsOldName(long id, object cell)
        {
            HashSet<string> keys;
            return aliasKeys.TryGetValue(id, out keys) && keys.Contains(SheetLogic.Norm(cell));
        }
    }
}
