// Test host: prints SheetBuilder requests for a plan given on stdin.
// stdin: {"title":..., "chars":[{"name":..,"raids":true,"dungeons":false}], "collected":[...], "small": N, "defaultSheetId": 0}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    static class BuilderHost
    {
        static int Main(string[] args)
        {
            Tr.Use(Environment.GetEnvironmentVariable("MSS_HOST_LANG") ?? "ru");   // tests expect Russian texts by default
            Console.InputEncoding = new UTF8Encoding(false);
            string input = Console.In.ReadToEnd();
            Dictionary<string, object> job = (Dictionary<string, object>)Json.Parse(input);
            BuildPlan plan = new BuildPlan();
            plan.Title = J.Str(job, "title");
            plan.Today = new DateTime(2026, 10, 21);
            foreach (object o in J.Arr(J.Get(job, "chars")))
            {
                Dictionary<string, object> c = J.Obj(o);
                plan.Characters.Add(new BuildCharacter(J.Str(c, "name"), J.Truthy(c, "raids"), J.Truthy(c, "dungeons")));
            }
            plan.CollectedMounts = J.Arr(J.Get(job, "collected")).Select(x => SheetLogic.JsString(x)).ToList();
            Catalog cat = Catalog.Embedded();
            long small = J.Long(job, "small");
            if (small > 0)
            {
                Catalog c2 = new Catalog();
                c2.Version = cat.Version;
                c2.Raids = Cut(cat.Raids, J.Arr(J.Get(job, "raidMounts")));
                c2.Dungeons = Cut(cat.Dungeons, J.Arr(J.Get(job, "dungeonMounts")));
                c2.World = Cut(cat.World, J.Arr(J.Get(job, "worldMounts")));
                cat = c2;
            }
            plan.Catalog = cat;
            Dictionary<string, object> outp = new Dictionary<string, object>();
            try
            {
                outp["create"] = SheetBuilder.CreateBody(plan.Title);
                outp["requests"] = SheetBuilder.Requests(plan, J.Long(job, "defaultSheetId"));
                outp["finish"] = SheetBuilder.FinishRequests(cat);
            }
            catch (ArgumentException ex)
            {
                outp["error"] = ex.Message;
            }
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Write(outp, false));
            Stream stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            return 0;
        }

        static CatalogSheet Cut(CatalogSheet s, List<object> mounts)
        {
            CatalogSheet c = new CatalogSheet();
            c.Key = s.Key;
            c.Name = s.Name;
            c.Columns = s.Columns;
            HashSet<string> keep = new HashSet<string>(mounts.Select(m => SheetLogic.JsString(m)));
            c.Rows = s.Rows.Where(r => keep.Contains(SheetLogic.JsString(r[s.Col("Маунт")]))).ToList();
            return c;
        }
    }
}
