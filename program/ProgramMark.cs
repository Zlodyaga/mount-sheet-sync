using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MountSheetSync
{
    /// <summary>
    /// «Программа запущена» для аддона: пока программа работает, она раз в минуту пишет в папку аддона
    /// Program.lua (MountSheetSyncProgram.seen — время записи). Игра читает файл при входе и /reload; если отметка
    /// старая или её нет, аддон мягко подсказывает, что работает без программы: убийства запоминает, а в таблицу
    /// они попадут, когда программа запустится. При выходе программа ставит seen = 0.
    /// </summary>
    class ProgramMark
    {
        public const string FileName = "Program.lua";
        static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
        static readonly TimeSpan RescanEvery = TimeSpan.FromMinutes(10);
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        readonly object gate = new object();
        List<string> dirs = new List<string>();
        string wowPath;
        DateTime nextRescan = DateTime.MinValue;
        DateTime nextBeat = DateTime.MinValue;
        bool lastSheet;
        bool stopped;

        /// <summary>Из цикла синхронизации: не чаще раза в минуту (или сразу, если настройки поменялись).</summary>
        public void Beat(Settings settings)
        {
            lock (gate)
            {
                if (stopped)
                {
                    return;
                }
                DateTime now = DateTime.UtcNow;
                bool sheet = settings.IsConfigured;
                if (settings.WowPath != wowPath)
                {
                    wowPath = settings.WowPath;
                    nextRescan = DateTime.MinValue;
                }
                if (now < nextBeat && sheet == lastSheet && now < nextRescan)
                {
                    return;
                }
                if (now >= nextRescan)
                {
                    try
                    {
                        dirs = WowLocator.FindRetailDirs(wowPath);
                    }
                    catch (Exception)
                    {
                        dirs = new List<string>();
                    }
                    nextRescan = now + RescanEvery;
                }
                lastSheet = sheet;
                nextBeat = now + Every;
                WriteAll(Text(UnixNow(), sheet));
            }
        }

        /// <summary>Программа закрывается: аддон при следующем входе сразу увидит, что её нет.</summary>
        public void Stop()
        {
            lock (gate)
            {
                if (stopped)
                {
                    return;
                }
                stopped = true;
                if (nextBeat != DateTime.MinValue)
                {
                    WriteAll(Text(0, lastSheet));
                }
            }
        }

        public static string Text(long seen, bool sheet)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-- Mount Sheet Sync: MountSheetSync.exe rewrites this file every minute while it runs,\r\n");
            sb.Append("-- so the addon knows whether kills go to the sheet right away. There is no need to edit it.\r\n");
            sb.Append("MountSheetSyncProgram = { seen = ").Append(seen)
              .Append(", sheet = ").Append(sheet ? "true" : "false")
              .Append(", version = \"").Append(Program.Version)
              .Append("\", addon = \"").Append(Program.MinAddon).Append("\" }\r\n");
            return sb.ToString();
        }

        static long UnixNow()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        void WriteAll(string text)
        {
            foreach (string dir in dirs)
            {
                string addonDir = SheetData.AddonDir(dir);
                if (!Directory.Exists(addonDir))
                {
                    continue;
                }
                try
                {
                    FileUtil.WriteAtomic(Path.Combine(addonDir, FileName), text);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
