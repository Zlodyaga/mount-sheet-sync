// Перенос состояния между двумя книгами одного устройства (данные «на этом компьютере» → новая таблица Google):
// отметки недели, «Фармлю», «Попыток», «Собрано?», раздельные сохранения и галочки персонажей на общих листах.
// Тексты каталога не трогаем: новая таблица уже создана по свежему каталогу.

using System;
using System.Collections.Generic;
using System.Linq;

namespace MountSheetSync
{
    static partial class SheetLogic
    {
        static readonly string[] StateColumns = { "пытался", "^фармлю", AttemptsPattern, "собрано", PartsPattern };

        static string RowKey(object[] row, int cMount, int cBoss, int cDiff)
        {
            return Norm(At(row, cMount)) + "|" + Norm(At(row, cBoss)) + "|" + (cDiff >= 0 ? Norm(At(row, cDiff)) : "");
        }

        /// <summary>
        /// Скопировать состояние из from в to: строки ищутся по маунту, боссу и сложности на листах с тем же именем.
        /// characters — имена персонажей (их колонки на «Мировых боссах» и «Редких монстрах» — тоже состояние).
        /// Возвращает число изменённых ячеек; Flush — за вызывающим.
        /// </summary>
        public static int CopyState(IBook from, IBook to, IEnumerable<string> characters)
        {
            HashSet<string> names = new HashSet<string>(characters.Select(c => Norm(c)));
            int written = 0;
            foreach (SheetEntry dest in to.Entries)
            {
                SheetEntry src = ByName(from, dest.Name);
                if (src == null || src.Data.Count < 2 || dest.Data.Count < 2)
                {
                    continue;
                }
                object[] dh = dest.Data[0];
                object[] sh = src.Data[0];
                int dMount = FindCol(dh, "маунт", -1), dBoss = FindCol(dh, "босс", -1), dDiff = FindCol(dh, "сложност|режим", -1);
                int sMount = FindCol(sh, "маунт", -1), sBoss = FindCol(sh, "босс", -1), sDiff = FindCol(sh, "сложност|режим", -1);
                if (dMount < 0 || dBoss < 0 || sMount < 0 || sBoss < 0)
                {
                    continue;
                }
                // колонки состояния: по заголовку — и на этом листе, и на исходном
                List<KeyValuePair<int, int>> pairs = new List<KeyValuePair<int, int>>();
                HashSet<int> stateCols = new HashSet<int>();
                foreach (string pattern in StateColumns)
                {
                    int c = FindCol(dh, pattern, -1);
                    if (c >= 0)
                    {
                        stateCols.Add(c);
                    }
                }
                for (int c = 0; c < dh.Length; c++)
                {
                    string head = Norm(StripNote(dh[c]));
                    if (!stateCols.Contains(c) && !(head.Length > 0 && names.Contains(head)))
                    {
                        continue;
                    }
                    int sc = Array.FindIndex(sh, x => Norm(StripNote(x)) == head);
                    if (sc >= 0)
                    {
                        pairs.Add(new KeyValuePair<int, int>(c, sc));
                    }
                }
                if (pairs.Count == 0)
                {
                    continue;
                }
                Dictionary<string, int> rows = new Dictionary<string, int>();
                for (int r = 1; r < src.Data.Count; r++)
                {
                    string key = RowKey(src.Data[r], sMount, sBoss, sDiff);
                    if (!rows.ContainsKey(key))
                    {
                        rows[key] = r;
                    }
                }
                for (int r = 1; r < dest.Data.Count; r++)
                {
                    int sr;
                    if (!rows.TryGetValue(RowKey(dest.Data[r], dMount, dBoss, dDiff), out sr))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<int, int> p in pairs)
                    {
                        if (dest.Formulas.Contains(Key(r, p.Key)) || src.Formulas.Contains(Key(sr, p.Value)))
                        {
                            continue;
                        }
                        object have = At(dest.Data[r], p.Key);
                        object want = At(src.Data[sr], p.Value);
                        if (want == null || CellText(want).Length == 0 || CellText(have) == CellText(want))
                        {
                            continue;
                        }
                        to.Set(dest, r, p.Key, want);
                        written++;
                    }
                }
            }
            return written;
        }
    }
}
