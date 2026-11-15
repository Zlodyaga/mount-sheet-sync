// SyncWorker: что видит пользователь — сбои и восстановление, итог прохода, «недавнее», состояние для значка.

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
    sealed partial class SyncWorker
    {
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
                        Local ? Tr._("Всё уже учтено. Свежие убийства игра записывает в файл, когда вы выходите на выбор персонажа, из игры или делаете /reload.")
                            : Tr._("Всё уже в таблице. Свежие убийства игра записывает в файл, когда вы выходите на выбор персонажа, из игры или делаете /reload."),
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
                AddRecent(Local ? Tr.F("! маунта «{0}» нет в списке", m) : Tr.F("! маунта «{0}» нет в таблице", m));
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
                title = Local ? Tr.F("Отмечено: {0}", sum.Marked.Count) : Tr.F("Отмечено в таблице: {0}", sum.Marked.Count);
                lines.AddRange(sum.Marked.Take(4));
                if (sum.Marked.Count > 4)
                {
                    lines.Add(Tr.F("…и ещё {0}", sum.Marked.Count - 4));
                }
            }
            else if (sum.Collected.Count > 0)
            {
                title = sum.Collected.Count == 1 ? Tr.F("Новый маунт: {0}", sum.Collected[0]) : Tr.F("Новые маунты: {0}", sum.Collected.Count);
                if (Local)
                {
                    lines.Add(Tr.F("Отмечены собранными: {0}.", string.Join(", ", sum.Collected)));
                }
                else
                {
    lines.Add(sum.Collected.Count == 1 ? Tr._("«Собрано?» в таблице отмечено.")
                        : Tr.F("«Собрано?» в таблице отмечено: {0}.", string.Join(", ", sum.Collected)));
                }
            }
            else if (sum.Planned.Count > 0)
            {
                title = Tr.F("Не фармлю: {0}", sum.Planned.Count);
                lines.AddRange(sum.Planned.Take(4));
                lines.Add(Local ? Tr._("В «Осталось» этих строк больше нет.") : Tr._("Галочка «Фармлю» в таблице снята — в «Осталось» этих строк больше нет."));
            }
            else if (sum.MountsNotFound.Count > 0)
            {
                title = Local ? Tr._("Маунт не нашёлся в списке") : Tr._("Маунт не нашёлся в таблице");
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
                lines.Add(Local ? Tr.F("Не нашёлся в списке: {0}.", string.Join(", ", sum.MountsNotFound)) : Tr.F("Не нашёлся в таблице: {0} — отметьте «Собрано?» вручную.", string.Join(", ", sum.MountsNotFound)));
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
