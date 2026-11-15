// Переключатели и подменю значка: возврат дневной копии, автодобавление персонажей, анонимная статистика,
// уведомления. Основа меню — в TrayApp.cs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MountSheetSync
{
    sealed partial class TrayApp
    {
        /// <summary>Подменю «Вернуть данные за день»: дневные копии из «backups» (LocalBook.KeepDaily), новые первыми.</summary>
        void FillRestore()
        {
            restoreItem.DropDownItems.Clear();
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();
            try
            {
                if (settings.IsLocal)
                {
                    copies = LocalBook.DailyCopies(AppPaths.LocalBookFile);
                }
            }
            catch (Exception swallowed)
            {
                Log.Quiet("дневные копии", swallowed);
            }
            foreach (KeyValuePair<string, string> one in copies)
            {
                string copy = one.Value;
                string day = DateTime.ParseExact(one.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToShortDateString();
                restoreItem.DropDownItems.Add(Item(day, delegate { RestoreCopy(day, copy); }));
            }
            restoreItem.Visible = copies.Count > 0;
        }

        void RestoreCopy(string day, string copy)
        {
            string file = AppPaths.LocalBookFile;
            DialogResult answer = MessageBox.Show(
                Tr.F("Вернуть данные, какими они были {0}? Отметки, попытки и персонажи, добавленные позже, пропадут. Нынешние данные сохранятся рядом: {1}.",
                    day, LocalBook.BeforeRestoreFile(file)),
                Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }
            try
            {
                LocalBook.Restore(file, copy);
                Log.Write(Tr.L("Данные возвращены из копии за {0}; прежние сохранены: {1}", day, LocalBook.BeforeRestoreFile(file)));
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                {
                    throw;
                }
                Log.Write(Tr.L("Не удалось вернуть данные из копии: {0}", ex.Message));
                MessageBox.Show(Tr.F("Не удалось вернуть данные из копии: {0}", ex.Message), Program.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (worker != null)
            {
                worker.Reload(settings.Clone(), false);
            }
        }

        void ToggleAutoAdd()
        {
            settings.AutoAddCharacters = !settings.AutoAddCharacters;
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
            if (worker != null)
            {
                worker.Reload(settings.Clone(), false);
            }
        }

        /// <summary>
        /// Вопрос об анонимной статистике — один раз: и после установки, и тем, кто обновился до версии с ней. Отказ и
        /// «не ответил» — одно и то же: ничего не отправляется. Потом — только галочка в меню.
        /// </summary>
        void AskStats()
        {
            DialogResult answer = MessageBox.Show(
                Tr._("Помочь автору понять, чем в Mount Sheet Sync пользуются?\n\nРаз в сутки программа будет отправлять автору короткое сообщение: версии программы, аддона и Windows, язык, где хранятся данные, сколько персонажей (примерно), сколько раз открывали вкладки и ставили метки, и случайный номер установки. Ни имён персонажей, ни серверов, ни данных таблицы в нём нет.\n\nТочный текст сообщения — «Что отправляется…» в меню значка; там же это выключается.\n\nОтправлять?"),
                Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            SetStats(answer == DialogResult.Yes);
        }

        void SetStats(bool on)
        {
            Stats.Apply(settings, on);
            try
            {
                settings.Save(AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось сохранить настройки: {0}", ex.Message));
            }
            Log.Write(on ? Tr.L("Анонимная статистика включена.") : Tr.L("Анонимная статистика выключена."));
            if (worker != null)
            {
                worker.Reload(settings.Clone(), false);
            }
        }

        void ShowStats()
        {
            string text;
            try
            {
                text = Stats.LastText(settings, worker != null ? worker.AddonFilesNow : new string[0]);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException)
                {
                    throw;
                }
                text = ex.Message;
            }
            MessageBox.Show((settings.StatsChoice == "yes"
                    ? Tr._("Статистика включена. Вот сообщение, которое уходит раз в сутки:")
                    : Tr._("Статистика выключена, ничего не отправляется. Вот что уходило бы раз в сутки:"))
                + "\n\n" + text, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
    }
}
