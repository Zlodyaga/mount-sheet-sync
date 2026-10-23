// Test host: CollectionSync on a MemoryBook. Reads a JSON job from the file in args[0], prints JSON.
//   job = { catalog: {...} | null (embedded), sheets: [{name, rows, formulas: [[r,c]]}],
//           game: {ids, names, updated}, mode: "run" | "remove" | "handle", request: {...} }
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    static class CollectionHost
    {
        static int Main(string[] args)
        {
            Tr.Use(Environment.GetEnvironmentVariable("MSS_HOST_LANG") ?? "ru");   // tests expect Russian texts by default
            AppPaths.Init(Path.Combine(Path.GetTempPath(), "mss-collectionhost"));
            Log.Init(AppPaths.LogFile, false);
            Dictionary<string, object> job = (Dictionary<string, object>)Json.Parse(File.ReadAllText(args[0], Encoding.UTF8));
            Catalog catalog = J.Get(job, "catalog") != null ? Catalog.Parse(Json.Write(J.Get(job, "catalog"), false)) : Catalog.Embedded();
            MemoryBook book = new MemoryBook();
            book.BookTitle = "Тест";
            book.Creator = J.Str(job, "creator");
            foreach (KeyValuePair<string, object> kv in J.Obj(J.Get(job, "properties")) ?? new Dictionary<string, object>())
            {
                book.Properties[kv.Key] = SheetLogic.JsString(kv.Value);
            }
            long sid = 100;
            foreach (object o in J.Arr(J.Get(job, "sheets")))
            {
                Dictionary<string, object> s = J.Obj(o);
                SheetEntry e = new SheetEntry();
                e.Name = J.Str(s, "name");
                e.SheetId = sid++;
                List<object> rows = J.Arr(J.Get(s, "rows"));
                int width = rows.Count == 0 ? 0 : rows.Max(r => J.Arr(r).Count);
                foreach (object r in rows)
                {
                    List<object> cells = J.Arr(r);
                    object[] row = new object[width];
                    for (int c = 0; c < width; c++)
                    {
                        row[c] = c < cells.Count && cells[c] != null ? cells[c] : "";
                    }
                    e.Data.Add(row);
                }
                foreach (object f in J.Arr(J.Get(s, "formulas")))
                {
                    List<object> rc = J.Arr(f);
                    e.Formulas.Add(SheetLogic.Key((int)(double)rc[0], (int)(double)rc[1]));
                }
                if (J.Get(s, "grid") != null)
                {
                    width = Math.Max(width, (int)J.Long(s, "grid"));
                }
                e.GridColumns = width;
                book.Entries.Add(e);
            }
            Dictionary<string, object> g = J.Obj(J.Get(job, "game")) ?? new Dictionary<string, object>();
            GameSnapshot game = new GameSnapshot();
            foreach (object id in J.Arr(J.Get(g, "ids")))
            {
                game.CollectedIds.Add((long)(double)id);
            }
            game.CollectedMounts = J.Arr(J.Get(g, "names")).Select(n => SheetLogic.JsString(n)).ToList();
            game.CollectionUpdated = J.Long(g, "updated");

            Dictionary<string, object> outp = new Dictionary<string, object>();
            string mode = J.Str(job, "mode");
            if (mode == "remove")
            {
                outp["removed"] = (double)CollectionSync.RemoveCollected(book);
            }
            else if (mode == "handle")
            {
                outp["reply"] = SheetLogic.Handle(book, J.Obj(J.Get(job, "request")));
            }
            else if (mode == "upgrade")
            {
                outp["changed"] = SheetUpgrade.Ensure(book);
                outp["applied"] = book.Applied.Select(a => (object)a).ToList();
                outp["properties"] = book.Properties.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            }
            else if (mode == "diff")
            {
                List<BuildCharacter> want = new List<BuildCharacter>();
                foreach (object o in J.Arr(J.Get(job, "desired")))
                {
                    Dictionary<string, object> c = J.Obj(o);
                    want.Add(new BuildCharacter(J.Str(c, "name"), J.Truthy(c, "raids"), J.Truthy(c, "dungeons"), J.Truthy(c, "world")));
                }
                CharacterChanges ch = SheetCreator.Diff(book, want);
                outp["diff"] = new Dictionary<string, object>
                {
                    { "addedSheets", ch.AddedSheets.Cast<object>().ToList() }, { "removedSheets", ch.RemovedSheets.Cast<object>().ToList() },
                    { "addedWorld", ch.AddedWorld.Cast<object>().ToList() }, { "removedWorld", ch.RemovedWorld.Cast<object>().ToList() },
                    { "removedCharacters", ch.RemovedCharacters.Cast<object>().ToList() }, { "summary", ch.Summary() },
                };
                outp["table"] = SheetCreator.TableCharacters(book).Select(c => (object)new Dictionary<string, object>
                {
                    { "name", c.Name }, { "raid", c.RaidSheet != null }, { "dungeon", c.DungeonSheet != null }, { "world", (double)c.WorldColumn },
                }).ToList();
            }
            else
            {
                CollectionResult res = CollectionSync.Run(book, game, MountIndex.For(catalog));
                outp["result"] = new Dictionary<string, object>
                {
                    { "marked", res.Marked.Cast<object>().ToList() }, { "removed", (double)res.Removed },
                    { "renamed", (double)res.Renamed }, { "idColumns", res.IdColumnsAdded.Cast<object>().ToList() },
                    { "changed", res.Changed }, { "summary", res.Summary() },
                };
            }
            outp["structure"] = book.Structure.Cast<object>().ToList();
            outp["writes"] = book.Writes.Cast<object>().ToList();
            outp["sheets"] = book.Entries.Select(e => (object)new Dictionary<string, object>
            {
                { "name", e.Name },
                { "rows", e.Data.Select(r => (object)r.ToList()).ToList() },
                { "formulas", e.Formulas.OrderBy(k => k).Select(k => (object)new List<object> { (double)(k / 100000), (double)(k % 100000) }).ToList() },
            }).ToList();
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Write(outp, false));
            Stream stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            return 0;
        }
    }
}
