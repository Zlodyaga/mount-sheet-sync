// Установка без установщика: программа сама себе установщик. С сайта скачивается один MountSheetSync.exe;
// при первом запуске из «Загрузок» (или с рабочего стола, или прямо из архива) она предлагает установиться:
// копирует себя в %LOCALAPPDATA%\Programs\MountSheetSync (права администратора не нужны), кладёт ярлык в меню
// «Пуск», записывает себя в «Установленные приложения» Windows (оттуда её можно удалить — это запуск с --uninstall)
// и запускается уже оттуда. Отказались — работает из той папки, где лежит (как раньше из архива).

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MountSheetSync
{
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MountSheetSync";
        const string ShortcutName = "Mount Sheet Sync.lnk";

        public static string InstallDir
        {
            get
            {
#if TESTS
                string test = Environment.GetEnvironmentVariable("MSS_INSTALL_DIR");
                if (!string.IsNullOrEmpty(test))
                {
                    return Path.GetFullPath(test);
                }
#endif
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                    "MountSheetSync");
            }
        }

        public static string InstalledExe
        {
            get { return Path.Combine(InstallDir, "MountSheetSync.exe"); }
        }

        /// <summary>Работает установленная копия.</summary>
        public static bool IsInstalledCopy
        {
            get { return SamePath(Updater.ExePath, InstalledExe); }
        }

        static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool Under(string file, string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return false;
            }
            try
            {
                string d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return Path.GetFullPath(file).StartsWith(d, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Папка «Загрузки» (Windows хранит её отдельно от остальных особых папок).</summary>
        static string DownloadsDir()
        {
#if TESTS
            string test = Environment.GetEnvironmentVariable("MSS_DOWNLOADS_DIR");
            if (!string.IsNullOrEmpty(test))
            {
                return test;
            }
#endif
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                {
                    string v = k == null ? null : k.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
                    if (!string.IsNullOrEmpty(v))
                    {
                        return Environment.ExpandEnvironmentVariables(v);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>
        /// Предложить установку: программа запущена из «Загрузок», с рабочего стола или из архива (временная папка),
        /// а не из своей папки установки. Из любой другой папки (например, распакованный архив в «Документах»)
        /// работает как есть.
        /// </summary>
        public static bool ShouldOffer()
        {
#if TESTS
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSS_INSTALL_DIR")))
            {
                return false;   // проверки программы, кроме проверок установки, идут из временных папок
            }
#endif
            if (IsInstalledCopy)
            {
                return false;
            }
            string exe = Updater.ExePath;
            try
            {
                if (SamePath(Settings.Load(AppPaths.SettingsFile).InstallDeclined, exe))
                {
                    return false;   // отсюда ставить уже отказались
                }
            }
            catch (Exception)
            {
            }
            return Under(exe, DownloadsDir()) || Under(exe, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                || Program.RunningFromTemp;
        }

        /// <summary>Первый запуск из «Загрузок»: спросить. true — установлено и запущено оттуда (этот процесс выходит).</summary>
        public static bool OfferAndInstall(string suffix)
        {
            bool update = File.Exists(InstalledExe);
            DialogResult answer = MessageBox.Show(
                (update ? Tr._("Обновить установленный Mount Sheet Sync этой версией?") : Tr._("Установить Mount Sheet Sync на этот компьютер?"))
                + "\n\n" + Tr.F("Программа скопирует себя в {0}, появится в меню «Пуск» и в списке установленных приложений Windows (оттуда её можно удалить). Права администратора не нужны.", InstallDir)
                + "\n\n" + Tr._("«Да» — установить и запустить. «Нет» — запустить без установки, прямо отсюда."),
                Program.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                return true;   // ничего не делать и выйти
            }
            if (answer == DialogResult.No)
            {
                try
                {
                    Settings s = Settings.Load(AppPaths.SettingsFile);
                    s.InstallDeclined = Updater.ExePath;
                    s.Save(AppPaths.SettingsFile);
                }
                catch (Exception)
                {
                }
                return false;
            }
            try
            {
                CloseRunning(suffix);
                string exe = Install();
                Launch(exe);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Установка не удалась: {0}", ex.Message));
                MessageBox.Show(Tr.F("Установить не получилось: {0}\n\nПрограмма запустится прямо отсюда.", ex.Message),
                    Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>Скопировать себя в папку установки, ярлык, запись в «Установленных приложениях». Возвращает путь exe.</summary>
        public static string Install()
        {
            string target = InstalledExe;
            Directory.CreateDirectory(InstallDir);
            if (!SamePath(Updater.ExePath, target))
            {
                string tmp = target + ".new";
                File.Copy(Updater.ExePath, tmp, true);
                if (File.Exists(target))
                {
                    string old = target + ".old";
                    if (File.Exists(old))
                    {
                        File.Delete(old);
                    }
                    File.Move(target, old);   // работающий exe можно переименовать (удалится при следующем запуске)
                }
                File.Move(tmp, target);
            }
            CreateShortcut(target);
            Register(target);
            Log.Write(Tr.L("Программа установлена: {0}", target));
            return target;
        }

        static void Launch(string exe)
        {
            string args = "--wait-instance";
            if (AppPaths.CustomDataDir != null)
            {
                args += " --data \"" + AppPaths.CustomDataDir + "\"";
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            Process.Start(psi);
        }

        static string StartMenuShortcut
        {
            get
            {
#if TESTS
                string test = Environment.GetEnvironmentVariable("MSS_INSTALL_DIR");
                if (!string.IsNullOrEmpty(test))
                {
                    return Path.Combine(Path.GetFullPath(test), ShortcutName);
                }
#endif
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            }
        }

        /// <summary>Ярлык в меню «Пуск» (через WScript.Shell; нет его — без ярлыка).</summary>
        static void CreateShortcut(string exe)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { StartMenuShortcut });
                Type lt = link.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { Tr._("Google-таблица фарма маунтов для World of Warcraft") });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Ярлык в меню «Пуск» не создан: {0}", ex.Message));
            }
        }

        /// <summary>Запись в «Установленных приложениях» (HKCU — для этого пользователя, без прав администратора).</summary>
        static void Register(string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Program.Title, RegistryValueKind.String);
                k.SetValue("DisplayVersion", Program.FullVersion, RegistryValueKind.String);
                k.SetValue("Publisher", "Zlodyaga", RegistryValueKind.String);
                k.SetValue("DisplayIcon", exe + ",0", RegistryValueKind.String);
                k.SetValue("InstallLocation", Path.GetDirectoryName(exe), RegistryValueKind.String);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall", RegistryValueKind.String);
                k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --yes", RegistryValueKind.String);
                k.SetValue("URLInfoAbout", "https://zlodyaga.github.io/", RegistryValueKind.String);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
                catch (Exception)
                {
                }
            }
        }

        public static bool Registered
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    {
                        return k != null;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>При запуске установленной копии: версия в «Установленных приложениях» — как у программы (после обновления).</summary>
        public static void Refresh()
        {
            if (!IsInstalledCopy)
            {
                return;
            }
            try
            {
                CleanupOld();
                Register(InstalledExe);
            }
            catch (Exception)
            {
            }
        }

        static void CleanupOld()
        {
            try
            {
                if (File.Exists(InstalledExe + ".old"))
                {
                    File.Delete(InstalledExe + ".old");
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Работающую программу (значок в трее) попросить выйти и дождаться этого.</summary>
        static void CloseRunning(string suffix)
        {
            if (!SingleInstance.RequestExit(suffix))
            {
                return;
            }
            using (Mutex m = new Mutex(false, Program.MutexName(suffix)))
            {
                try
                {
                    if (m.WaitOne(15000))
                    {
                        m.ReleaseMutex();
                    }
                }
                catch (AbandonedMutexException)
                {
                }
            }
        }

        /// <summary>--uninstall: убрать программу (ярлык, автозапуск, запись в Windows, папку); настройки — по желанию.</summary>
        public static int Uninstall(bool quiet, string suffix)
        {
            if (!quiet && MessageBox.Show(Tr._("Удалить Mount Sheet Sync с этого компьютера?") + "\n\n"
                + Tr._("Таблица на Google Диске останется. Аддон в игре не удаляется."),
                Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return 1;
            }
            CloseRunning(suffix);
            try
            {
                if (Autostart.IsEnabled())
                {
                    Autostart.Set(false);
                }
            }
            catch (Exception)
            {
            }
            try
            {
                if (File.Exists(StartMenuShortcut))
                {
                    File.Delete(StartMenuShortcut);
                }
            }
            catch (Exception)
            {
            }
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch (Exception)
            {
            }
            if (!quiet && MessageBox.Show(Tr.F("Удалить и настройки программы — вход в Google, журнал, список отправленного ({0})?", AppPaths.DataDir),
                Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                try
                {
                    GoogleSession.SignOut(true);
                }
                catch (Exception)
                {
                }
                Log.Close();
                try
                {
                    Directory.Delete(AppPaths.DataDir, true);
                }
                catch (Exception)
                {
                }
            }
            else
            {
                Log.Write(Tr.L("Программа удалена; настройки остались в {0}.", AppPaths.DataDir));
            }
            RemoveFolder();
            if (!quiet)
            {
                MessageBox.Show(Tr._("Mount Sheet Sync удалён."), Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        /// <summary>Папку установки — после выхода (работающий exe Windows удалить не даёт).</summary>
        static void RemoveFolder()
        {
            string dir = InstallDir;
            if (!Directory.Exists(dir))
            {
                return;
            }
            if (!IsInstalledCopy || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception)
                {
                }
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }
    }
}
