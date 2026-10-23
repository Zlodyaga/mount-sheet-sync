// Mount Sheet Sync — программа в трее Windows: следит за файлом сохранений аддона
// MountSheetSync и отправляет убитых боссов в Google-таблицу маунтов. С версии 2.0 программа
// сама создаёт таблицу и ведёт её через вход Google; таблица со скриптом Code.gs тоже работает.
//
// Сборка: build.cmd рядом с исходниками (компилятор C# уже есть в Windows, в .NET Framework 4.8).
//
// Параметры запуска (обычно не нужны):
//   --autostart   так программу запускает Windows (без окна настроек, с паузой на старте);
//   --once        один проход синхронизации и выход;
//   --test        только проверить связь с таблицей;
//   --google-login  войти через Google без окна настроек;
//   --create-sheet [название]  создать таблицу для всех персонажей из аддона;
//   --add-chars   добавить в таблицу персонажей, которых в ней ещё нет;
//   --sync-collection  сверить таблицу с коллекцией маунтов из игры;
//   --set-chars <файл.json>  персонажи таблицы как в файле: [{"name":"Эйра","raids":true,"dungeons":false,"world":true}, …];
//   --data <папка>  хранить настройки и журнал в другой папке (вместо %APPDATA%\MountSheetSync).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MountSheetSync
{
    static class Program
    {
        public const string Version = "2.3.1";
        public const string Title = "Mount Sheet Sync";

        [STAThread]
        static int Main(string[] args)
        {
            Options opts = Options.Parse(args);
            try
            {
                AppPaths.Init(opts.DataDir);
            }
            catch (Exception ex)
            {
                Tr.Use("auto");
                MessageBox.Show(Tr.F("Не удалось создать папку для настроек: {0}", ex.Message), Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            bool console = opts.Once || opts.Test || opts.GoogleLogin || opts.CreateSheet || opts.AddChars || opts.SyncCollection
                || opts.SetCharsFile != null;
            Tr.Use(Settings.Load(AppPaths.SettingsFile).Language);
            Log.Init(AppPaths.LogFile, console);
            ConfigureNetwork();
            GoogleConfig.ApplyTestOverride();

            if (opts.Test)
            {
                return RunTest();
            }
            if (opts.GoogleLogin)
            {
                return RunGoogleLogin();
            }
            if (opts.CreateSheet)
            {
                return RunCreateSheet(opts.SheetTitle);
            }
            if (opts.AddChars)
            {
                return RunAddChars();
            }
            if (opts.SyncCollection)
            {
                return RunSyncCollection();
            }
            if (opts.SetCharsFile != null)
            {
                return RunSetChars(opts.SetCharsFile);
            }

            // Отдельные папки данных (для проверок) — отдельные экземпляры.
            string suffix = opts.DataDir == null ? "" : "." + ShortHash(AppPaths.DataDir);
            using (Mutex mutex = new Mutex(false, "Local\\MountSheetSync.WoW" + suffix))
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }
                if (!owned)
                {
                    if (opts.Once)
                    {
                        Log.Write(Tr.L("Синхронизация уже запущена (значок в трее) — второй экземпляр не нужен."));
                        return 2;
                    }
                    SingleInstance.SignalFirst(suffix);
                    return 2;
                }
                try
                {
                    return opts.Once ? RunOnce() : RunTray(opts.Autostart, suffix);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        static int RunTray(bool autostart, string suffix)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            DateTime lastShown = DateTime.MinValue;
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Write(Tr.L("Ошибка: {0}", e.Exception));
                // кнопка, которая молча ничего не делает, хуже сообщения; но не чаще раза в полминуты
                if ((DateTime.Now - lastShown).TotalSeconds > 30)
                {
                    lastShown = DateTime.Now;
                    MessageBox.Show(Tr.F("Что-то пошло не так: {0}\n\nПодробности — в журнале работы (меню значка → «Журнал работы»). Программа продолжает работать.",
                        e.Exception.Message), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Log.Write(Tr.L("Необработанная ошибка: {0}", e.ExceptionObject));
            };
            using (TrayApp app = new TrayApp(autostart, suffix))
            {
                Application.Run(app);
            }
            return 0;
        }

        static int RunOnce()
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            if (!settings.IsConfigured)
            {
                Log.Write(Tr.L("{0}: запустите программу без параметров и заполните настройки.", settings.MissingText));
                return 1;
            }
            SyncWorker worker = new SyncWorker(AppPaths.StateFile);
            worker.RunOnce(settings);
            return 0;
        }

        static int RunTest()
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            if (!settings.IsConfigured)
            {
                Log.Write(Tr.L("{0}: запустите программу без параметров и заполните настройки.", settings.MissingText));
                return 1;
            }
            SentLedger sent = new SentLedger(AppPaths.SentFileFor(settings));
            SyncEngine engine = new SyncEngine(settings, sent);
            var dirs = WowLocator.FindRetailDirs(settings.WowPath);
            foreach (string d in dirs)
            {
                Log.Write(Tr.L("Папка игры: {0}", d));
            }
            var files = WowLocator.GetAddonFiles(dirs);
            try
            {
                engine.Ping(files);
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Проверка связи не прошла: {0}", ex.Message));
                return 1;
            }
            if (dirs.Count == 0)
            {
                Log.Write(Tr.L("Не нашёл папку World of Warcraft — укажите её в настройках."));
            }
            else if (files.Count == 0)
            {
                Log.Write(Tr.L("Файл аддона пока не найден (он появится после первого выхода из игры с аддоном)."));
            }
            foreach (string f in files)
            {
                var records = engine.ReadRecords(f);
                int pending = 0;
                foreach (var r in records)
                {
                    if (!engine.IsSent(r))
                    {
                        pending++;
                    }
                }
                Log.Write(Tr.L("{0}: записей {1}, ещё не отправлено {2}", f, records.Count, pending));
            }
            return 0;
        }

        static int RunGoogleLogin()
        {
            try
            {
                GoogleToken token = GoogleAuth.SignIn(Browser.Open, CancellationToken.None, delegate(string text) { Log.Write(Tr.L("Вход в Google: {0}", text)); });
                GoogleSession.Set(token);
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Вход в Google не выполнен: {0}", ex.Message));
                return 1;
            }
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            settings.Mode = Settings.ModeGoogle;
            settings.Save(AppPaths.SettingsFile);
            return 0;
        }

        /// <summary>Персонажи и коллекция из файлов аддона всех WoW-аккаунтов.</summary>
        public static GameSnapshot ReadGame(Settings settings)
        {
            return GameData.Read(WowLocator.GetAddonFiles(WowLocator.FindRetailDirs(settings.WowPath)));
        }

        static int RunCreateSheet(string title)
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            GameSnapshot game = ReadGame(settings);
            if (game.Characters.Count == 0)
            {
                Log.Write(Tr.L("Аддон пока не знает ни одного персонажа: зайдите в игру с аддоном Mount Sheet Sync и выйдите (или /reload)."));
                return 1;
            }
            BuildPlan plan = new BuildPlan();
            if (!string.IsNullOrWhiteSpace(title))
            {
                plan.Title = title.Trim();
            }
            foreach (GameCharacter c in game.Characters)
            {
                plan.Characters.Add(new BuildCharacter(c.Name, true, true));
            }
            plan.CollectedMounts = game.CollectedMounts;
            plan.CollectedIds = game.CollectedIds;
            plan.CollectionStamp = game.CollectionUpdated;
            try
            {
                CreatedSheet sheet = SheetCreator.Create(plan, Log.Write);
                UseNewSheet(settings, sheet);
                Log.Write(Tr.L("Готово: {0}", sheet.Url));
                return 0;
            }
            catch (Exception ex)
            {
                if (!(ex is SyncException) && !(ex is ArgumentException))
                {
                    throw;
                }
                Log.Write(Tr.L("Таблица не создана: {0}", ex.Message));
                return 1;
            }
        }

        /// <summary>Новая таблица — основная (у неё свой список отправленного, так что журнал аддона уйдёт в неё весь).</summary>
        public static void UseNewSheet(Settings settings, CreatedSheet sheet)
        {
            settings.Mode = Settings.ModeGoogle;
            settings.SpreadsheetId = sheet.Id;
            settings.SpreadsheetTitle = sheet.Title;
            settings.SpreadsheetUrl = sheet.Url;
            settings.Save(AppPaths.SettingsFile);
        }

        static int RunAddChars()
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            if (!settings.UseGoogle || settings.SpreadsheetId.Length == 0)
            {
                Log.Write(Tr.L("Таблица, созданная программой, не выбрана — сначала --create-sheet."));
                return 1;
            }
            GameSnapshot game = ReadGame(settings);
            List<BuildCharacter> chars = new List<BuildCharacter>();
            foreach (GameCharacter c in game.Characters)
            {
                chars.Add(new BuildCharacter(c.Name, true, true));
            }
            try
            {
                int n = SheetCreator.AddCharacters(settings.SpreadsheetId, chars, Log.Write);
                Log.Write(n == 0 ? Tr.L("Все персонажи уже есть в таблице.") : Tr.L("Добавлено листов: {0}.", n));
                return 0;
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Персонажи не добавлены: {0}", ex.Message));
                return 1;
            }
        }

        static int RunSetChars(string file)
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            if (!settings.UseGoogle || settings.SpreadsheetId.Length == 0)
            {
                Log.Write(Tr.L("Таблица, созданная программой, не выбрана — сначала --create-sheet."));
                return 1;
            }
            List<BuildCharacter> chars = new List<BuildCharacter>();
            try
            {
                foreach (object o in J.Arr(Json.Parse(File.ReadAllText(file, Encoding.UTF8))))
                {
                    Dictionary<string, object> c = J.Obj(o);
                    chars.Add(new BuildCharacter(J.Str(c, "name"), J.Truthy(c, "raids"), J.Truthy(c, "dungeons"), J.Truthy(c, "world")));
                }
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не читается {0}: {1}", file, ex.Message));
                return 1;
            }
            try
            {
                CharacterChanges ch = SheetCreator.UpdateCharacters(settings.SpreadsheetId, chars, Log.Write);
                Log.Write(Tr.L("Персонажи: {0}.", ch.Summary()));
                return 0;
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Персонажи не изменены: {0}", ex.Message));
                return 1;
            }
        }

        static int RunSyncCollection()
        {
            Settings settings = Settings.Load(AppPaths.SettingsFile);
            if (!settings.UseGoogle || settings.SpreadsheetId.Length == 0)
            {
                Log.Write(Tr.L("Таблица, созданная программой, не выбрана — сверять не с чем."));
                return 1;
            }
            GameSnapshot game = ReadGame(settings);
            if (game.CollectionUpdated <= 0)
            {
                Log.Write(Tr.L("Аддон ещё не прочитал коллекцию маунтов: зайдите в игру с аддоном и сделайте /reload."));
                return 1;
            }
            try
            {
                CollectionResult res = new GoogleSheetService(settings.SpreadsheetId).SyncCollection(game, true);
                Log.Write(Tr.L("Сверка с коллекцией: {0}.", res.Summary()));
                return 0;
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Сверить с коллекцией не получилось: {0}", ex.Message));
                return 1;
            }
        }

        static void ConfigureNetwork()
        {
            ServicePointManager.Expect100Continue = false;
            try
            {
                // В .NET Framework 4.8 по умолчанию протокол выбирает Windows (TLS 1.2/1.3).
                // Если кто-то задал версии явно — добавляем TLS 1.2. Он записан числом (3072),
                // чтобы исходники собирались и со старыми справочными сборками .NET 4.0.
                const SecurityProtocolType Tls12 = (SecurityProtocolType)3072;
                SecurityProtocolType current = ServicePointManager.SecurityProtocol;
                if (current != 0 && (current & Tls12) == 0)
                {
                    ServicePointManager.SecurityProtocol = current | Tls12;
                }
            }
            catch (NotSupportedException)
            {
            }
            try
            {
                IWebProxy proxy = WebRequest.DefaultWebProxy;
                if (proxy != null)
                {
                    proxy.Credentials = CredentialCache.DefaultCredentials;
                }
            }
            catch (Exception)
            {
            }
        }

        static string ShortHash(string text)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
                return BitConverter.ToString(hash, 0, 4).Replace("-", "");
            }
        }

        /// <summary>Запущено прямо из архива (Windows распаковала его во временную папку).</summary>
        public static bool RunningFromTemp
        {
            get
            {
                try
                {
                    string exe = Path.GetFullPath(Application.ExecutablePath);
                    string temp = Path.GetFullPath(Path.GetTempPath());
                    return exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }

    sealed class Options
    {
        public bool Once;
        public bool Test;
        public bool Autostart;
        public bool GoogleLogin;
        public bool CreateSheet;
        public bool AddChars;
        public bool SyncCollection;
        public string SetCharsFile;
        public string SheetTitle;
        public string DataDir;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim().TrimStart('-', '/').ToLowerInvariant();
                if (a == "once")
                {
                    o.Once = true;
                }
                else if (a == "test")
                {
                    o.Test = true;
                }
                else if (a == "autostart")
                {
                    o.Autostart = true;
                }
                else if (a == "google-login")
                {
                    o.GoogleLogin = true;
                }
                else if (a == "create-sheet")
                {
                    o.CreateSheet = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        o.SheetTitle = args[++i];
                    }
                }
                else if (a == "add-chars")
                {
                    o.AddChars = true;
                }
                else if (a == "sync-collection")
                {
                    o.SyncCollection = true;
                }
                else if (a == "set-chars" && i + 1 < args.Length)
                {
                    o.SetCharsFile = Path.GetFullPath(args[++i]);
                }
                else if (a == "data" && i + 1 < args.Length)
                {
                    o.DataDir = Path.GetFullPath(args[++i]);
                }
            }
            return o;
        }
    }

    static class AppPaths
    {
        /// <summary>
        /// Что уже отправлено — отдельно для каждой таблицы: новая таблица получает всё из журнала аддона,
        /// а вернувшись к старой, программа не шлёт ей записи заново.
        /// </summary>
        public static string SentFileFor(Settings s)
        {
            if (s == null || !s.UseGoogle || string.IsNullOrEmpty(s.SpreadsheetId))
            {
                return SentFile;
            }
            StringBuilder safe = new StringBuilder();
            foreach (char c in s.SpreadsheetId)
            {
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return Path.Combine(DataDir, "sent-ids-google-" + safe + ".txt");
        }

        public static string DataDir;
        public static string SettingsFile;
        public static string SentFile;
        public static string StateFile;
        public static string LogFile;

        public static void Init(string overrideDir)
        {
            DataDir = overrideDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MountSheetSync");
            Directory.CreateDirectory(DataDir);
            SettingsFile = Path.Combine(DataDir, "settings.json");
            SentFile = Path.Combine(DataDir, "sent-ids.txt");
            StateFile = Path.Combine(DataDir, "state.json");
            LogFile = Path.Combine(DataDir, "sync.log");
        }
    }

    /// <summary>Второй запуск не создаёт второй значок, а просит первый показаться.</summary>
    static class SingleInstance
    {
        static EventWaitHandle showEvent;

        static string EventName(string suffix)
        {
            return "Local\\MountSheetSync.Show" + suffix;
        }

        public static void SignalFirst(string suffix)
        {
            try
            {
                using (EventWaitHandle e = EventWaitHandle.OpenExisting(EventName(suffix)))
                {
                    e.Set();
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Tr._("Mount Sheet Sync уже запущен — его значок в трее, возле часов."),
                    Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static void Listen(string suffix, Action onSignal)
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(suffix));
            }
            catch (Exception)
            {
                return;
            }
            Thread thread = new Thread(delegate()
            {
                while (true)
                {
                    showEvent.WaitOne();
                    onSignal();
                }
            });
            thread.IsBackground = true;
            thread.Name = "MountSheetSync show";
            thread.Start();
        }
    }
}
