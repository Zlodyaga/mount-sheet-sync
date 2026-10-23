// «Что фармить…»: для каждого персонажа — галочки «Фармлю» по дополнениям, рейдам и подземельям.
// Снятая галочка — строки нет в «Осталось» (в игре и в меню значка) и в «Осталось попыток выбить».

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MountSheetSync
{
    sealed class FarmForm : Form
    {
        /// <summary>Строка листа персонажа: где она и как её узнать при сохранении.</summary>
        sealed class RowRef
        {
            public string Sheet = "";
            public int Row;
            public string Mount = "";
            public string Boss = "";
            public string Diff = "";
            public bool Original;
        }

        readonly Settings settings;
        readonly ComboBox charBox = new ComboBox();
        readonly TreeView tree = new TreeView();
        readonly Label statusLabel;
        readonly Button allButton = new Button();
        readonly Button noneButton = new Button();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Dictionary<RowRef, bool> wanted = new Dictionary<RowRef, bool>();
        List<CharacterInTable> chars = new List<CharacterInTable>();
        GoogleBook book;
        bool busy;
        bool updating;

        /// <summary>Сколько строк поменялось.</summary>
        public int Changed { get; private set; }

        public FarmForm(Settings settings)
        {
            this.settings = settings;
            UiStyle.Dialog(this, Tr._("что фармить"));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int unit = Font.Height;
            int width = unit * 34;
            Padding = new Padding(0, 0, unit, unit);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Location = new Point(unit, unit);
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 1;
            table.Controls.Add(UiStyle.Text(Tr._("Галочки — что этот персонаж фармит каждую неделю. Снимите — строки не будет в «Осталось» (в игре и в меню значка) и в «Осталось попыток выбить». Отметки убийств ставятся как обычно."),
                width, SystemColors.ControlText));

            FlowLayoutPanel top = new FlowLayoutPanel();
            top.AutoSize = true;
            top.Margin = new Padding(0, unit / 2, 0, 0);
            Label who = UiStyle.Text(Tr._("Персонаж:"), width, SystemColors.ControlText);
            who.Margin = new Padding(3, 6, 3, 0);
            top.Controls.Add(who);
            charBox.DropDownStyle = ComboBoxStyle.DropDownList;
            charBox.Width = unit * 14;
            top.Controls.Add(charBox);
            UiStyle.Button(allButton, Tr._("Отметить всё"), unit);
            UiStyle.Button(noneButton, Tr._("Снять всё"), unit);
            top.Controls.Add(allButton);
            top.Controls.Add(noneButton);
            table.Controls.Add(top);

            tree.CheckBoxes = true;
            tree.Width = width;
            tree.Height = unit * 24;
            tree.HideSelection = false;
            tree.Font = Font;
            table.Controls.Add(tree);

            statusLabel = UiStyle.Text(Tr._("Читаю таблицу…"), width, SystemColors.GrayText);
            statusLabel.MinimumSize = new Size(width, unit * 2);
            table.Controls.Add(statusLabel);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            UiStyle.Button(cancelButton, Tr._("Отмена"), unit);
            UiStyle.Button(okButton, Tr._("Сохранить"), unit);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            table.Controls.Add(buttons);
            Controls.Add(table);

            AcceptButton = okButton;
            CancelButton = cancelButton;
            okButton.Enabled = false;
            okButton.Click += delegate { OnSave(); };
            cancelButton.Click += delegate
            {
                if (!busy)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            charBox.SelectedIndexChanged += delegate { ShowCharacter(); };
            allButton.Click += delegate { SetAll(true); };
            noneButton.Click += delegate { SetAll(false); };
            tree.AfterCheck += OnAfterCheck;
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
            SetBusy(true);
            string id = settings.SpreadsheetId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                GoogleBook b = null;
                string error = null;
                try
                {
                    b = GoogleBook.Load(id);
                    GoogleSheetService.Upgrade(b);   // в таблице 2.0/2.1 колонки «Фармлю» ещё нет
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Что фармить: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate { Loaded(b, error); });
            });
        }

        void Loaded(GoogleBook b, string error)
        {
            SetBusy(false);
            if (error != null)
            {
                ShowStatus(Tr.F("Не получилось: {0}.", error), UiStyle.Bad);
                return;
            }
            book = b;
            chars = SheetCreator.TableCharacters(b).Where(c => c.RaidSheet != null || c.DungeonSheet != null).ToList();
            foreach (CharacterInTable c in chars)
            {
                charBox.Items.Add(c.Name);
            }
            if (chars.Count == 0)
            {
                ShowStatus(Tr._("В таблице нет листов персонажей."), UiStyle.Warn);
                return;
            }
            ShowStatus("", SystemColors.GrayText);
            okButton.Enabled = true;
            charBox.SelectedIndex = 0;
        }

        static string Cell(object[] row, int c)
        {
            return c >= 0 && c < row.Length ? SheetLogic.CellText(row[c]) : "";
        }

        void ShowCharacter()
        {
            int i = charBox.SelectedIndex;
            if (i < 0 || i >= chars.Count)
            {
                return;
            }
            updating = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            CharacterInTable c = chars[i];
            foreach (SheetEntry e in new[] { c.RaidSheet, c.DungeonSheet })
            {
                if (e == null || e.Data.Count < 2)
                {
                    continue;
                }
                object[] header = e.Data[0];
                int cFarm = SheetLogic.FindCol(header, "^фармлю", -1);
                if (cFarm < 0)
                {
                    continue;
                }
                int cAddon = SheetLogic.FindCol(header, "^аддон", 1);
                int cPlace = SheetLogic.FindCol(header, "^рейд|подземел", 2);
                int cMount = SheetLogic.FindCol(header, "маунт", 3);
                int cBoss = SheetLogic.FindCol(header, "босс", 4);
                int cDiff = SheetLogic.FindCol(header, "сложност|режим", 5);
                bool raid = SheetLogic.SheetOwner(e.Name, SheetLogic.RaidPrefix) != null;
                TreeNode root = tree.Nodes.Add(raid ? Tr._("Рейды") : Tr._("Подземелья"));
                for (int r = 1; r < e.Data.Count; r++)
                {
                    object[] row = e.Data[r];
                    string mount = Cell(row, cMount);
                    string boss = Cell(row, cBoss);
                    if (mount.Length == 0 && boss.Length == 0)
                    {
                        continue;
                    }
                    string addon = Cell(row, cAddon);
                    string place = Cell(row, cPlace);
                    TreeNode a = Child(root, addon.Length > 0 ? addon : Tr._("Без дополнения"));
                    TreeNode p = Child(a, place.Length > 0 ? place : Tr._("Без названия"));
                    RowRef rr = Ref(e.Name, r, mount, boss, Cell(row, cDiff), !SheetLogic.IsUnticked(row[cFarm]));
                    string diff = Cell(row, cDiff);
                    TreeNode leaf = p.Nodes.Add((mount.Length > 0 ? mount : "?") + " — " + boss + (diff.Length > 0 ? " (" + diff + ")" : ""));
                    leaf.Tag = rr;
                    bool on;
                    leaf.Checked = wanted.TryGetValue(rr, out on) ? on : rr.Original;
                    leaf.ForeColor = leaf.Checked ? SystemColors.WindowText : SystemColors.GrayText;
                }
                root.Expand();
            }
            foreach (TreeNode n in tree.Nodes)
            {
                Refresh(n);
            }
            tree.EndUpdate();
            updating = false;
            UpdateStatus();
        }

        readonly List<RowRef> refs = new List<RowRef>();

        RowRef Ref(string sheet, int row, string mount, string boss, string diff, bool original)
        {
            RowRef found = refs.FirstOrDefault(x => x.Sheet == sheet && x.Row == row);
            if (found == null)
            {
                found = new RowRef { Sheet = sheet, Row = row, Mount = mount, Boss = boss, Diff = diff, Original = original };
                refs.Add(found);
            }
            return found;
        }

        static TreeNode Child(TreeNode parent, string text)
        {
            foreach (TreeNode n in parent.Nodes)
            {
                if (n.Tag == null && (string)n.Name == text)
                {
                    return n;
                }
            }
            TreeNode c = parent.Nodes.Add(text, text);
            return c;
        }

        /// <summary>Галочка группы — по её строкам; «частично» в названии, если отмечены не все.</summary>
        static bool? Refresh(TreeNode node)
        {
            if (node.Tag is RowRef)
            {
                return node.Checked;
            }
            int on = 0, off = 0;
            foreach (TreeNode c in node.Nodes)
            {
                bool? s = Refresh(c);
                if (s == true)
                {
                    on++;
                }
                else if (s == false)
                {
                    off++;
                }
                else
                {
                    on++;
                    off++;
                }
            }
            string name = node.Name.Length > 0 ? node.Name : node.Text.Replace(" — " + Tr._("частично"), "");
            node.Name = name;
            node.Checked = on > 0;
            node.Text = on > 0 && off > 0 ? name + " — " + Tr._("частично") : name;
            node.ForeColor = on == 0 ? SystemColors.GrayText : SystemColors.WindowText;
            if (on > 0 && off > 0)
            {
                return null;
            }
            return on > 0;
        }

        void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (updating)
            {
                return;
            }
            updating = true;
            SetSubtree(e.Node, e.Node.Checked);
            TreeNode top = e.Node;
            while (top.Parent != null)
            {
                top = top.Parent;
            }
            Refresh(top);
            updating = false;
            UpdateStatus();
        }

        void SetSubtree(TreeNode node, bool on)
        {
            node.Checked = on;
            RowRef rr = node.Tag as RowRef;
            if (rr != null)
            {
                wanted[rr] = on;
                node.ForeColor = on ? SystemColors.WindowText : SystemColors.GrayText;
            }
            foreach (TreeNode c in node.Nodes)
            {
                SetSubtree(c, on);
            }
        }

        void SetAll(bool on)
        {
            updating = true;
            foreach (TreeNode n in tree.Nodes)
            {
                SetSubtree(n, on);
                Refresh(n);
            }
            updating = false;
            UpdateStatus();
        }

        List<KeyValuePair<RowRef, bool>> Changes()
        {
            return wanted.Where(kv => kv.Value != kv.Key.Original).ToList();
        }

        void UpdateStatus()
        {
            int n = Changes().Count;
            ShowStatus(n == 0 ? "" : Tr.F("Изменено строк: {0} — нажмите «Сохранить».", n), SystemColors.ControlText);
        }

        void OnSave()
        {
            if (busy)
            {
                return;
            }
            List<KeyValuePair<RowRef, bool>> changes = Changes();
            if (changes.Count == 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            SetBusy(true);
            ShowStatus(Tr._("Сохраняю…"), SystemColors.ControlText);
            string id = settings.SpreadsheetId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                int written = 0;
                try
                {
                    written = Save(id, changes);
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Что фармить: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate
                {
                    SetBusy(false);
                    if (error != null)
                    {
                        ShowStatus(Tr.F("Не получилось: {0}.", error), UiStyle.Bad);
                        return;
                    }
                    Changed = written;
                    DialogResult = DialogResult.OK;
                    Close();
                });
            });
        }

        /// <summary>
        /// Записать галочки. Таблицу читаем заново: пока окно было открыто, строки могли сдвинуться
        /// (собранные маунты уходят с листов) — строку узнаём по маунту, боссу и сложности.
        /// </summary>
        static int Save(string spreadsheetId, List<KeyValuePair<RowRef, bool>> changes)
        {
            GoogleBook fresh = GoogleBook.Load(spreadsheetId);
            int written = 0;
            foreach (KeyValuePair<RowRef, bool> kv in changes)
            {
                SheetEntry e = fresh.Entries.FirstOrDefault(x => x.Name == kv.Key.Sheet);
                if (e == null || e.Data.Count < 2)
                {
                    continue;
                }
                object[] header = e.Data[0];
                int cFarm = SheetLogic.FindCol(header, "^фармлю", -1);
                int cMount = SheetLogic.FindCol(header, "маунт", 3);
                int cBoss = SheetLogic.FindCol(header, "босс", 4);
                int cDiff = SheetLogic.FindCol(header, "сложност|режим", 5);
                if (cFarm < 0)
                {
                    continue;
                }
                Func<int, bool> same = r => SheetLogic.Norm(Cell(e.Data[r], cMount)) == SheetLogic.Norm(kv.Key.Mount)
                    && SheetLogic.Norm(Cell(e.Data[r], cBoss)) == SheetLogic.Norm(kv.Key.Boss)
                    && SheetLogic.Norm(Cell(e.Data[r], cDiff)) == SheetLogic.Norm(kv.Key.Diff);
                int row = kv.Key.Row < e.Data.Count && same(kv.Key.Row) ? kv.Key.Row
                    : Enumerable.Range(1, e.Data.Count - 1).FirstOrDefault(same);
                if (row <= 0 || e.Formulas.Contains(SheetLogic.Key(row, cFarm)))
                {
                    continue;
                }
                fresh.Set(e, row, cFarm, kv.Value);
                written++;
            }
            fresh.Flush();
            Log.Write(Tr.L("Что фармить: изменено строк — {0}.", written));
            return written;
        }

        void SetBusy(bool on)
        {
            busy = on;
            okButton.Enabled = !on && book != null;
            cancelButton.Enabled = !on;
            tree.Enabled = !on;
            charBox.Enabled = !on;
            allButton.Enabled = !on;
            noneButton.Enabled = !on;
            UseWaitCursor = on;
        }

        void ShowStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }
    }
}
