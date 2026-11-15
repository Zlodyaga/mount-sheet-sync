// SheetLogic: как убийство находит свою строку — слова маунта и босса, имена персонажей, место, сложность;
// отметки в списке, у мировых боссов и раров.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MountSheetSync
{
    static partial class SheetLogic
    {
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
            // персонажи — правее «Источника»: при одном персонаже «Осталось попыток» в старых таблицах считается в TRUE/FALSE
            int cSource = data.Count > 0 ? FindCol(data[0], SourcePattern, -1) : -1;
            if (cSource >= 0 && c <= cSource)
            {
                return false;
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
                string part = J.Str(kill, "kind") == "manual" ? null : PartOf(J.Get(kill, "difficultyID"));
                if (part != null && IsNo(current) && SeparateRow(At(row, cDiff)) && book is IStructuredBook)
                {
                    int cParts = FindCol(entry.Data[0], PartsPattern, -1);
                    if (cParts < 0)
                    {
                        List<object> blank = new List<object> { PartsColumn };
                        for (int i = 1; i < entry.Data.Count; i++)
                        {
                            blank.Add("");
                        }
                        ((IStructuredBook)book).AppendColumn(entry, blank, true);
                        cParts = FindCol(entry.Data[0], PartsPattern, -1);
                        data = entry.Data;
                        row = data[r];
                    }
                    string before = CellText(At(row, cParts));
                    string after = AddPart(before, part);
                    if (after != before)
                    {
                        book.Set(entry, r, cParts, after);
                    }
                    if (after.Split(',').Length < AllParts.Length)
                    {
                        // другие сложности ещё можно убить: строка остаётся «Нет»
                        ctx.Cells.Add(CellInfo(entry, r, Truthy(At(row, cMount)) ? JsString(At(row, cMount)) : "", after != before));
                        continue;
                    }
                }
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

        // Рейды с раздельными сохранениями на Normal, Heroic и Mythic (с Warlords of Draenor): на строку «Normal/Heroic/Mythic»
        // за неделю три попытки. Убитые сложности — в скрытой колонке «N/H/M» листа персонажа, она появляется при первом таком
        // убийстве; «Да» в «Пытался…» — только когда убиты все три. В старых рейдах сохранение общее: там строки «Normal/Heroic».
        public const string PartsColumn = "N/H/M";
        const string PartsPattern = "^n h m$";
        static readonly string[] AllParts = { "N", "H", "M" };
        static readonly Regex SeparatePattern = new Regex("normal.*heroic.*mythic");

        static bool SeparateRow(object diffCell)
        {
            return SeparatePattern.IsMatch(Norm(diffCell));
        }

        static string PartOf(object difficultyId)
        {
            double id;
            if (difficultyId == null || !double.TryParse(JsString(difficultyId), NumberStyles.Any, CultureInfo.InvariantCulture, out id))
            {
                return null;
            }
            return id == 14 ? "N" : id == 15 ? "H" : id == 16 ? "M" : null;
        }

        /// <summary>«N» + «M» → «N,M»: всегда в порядке N, H, M.</summary>
        static string AddPart(string parts, string part)
        {
            HashSet<string> have = new HashSet<string>(parts.Split(',').Select(x => x.Trim()));
            have.Add(part);
            return string.Join(",", AllParts.Where(x => have.Contains(x)));
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

        /// <summary>Рар: «Последнее убийство» на его листе — когда и кто. Откат считает аддон, персонажам галочек нет.</summary>
        static bool MarkInRare(IBook book, Dictionary<string, object> kill, List<string> names, MarkContext ctx)
        {
            SheetEntry entry = ByName(book, RareSheet);
            if (entry == null || entry.Data.Count == 0)
            {
                return false;
            }
            object[] header = entry.Data[0];
            int cBoss = FindCol(header, "босс", 4);
            int cMount = FindCol(header, "маунт", 3);
            int cLast = FindCol(header, "^последнее убийство", -1);
            bool loose = J.Str(kill, "kind") == "manual";
            bool found = false;
            for (int r = 1; r < entry.Data.Count; r++)
            {
                object[] row = entry.Data[r];
                string text = JsString(At(row, cBoss));
                if (text.Trim().Length == 0 || !text.Split('/').Any(alt => names.Any(n => NameMatches(n, alt, loose))))
                {
                    continue;
                }
                found = true;
                bool write = cLast >= 0 && !Truthy(J.Get(kill, "again"));
                if (write)
                {
                    DateTime when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(J.Long(kill, "time")).ToLocalTime();
                    book.Set(entry, r, cLast, when.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture) + " " + J.Str(kill, "char"));
                }
                ctx.Cells.Add(CellInfo(entry, r, Truthy(At(row, cMount)) ? JsString(At(row, cMount)) : "", write));
            }
            return found;
        }

        static readonly Regex EveryHours = new Regex(@"(\d+)\s*(ч|h)", RegexOptions.IgnoreCase);

        /// <summary>
        /// Откат рара из «Сброс / частота» (на любом языке таблицы): "weekly", число секунд («каждые 8 ч»; «Без отката» — 0)
        /// или "daily" — ежедневный сброс игры, он же значение по умолчанию.
        /// </summary>
        public static object RareEvery(object cell)
        {
            string text = JsString(cell).ToLowerInvariant();
            // маунт с любого из нескольких раров, у каждого своя добыча на день: общего отката у строки нет
            foreach (string w in new[] { "без отката", "no cooldown" })
            {
                if (text.Contains(w))
                {
                    return 0.0;
                }
            }
            foreach (string w in new[] { "еженедельно", "раз в неделю", "weekly", "wöchentlich", "hebdomadaire", "semanal" })
            {
                if (text.Contains(w))
                {
                    return "weekly";
                }
            }
            Match m = EveryHours.Match(text);
            if (m.Success)
            {
                return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600;
            }
            return "daily";
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
    }
}
