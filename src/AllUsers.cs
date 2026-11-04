using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // The all-users install in C:\Program Files\BusyLightz, and the move to it from copies installed just for one
    // user (in %LOCALAPPDATA%\Programs, from before it existed). Settings, the Teams sign-in and the log are kept per
    // user either way, so they carry over.
    static class AllUsersInstall
    {
        // Copies installed just for one user were installed by the MSI with this upgrade code.
        const string JustForMeUpgradeCode = "{B7D1A4C2-58E3-4F0B-A96D-2C4E7F81D935}";
        // A just-for-me copy's sign-in entry, under HKCU\...\Run.
        public const string JustForMeRunValue = "Krabbz9999 Blynclight App Control";

        static string ProgramFiles { get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } }
        public static string ExePath { get { return Path.Combine(ProgramFiles, @"BusyLightz\BusyLightz.exe"); } }
        // Only administrators can write here, so the background updater can trust what it finds.
        public static string UpdatesDir { get { return Path.Combine(ProgramFiles, "BusyLightz Updates"); } }
        // Present while the background updater installs an update; each user's copy closes and reopens after it.
        public static string UpdatingFlag { get { return Path.Combine(UpdatesDir, "updating.flag"); } }

        // True when this is the all-users copy.
        public static bool IsThisCopy
        {
            get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), ExePath, StringComparison.OrdinalIgnoreCase); }
        }

        // True when this is a just-for-me copy and the all-users copy is installed, so this one should hand over.
        public static bool ShouldHandOff { get { return !IsThisCopy && File.Exists(ExePath); } }

        // True while an update is being installed by the background updater. A flag left behind by an updater that
        // didn't finish is ignored after a while, so the app can't keep closing because of it.
        public static bool UpdateInProgress
        {
            get
            {
                try { return File.Exists(UpdatingFlag) && DateTime.Now - File.GetLastWriteTime(UpdatingFlag) < TimeSpan.FromMinutes(15); }
                catch (Exception) { return false; }
            }
        }

        // Starts the all-users copy once this one has closed. The caller exits right away; the all-users copy then
        // removes this one.
        public static void HandOff(bool showWindow)
        {
            RunHidden("ping -n 3 127.0.0.1 >nul & start \"\" \"" + ExePath + "\"" + (showWindow ? "" : " --tray"));
        }

        // Reopens this copy once the background updater's flag is gone (or after 5 minutes). The caller exits right
        // away so the installer can replace the file.
        public static void ReopenAfterUpdate(bool showWindow)
        {
            RunHidden(ReopenScript(UpdatingFlag, "\"" + ExePath + "\"" + (showWindow ? " --updated" : " --tray --updated")));
        }

        // The wait loop is in brackets: without them cmd treats the rest of the line as part of the loop, so the
        // app would only be started while the flag was still there.
        public static string ReopenScript(string flag, string command)
        {
            return "ping -n 6 127.0.0.1 >nul" +
                " & (for /l %i in (1,1,300) do @if exist \"" + flag + "\" ping -n 2 127.0.0.1 >nul)" +
                " & start \"\" " + command;
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);

        // Uninstalls this user's just-for-me copies (no admin rights needed), and their sign-in entry with them.
        public static void RemoveJustForMeCopies()
        {
            var codes = new List<string>();
            for (uint i = 0; ; i++)
            {
                var code = new StringBuilder(39);
                if (MsiEnumRelatedProducts(JustForMeUpgradeCode, 0, i, code) != 0) break;
                codes.Add(code.ToString());
            }
            foreach (string code in codes)
            {
                Program.LogEvent("Removing the copy installed just for this user (" + code + ")");
                var uninstall = new ProcessStartInfo("msiexec.exe", "/x " + code + " /qn") { UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(uninstall)) process.WaitForExit(5 * 60 * 1000);
            }
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (run != null) run.DeleteValue(JustForMeRunValue, false);
        }

        // Downloads the all-users installer and runs it with a Windows admin prompt. Returns false if the user said
        // no to the prompt; throws if it couldn't be downloaded or failed.
        public static bool InstallForEveryone()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Krabbz9999 Blynclight\updates");
            var setup = Updater.DownloadAllUsersIfNewer(Program.UpdateRepo, null, dir);
            if (setup == null) throw new InvalidOperationException("The installer for everyone on this PC isn't published yet.");
            var install = new ProcessStartInfo("msiexec.exe", "/i \"" + setup.MsiPath + "\" /passive") { UseShellExecute = true, Verb = "runas" };
            try
            {
                using (var process = Process.Start(install))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0 && process.ExitCode != 3010)
                        throw new InvalidOperationException("The installer stopped with code " + process.ExitCode + ".");
                }
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return false; // The admin prompt was cancelled.
                throw;
            }
        }

        static void RunHidden(string script)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
    }

    // BusyLightz.exe --system-update, run as SYSTEM by the "BusyLightz Updater" scheduled task the all-users installer
    // adds: installs a newer release's all-users installer silently, since Program Files needs admin rights.
    static class SystemUpdater
    {
        static string LogPath { get { return Path.Combine(AllUsersInstall.UpdatesDir, "updater.log"); } }

        public static int Run()
        {
            // Only as SYSTEM, from Program Files: anywhere else, a user could have put this exe there.
            if (!WindowsIdentity.GetCurrent().IsSystem || !AllUsersInstall.IsThisCopy || Program.UpdateRepo == null) return 1;
            try
            {
                Directory.CreateDirectory(AllUsersInstall.UpdatesDir);
                // Another run (the scheduled one, or one started by hand) is installing already; two at once collide.
                if (AllUsersInstall.UpdateInProgress)
                {
                    Log("An update is already installing; skipped");
                    return 0;
                }
                var update = Updater.DownloadAllUsersIfNewer(Program.UpdateRepo, Program.Version, AllUsersInstall.UpdatesDir);
                if (update == null)
                {
                    Log("Up to date (version " + Program.VersionText + ")");
                    return 0;
                }
                Log("Installing version " + update.Version);
                // Each user's copy sees the flag, closes, and reopens once it's gone.
                File.WriteAllText(AllUsersInstall.UpdatingFlag, update.Version.ToString());
                WaitForOtherCopiesToClose(TimeSpan.FromSeconds(30));
                // A copy still open now is stuck closing (it would keep the installer waiting); end it. Each copy's
                // reopen helper starts the new version once the update's done.
                EndOtherCopies();
                string log = Path.Combine(AllUsersInstall.UpdatesDir, "install-" + update.Version + ".log");
                // From a helper, so this exe isn't in use when the installer replaces it.
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul" +
                    " & start \"\" /wait msiexec.exe /i \"" + update.MsiPath + "\" /qn /norestart /l*v \"" + log + "\"" +
                    " & del /f /q \"" + AllUsersInstall.UpdatingFlag + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return 0;
            }
            catch (Exception ex)
            {
                Log("Update failed: " + ex.Message);
                try { File.Delete(AllUsersInstall.UpdatingFlag); } catch (Exception) { }
                return 1;
            }
        }

        static void WaitForOtherCopiesToClose(TimeSpan timeout)
        {
            var until = DateTime.Now + timeout;
            int self = Process.GetCurrentProcess().Id;
            while (DateTime.Now < until)
            {
                bool others = false;
                foreach (var process in Process.GetProcessesByName("BusyLightz"))
                    using (process) if (process.Id != self) others = true;
                if (!others) return;
                Thread.Sleep(1000);
            }
        }

        static void EndOtherCopies()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var process in Process.GetProcessesByName("BusyLightz"))
                using (process)
                {
                    if (process.Id == self) continue;
                    try
                    {
                        process.Kill();
                        Log("Ended a copy that didn't close for the update (process " + process.Id + ")");
                    }
                    catch (Exception ex) { Log("Couldn't end process " + process.Id + ": " + ex.Message); }
                }
        }

        // updater.log in the updates folder, kept short.
        static void Log(string message)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("s") + "  " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
