// Анонимная статистика: одно короткое сообщение в сутки, только с согласия игрока (settings.StatsChoice = "yes").
// В сообщении — версии, язык, режим, вилка числа персонажей, счётчики аддона за прошлый день и случайный номер
// установки. Ни имён, ни серверов, ни данных таблицы. Что именно ушло, игрок видит в меню («Что отправляется…»).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;

namespace MountSheetSync
{
    static class Stats
    {
        /// <summary>
        /// Адрес приёмника — веб-приложение Apps Script автора (tools/stats/Code.gs). Не секрет: по нему можно только
        /// добавить строку. Пустой адрес выключает статистику целиком: ни вопроса, ни пунктов меню, ни отправки.
        /// </summary>
        public const string Url = "https://script.google.com/macros/s/AKfycbyVEf7FbRlom2vlALzMZcVZxDZrQP8eAlGo2BzHH-5LwwScRt9Irv5001f0NT4T7NGq/exec";
        const int TimeoutMs = 15000;

        public static string UrlInUse
        {
            get
            {
#if TESTS
                // сборка для тестов настоящему приёмнику не пишет и вопроса не задаёт: адрес — только из окружения
                return Environment.GetEnvironmentVariable("MSS_STATS_URL") ?? "";
#else
                return Url;
#endif
            }
        }

        public static bool Available
        {
            get { return UrlInUse.Length > 0; }
        }

