using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MountSheetSync
{
    /// <summary>Значок в трее: меню по правой (и левой) кнопке, всплывающие уведомления.</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly Control invoker = new Control();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Icon okIcon = AppIcons.Tray(false);
        readonly Icon warnIcon = AppIcons.Tray(true);
        readonly SyncWorker worker;
        ToolStripMenuItem statusItem;
        ToolStripMenuItem detailItem;
        ToolStripMenuItem syncItem;
        ToolStripMenuItem recentItem;
        ToolStripMenuItem leftItem;
        ToolStripMenuItem openSheetItem;
        ToolStripMenuItem signInItem;
        ToolStripMenuItem createItem;
        ToolStripMenuItem addCharsItem;
        ToolStripMenuItem collectionItem;
        ToolStripMenuItem farmItem;
        ToolStripMenuItem autostartItem;
        ToolStripMenuItem notifyItem;
        ToolStripMenuItem languageItem;
        ToolStripMenuItem updateItem;
        ToolStripMenuItem checkUpdatesItem;
        /// <summary>Новая версия на GitHub (null — нет или ещё не проверяли).</summary>
        UpdateInfo available;
        bool updating;
        bool balloonIsUpdate;
        System.Threading.Timer updateTimer;
        Settings settings;
        WorkerStatus last = new WorkerStatus();
        SettingsForm settingsForm;
        bool exiting;

        public TrayApp(bool autostart, string instanceSuffix, string updatedFrom)
        {
            GC.KeepAlive(invoker.Handle);   // окно для передачи событий из фонового потока
            settings = Settings.Load(AppPaths.SettingsFile);
            BuildMenu();
            menu.Opening += delegate { RefreshMenu(); };
            tray.Icon = okIcon;
            tray.ContextMenuStrip = menu;
            tray.MouseUp += OnTrayMouseUp;
            tray.BalloonTipClicked += delegate
            {
                if (balloonIsUpdate && available != null)
                {
                    StartUpdate();
                }
            };
            SetTooltip(Program.Title + " — " + Tr._("запуск…"));
            tray.Visible = true;

            Log.Write(autostart
                ? Tr.L("{0} {1} запущен вместе с Windows. Настройки и журнал: {2}", Program.Title, Program.Version, AppPaths.DataDir)
                : Tr.L("{0} {1} запущен. Настройки и журнал: {2}", Program.Title, Program.Version, AppPaths.DataDir));
            Autostart.Repair();

            worker = new SyncWorker(AppPaths.StateFile);
            worker.StatusChanged += delegate(WorkerStatus s) { Post(delegate { ApplyStatus(s); }); };
            worker.Notify += delegate(Note n) { Post(delegate { ShowNote(n); }); };
            if (autostart)
            {
                worker.Start(settings, false, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(3));
            }
            else
            {
                worker.Start(settings, true, TimeSpan.Zero, TimeSpan.Zero);
            }
            SingleInstance.Listen(instanceSuffix, delegate { Post(OnSecondInstance); });
            SingleInstance.ListenExit(instanceSuffix, delegate { Post(ExitApp); });   // установка или удаление
            Installer.Refresh();

            Updater.CleanupOld();
            if (!string.IsNullOrEmpty(updatedFrom))
            {
                Log.Write(Tr.L("Обновление установлено: {0} → {1}.", updatedFrom, Program.Version));
                ShowNote(MakeNote(Tr.F("Mount Sheet Sync обновлён до версии {0}", Program.Version),
                    Tr._("Что нового — на странице выпуска на GitHub. Если обновился и аддон, наберите в игре /reload."), false));
            }
            // новая версия: через минуту после запуска, потом раз в сутки
            TimeSpan firstCheck = TimeSpan.FromMinutes(1);
#if TESTS
            int testDelay;
            if (int.TryParse(Environment.GetEnvironmentVariable("MSS_UPDATE_DELAY"), out testDelay))
            {
                firstCheck = TimeSpan.FromSeconds(testDelay);
            }
#endif
            updateTimer = new System.Threading.Timer(delegate { CheckForUpdate(false); }, null, firstCheck, TimeSpan.FromHours(24));

            if (!settings.IsConfigured)
            {
                if (autostart)
                {
                    ShowNote(MakeNote(settings.MissingText, Tr._("ПКМ по значку Mount Sheet Sync → «Настройки…»."), true));
                }
                else
                {
                    Post(delegate { OpenSettings(true); });
                }
            }
        }

        static Note MakeNote(string title, string text, bool warning)
        {
            Note n = new Note();
            n.Title = title;
            n.Text = text;
            n.Warning = warning;
            n.Manual = true;
            return n;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            ToolStripMenuItem title = Disabled(Program.Title + " " + Program.Version);
            title.Font = new Font(menu.Font, FontStyle.Bold);
            updateItem = Item("", delegate { StartUpdate(); });
            updateItem.Font = new Font(menu.Font, FontStyle.Bold);
            updateItem.Visible = false;
            checkUpdatesItem = Item(Tr._("Проверять обновления"), delegate { ToggleUpdates(); });
            statusItem = Disabled(Tr._("Запуск…"));
            detailItem = Disabled("");
            detailItem.Visible = false;
            syncItem = Item(Tr._("Отправить сейчас"), delegate { worker.SyncNow(); });
            recentItem = new ToolStripMenuItem(Tr._("Последние события"));
            recentItem.DropDownItems.Add(Disabled(Tr._("Пока ничего не отправлялось")));
            leftItem = new ToolStripMenuItem(Tr._("Осталось на неделе"));
            leftItem.DropDownItems.Add(Disabled(Tr._("Список ещё не получен из таблицы")));
            openSheetItem = Item(Tr._("Открыть таблицу"), delegate { OpenSheet(); });
            openSheetItem.Visible = false;
            signInItem = Item(Tr._("Войти через Google…"), delegate { SignIn(); });
            createItem = Item(Tr._("Создать таблицу…"), delegate { OpenCharacters(false); });
            addCharsItem = Item(Tr._("Персонажи в таблице…"), delegate { OpenCharacters(true); });
            farmItem = Item(Tr._("Что фармить…"), delegate { OpenFarm(); });
            collectionItem = Item(Tr._("Сверить с коллекцией"), delegate { worker.SyncCollectionNow(); });
            collectionItem.ToolTipText = Tr._("Отметить «Собрано?» у маунтов, которые уже есть в игре, и убрать их с листов персонажей");
            autostartItem = Item(Tr._("Запускать вместе с Windows"), delegate { ToggleAutostart(); });
            notifyItem = Item(Tr._("Показывать уведомления"), delegate { ToggleNotifications(); });
            languageItem = new ToolStripMenuItem(Tr._("Язык") + (Tr.Lang != "en" ? " / Language" : ""));
            foreach (string code in new[] { "auto" }.Concat(Tr.Languages))
            {
                string c = code;
                string text = c == "auto" ? Tr.F("Как в Windows ({0})", Tr.Names[Tr.SystemLanguage()]) : Tr.Names[c];
                ToolStripMenuItem li = Item(text, delegate { SetLanguage(c); });
                li.Checked = Tr.Setting == c;
                languageItem.DropDownItems.Add(li);
            }
            menu.Items.AddRange(new ToolStripItem[]
            {
                title, updateItem, statusItem, detailItem, new ToolStripSeparator(),
                syncItem, recentItem, leftItem, openSheetItem, signInItem, createItem, addCharsItem, farmItem, collectionItem, new ToolStripSeparator(),
                Item(Tr._("Настройки…"), delegate { OpenSettings(false); }),
                Item(Tr._("Журнал работы"), delegate { OpenLog(); }),
                autostartItem, notifyItem, checkUpdatesItem, languageItem, new ToolStripSeparator(),
                Item(Tr._("Выход"), delegate { ExitApp(); }),
            });
        }

        /// <summary>Язык программы из меню: сохраняется и действует сразу (меню, подсказка, новые окна).</summary>
        void SetLanguage(string code)
        {
            settings.Language = Tr.Normalize(code);
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
            Tr.Use(settings.Language);
            Log.Write(Tr.L("Язык программы: {0}.", settings.Language == "auto"
                ? Tr.F("Как в Windows ({0})", Tr.Names[Tr.SystemLanguage()]) : Tr.Names[settings.Language]));
            BuildMenu();
            ApplyStatus(last);
        }

        static ToolStripMenuItem Item(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += onClick;
            return item;
        }

        static ToolStripMenuItem Disabled(string text)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Enabled = false;
            return item;
        }

        static string MenuText(string s)
        {
            return (s ?? "").Replace("&", "&&");
        }

        void Post(Action action)
        {
            if (exiting || invoker.IsDisposed || !invoker.IsHandleCreated)
            {
                return;
            }
            try
            {
                invoker.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // окно уже закрыто (ObjectDisposedException — тоже сюда)
            }
        }

        void SetTooltip(string text)
        {
            // В .NET Framework подсказка значка — не длиннее 63 символов, иначе исключение.
            tray.Text = TextUtil.Shorten(text, 63);
        }

        void ApplyStatus(WorkerStatus s)
        {
            if (exiting)
            {
                return;
            }
            last = s;
            bool bad = s.State == SyncState.Error || s.State == SyncState.NotConfigured || s.State == SyncState.NoGame
                || (s.State == SyncState.NoFile && s.Message == SyncWorker.NoAddonState);
            Icon wanted = bad ? warnIcon : okIcon;
            if (tray.Icon != wanted)
            {
                tray.Icon = wanted;
            }
            SetTooltip(TooltipText(s, settings));
            if (menu.Visible)
            {
                RefreshMenu();
            }
        }

        static string TooltipText(WorkerStatus s, Settings cfg)
        {
            string head = Program.Title + " — ";
            switch (s.State)
            {
                case SyncState.NotConfigured:
                    return head + (cfg.UseGoogle ? (TokenStore.Exists ? Tr._("создайте таблицу") : Tr._("войдите через Google")) : Tr._("укажите адрес таблицы"));
                case SyncState.Checking:
                    return head + Tr._("проверяю связь…");
                case SyncState.NoGame:
                    return head + Tr._("не найдена папка WoW");
                case SyncState.NoFile:
                    return head + (s.Message == SyncWorker.NoAddonState ? Tr._("аддон не установлен") : Tr._("связь есть, жду файл аддона"));
                case SyncState.Ok:
                    return head + (s.LastSend.HasValue ? Tr.F("связь есть, отправлено {0}", TextUtil.Stamp(s.LastSend.Value)) : Tr._("связь есть"));
                case SyncState.Error:
                    return head + (SyncWorker.IsNetworkError(s.Message) ? Tr._("нет связи") : Tr._("ошибка"))
                        + (s.RetryAt.HasValue ? Tr.F(", повтор в {0}", s.RetryAt.Value.ToString("HH:mm")) : "");
                default:
                    return head + Tr._("запуск…");
            }
        }

        static void Describe(WorkerStatus s, Settings cfg, out string status, out string detail)
        {
            detail = "";
            switch (s.State)
            {
                case SyncState.NotConfigured:
                    status = cfg.MissingText;
                    detail = cfg.UseGoogle
                        ? (TokenStore.Exists ? Tr._("Нажмите «Создать таблицу…» ниже") : Tr._("Нажмите «Войти через Google…» ниже"))
                        : Tr._("Откройте «Настройки…» и вставьте адрес веб-приложения");
                    break;
                case SyncState.Checking:
                    status = Tr._("Проверяю связь с таблицей…");
                    break;
                case SyncState.NoGame:
                    status = Tr._("Не найдена папка World of Warcraft");
                    detail = Tr._("Укажите её в «Настройках…»");
                    break;
                case SyncState.NoFile:
                    if (s.Message == SyncWorker.NoAddonState)
                    {
                        status = Tr._("Аддон Mount Sheet Sync не установлен");
                        detail = Tr._("Установите его (CurseForge или папка из архива) и зайдите в игру");
                        break;
                    }
                    status = Tr._("Связь с таблицей есть");
                    detail = Tr._("Жду файл аддона: зайдите в игру и выйдите на выбор персонажа");
                    break;
                case SyncState.Ok:
                    status = s.Spreadsheet.Length > 0 ? Tr.F("Таблица «{0}» — связь есть", s.Spreadsheet) : Tr._("Связь с таблицей есть");
                    detail = s.LastSend.HasValue
                        ? Tr.F("Последняя отправка: {0}", TextUtil.When(s.LastSend.Value)) + (s.LastResult.Length > 0 ? " — " + s.LastResult : "")
                        : Tr._("Новых записей пока не было");
                    break;
                case SyncState.Error:
                    status = SyncWorker.FailureTitle(s.Message);
                    detail = TextUtil.Shorten(TextUtil.Capitalize(s.Message), 80)
                        + (s.RetryAt.HasValue ? Tr.F(" · повтор в {0}", s.RetryAt.Value.ToString("HH:mm")) : "");
                    break;
                default:
                    status = Tr._("Запуск…");
                    break;
            }
        }

        void RefreshMenu()
        {
            string status;
            string detail;
            Describe(last, settings, out status, out detail);
            statusItem.Text = MenuText(TextUtil.Shorten(status, 90));
            detailItem.Text = MenuText(detail);
            detailItem.Visible = detail.Length > 0;
            syncItem.Enabled = settings.IsConfigured;
            openSheetItem.Visible = SheetUrl().Length > 0;
            bool google = settings.UseGoogle;
            bool signedIn = google && GoogleSession.SignedIn;
            signInItem.Visible = google && !signedIn;
            createItem.Visible = signedIn && settings.SpreadsheetId.Length == 0;
            // Visible у пункта меню читается как «виден ли сейчас на экране» — поэтому условие отдельно
            bool haveSheet = signedIn && settings.SpreadsheetId.Length > 0;
            addCharsItem.Visible = haveSheet;
            collectionItem.Visible = haveSheet;
            farmItem.Visible = haveSheet;
            autostartItem.Checked = Autostart.IsEnabled();
            notifyItem.Checked = settings.Notifications;
            checkUpdatesItem.Checked = settings.CheckUpdates;
            updateItem.Visible = available != null;
            updateItem.Enabled = !updating;
            if (available != null)
            {
                updateItem.Text = MenuText(updating ? Tr.F("Обновляю до версии {0}…", available.Version)
                    : Tr.F("Обновить до версии {0}…", available.Version));
            }
            ClearItems(recentItem);
            if (last.Recent.Count == 0)
            {
                recentItem.DropDownItems.Add(Disabled(Tr._("Пока ничего не отправлялось")));
            }
            foreach (RecentEvent e in last.Recent)
            {
                string line = TextUtil.Stamp(e.When) + "   " + e.Text;
                recentItem.DropDownItems.Add(Disabled(MenuText(TextUtil.Shorten(line, 90))));
            }
            FillLeft();
        }

        static void ClearItems(ToolStripMenuItem parent)
        {
            while (parent.DropDownItems.Count > 0)
            {
                ToolStripItem old = parent.DropDownItems[0];
                parent.DropDownItems.RemoveAt(0);
                old.Dispose();
            }
        }

        /// <summary>«Осталось на неделе» → персонаж → строки «Нет» из таблицы.</summary>
        void FillLeft()
        {
            ClearItems(leftItem);
            if (last.Left.Count == 0)
            {
                leftItem.DropDownItems.Add(Disabled(last.LeftNote.Length > 0 ? last.LeftNote : Tr._("Список ещё не получен из таблицы")));
                return;
            }
            foreach (CharLeft c in last.Left)
            {
                if (c.Total == 0)
                {
                    leftItem.DropDownItems.Add(Disabled(MenuText(Tr.F("{0} — всё отмечено", c.Name))));
                    continue;
                }
                ToolStripMenuItem sub = new ToolStripMenuItem(MenuText(Tr.F("{0} — {1} из {2}", c.Name, c.Total, c.All)));
                foreach (string line in c.Lines)
                {
                    sub.DropDownItems.Add(Disabled(MenuText(TextUtil.Shorten(line, 90))));
                }
                leftItem.DropDownItems.Add(sub);
            }
            if (last.LeftAt.HasValue || last.LeftNote.Length > 0)
            {
                leftItem.DropDownItems.Add(new ToolStripSeparator());
            }
            if (last.LeftNote.Length > 0)
            {
                leftItem.DropDownItems.Add(Disabled(MenuText(last.LeftNote)));
            }
            if (last.LeftAt.HasValue)
            {
                leftItem.DropDownItems.Add(Disabled(Tr.F("Из таблицы {0}", TextUtil.When(last.LeftAt.Value))));
            }
        }

        void ShowNote(Note n)
        {
            if (n == null || exiting || (!n.Manual && !settings.Notifications))
            {
                return;
            }
            balloonIsUpdate = n.IsUpdate;
            string title = TextUtil.Shorten(string.IsNullOrEmpty(n.Title) ? Program.Title : n.Title, 63);
            string text = TextUtil.Shorten(string.IsNullOrEmpty(n.Text) ? title : n.Text, 255);
            try
            {
                tray.ShowBalloonTip(10000, title, text, n.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Уведомление не показано: {0}", ex.Message));
            }
        }

        void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            // Левой кнопкой — то же меню, что и правой.
            try
            {
                MethodInfo show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                if (show != null)
                {
                    show.Invoke(tray, null);
                    return;
                }
            }
            catch (Exception)
            {
            }
            menu.Show(Cursor.Position);
        }

        void OpenSettings(bool firstRun)
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            bool autostartNow = Autostart.IsEnabled();
            using (SettingsForm form = new SettingsForm(settings.Clone(), firstRun, firstRun || autostartNow, CommitGoogle))
            {
                settingsForm = form;
                DialogResult result;
                try
                {
                    result = form.ShowDialog();
                }
                finally
                {
                    settingsForm = null;
                }
                if (result == DialogResult.OK && form.Result != null)
                {
                    settings = form.Result;
                    try
                    {
                        settings.Save(AppPaths.SettingsFile);
                        Log.Write(Tr.L("Настройки сохранены."));
                    }
                    catch (Exception ex)
                    {
                        Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
                        MessageBox.Show(Tr.F("Не удалось сохранить настройки: {0}", ex.Message), Program.Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    if (form.AutostartChecked != autostartNow || (firstRun && form.AutostartChecked))
                    {
                        SetAutostart(form.AutostartChecked);
                    }
                    worker.Reload(settings, true);
                }
                else if (firstRun && !settings.IsConfigured)
                {
                    ShowNote(MakeNote(settings.MissingText,
                        Tr._("Когда будете готовы: ПКМ по значку Mount Sheet Sync → «Настройки…»."), false));
                }
            }
        }

        /// <summary>Вход, выход, новая таблица — сохраняются сразу, не дожидаясь «Сохранить» в настройках.</summary>
        void CommitGoogle(Settings s, bool newSheet)
        {
            settings.Mode = s.Mode;
            settings.SpreadsheetId = s.SpreadsheetId;
            settings.SpreadsheetTitle = s.SpreadsheetTitle;
            settings.SpreadsheetUrl = s.SpreadsheetUrl;
            if (newSheet)
            {
                settings.WowPath = s.WowPath;
            }
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
            if (newSheet)
            {
                Log.Write(Tr.L("Новая таблица «{0}»: {1}", settings.SpreadsheetTitle, settings.SpreadsheetUrl));
            }
            worker.Reload(settings, true);
        }

        void SignIn()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (SignInForm f = new SignInForm())
            {
                if (f.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }
            Settings s = settings.Clone();
            s.Mode = Settings.ModeGoogle;
            CommitGoogle(s, false);
            if (settings.SpreadsheetId.Length == 0)
            {
                OpenCharacters(false);
            }
            else
            {
                ShowNote(MakeNote(Tr._("Вход в Google выполнен"), Tr.F("Отметки снова уходят в таблицу «{0}».", settings.SpreadsheetTitle), false));
            }
        }

        void OpenFarm()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (FarmForm f = new FarmForm(settings.Clone()))
            {
                if (f.ShowDialog() == DialogResult.OK && f.Changed > 0)
                {
                    ShowNote(MakeNote(Tr._("Что фармить: сохранено"),
                        Tr.F("Изменено строк: {0}. Список «Осталось» в игре обновится после входа персонажем или /reload.", f.Changed), false));
                    worker.SyncNow();
                }
            }
        }

        void OpenCharacters(bool add)
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (CharactersForm f = new CharactersForm(settings.Clone(), add))
            {
                if (f.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                if (add)
                {
                    CharacterChanges ch = f.Changes;
                    ShowNote(MakeNote(Tr._("Персонажи в таблице"), TextUtil.Sentence(ch != null ? ch.Summary() : Tr._("без изменений")), false));
                    if (ch != null && ch.RemovedCharacters.Count > 0)
                    {
                        worker.IgnoreMissing(ch.RemovedCharacters);   // убрали сами — не напоминать «нет листов»
                    }
                    worker.Reload(settings.Clone(), false);
                    return;
                }
                Settings s = settings.Clone();
                s.Mode = Settings.ModeGoogle;
                s.SpreadsheetId = f.NewSheet.Id;
                s.SpreadsheetTitle = f.NewSheet.Title;
                s.SpreadsheetUrl = f.NewSheet.Url;
                s.WowPath = settings.WowPath;
                CommitGoogle(s, true);
                ShowNote(MakeNote(Tr._("Таблица создана"), Tr.F("«{0}» — открываю в браузере. Отметки этой недели уйдут в неё сами.", f.NewSheet.Title), false));
                try
                {
                    Browser.Open(f.NewSheet.Url);
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Браузер не открылся: {0}", ex.Message));
                }
            }
        }

        void SetAutostart(bool enable)
        {
            try
            {
                Autostart.Set(enable);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось изменить автозапуск: {0}", ex.Message));
                MessageBox.Show(Tr.F("Не удалось изменить автозапуск: {0}", ex.Message), Program.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (enable && Program.RunningFromTemp)
            {
                ShowNote(MakeNote(Tr._("Программа запущена прямо из архива"),
                    Tr._("Распакуйте архив в обычную папку (например, «Документы») и запустите MountSheetSync.exe оттуда — иначе автозапуск не сработает."),
                    true));
            }
        }

        void ToggleAutostart()
        {
            SetAutostart(!Autostart.IsEnabled());
        }

        // ------------------------------------------------------------------
        // Обновления
        // ------------------------------------------------------------------

        void ToggleUpdates()
        {
            settings.CheckUpdates = !settings.CheckUpdates;
            SaveSettings();
            if (settings.CheckUpdates)
            {
                CheckForUpdate(true);
            }
            else
            {
                available = null;
            }
        }

        void SaveSettings()
        {
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
        }

        /// <summary>Фоном спросить GitHub. manual — по галочке в меню: ответ показывается всегда.</summary>
        void CheckForUpdate(bool manual)
        {
            if (!manual && !settings.CheckUpdates)
            {
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                string error = null;
                try
                {
                    info = Updater.Check();
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                Post(delegate { UpdateChecked(info, error, manual); });
            });
        }

        void UpdateChecked(UpdateInfo info, string error, bool manual)
        {
            if (error != null)
            {
                Log.Write(Tr.L("Проверить обновления не получилось: {0}", error));
                return;
            }
            available = info;
            if (info == null)
            {
                if (manual)
                {
                    ShowNote(MakeNote(Tr._("Обновлений нет"), Tr.F("У вас последняя версия: {0}.", Program.Version), false));
                }
                return;
            }
            Log.Write(Tr.L("Есть новая версия: {0} ({1}).", info.Version, info.PageUrl));
            if (manual || settings.UpdateNotified != info.Version)
            {
                settings.UpdateNotified = info.Version;
                SaveSettings();
                Note n = MakeNote(Tr.F("Вышла версия {0}", info.Version),
                    Tr._("Нажмите, чтобы обновить, — или меню значка → «Обновить до версии…»."), false);
                n.IsUpdate = true;
                ShowNote(n);
            }
        }

        void StartUpdate()
        {
            UpdateInfo info = available;
            if (info == null || updating)
            {
                return;
            }
            string notes = Updater.ShortNotes(info.Notes);
            DialogResult answer = MessageBox.Show(
                Tr.F("Обновить Mount Sheet Sync до версии {0}?", info.Version)
                + (notes.Length > 0 ? "\n\n" + notes : "")
                + "\n\n" + Tr._("Программа скачает новую версию с GitHub, проверит контрольную сумму и перезапустится. Аддон, поставленный из архива, обновится тоже (в игре — /reload); аддон из CurseForge обновляет приложение CurseForge."),
                Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                return;
            }
            updating = true;
            Log.Write(Tr.L("Обновление до {0}…", info.Version));
            Settings s = settings.Clone();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateResult result = null;
                string error = null;
                try
                {
                    result = Updater.Install(info, WowLocator.FindRetailDirs(s.WowPath), delegate(string text) { Log.Write(text); });
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Ошибка обновления: {0}", ex));
                    error = ex.Message;
                }
                Post(delegate { UpdateInstalled(info, result, error); });
            });
        }

        void UpdateInstalled(UpdateInfo info, UpdateResult result, string error)
        {
            updating = false;
            if (error != null)
            {
                Log.Write(Tr.L("Обновление не установлено: {0}", error));
                DialogResult answer = MessageBox.Show(Tr.F("Обновление не установлено: {0}.", error) + "\n\n"
                    + Tr._("Открыть страницу выпуска, чтобы скачать новую версию вручную?"),
                    Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                {
                    Browser.Open(info.PageUrl);
                }
                return;
            }
            Program.LogUpdate(info, result);
            try
            {
                Updater.Restart(Program.Version);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось перезапустить программу: {0}", ex.Message));
                ShowNote(MakeNote(Tr.F("Версия {0} установлена", info.Version), Tr._("Перезапустите программу, чтобы она заработала."), false));
                return;
            }
            ExitApp();
        }

        void ToggleNotifications()
        {
            settings.Notifications = !settings.Notifications;
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
            if (!settings.Notifications)
            {
                ShowNote(MakeNote(Tr._("Уведомления выключены"),
                    Tr._("Что отправлено, видно в меню значка: «Последние события» и «Журнал работы»."), false));
            }
        }

        void OpenLog()
        {
            try
            {
                if (!File.Exists(Log.FilePath))
                {
                    Log.Write(Tr.L("Журнал создан."));
                }
                Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Tr.F("Не удалось открыть журнал: {0}\n\nОн лежит здесь: {1}", ex.Message, Log.FilePath),
                    Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        string SheetUrl()
        {
            if (last.SpreadsheetUrl.Length > 0)
            {
                return last.SpreadsheetUrl;
            }
            return settings.UseGoogle ? settings.SpreadsheetUrl : "";
        }

        void OpenSheet()
        {
            string url = SheetUrl();
            if (url.Length == 0)
            {
                return;
            }
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Tr.F("Не удалось открыть браузер: {0}", ex.Message), Program.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void OnSecondInstance()
        {
            if (!settings.IsConfigured)
            {
                OpenSettings(true);
                return;
            }
            ShowNote(MakeNote(Tr._("Mount Sheet Sync уже запущен"),
                Tr._("Его значок — в трее у часов (если не видно, нажмите ^). Меню — по правой кнопке мыши."), false));
        }

        void ExitApp()
        {
            if (exiting)
            {
                return;
            }
            exiting = true;
            Log.Write(Tr.L("Выход."));
            if (updateTimer != null)
            {
                updateTimer.Dispose();
            }
            worker.Stop();
            tray.Visible = false;
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true;
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                okIcon.Dispose();
                warnIcon.Dispose();
                invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Автозапуск: значение в HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "Mount Sheet Sync";

        static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --autostart"; }
        }

        static string ReadRun()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k == null ? null : k.GetValue(ValueName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsEnabled()
        {
            if (string.IsNullOrEmpty(ReadRun()))
            {
                return false;
            }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    byte[] data = k == null ? null : k.GetValue(ValueName) as byte[];
                    // нечётный первый байт — выключено в «Диспетчер задач → Автозагрузка»
                    if (data != null && data.Length > 0 && (data[0] & 1) == 1)
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        public static void Set(bool enable)
        {
            if (enable)
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    k.SetValue(ValueName, Command, RegistryValueKind.String);
                }
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    {
                        if (k != null)
                        {
                            k.DeleteValue(ValueName, false);
                        }
                    }
                }
                catch (Exception)
                {
                }
                Log.Write(Tr.L("Автозапуск включён: {0}", Command));
            }
            else
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null)
                    {
                        k.DeleteValue(ValueName, false);
                    }
                }
                Log.Write(Tr.L("Автозапуск выключен."));
            }
        }

        /// <summary>Если программу переложили в другую папку — поправить путь в автозапуске.</summary>
        public static void Repair()
        {
            string current = ReadRun();
            if (string.IsNullOrEmpty(current) || current == Command || Program.RunningFromTemp)
            {
                return;
            }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null)
                    {
                        k.SetValue(ValueName, Command, RegistryValueKind.String);
                        Log.Write(Tr.L("Путь в автозапуске обновлён: {0}", Command));
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }

    static class AppIcons
    {
        public static Icon Tray(bool warning)
        {
            return Load(warning ? "MountSheetSync.warn.ico" : "MountSheetSync.app.ico", SystemInformation.SmallIconSize);
        }

        public static Icon Window()
        {
            return Load("MountSheetSync.app.ico", SystemInformation.IconSize);
        }

        static Icon Load(string name, Size size)
        {
            try
            {
                using (Stream s = typeof(AppIcons).Assembly.GetManifestResourceStream(name))
                {
                    if (s != null)
                    {
                        return new Icon(s, size);
                    }
                }
            }
            catch (Exception)
            {
            }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
