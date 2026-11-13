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
    enum SyncState
    {
        Starting,
        NotConfigured,
        Checking,
        NoGame,
        NoFile,
        Ok,
        Error,
    }

    /// <summary>Строка «Последних событий» в меню.</summary>
    sealed class RecentEvent
    {
        public DateTime When;
        public string Text;

        public RecentEvent(DateTime when, string text)
        {
            When = when;
            Text = text;
        }
    }

    /// <summary>Состояние для значка и меню (копия, которую можно отдать интерфейсу).</summary>
    sealed class WorkerStatus
    {
        public SyncState State = SyncState.Starting;
        public string Message = "";
        public string Spreadsheet = "";
        public string SpreadsheetUrl = "";
        public DateTime? LastSend;
        public string LastResult = "";
        public DateTime? RetryAt;
        public int Files;
        public List<RecentEvent> Recent = new List<RecentEvent>();
        /// <summary>«Осталось на этой неделе» по персонажам и когда список взят из таблицы.</summary>
        public List<CharLeft> Left = new List<CharLeft>();
        public DateTime? LeftAt;
        /// <summary>Почему списка нет (например, скрипт таблицы старой версии).</summary>
        public string LeftNote = "";
        /// <summary>«Обновите аддон»: аддон в игре старше Program.MinAddon ("" — всё в порядке).</summary>
        public string AddonNote = "";

        public WorkerStatus Clone()
        {
            WorkerStatus c = (WorkerStatus)MemberwiseClone();
            c.Recent = new List<RecentEvent>(Recent);
            c.Left = new List<CharLeft>(Left);
            return c;
        }
    }

    /// <summary>Уведомление для значка в трее.</summary>
    sealed class Note
    {
        public string Title;
        public string Text;
        public bool Warning;
        /// <summary>Ответ на действие человека — показывать, даже если уведомления выключены.</summary>
        public bool Manual;
        /// <summary>Уведомление о новой версии: щелчок по нему запускает обновление.</summary>
        public bool IsUpdate;
    }

    /// <summary>
    /// Фоновая синхронизация: раз в несколько секунд проверяет файлы сохранений аддона
    /// и, когда они изменились, отправляет новые записи. Без связи повторяет через
    /// 1, 2, 4, 8, а затем каждые 15 минут.
    /// </summary>
    sealed partial class SyncWorker
    {
        public event Action<WorkerStatus> StatusChanged;
        public event Action<Note> Notify;

        const int RecentLimit = 10;

        SentLedger sent;
        string sentPath;
        readonly string statePath;
        readonly HashSet<string> missingNotified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object gate = new object();
        readonly WorkerStatus status = new WorkerStatus();
        SyncEngine engine;
        Settings pendingSettings;
        bool pendingAnnounce;
        bool reloadRequested;
        bool forceRequested;
        bool collectionRequested;
        // время снимка коллекции, с которым таблица уже сверена (-1 — ещё не смотрели)
        long collectionSeen = -1;
        bool networkBack;
        volatile bool stopping;
        readonly ProgramMark mark = new ProgramMark();   // Program.lua: «программа запущена» для аддона
        bool announce;
        DateTime quietUntil = DateTime.MinValue;
        string lastSignature;

        // состояние наблюдения
        readonly Dictionary<string, string> stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<string> dirs = new List<string>();
        List<string> files = new List<string>();
        DateTime nextRescan = DateTime.MinValue;
        DateTime? retryAt;
        int retryStep = 1;
        bool pinged;
        bool saidNoGame;
        bool saidNoFiles;
        bool failureNotified;
        DateTime lastAttempt = DateTime.MinValue;
        string lastAttemptFiles = "";

        // список «Осталось» для аддона
        static readonly TimeSpan StatusInterval = TimeSpan.FromMinutes(30);
        bool statusDirty = true;

        /// <summary>Данные на этом компьютере: в сообщениях нет таблицы.</summary>
        bool Local
        {
            get { return engine.Settings.IsLocal; }
        }

        DateTime nextStatusAt = DateTime.MinValue;
        int statusFailures;
        bool statusUnsupported;
        string sheetLua;
        string leftCounts = "";
        readonly HashSet<string> sheetWritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> sheetProblems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool saidNoAddon;

        /// <param name="statePath">state.json: последняя отправка и «Последние события» — чтобы меню
        /// помнило их и после перезапуска (null — не сохранять)</param>
        public SyncWorker(string statePath)
        {
            this.statePath = statePath;
            LoadState();
        }

        void LoadState()
        {
            if (statePath == null || !File.Exists(statePath))
            {
                return;
            }
            try
            {
                Dictionary<string, object> d = Json.Parse(File.ReadAllText(statePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null)
                {
                    return;
                }
                long last = J.Long(d, "lastSend");
                if (last > 0)
                {
                    status.LastSend = Epoch.ToLocal(last);
                    status.LastResult = J.Str(d, "lastResult");
                }
                foreach (object o in J.Arr(J.Get(d, "recent")))
                {
                    Dictionary<string, object> e = J.Obj(o);
                    long t = J.Long(e, "time");
                    string text = J.Str(e, "text");
                    if (t > 0 && text.Length > 0 && status.Recent.Count < RecentLimit)
                    {
                        status.Recent.Add(new RecentEvent(Epoch.ToLocal(t), text));
                    }
                }
                foreach (object o in J.Arr(J.Get(d, "missingNotified")))
                {
                    string name = Convert.ToString(o, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(name))
                    {
                        missingNotified.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось прочитать state.json ({0}) — история событий начнётся заново.", ex.Message));
            }
        }

        void SaveState()
        {
            if (statePath == null)
            {
                return;
            }
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["lastSend"] = status.LastSend.HasValue ? Epoch.FromLocal(status.LastSend.Value) : 0L;
            d["lastResult"] = status.LastResult;
            List<object> recent = new List<object>();
            foreach (RecentEvent e in status.Recent)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["time"] = Epoch.FromLocal(e.When);
                item["text"] = e.Text;
                recent.Add(item);
            }
            d["recent"] = recent;
            List<object> missing = new List<object>();
            foreach (string name in missingNotified)
            {
                missing.Add(name);
            }
            d["missingNotified"] = missing;
            try
            {
                FileUtil.WriteAtomic(statePath, Json.Write(d, true) + "\r\n");
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить state.json: {0}", ex.Message));
            }
        }

        /// <param name="announceFirstPing">сообщить уведомлением, что связь есть (при ручном запуске)</param>
        /// <param name="startDelay">пауза перед первой проверкой (при автозапуске сеть может быть ещё не готова)</param>
        /// <param name="quietPeriod">первое время не сообщать об ошибках связи — только повторять</param>
        public void Start(Settings settings, bool announceFirstPing, TimeSpan startDelay, TimeSpan quietPeriod)
        {
            UseLedger(settings);
            announce = announceFirstPing;
            quietUntil = DateTime.Now + startDelay + quietPeriod;
            Thread thread = new Thread(delegate()
            {
                if (startDelay > TimeSpan.Zero)
                {
                    wake.WaitOne(startDelay);
                }
                Loop();
            });
            thread.IsBackground = true;
            thread.Name = "MountSheetSync sync";
            thread.Start();
            try
            {
                // сеть появилась (например, после сна) — не ждать следующей попытки по расписанию
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            }
            catch (Exception)
            {
            }
        }

        void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                return;
            }
            lock (gate)
            {
                networkBack = true;
            }
            wake.Set();
        }

        /// <summary>Один проход (параметр --once).</summary>
        public void RunOnce(Settings settings)
        {
            UseLedger(settings);
            IterationInSession();
        }

        /// <summary>Один проход — одна загрузка таблицы (см. SyncEngine.BeginSession).</summary>
        void IterationInSession()
        {
            SyncEngine current = engine;
            current.BeginSession();
            try
            {
                Iteration();
            }
            finally
            {
                current.EndSession();
            }
        }

        /// <summary>Список отправленного — свой у каждой таблицы (AppPaths.SentFileFor).</summary>
        void UseLedger(Settings settings)
        {
            string path = AppPaths.SentFileFor(settings);
            if (sent == null || path != sentPath)
            {
                sent = new SentLedger(path);
                sentPath = path;
                engine = new SyncEngine(settings.Clone(), sent);
                return;
            }
            engine.Settings = settings.Clone();
        }

        public void Reload(Settings settings, bool announceResult)
        {
            lock (gate)
            {
                pendingSettings = settings.Clone();
                pendingAnnounce = announceResult;
                reloadRequested = true;
            }
            wake.Set();
        }

        public void SyncNow()
        {
            lock (gate)
            {
                forceRequested = true;
            }
            wake.Set();
        }

        /// <summary>Этих персонажей убрали из таблицы сами — не напоминать, что у них нет листов.</summary>
        public void IgnoreMissing(IEnumerable<string> names)
        {
            lock (gate)
            {
                foreach (string n in names)
                {
                    missingNotified.Add(SheetLogic.StripNote(n));
                }
            }
            SaveState();
        }

        /// <summary>«Сверить с коллекцией» из меню: сверить сейчас, даже если снимок коллекции не менялся.</summary>
        public void SyncCollectionNow()
        {
            lock (gate)
            {
                collectionRequested = true;
            }
            wake.Set();
        }

        public void Stop()
        {
            stopping = true;
            WatchFiles(new List<string>());
            wake.Set();
            mark.Stop();
        }

        // Слежение за файлами аддона (2.10.1): игра записала файл — цикл просыпается сразу, а не через pollSeconds.
        // Опрос остаётся про запас: на сетевом диске или в облачной папке события приходят не всегда.
        bool collectionSameSaid;
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        string watchedKey = "";

        void WatchFiles(List<string> list)
        {
            lock (watchers)
            {
                WatchFilesLocked(list);
            }
        }

        void WatchFilesLocked(List<string> list)
        {
            string key = string.Join("|", list.ToArray());
            if (key == watchedKey)
            {
                return;
            }
            watchedKey = key;
            foreach (FileSystemWatcher old in watchers)
            {
                try
                {
                    old.Dispose();
                }
                catch (Exception)
                {
                }
            }
            watchers.Clear();
            foreach (string f in list)
            {
                try
                {
                    // игра пишет во временный файл и переименовывает: ловим и запись, и появление, и переименование
                    FileSystemWatcher w = new FileSystemWatcher(Path.GetDirectoryName(f), Path.GetFileName(f));
                    w.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                    w.Changed += delegate { wake.Set(); };
                    w.Created += delegate { wake.Set(); };
                    w.Renamed += delegate { wake.Set(); };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Не удалось включить слежение за файлом ({0}): проверяю его раз в несколько секунд.", ex.Message));
                }
            }
        }

        void Loop()
        {
            while (!stopping)
            {
                int waitSeconds = Math.Max(2, engine.Settings.PollSeconds);
                try
                {
                    IterationInSession();
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Внутренняя ошибка: {0}", ex));
                    status.State = SyncState.Error;
                    status.Message = Tr.F("внутренняя ошибка: {0}", ex.Message);
                    Publish();
                    waitSeconds = 60;
                }
                if (stopping)
                {
                    break;
                }
                wake.WaitOne(TimeSpan.FromSeconds(waitSeconds));
            }
        }

        void Iteration()
        {
            bool force;
            bool network;
            bool collectionNow;
            Settings fresh = null;
            bool freshAnnounce = false;
            lock (gate)
            {
                force = forceRequested;
                forceRequested = false;
                collectionNow = collectionRequested;
                collectionRequested = false;
                network = networkBack;
                networkBack = false;
                if (reloadRequested)
                {
                    fresh = pendingSettings;
                    freshAnnounce = pendingAnnounce;
                    reloadRequested = false;
                }
            }
            if (fresh != null)
            {
                UseLedger(fresh);
                stamps.Clear();
                dirs = new List<string>();
                files = new List<string>();
                nextRescan = DateTime.MinValue;
                retryAt = null;
                retryStep = 1;
                pinged = false;
                saidNoGame = false;
                saidNoFiles = false;
                failureNotified = false;
                quietUntil = DateTime.MinValue;
                status.Spreadsheet = "";
                status.SpreadsheetUrl = "";
                statusDirty = true;
                statusUnsupported = false;
                statusFailures = 0;
                collectionSeen = -1;
                if (freshAnnounce)
                {
                    announce = true;
                }
            }
            if (force || network)
            {
                statusDirty = true;
            }
            mark.Beat(engine.Settings);

            if (!engine.Settings.IsConfigured)
            {
                SetState(SyncState.NotConfigured, "");
                if (force || collectionNow)
                {
                    Send(engine.Settings.MissingText, Tr._("ПКМ по значку → «Настройки…»."), true, true);
                }
                return;
            }

            DateTime now = DateTime.Now;
            if (collectionNow)
            {
                nextRescan = DateTime.MinValue;
            }
            if (force)
            {
                nextRescan = DateTime.MinValue;
            }
            if (now >= nextRescan)
            {
                Rescan(now);
            }
            KeepSheetFiles();

            if (retryAt.HasValue && now < retryAt.Value && !force)
            {
                if (network && retryAt.Value > now.AddSeconds(5))
                {
                    retryAt = now.AddSeconds(5);   // сеть вернулась — попробуем через 5 секунд
                    status.RetryAt = retryAt;
                    Publish();
                }
                // Пока ждём повтора, свежие записи из игры (файл изменился) — повод попробовать раньше,
                // но не чаще раза в 30 секунд.
                bool fresher = now - lastAttempt >= TimeSpan.FromSeconds(30) && FilesSignature() != lastAttemptFiles;
                if (!fresher)
                {
                    if (collectionNow)
                    {
                        Send(Tr._("Сверить с коллекцией пока нельзя"), Tr.F("Нет связи с таблицей — программа повторит попытку в {0}. Потом выберите «Сверить с коллекцией» ещё раз.",
                            retryAt.Value.ToString("HH:mm")), true, true);
                    }
                    return;   // состояние уже показывает время повтора
                }
            }

            if (!pinged)
            {
                SetState(SyncState.Checking, "");
                NoteAttempt();
                PingInfo info;
                try
                {
                    info = engine.Ping(files);
                }
                catch (SyncException ex)
                {
                    Failed(ex, force || collectionNow, true);
                    return;
                }
                pinged = true;
                status.Spreadsheet = info.Spreadsheet;
                status.SpreadsheetUrl = info.Url;
                Recovered();
                if (announce)
                {
                    announce = false;
                    Send(Tr._("Mount Sheet Sync работает"),
                        engine.Settings.IsLocal
                            ? Tr._("Данные хранятся на этом компьютере. Отметки появятся сами, когда выйдете на выбор персонажа, из игры или сделаете /reload.")
                            : Tr.F("Связь с таблицей «{0}» есть. Отметки уйдут сами, когда выйдете на выбор персонажа, из игры или сделаете /reload.", info.Spreadsheet),
                        false, false);
                }
                // О персонаже без листов сообщаем один раз, а не при каждом запуске Windows.
                bool stateChanged = false;
                foreach (string name in info.Found)
                {
                    stateChanged |= missingNotified.Remove(name);
                }
                List<string> newlyMissing = info.Missing.Where(m => !missingNotified.Contains(m)).ToList();
                if (newlyMissing.Count > 0)
                {
                    missingNotified.UnionWith(newlyMissing);
                    stateChanged = true;
                    if (!engine.Settings.IsLocal)   // без таблицы новые персонажи добавляются сами, а остальных убрал сам человек
                    {
                        Send(Tr.F("Нет листов для: {0}", string.Join(", ", newlyMissing)),
                            !engine.Settings.UsesScript
                                ? Tr._("Добавьте их в таблицу: меню значка → «Персонажи в таблице…».")
                                : Tr._("Отметки этих персонажей сохраняться не будут. Проверьте названия листов в таблице."), true, false);
                    }
                }
                if (stateChanged)
                {
                    SaveState();
                }
            }

            if (dirs.Count == 0)
            {
                SetState(SyncState.NoGame, "");
                if (force)
                {
                    Send(Tr._("Не найдена папка World of Warcraft"), Tr._("Укажите её: ПКМ по значку → «Настройки…»."), true, true);
                }
                return;
            }
            if (files.Count == 0)
            {
                bool noAddon = !AddonInstalled();
                SetState(SyncState.NoFile, noAddon ? NoAddonState : "");
                if (force || collectionNow)
                {
                    if (noAddon)
                    {
                        Send(Tr._("Аддон Mount Sheet Sync не установлен"), NoAddonText(), true, true);
                    }
                    else
                    {
                        Send(Tr._("Файла аддона пока нет"),
                            Tr._("Зайдите в игру с включённым аддоном и выйдите на выбор персонажа (или наберите /reload)."), false, true);
                    }
                }
                RefreshStatusIfDue();
                return;
            }

            Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (string f in files)
            {
                string stamp = Stamp(f);
                if (stamp == null)
                {
                    continue;
                }
                current[f] = stamp;
                string old;
                if (!stamps.TryGetValue(f, out old) || old != stamp)
                {
                    changed = true;
                }
            }
            if (current.Count == 0)
            {
                nextRescan = DateTime.MinValue;   // файлы пропали — поискать заново
                SetIdle();
                return;
            }
            bool firstPass = stamps.Count == 0;
            if (!changed && !retryAt.HasValue && !force)
            {
                SetIdle();
                if (collectionNow)
                {
                    CheckCollection(current.Keys.ToList(), true);
                }
                RefreshStatusIfDue();
                return;
            }
            if (!firstPass && changed && !force)
            {
                Thread.Sleep(800);   // игра может ещё дописывать файл
            }

            SyncSummary sum;
            NoteAttempt();
            try
            {
                sum = engine.SyncAll(current.Keys.ToList());
            }
            catch (SyncException ex)
            {
                Failed(ex, force, false);
                return;
            }
            foreach (KeyValuePair<string, string> kv in current)
            {
                stamps[kv.Key] = kv.Value;
            }
            Recovered();
            Report(sum, force);
            SetIdle();
            if (sum.SentRecords > 0 || sum.AddedCharacters.Count > 0 || (engine.Settings.IsLocal && !engine.Settings.HasOwnBook))
            {
                // таблица изменилась — список «Осталось» тоже; а новому игроку и новому персонажу список нужен сразу, не через полчаса по расписанию
                statusDirty = true;
            }
            CheckCollection(current.Keys.ToList(), collectionNow);
            CheckAddonVersion(current.Keys.ToList());
            RefreshStatusIfDue();
        }

        static readonly System.Text.RegularExpressions.Regex AddonVersionField =
            new System.Text.RegularExpressions.Regex(@"\[""addonVersion""\]\s*=\s*""([^""]+)""");
        string addonNoted = "";

        /// <summary>
        /// Версия аддона из файлов сохранений (аддон 2.8+ пишет её при входе). Старше Program.MinAddon — строка в
        /// меню и одно уведомление на эту версию: программа и аддон выходят отдельно.
        /// </summary>
        void CheckAddonVersion(List<string> addonFiles)
        {
            string newest = null;
            foreach (string f in addonFiles)
            {
                try
                {
                    System.Text.RegularExpressions.Match m = AddonVersionField.Match(File.ReadAllText(f, Encoding.UTF8));
                    if (m.Success && (newest == null || Updater.IsNewer(m.Groups[1].Value, newest)))
                    {
                        newest = m.Groups[1].Value;
                    }
                }
                catch (Exception swallowed)
                {
                    Log.Quiet("SyncWorker: read the addon version", swallowed);
                }
            }
            string note = newest != null && Updater.IsNewer(Program.MinAddon, newest)
                ? Tr.F("Аддон {0} устарел — обновите его в CurseForge (программе нужна {1} или новее)", newest, Program.MinAddon)
                : "";
            if (note == status.AddonNote)
            {
                return;
            }
            status.AddonNote = note;
            Publish();
            if (note.Length > 0 && addonNoted != newest)
            {
                addonNoted = newest;
                Log.Write(Tr.L("Аддон {0} старше, чем нужно программе ({1}): обновите его в CurseForge.", newest, Program.MinAddon));
                Send(Tr._("Обновите аддон Mount Sheet Sync"), note + ".", true, false);
            }
        }

        /// <summary>
        /// Сверка таблицы с коллекцией маунтов из игры: сама — когда аддон прочитал коллекцию заново
        /// (после входа в игру), из меню — сразу. Только для таблицы, созданной программой.
        /// </summary>
        void CheckCollection(List<string> addonFiles, bool manual)
        {
            if (!engine.Settings.HasOwnBook)
            {
                if (manual)
                {
                    Send(Tr._("Сверка — для таблицы, созданной программой"),
                        Tr._("В таблице со скриптом отметьте «Собрано?» вручную или создайте новую таблицу (вход через Google)."), true, true);
                }
                return;
            }
            GameSnapshot game;
            try
            {
                game = GameData.Read(addonFiles);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось прочитать коллекцию маунтов из файлов аддона: {0}", ex.Message));
                return;
            }
            if (game.CollectionUpdated <= 0)
            {
                if (manual)
                {
                    Send(Tr._("Аддон ещё не прочитал коллекцию маунтов"),
                        Tr._("Зайдите в игру с аддоном Mount Sheet Sync и сделайте /reload — потом сверьте ещё раз."), true, true);
                }
                return;
            }
            if (!manual && game.CollectionUpdated == collectionSeen)
            {
                return;
            }
            CollectionResult res;
            try
            {
                res = engine.BookService().SyncCollection(game, manual);
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Сверка с коллекцией не прошла: {0} Попробую при следующем обновлении данных из игры.", TextUtil.Sentence(ex.Message)));
                if (manual)
                {
                    Send(Tr._("Сверить с коллекцией не получилось"), TextUtil.Sentence(ex.Message), true, true);
                }
                return;
            }
            collectionSeen = game.CollectionUpdated;
            if (res == null)
            {
                return;   // с этим снимком коллекции таблица уже сверена
            }
            // «всё уже совпадает» — раз за запуск или по ручной команде: иначе это строка на каждый вход в игру
            if (res.Changed || manual || !collectionSameSaid)
            {
                Log.Write(Tr.L("Сверка с коллекцией: {0}.", res.Summary()));
            }
            collectionSameSaid = collectionSameSaid || !res.Changed;
            if (res.Changed)
            {
                statusDirty = true;
                AddRecent(Tr.F("Сверка с коллекцией: {0}", res.Summary()));
                SaveState();
                Publish();
                Send(Local ? Tr._("Данные сверены с коллекцией") : Tr._("Таблица сверена с коллекцией"), TextUtil.Sentence(res.Summary()), false, manual);
            }
            else if (manual)
            {
                Send(Local ? Tr._("Данные сверены с коллекцией") : Tr._("Таблица сверена с коллекцией"), Tr._("Всё уже совпадает: собранные маунты отмечены, лишних строк нет."), false, true);
            }
        }

        /// <summary>
        /// Список «Осталось на этой неделе» из таблицы → SheetData.lua в папке аддона.
        /// Обновляется после отправки, по «Отправить сейчас» и раз в 30 минут (правки в таблице руками).
        /// </summary>
        bool wowWasRunning;
        bool wowCached;
        DateTime wowCheckedAt = DateTime.MinValue;

        /// <summary>Запущена ли игра (Wow.exe и её варианты); проверка не чаще раза в 15 секунд.</summary>
        bool WowRunning(DateTime now)
        {
            if (now - wowCheckedAt < TimeSpan.FromSeconds(15) && now >= wowCheckedAt)
            {
                return wowCached;
            }
            wowCheckedAt = now;
            wowCached = GameProcess.Running();
            return wowCached;
        }

        void RefreshStatusIfDue()
        {
            DateTime now = DateTime.Now;
            bool wow = WowRunning(now);
            if (wow && !wowWasRunning)
            {
                statusDirty = true;   // игра только что запустилась — свежий список к первому входу персонажем
            }
            wowWasRunning = wow;
            if (!statusDirty && (now < nextStatusAt || !wow))
            {
                return;   // по расписанию (правки в таблице руками) — только пока игра запущена
            }
            if (statusUnsupported && !statusDirty)
            {
                return;
            }
            statusDirty = false;
            Dictionary<string, object> resp;
            try
            {
                resp = engine.FetchStatus();
            }
            catch (OldScriptException ex)
            {
                if (!statusUnsupported)
                {
                    Log.Write(Tr.L("Список «Осталось» недоступен: {0}.", ex.Message));
                }
                statusUnsupported = true;
                nextStatusAt = now + StatusInterval;
                status.LeftNote = Tr._("Обновите Code.gs в таблице");
                Publish();
                return;
            }
            catch (SyncException ex)
            {
                statusFailures++;
                nextStatusAt = now.AddMinutes(Math.Min(30, 5 * statusFailures));
                if (statusFailures == 1)
                {
                    Log.Write(Tr.L("Не удалось получить список «Осталось»: {0} Попробую позже.", TextUtil.Sentence(ex.Message)));
                }
                return;
            }
            statusFailures = 0;
            statusUnsupported = false;
            nextStatusAt = now + StatusInterval;
            resp = ResetWeekIfDue(resp);
            sheetLua = SheetData.Build(resp);
            status.Left = SheetData.Summarize(resp);
            status.LeftAt = now;
            status.LeftNote = WeekNote(resp);
            string counts = SheetData.Counts(status.Left);
            if (counts != leftCounts)
            {
                leftCounts = counts;
                Log.Write(Tr.L("Список «Осталось» обновлён: {0}.", counts.Length > 0 ? counts : Tr.L("персонажей нет")));
            }
            WriteSheetFiles();
            Publish();
        }

        /// <summary>
        /// Новая неделя игры, а в таблице сброса ещё не было — сбросить самим (и заново отметить убийства этой недели,
        /// как после кнопки). Ответ — свежий список; не получилось — прежний и попытка при следующем обновлении списка.
        /// </summary>
        Dictionary<string, object> ResetWeekIfDue(Dictionary<string, object> resp)
        {
            try
            {
                Dictionary<string, object> reset = engine.WeekResetRecord(resp, engine.ReadAllRecords(files), DateTime.UtcNow);
                if (reset == null)
                {
                    return resp;
                }
                Report(engine.SyncAll(files, reset), false);
                SetIdle();
                return engine.FetchStatus();
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Автоматический сброс недели не прошёл: {0} Попробую позже.", TextUtil.Sentence(ex.Message)));
                return resp;
            }
        }

        /// <summary>Напоминание о «Сбросе недели» для меню; сломанный файл игры его просто отменяет.</summary>
        string WeekNote(Dictionary<string, object> resp)
        {
            try
            {
                return SheetData.WeekNote(resp, engine.ReadAllRecords(files), DateTime.UtcNow);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Значение WorkerStatus.Message в состоянии NoFile, когда в игре нет самого аддона.</summary>
        public const string NoAddonState = "no-addon";
    }
}

namespace MountSheetSync
{
    /// <summary>Процесс игры: «Осталось» по расписанию обновляется, только пока он есть (меньше обращений к Google).</summary>
    static class GameProcess
    {
#if !TESTS
        static readonly string[] Names = { "Wow", "WowT", "WowB", "Wow-ARM64", "WowT-ARM64", "WowB-ARM64" };
#endif

        public static bool Running()
        {
#if TESTS
            // проверки под mono: игры нет, поэтому по умолчанию «запущена» (как раньше); MSS_WOW_RUNNING=0 — нет
            string test = Environment.GetEnvironmentVariable("MSS_WOW_RUNNING");
            if (test != null)
            {
                return test == "1";
            }
            return true;
#else
            foreach (string name in Names)
            {
                try
                {
                    System.Diagnostics.Process[] found = System.Diagnostics.Process.GetProcessesByName(name);
                    bool any = found.Length > 0;
                    foreach (System.Diagnostics.Process p in found)
                    {
                        p.Dispose();
                    }
                    if (any)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    return true;   // не смогли проверить — ведём себя как раньше
                }
            }
            return false;
#endif
        }
    }
}
