using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Krabbz.Blynclight
{
    // "Email crash log": gathers app.log into one file and opens a new email with it attached, so users can send
    // what went wrong without hunting for the log folder.
    static class CrashReport
    {
        // Where crash logs are sent; empty leaves the To line for the user to fill in.
        public static readonly string SupportEmail = "clove@showtechsolutions.com";

        static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight"); }
        }

        // Opens the email in the background, so a slow mail app doesn't freeze the window.
        public static void Email()
        {
            var thread = new Thread(delegate () { Program.Guard("Emailing the crash log", Send); });
            // Simple MAPI needs a single-threaded apartment, like the UI thread.
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        static void Send()
        {
            string file = WriteReport();
            string subject = Program.AppName + " crash log (version " + Program.VersionText + ")";
            string body = "What were you doing when it crashed?\r\n\r\n\r\n";
            // Classic Outlook and most desktop mail apps attach the file directly. The new Outlook and webmail
            // can't be handed a file, so show it in its folder and start an email to drag it into.
            if (SendWithAttachment(subject, body, file)) return;
            Process.Start("explorer.exe", "/select,\"" + file + "\"");
            Process.Start("mailto:" + SupportEmail + "?subject=" + Uri.EscapeDataString(subject)
                + "&body=" + Uri.EscapeDataString(body + "Please attach the log file from the folder that just opened:\r\n" + file));
        }

        // Writes the version, Windows version and both logs (newest last) into one file in %TEMP%.
        static string WriteReport()
        {
            var text = new StringBuilder();
            text.AppendLine(Program.AppName + " " + Program.VersionText + " (built " + Program.BuildDate + ")");
            text.AppendLine(Program.WindowsVersion + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"));
            text.AppendLine("Saved " + DateTime.Now.ToString("s"));
            bool anyLog = false;
            foreach (string name in new[] { "app.old.log", "app.log" })
            {
                string path = Path.Combine(DataDir, name);
                if (!File.Exists(path)) continue;
                anyLog = true;
                text.AppendLine();
                text.AppendLine("===== " + name + " =====");
                try { text.Append(File.ReadAllText(path)); }
                catch (IOException ex) { text.AppendLine("Couldn't read it: " + ex.Message); }
            }
            if (!anyLog)
            {
                text.AppendLine();
                text.AppendLine("No app.log found in " + DataDir + ": the app has not logged any errors on this PC.");
                text.AppendLine("If it still closes by itself, the Windows event log below usually shows why.");
            }
            AppendEventLog(text);
            string file = Path.Combine(Path.GetTempPath(), "BusyLightz-crash-log-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".txt");
            File.WriteAllText(file, text.ToString());
            return file;
        }

        // Adds this app's crashes and hangs from the Windows Application event log (last 14 days). A crash inside
        // Windows itself or a USB driver never reaches app.log, but Windows records it here with the faulting module.
        static void AppendEventLog(StringBuilder text)
        {
            text.AppendLine();
            text.AppendLine("===== Windows event log (Application), last 14 days =====");
            try
            {
                string exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
                DateTime since = DateTime.Now.AddDays(-14);
                int found = 0, scanned = 0;
                using (var log = new EventLog("Application"))
                {
                    var entries = log.Entries;
                    // Newest entries are last; walk backwards and stop at the cutoff or after a sensible number.
                    for (int i = entries.Count - 1; i >= 0 && scanned < 5000 && found < 15; i--, scanned++)
                    {
                        EventLogEntry entry = entries[i];
                        if (entry.TimeGenerated < since) break;
                        string source = entry.Source ?? "";
                        if (source != "Application Error" && source != ".NET Runtime" && source != "Application Hang"
                            && source != "Windows Error Reporting") continue;
                        string message = entry.Message ?? "";
                        if (message.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0
                            && message.IndexOf("Blynclight", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found++;
                        text.AppendLine();
                        text.AppendLine(entry.TimeGenerated.ToString("s") + "  " + source + " (event " + (entry.InstanceId & 0xFFFF) + ")");
                        text.AppendLine(message.Length > 3000 ? message.Substring(0, 3000) + "..." : message);
                    }
                }
                if (found == 0) text.AppendLine("No crash or hang entries for this app.");
            }
            catch (Exception ex) { text.AppendLine("Couldn't read the Windows event log: " + ex.Message); }
        }

        const int MAPI_LOGON_UI = 0x1, MAPI_DIALOG = 0x8, MAPI_TO = 1, SUCCESS_SUCCESS = 0, MAPI_USER_ABORT = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        class MapiMessage
        {
            public int Reserved;
            public string Subject, NoteText, MessageType, DateReceived, ConversationId;
            public int Flags;
            public IntPtr Originator;
            public int RecipCount;
            public IntPtr Recips;
            public int FileCount;
            public IntPtr Files;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct MapiFileDesc
        {
            public int Reserved, Flags, Position;
            public string PathName, FileName;
            public IntPtr FileType;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct MapiRecipDesc
        {
            public int Reserved, RecipClass;
            public string Name, Address;
            public int EntryIdSize;
            public IntPtr EntryId;
        }

        [DllImport("MAPI32.DLL", CharSet = CharSet.Ansi)]
        static extern int MAPISendMail(IntPtr session, IntPtr hwnd, MapiMessage message, int flags, int reserved);

        // Opens the default mail app's new-email window with the file attached. False if there's no mail app
        // that takes Simple MAPI (e.g. the new Outlook); true if it opened, even if the user then cancelled.
        static bool SendWithAttachment(string subject, string body, string file)
        {
            var attachment = new MapiFileDesc { Position = -1, PathName = file, FileName = Path.GetFileName(file) };
            IntPtr files = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDesc)));
            IntPtr recips = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(attachment, files, false);
                var message = new MapiMessage { Subject = subject, NoteText = body, FileCount = 1, Files = files };
                if (SupportEmail != "")
                {
                    var to = new MapiRecipDesc { RecipClass = MAPI_TO, Name = SupportEmail, Address = "SMTP:" + SupportEmail };
                    recips = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiRecipDesc)));
                    Marshal.StructureToPtr(to, recips, false);
                    message.RecipCount = 1;
                    message.Recips = recips;
                }
                int result = MAPISendMail(IntPtr.Zero, IntPtr.Zero, message, MAPI_LOGON_UI | MAPI_DIALOG, 0);
                return result == SUCCESS_SUCCESS || result == MAPI_USER_ABORT;
            }
            catch (Exception) { return false; } // No MAPI32.DLL, or it couldn't be loaded.
            finally
            {
                Marshal.DestroyStructure(files, typeof(MapiFileDesc));
                Marshal.FreeHGlobal(files);
                if (recips != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(recips, typeof(MapiRecipDesc));
                    Marshal.FreeHGlobal(recips);
                }
            }
        }
    }
}
