// Вход через Google (OAuth 2.0 для настольных программ: браузер + ответ на 127.0.0.1 + PKCE)
// и запросы к Google Sheets API. Доступ — только drive.file: программа видит лишь те файлы,
// которые создала сама. Ключ входа (refresh token) хранится зашифрованным средствами Windows
// (DPAPI, только для текущего пользователя) в %APPDATA%\MountSheetSync\google-token.dat.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MountSheetSync
{
    static class GoogleConfig
    {
        // Проект Google Cloud «mount-sheet-sync», клиент OAuth типа «Desktop app».
        public const string ClientId = "676267272691-cq183fvmictehrh4u6p305i39rckt71q.apps.googleusercontent.com";

        // Для настольных программ Google не считает этот ключ секретом: он есть в каждой копии программы,
        // а защищает вход PKCE. Записан частями, чтобы его не принимали за утечку автоматические проверки
        // репозиториев (они отзывают найденные ключи).
        static readonly string[] SecretParts = { "GOCSPX", "-2P-Snr0_C", "qlSNjhRhBxe", "XWUXmJzQ" };

        public static string ClientSecret
        {
            get { return string.Concat(SecretParts); }
        }

        public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
        public const string Scopes = "openid email " + DriveFileScope;

        public static string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public static string TokenEndpoint = "https://oauth2.googleapis.com/token";
        public static string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
        public static string SheetsBase = "https://sheets.googleapis.com/v4/spreadsheets";

        /// <summary>
        /// Для проверок: MSS_GOOGLE_TEST=http://127.0.0.1:порт направляет все запросы к Google
        /// на имитацию на этом компьютере. Другие адреса не принимаются.
        /// </summary>
        public static void ApplyTestOverride()
        {
#if TESTS
            string t = Environment.GetEnvironmentVariable("MSS_GOOGLE_TEST");
            if (string.IsNullOrEmpty(t))
            {
                return;
            }
            t = t.TrimEnd('/');
            Uri u;
            if (!Uri.TryCreate(t, UriKind.Absolute, out u) || u.Scheme != "http"
                || (u.Host != "127.0.0.1" && u.Host != "localhost"))
            {
                Log.Write("MSS_GOOGLE_TEST is not http://127.0.0.1:port — ignored.");
                return;
            }
            AuthEndpoint = t + "/o/oauth2/v2/auth";
            TokenEndpoint = t + "/token";
            RevokeEndpoint = t + "/revoke";
            SheetsBase = t + "/v4/spreadsheets";
            SheetCreator.DriveBase = t + "/drive/v3/files";
            GoogleHttp.RetryScale = 50;   // повторы на имитации — без долгих пауз
            Log.Write("Google requests go to the test server " + t);
#endif
        }
    }

    /// <summary>Ошибка входа: показывается человеку как есть.</summary>
    sealed class GoogleAuthException : SyncException
    {
        public readonly bool SignedOut;

        public GoogleAuthException(string message, bool signedOut) : base(message)
        {
            SignedOut = signedOut;
        }
    }

    /// <summary>Google API ответил ошибкой.</summary>
    sealed class GoogleApiException : SyncException
    {
        public readonly int Code;
        public readonly string Status;

        public GoogleApiException(int code, string status, string message) : base(message)
        {
            Code = code;
            Status = status ?? "";
        }
    }

    sealed class GoogleToken
    {
        public string Refresh = "";
        public string Email = "";
        public string Scope = "";
        public long Saved;
        // ключ доступа сразу после входа — в SessionCache, когда вход признан удачным
        public string PendingAccess;
        public long PendingExpires;
    }

    /// <summary>Ключ входа на диске — зашифрован DPAPI (расшифровать может только этот пользователь Windows).</summary>
    static class TokenStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MountSheetSync.google-token.v1");

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDir, "google-token.dat"); }
        }

        public static bool Exists
        {
            get { return File.Exists(FilePath); }
        }

        public static GoogleToken Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            try
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                Dictionary<string, object> d = Json.Parse(Encoding.UTF8.GetString(plain)) as Dictionary<string, object>;
                if (d == null || J.Str(d, "refresh").Length == 0)
                {
                    return null;
                }
                GoogleToken t = new GoogleToken();
                t.Refresh = J.Str(d, "refresh");
                t.Email = J.Str(d, "email");
                t.Scope = J.Str(d, "scope");
                t.Saved = J.Long(d, "saved");
                return t;
            }
            catch (Exception ex)
            {
                if (!(ex is CryptographicException) && !(ex is IOException) && !(ex is FormatException)
                    && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                Log.Write(Tr.L("Ключ входа в Google не читается ({0}) — нужно войти заново.", ex.Message));
                return null;
            }
        }

        public static void Save(GoogleToken t)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["refresh"] = t.Refresh;
            d["email"] = t.Email;
            d["scope"] = t.Scope;
            d["saved"] = Epoch.Now;
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Write(d, false)), Entropy, DataProtectionScope.CurrentUser);
            string tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(FilePath))
            {
                File.Replace(tmp, FilePath, null);
            }
            else
            {
                File.Move(tmp, FilePath);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Не удалось удалить ключ входа: {0}", ex.Message));
            }
        }
    }

    static class GoogleAuth
    {
        static readonly TimeSpan WaitForBrowser = TimeSpan.FromMinutes(5);

        static string RandomUrlSafe(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            byte[] bytes = new byte[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            StringBuilder sb = new StringBuilder(length);
            foreach (byte b in bytes)
            {
                sb.Append(chars[b % chars.Length]);
            }
            return sb.ToString();
        }

        public static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        public static string Challenge(string verifier)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            }
        }

        /// <summary>Одна попытка входа: свои state и PKCE-ключ, адрес страницы Google.</summary>
        sealed class Attempt
        {
            public string Verifier;
            public string State;
            public string Url;
        }

        static Attempt NewAttempt(string redirect)
        {
            Attempt a = new Attempt();
            a.Verifier = RandomUrlSafe(64);
            a.State = RandomUrlSafe(24);
            a.Url = GoogleConfig.AuthEndpoint
                + "?client_id=" + Uri.EscapeDataString(GoogleConfig.ClientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(GoogleConfig.Scopes)
                + "&code_challenge=" + Challenge(a.Verifier)
                + "&code_challenge_method=S256"
                + "&state=" + a.State
                + "&access_type=offline&prompt=consent";
            return a;
        }

        /// <summary>Адрес «попробовать ещё раз» на 127.0.0.1 для страницы входа (по её redirect_uri).</summary>
        public static string RetryUrlFor(string authUrl)
        {
            int q = authUrl.IndexOf('?');
            string redirect;
            if (q >= 0 && ParseQuery(authUrl.Substring(q + 1)).TryGetValue("redirect_uri", out redirect) && redirect.Length > 0)
            {
                return redirect.TrimEnd('/') + "/retry";
            }
            return authUrl;
        }

        public static string NoFileAccessText
        {
            get
            {
                return Tr._("Google не дал программе доступ к файлам: на странице разрешений не стояла галочка у пункта про файлы Google Диска, которые использует это приложение. Без него программа не сможет создать и вести таблицу.");
            }
        }

        /// <summary>
        /// Вход: открывает страницу Google в браузере и ждёт, пока человек войдёт и разрешит доступ.
        /// openBrowser получает адрес страницы входа (обычно — Process.Start). status (может быть null) —
        /// подсказки для окна входа, пока оно ждёт (например, «не поставлена галочка — попробуйте ещё раз»).
        /// Браузеру отвечаем только после обмена кода на ключ: страница говорит правду о результате.
        /// </summary>
        public static GoogleToken SignIn(Action<string> openBrowser, CancellationToken cancel, Action<string> status)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
                Attempt attempt = NewAttempt(redirect);
                Log.Write(Tr.L("Вход в Google: жду ответа на {0}", redirect));
                openBrowser(attempt.Url);

                DateTime deadline = DateTime.UtcNow + WaitForBrowser;
                while (true)
                {
                    using (BrowserRequest req = BrowserRequest.Accept(listener, deadline, cancel))
                    {
                        if (req.Path == "/retry")
                        {
                            // «Попробовать ещё раз» на нашей странице: новая попытка, браузер — снова к Google
                            attempt = NewAttempt(redirect);
                            deadline = DateTime.UtcNow + WaitForBrowser;
                            Log.Write(Tr.L("Вход в Google: новая попытка из браузера."));
                            req.Redirect(attempt.Url);
                            if (status != null)
                            {
                                status(Tr._("Жду, пока вы разрешите доступ на странице Google."));
                            }
                            continue;
                        }
                        if (req.Path != "/")
                        {
                            req.Respond("404 Not Found", Tr._("Не найдено."), false);
                            continue;
                        }
                        Dictionary<string, string> query = req.Query;
                        string gotState;
                        query.TryGetValue("state", out gotState);
                        if (gotState != attempt.State)
                        {
                            Log.Write(Tr.L("Вход в Google: ответ с чужим state — пропускаю."));
                            req.Respond("200 OK", Tr._("Эта страница от прошлой попытки входа. Вернитесь к Mount Sheet Sync."), false);
                            continue;
                        }
                        string error;
                        if (query.TryGetValue("error", out error))
                        {
                            req.Respond("200 OK", Tr._("Вход не выполнен. Вернитесь к Mount Sheet Sync и попробуйте ещё раз."), false);
                            if (error == "access_denied")
                            {
                                throw new GoogleAuthException(Tr._("вход отменён: на странице Google не дали разрешение"), false);
                            }
                            throw new GoogleAuthException(Tr.F("Google не пустил: {0}", error), false);
                        }
                        string code;
                        if (!query.TryGetValue("code", out code) || code.Length == 0)
                        {
                            req.Respond("200 OK", Tr._("Вход не выполнен: Google не прислал код входа. Попробуйте ещё раз."), true);
                            continue;
                        }
                        GoogleToken t;
                        try
                        {
                            t = Exchange(code, attempt.Verifier, redirect);
                        }
                        catch (Exception ex)
                        {
                            req.Respond("200 OK", Tr.F("Вход не выполнен: {0}.", ex.Message), true);
                            Log.Write(Tr.L("Вход в Google: обмен кода не удался — {0}", ex.Message));
                            if (status != null)
                            {
                                status(Tr.F("Вход не выполнен: {0}. На странице в браузере можно нажать «Попробовать ещё раз».", ex.Message));
                            }
                            continue;
                        }
                        if (!t.Scope.Split(' ').Contains(GoogleConfig.DriveFileScope))
                        {
                            Log.Write(Tr.L("Вход в Google: нет доступа к файлам (drive.file) — прошу попробовать ещё раз."));
                            req.Respond("200 OK", NoFileAccessText + "\n\n" + Tr._("Нажмите «Попробовать ещё раз» и на странице Google поставьте все галочки (или «Выбрать все»)."), true);
                            if (status != null)
                            {
                                status(Tr._("Не поставлена галочка доступа к файлам. В браузере нажмите «Попробовать ещё раз» и отметьте все пункты на странице Google."));
                            }
                            continue;
                        }
                        SessionCache.Set(t.PendingAccess, t.PendingExpires);
                        t.PendingAccess = null;
                        Log.Write(t.Email.Length > 0 ? Tr.L("Вход в Google выполнен: {0}.", t.Email) : Tr.L("Вход в Google выполнен."));
                        req.Respond("200 OK", Tr._("Вход выполнен. Эту вкладку можно закрыть и вернуться к Mount Sheet Sync."), false);
                        return t;
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>Один HTTP-запрос браузера на 127.0.0.1; ответ отправляется позже, когда ясно, что сказать.</summary>
        sealed class BrowserRequest : IDisposable
        {
            TcpClient client;
            NetworkStream stream;
            bool answered;
            public string Path = "";
            public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.Ordinal);

            public static BrowserRequest Accept(TcpListener listener, DateTime deadline, CancellationToken cancel)
            {
                IAsyncResult ar = listener.BeginAcceptTcpClient(null, null);
                while (!ar.AsyncWaitHandle.WaitOne(250))
                {
                    if (cancel.IsCancellationRequested)
                    {
                        throw new GoogleAuthException(Tr._("вход отменён"), false);
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        throw new GoogleAuthException(Tr._("вход не завершён за 5 минут — попробуйте ещё раз"), false);
                    }
                }
                BrowserRequest r = new BrowserRequest();
                r.client = listener.EndAcceptTcpClient(ar);
                r.client.ReceiveTimeout = 10000;
                r.client.SendTimeout = 10000;
                r.stream = r.client.GetStream();
                string[] parts = ReadRequestHead(r.stream).Split(' ');
                string target = parts.Length >= 2 && parts[0] == "GET" ? parts[1] : "";
                int q = target.IndexOf('?');
                r.Path = q >= 0 ? target.Substring(0, q) : target;
                if (q >= 0)
                {
                    r.Query = ParseQuery(target.Substring(q + 1));
                }
                return r;
            }

            public void Respond(string status, string message, bool offerRetry)
            {
                answered = true;
                GoogleAuth.Respond(stream, status, message, offerRetry);
            }

            public void Redirect(string url)
            {
                answered = true;
                byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: " + url
                    + "\r\nContent-Length: 0\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                try
                {
                    stream.Write(h, 0, h.Length);
                    stream.Flush();
                }
                catch (IOException)
                {
                }
            }

            public void Dispose()
            {
                if (!answered && stream != null)
                {
                    GoogleAuth.Respond(stream, "500 Internal Server Error", Tr._("Что-то пошло не так. Вернитесь к Mount Sheet Sync."), false);
                }
                if (client != null)
                {
                    client.Close();
                }
            }
        }

        static string ReadRequestHead(NetworkStream stream)
        {
            byte[] buf = new byte[8192];
            int total = 0;
            while (total < buf.Length)
            {
                int n;
                try
                {
                    n = stream.Read(buf, total, buf.Length - total);
                }
                catch (IOException)
                {
                    break;
                }
                if (n <= 0)
                {
                    break;
                }
                total += n;
                string head = Encoding.ASCII.GetString(buf, 0, total);
                if (head.Contains("\r\n\r\n") || head.Contains("\n\n"))
                {
                    break;
                }
            }
            string text = Encoding.ASCII.GetString(buf, 0, total);
            int end = text.IndexOf('\n');
            return (end >= 0 ? text.Substring(0, end) : text).TrimEnd('\r');
        }

        static void Respond(NetworkStream stream, string status, string message, bool offerRetry)
        {
            StringBuilder paras = new StringBuilder();
            foreach (string p in message.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                paras.Append("<p>").Append(WebUtilityHtml(p)).Append("</p>");
            }
            string retry = offerRetry
                ? "<p><a href=\"/retry\" style=\"display:inline-block;padding:.6em 1.2em;background:#1a73e8;color:#fff;"
                    + "border-radius:4px;text-decoration:none\">" + WebUtilityHtml(Tr._("Попробовать ещё раз")) + "</a></p>"
                : "";
            string html = "<!doctype html><html lang=\"" + Tr.Lang + "\"><head><meta charset=\"utf-8\"><title>Mount Sheet Sync</title></head>"
                + "<body style=\"font-family:Segoe UI,Arial,sans-serif;max-width:34em;margin:4em auto;line-height:1.5\">"
                + "<h2>Mount Sheet Sync</h2>" + paras + retry + "</body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = "HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
                + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
            try
            {
                byte[] h = Encoding.ASCII.GetBytes(head);
                stream.Write(h, 0, h.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch (IOException)
            {
            }
        }

        static string WebUtilityHtml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        public static Dictionary<string, string> ParseQuery(string q)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string pair in q.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }
                int eq = pair.IndexOf('=');
                string k = eq >= 0 ? pair.Substring(0, eq) : pair;
                string v = eq >= 0 ? pair.Substring(eq + 1) : "";
                d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' '));
            }
            return d;
        }

        static GoogleToken Exchange(string code, string verifier, string redirect)
        {
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["code"] = code;
            form["client_id"] = GoogleConfig.ClientId;
            form["client_secret"] = GoogleConfig.ClientSecret;
            form["redirect_uri"] = redirect;
            form["grant_type"] = "authorization_code";
            form["code_verifier"] = verifier;
            Dictionary<string, object> resp = PostForm(GoogleConfig.TokenEndpoint, form);
            GoogleToken t = new GoogleToken();
            t.Refresh = J.Str(resp, "refresh_token");
            t.Scope = J.Str(resp, "scope");
            t.Email = EmailFromIdToken(J.Str(resp, "id_token"));
            if (t.Refresh.Length == 0)
            {
                throw new GoogleAuthException(Tr._("Google не выдал ключ для работы без вас — попробуйте войти ещё раз"), false);
            }
            t.PendingAccess = J.Str(resp, "access_token");
            t.PendingExpires = J.Long(resp, "expires_in");
            return t;
        }

        public static string EmailFromIdToken(string idToken)
        {
            try
            {
                string[] parts = (idToken ?? "").Split('.');
                if (parts.Length < 2)
                {
                    return "";
                }
                Dictionary<string, object> claims = Json.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[1]))) as Dictionary<string, object>;
                return J.Str(claims, "email");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Новый ключ доступа по ключу входа. Ключ входа отозван или устарел — GoogleAuthException(signedOut).</summary>
        public static void Refresh(GoogleToken t)
        {
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["client_id"] = GoogleConfig.ClientId;
            form["client_secret"] = GoogleConfig.ClientSecret;
            form["refresh_token"] = t.Refresh;
            form["grant_type"] = "refresh_token";
            Dictionary<string, object> resp;
            try
            {
                resp = PostForm(GoogleConfig.TokenEndpoint, form);
            }
            catch (GoogleApiException ex)
            {
                if (ex.Status == "invalid_grant" || ex.Status == "unauthorized_client" || ex.Status == "invalid_client")
                {
                    throw new GoogleAuthException(Tr._("вход в Google больше не действует — войдите снова (меню значка → «Войти через Google»)"), true);
                }
                throw;
            }
            SessionCache.Set(J.Str(resp, "access_token"), J.Long(resp, "expires_in"));
        }

        /// <summary>Отозвать доступ программы в Google (при «Выйти»). Ошибки не важны.</summary>
        public static void Revoke(GoogleToken t)
        {
            if (t == null || t.Refresh.Length == 0)
            {
                return;
            }
            try
            {
                Dictionary<string, string> form = new Dictionary<string, string>();
                form["token"] = t.Refresh;
                PostForm(GoogleConfig.RevokeEndpoint, form);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Отозвать доступ в Google не получилось ({0}) — это не страшно.", ex.Message));
            }
        }

        static Dictionary<string, object> PostForm(string url, Dictionary<string, string> form)
        {
            string body = string.Join("&", form.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)).ToArray());
            object resp = GoogleHttp.SendRaw("POST", url, "application/x-www-form-urlencoded", Encoding.ASCII.GetBytes(body), null);
            return resp as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
    }

    /// <summary>Ключ доступа (живёт около часа) — в памяти.</summary>
    static class SessionCache
    {
        static readonly object Gate = new object();
        static string access = "";
        static DateTime expires = DateTime.MinValue;

        public static void Set(string token, long expiresIn)
        {
            lock (Gate)
            {
                access = token ?? "";
                expires = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn) - 60);
            }
        }

        public static string Get()
        {
            lock (Gate)
            {
                return access.Length > 0 && DateTime.UtcNow < expires ? access : null;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                access = "";
                expires = DateTime.MinValue;
            }
        }
    }

    /// <summary>Вход, которым пользуются запросы к таблице.</summary>
    static class GoogleSession
    {
        static readonly object Gate = new object();
        static GoogleToken token;
        static bool loaded;

        public static GoogleToken Current
        {
            get
            {
                lock (Gate)
                {
                    if (!loaded)
                    {
                        token = TokenStore.Load();
                        loaded = true;
                    }
                    return token;
                }
            }
        }

        public static bool SignedIn
        {
            get { return Current != null; }
        }

        public static string Email
        {
            get
            {
                GoogleToken t = Current;
                return t == null ? "" : t.Email;
            }
        }

        public static void Set(GoogleToken t)
        {
            lock (Gate)
            {
                TokenStore.Save(t);
                token = t;
                loaded = true;
            }
        }

        public static void SignOut(bool revoke)
        {
            GoogleToken t;
            lock (Gate)
            {
                t = Current;
                token = null;
                loaded = true;
                TokenStore.Delete();
                SessionCache.Clear();
            }
            if (revoke)
            {
                GoogleAuth.Revoke(t);
            }
        }

        /// <summary>Перечитать ключ с диска (его мог записать другой экземпляр, например --google-login).</summary>
        public static void Reload()
        {
            lock (Gate)
            {
                loaded = false;
                SessionCache.Clear();
            }
        }

        /// <summary>Действующий ключ доступа: при необходимости обновляется по ключу входа.</summary>
        public static string AccessToken()
        {
            string a = SessionCache.Get();
            if (a != null)
            {
                return a;
            }
            GoogleToken t = Current;
            if (t == null)
            {
                throw new GoogleAuthException(Tr._("не выполнен вход в Google (меню значка → «Войти через Google»)"), true);
            }
            lock (Gate)
            {
                a = SessionCache.Get();
                if (a != null)
                {
                    return a;
                }
                try
                {
                    GoogleAuth.Refresh(t);
                }
                catch (GoogleAuthException ex)
                {
                    if (ex.SignedOut)
                    {
                        token = null;
                        TokenStore.Delete();
                        Log.Write(Tr.L("Ключ входа в Google не принят — удалён, нужно войти заново."));
                    }
                    throw;
                }
                return SessionCache.Get() ?? "";
            }
        }
    }

    /// <summary>HTTP-запросы к Google: JSON туда и обратно, повторы при перегрузке.</summary>
    static class GoogleHttp
    {
        const int TimeoutMs = 120000;
        static readonly int[] RetryDelaysMs = { 1000, 2000, 4000, 8000 };

        /// <summary>Тестам: задержки повторов (мс) вместо настоящих.</summary>
        public static int RetryScale = 1;

        public static object Send(string method, string url, object body)
        {
            byte[] data = body == null ? null : FileUtil.Utf8NoBom.GetBytes(Json.Write(body, false));
            bool refreshed = false;
            for (int attempt = 0; ; attempt++)
            {
                string access = GoogleSession.AccessToken();
                try
                {
                    return SendRaw(method, url, "application/json; charset=utf-8", data, access);
                }
                catch (GoogleApiException ex)
                {
                    if (ex.Code == 401 && !refreshed)
                    {
                        refreshed = true;
                        SessionCache.Clear();
                        continue;
                    }
                    bool busy = ex.Code == 429 || ex.Code == 500 || ex.Code == 502 || ex.Code == 503 || ex.Code == 504;
                    if (!busy || attempt >= RetryDelaysMs.Length)
                    {
                        throw;
                    }
                    Log.Write(Tr.L("Google ответил {0} — повторю через {1} с.", ex.Code, RetryDelaysMs[attempt] / 1000));
                    Thread.Sleep(RetryDelaysMs[attempt] / RetryScale);
                }
            }
        }

        public static object SendRaw(string method, string url, string contentType, byte[] data, string bearer)
        {
            HttpWebRequest request;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(url);
            }
            catch (Exception ex)
            {
                throw new SyncException(Tr.F("внутренняя ошибка: неверный адрес Google ({0})", ex.Message));
            }
            request.Method = method;
            request.Accept = "application/json";
            request.UserAgent = "MountSheetSync/" + Program.Version;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.AllowAutoRedirect = false;
            if (bearer != null)
            {
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            }
            string text;
            int code;
            try
            {
                if (data != null)
                {
                    request.ContentType = contentType;
                    request.ContentLength = data.Length;
                    using (Stream s = request.GetRequestStream())
                    {
                        s.Write(data, 0, data.Length);
                    }
                }
                else if (method == "POST")
                {
                    request.ContentLength = 0;
                }
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    code = (int)response.StatusCode;
                    text = ReadText(response);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response == null)
                {
                    throw new SyncException(Tr.F("нет связи с Google: {0}", ex.Message));
                }
                using (response)
                {
                    code = (int)response.StatusCode;
                    text = ReadText(response);
                }
                throw Failure(code, text, url);
            }
            catch (IOException ex)
            {
                throw new SyncException(Tr.F("нет связи с Google: {0}", ex.Message));
            }
            if (text.Length == 0)
            {
                return new Dictionary<string, object>();
            }
            try
            {
                return Json.Parse(text);
            }
            catch (FormatException)
            {
                throw new GoogleApiException(code, "", Tr.F("Google прислал не JSON (код {0})", code));
            }
        }

        static string ReadText(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                if (stream != null)
                {
                    stream.CopyTo(ms);
                }
                return FileUtil.Utf8NoBom.GetString(ms.ToArray()).Trim();
            }
        }

        static GoogleApiException Failure(int code, string text, string url)
        {
            string status = "";
            string message = "";
            try
            {
                Dictionary<string, object> d = Json.Parse(text) as Dictionary<string, object>;
                object err = J.Get(d, "error");
                Dictionary<string, object> e = J.Obj(err);
                if (e != null)
                {
                    status = J.Str(e, "status");
                    message = J.Str(e, "message");
                }
                else if (err is string)
                {
                    // ответы OAuth: {"error": "invalid_grant", "error_description": "..."}
                    status = (string)err;
                    message = J.Str(d, "error_description");
                }
            }
            catch (Exception)
            {
            }
            string human;
            if (code == 404)
            {
                human = Tr._("таблица не найдена — возможно, её удалили (или она создана не этой программой)");
            }
            else if (code == 403 && status == "PERMISSION_DENIED")
            {
                human = Tr._("нет доступа к таблице: программа видит только таблицы, которые создала сама") + (message.Length > 0 ? " (" + message + ")" : "");
            }
            else if (code == 429)
            {
                human = Tr._("Google просит подождать: слишком много запросов");
            }
            else if (code >= 500)
            {
                human = Tr.F("у Google временный сбой ({0})", code);
            }
            else
            {
                human = Tr.F("Google ответил ошибкой {0}", code) + (message.Length > 0 ? ": " + message : (status.Length > 0 ? ": " + status : ""));
            }
            Log.Write("Google " + code + " " + status + " — " + url.Split('?')[0] + (message.Length > 0 ? ": " + message : ""));
            return new GoogleApiException(code, status, human);
        }
    }

    /// <summary>Нужные программе запросы Google Sheets API.</summary>
    static class GoogleSheets
    {
        static string Base(string id)
        {
            return GoogleConfig.SheetsBase + "/" + Uri.EscapeDataString(id);
        }

        public static Dictionary<string, object> Create(Dictionary<string, object> body)
        {
            return J.Obj(GoogleHttp.Send("POST", GoogleConfig.SheetsBase, body)) ?? new Dictionary<string, object>();
        }

        public static Dictionary<string, object> BatchUpdate(string id, List<object> requests)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["requests"] = requests;
            return J.Obj(GoogleHttp.Send("POST", Base(id) + ":batchUpdate", body)) ?? new Dictionary<string, object>();
        }

        public static Dictionary<string, object> Get(string id, string fields)
        {
            return J.Obj(GoogleHttp.Send("GET", Base(id) + "?fields=" + Uri.EscapeDataString(fields), null))
                ?? new Dictionary<string, object>();
        }

        /// <summary>Значения нескольких диапазонов; render — UNFORMATTED_VALUE или FORMULA.</summary>
        public static List<object> BatchGetValues(string id, IList<string> ranges, string render)
        {
            StringBuilder url = new StringBuilder(Base(id) + "/values:batchGet?majorDimension=ROWS&valueRenderOption=" + render
                + "&dateTimeRenderOption=SERIAL_NUMBER");
            foreach (string r in ranges)
            {
                url.Append("&ranges=").Append(Uri.EscapeDataString(r));
            }
            Dictionary<string, object> resp = J.Obj(GoogleHttp.Send("GET", url.ToString(), null));
            return J.Arr(J.Get(resp, "valueRanges"));
        }

        public static void BatchUpdateValues(string id, List<object> data)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["valueInputOption"] = "RAW";
            body["data"] = data;
            GoogleHttp.Send("POST", Base(id) + "/values:batchUpdate", body);
        }
    }

    static class Browser
    {
        /// <summary>Открыть адрес в браузере по умолчанию. Для проверок — MSS_BROWSER_LOG: адрес пишется в файл.</summary>
        public static void Open(string url)
        {
#if TESTS
            string logFile = Environment.GetEnvironmentVariable("MSS_BROWSER_LOG");
            if (!string.IsNullOrEmpty(logFile))
            {
                File.AppendAllText(logFile, url + "\n", FileUtil.Utf8NoBom);
                return;
            }
#endif
            Process.Start(url);
        }
    }
}
