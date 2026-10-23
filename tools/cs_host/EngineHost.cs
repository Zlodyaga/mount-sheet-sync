// Test host: runs SheetLogic (the C# port of Code.gs) on a workbook JSON, like gas_harness.js does with Code.gs.
// stdin: {"workbook": {...}, "body": "<request JSON>", "opts": {"noProps": bool}}
// stdout: {"reply": {...}, "writes": [...], "calls": [...], "properties": {...}}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MountSheetSync
{
    static class EngineHost
    {
        static int Main()
        {
            Tr.Use(Environment.GetEnvironmentVariable("MSS_HOST_LANG") ?? "ru");   // tests expect Russian texts by default
            Console.InputEncoding = new UTF8Encoding(false);
            string input = Console.In.ReadToEnd();
            Dictionary<string, object> job = (Dictionary<string, object>)Json.Parse(input);
            Dictionary<string, object> wb = J.Obj(job["workbook"]);
            Dictionary<string, object> opts = J.Obj(J.Get(job, "opts")) ?? new Dictionary<string, object>();
            MemoryBook book = new MemoryBook();
            book.BookTitle = J.Str(wb, "name");
            book.BookUrl = "https://docs.google.com/spreadsheets/d/TEST-SHEET/edit";
            book.PropertiesBroken = J.Truthy(opts, "noProps");
            Dictionary<string, object> props = J.Obj(J.Get(wb, "properties"));
            if (props != null)
            {
                foreach (KeyValuePair<string, object> kv in props)
                {
                    book.Properties[kv.Key] = SheetLogic.JsString(kv.Value);
                }
            }
            foreach (object so in J.Arr(J.Get(wb, "sheets")))
            {
                Dictionary<string, object> s = J.Obj(so);
                SheetEntry e = new SheetEntry();
                e.Name = J.Str(s, "name");
                List<object> rows = J.Arr(J.Get(s, "values"));
                int width = 0;
                foreach (object r in rows)
                {
                    width = Math.Max(width, J.Arr(r).Count);
                }
                foreach (object r in rows)
                {
                    object[] row = new object[width];
                    List<object> src = J.Arr(r);
                    for (int c = 0; c < width; c++)
                    {
                        row[c] = c < src.Count && src[c] != null ? src[c] : "";
                    }
                    e.Data.Add(row);
                }
                Dictionary<string, object> f = J.Obj(J.Get(s, "formulas"));
                if (f != null)
                {
                    foreach (KeyValuePair<string, object> kv in f)
                    {
                        string[] rc = kv.Key.Split(',');
                        if (SheetLogic.JsString(kv.Value).Length > 0)
                        {
                            e.Formulas.Add(SheetLogic.Key(int.Parse(rc[0]) - 1, int.Parse(rc[1]) - 1));
                        }
                    }
                }
                book.Entries.Add(e);
            }

            Dictionary<string, object> reply;
            object req = null;
            try
            {
                req = Json.Parse(J.Str(job, "body"));
            }
            catch (Exception)
            {
            }
            if (!(req is Dictionary<string, object>))
            {
                reply = new Dictionary<string, object>();
                reply["ok"] = false;
                reply["error"] = "Запрос не является JSON.";
            }
            else
            {
                try
                {
                    reply = SheetLogic.Handle(book, (Dictionary<string, object>)req);
                }
                catch (Exception ex)
                {
                    reply = new Dictionary<string, object>();
                    reply["ok"] = false;
                    reply["error"] = ex.ToString();
                }
            }
            Dictionary<string, object> outp = new Dictionary<string, object>();
            outp["reply"] = reply;
            outp["writes"] = book.Writes.ConvertAll(w => (object)w);
            outp["calls"] = book.Calls.ConvertAll(n => (object)(double)n);
            Dictionary<string, object> p = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> kv in book.Properties)
            {
                p[kv.Key] = kv.Value;
            }
            outp["properties"] = p;
            Stream stdout = Console.OpenStandardOutput();
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Write(outp, false));
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
            return 0;
        }
    }
}
