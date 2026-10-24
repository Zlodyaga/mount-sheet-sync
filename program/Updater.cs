// Обновление программы (и аддона, если его ставили вручную из архива) с GitHub Releases.
//
// Проверка: api.github.com/…/releases/latest — номер последнего выпуска и архив MountSheetSync-<версия>.zip.
// Установка — только по кнопке: архив скачивается, его SHA-256 сверяется с контрольной суммой, которую
// GitHub сам считает для каждого файла выпуска (без неё ничего не ставится), из архива берётся
// 2-program\MountSheetSync.exe. Работающий exe Windows не даёт удалить, но даёт переименовать: старый
// становится MountSheetSync.exe.old, новый встаёт на его место, программа перезапускается, и при следующем
// запуске .old удаляется. Если exe подписан, новый должен быть подписан тем же издателем.
//
// Аддон: в архиве выпуска в папке аддона лежит файл-метка (MarkerFile). Есть метка — аддон ставили из архива,
// и программа обновляет его сама (SheetData.lua сохраняется). Нет метки — аддон из CurseForge (или старый
// архив): его обновляет приложение CurseForge, программа его не трогает.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace MountSheetSync
{
    sealed class UpdateInfo
    {
        public string Version = "";
        public string Tag = "";
        public string Notes = "";
        public string PageUrl = "";
        public string AssetName = "";
        public string AssetUrl = "";
        /// <summary>SHA-256 архива (hex, нижний регистр) из выпуска GitHub; "" — GitHub его не дал.</summary>
        public string Sha256 = "";
    }

    sealed class UpdateResult
    {
        public bool ExeReplaced;
        public string ExePath = "";
        public List<string> AddonsUpdated = new List<string>();
        /// <summary>Аддоны без метки (CurseForge): их программа не трогает.</summary>
        public List<string> AddonsLeft = new List<string>();
    }

    static class Updater
    {
        public const string Repo = "Zlodyaga/mount-sheet-sync";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases";
        /// <summary>Файл в папке аддона из архива выпуска: «этот аддон обновляет программа».</summary>
        public const string MarkerFile = "installed-from-archive.txt";

        static string ApiUrl
        {
            get
            {
#if TESTS
                string test = Environment.GetEnvironmentVariable("MSS_UPDATE_TEST");
                if (!string.IsNullOrEmpty(test))
                {
                    return test.TrimEnd('/') + "/repos/" + Repo + "/releases/latest";
                }
#endif
                return "https://api.github.com/repos/" + Repo + "/releases/latest";
            }
        }

        // ------------------------------------------------------------------
        // Проверка
        // ------------------------------------------------------------------

        /// <summary>Новый выпуск на GitHub или null (версия та же или старше). Ошибка сети — SyncException.</summary>
        public static UpdateInfo Check()
        {
            Dictionary<string, object> rel;
            try
            {
                rel = Json.Parse(Encoding.UTF8.GetString(Download(ApiUrl, "application/vnd.github+json"))) as Dictionary<string, object>;
            }
            catch (WebException ex)
            {
                throw new SyncException(Tr.F("нет связи с GitHub: {0}", ex.Message));
            }
            catch (FormatException ex)
            {
                throw new SyncException(Tr.F("GitHub ответил непонятно: {0}", ex.Message));
            }
            if (rel == null || J.Truthy(rel, "draft") || J.Truthy(rel, "prerelease"))
            {
                return null;
            }
            UpdateInfo info = new UpdateInfo();
            info.Tag = J.Str(rel, "tag_name").Trim();
            info.Version = info.Tag.TrimStart('v', 'V');
            info.Notes = J.Str(rel, "body").Trim();
            info.PageUrl = J.Str(rel, "html_url");
            if (info.PageUrl.Length == 0)
            {
                info.PageUrl = ReleasesPage;
            }
            if (!IsNewer(info.Version, Program.Version))
            {
                return null;
            }
            Regex full = new Regex(@"^MountSheetSync-\d+(\.\d+)*\.zip$", RegexOptions.IgnoreCase);
            foreach (object o in J.Arr(J.Get(rel, "assets")))
            {
                Dictionary<string, object> a = J.Obj(o);
                string name = J.Str(a, "name");
                if (!full.IsMatch(name))
                {
                    continue;
                }
                info.AssetName = name;
                info.AssetUrl = J.Str(a, "browser_download_url");
                string digest = J.Str(a, "digest").Trim();
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    info.Sha256 = digest.Substring(7).Trim().ToLowerInvariant();
                }
                break;
            }
            return info;
        }

        /// <summary>«2.10.0» новее «2.9.3»; «2.4.0» не новее «2.4».</summary>
        public static bool IsNewer(string candidate, string current)
        {
            int[] a = Parts(candidate);
            int[] b = Parts(current);
            if (a.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y)
                {
                    return x > y;
                }
            }
            return false;
        }

        static int[] Parts(string v)
        {
            Match m = Regex.Match(v ?? "", @"^\s*v?(\d+(?:\.\d+)*)");
            if (!m.Success)
            {
                return new int[0];
            }
            return m.Groups[1].Value.Split('.').Select(p =>
            {
                int n;
                return int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
            }).ToArray();
        }

        static byte[] Download(string url, string accept)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Program.Title.Replace(" ", "") + "/" + Program.Version;
            req.Accept = accept;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            req.AllowAutoRedirect = true;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------
        // Установка
        // ------------------------------------------------------------------

        public static string ExePath
        {
            get
            {
                Assembly entry = Assembly.GetEntryAssembly();
                return Path.GetFullPath(entry != null ? entry.Location : Process.GetCurrentProcess().MainModule.FileName);
            }
        }

        /// <summary>Скачать, проверить и поставить. Ошибка — SyncException (ничего не изменено).</summary>
        public static UpdateResult Install(UpdateInfo info, IList<string> retailDirs, Action<string> progress)
        {
            if (info.AssetUrl.Length == 0)
            {
                throw new SyncException(Tr.F("в выпуске {0} нет архива программы", info.Tag));
            }
            if (info.Sha256.Length != 64)
            {
                throw new SyncException(Tr._("GitHub не дал контрольную сумму архива — обновите программу вручную"));
            }
            progress(Tr.F("Скачиваю {0}…", info.AssetName));
            byte[] zip;
            try
            {
                zip = Download(info.AssetUrl, "application/octet-stream");
            }
            catch (WebException ex)
            {
                throw new SyncException(Tr.F("не удалось скачать обновление: {0}", ex.Message));
            }
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = string.Concat(sha.ComputeHash(zip).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)).ToArray());
            }
            if (hash != info.Sha256)
            {
                throw new SyncException(Tr._("контрольная сумма скачанного архива не совпала — обновление не установлено"));
            }
            byte[] exe;
            Dictionary<string, byte[]> addon;
            ReadArchive(zip, out exe, out addon);

            string exePath = ExePath;
            string tmpExe = exePath + ".new";
            File.WriteAllBytes(tmpExe, exe);
            try
            {
                CheckSigner(exePath, tmpExe);
            }
            catch
            {
                TryDelete(tmpExe);
                throw;
            }

            UpdateResult result = new UpdateResult();
            // сначала аддоны: если с exe что-то пойдёт не так, аддон новой версии со старой программой работает
            foreach (string dir in retailDirs ?? new string[0])
            {
                string addonDir = SheetData.AddonDir(dir);
                if (!Directory.Exists(addonDir))
                {
                    continue;
                }
                if (!File.Exists(Path.Combine(addonDir, MarkerFile)))
                {
                    result.AddonsLeft.Add(addonDir);
                    continue;
                }
                progress(Tr._("Обновляю аддон…"));
                ReplaceAddon(addonDir, addon);
                result.AddonsUpdated.Add(addonDir);
            }

            progress(Tr._("Ставлю новую версию программы…"));
            string oldExe = exePath + ".old";
            TryDelete(oldExe);
            try
            {
                File.Move(exePath, oldExe);
            }
            catch (Exception ex)
            {
                TryDelete(tmpExe);
                throw new SyncException(Tr.F("не удалось заменить программу ({0}) — в папку программы нельзя писать? Скачайте новую версию вручную", ex.Message));
            }
            try
            {
                File.Move(tmpExe, exePath);
            }
            catch (Exception ex)
            {
                try
                {
                    File.Move(oldExe, exePath);   // вернуть как было
                }
                catch (Exception)
                {
                }
                TryDelete(tmpExe);
                throw new SyncException(Tr.F("не удалось заменить программу: {0}", ex.Message));
            }
            result.ExeReplaced = true;
            result.ExePath = exePath;
            return result;
        }

        /// <summary>Из архива выпуска: 2-program/MountSheetSync.exe и файлы 1-addon/MountSheetSync/.</summary>
        static void ReadArchive(byte[] zip, out byte[] exe, out Dictionary<string, byte[]> addon)
        {
            exe = null;
            addon = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ZipArchive z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        int slash = name.IndexOf('/');
                        string inner = slash >= 0 ? name.Substring(slash + 1) : name;
                        if (inner == "2-program/MountSheetSync.exe")
                        {
                            exe = ReadEntry(e);
                        }
                        else if (inner.StartsWith("1-addon/MountSheetSync/", StringComparison.Ordinal) && !inner.EndsWith("/", StringComparison.Ordinal))
                        {
                            string rel = inner.Substring("1-addon/MountSheetSync/".Length);
                            if (rel.Split('/').Any(p => p == ".." || p.Length == 0) || Path.IsPathRooted(rel))
                            {
                                throw new SyncException(Tr._("в архиве обновления странные пути — обновление не установлено"));
                            }
                            addon[rel] = ReadEntry(e);
                        }
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                throw new SyncException(Tr.F("архив обновления повреждён: {0}", ex.Message));
            }
            if (exe == null || exe.Length < 1024 || exe[0] != 'M' || exe[1] != 'Z')
            {
                throw new SyncException(Tr._("в архиве обновления нет программы"));
            }
            if (!addon.ContainsKey("MountSheetSync.toc"))
            {
                throw new SyncException(Tr._("в архиве обновления нет аддона"));
            }
        }

        static byte[] ReadEntry(ZipArchiveEntry e)
        {
            using (Stream s = e.Open())
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>Подписанную программу заменяет только программа того же издателя.</summary>
        static void CheckSigner(string current, string candidate)
        {
            string was = Signer(current);
            if (was == null)
            {
                return;
            }
            string now = Signer(candidate);
            if (now != was)
            {
                throw new SyncException(Tr.F("новая версия подписана не тем издателем ({0}) — обновление не установлено", now ?? Tr._("без подписи")));
            }
        }

        static string Signer(string file)
        {
            try
            {
                return X509Certificate.CreateFromSignedFile(file).Subject;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Аддон из архива: новые файлы собираются рядом (Interface\MountSheetSync.update), потом папки меняются
        /// местами. SheetData.lua (список «Осталось», его пишет программа) переносится в новую папку.
        /// </summary>
        static void ReplaceAddon(string addonDir, Dictionary<string, byte[]> files)
        {
            string iface = Path.GetDirectoryName(Path.GetDirectoryName(addonDir));
            string staging = Path.Combine(iface, "MountSheetSync.update");
            string previous = Path.Combine(iface, "MountSheetSync.previous");
            DeleteDir(staging);
            DeleteDir(previous);
            try
            {
                foreach (KeyValuePair<string, byte[]> kv in files)
                {
                    string path = Path.Combine(staging, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, kv.Value);
                }
                string sheet = Path.Combine(addonDir, SheetData.FileName);
                if (File.Exists(sheet))
                {
                    File.Copy(sheet, Path.Combine(staging, SheetData.FileName), true);
                }
                File.WriteAllText(Path.Combine(staging, MarkerFile), MarkerText(), FileUtil.Utf8NoBom);
                Directory.Move(addonDir, previous);
                try
                {
                    Directory.Move(staging, addonDir);
                }
                catch
                {
                    Directory.Move(previous, addonDir);
                    throw;
                }
            }
            catch (Exception ex)
            {
                DeleteDir(staging);
                if (ex is SyncException)
                {
                    throw;
                }
                throw new SyncException(Tr.F("не удалось обновить аддон ({0}): {1}", addonDir, ex.Message));
            }
            DeleteDir(previous);
        }

        /// <summary>Текст файла-метки (тот же кладёт в архив tools/package.sh).</summary>
        public static string MarkerText()
        {
            return "This copy of the addon was installed from the Mount Sheet Sync release archive.\r\n"
                + "The Mount Sheet Sync program updates it together with itself. Delete this file to stop that\r\n"
                + "(for example, if you switch to the CurseForge app).\r\n";
        }

        /// <summary>После обновления: старый exe больше не нужен.</summary>
        public static void CleanupOld()
        {
            try
            {
                TryDelete(ExePath + ".old");
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Запустить новую версию; она подождёт, пока этот процесс выйдет.</summary>
        public static void Restart(string oldVersion)
        {
            List<string> args = new List<string> { "--wait-pid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
                "--updated-from", oldVersion };
            if (AppPaths.CustomDataDir != null)
            {
                args.Add("--data");
                args.Add(AppPaths.CustomDataDir);
            }
            ProcessStartInfo psi = new ProcessStartInfo(ExePath, string.Join(" ", args.Select(Quote).ToArray()));
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(ExePath);
            Process.Start(psi);
        }

        static string Quote(string a)
        {
            return a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Подождать выхода прежнего процесса (после обновления), не дольше 30 секунд.</summary>
        public static void WaitFor(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.WaitForExit(30000);
                }
            }
            catch (Exception)
            {
                // уже вышел
            }
        }

        static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
            }
        }

        static void DeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Первые строки заметок к выпуску для окна «Обновить?».</summary>
        public static string ShortNotes(string notes)
        {
            List<string> lines = (notes ?? "").Replace("\r", "").Split('\n').Select(l => l.TrimEnd())
                .Where(l => l.Trim().Length > 0).ToList();
            string text = string.Join("\n", lines.Take(8).ToArray());
            return TextUtil.Shorten(text, 600);
        }
    }
}