        public static string StateFile
        {
            get { return Path.Combine(AppPaths.DataDir, "stats.json"); }
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        static string Today()
        {
            return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>Вилка вместо точного числа: по числу персонажей игрока не узнать.</summary>
        public static string Bucket(int n)
        {
            return n <= 0 ? "0" : n == 1 ? "1" : n <= 5 ? "2-5" : n <= 10 ? "6-10" : "11+";
        }

        static string WindowsMajor()
        {
            Version v = Environment.OSVersion.Version;
            return v.Major == 10 ? (v.Build >= 22000 ? "11" : "10") : v.Major.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Сообщение целиком. addonFiles — файлы сохранений аддона; берём самый свежий.</summary>
        public static Dictionary<string, object> Build(Settings settings, IEnumerable<string> addonFiles)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["v"] = 1;
            m["id"] = settings.InstallId;
            m["program"] = Program.Version;
            m["windows"] = WindowsMajor();
            m["lang"] = Tr.Lang;
            m["mode"] = settings.IsLocal ? "local" : "google";
            string newest = null;
            DateTime newestAt = DateTime.MinValue;
            foreach (string f in addonFiles ?? new string[0])
            {
                try
                {
                    DateTime at = File.GetLastWriteTimeUtc(f);
                    if (File.Exists(f) && at > newestAt)
                    {
                        newest = f;
                        newestAt = at;
                    }
                }
                catch (Exception swallowed)
                {
                    Log.Quiet("статистика: файл аддона", swallowed);
                }
            }
            Dictionary<string, object> db = newest == null ? null : J.Obj(J.Get(LuaData.ParseFile(newest), "MountSheetSyncDB"));
            if (db == null)
            {
                return m;
            }
            m["addon"] = Short(J.Str(db, "addonVersion"), 20);
            Dictionary<string, object> col = J.Obj(J.Get(db, "collection"));
            if (col != null)
            {
                m["game"] = Short(J.Str(col, "locale"), 8);
            }
            Dictionary<string, object> chars = J.Obj(J.Get(db, "chars"));
            m["chars"] = Bucket(chars == null ? 0 : chars.Count);
            Dictionary<string, object> usage = J.Obj(J.Get(db, "usage"));
            if (usage != null)
            {
                m["mapzeroth"] = J.Truthy(usage, "mapzeroth");
                // последний полный день: сегодняшний ещё не кончился
                Dictionary<string, object> days = J.Obj(J.Get(usage, "days"));
                string best = null;
                foreach (string day in days == null ? new List<string>() : new List<string>(days.Keys))
                {
                    if (string.CompareOrdinal(day, Today()) < 0 && (best == null || string.CompareOrdinal(day, best) > 0))
                    {
                        best = day;
                    }
                }
                Dictionary<string, object> counts = best == null ? null : J.Obj(J.Get(days, best));
                if (counts != null)
                {
                    Dictionary<string, object> use = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, object> one in counts)
                    {
                        // только известные аддону короткие имена и числа: ничего другого из файла игры не уйдёт
                        if (one.Value is double && one.Key.Length <= 20 && IsPlainName(one.Key))
                        {
                            use[one.Key] = (long)Math.Min((double)one.Value, 100000);
                        }
                    }
                    m["day"] = best;
                    m["use"] = use;
                }
            }
            return m;
        }

        static bool IsPlainName(string s)
        {
            foreach (char c in s)
            {
                if (!(c >= 'a' && c <= 'z') && c != '_')
                {
                    return false;
                }
            }
            return s.Length > 0;
        }

        static string Short(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        public static string Text(Dictionary<string, object> message)
        {
            return Json.Write(message, true);
        }

        /// <summary>Что ушло в прошлый раз (или что ушло бы сейчас, если ещё ничего не отправляли).</summary>
        public static string LastText(Settings settings, IEnumerable<string> addonFiles)
        {
            try
            {
                Dictionary<string, object> state = J.Obj(Json.Parse(File.ReadAllText(StateFile, FileUtil.Utf8NoBom)));
                Dictionary<string, object> last = state == null ? null : J.Obj(J.Get(state, "last"));
                if (last != null)
                {
                    return Text(last);
                }
            }
            catch (Exception swallowed)
            {
                if (!(swallowed is FileNotFoundException) && !(swallowed is DirectoryNotFoundException))
                {
                    Log.Quiet("статистика: прошлое сообщение", swallowed);
                }
            }
            Settings shown = settings.Clone();
            if (shown.InstallId.Length == 0)
            {
                shown.InstallId = "(номер появится после согласия)";
            }
            return Text(Build(shown, addonFiles));
        }

        static string SentDay()
        {
            try
            {
                Dictionary<string, object> state = J.Obj(Json.Parse(File.ReadAllText(StateFile, FileUtil.Utf8NoBom)));
                return state == null ? "" : J.Str(state, "day");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Раз в сутки, если игрок согласился. Не вышло — молчим и не повторяем до завтра: день отмечается до отправки.
        /// </summary>
        public static void SendIfDue(Settings settings, IEnumerable<string> addonFiles)
        {
            if (!Available || settings.StatsChoice != "yes" || settings.InstallId.Length == 0 || SentDay() == Today())
            {
                return;
            }
            Dictionary<string, object> message = Build(settings, addonFiles);
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["day"] = Today();
            state["last"] = message;
            File.WriteAllText(StateFile, Json.Write(state, true), FileUtil.Utf8NoBom);
            byte[] body = FileUtil.Utf8NoBom.GetBytes(Json.Write(message, false));
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UrlInUse);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.UserAgent = "MountSheetSync/" + Program.Version;
            request.AllowAutoRedirect = true;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.ContentLength = body.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }
            // ответ дочитываем: брошенное на полуслове соединение приёмник видит как обрыв
            using (WebResponse response = request.GetResponse())
            using (Stream answer = response.GetResponseStream())
            {
                byte[] skip = new byte[1024];
                while (answer.Read(skip, 0, skip.Length) > 0)
                {
                }
            }
        }

        /// <summary>Игрок выключил статистику: номер установки стирается, включит снова — будет новый.</summary>
        public static void Apply(Settings settings, bool on)
        {
            settings.StatsChoice = on ? "yes" : "no";
            settings.InstallId = on ? (settings.InstallId.Length > 0 ? settings.InstallId : NewId()) : "";
            if (!on)
            {
                try
                {
                    File.Delete(StateFile);
                }
                catch (Exception swallowed)
                {
                    Log.Quiet("статистика: файл состояния", swallowed);
                }
            }
        }
    }
}
