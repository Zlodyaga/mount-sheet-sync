// Окна для работы с Google: вход (ожидание браузера) и выбор персонажей для новой таблицы
// («Создать таблицу») или для готовой («Добавить персонажей»).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MountSheetSync
{
    static class UiStyle
    {
        public static readonly Color Good = Color.FromArgb(0x1B, 0x7F, 0x3B);
        public static readonly Color Warn = Color.FromArgb(0xA8, 0x5A, 0x00);
        public static readonly Color Bad = Color.FromArgb(0xC0, 0x1C, 0x28);

        public static void Dialog(Form f, string title)
        {
            f.Text = Program.Title + " — " + title;
            f.Font = SystemFonts.MessageBoxFont;
            f.AutoScaleMode = AutoScaleMode.None;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = true;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Icon = AppIcons.Window();
        }

        public static Label Text(string text, int maxWidth, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(maxWidth, 0);
            label.ForeColor = color;
            label.Margin = new Padding(3, 3, 3, 0);
            return label;
        }

        public static void Button(Button button, string text, int unit)
        {
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(unit * 6, 0);
            button.Padding = new Padding(unit / 3, 0, unit / 3, 0);
        }

        public static void RunOnUi(Control c, Action action)
        {
            if (c.IsDisposed || !c.IsHandleCreated)
            {
                return;
            }
            try
            {
                c.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public static void BringToFront(Form f)
        {
            f.TopMost = true;
            f.Activate();
            f.TopMost = false;
        }
    }

    /// <summary>«Войти через Google»: открывает браузер и ждёт, пока человек войдёт. Результат — Token.</summary>
    sealed class SignInForm : Form
    {
        readonly Label info;
        readonly Label statusLabel;
        readonly Button cancelButton = new Button();
        readonly Button againButton = new Button();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        string lastUrl;

        public GoogleToken Token { get; private set; }

        public SignInForm()
        {
            UiStyle.Dialog(this, Tr._("вход через Google"));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int unit = Font.Height;
            int width = unit * 30;
            Padding = new Padding(unit);
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Location = new Point(unit, unit);
            Label head = UiStyle.Text(Tr._("В браузере открылась страница Google."), width, SystemColors.ControlText);
            head.Font = new Font(Font, FontStyle.Bold);
            panel.Controls.Add(head);
            info = UiStyle.Text(Tr._("Войдите в свой аккаунт и разрешите доступ. На странице разрешений поставьте все галочки (или «Выбрать все») — особенно у пункта про файлы Google Диска: без него программа не сможет вести таблицу. Программа увидит только таблицы, которые создаст сама, — остальные файлы на Google Диске ей недоступны.")
                + "\n\n" + Tr._("Если Google напишет, что не проверял это приложение, — это нормально для новой программы: нажмите «Продолжить».")
                + "\n\n" + Tr._("Пароль вводится только на странице Google, программа его не видит."), width, SystemColors.ControlText);
            panel.Controls.Add(info);
            statusLabel = UiStyle.Text("", width, Color.FromArgb(176, 0, 32));
            statusLabel.Visible = false;
            panel.Controls.Add(statusLabel);
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.Margin = new Padding(0, unit, 0, 0);
            UiStyle.Button(againButton, Tr._("Открыть страницу ещё раз"), unit);
            UiStyle.Button(cancelButton, Tr._("Отмена"), unit);
            buttons.Controls.Add(againButton);
            buttons.Controls.Add(cancelButton);
            panel.Controls.Add(buttons);
            Controls.Add(panel);
            CancelButton = cancelButton;
            cancelButton.Click += delegate { cancel.Cancel(); };
            againButton.Click += delegate
            {
                if (lastUrl != null)
                {
                    // через программу: она начнёт новую попытку, и старая страница Google не помешает
                    OpenBrowser(GoogleAuth.RetryUrlFor(lastUrl));
                }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (DialogResult != DialogResult.OK)
                {
                    cancel.Cancel();
                }
            };
        }

        void OpenBrowser(string url)
        {
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Tr.F("Браузер не открылся ({0}). Скопируйте адрес вручную:", ex.Message) + "\n\n" + url,
                    Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UiStyle.BringToFront(this);
            ThreadPool.QueueUserWorkItem(delegate
            {
                GoogleToken token = null;
                string error = null;
                try
                {
                    token = GoogleAuth.SignIn(delegate(string url)
                    {
                        lastUrl = url;
                        UiStyle.RunOnUi(this, delegate { OpenBrowser(url); });
                    }, cancel.Token, delegate(string text)
                    {
                        UiStyle.RunOnUi(this, delegate
                        {
                            statusLabel.Text = text;
                            statusLabel.Visible = true;
                            UiStyle.BringToFront(this);
                        });
                    });
                    GoogleSession.Set(token);
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Вход в Google: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate
                {
                    if (token != null)
                    {
                        Token = token;
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }
                    if (cancel.IsCancellationRequested)
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                        return;
                    }
                    Log.Write(Tr.L("Вход в Google не выполнен: {0}", error));
                    UiStyle.BringToFront(this);
                    MessageBox.Show(this, Tr.F("Вход не выполнен: {0}.", error), Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.Cancel;
                    Close();
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancel.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Персонажи: для новой таблицы (Create) или какие листы у кого в готовой (Manage).</summary>
    sealed class CharactersForm : Form
    {
        readonly bool addMode;
        readonly Settings settings;
        readonly TextBox titleBox = new TextBox();
        readonly DataGridView grid = new DataGridView();
        readonly TextBox nameBox = new TextBox();
        readonly Button addNameButton = new Button();
        readonly Label collectionLabel;
        readonly Label statusLabel;
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        GameSnapshot game;
        bool busy;
        GoogleBook loadedBook;
        List<CharacterInTable> inTable = new List<CharacterInTable>();

        /// <summary>Создана таблица (Create).</summary>
        public CreatedSheet NewSheet { get; private set; }

        /// <summary>Что поменялось в таблице (Manage).</summary>
        public CharacterChanges Changes { get; private set; }

        public CharactersForm(Settings settings, bool addMode)
        {
            this.settings = settings;
            this.addMode = addMode;
            UiStyle.Dialog(this, addMode ? Tr._("персонажи в таблице") : Tr._("новая таблица"));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int unit = Font.Height;
            int width = unit * 36;
            Padding = new Padding(0, 0, unit, unit);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Location = new Point(unit, unit);
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 1;

            if (!addMode)
            {
                table.Controls.Add(UiStyle.Text(Tr._("Название таблицы на Google Диске:"), width, SystemColors.ControlText));
                titleBox.Width = unit * 20;
                titleBox.Text = Tr._("Маунты WoW");
                table.Controls.Add(titleBox);
            }
            Label charsLabel = UiStyle.Text(addMode
                ? Tr._("Галочки — какие листы есть у персонажа. Поставьте — лист появится, снимите — лист удалится; снимите все — персонаж уйдёт из таблицы. Ниже в списке — персонажи из аддона, которых в таблице ещё нет:")
                : Tr._("Персонажи (их запомнил аддон). Галочки — для каких листов завести персонажа:"), width, SystemColors.ControlText);
            charsLabel.Margin = new Padding(3, unit / 2, 3, 3);
            table.Controls.Add(charsLabel);

            grid.Width = width;
            grid.Height = unit * 14;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.BackgroundColor = SystemColors.Window;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.DefaultCellStyle.Font = Font;
            grid.ColumnHeadersDefaultCellStyle.Font = Font;
            grid.RowTemplate.Height = unit + unit / 2;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = Tr._("Персонаж"), ReadOnly = true, FillWeight = 28 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "realm", HeaderText = Tr._("Сервер"), ReadOnly = true, FillWeight = 26 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "info", HeaderText = Tr._("Класс"), ReadOnly = true, FillWeight = 24 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "raids", HeaderText = Tr._("Рейды"), FillWeight = 13 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "dungeons", HeaderText = Tr._("Подземелья"), FillWeight = 18 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "world", HeaderText = Tr._("Мир. боссы"), FillWeight = 17 });
            grid.Width = width + unit * 4;
            table.Controls.Add(grid);

            FlowLayoutPanel manual = new FlowLayoutPanel();
            manual.AutoSize = true;
            manual.Margin = new Padding(0, 3, 0, 0);
            Label manualLabel = UiStyle.Text(Tr._("Нет в списке? Имя:"), width, SystemColors.ControlText);
            manualLabel.Margin = new Padding(3, 6, 3, 0);
            manual.Controls.Add(manualLabel);
            nameBox.Width = unit * 12;
            manual.Controls.Add(nameBox);
            UiStyle.Button(addNameButton, Tr._("Добавить в список"), unit);
            manual.Controls.Add(addNameButton);
            table.Controls.Add(manual);

            collectionLabel = UiStyle.Text(Tr._("Читаю файлы аддона…"), width, SystemColors.GrayText);
            collectionLabel.Margin = new Padding(3, unit / 2, 3, 3);
            table.Controls.Add(collectionLabel);

            statusLabel = UiStyle.Text("", width, SystemColors.ControlText);
            statusLabel.MinimumSize = new Size(width, unit * 2);
            table.Controls.Add(statusLabel);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            UiStyle.Button(cancelButton, Tr._("Отмена"), unit);
            UiStyle.Button(okButton, addMode ? Tr._("Сохранить") : Tr._("Создать таблицу"), unit);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            table.Controls.Add(buttons);
            Controls.Add(table);

            AcceptButton = okButton;
            CancelButton = cancelButton;
            okButton.Enabled = false;
            okButton.Click += delegate { OnOk(); };
            cancelButton.Click += delegate
            {
                if (!busy)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            addNameButton.Click += delegate { AddManual(); };
            nameBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddManual();
                }
            };
            nameBox.Enter += delegate { AcceptButton = null; };
            nameBox.Leave += delegate { AcceptButton = okButton; };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy)
                {
                    e.Cancel = true;
                }
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UiStyle.BringToFront(this);
            SetBusy(true, addMode ? Tr._("Читаю таблицу…") : "");
            Settings s = settings.Clone();
            ThreadPool.QueueUserWorkItem(delegate
            {
                GameSnapshot snap = null;
                GoogleBook book = null;
                string error = null;
                try
                {
                    snap = Program.ReadGame(s);
                    if (addMode)
                    {
                        book = GoogleBook.Load(s.SpreadsheetId);
                    }
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Список персонажей: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate { Loaded(snap, book, error); });
            });
        }

        void Loaded(GameSnapshot snap, GoogleBook book, string error)
        {
            SetBusy(false, "");
            if (error != null)
            {
                ShowStatus(Tr.F("Не получилось: {0}", error), UiStyle.Bad);
                okButton.Enabled = false;
                return;
            }
            game = snap;
            if (book != null)
            {
                loadedBook = book;
                inTable = SheetCreator.TableCharacters(book);
                foreach (CharacterInTable c in inTable)
                {
                    GameCharacter g = game.Characters.FirstOrDefault(x => SheetLogic.Norm(x.Name) == SheetLogic.Norm(SheetLogic.StripNote(c.Name)));
                    string info = g == null ? Tr._("есть в таблице") : (g.Class + (g.Level > 0 ? ", " + g.Level : "")).Trim(',', ' ');
                    grid.Rows.Add(c.Name, g != null ? g.Realm : "", info, c.RaidSheet != null, c.DungeonSheet != null, c.WorldColumn >= 0);
                }
            }
            foreach (GameCharacter c in game.Characters)
            {
                if (addMode && inTable.Any(t => SheetLogic.Norm(SheetLogic.StripNote(t.Name)) == SheetLogic.Norm(c.Name)))
                {
                    continue;
                }
                string info = c.FromKills ? Tr._("из журнала убийств") : (c.Class + (c.Level > 0 ? ", " + c.Level : "")).Trim(',', ' ');
                int i = grid.Rows.Add(c.Name, c.Realm, info, !addMode, !addMode, !addMode);
                if (addMode)
                {
                    grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;   // ещё не в таблице
                }
            }
            if (game.CollectionUpdated > 0)
            {
                collectionLabel.Text = (game.JournalSize > 0
                        ? Tr.F("Собранных маунтов в игре: {0} из {1} (на {2}).", game.CollectedMounts.Count, game.JournalSize, TextUtil.When(Epoch.ToLocal(game.CollectionUpdated)))
                        : Tr.F("Собранных маунтов в игре: {0} (на {1}).", game.CollectedMounts.Count, TextUtil.When(Epoch.ToLocal(game.CollectionUpdated))))
                    + (addMode ? "" : " " + Tr._("У них «Собрано?» сразу будет «Да»."));
                collectionLabel.ForeColor = SystemColors.GrayText;
            }
            else
            {
                collectionLabel.Text = Tr._("Аддон ещё не прочитал коллекцию маунтов (нужна версия аддона 2.0 и один вход в игру).")
                    + (addMode ? "" : " " + Tr._("Таблицу можно создать и так — «Собрано?» будет «Нет» и станет «Да» по мере новых маунтов."));
                collectionLabel.ForeColor = UiStyle.Warn;
            }
            if (game.Characters.Count == 0)
            {
                ShowStatus(Tr._("Аддон пока не знает ни одного персонажа. Зайдите в игру с аддоном Mount Sheet Sync 2.0 и выйдите на выбор персонажа (или наберите /reload) — или впишите имена вручную ниже."), UiStyle.Warn);
            }
            else if (addMode && grid.Rows.Count == 0)
            {
                ShowStatus(Tr._("В таблице пока нет персонажей. Отметьте их галочками или впишите вручную ниже."), UiStyle.Warn);
            }
            okButton.Enabled = true;
        }

        void AddManual()
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                return;
            }
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (string.Equals(Convert.ToString(r.Cells["name"].Value), name, StringComparison.OrdinalIgnoreCase))
                {
                    ShowStatus(Tr.F("«{0}» уже в списке.", name), UiStyle.Warn);
                    return;
                }
            }
            grid.Rows.Add(name, "", Tr._("вписан вручную"), true, true, true);
            nameBox.Text = "";
            ShowStatus("", SystemColors.ControlText);
        }

        List<BuildCharacter> Selected()
        {
            grid.EndEdit();
            List<BuildCharacter> list = new List<BuildCharacter>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                bool raid = Convert.ToBoolean(r.Cells["raids"].Value ?? false);
                bool dungeon = Convert.ToBoolean(r.Cells["dungeons"].Value ?? false);
                bool world = Convert.ToBoolean(r.Cells["world"].Value ?? false);
                // в режиме таблицы нужны все строки: персонаж без галочек — значит, убрать его
                if (raid || dungeon || world || addMode)
                {
                    list.Add(new BuildCharacter(Convert.ToString(r.Cells["name"].Value), raid, dungeon, world));
                }
            }
            return list;
        }

        void OnOk()
        {
            if (busy)
            {
                return;
            }
            List<BuildCharacter> chars = Selected();
            if (chars.Count == 0 && !addMode)
            {
                ShowStatus(Tr._("Отметьте хотя бы одного персонажа (галочки «Рейды», «Подземелья» или «Мир. боссы»)."), UiStyle.Bad);
                return;
            }
            if (addMode)
            {
                CharacterChanges diff = SheetCreator.Diff(loadedBook, chars);
                if (!diff.Any)
                {
                    ShowStatus(Tr._("Ничего не поменялось."), SystemColors.ControlText);
                    return;
                }
                if (diff.RemovesSomething)
                {
                    List<string> lines = new List<string>();
                    lines.AddRange(diff.RemovedSheets.Select(n => "• " + Tr.F("лист «{0}»", n)));
                    lines.AddRange(diff.RemovedWorld.Select(n => "• " + Tr.F("колонка «{0}» на «Мировых боссах»", n)));
                    DialogResult answer = MessageBox.Show(this, Tr._("Будут удалены вместе с отметками:") + "\n\n" + string.Join("\n", lines.ToArray())
                        + "\n\n" + Tr._("Вернуть можно: поставить галочку снова (лист создастся заново, без старых отметок) или «Файл» → «История версий» в Google Таблицах. Удалить?"),
                        Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.OK)
                    {
                        return;
                    }
                }
            }
            BuildPlan plan = new BuildPlan();
            plan.Title = titleBox.Text.Trim().Length > 0 ? titleBox.Text.Trim() : Tr._("Маунты WoW");
            plan.Characters = chars;
            plan.CollectedMounts = game != null ? game.CollectedMounts : new List<string>();
            plan.CollectedIds = game != null ? game.CollectedIds : new HashSet<long>();
            plan.CollectionStamp = game != null ? game.CollectionUpdated : 0;
            if (!addMode)
            {
                try
                {
                    SheetBuilder.Validate(plan);
                }
                catch (ArgumentException ex)
                {
                    ShowStatus(TextUtil.Capitalize(ex.Message) + ".", UiStyle.Bad);
                    return;
                }
            }
            SetBusy(true, addMode ? Tr._("Сохраняю…") : Tr._("Создаю таблицу…"));
            string id = settings.SpreadsheetId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                CreatedSheet created = null;
                CharacterChanges changes = null;
                string error = null;
                Action<string> progress = delegate(string text) { UiStyle.RunOnUi(this, delegate { ShowStatus(text, SystemColors.ControlText); }); };
                try
                {
                    if (addMode)
                    {
                        changes = SheetCreator.UpdateCharacters(id, chars, progress);
                    }
                    else
                    {
                        created = SheetCreator.Create(plan, progress);
                    }
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Таблица: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate
                {
                    SetBusy(false, "");
                    if (error != null)
                    {
                        ShowStatus(Tr.F("Не получилось: {0}.", error), UiStyle.Bad);
                        return;
                    }
                    NewSheet = created;
                    Changes = changes;
                    DialogResult = DialogResult.OK;
                    Close();
                });
            });
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            okButton.Enabled = !on && game != null;
            cancelButton.Enabled = !on;
            grid.Enabled = !on;
            titleBox.Enabled = !on;
            nameBox.Enabled = !on;
            addNameButton.Enabled = !on;
            UseWaitCursor = on;
            if (text.Length > 0)
            {
                ShowStatus(text, SystemColors.ControlText);
            }
        }

        void ShowStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }
    }
}
