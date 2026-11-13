// SheetLogic: ответ аддону — «программа видит таблицу» (Ping) и список «Осталось» (Status).

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
                    int cParts = FindCol(header, PartsPattern, -1);
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
                        Dictionary<string, object> statusItem = StatusItem(kind, entry.Name, r, At(row, cPlace), boss, At(row, cDiff), mount, yes);
                        if (SeparateRow(At(row, cDiff)))
                        {
                            statusItem["sep"] = true;
                            string killedParts = cParts >= 0 ? CellText(At(row, cParts)) : "";
                            if (!yes && killedParts.Length > 0)
                            {
                                statusItem["k"] = killedParts;
                            }
                        }
                        ((List<object>)ch["items"]).Add(statusItem);
                    }
                }
            }

            List<string> worldOwners = new List<string>();
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
                    worldOwners.Add(owner);
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

            // Рары — всем, у кого есть колонка на «Мировых боссах»; убит ли и когда снова можно, считает аддон (every — откат)
            SheetEntry rare = ByName(book, RareSheet);
            if (rare != null && rare.Data.Count > 1)
            {
                object[] header = rare.Data[0];
                int cBoss = FindCol(header, "босс", 4);
                int cMount = FindCol(header, "маунт", 3);
                int cPlace = FindCol(header, "локац", 2);
                int cCollected = FindCol(header, "собрано", -1);
                int cEvery = FindCol(header, "сброс|частота", -1);
                int cRareId = FindCol(header, "^id в игре$", -1);
                for (int r = 1; r < rare.Data.Count; r++)
                {
                    object[] row = rare.Data[r];
                    string boss = CellText(At(row, cBoss));
                    string mount = CellText(At(row, cMount));
                    if ((boss.Length == 0 && mount.Length == 0) || (cCollected >= 0 && IsYes(At(row, cCollected))) || collected.Contains(Norm(mount)))
                    {
                        continue;
                    }
                    string group = RareGroup(cRareId >= 0 ? MountIndex.IdOf(At(row, cRareId)) : 0);
                    foreach (string owner in worldOwners)
                    {
                        Dictionary<string, object> item = StatusItem("rare", rare.Name, r, At(row, cPlace), boss, "", mount, false);
                        item["every"] = RareEvery(cEvery >= 0 ? At(row, cEvery) : "");
                        if (group != null)
                        {
                            item["group"] = group;   // своя группа в окне аддона: «100 %» или «Фарм мобов»
                        }
                        ((List<object>)charFor(owner)["items"]).Add(item);
                    }
                }
            }
            string resetAt = null;
            try
            {
                resetAt = book.GetProperty(ResetProperty);
            }
            catch (Exception swallowed)
            {
                Log.Quiet("SheetLogic: read the week reset", swallowed);
            }
            double reset = Number(resetAt);
            if (reset > 0)
            {
                reply["resetAt"] = reset;
            }
            reply["known"] = KnownDifficultyIds();
            reply["chars"] = order.Select(k => (object)chars[k]).ToList();
            List<object> mounts = StatusMounts(book, collected);
            AddRowInfo(book, order.Select(k => chars[k]), mounts);
            if (mounts.Count > 0)
            {
                reply["mounts"] = mounts;   // счётчик попыток (таблица программы 2.8+)
            }
            List<object> rewards = RewardRows(book);
            if (rewards.Count > 0)
            {
                reply["rewards"] = rewards;   // вкладка «Достижения» в аддоне (программа 2.10+)
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
            // номера боёв: аддон узнаёт убийство по encounterID, даже если игра пишет имя иначе («Галеон» / «Галлеон»)
            List<long> encounters = Encounters.IdsFor(names);
            if (encounters.Count > 0)
            {
                item["e"] = encounters.Select(e => (object)(double)e).ToList();
            }
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
    }
}
