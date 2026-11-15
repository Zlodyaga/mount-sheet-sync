// Окно «Попытки»: число попыток по каждому несобранному маунту можно поправить руками (например, убийства до
// установки аддона, которых нет в статистике игры). То же из игры делает аддон 3.0.7 записью kind = "attempts".

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MountSheetSync
{
    sealed class AttemptRow
    {
        public string Mount;
        public string Boss;
        public long Id;
        public double N;
        public double Original;
    }

    /// <summary>Чтение и запись числа попыток без окна (окно и ключ --set-attempts работают через него).</summary>
    static class AttemptsEdit
    {
        /// <summary>Несобранные маунты со счётчиком попыток, как их видит аддон.</summary>
        public static List<AttemptRow> Rows(IBook book)
        {
            List<AttemptRow> rows = new List<AttemptRow>();
            HashSet<string> seen = new HashSet<string>();
            foreach (object o in SheetLogic.StatusMounts(book, new HashSet<string>()))
            {
                Dictionary<string, object> item = J.Obj(o);
                AttemptRow row = new AttemptRow();
                row.Mount = J.Str(item, "mount");
                row.Boss = J.Str(item, "boss");
                row.Id = MountIndex.IdOf(J.Get(item, "id"));
                row.N = row.Original = SheetLogic.Number(J.Get(item, "n"));
                if (seen.Add(SheetLogic.Norm(row.Mount) + "|" + SheetLogic.Norm(row.Boss)))
                {
                    rows.Add(row);
                }
            }
            return rows.OrderBy(r => r.Mount, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Записать изменённые числа; возвращает, сколько маунтов изменилось. Flush — за вызывающим.</summary>
        public static int Save(IBook book, IEnumerable<AttemptRow> changed)
        {
            int written = 0;
            foreach (AttemptRow row in changed)
            {
                if (SheetLogic.SetAttemptsFor(book, row.Mount, row.Boss, row.Id, row.N))
                {
                    written++;
                }
            }
            return written;
        }
    }

    sealed class AttemptsForm : Form
    {
        readonly Settings settings;
        readonly ListView list = new ListView();
        readonly NumericUpDown number = new NumericUpDown();
        readonly Button okButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Label statusLabel;
        List<AttemptRow> rows = new List<AttemptRow>();
        bool busy;
        bool updating;

        /// <summary>Сколько маунтов изменено (после «Сохранить»).</summary>
        public int Changed;

        public AttemptsForm(Settings settings)
        {
            this.settings = settings;
            UiStyle.Dialog(this, Tr._("попытки"));
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
            table.Controls.Add(UiStyle.Text(Tr._("Сколько попыток уже было по каждому маунту. Выберите строку и поправьте число — например, если часть убийств была до установки аддона."),
                width, SystemColors.ControlText));

            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.Width = width;
            list.Height = unit * 22;
            list.Font = Font;
            list.Columns.Add(Tr._("Маунт"), unit * 15);
            list.Columns.Add(Tr._("Босс"), unit * 15);
            list.Columns.Add(Tr._("Попыток"), unit * 4, HorizontalAlignment.Right);
            table.Controls.Add(list);

            FlowLayoutPanel edit = new FlowLayoutPanel();
            edit.AutoSize = true;
            edit.Margin = new Padding(0, unit / 2, 0, 0);
            Label label = UiStyle.Text(Tr._("Попыток у выбранного маунта:"), width, SystemColors.ControlText);
            label.Margin = new Padding(3, 6, 3, 0);
            edit.Controls.Add(label);
            number.Minimum = 0;
            number.Maximum = 100000;
            number.Width = unit * 6;
            number.Enabled = false;
            edit.Controls.Add(number);
            table.Controls.Add(edit);

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
            list.SelectedIndexChanged += delegate { ShowSelected(); };
            number.ValueChanged += delegate { OnNumber(); };
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
            Settings s = settings.Clone();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<AttemptRow> loaded = null;
                string error = null;
                try
                {
                    loaded = AttemptsEdit.Rows(s.OpenBook());
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Попытки: {0}", ex));
                    error = Tr.F("ошибка: {0}", ex.Message);
                }
                UiStyle.RunOnUi(this, delegate { Loaded(loaded, error); });
            });
        }

        void Loaded(List<AttemptRow> loaded, string error)
        {
            SetBusy(false);
            if (error != null)
            {
                ShowStatus(Tr.F("Не получилось: {0}.", error), UiStyle.Bad);
                return;
            }
            rows = loaded;
            list.BeginUpdate();
            foreach (AttemptRow row in rows)
            {
                ListViewItem item = new ListViewItem(new[] { row.Mount, row.Boss, Count(row.N) });
                item.Tag = row;
                list.Items.Add(item);
            }
            list.EndUpdate();
            ShowStatus(rows.Count == 0 ? Tr._("Счётчика попыток пока нет.") : "", SystemColors.GrayText);
        }

        static string Count(double n)
        {
            return n.ToString("0", CultureInfo.InvariantCulture);
        }

        AttemptRow Selected()
        {
            return list.SelectedItems.Count == 1 ? (AttemptRow)list.SelectedItems[0].Tag : null;
        }

        void ShowSelected()
        {
            AttemptRow row = Selected();
            updating = true;
            number.Enabled = row != null && !busy;
            number.Value = row == null ? 0 : (decimal)Math.Max(0, Math.Min(100000, row.N));
            updating = false;
        }

        void OnNumber()
        {
            AttemptRow row = Selected();
            if (updating || row == null)
            {
                return;
            }
            row.N = (double)number.Value;
            list.SelectedItems[0].SubItems[2].Text = Count(row.N);
            int n = Changes().Count;
            okButton.Enabled = !busy;
            ShowStatus(n == 0 ? "" : Tr.F("Изменено строк: {0} — нажмите «Сохранить».", n), SystemColors.ControlText);
        }

        List<AttemptRow> Changes()
        {
            return rows.Where(r => r.N != r.Original).ToList();
        }

        void SetBusy(bool on)
        {
            busy = on;
            okButton.Enabled = !on && rows.Count > 0;
            list.Enabled = !on;
            number.Enabled = !on && Selected() != null;
        }

        void ShowStatus(string text, Color color)
        {
            statusLabel.ForeColor = color;
            statusLabel.Text = text;
        }

        void OnSave()
        {
            if (busy)
            {
                return;
            }
            List<AttemptRow> changes = Changes();
            if (changes.Count == 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            SetBusy(true);
            ShowStatus(Tr._("Сохраняю…"), SystemColors.ControlText);
            Settings s = settings.Clone();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                int written = 0;
                try
                {
                    using (s.ExclusiveBook())
                    {
                        // таблицу читаем заново: пока окно было открыто, программа могла досчитать попытки
                        IStructuredBook fresh = s.OpenBook();
                        written = AttemptsEdit.Save(fresh, changes);
                        fresh.Flush();
                    }
                }
                catch (SyncException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    Log.Write(Tr.L("Попытки: {0}", ex));
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
    }
}
