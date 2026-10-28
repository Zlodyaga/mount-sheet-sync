// Счётчик попыток (2.8): на общих листах «Рейды», «Подземелья» и «Мировые боссы» колонки «Попыток» и
// «Шанс за все попытки» рядом с «Шансом выпадения». «Попыток» — сколько раз этого босса убили (все персонажи вместе)
// с подходящей сложностью, пока маунт не собран; программа прибавляет по одной за убийство, число можно поправить
// руками. «Шанс за все попытки» — формула 1 − (1 − шанс)^попыток. Строки без случайного шанса (достижения,
// гарантированные, покупки) — пустые, их программа не считает. Список «Осталось» для аддона получает эти строки
// («mounts»): аддон пишет попытку в чат при убийстве и показывает её в подсказке босса.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MountSheetSync
{
    static partial class SheetLogic
    {
        public const string AttemptsPattern = "^попыток$";
        public const string ChancePattern = "^шанс за все попытки";
        const string DropPattern = "^шанс выпадения";
        /// <summary>«Источник» — последняя общая колонка «Мировых боссов», за ней колонки персонажей.</summary>
        public const string SourcePattern = "^источник$";

        static readonly Regex ChanceText = new Regex(@"^\s*[~≈<]?\s*(\d+(?:[.,]\d+)?)\s*%");

        /// <summary>Шанс выпадения за одну попытку (0 &lt; p &lt; 1); 0 — не случайный (достижение, 100 %, покупка…).</summary>
        public static double DropChance(object cell)
        {
            if (cell is double)
            {
                double d = (double)cell;
                return d > 0 && d < 1 ? d : 0;
            }
            Match m = ChanceText.Match(JsString(cell));
            if (!m.Success)
            {
                return 0;
            }
            double v;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return 0;
            }
            return v > 0 && v < 100 ? v / 100 : 0;
        }

        /// <summary>Шанс дробью из целых чисел («1/100», «1/1000»): формула тогда не зависит от десятичного знака локали.</summary>
        public static string Fraction(double p)
        {
            decimal d = Math.Round((decimal)p, 8);
            long den = 1;
            while (d != decimal.Floor(d) && den < 100000000)
            {
                d *= 10;
                den *= 10;
            }
            return ((long)d).ToString(CultureInfo.InvariantCulture) + "/" + den.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>«Шанс за все попытки» строки r (с 0): =IF(N(P5)>0, 1-(1-1/100)^P5, "").</summary>
        public static string ChanceFormula(int attemptsCol, int r, double p)
        {
            string a = SheetBuilder.ColumnName(attemptsCol) + (r + 1).ToString(CultureInfo.InvariantCulture);
            return "=IF(N(" + a + ")>0, 1-(1-" + Fraction(p) + ")^" + a + ", \"\")";
        }

        /// <summary>1 − (1 − p)^n в процентах: «31%», «4,9%» (меньше 10 % — с десятой).</summary>
        public static string ChancePercent(double n, double p)
        {
            double v = (1 - Math.Pow(1 - p, n)) * 100;
            return (v < 10 ? Math.Round(v, 1) : Math.Round(v)).ToString(v < 10 ? "0.#" : "0", CultureInfo.CurrentCulture) + "%";
        }

        /// <summary>Число попыток в ячейке (пустая ячейка — строка без счётчика).</summary>
        public static bool AttemptCount(object value, out double n)
        {
            n = 0;
            if (value is double)
            {
                n = (double)value;
                return !double.IsNaN(n);
            }
            string s = value as string;
            if (s == null || s.Trim().Length == 0)
            {
                return false;
            }
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n);
        }

        /// <summary>Общие листы, на которых убийство этого вида считается попыткой.</summary>
        static string MasterFor(string kind)
        {
            if (kind == "raid")
            {
                return SheetBuilder.RaidsSheet;
            }
            if (kind == "party" || kind == "scenario")
            {
                return SheetBuilder.DungeonsSheet;
            }
            if (kind == "world" || kind == "manual")
            {
                return SheetBuilder.WorldSheet;   // отметка вручную — без сложности: только мировые боссы
            }
            return null;
        }

        /// <summary>
        /// Убийство → +1 в «Попыток» у несобранных маунтов этого босса (сложность подходит). Повтор той же записи
        /// (связь оборвалась уже после записи в таблицу) прибавил бы ещё одну — число всегда можно поправить руками.
        /// </summary>
        static void CountAttempts(IBook book, Dictionary<string, object> kill, string kind, List<string> names, Dictionary<string, object> o)
        {
            if (Truthy(J.Get(kill, "again")))
            {
                return;   // повтор уже отправленного убийства после «Сброса недели»
            }
            string masterName = MasterFor(kind);
            SheetEntry entry = masterName != null ? ByName(book, masterName) : null;
            if (entry == null || entry.Data.Count < 2)
            {
                return;
            }
            object[] header = entry.Data[0];
            int cAttempts = FindCol(header, AttemptsPattern, -1);
            int cCollected = FindCol(header, "собрано", -1);
            if (cAttempts < 0)
            {
                return;   // таблица без счётчика (скрипт Code.gs, своя таблица)
            }
            int cBoss = FindCol(header, "босс", 4);
            int cDiff = FindCol(header, "сложност|режим", -1);
            int cMount = FindCol(header, "маунт", 3);
            int cPlace = FindCol(header, "^рейд|подземел|локац", 2);
            int cDrop = FindCol(header, DropPattern, -1);
            bool world = masterName == SheetBuilder.WorldSheet;
            bool loose = kind == "manual";
            List<object> counted = new List<object>();
            for (int r = 1; r < entry.Data.Count; r++)
            {
                object[] row = entry.Data[r];
                double n;
                if (!AttemptCount(At(row, cAttempts), out n) || entry.Formulas.Contains(Key(r, cAttempts)))
                {
                    continue;
                }
                if (cCollected >= 0 && IsYes(At(row, cCollected)))
                {
                    continue;   // маунт уже собран — счёт остаётся каким был
                }
                bool hit;
                if (world)
                {
                    string text = JsString(At(row, cBoss));
                    hit = text.Trim().Length > 0 && text.Split('/').Any(alt => names.Any(nm => NameMatches(nm, alt, loose)));
                }
                else
                {
                    hit = RowMatch(kill, names, At(row, cBoss), cDiff >= 0 ? At(row, cDiff) : "", At(row, cPlace)) == Match3.Yes;
                }
                if (!hit)
                {
                    continue;
                }
                n = Math.Max(0, Math.Floor(n)) + 1;
                book.Set(entry, r, cAttempts, n);
                Dictionary<string, object> a = new Dictionary<string, object>();
                a["mount"] = CellText(At(row, cMount));
                a["n"] = n;
                a["p"] = cDrop >= 0 ? DropChance(At(row, cDrop)) : 0.0;
                a["sheet"] = entry.Name;
                a["row"] = (double)(r + 1);
                counted.Add(a);
            }
            if (counted.Count > 0)
            {
                o["attempts"] = counted;
            }
        }

        /// <summary>
        /// Строки со счётчиком для аддона: несобранные маунты со случайным шансом — кто их роняет (как строки
        /// «Осталось»: имена босса, сложности), сколько попыток (n) и шанс за попытку (p).
        /// </summary>
        public static List<object> StatusMounts(IBook book, HashSet<string> collected)
        {
            List<object> list = new List<object>();
            foreach (string[] m in new[]
            {
                new[] { SheetBuilder.RaidsSheet, "raid" }, new[] { SheetBuilder.DungeonsSheet, "dungeon" }, new[] { SheetBuilder.WorldSheet, "world" },
            })
            {
                SheetEntry entry = ByName(book, m[0]);
                if (entry == null || entry.Data.Count < 2)
                {
                    continue;
                }
                object[] header = entry.Data[0];
                int cAttempts = FindCol(header, AttemptsPattern, -1);
                int cDrop = FindCol(header, DropPattern, -1);
                if (cAttempts < 0 || cDrop < 0)
                {
                    continue;
                }
                bool world = m[1] == "world";
                int cCollected = FindCol(header, "собрано", -1);
                int cBoss = FindCol(header, "босс", 4);
                int cDiff = world ? -1 : FindCol(header, "сложност|режим", 5);
                int cMount = FindCol(header, "маунт", 3);
                int cPlace = FindCol(header, world ? "локац" : "^рейд|подземел", 2);
                int cId = FindCol(header, "^id в игре$", -1);
                for (int r = 1; r < entry.Data.Count; r++)
                {
                    object[] row = entry.Data[r];
                    double n;
                    double p = DropChance(At(row, cDrop));
                    string boss = CellText(At(row, cBoss));
                    string mount = CellText(At(row, cMount));
                    if (p <= 0 || !AttemptCount(At(row, cAttempts), out n) || boss.Length == 0 || mount.Length == 0)
                    {
                        continue;
                    }
                    if ((cCollected >= 0 && IsYes(At(row, cCollected))) || collected.Contains(Norm(mount)))
                    {
                        continue;
                    }
                    Dictionary<string, object> item = StatusItem(m[1], entry.Name, r, At(row, cPlace), boss, cDiff >= 0 ? At(row, cDiff) : "", mount, false);
                    item["n"] = Math.Max(0, Math.Floor(n));
                    item["p"] = p;
                    long id = cId >= 0 ? MountIndex.IdOf(At(row, cId)) : 0;
                    if (id > 0)
                    {
                        item["id"] = (double)id;
                    }
                    list.Add(item);
                }
            }
            return list;
        }

        /// <summary>Значения новых колонок для строки общего листа: «Попыток» = 0 и формула шанса (или пусто).</summary>
        public static object[] AttemptCells(object dropCell, int attemptsCol, int r)
        {
            double p = DropChance(dropCell);
            if (p <= 0)
            {
                return new object[] { "", "" };
            }
            return new object[] { 0.0, new SheetBuilder.Formula(ChanceFormula(attemptsCol, r, p)) };
        }
    }
}
