// Языки программы: английский (по умолчанию и запасной), русский, немецкий, французский, испанский,
// португальский. Язык — как в Windows, если программа его знает, иначе английский; выбрать другой можно
// в меню значка → «Язык». Текст в коде — русский оригинал, он же ключ перевода (i18n.json).
//   Tr._("…")          — надпись для человека: переведена на все языки;
//   Tr.F("…{0}…", x)   — то же с подстановками;
//   Tr.L("…{0}…", x)   — строка журнала работы: только английский и русский.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MountSheetSync
{
    static class Tr
    {
        public static readonly string[] Languages = { "en", "ru", "de", "fr", "es", "pt" };

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "en", "English" }, { "ru", "Русский" }, { "de", "Deutsch" },
            { "fr", "Français" }, { "es", "Español" }, { "pt", "Português" },
        };

        static Dictionary<string, Dictionary<string, string>> tables;
        static string lang = "en";
        static string setting = "auto";

        /// <summary>Язык сейчас: "en", "ru", …</summary>
        public static string Lang
        {
            get { return lang; }
        }

        /// <summary>Выбор в настройках: "auto" (как в Windows) или код языка.</summary>
        public static string Setting
        {
            get { return setting; }
        }

        public static event Action Changed;

        /// <summary>Язык Windows, если он есть в программе (иначе английский).</summary>
        public static string SystemLanguage()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
#if TESTS
            string forced = Environment.GetEnvironmentVariable("MSS_SYSTEM_LANG");
            if (!string.IsNullOrEmpty(forced))
            {
                two = forced;
            }
#endif
            two = (two ?? "").ToLowerInvariant();
            return Languages.Contains(two) ? two : "en";
        }

        public static string Normalize(string code)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            return code.Length == 0 || code == "auto" ? "auto" : (Languages.Contains(code) ? code : "auto");
        }

        /// <summary>Выбрать язык ("auto" — как в Windows).</summary>
        public static void Use(string code)
        {
            setting = Normalize(code);
            string next = setting == "auto" ? SystemLanguage() : setting;
            bool changed = next != lang;
            lang = next;
            if (changed && Changed != null)
            {
                Changed();
            }
        }

        static Dictionary<string, Dictionary<string, string>> Tables
        {
            get
            {
                if (tables == null)
                {
                    tables = Load();
                }
                return tables;
            }
        }

        static Dictionary<string, Dictionary<string, string>> Load()
        {
            Dictionary<string, Dictionary<string, string>> t = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                using (Stream s = typeof(Tr).Assembly.GetManifestResourceStream("MountSheetSync.i18n.json"))
                {
                    if (s == null)
                    {
                        return t;
                    }
                    using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                    {
                        Dictionary<string, object> root = Json.Parse(reader.ReadToEnd()) as Dictionary<string, object>;
                        foreach (KeyValuePair<string, object> kv in root ?? new Dictionary<string, object>())
                        {
                            Dictionary<string, object> table = kv.Value as Dictionary<string, object>;
                            if (table == null)
                            {
                                continue;
                            }
                            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (KeyValuePair<string, object> e in table)
                            {
                                d[e.Key] = Convert.ToString(e.Value, CultureInfo.InvariantCulture);
                            }
                            t[kv.Key] = d;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // без переводов программа всё равно работает — по-русски
            }
            return t;
        }

        static string Lookup(string ru, string language)
        {
            if (language == "ru")
            {
                return ru;
            }
            Dictionary<string, string> d;
            string v;
            if (Tables.TryGetValue(language, out d) && d.TryGetValue(ru, out v) && v.Length > 0)
            {
                return v;
            }
            if (language != "en" && Tables.TryGetValue("en", out d) && d.TryGetValue(ru, out v) && v.Length > 0)
            {
                return v;
            }
            return ru;
        }

        /// <summary>Надпись на текущем языке.</summary>
        public static string _(string ru)
        {
            return Lookup(ru, lang);
        }

        /// <summary>Надпись с подстановками {0}, {1}…</summary>
        public static string F(string ru, params object[] args)
        {
            return Format(Lookup(ru, lang), args);
        }

        /// <summary>Строка журнала: по-русски для русского языка, иначе по-английски.</summary>
        public static string L(string ru, params object[] args)
        {
            return Format(Lookup(ru, lang == "ru" ? "ru" : "en"), args);
        }

        /// <summary>
        /// Текст начинается с этой фразы на каком-нибудь из языков (сообщения об ошибках узнаются по началу:
        /// «нет связи…», «внутренняя ошибка…»; переводы этих сообщений начинаются с перевода самой фразы).
        /// </summary>
        public static bool StartsWith(string text, string ruPrefix)
        {
            text = text ?? "";
            foreach (string language in Languages)
            {
                string p = Lookup(ruPrefix, language);
                if (p.Length > 0 && text.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        static string Format(string text, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return text;
            }
            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, args);
            }
            catch (FormatException)
            {
                return text + " " + string.Join(" ", args.Select(a => Convert.ToString(a, CultureInfo.CurrentCulture)).ToArray());
            }
        }
    }
}
