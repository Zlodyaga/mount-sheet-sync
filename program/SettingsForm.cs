using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MountSheetSync
{
    /// <summary>
    /// Окно настроек. Два способа: таблица, которую создаёт программа (вход через Google), или своя таблица
    /// со скриптом Code.gs (адрес веб-приложения и ключ). Плюс папка игры и автозапуск.
    /// </summary>
    sealed class SettingsForm : Form
    {
        static readonly Color Good = UiStyle.Good;
        static readonly Color Warn = UiStyle.Warn;
        static readonly Color Bad = UiStyle.Bad;

        Settings working;
        readonly Action<Settings, bool> commit;
        readonly RadioButton googleRadio = new RadioButton();
        readonly RadioButton scriptRadio = new RadioButton();
        readonly TableLayoutPanel googlePanel = new TableLayoutPanel();
        readonly TableLayoutPanel scriptPanel = new TableLayoutPanel();
        readonly Label accountLabel = new Label();
        readonly Label sheetLabel = new Label();
        readonly Button signInButton = new Button();
        readonly Button signOutButton = new Button();
        readonly Button createButton = new Button();
        readonly Button addCharsButton = new Button();
        readonly Button openButton = new Button();
        readonly TextBox urlBox = new TextBox();
        readonly TextBox secretBox = new TextBox();
        readonly TextBox wowBox = new TextBox();
        readonly Label wowHint = new Label();
        readonly Label statusLabel = new Label();
        readonly CheckBox autostartBox = new CheckBox();
        readonly Button checkButton = new Button();
        readonly Button saveButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Button browseButton = new Button();
        string detected;
        bool detecting = true;
        bool checking;

        public Settings Result { get; private set; }

        public bool AutostartChecked
        {
            get { return autostartBox.Checked; }
        }

        /// <param name="commit">Сохранить сразу (вход, новая таблица): настройки и «таблица сменилась».</param>
        public SettingsForm(Settings settings, bool firstRun, bool autostart, Action<Settings, bool> commit)
        {
            working = settings.Clone();
            this.commit = commit;
            UiStyle.Dialog(this, Tr._("настройки"));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // Размеры — от высоты шрифта, чтобы окно одинаково выглядело при любом масштабе экрана.
            int unit = Font.Height;
            int wide = unit * 34;
            int textWidth = wide + unit * 7;
            Padding = new Padding(0, 0, unit, unit);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Location = new Point(unit, unit);
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            if (firstRun)
            {
                Label intro = UiStyle.Text(Tr._("Войдите через Google и создайте таблицу — остальное программа сделает сама."),
                    textWidth, SystemColors.ControlText);
                intro.Font = new Font(Font, FontStyle.Bold);
                intro.Margin = new Padding(3, 0, 3, unit / 2);
                AddWide(table, intro);
            }

            AddWide(table, UiStyle.Text(Tr._("Где таблица:"), textWidth, SystemColors.ControlText));
            googleRadio.Text = Tr._("Таблица, которую создаёт программа (вход через Google) — рекомендуется");
            googleRadio.AutoSize = true;
            scriptRadio.Text = Tr._("Своя таблица со скриптом Code.gs (как в версиях 1.x)");
            scriptRadio.AutoSize = true;
            AddWide(table, googleRadio);
            AddWide(table, scriptRadio);

            // ---- Google
            googlePanel.AutoSize = true;
            googlePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            googlePanel.ColumnCount = 1;
            googlePanel.Margin = new Padding(unit, unit / 3, 3, 3);
            accountLabel.AutoSize = true;
            accountLabel.MaximumSize = new Size(textWidth - unit, 0);
            accountLabel.Margin = new Padding(3, 3, 3, 0);
            googlePanel.Controls.Add(accountLabel);
            FlowLayoutPanel accountButtons = Row();
            UiStyle.Button(signInButton, Tr._("Войти через Google…"), unit);
            UiStyle.Button(signOutButton, Tr._("Выйти"), unit);
            accountButtons.Controls.Add(signInButton);
            accountButtons.Controls.Add(signOutButton);
            googlePanel.Controls.Add(accountButtons);
            sheetLabel.AutoSize = true;
            sheetLabel.MaximumSize = new Size(textWidth - unit, 0);
            sheetLabel.Margin = new Padding(3, unit / 2, 3, 0);
            googlePanel.Controls.Add(sheetLabel);
            FlowLayoutPanel sheetButtons = Row();
            UiStyle.Button(createButton, Tr._("Создать таблицу…"), unit);
            UiStyle.Button(addCharsButton, Tr._("Персонажи…"), unit);
            UiStyle.Button(openButton, Tr._("Открыть"), unit);
            sheetButtons.Controls.Add(createButton);
            sheetButtons.Controls.Add(addCharsButton);
            sheetButtons.Controls.Add(openButton);
            googlePanel.Controls.Add(sheetButtons);
            googlePanel.Controls.Add(UiStyle.Text(Tr._("Программа видит только таблицы, которые создала сама. Остальные файлы на вашем Google Диске ей недоступны."), wide, SystemColors.GrayText));
            AddWide(table, googlePanel);

            // ---- скрипт Code.gs
            scriptPanel.AutoSize = true;
            scriptPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            scriptPanel.ColumnCount = 1;
            scriptPanel.Margin = new Padding(unit, unit / 3, 3, 3);
            scriptPanel.Controls.Add(UiStyle.Text(Tr._("Адрес веб-приложения (URL, оканчивается на /exec):"), textWidth - unit, SystemColors.ControlText));
            urlBox.Width = textWidth - unit * 2;
            scriptPanel.Controls.Add(urlBox);
            scriptPanel.Controls.Add(UiStyle.Text(Tr._("Apps Script → «Развертывание» → «Новое развертывание» → скопируйте «URL веб-приложения»."),
                textWidth - unit, SystemColors.GrayText));
            Label secretLabel = UiStyle.Text(Tr._("Ключ — SECRET из Code.gs (уже вписан, менять не нужно):"), textWidth - unit, SystemColors.ControlText);
            secretLabel.Margin = new Padding(3, unit / 2, 3, 0);
            scriptPanel.Controls.Add(secretLabel);
            secretBox.Width = unit * 16;
            scriptPanel.Controls.Add(secretBox);
            AddWide(table, scriptPanel);

            Label wowLabel = UiStyle.Text(Tr._("Папка World of Warcraft (если пусто — ищется сама):"), textWidth, SystemColors.ControlText);
            wowLabel.Margin = new Padding(3, unit / 2, 3, 0);
            AddWide(table, wowLabel);
            wowBox.Width = wide;
            browseButton.Text = Tr._("Обзор…");
            browseButton.AutoSize = true;
            browseButton.Margin = new Padding(3, 2, 3, 3);
            table.Controls.Add(wowBox);
            table.Controls.Add(browseButton);
            wowHint.AutoSize = true;
            wowHint.MaximumSize = new Size(textWidth, 0);
            wowHint.ForeColor = SystemColors.GrayText;
            wowHint.Text = Tr._("Ищу папку игры…");
            AddWide(table, wowHint);

            autostartBox.Text = Tr._("Запускать вместе с Windows (значок будет в трее)");
            autostartBox.AutoSize = true;
            autostartBox.Checked = autostart;
            autostartBox.Margin = new Padding(3, unit / 2, 3, 3);
            AddWide(table, autostartBox);

            if (Program.RunningFromTemp)
            {
                AddWide(table, UiStyle.Text(Tr._("Программа запущена прямо из архива. Распакуйте его в обычную папку (например, «Документы») и запустите MountSheetSync.exe оттуда, иначе автозапуск не сработает."), textWidth, Warn));
            }

            statusLabel.AutoSize = true;
            statusLabel.MaximumSize = new Size(textWidth, 0);
            statusLabel.MinimumSize = new Size(textWidth, unit * 2);
            statusLabel.Margin = new Padding(3, unit / 2, 3, 3);
            AddWide(table, statusLabel);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0);
            UiStyle.Button(cancelButton, Tr._("Отмена"), unit);
            UiStyle.Button(saveButton, Tr._("Сохранить"), unit);
            UiStyle.Button(checkButton, Tr._("Проверить связь"), unit);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(checkButton);
            AddWide(table, buttons);

            Controls.Add(table);
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            urlBox.Text = settings.WebAppUrl;
            secretBox.Text = settings.Secret;
            wowBox.Text = settings.WowPath;
            googleRadio.Checked = settings.UseGoogle;
            scriptRadio.Checked = !settings.UseGoogle;
            UpdateMode();
            UpdateGoogle();

            googleRadio.CheckedChanged += delegate { UpdateMode(); };
            scriptRadio.CheckedChanged += delegate { UpdateMode(); };
            signInButton.Click += delegate { OnSignIn(); };
            signOutButton.Click += delegate { OnSignOut(); };
            createButton.Click += delegate { OnCharacters(false); };
            addCharsButton.Click += delegate { OnCharacters(true); };
            openButton.Click += delegate { OpenUrl(working.SpreadsheetUrl); };
            checkButton.Click += delegate { OnCheck(); };
            saveButton.Click += delegate { OnSave(); };
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            browseButton.Click += delegate { OnBrowse(); };
            wowBox.TextChanged += delegate { UpdateWowHint(); };
        }

        static FlowLayoutPanel Row()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0, 2, 0, 0);
            return p;
        }

        static void AddWide(TableLayoutPanel table, Control control)
        {
            table.Controls.Add(control);
            table.SetColumnSpan(control, 2);
        }

        bool GoogleMode
        {
            get { return googleRadio.Checked; }
        }

        void UpdateMode()
        {
            googlePanel.Visible = GoogleMode;
            scriptPanel.Visible = !GoogleMode;
            ShowStatus("", SystemColors.ControlText);
        }

        void UpdateGoogle()
        {
            bool signedIn = GoogleSession.SignedIn;
            string email = GoogleSession.Email;
            accountLabel.Text = signedIn
                ? Tr.F("Аккаунт Google: {0}", email.Length > 0 ? email : Tr._("вход выполнен"))
                : Tr._("Аккаунт Google: вход не выполнен");
            accountLabel.ForeColor = signedIn ? Good : SystemColors.ControlText;
            signInButton.Visible = !signedIn;
            signOutButton.Visible = signedIn;
            bool hasSheet = working.SpreadsheetId.Length > 0;
            sheetLabel.Text = hasSheet
                ? Tr.F("Таблица: «{0}»", working.SpreadsheetTitle.Length > 0 ? working.SpreadsheetTitle : working.SpreadsheetId)
                : Tr._("Таблица: ещё не создана");
            sheetLabel.ForeColor = hasSheet ? Good : SystemColors.ControlText;
            createButton.Enabled = signedIn;
            createButton.Text = hasSheet ? Tr._("Создать новую…") : Tr._("Создать таблицу…");
            addCharsButton.Visible = hasSheet;
            addCharsButton.Enabled = signedIn;
            openButton.Visible = hasSheet && working.SpreadsheetUrl.Length > 0;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!GoogleMode && urlBox.Text.Length == 0)
            {
                // Адрес часто уже скопирован — подставим его сами.
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Settings.CleanUrl(Clipboard.GetText());
                        if (Settings.IsWebAppUrl(clip))
                        {
                            urlBox.Text = clip;
                            ShowStatus(Tr._("Адрес вставлен из буфера обмена. Нажмите «Сохранить» (или сначала «Проверить связь»)."), Good);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string found = null;
                try
                {
                    List<string> dirs = WowLocator.FindRetailDirs("");
                    if (dirs.Count > 0)
                    {
                        found = dirs[0];
                    }
                }
                catch (Exception)
                {
                }
                UiStyle.RunOnUi(this, delegate
                {
                    detected = found;
                    detecting = false;
                    UpdateWowHint();
                });
            });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Окно открывается из трея — поднимаем его над остальными.
            UiStyle.BringToFront(this);
            if (GoogleMode && !GoogleSession.SignedIn)
            {
                signInButton.Focus();
            }
            else if (GoogleMode && working.SpreadsheetId.Length == 0)
            {
                createButton.Focus();
            }
            else if (!GoogleMode && urlBox.Text.Length == 0)
            {
                urlBox.Focus();
            }
            else
            {
                saveButton.Focus();
            }
        }

        /// <summary>Папка игры из поля (её надо знать до «Создать таблицу»). null — неверная.</summary>
        string CurrentWowPath()
        {
            string wow = wowBox.Text.Trim().Trim('"');
            if (wow.Length == 0)
            {
                return "";
            }
            return WowLocator.ResolveRetailDir(wow);
        }

        void OnSignIn()
        {
            using (SignInForm f = new SignInForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    UpdateGoogle();
                    return;
                }
            }
            // способ (скрипт или Google) меняется только кнопкой «Сохранить» или созданием таблицы
            Commit(false);
            UpdateGoogle();
            ShowStatus(working.SpreadsheetId.Length == 0
                ? Tr._("Вход выполнен. Теперь нажмите «Создать таблицу…».")
                : Tr._("Вход выполнен."), Good);
            if (working.SpreadsheetId.Length == 0)
            {
                createButton.Focus();
            }
        }

        void OnSignOut()
        {
            DialogResult answer = MessageBox.Show(this,
                Tr._("Выйти из аккаунта Google? Программа перестанет отмечать боссов в таблице, пока вы не войдёте снова. Сама таблица останется на Google Диске."), Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
            {
                return;
            }
            UseWaitCursor = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                GoogleSession.SignOut(true);
                UiStyle.RunOnUi(this, delegate
                {
                    UseWaitCursor = false;
                    Log.Write(Tr.L("Выход из аккаунта Google."));
                    Commit(false);
                    UpdateGoogle();
                    ShowStatus(Tr._("Вы вышли из аккаунта Google."), SystemColors.ControlText);
                });
            });
        }

        void OnCharacters(bool add)
        {
            string wow = CurrentWowPath();
            if (wow == null)
            {
                ShowStatus(Tr._("Сначала поправьте папку World of Warcraft ниже — по ней программа находит персонажей."), Bad);
                wowBox.Focus();
                return;
            }
            if (!add && working.SpreadsheetId.Length > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    Tr.F("Создать ещё одну таблицу? Отметки пойдут в новую, а нынешняя «{0}» останется на Google Диске как есть.", working.SpreadsheetTitle), Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK)
                {
                    return;
                }
            }
            Settings s = working.Clone();
            s.WowPath = wow;
            using (CharactersForm f = new CharactersForm(s, add))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (add)
                {
                    ShowStatus(TextUtil.Sentence(f.Changes != null ? f.Changes.Summary() : Tr._("без изменений")), Good);
                    return;
                }
                CreatedSheet sheet = f.NewSheet;
                working.Mode = Settings.ModeGoogle;
                working.WowPath = wow;
                working.SpreadsheetId = sheet.Id;
                working.SpreadsheetTitle = sheet.Title;
                working.SpreadsheetUrl = sheet.Url;
                Commit(true);
                UpdateGoogle();
                ShowStatus(Tr.F("Таблица «{0}» создана. Убийства этой недели из журнала аддона сейчас уйдут в неё.", sheet.Title), Good);
                OpenUrl(sheet.Url);
                saveButton.Focus();
            }
        }

        void Commit(bool newSheet)
        {
            if (commit != null)
            {
                Settings s = working.Clone();
                commit(s, newSheet);
            }
        }

        void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex)
            {
                ShowStatus(Tr.F("Не удалось открыть браузер: {0}", ex.Message), Warn);
            }
        }

        void UpdateWowHint()
        {
            string text = wowBox.Text.Trim().Trim('"');
            if (text.Length > 0)
            {
                string resolved = WowLocator.ResolveRetailDir(text);
                if (resolved != null)
                {
                    wowHint.Text = Tr.F("Ок, файлы аддона ищутся в {0}.", Path.Combine(resolved, "WTF"));
                    wowHint.ForeColor = Good;
                }
                else
                {
                    wowHint.Text = Tr._("В этой папке нет _retail_\\WTF\\Account. Нужна папка World of Warcraft (в ней лежит _retail_).");
                    wowHint.ForeColor = Bad;
                }
                return;
            }
            if (detecting)
            {
                wowHint.Text = Tr._("Ищу папку игры…");
                wowHint.ForeColor = SystemColors.GrayText;
            }
            else if (detected != null)
            {
                wowHint.Text = Tr.F("Нашлась сама: {0}", detected);
                wowHint.ForeColor = Good;
            }
            else
            {
                wowHint.Text = Tr._("Сама не нашлась — нажмите «Обзор…» и выберите папку World of Warcraft.");
                wowHint.ForeColor = Warn;
            }
        }

        void OnBrowse()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Tr._("Выберите папку World of Warcraft (в ней лежит папка _retail_)");
                dialog.ShowNewFolderButton = false;
                string start = wowBox.Text.Trim().Trim('"');
                if (start.Length == 0 && detected != null)
                {
                    start = detected;
                }
                try
                {
                    if (start.Length > 0 && Directory.Exists(start))
                    {
                        dialog.SelectedPath = start;
                    }
                }
                catch (Exception)
                {
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    wowBox.Text = dialog.SelectedPath;
                }
            }
        }

        static bool IsLocalTestUrl(string url)
        {
            // для проверок программы с поддельным сервером на этом же компьютере
            return (url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)
                || url.StartsWith("http://localhost:", StringComparison.Ordinal))
                && url.EndsWith("/exec", StringComparison.Ordinal);
        }

        /// <summary>Проверить поля; при успехе вернуть null и заполненные настройки.</summary>
        string Collect(out Settings result)
        {
            result = null;
            string wow = wowBox.Text.Trim().Trim('"');
            string resolved = "";
            if (wow.Length > 0)
            {
                resolved = WowLocator.ResolveRetailDir(wow);
                if (resolved == null)
                {
                    wowBox.Focus();
                    return Tr._("В указанной папке нет _retail_\\WTF\\Account. Выберите папку World of Warcraft или оставьте поле пустым.");
                }
            }
            string url = Settings.CleanUrl(urlBox.Text);
            if (url != urlBox.Text)
            {
                urlBox.Text = url;
            }
            string secret = secretBox.Text.Trim();
            if (!GoogleMode)
            {
                if (url.Length == 0)
                {
                    urlBox.Focus();
                    return Tr._("Вставьте адрес веб-приложения (Ctrl+V в поле адреса).");
                }
                if (!Settings.IsWebAppUrl(url) && !IsLocalTestUrl(url))
                {
                    urlBox.Focus();
                    if (url.EndsWith("/dev", StringComparison.OrdinalIgnoreCase))
                    {
                        return Tr._("Это тестовый адрес (/dev). Нужен адрес из «Новое развертывание» — он оканчивается на /exec.");
                    }
                    return Tr._("Не похоже на адрес веб-приложения. Скопируйте его из окна развертывания: он начинается с https://script.google.com/ и оканчивается на /exec.");
                }
                if (secret.Length == 0)
                {
                    secretBox.Focus();
                    return Tr._("Ключ не может быть пустым: он должен совпадать с SECRET в Code.gs.");
                }
            }
            result = working.Clone();
            result.Mode = GoogleMode ? Settings.ModeGoogle : Settings.ModeScript;
            result.WebAppUrl = url;
            if (secret.Length > 0)
            {
                result.Secret = secret;
            }
            result.WowPath = resolved;
            return null;
        }

        void ShowStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        void SetBusy(bool busy)
        {
            checking = busy;
            checkButton.Enabled = !busy;
            saveButton.Enabled = !busy;
            UseWaitCursor = busy;
        }

        void OnCheck()
        {
            if (checking)
            {
                return;
            }
            Settings candidate;
            string error = Collect(out candidate);
            if (error != null)
            {
                ShowStatus(error, Bad);
                return;
            }
            if (candidate.UseGoogle && !candidate.IsConfigured)
            {
                ShowStatus(candidate.MissingText + ".", Warn);
                return;
            }
            SetBusy(true);
            ShowStatus(Tr._("Проверяю связь с таблицей…"), SystemColors.ControlText);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text;
                Color color;
                try
                {
                    List<string> files = WowLocator.GetAddonFiles(WowLocator.FindRetailDirs(candidate.WowPath));
                    PingInfo info = new SyncEngine(candidate, new SentLedger(null)).Ping(files);
                    text = Tr.F("Связь есть: таблица «{0}».", info.Spreadsheet);
                    color = Good;
                    if (info.Found.Count > 0)
                    {
                        text += " " + Tr.F("Листы найдены: {0}.", string.Join(", ", info.Found));
                    }
                    if (info.Missing.Count > 0)
                    {
                        text += " " + Tr.F("Нет листов для: {0} — их отметки не сохранятся.", string.Join(", ", info.Missing))
                            + (candidate.UseGoogle ? " " + Tr._("Добавьте их: «Персонажи…».") : "");
                        color = Warn;
                    }
                    if (files.Count == 0)
                    {
                        text += " " + Tr._("Файла аддона пока нет — он появится, когда вы выйдете из игры с аддоном на выбор персонажа.");
                    }
                }
                catch (SyncException ex)
                {
                    text = Tr.F("Не получилось: {0}", ex.Message);
                    color = Bad;
                }
                catch (Exception ex)
                {
                    text = Tr.F("Ошибка: {0}", ex.Message);
                    color = Bad;
                }
                UiStyle.RunOnUi(this, delegate
                {
                    SetBusy(false);
                    ShowStatus(text, color);
                });
            });
        }

        void OnSave()
        {
            if (checking)
            {
                return;
            }
            Settings candidate;
            string error = Collect(out candidate);
            if (error != null)
            {
                ShowStatus(error, Bad);
                return;
            }
            Result = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
