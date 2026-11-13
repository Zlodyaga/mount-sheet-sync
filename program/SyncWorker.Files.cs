// SyncWorker: файлы аддона — есть ли аддон, запись SheetData.lua и Program.lua, поиск файлов игры.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace MountSheetSync
{
    sealed partial class SyncWorker
    {
        /// <summary>Аддон стоит хотя бы в одной найденной папке игры.</summary>
        bool AddonInstalled()
        {
            foreach (string dir in dirs)
            {
                if (Directory.Exists(SheetData.AddonDir(dir)))
                {
                    return true;
                }
            }
            return false;
        }

        public static string NoAddonText()
        {
            return Tr._("Установите аддон из CurseForge (или скопируйте папку MountSheetSync из архива в World of Warcraft\\_retail_\\Interface\\AddOns), включите его в игре и выйдите на выбор персонажа.");
        }

        /// <summary>Время изменения SheetData.lua после нашей записи (или проверки) — по папкам аддона.</summary>
        readonly Dictionary<string, DateTime> sheetStamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// SheetData.lua тронул кто-то другой — обычно приложение CurseForge, поставившее новую версию аддона с пустым
        /// списком, — вернуть наш сразу (проверка — время изменения файла, каждые несколько секунд), а не через 10 минут.
        /// </summary>
        void KeepSheetFiles()
        {
            if (sheetLua == null)
            {
                return;
            }
            foreach (string dir in dirs)
            {
                string addonDir = SheetData.AddonDir(dir);
                if (!Directory.Exists(addonDir))
                {
                    continue;
                }
                string path = Path.Combine(addonDir, SheetData.FileName);
                DateTime known;
                DateTime now;
                try
                {
                    now = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                }
                catch (Exception)
                {
                    continue;
                }
                if (!sheetStamps.TryGetValue(path, out known) || known != now)
                {
                    WriteSheetFiles();
                    return;
                }
            }
        }

        /// <summary>SheetData.lua во все найденные папки аддона, где он другой (или его нет).</summary>
        void WriteSheetFiles()
        {
            if (sheetLua == null)
            {
                return;
            }
            bool anyAddon = false;
            foreach (string dir in dirs)
            {
                string addonDir = SheetData.AddonDir(dir);
                if (!Directory.Exists(addonDir))
                {
                    continue;
                }
                anyAddon = true;
                string path = Path.Combine(addonDir, SheetData.FileName);
                try
                {
                    if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == sheetLua)
                    {
                        sheetStamps[path] = File.GetLastWriteTimeUtc(path);
                        continue;
                    }
                    DateTime ours;
                    bool replaced = sheetStamps.TryGetValue(path, out ours) && (!File.Exists(path) || File.GetLastWriteTimeUtc(path) != ours);
                    FileUtil.WriteAtomic(path, sheetLua);
                    sheetStamps[path] = File.GetLastWriteTimeUtc(path);
                    if (replaced)
                    {
                        Log.Write(Tr.L("Список «Осталось» записан заново: {0} (файл заменили — например, CurseForge обновил аддон).", path));
                    }
                    sheetProblems.Remove(path);
                    if (sheetWritten.Add(path))
                    {
                        Log.Write(Tr.L("Список «Осталось» записан для аддона: {0} (в игре он появится после входа или /reload).", path));
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                    {
                        throw;
                    }
                    if (sheetProblems.Add(path))
                    {
                        Log.Write(Tr.L("Не удалось записать {0}: {1}", path, ex.Message));
                    }
                }
            }
            if (!anyAddon && !saidNoAddon && dirs.Count > 0)
            {
                saidNoAddon = true;
                Log.Write(Tr.L("Папка аддона Interface\\AddOns\\MountSheetSync не найдена — список «Осталось» записать некуда. Установите аддон (шаг 1)."));
            }
            if (anyAddon)
            {
                saidNoAddon = false;
            }
        }

        void Rescan(DateTime now)
        {
            List<string> foundDirs = WowLocator.FindRetailDirs(engine.Settings.WowPath);
            foreach (string d in foundDirs)
            {
                if (!dirs.Contains(d))
                {
                    Log.Write(Tr.L("Папка игры: {0}", d));
                }
            }
            dirs = foundDirs;
            if (dirs.Count == 0)
            {
                files = new List<string>();
                nextRescan = now.AddSeconds(30);
                if (!saidNoGame)
                {
                    saidNoGame = true;
                    Log.Write(Tr.L("Не нашёл папку World of Warcraft. Укажите её в настройках (ПКМ по значку → «Настройки…»)."));
                    Send(Tr._("Не найдена папка World of Warcraft"), Tr._("Укажите её: ПКМ по значку → «Настройки…»."), true, false);
                }
                return;
            }
            saidNoGame = false;
            WriteSheetFiles();   // аддон могли поставить или переустановить с пустым списком
            List<string> foundFiles = WowLocator.GetAddonFiles(dirs);
            foreach (string f in foundFiles)
            {
                if (!files.Contains(f))
                {
                    Log.Write(Tr.L("Слежу за файлом: {0}", f));
                }
            }
            files = foundFiles;
            WatchFiles(files);
            if (files.Count == 0)
            {
                nextRescan = now.AddSeconds(30);
                if (!saidNoFiles)
                {
                    saidNoFiles = true;
                    if (!AddonInstalled())
                    {
                        // первый запуск на чужом компьютере: программа есть, аддона нет — без него ждать нечего
                        Log.Write(Tr.L("Аддон Mount Sheet Sync не установлен: нет папки {0}.", SheetData.AddonDir(dirs[0])));
                        Send(Tr._("Аддон Mount Sheet Sync не установлен"), NoAddonText(), true, false);
                    }
                    else
                    {
                        Log.Write(Tr.L("Файл аддона ещё не создан: зайдите в игру с включённым аддоном и выйдите на выбор персонажа (или наберите /reload)."));
                    }
                }
            }
            else
            {
                nextRescan = now.AddMinutes(10);   // новые аккаунты появляются редко
            }
        }

        void NoteAttempt()
        {
            lastAttempt = DateTime.Now;
            lastAttemptFiles = FilesSignature();
        }

        string FilesSignature()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string f in files)
            {
                sb.Append(f).Append('=').Append(Stamp(f) ?? "-").Append(';');
            }
            return sb.ToString();
        }

        static string Stamp(string file)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists)
                {
                    return null;
                }
                return info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "/"
                    + info.Length.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
