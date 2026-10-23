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
    }

    /// <summary>
    /// Фоновая синхронизация: раз в несколько секунд проверяет файлы сохранений аддона
    /// и, когда они изменились, отправляет новые записи. Без связи повторяет через
    /// 1, 2, 4, 8, а затем каждые 15 минут.
    /// </summary>
    sealed class SyncWorker
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
            Iteration();
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
            wake.Set();
        }

        void Loop()
        {
            while (!stopping)
            {
                int waitSeconds = Math.Max(2, engine.Settings.PollSeconds);
                try
                {
                    Iteration();
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
                        Tr.F("Связь с таблицей «{0}» есть. Отметки уйдут сами, когда выйдете на выбор персонажа, из игры или сделаете /reload.", info.Spreadsheet),
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
                    Send(Tr.F("Нет листов для: {0}", string.Join(", ", newlyMissing)),
                        engine.Settings.UseGoogle
                            ? Tr._("Добавьте их в таблицу: меню значка → «Персонажи в таблице…».")
                            : Tr._("Отметки этих персонажей сохраняться не будут. Проверьте названия листов в таблице."), true, false);
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
            if (sum.SentRecords > 0)
            {
                statusDirty = true;   // таблица изменилась — список «Осталось» тоже
            }
            CheckCollection(current.Keys.ToList(), collectionNow);
            RefreshStatusIfDue();
        }

        /// <summary>
        /// Сверка таблицы с коллекцией маунтов из игры: сама — когда аддон прочитал коллекцию заново
        /// (после входа в игру), из меню — сразу. Только для таблицы, созданной программой.
        /// </summary>
        void CheckCollection(List<string> addonFiles, bool manual)
        {
            if (!engine.Settings.UseGoogle || engine.Settings.SpreadsheetId.Length == 0)
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
                res = new GoogleSheetService(engine.Settings.SpreadsheetId).SyncCollection(game, manual);
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
            Log.Write(Tr.L("Сверка с коллекцией: {0}.", res.Summary()));
            if (res.Changed)
            {
                statusDirty = true;
                AddRecent(Tr.F("Сверка с коллекцией: {0}", res.Summary()));
                SaveState();
                Publish();
                Send(Tr._("Таблица сверена с коллекцией"), TextUtil.Sentence(res.Summary()), false, manual);
            }
            else if (manual)
            {
                Send(Tr._("Таблица сверена с коллекцией"), Tr._("Всё уже совпадает: собранные маунты отмечены, лишних строк нет."), false, true);
            }
        }

        /// <summary>
        /// Список «Осталось на этой неделе» из таблицы → SheetData.lua в папке аддона.
        /// Обновляется после отправки, по «Отправить сейчас» и раз в 30 минут (правки в таблице руками).
        /// </summary>
        void RefreshStatusIfDue()
        {
            DateTime now = DateTime.Now;
            if (!statusDirty && now < nextStatusAt)
            {
                return;
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
                        continue;
                    }
                    FileUtil.WriteAtomic(path, sheetLua);
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

        void Failed(SyncException ex, bool force, bool duringPing)
        {
            int unit = Math.Max(1, engine.Settings.RetrySeconds);
            int wait = unit * retryStep;
            string when = unit == 60 ? Tr.F("{0} мин.", retryStep) : Tr.F("{0} с", wait);
            string whenLog = unit == 60 ? Tr.L("{0} мин.", retryStep) : Tr.L("{0} с", wait);
            Log.Write(duringPing
                ? Tr.L("Проверка связи не прошла: {0} Повторю через {1}", TextUtil.Sentence(ex.Message), whenLog)
                : Tr.L("Не удалось отправить: {0} Повторю через {1}", TextUtil.Sentence(ex.Message), whenLog));
            bool first = !retryAt.HasValue;
            retryAt = DateTime.Now.AddSeconds(wait);
            retryStep = Math.Min(retryStep * 2, 15);
            if (first)
            {
                AddRecent(TextUtil.Capitalize(ex.Message));
                SaveState();
            }
            status.State = SyncState.Error;
            status.Message = ex.Message;
            status.RetryAt = retryAt;
            Publish();
            bool quiet = DateTime.Now < quietUntil;
            if (force || (!failureNotified && !quiet))
            {
                failureNotified = true;
                Send(FailureTitle(ex.Message), Tr.F("{0} Повторю через {1}", TextUtil.Sentence(TextUtil.Capitalize(ex.Message)), when), true, force);
            }
        }

        /// <summary>Сеть недоступна (а не таблица ответила ошибкой).</summary>
        public static bool IsNetworkError(string message)
        {
            return Tr.StartsWith(message, "нет связи");
        }

        /// <summary>Заголовок для меню и уведомления об ошибке.</summary>
        public static string FailureTitle(string message)
        {
            if (IsNetworkError(message))
            {
                return Tr._("Нет связи с таблицей");
            }
            if (Tr.StartsWith(message, "внутренняя ошибка"))
            {
                return Tr._("Сбой программы — подробности в журнале");
            }
            if (Tr.StartsWith(message, "вход в Google") || Tr.StartsWith(message, "не выполнен вход"))
            {
                return Tr._("Нужно снова войти в Google");
            }
            return Tr._("Таблица не приняла запрос");
        }

        void Recovered()
        {
            if (retryAt.HasValue)
            {
                Log.Write(Tr.L("Связь с таблицей восстановлена."));
                AddRecent(Tr._("Связь восстановлена"));
                SaveState();
                if (failureNotified)
                {
                    Send(Tr._("Связь с таблицей восстановлена"), Tr._("Всё, что накопилось, отправлено."), false, false);
                }
            }
            failureNotified = false;
            retryAt = null;
            retryStep = 1;
            status.RetryAt = null;
        }

        void Report(SyncSummary sum, bool manual)
        {
            if (sum.NothingToSend)
            {
                if (manual)
                {
                    Send(Tr._("Новых записей нет"),
                        Tr._("Всё уже в таблице. Свежие убийства игра записывает в файл, когда вы выходите на выбор персонажа, из игры или делаете /reload."),
                        false, true);
                }
                return;
            }
            status.LastSend = DateTime.Now;
            if (sum.ResetMessage != null)
            {
                AddRecent(Tr.F("Сброс недели ({0}): {1}", sum.ResetBy, sum.ResetMessage));
            }
            foreach (string m in sum.Marked)
            {
                AddRecent(Tr.F("{0} → «Да»", m));
            }
            foreach (string m in sum.Collected)
            {
                AddRecent(Tr.F("Собрано: {0}", m));
            }
            foreach (string m in sum.MountsNotFound)
            {
                AddRecent(Tr.F("! маунта «{0}» нет в таблице", m));
            }
            foreach (string p in sum.Planned)
            {
                AddRecent(Tr.F("Не фармлю — {0}", p));
            }
            foreach (string w in sum.Warnings)
            {
                AddRecent("! " + w);
            }
            status.LastResult = sum.ResetMessage != null ? Tr._("сброс недели")
                : sum.Marked.Count > 0 ? Tr.F("отмечено {0}", sum.Marked.Count)
                : sum.Collected.Count > 0 ? Tr._("новый маунт")
                : sum.Planned.Count > 0 ? Tr.F("не фармлю: {0}", sum.Planned.Count)
                : Tr._("новых отметок нет");
            SaveState();

            List<string> lines = new List<string>();
            string title;
            bool warning = false;
            if (sum.ResetMessage != null)
            {
                title = Tr._("Сброс недели выполнен");
                lines.Add(TextUtil.Sentence(sum.ResetMessage));
                if (sum.Marked.Count > 0)
                {
                    lines.Add(Tr.F("Заново отмечено убийств этой недели: {0}.", sum.Marked.Count));
                }
            }
            else if (sum.Marked.Count > 0)
            {
                title = Tr.F("Отмечено в таблице: {0}", sum.Marked.Count);
                lines.AddRange(sum.Marked.Take(4));
                if (sum.Marked.Count > 4)
                {
                    lines.Add(Tr.F("…и ещё {0}", sum.Marked.Count - 4));
                }
            }
            else if (sum.Collected.Count > 0)
            {
                title = sum.Collected.Count == 1 ? Tr.F("Новый маунт: {0}", sum.Collected[0]) : Tr.F("Новые маунты: {0}", sum.Collected.Count);
                lines.Add(sum.Collected.Count == 1 ? Tr._("«Собрано?» в таблице отмечено.")
                    : Tr.F("«Собрано?» в таблице отмечено: {0}.", string.Join(", ", sum.Collected)));
            }
            else if (sum.Planned.Count > 0)
            {
                title = Tr.F("Не фармлю: {0}", sum.Planned.Count);
                lines.AddRange(sum.Planned.Take(4));
                lines.Add(Tr._("Галочка «Фармлю» в таблице снята — в «Осталось» этих строк больше нет."));
            }
            else if (sum.MountsNotFound.Count > 0)
            {
                title = Tr._("Маунт не нашёлся в таблице");
                lines.Add(Tr.F("{0} — отметьте «Собрано?» вручную.", string.Join(", ", sum.MountsNotFound)));
                warning = true;
            }
            else if (sum.Warnings.Count > 0)
            {
                title = Tr._("Есть замечания");
                lines.Add(sum.Warnings[0]);
                warning = true;
            }
            else
            {
                if (!manual)
                {
                    return;   // только «уже Да» / нет на листах — без уведомления
                }
                title = Tr._("Новых отметок нет");
                lines.Add(Breakdown(sum));
            }
            if (sum.Collected.Count > 0 && (sum.ResetMessage != null || sum.Marked.Count > 0))
            {
                lines.Add(Tr.F("Собрано: {0}.", string.Join(", ", sum.Collected)));
            }
            if (sum.MountsNotFound.Count > 0 && !warning)
            {
                lines.Add(Tr.F("Не нашёлся в таблице: {0} — отметьте «Собрано?» вручную.", string.Join(", ", sum.MountsNotFound)));
            }
            if (sum.Warnings.Count > 0 && !warning)
            {
                lines.Add(Tr.F("Замечаний: {0} — подробности в журнале.", sum.Warnings.Count));
            }
            Send(title, string.Join("\n", lines), warning, manual);
        }

        static string Breakdown(SyncSummary sum)
        {
            List<string> parts = new List<string>();
            if (sum.Already > 0)
            {
                parts.Add(Tr.F("уже «Да»: {0}", sum.Already));
            }
            if (sum.WrongDifficulty > 0)
            {
                parts.Add(Tr.F("другая сложность: {0}", sum.WrongDifficulty));
            }
            if (sum.NotFound > 0)
            {
                parts.Add(Tr.F("нет на листах: {0}", sum.NotFound));
            }
            if (sum.Stale > 0)
            {
                parts.Add(Tr.F("до сброса недели: {0}", sum.Stale));
            }
            if (parts.Count == 0)
            {
                return Tr._("Записи обработаны.");
            }
            string text = string.Join("; ", parts) + ".";
            return char.ToUpper(text[0], CultureInfo.GetCultureInfo("ru-RU")) + text.Substring(1);
        }

        void AddRecent(string text)
        {
            status.Recent.Insert(0, new RecentEvent(DateTime.Now, text));
            if (status.Recent.Count > RecentLimit)
            {
                status.Recent.RemoveRange(RecentLimit, status.Recent.Count - RecentLimit);
            }
        }

        void SetIdle()
        {
            SetState(files.Count > 0 ? SyncState.Ok : SyncState.NoFile, files.Count == 0 && !AddonInstalled() ? NoAddonState : "");
        }

        void SetState(SyncState state, string message)
        {
            status.State = state;
            status.Message = message ?? "";
            if (state != SyncState.Error)
            {
                status.RetryAt = null;
            }
            Publish();
        }

        void Publish()
        {
            status.Files = files.Count;
            string signature = string.Join("|", new string[]
            {
                status.State.ToString(), status.Message, status.Spreadsheet, status.SpreadsheetUrl,
                status.LastSend.HasValue ? status.LastSend.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "",
                status.RetryAt.HasValue ? status.RetryAt.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "",
                status.LastResult, status.Files.ToString(CultureInfo.InvariantCulture),
                status.LeftAt.HasValue ? status.LeftAt.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "", status.LeftNote,
                status.Recent.Count.ToString(CultureInfo.InvariantCulture),
                status.Recent.Count > 0 ? status.Recent[0].When.Ticks.ToString(CultureInfo.InvariantCulture) + status.Recent[0].Text : "",
            });
            if (signature == lastSignature)
            {
                return;
            }
            lastSignature = signature;
            Action<WorkerStatus> handler = StatusChanged;
            if (handler != null)
            {
                handler(status.Clone());
            }
        }

        void Send(string title, string text, bool warning, bool manual)
        {
            Action<Note> handler = Notify;
            if (handler != null)
            {
                Note note = new Note();
                note.Title = title;
                note.Text = text;
                note.Warning = warning;
                note.Manual = manual;
                handler(note);
            }
        }
    }
}
