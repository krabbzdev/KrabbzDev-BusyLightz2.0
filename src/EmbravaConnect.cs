using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Krabbz.Blynclight
{
    // Embrava Connect, Embrava's own app, fights BusyLightz over the light. BusyLightz closes it when it finds it running
    // (see TrayApp.CheckEmbrava) and never starts it.
    public static class EmbravaConnect
    {
        const string ProcessName = "EmbravaConnect";
        const string DefaultPath = @"C:\Program Files (x86)\Embrava\Embrava Connect\EmbravaConnect.exe";

        public static bool IsInstalled { get { return FindExe() != null; } }

        public static bool IsRunning
        {
            get
            {
                int session = Process.GetCurrentProcess().SessionId;
                foreach (var running in Process.GetProcessesByName(ProcessName))
                    using (running) if (running.SessionId == session) return true;
                return false;
            }
        }

        // Closes Embrava Connect for this Windows session. It starts again at the next sign-in unless it's uninstalled.
        public static void Quit()
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var running in Process.GetProcessesByName(ProcessName))
            {
                using (running)
                {
                    if (running.SessionId != session) continue;
                    try
                    {
                        running.Kill();
                        running.WaitForExit(5000);
                    }
                    catch (Exception) { } // Already exiting.
                }
            }
        }

        // Prefers the path from Embrava Connect's own startup entry, in case it's installed elsewhere.
        static string FindExe()
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var keyPath in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" })
                {
                    using (var run = hive.OpenSubKey(keyPath))
                    {
                        string value = run == null ? null : run.GetValue("Embrava Connect") as string;
                        if (value == null) continue;
                        value = value.Trim().Trim('"');
                        if (File.Exists(value)) return value;
                    }
                }
            }
            return File.Exists(DefaultPath) ? DefaultPath : null;
        }
    }
}
