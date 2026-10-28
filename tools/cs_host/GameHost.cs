// Test host: GameData.Read on SavedVariables files given as arguments; prints JSON. With --records prints legacy ParseRecords.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MountSheetSync
{
    static class GameHost
    {
        static int Main(string[] args)
        {
            Tr.Use(Environment.GetEnvironmentVariable("MSS_HOST_LANG") ?? "ru");   // tests expect Russian texts by default
            AppPaths.Init(Path.Combine(Path.GetTempPath(), "mss-gamehost"));
            Log.Init(AppPaths.LogFile, false);
            Dictionary<string, object> outp = new Dictionary<string, object>();
            if (args.Length > 0 && args[0] == "--records")
            {
                outp["records"] = SavedVariables.ParseRecords(SavedVariables.ReadShared(args[1])).Select(r => (object)r.Keys.OrderBy(k => k).ToList<object>()).ToList();
            }
            else if (args.Length > 0 && args[0] == "--lua")
            {
                outp["lua"] = LuaData.Parse(File.ReadAllBytes(args[1]));
            }
            else
            {
                GameSnapshot g = GameData.Read(args);
                outp["chars"] = g.Characters.Select(c => (object)new Dictionary<string, object> {
                    { "name", c.Name }, { "realm", c.Realm }, { "class", c.Class }, { "level", (double)c.Level }, { "seen", (double)c.Seen }, { "fromKills", c.FromKills },
                    { "statsAt", (double)c.StatsAt },
                    { "stats", c.Stats.ToDictionary(kv => kv.Key.ToString(), kv => (object)(double)kv.Value) } }).ToList();
                outp["mounts"] = g.CollectedMounts.Cast<object>().ToList();
                outp["drops"] = g.Drops.Select(d => (object)new Dictionary<string, object> {
                    { "id", (double)d.Id }, { "name", d.Name }, { "from", d.From }, { "got", d.Collected } }).ToList();
                outp["all"] = g.Mounts.Select(d => (object)new Dictionary<string, object> {
                    { "id", (double)d.Id }, { "name", d.Name }, { "from", d.From }, { "type", (double)d.Type }, { "got", d.Collected } }).ToList();
                outp["ids"] = g.CollectedIds.OrderBy(x => x).Select(x => (object)(double)x).ToList();
                outp["updated"] = (double)g.CollectionUpdated;
                outp["journal"] = (double)g.JournalSize;
                outp["problems"] = g.Problems.Cast<object>().ToList();
            }
            byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Write(outp, false));
            Stream stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            return 0;
        }
    }
}
