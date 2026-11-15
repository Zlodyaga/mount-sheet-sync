// Папка данных по выбору игрока: настройки, список «на этом компьютере», копии, журнал. Обычная папка —
// %APPDATA%\MountSheetSync; в ней же лежит указатель data-folder.txt с путём выбранной. Ключ --data главнее указателя.

using System;
using System.IO;

namespace MountSheetSync
{
    static class DataFolder
    {
        public const string PointerName = "data-folder.txt";

        public static string DefaultDir
        {
            get
            {
#if TESTS
                // сборка для тестов не трогает настоящую папку пользователя
                string test = Environment.GetEnvironmentVariable("MSS_DEFAULT_DATA");
                if (!string.IsNullOrEmpty(test))
                {
                    return Path.GetFullPath(test);
                }
#endif
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MountSheetSync");
            }
        }

        public static string PointerFile
        {
            get { return Path.Combine(DefaultDir, PointerName); }
        }

        /// <summary>Папка из указателя; null — указателя нет или папка пропала (тогда работаем в обычной).</summary>
        public static string Chosen()
        {
            try
            {
                if (!File.Exists(PointerFile))
                {
                    return null;
                }
                string dir = File.ReadAllText(PointerFile, FileUtil.Utf8NoBom).Trim();
                return dir.Length > 0 && Directory.Exists(dir) ? Path.GetFullPath(dir) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool Same(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>В папке уже есть данные программы (тогда их берём как есть, ничего не копируя).</summary>
        public static bool HasData(string dir)
        {
            return File.Exists(Path.Combine(dir, "settings.json")) || File.Exists(Path.Combine(dir, "local-data.json"));
        }

        /// <summary>
        /// Перейти в другую папку: файлы нынешней копируются туда (если там ещё нет данных программы), прежние остаются
        /// на месте. Возвращает false, если это та же папка. Действует со следующего запуска.
        /// </summary>
        public static bool Move(string from, string to)
        {
            to = Path.GetFullPath(to);
            if (Same(from, to))
            {
                return false;
            }
            Directory.CreateDirectory(to);
            if (!HasData(to))
            {
                Copy(from, to);
            }
            Directory.CreateDirectory(DefaultDir);
            if (Same(to, DefaultDir))
            {
                File.Delete(PointerFile);
            }
            else
            {
                File.WriteAllText(PointerFile, to, FileUtil.Utf8NoBom);
            }
            return true;
        }

        static void Copy(string from, string to)
        {
            foreach (string file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);
                if (name == PointerName || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                File.Copy(file, Path.Combine(to, name), true);
            }
            foreach (string dir in Directory.GetDirectories(from))
            {
                string target = Path.Combine(to, Path.GetFileName(dir));
                if (Same(dir, to))
                {
                    continue;   // новая папка внутри прежней
                }
                Directory.CreateDirectory(target);
                Copy(dir, target);
            }
        }
    }
}
