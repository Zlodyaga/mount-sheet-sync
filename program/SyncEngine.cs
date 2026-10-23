using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace MountSheetSync
{
    class SyncException : Exception
    {
        public SyncException(string message) : base(message)
        {
        }
    }

    /// <summary>Скрипт в таблице старше 1.4: нет списка «Осталось» и отметок «Собрано?».</summary>
    sealed class OldScriptException : SyncException
    {
        public OldScriptException()
            : base(Tr._("скрипт в таблице старой версии — вставьте Code.gs из архива заново и опубликуйте новую версию (раздел «Своя таблица со скриптом» в README)"))
        {
        }
    }

    /// <summary>Запросы к веб-приложению Google Apps Script (Code.gs).</summary>
    static class SheetClient
    {
        const int TimeoutMs = 90000;

        public static Dictionary<string, object> Call(string url, Dictionary<string, object> payload)
        {
            byte[] body = FileUtil.Utf8NoBom.GetBytes(Json.Write(payload, false));
            HttpWebRequest request;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(url);
            }
            catch (Exception ex)
            {
                throw new SyncException(Tr.F("неверный адрес веб-приложения ({0})", ex.Message));
            }
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Accept = "application/json";
            request.UserAgent = "MountSheetSync/" + Program.Version;
            // Google отвечает на POST переадресацией (302) на страницу с результатом — она загружается сама.
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 10;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.ContentLength = body.Length;

            string text;
            try
            {
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    text = ReadText(response);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    response.Close();
                    if (code == 404)
                    {
                        throw new SyncException(Tr._("адрес не найден (404). Проверьте адрес веб-приложения в настройках."));
                    }
                    if (code == 401 || code == 403)
                    {
                        throw new SyncException(Tr.F("нет доступа ({0}). В развертывании нужно «У кого есть доступ: Все».", code));
                    }
                }
                throw new SyncException(Tr.F("нет связи с таблицей: {0}", ex.Message));
            }
            catch (IOException ex)
            {
                throw new SyncException(Tr.F("нет связи с таблицей: {0}", ex.Message));
            }
            catch (ProtocolViolationException ex)
            {
                throw new SyncException(Tr.F("нет связи с таблицей: {0}", ex.Message));
            }

            Dictionary<string, object> resp = null;
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    resp = Json.Parse(text) as Dictionary<string, object>;
                }
                catch (FormatException)
                {
                    resp = null;
                }
                catch (OverflowException)
                {
                    resp = null;
                }
            }
            if (resp == null || !resp.ContainsKey("ok"))
            {
                throw new SyncException(Tr._("Google вернул страницу вместо ответа скрипта. Проверьте развертывание: тип «Веб-приложение», доступ «Все»."));
            }
            if (!J.Truthy(resp, "ok"))
            {
                throw new SyncException(Tr.F("таблица ответила ошибкой: {0}", J.Str(resp, "error")));
            }
            return resp;
        }

        static string ReadText(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                // Ответ скрипта всегда в UTF-8 — не полагаемся на заголовки.
                string text = FileUtil.Utf8NoBom.GetString(ms.ToArray());
                return text.TrimStart((char)0xFEFF, ' ', '\r', '\n', '\t');
            }
        }
    }

    sealed class PingInfo
    {
        public string Spreadsheet = "";
        public string Url = "";
        public List<string> Found = new List<string>();
        public List<string> Missing = new List<string>();
    }

    sealed class SyncSummary
    {
        public bool NothingToSend;
        public int SentRecords;
        /// <summary>«Не фармлю» из игры: «Имя: что».</summary>
        public List<string> Planned = new List<string>();
        public int MarkedCells;
        public List<string> Marked = new List<string>();
        public int Already;
        public int WrongDifficulty;
        public int NotFound;
        public int Stale;
        public List<string> Warnings = new List<string>();
        public string ResetMessage;
        public string ResetBy;
        /// <summary>Новые маунты, у которых «Собрано?» стало «Да».</summary>
        public List<string> Collected = new List<string>();
        /// <summary>Новые маунты, которых нет в колонке «Маунт».</summary>
        public List<string> MountsNotFound = new List<string>();
    }

    /// <summary>
    /// Что и как отправлять в таблицу. Перенос логики MountSheetSync.ps1 1.2:
    /// записи всех WoW-аккаунтов, без повторов, по времени; «Сброс недели» — первым,
    /// а за ним заново все убийства той же недели.
    /// </summary>
    sealed class SyncEngine
    {
        public const int BatchSize = 40;

        readonly SentLedger sent;
        public Settings Settings;

        public SyncEngine(Settings settings, SentLedger sent)
        {
            Settings = settings;
            this.sent = sent;
        }

        static string Id(Dictionary<string, object> r)
        {
            return J.Str(r, "id");
        }

        static bool IsReset(Dictionary<string, object> r)
        {
            return J.Str(r, "kind") == "reset";
        }

        static bool IsMount(Dictionary<string, object> r)
        {
            return J.Str(r, "kind") == "mount";
        }

        public bool IsSent(Dictionary<string, object> r)
        {
            return sent.Contains(Id(r));
        }

        public List<Dictionary<string, object>> ReadRecords(string file)
        {
            return SavedVariables.ParseRecords(SavedVariables.ReadShared(file));
        }

        public List<Dictionary<string, object>> ReadAllRecords(IEnumerable<string> files)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (string f in files)
            {
                List<Dictionary<string, object>> records;
                try
                {
                    records = ReadRecords(f);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
                    {
                        throw;
                    }
                    Log.Write(Tr.L("Не удалось прочитать {0}: {1}", f, ex.Message));
                    continue;
                }
                foreach (Dictionary<string, object> r in records)
                {
                    if (seen.Add(Id(r)))
                    {
                        list.Add(r);
                    }
                }
            }
            // OrderBy сохраняет порядок записей с одинаковым временем
            return list.OrderBy(r => J.Long(r, "time")).ToList();
        }

        Dictionary<string, object> Request(string action)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["secret"] = Settings.Secret;
            payload["action"] = action;
            return payload;
        }

        public PingInfo Ping(IList<string> files)
        {
            List<object> chars = new List<object>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> r in ReadAllRecords(files))
            {
                string name = J.Str(r, "char");
                if (seen.Add(name))
                {
                    Dictionary<string, object> c = new Dictionary<string, object>();
                    c["char"] = name;
                    c["realm"] = J.Str(r, "realm");
                    chars.Add(c);
                }
            }
            Dictionary<string, object> payload = Request("ping");
            payload["chars"] = chars;
            Dictionary<string, object> resp = Settings.CreateService().Call(payload);

            PingInfo info = new PingInfo();
            info.Spreadsheet = J.Str(resp, "spreadsheet");
            info.Url = J.Str(resp, "url");
            Log.Write(Tr.L("Таблица «{0}» — связь есть.", info.Spreadsheet));
            List<string> names = new List<string>();
            foreach (object o in J.Arr(J.Get(resp, "characters")))
            {
                string name = J.Str(J.Obj(o), "name");
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            if (names.Count > 0)
            {
                Log.Write(Tr.L("Персонажи в таблице: {0}", string.Join(", ", names)));
            }
            foreach (object o in J.Arr(J.Get(resp, "checked")))
            {
                Dictionary<string, object> c = J.Obj(o);
                string who = J.Str(c, "char");
                if (J.Truthy(c, "found"))
                {
                    List<string> sheets = new List<string>();
                    foreach (object sheet in J.Arr(J.Get(c, "sheets")))
                    {
                        sheets.Add(Convert.ToString(sheet, CultureInfo.InvariantCulture));
                    }
                    Log.Write(Tr.L("  {0} — листы: {1}", who, string.Join(", ", sheets)));
                    info.Found.Add(who);
                }
                else
                {
                    Log.Write(Tr.L("  {0} ({1}) — в таблице нет его листов, отметки для него сохраняться не будут",
                        who, J.Str(c, "realm")));
                    info.Missing.Add(who);
                }
            }
            return info;
        }

        /// <summary>Список «Осталось на этой неделе» для аддона (Code.gs 1.4+).</summary>
        public Dictionary<string, object> FetchStatus()
        {
            Dictionary<string, object> payload = Request("status");
            payload["client"] = "exe-" + Program.Version;
            Dictionary<string, object> resp = Settings.CreateService().Call(payload);
            if (!(J.Get(resp, "chars") is List<object>))
            {
                throw new OldScriptException();
            }
            return resp;
        }

        /// <summary>
        /// Что отправить. Обычно — новые записи по порядку. Если среди них есть «Сброс недели»,
        /// то сначала последний сброс, а за ним заново все убийства той же недели
        /// (и уже отправленные), чтобы сброс не стёр отметки этой недели.
        /// </summary>
        public List<Dictionary<string, object>> GetSendList(List<Dictionary<string, object>> records)
        {
            List<Dictionary<string, object>> pending = records.Where(r => !sent.Contains(Id(r))).ToList();
            List<Dictionary<string, object>> resets = pending.Where(r => IsReset(r)).ToList();
            if (resets.Count == 0)
            {
                return pending;
            }
            Dictionary<string, object> reset = resets[resets.Count - 1];
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            list.Add(reset);
            bool hasWeek = reset.ContainsKey("weekEnd");
            long week = J.Long(reset, "weekEnd");
            foreach (Dictionary<string, object> r in records)
            {
                if (IsReset(r))
                {
                    continue;
                }
                bool isNew = !sent.Contains(Id(r));
                bool sameWeek = hasWeek && r.ContainsKey("weekEnd") && J.Long(r, "weekEnd") == week;
                if (isNew || sameWeek)
                {
                    list.Add(r);
                }
            }
            return list;
        }

        public SyncSummary SyncAll(IList<string> files)
        {
            SyncSummary sum = new SyncSummary();
            List<Dictionary<string, object>> records = ReadAllRecords(files);
            List<Dictionary<string, object>> pending = GetSendList(records);
            if (!Settings.UseGoogle)
            {
                // «Не фармлю» из игры — только для таблицы программы; скрипт Code.gs принял бы её за убийство
                List<Dictionary<string, object>> plans = pending.Where(r => J.Str(r, "kind") == "plan").ToList();
                if (plans.Count > 0)
                {
                    long at = Epoch.Now;
                    foreach (Dictionary<string, object> r in plans)
                    {
                        sent.Mark(Id(r), at);
                    }
                    sent.Save();
                    pending.RemoveAll(r => J.Str(r, "kind") == "plan");
                    Log.Write(Tr.L("«Не фармлю» из игры ({0}) в таблицу со скриптом не отправляется — снимите галочку «Фармлю» в самой таблице.", plans.Count));
                }
            }
            if (pending.Count == 0)
            {
                sum.NothingToSend = true;
                return sum;
            }
            HashSet<string> mountIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Dictionary<string, object> r in pending)
            {
                if (IsMount(r))
                {
                    mountIds.Add(Id(r));
                }
            }
            // сброс и повторные отметки — одним запросом
            int size = IsReset(pending[0]) ? pending.Count : BatchSize;
            for (int i = 0; i < pending.Count; i += size)
            {
                List<Dictionary<string, object>> batch = pending.GetRange(i, Math.Min(size, pending.Count - i));
                Dictionary<string, object> payload = Request("kills");
                payload["client"] = "exe-" + Program.Version;
                payload["kills"] = batch;
                Dictionary<string, object> resp = Settings.CreateService().Call(payload);
                List<object> results = J.Arr(J.Get(resp, "results"));
                Report(results, sum, mountIds);
                long now = Epoch.Now;
                foreach (object o in results)
                {
                    string id = J.Str(J.Obj(o), "id");
                    if (id.Length > 0)
                    {
                        sent.Mark(id, now);
                    }
                }
                sent.Save();
                sum.SentRecords += batch.Count;
            }
            // более ранние «Сбросы недели» перекрыты последним
            long t = Epoch.Now;
            foreach (Dictionary<string, object> r in records)
            {
                if (IsReset(r) && !sent.Contains(Id(r)))
                {
                    sent.Mark(Id(r), t);
                }
            }
            sent.Save();
            return sum;
        }

        static void Report(List<object> results, SyncSummary sum, HashSet<string> mountIds)
        {
            List<string> notFound = new List<string>();
            HashSet<string> shown = new HashSet<string>(StringComparer.Ordinal);
            int stale = 0;
            foreach (object o in results)
            {
                Dictionary<string, object> r = J.Obj(o);
                if (r == null)
                {
                    continue;
                }
                string who = J.Str(r, "char");
                string boss = J.Str(r, "boss");
                string status = J.Str(r, "status");
                string message = J.Str(r, "message");
                if (mountIds.Contains(J.Str(r, "id")))
                {
                    ReportMount(r, who, boss, status, message, sum);
                    continue;
                }
                if (J.Str(r, "kind") == "plan")
                {
                    if (status == "planned" || status == "already")
                    {
                        int n = J.Arr(J.Get(r, "cells")).Count(c => J.Truthy(J.Obj(c), "changed"));
                        Log.Write(status == "planned"
                            ? Tr.L("[-] {0}: не фармит «{1}» — галочка «Фармлю» снята (строк: {2})", who, boss, n)
                            : Tr.L("[-] {0}: не фармит «{1}» — галочка «Фармлю» уже была снята", who, boss));
                        if (status == "planned")
                        {
                            sum.Planned.Add(who + ": " + boss);
                        }
                    }
                    else
                    {
                        Log.Write(Tr.L("[!] {0}: «не фармлю» {1} — {2}", who, boss, message));
                        sum.Warnings.Add(Tr.F("{0}: «не фармлю» {1} — {2}", who, boss, message));
                    }
                    continue;
                }
                if (status == "marked")
                {
                    bool any = false;
                    foreach (object co in J.Arr(J.Get(r, "cells")))
                    {
                        Dictionary<string, object> c = J.Obj(co);
                        if (!J.Truthy(c, "changed"))
                        {
                            continue;
                        }
                        string mount = J.Str(c, "mount");
                        Log.Write(Tr.L("[+] {0}: {1} -> «Да»  ({2}, строка {3}{4})",
                            who, boss, J.Str(c, "sheet"), J.Str(c, "row"), mount.Length > 0 ? " — " + mount : ""));
                        sum.MarkedCells++;
                        any = true;
                    }
                    if (any)
                    {
                        sum.Marked.Add(who + ": " + boss);
                    }
                }
                else if (status == "reset")
                {
                    Log.Write(Tr.L("[*] Сброс недели (нажал {0}): {1}. Заново отмечаю убийства этой недели из журнала.",
                        who, message));
                    sum.ResetMessage = message;
                    sum.ResetBy = who;
                }
                else if (status == "already")
                {
                    Log.Write(Tr.L("[=] {0}: {1} — уже «Да»", who, boss));
                    sum.Already++;
                }
                else if (status == "stale")
                {
                    stale++;
                }
                else if (status == "not_found")
                {
                    notFound.Add(string.Format("{0} ({1})", boss, who));
                }
                else if (status == "wrong_difficulty")
                {
                    // маунт с этой сложности не падает — делать ничего не нужно
                    Log.Write(Tr.L("[~] {0}: {1} — {2}", who, boss, message));
                    sum.WrongDifficulty++;
                }
                else if (shown.Add(status + "|" + message))
                {
                    Log.Write(Tr.L("[!] {0}: {1} — {2}", who, boss, message));
                    sum.Warnings.Add(Tr.F("{0}: {1} — {2}", who, boss, message));
                }
            }
            if (notFound.Count > 0)
            {
                Log.Write(Tr.L("[ ] Нет на листах персонажа, пропущено: {0}", string.Join(", ", notFound)));
            }
            if (stale > 0)
            {
                Log.Write(Tr.L("[ ] Убийства до еженедельного сброса, пропущено: {0}", stale));
            }
            sum.NotFound += notFound.Count;
            sum.Stale += stale;
        }

        /// <summary>Новый маунт: «Собрано?» в общих листах таблицы.</summary>
        static void ReportMount(Dictionary<string, object> r, string who, string mount, string status, string message, SyncSummary sum)
        {
            if (J.Str(r, "kind") != "mount")
            {
                // Code.gs до 1.4 не знает о маунтах и принял запись за убийство
                Log.Write(Tr.L("[!] {0}: маунт «{1}» — {2}", who, mount, new OldScriptException().Message));
                sum.Warnings.Add(Tr.F("маунт «{0}»: обновите Code.gs в таблице", mount));
                return;
            }
            if (status == "collected")
            {
                foreach (object co in J.Arr(J.Get(r, "cells")))
                {
                    Dictionary<string, object> c = J.Obj(co);
                    if (J.Truthy(c, "changed"))
                    {
                        Log.Write(Tr.L("[$] {0}: новый маунт «{1}» -> «Собрано?» = «Да»  ({2}, строка {3})",
                            who, mount, J.Str(c, "sheet"), J.Str(c, "row")));
                    }
                }
                if (!sum.Collected.Contains(mount))
                {
                    sum.Collected.Add(mount);
                }
            }
            else if (status == "already")
            {
                Log.Write(Tr.L("[=] {0}: маунт «{1}» — «Собрано?» уже «Да»", who, mount));
            }
            else
            {
                Log.Write(Tr.L("[?] {0}: {1}", who, message.Length > 0 ? message : Tr.F("маунт «{0}» не найден в таблице", mount)));
                if (!sum.MountsNotFound.Contains(mount))
                {
                    sum.MountsNotFound.Add(mount);
                }
            }
        }
    }
}
