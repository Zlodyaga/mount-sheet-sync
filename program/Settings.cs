using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MountSheetSync
{
    /// <summary>Настройки: %APPDATA%\MountSheetSync\settings.json.</summary>
    sealed class Settings
    {
        /// <summary>Должен совпадать с SECRET в Code.gs.</summary>
        public const string DefaultSecret = "snel1Y-KWGZbBii0vKrBNGfT";

        public const string ModeGoogle = "google";
        public const string ModeScript = "script";

        /// <summary>«google» — таблица, созданная программой (вход через Google); «script» — своя таблица со скриптом Code.gs.</summary>
        public string Mode = ModeGoogle;
        public string SpreadsheetId = "";
        public string SpreadsheetTitle = "";
        public string SpreadsheetUrl = "";
        public string WebAppUrl = "";
        public string Secret = DefaultSecret;
        public string WowPath = "";
        public bool Notifications = true;
        /// <summary>Язык программы: "auto" (как в Windows) или "en", "ru", "de", "fr", "es", "pt".</summary>
        public string Language = "auto";
        public int PollSeconds = 5;
        public int RetrySeconds = 60;

        static readonly Regex UrlPattern = new Regex(
            @"^https://script\.google\.com/(a/macros/[^/\s]+|macros)/s/[^/\s]+/exec$", RegexOptions.CultureInvariant);

        public bool UseGoogle
        {
            get { return Mode != ModeScript; }
        }

        public bool IsConfigured
        {
            get
            {
                if (UseGoogle)
                {
                    return !string.IsNullOrWhiteSpace(SpreadsheetId) && TokenStore.Exists;
                }
                return !string.IsNullOrWhiteSpace(WebAppUrl) && !string.IsNullOrWhiteSpace(Secret);
            }
        }

        /// <summary>Чего не хватает для работы — для меню и подсказок.</summary>
        public string MissingText
        {
            get
            {
                if (UseGoogle)
                {
                    if (!TokenStore.Exists)
                    {
                        return Tr._("Не выполнен вход в Google");
                    }
                    return Tr._("Таблица ещё не создана");
                }
                return Tr._("Не указан адрес таблицы");
            }
        }

        /// <summary>Куда отправлять записи.</summary>
        public ISheetService CreateService()
        {
            if (UseGoogle)
            {
                return new GoogleSheetService(SpreadsheetId);
            }
            return new ScriptSheetService(WebAppUrl);
        }

        public static bool IsWebAppUrl(string url)
        {
            return url != null && UrlPattern.IsMatch(url);
        }

        /// <summary>Адрес без пробелов и кавычек, которые прихватываются при копировании.</summary>
        public static string CleanUrl(string text)
        {
            return (text ?? "").Trim().Trim('"', '\'', '<', '>', ' ', '\t', '\r', '\n');
        }

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        public static Settings Load(string file)
        {
            Settings s = new Settings();
            if (!File.Exists(file))
            {
                return s;
            }
            try
            {
                Dictionary<string, object> d = Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null)
                {
                    return s;
                }
                s.WebAppUrl = J.Str(d, "webAppUrl").Trim();
                string mode = J.Str(d, "mode").Trim().ToLowerInvariant();
                if (mode == ModeGoogle || mode == ModeScript)
                {
                    s.Mode = mode;
                }
                else
                {
                    // настройки 1.x: был адрес скрипта — значит, своя таблица со скриптом
                    s.Mode = s.WebAppUrl.Length > 0 ? ModeScript : ModeGoogle;
                }
                s.SpreadsheetId = J.Str(d, "spreadsheetId").Trim();
                s.SpreadsheetTitle = J.Str(d, "spreadsheetTitle").Trim();
                s.SpreadsheetUrl = J.Str(d, "spreadsheetUrl").Trim();
                string secret = J.Str(d, "secret").Trim();
                if (secret.Length > 0)
                {
                    s.Secret = secret;
                }
                s.WowPath = J.Str(d, "wowPath").Trim();
                s.Language = Tr.Normalize(J.Str(d, "language"));
                if (d.ContainsKey("notifications"))
                {
                    s.Notifications = J.Truthy(d, "notifications");
                }
                long n = J.Long(d, "pollSeconds");
                if (n >= 2 && n <= 3600)
                {
                    s.PollSeconds = (int)n;
                }
                n = J.Long(d, "retrySeconds");
                if (n >= 1 && n <= 3600)
                {
                    s.RetrySeconds = (int)n;
                }
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось прочитать settings.json ({0}) — беру значения по умолчанию.", ex.Message));
            }
            return s;
        }

        public void Save(string file)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["mode"] = Mode;
            d["spreadsheetId"] = SpreadsheetId;
            d["spreadsheetTitle"] = SpreadsheetTitle;
            d["spreadsheetUrl"] = SpreadsheetUrl;
            d["webAppUrl"] = WebAppUrl;
            d["secret"] = Secret;
            d["wowPath"] = WowPath;
            d["notifications"] = Notifications;
            d["language"] = Language;
            d["pollSeconds"] = PollSeconds;
            if (RetrySeconds != 60)
            {
                d["retrySeconds"] = RetrySeconds;
            }
            FileUtil.WriteAtomic(file, Json.Write(d, true) + "\r\n");
        }
    }

    /// <summary>Поиск папки игры и файлов сохранений аддона.</summary>
    static class WowLocator
    {
        public const string AddonFile = "MountSheetSync.lua";

        static readonly string[] CommonFolders =
        {
            "World of Warcraft",
            @"Games\World of Warcraft",
            @"Program Files (x86)\World of Warcraft",
            @"Program Files\World of Warcraft",
            @"Blizzard\World of Warcraft",
            @"Battle.net\World of Warcraft",
            @"Blizzard Games\World of Warcraft",
            @"Games\Blizzard\World of Warcraft",
        };

        /// <summary>
        /// Папка _retail_ (в ней WTF\Account) по пути, который указал человек:
        /// подойдёт и «World of Warcraft», и «_retail_», и «_retail_\WTF», и путь к Wow.exe.
        /// </summary>
        public static string ResolveRetailDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            try
            {
                string p = path.Trim().Trim('"').TrimEnd('\\', '/');
                if (p.Length == 0)
                {
                    return null;
                }
                if (File.Exists(p))
                {
                    p = Path.GetDirectoryName(p);
                }
                List<string> candidates = new List<string>();
                if (string.Equals(Path.GetFileName(p), "WTF", StringComparison.OrdinalIgnoreCase))
                {
                    string parent = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        candidates.Add(parent);
                    }
                }
                candidates.Add(p);
                candidates.Add(Path.Combine(p, "_retail_"));
                foreach (string c in candidates)
                {
                    if (Directory.Exists(Path.Combine(Path.Combine(c, "WTF"), "Account")))
                    {
                        return Path.GetFullPath(c);
                    }
                }
            }
            catch (Exception)
            {
                // недопустимые символы в пути и т. п.
            }
            return null;
        }

        public static List<string> FindRetailDirs(string configured)
        {
            List<string> found = new List<string>();
            Action<string> add = delegate(string p)
            {
                string r = ResolveRetailDir(p);
                if (r != null && !found.Exists(x => string.Equals(x, r, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(r);
                }
            };
            add(configured);
            if (found.Count > 0)
            {
                return found;
            }
            add(ReadMachineValue(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft", "InstallPath"));
            add(ReadMachineValue(@"SOFTWARE\Blizzard Entertainment\World of Warcraft", "InstallPath"));
            add(ReadMachineValue(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft", "InstallLocation"));
            add(ReadMachineValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft", "InstallLocation"));
            foreach (string name in new[] { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432" })
            {
                string basePath = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(basePath))
                {
                    add(Path.Combine(basePath, "World of Warcraft"));
                }
            }
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                DriveInfo[] drives;
                try
                {
                    drives = DriveInfo.GetDrives();
                }
                catch (Exception)
                {
                    drives = new DriveInfo[0];
                }
                foreach (DriveInfo drive in drives)
                {
                    bool usable;
                    try
                    {
                        // только обычные диски: без съёмных, сетевых и пустых дисководов
                        usable = drive.DriveType == DriveType.Fixed && drive.IsReady;
                    }
                    catch (Exception)
                    {
                        usable = false;
                    }
                    if (!usable)
                    {
                        continue;
                    }
                    foreach (string sub in CommonFolders)
                    {
                        add(Path.Combine(drive.RootDirectory.FullName, sub));
                    }
                }
            }
            return found;
        }

        static string ReadMachineValue(string key, string name)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                {
                    return k == null ? null : k.GetValue(name) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Файлы сохранений аддона во всех аккаунтах (WTF\Account\*\SavedVariables).</summary>
        public static List<string> GetAddonFiles(IEnumerable<string> dirs)
        {
            List<string> files = new List<string>();
            foreach (string d in dirs)
            {
                string[] accounts;
                try
                {
                    accounts = Directory.GetDirectories(Path.Combine(Path.Combine(d, "WTF"), "Account"));
                }
                catch (Exception)
                {
                    continue;
                }
                Array.Sort(accounts, StringComparer.OrdinalIgnoreCase);
                foreach (string account in accounts)
                {
                    string f = Path.Combine(Path.Combine(account, "SavedVariables"), AddonFile);
                    if (File.Exists(f))
                    {
                        files.Add(f);
                    }
                }
            }
            return files;
        }
    }

    /// <summary>Чтение записей из файла сохранений аддона.</summary>
    static class SavedVariables
    {
        // Каждая запись — плоская таблица Lua: { ["id"] = "...", ["boss"] = "...", ["time"] = 123, ... }
        static readonly Regex RecordPattern = new Regex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);
        static readonly Regex PairPattern = new Regex(
            @"\[""([^""\\]+)""\]\s*=\s*(""(?:[^""\\]|\\.)*""|-?[0-9]+(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?|true|false)",
            RegexOptions.CultureInvariant);

        public static string ReadShared(string path)
        {
            // Игра может держать файл открытым — читаем с разрешением совместного доступа.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        public static List<Dictionary<string, object>> ParseRecords(string text)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (Match m in RecordPattern.Matches(text))
            {
                Dictionary<string, object> rec = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (Match p in PairPattern.Matches(m.Groups[1].Value))
                {
                    string raw = p.Groups[2].Value;
                    object value;
                    if (raw.StartsWith("\"", StringComparison.Ordinal))
                    {
                        value = Unescape(raw.Substring(1, raw.Length - 2));
                    }
                    else if (raw == "true")
                    {
                        value = true;
                    }
                    else if (raw == "false")
                    {
                        value = false;
                    }
                    else
                    {
                        double num = double.Parse(raw, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture);
                        if (Math.Floor(num) == num && Math.Abs(num) < 1e15)
                        {
                            value = (long)num;
                        }
                        else
                        {
                            value = num;
                        }
                    }
                    rec[p.Groups[1].Value] = value;
                }
                if (rec.ContainsKey("id") && rec.ContainsKey("boss") && rec.ContainsKey("char"))
                {
                    list.Add(rec);
                }
            }
            return list;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0)
            {
                return s;
            }
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                char n = s[i + 1];
                if (n == 'n')
                {
                    sb.Append('\n');
                    i += 2;
                }
                else if (n == 't')
                {
                    sb.Append('\t');
                    i += 2;
                }
                else if (n == 'r')
                {
                    sb.Append('\r');
                    i += 2;
                }
                else if (n >= '0' && n <= '9')
                {
                    int j = i + 1;
                    int code = 0;
                    int digits = 0;
                    while (j < s.Length && digits < 3 && s[j] >= '0' && s[j] <= '9')
                    {
                        code = code * 10 + (s[j] - '0');
                        j++;
                        digits++;
                    }
                    sb.Append((char)code);
                    i = j;
                }
                else
                {
                    sb.Append(n);
                    i += 2;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Уже отправленные записи: sent-ids.txt (id и время отправки), хранятся 60 дней.</summary>
    sealed class SentLedger
    {
        readonly string path;
        readonly Dictionary<string, long> ids = new Dictionary<string, long>(StringComparer.Ordinal);

        public SentLedger(string path)
        {
            this.path = path;
            Load();
        }

        public bool Contains(string id)
        {
            return ids.ContainsKey(id);
        }

        public void Mark(string id, long when)
        {
            ids[id] = when;
        }

        /// <summary>Забыть всё отправленное — для новой таблицы.</summary>
        public void Clear()
        {
            ids.Clear();
        }

        void Load()
        {
            if (path == null || !File.Exists(path))
            {
                return;
            }
            long cutoff = Epoch.Now - 60L * 86400;   // аддон хранит записи 3 недели, 60 дней хватит
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                long t;
                if (parts.Length >= 2 && parts[0].Length > 0
                    && long.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out t)
                    && t >= cutoff)
                {
                    ids[parts[0]] = t;
                }
            }
        }

        public void Save()
        {
            if (path == null)
            {
                return;
            }
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, long> kv in ids)
            {
                sb.Append(kv.Key).Append('\t')
                  .Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
            }
            FileUtil.WriteAtomic(path, sb.ToString());
        }
    }
}
