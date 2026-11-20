using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    static class Program
    {
        // Display name. The exe file, install folder, settings folder and registry key keep their original
        // "Krabbz9999 Blynclight..." names, because installed copies relaunch themselves by exe path after updating.
        public const string AppName = "KrabbzDev-BusyLightz2.0";

        // Stamped by build.ps1 into build\AssemblyInfo.cs.
        public static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version;
        public static string VersionText { get { return Version.ToString(3); } }
        public static string BuildDate { get { return Metadata("BuildDate") ?? "unknown"; } }
        // GitHub "owner/repo" whose releases this build updates from; null disables auto-update.
        public static string UpdateRepo { get { return Metadata("UpdateRepo"); } }
        // Entra app registration for setting Teams presence; null hides the feature.
        public static string GraphClientId { get { return Metadata("GraphClientId"); } }
        public static string GraphTenantId { get { return Metadata("GraphTenantId"); } }

        static void WaitForPreviousCopy(string[] args)
        {
            int at = Array.IndexOf(args, "--after-pid"), pid;
            if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out pid)) return;
            try
            {
                using (var previous = System.Diagnostics.Process.GetProcessById(pid)) previous.WaitForExit(30000);
            }
            catch (ArgumentException) { } // Already gone.
            catch (InvalidOperationException) { }
        }

        static string Metadata(string key)
        {
            foreach (AssemblyMetadataAttribute meta in Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                if (meta.Key == key && !string.IsNullOrEmpty(meta.Value)) return meta.Value;
            return null;
        }

        // Usage: no args opens the window (also used at sign-in); --tray starts in the tray only;
        // --updated (added after an auto-update) shows an "Updated to version x" notice.
        [STAThread]
        static int Main(string[] args)
        {
            // Run as SYSTEM by the all-users install's scheduled task, to install updates; no window or tray icon.
            if (Array.IndexOf(args, "--system-update") >= 0) return SystemUpdater.Run();
            // --after-pid N: started by a copy that's closing (handing over, or restarting after a freeze); wait for
            // it to finish closing first, so this one isn't turned away as a second copy.
            WaitForPreviousCopy(args);
            bool trayOnly = Array.IndexOf(args, "--tray") >= 0;
            bool justUpdated = Array.IndexOf(args, "--updated") >= 0;
            // The all-users install starts the app at every user's sign-in; each user's own settings decide whether
            // it stays, and whether the window opens.
            if (Array.IndexOf(args, "--autostart") >= 0)
            {
                if (!AppSettings.StartWithWindows) return 0;
                trayOnly = !AppSettings.OpenWindowAtSignIn;
            }
            bool firstInstance;
            using (var mutex = new Mutex(true, @"Local\Krabbz9999BlynclightTray", out firstInstance))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Krabbz9999BlynclightShow"))
            {
                // A second launch just asks the running copy to show its window.
                if (!firstInstance)
                {
                    if (!trayOnly) showSignal.Set();
                    return 0;
                }
                // A copy installed just for this user, now that BusyLightz is installed for everyone on the PC.
                if (AllUsersInstall.ShouldHandOff)
                {
                    LogEvent("Handing over to the copy installed for everyone on this PC");
                    AllUsersInstall.HandOff(!trayOnly);
                    return 0;
                }
                // Errors nothing else caught go to app.log. On the UI thread the app logs them and keeps running,
                // instead of showing .NET's error dialog; on other threads Windows still closes the app.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) => LogError("Unexpected error", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                    LogError("Unexpected error, the app closed", e.ExceptionObject as Exception);
                // Starts and stops go in app.log too, so a start with no stop before it shows the app was closed
                // without a chance to log why (Windows ended it, or it crashed outside .NET).
                LogLastSeen();
                LogEvent("Started" + (justUpdated ? " after updating" : trayOnly ? " in the tray" : ""));
                Microsoft.Win32.SystemEvents.SessionEnding += (sender, e) =>
                {
                    LogEvent(e.Reason == Microsoft.Win32.SessionEndReasons.Logoff ? "Windows is signing out" : "Windows is shutting down");
                    ClearLastSeen();
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.Run(new TrayContext(showSignal, !trayOnly, justUpdated)); }
                catch (Exception ex) { LogError("Unexpected error, the app closed", ex); throw; }
                LogEvent("Closed");
                ClearLastSeen();
            }
            return 0;
        }

        // Runs background work and logs anything it throws, instead of letting it close the app.
        public static void Guard(string what, Action work)
        {
            try { work(); }
            catch (Exception ex) { LogError(what, ex); }
        }

        // Guard on a thread-pool thread.
        public static void Background(string what, Action work)
        {
            ThreadPool.QueueUserWorkItem(delegate { Guard(what, work); });
        }

        static readonly object logGate = new object();

        // Appends the full error, with the version and where it happened, to app.log. Past 1 MB the log
        // moves to app.old.log first, so crash details can't fill the disk.
        public static void LogError(string what, Exception ex)
        {
            Log(what + " (version " + VersionText + ", " + WindowsVersion + "): " + (ex == null ? "unknown error" : ex.ToString()));
        }

        static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight"); }
        }

        // Written about once a minute while the app runs, and deleted when it closes normally. Still there at the
        // next start means the app was ended without closing (a freeze it was forced out of, or a crash).
        static string LastSeenPath { get { return Path.Combine(DataDir, "last-seen.txt"); } }

        static void LogLastSeen()
        {
            try
            {
                long ticks;
                if (File.Exists(LastSeenPath) && long.TryParse(File.ReadAllText(LastSeenPath).Trim(), out ticks))
                    LogEvent("The last run ended without closing; it was last seen running at " + new DateTime(ticks).ToString("s"));
            }
            catch (Exception) { }
        }

        public static void ClearLastSeen()
        {
            try { File.Delete(LastSeenPath); } catch (Exception) { }
        }

        static long uiAnswered = DateTime.Now.Ticks;

        // Checks every few seconds that the window thread still answers, and logs when it stops (and starts again),
        // so a freeze shows in app.log even if the app is then forced closed. Also keeps last-seen.txt current.
        public static void WatchForFreezes(Control ui)
        {
            const int CheckMs = 5000, FrozenMs = 30000, RestartMs = 120000, LastSeenMs = 60000;
            var watcher = new Thread(delegate ()
            {
                bool frozen = false;
                DateTime lastSeenSaved = DateTime.MinValue, lastCheck = DateTime.Now;
                while (true)
                {
                    Thread.Sleep(CheckMs);
                    // A much longer wait than asked for means the PC was asleep, which isn't a freeze: start counting again.
                    if ((DateTime.Now - lastCheck).TotalMilliseconds > CheckMs * 3) Interlocked.Exchange(ref uiAnswered, DateTime.Now.Ticks);
                    lastCheck = DateTime.Now;
                    try { ui.BeginInvoke((Action)delegate { Interlocked.Exchange(ref uiAnswered, DateTime.Now.Ticks); }); }
                    catch (Exception) { return; } // The window is gone: the app is closing.
                    double quietMs = (DateTime.Now - new DateTime(Interlocked.Read(ref uiAnswered))).TotalMilliseconds;
                    if (!frozen && quietMs > FrozenMs)
                    {
                        frozen = true;
                        LogEvent("Stopped responding: the window hasn't answered for " + (int)(quietMs / 1000) + " seconds");
                    }
                    // Frozen for good: start a fresh copy rather than leave the lights, Teams status and buttons
                    // stuck (and every launch quietly handing over to the frozen copy).
                    if (frozen && quietMs > RestartMs) RestartFrozen((int)(quietMs / 1000));
                    else if (frozen && quietMs < CheckMs * 2)
                    {
                        frozen = false;
                        LogEvent("Responding again");
                    }
                    if ((DateTime.Now - lastSeenSaved).TotalMilliseconds >= LastSeenMs)
                    {
                        try { File.WriteAllText(LastSeenPath, DateTime.Now.Ticks.ToString()); } catch (Exception) { }
                        lastSeenSaved = DateTime.Now;
                    }
                }
            });
            watcher.IsBackground = true;
            watcher.Start();
        }

        // Ends this frozen copy and starts a fresh one, in the tray, which waits for this one to be gone. While an
        // update installs, just ends it: the updater opens the new version.
        static void RestartFrozen(int seconds)
        {
            bool updating = AllUsersInstall.UpdateInProgress;
            LogEvent("Still frozen after " + seconds + " seconds; " + (updating ? "closing it so the update can finish" : "restarting"));
            if (!updating)
            {
                try
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath,
                        "--autostart --after-pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
                }
                catch (Exception ex) { LogError("Couldn't start a fresh copy", ex); }
            }
            ClearLastSeen();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        static int forceCloseStarted;

        // A safety net for closing: if the app hasn't finished closing after ms (something it was shutting down got
        // stuck), it's ended outright, so an update can always replace it and it can always be started again.
        public static void ForceCloseAfter(int ms, string why)
        {
            if (Interlocked.Exchange(ref forceCloseStarted, 1) == 1) return;
            var timer = new Thread(delegate ()
            {
                Thread.Sleep(ms);
                LogEvent("Still closing (" + why + ") after " + ms / 1000 + " seconds; ending it");
                ClearLastSeen();
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            });
            timer.IsBackground = true;
            timer.Start();
        }

        // A one-line note in app.log, e.g. "Started" or "Closed".
        public static void LogEvent(string what)
        {
            Log(what + " (version " + VersionText + ")");
        }

        static void Log(string line)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight");
                string log = Path.Combine(dir, "app.log");
                lock (logGate)
                {
                    Directory.CreateDirectory(dir);
                    if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024)
                    {
                        string old = Path.Combine(dir, "app.old.log");
                        File.Delete(old);
                        File.Move(log, old);
                    }
                    File.AppendAllText(log, DateTime.Now.ToString("s") + "  " + line + Environment.NewLine);
                }
            }
            catch (Exception) { } // Logging must never cause a crash of its own.
        }

        // E.g. "Windows 25H2, build 26200.9711". Environment.OSVersion says 6.2 on every Windows since 8 for apps without
        // a compatibility manifest, so read the registry.
        public static string WindowsVersion
        {
            get
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        return "Windows " + key.GetValue("DisplayVersion") + ", build " + key.GetValue("CurrentBuild") + "." + key.GetValue("UBR");
                }
                catch (Exception) { return "Windows " + Environment.OSVersion.Version; }
            }
        }
    }

    // Hidden window that receives global hotkeys and lets background threads post work to the UI thread.
    class MessageWindow : Control
    {
        const int WM_HOTKEY = 0x0312;
        public event Action<int> HotkeyPressed;

        public MessageWindow() { CreateHandle(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    class TrayContext : ApplicationContext
    {
        const uint MOD_ALT = 0x1, MOD_NOREPEAT = 0x4000;
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Global hotkeys. The hotkey id is the index into HotkeyStates/HotkeyKeys; HotkeyText is shown in the menu and on buttons.
        public static readonly Dictionary<string, string> HotkeyText = new Dictionary<string, string>
        {
            { LightState.Red, "Alt+R" },
            { LightState.Green, "Alt+G" },
        };
        static readonly string[] HotkeyStates = { LightState.Red, LightState.Green };
        static readonly Keys[] HotkeyKeys = { Keys.R, Keys.G };

        // Check for updates a minute after starting, then this often.
        const int UpdateFirstCheckMs = 60 * 1000;
        const int UpdateCheckMs = 6 * 60 * 60 * 1000;
        // How often to read Teams presence while following Teams status.
        const int TeamsPollMs = 20 * 1000;

        readonly LightController light = new LightController(!AppSettings.UsbLightsDisabled);
        readonly CallDetector calls;
        // A mutesync mute button, if one is plugged in: mutes the mic and shows red/green on its ring.
        readonly MuteSyncButton muteButton = new MuteSyncButton(AppSettings.MuteSyncEnabled);
        // App that has the microphone open, or null when not on a call.
        MicApp callApp;
        // Reads Teams status and sets Teams to Busy during non-Teams calls; null when no Teams app is configured.
        TeamsPresence presence;
        ToolStripMenuItem presenceItem;
        const string ReauthText = "Re-authenticate Microsoft 365";
        readonly System.Threading.Timer teamsPoll;
        MeetingAlerts meetingAlerts;
        AgendaForm agendaForm;
        DateTime agendaClosedUtc;
        // After a double-click opens the window, the click that ends it mustn't open the menu too.
        DateTime menuSuppressedUntil;
        // Last Teams availability read while signed in (e.g. "Available"), or null.
        string teamsAvailability;
        bool embravaWarned;
        // What the last notification described, so only real changes notify.
        string lastNotifiedKey;
        readonly System.Threading.Timer updateTimer;
        int updateCheckRunning;
        readonly NotifyIcon tray;
        readonly MessageWindow messages = new MessageWindow();
        readonly System.Windows.Forms.Timer flashTimer = new System.Windows.Forms.Timer();
        // Counts down a color override and switches back to Follow Teams Status when it runs out.
        readonly System.Windows.Forms.Timer overrideTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly RegisteredWaitHandle showWait;
        readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>();
        readonly Dictionary<string, ToolStripMenuItem> stateItems = new Dictionary<string, ToolStripMenuItem>();
        ControlForm form;
        SetupForm setupForm;
        SettingsForm settingsForm;
        // Where the tray icon and swatch are on the color wheel in Party Mode.
        int partyHue;

        // What the light is showing right now (red during a softphone call).
        public string State { get { return light.ShownState; } }
        public Color DisplayColor { get; private set; }
        public event Action DisplayChanged;

        public TrayContext(EventWaitHandle showSignal, bool showWindow, bool justUpdated)
        {
            var menu = new ContextMenuStrip();
            AddItem(menu, "Open " + Program.AppName, OpenGlyph, delegate { ShowWindow(); }).Font = new Font("Segoe UI Semibold", 9.5f);
            AddItem(menu, "Today's meetings", "\uE787", delegate { ShowAgenda(); });
            menu.Items.Add(new ToolStripSeparator());
            AddStateItem(menu, "Red/Busy", LightState.Red);
            AddStateItem(menu, "Green/Available", LightState.Green);
            AddStateItem(menu, "On A Call", LightState.CallFlash);
            menu.Items.Add(new ToolStripSeparator());
            presence = CreatePresence();
            // Signs in to Microsoft 365 again, in the browser. Without a Teams app for this organization, it opens the
            // setup checklist to set one up. (Connecting and disconnecting are in Settings > Microsoft Teams.)
            presenceItem = new ToolStripMenuItem(ReauthText, null, delegate { ReauthTeams(); });
            menu.Items.Add(presenceItem);
            menuRenderer.Glyph(presenceItem, TeamsGlyph);
            menu.Items.Add(new ToolStripSeparator());
            AddItem(menu, "Settings...", SettingsGlyph, delegate { ShowSettings(); });
            if (Program.UpdateRepo != null)
            {
                AddItem(menu, "Check and install updates", UpdateGlyph, delegate { CheckForUpdates(true); });
                AddItem(menu, "Help (user guide)", HelpGlyph, delegate { OpenHelp(); });
            }
            if (Program.UpdateRepo != null) AddItem(menu, "Send feedback...", FeedbackGlyph, delegate { ShowFeedback(); });
            // A coffee-yellow dot: the coffee emoji came out as a half-drawn circle in the menu's font.
            var coffee = AddItem(menu, "Buy me a coffee", null, delegate { OpenCoffee(); });
            menuRenderer.Dot(coffee, () => new[] { Color.FromArgb(255, 221, 0) });
            AddItem(menu, "Exit tray app", ExitGlyph, delegate { Application.Exit(); });
            menu.Items.Add(new ToolStripSeparator());
            var version = menu.Items.Add("Version " + Program.VersionText);
            version.Enabled = false;
            version.Font = new Font("Segoe UI", 8.5f);
            ModernMenuRenderer.Style(menu, menuRenderer);

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            // A normal (left) click opens the menu, the same as a right-click; a double-click opens the window.
            tray.MouseClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Left && DateTime.UtcNow >= menuSuppressedUntil) ShowTrayMenu();
            };
            tray.DoubleClick += delegate
            {
                menuSuppressedUntil = DateTime.UtcNow.AddMilliseconds(SystemInformation.DoubleClickTime);
                tray.ContextMenuStrip.Close(); // The first click of the double-click opened it.
                ShowWindow();
            };
            tray.Visible = true;

            // Cycle the tray icon round the color wheel at the light's speed, in half as many steps.
            // In Party Mode the tray icon and window cycle smoothly, once every 3 seconds, rather than with the lights:
            // the lights jump to a new color 10 times a second, and a large area doing that flashes fast enough to
            // trigger seizures.
            flashTimer.Interval = 100;
            flashTimer.Tick += delegate { partyHue = (partyHue + 12) % 360; UpdateDisplay(); };
            callFlashTimer.Tick += delegate { callFlashSecond = !callFlashSecond; UpdateDisplay(); };

            if (AppSettings.HotkeysEnabled) RegisterHotkeys();
            messages.HotkeyPressed += id => SetState(HotkeyStates[id]);
            // Installs and updates rewrite the sign-in startup entry; put back the user's choice.
            try { AppSettings.ApplyStartup(); } catch (Exception) { }
            // An override saved by a version without the timer gets a fresh hour from now.
            StartOverrideTimerIfNeeded();
            overrideTimer.Tick += delegate { CheckOverride(); };
            overrideTimer.Start();
            Refresh();

            light.LightsChanged += delegate
            {
                try { messages.BeginInvoke((Action)Refresh); }
                catch (InvalidOperationException) { } // Window already gone during exit.
            };
            K2Connected = K2Key.Present();
            // The K2 key's buttons run the macros picked in Settings.
            K2Key.Disabled = AppSettings.K2Disabled;
            UpdateK2Listener();
            deviceCheck.Tick += delegate { Program.Guard("Device check", CheckDevices); };
            deviceCheck.Start();
            // The window's mutesync tile follows the button's ring.
            muteButton.StatusChanged += delegate
            {
                try { messages.BeginInvoke((Action)delegate { if (!exiting) UpdateDisplay(); }); }
                catch (InvalidOperationException) { } // Window already gone during exit.
            };
            // Red while any (non-ignored) app has the microphone open, then back to the user's color
            // (or back to Teams status when following Teams). Both parts can be turned off in Settings.
            calls = new CallDetector(app =>
            {
                try { messages.BeginInvoke((Action)delegate { OnMicApp(app); }); }
                catch (InvalidOperationException) { } // Window already gone during exit.
            });
            teamsPoll = new System.Threading.Timer(delegate { Program.Guard("Teams status check", PollTeams); }, null, 0, TeamsPollMs);
            meetingAlerts = new MeetingAlerts(() => presence, messages, () => callApp != null);
            Program.WatchForFreezes(messages);
            WatchInstall();
            if (Program.UpdateRepo != null)
                updateTimer = new System.Threading.Timer(delegate { Program.Guard("Update check", () => CheckForUpdates(false)); },
                    null, UpdateFirstCheckMs, UpdateCheckMs);
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
                delegate { Program.Guard("Opening the window", () => messages.BeginInvoke((Action)ShowWindow)); }, null, -1, false);
            if (showWindow) ShowWindow();
            // Walk new users (and users updating from before the checklist existed) through setup once.
            if (!File.Exists(SetupShownPath))
            {
                ShowSetup();
                try { File.WriteAllText(SetupShownPath, Program.VersionText); } catch (IOException) { }
            }
            string lastVersion = AppSettings.LastVersion;
            try { AppSettings.LastVersion = Program.VersionText; } catch (Exception) { }
            if (justUpdated || (lastVersion != null && lastVersion != Program.VersionText)) ShowWhatsNew();
            tray.BalloonTipClicked += delegate
            {
                var action = balloonAction;
                balloonAction = null;
                if (action != null) action();
            };
            tray.BalloonTipClosed += delegate { balloonAction = null; };
            // Not a brand new install (the setup checklist covers those): tell them about Meeting Alerts, once.
            ScheduleMeetingAlertsIntro(lastVersion != null);
        }

        // What clicking the notification just shown does, if anything.
        Action balloonAction;

        // A while after start-up, a notice about Meeting Alerts that opens its page in Settings: "New: Meeting Alerts" once
        // after an update, then a reminder at most once a week for as long as calendar access still isn't allowed. Not
        // shown when calendar access is already allowed, alerts are off, or no Teams app is set up.
        void ScheduleMeetingAlertsIntro(bool existingInstall)
        {
            if (!existingInstall && !AppSettings.MeetingAlertsIntroShown)
            {
                // A brand new install is walked through setup instead; its first reminder comes a week later.
                AppSettings.MeetingAlertsIntroShown = true;
                AppSettings.MarkMeetingAlertsReminded();
                return;
            }
            var wait = new System.Windows.Forms.Timer { Interval = 25000 };
            wait.Tick += delegate
            {
                wait.Dispose();
                var teams = presence;
                if (teams == null || !AppSettings.MeetingAlertsOn) return; // Try again at the next start.
                if (teams.CalendarGranted) { AppSettings.MeetingAlertsIntroShown = true; return; }
                bool intro = !AppSettings.MeetingAlertsIntroShown;
                if (!intro && !AppSettings.MeetingAlertsReminderDue) return;
                AppSettings.MeetingAlertsIntroShown = true;
                AppSettings.MarkMeetingAlertsReminded();
                balloonLink = null;
                balloonAction = ShowMeetingAlertsSettings;
                tray.ShowBalloonTip(15000, intro ? "New: Meeting Alerts" : "Meeting Alerts needs your calendar",
                    teams.SignedIn
                        ? "Get a pop-up before each meeting, with a Join button. Click here to connect your calendar."
                        : "Get a pop-up before each meeting, with a Join button. Click here to see how to set it up.",
                    ToolTipIcon.Info);
            };
            wait.Start();
        }

        // Opens Settings on the Meeting Alerts page.
        public void ShowMeetingAlertsSettings()
        {
            ShowSettings();
            settingsForm.ShowMeetingAlerts();
        }

        // Clicking the "what changed" card opens this release page; other cards open nothing.
        string balloonLink;

        // Shows an "Updated to version x" card listing what changed, from the release notes on GitHub.
        void ShowWhatsNew()
        {
            tray.BalloonTipClicked += delegate { if (balloonLink != null) System.Diagnostics.Process.Start(balloonLink); };
            tray.BalloonTipClosed += delegate { balloonLink = null; };
            Program.Background("Showing what changed", delegate
            {
                string repo = Program.UpdateRepo;
                string notes = repo == null ? null : Updater.ReleaseNotes(repo, Program.VersionText);
                // Windows cuts notification text off at about 200 characters.
                const int MaxLength = 200;
                string more = repo == null ? "" : "\nClick to see the full release notes.";
                string text = notes ?? "Click to see what changed.";
                if (notes != null && text.Length + more.Length > MaxLength)
                    text = text.Substring(0, MaxLength - more.Length - 3).TrimEnd() + "...";
                if (notes != null) text += more;
                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        balloonLink = repo == null ? null : Updater.ReleasePage(repo, Program.VersionText);
                        tray.ShowBalloonTip(15000, "Updated to version " + Program.VersionText, repo == null ? "You're on the newest version." : text, ToolTipIcon.Info);
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
            });
        }

        static string SetupShownPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Krabbz9999 Blynclight", "setup-shown.txt");
            }
        }

        // For the setup checklist.
        public LightController Light { get { return light; } }
        public TeamsPresence Presence { get { return presence; } }
        public MuteSyncButton MuteButton { get { return muteButton; } }

        // Whether a K2 macro key is plugged in, for the window's Active Devices; checked every few seconds.
        public bool K2Connected { get; private set; }
        // Listens to the K2 (and cancels its own clicks with a mouse hook) only while one is plugged in and Use the K2 key
        // is on in Settings; null otherwise, so a PC without a K2 never has the hook or the input watching.
        K2Key k2Key;
        // Raised on the UI thread for each K2 press, so Settings can light that key in its picture.
        public event Action<K2Key.Button> K2Pressed;
        readonly System.Windows.Forms.Timer deviceCheck = new System.Windows.Forms.Timer { Interval = 3000 };

        void CheckDevices()
        {
            bool k2 = K2Key.Present();
            if (k2 == K2Connected) return;
            K2Connected = k2;
            Program.LogEvent(k2 ? "K2 key plugged in" : "K2 key unplugged");
            UpdateK2Listener();
            UpdateDisplay();
        }

        // Keys added in Find my key (or forgotten): looks for a K2 again, and restarts listening so the change takes effect.
        public void K2IdsChanged()
        {
            if (k2Key != null) { k2Key.Dispose(); k2Key = null; }
            K2Connected = K2Key.Present();
            UpdateK2Listener();
            UpdateDisplay();
        }

        // Starts or stops listening to the K2 to match whether one is plugged in and its functions are on.
        void UpdateK2Listener()
        {
            bool want = K2Connected && !AppSettings.K2Disabled;
            if (want && k2Key == null)
            {
                k2Key = new K2Key();
                k2Key.Pressed += button =>
                {
                    if (K2Pressed != null) K2Pressed(button);
                    K2Actions.Run(button, this);
                };
            }
            else if (!want && k2Key != null)
            {
                k2Key.Dispose();
                k2Key = null;
            }
        }

        // Meeting alerts: whether BusyLightz may read the calendar, why it couldn't, and the buttons in Settings.
        public string MeetingAlertProblem { get { return meetingAlerts == null ? null : meetingAlerts.Problem; } }

        // Asks Microsoft, in the browser, to let BusyLightz read the user's own calendar (read-only) for meeting alerts.
        public void GrantCalendarAccess()
        {
            var teams = presence;
            if (teams == null) { ShowSetup(); return; }
            if (!teams.SignedIn)
            {
                ShowBalloon("Connect to Microsoft Teams first (Settings > Microsoft Teams), then allow calendar access.", ToolTipIcon.Info);
                return;
            }
            Program.Background("Calendar access", delegate
            {
                try
                {
                    teams.GrantCalendarAccess();
                    ShowBalloon("Calendar access allowed. You'll get an alert before your meetings.", ToolTipIcon.Info);
                    meetingAlerts.CheckNow();
                }
                catch (Exception ex) { ShowBalloon("Couldn't allow calendar access: " + ex.Message, ToolTipIcon.Warning); }
            });
        }

        // Opens the tray menu where the pointer is, as a right-click does. NotifyIcon only does that itself for the right
        // button, so a left click asks it to the same way its own right-click handling does.
        void ShowTrayMenu()
        {
            try
            {
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tray, null);
            }
            catch (Exception)
            {
                tray.ContextMenuStrip.Show(Cursor.Position);
            }
        }

        // Today's meetings, in a small window by the tray icon: from the tray menu.
        public void ShowAgenda()
        {
            if (agendaForm != null && !agendaForm.IsDisposed)
            {
                agendaForm.Close();
                return;
            }
            // Clicking the tray icon takes focus from the window, which closes it; don't open it again for that click.
            if ((DateTime.UtcNow - agendaClosedUtc).TotalMilliseconds < 700) return;
            agendaForm = new AgendaForm(() => presence, ShowMeetingAlertsSettings, ShowWindow);
            agendaForm.FormClosed += delegate { agendaClosedUtc = DateTime.UtcNow; };
            agendaForm.Show();
        }

        public void ShowTestMeetingAlert()
        {
            meetingAlerts.ShowSample();
        }

        public void ConnectTeams()
        {
            if (presence != null && !presence.SignedIn) TogglePresence();
        }

        public void DisconnectTeams()
        {
            if (presence != null && presence.SignedIn) TogglePresence();
        }

        TeamsPresence CreatePresence()
        {
            if (!TeamsAppConfig.Configured) return null;
            return new TeamsPresence(TeamsAppConfig.ClientId, TeamsAppConfig.TenantId, message => ShowBalloon(message, ToolTipIcon.Warning));
        }

        // Switches to the Teams app now saved in TeamsAppConfig. Any sign-in with the previous app is dropped.
        public void ApplyTeamsApp()
        {
            var old = presence;
            presence = null;
            teamsAvailability = null;
            light.SetTeamsColor(null);
            if (old != null)
            {
                Program.Background("Teams sign-out", delegate
                {
                    old.SignOut();
                    old.Dispose();
                });
            }
            presence = CreatePresence();
            Refresh();
        }

        public void ShowSetup()
        {
            if (setupForm == null || setupForm.IsDisposed) setupForm = new SetupForm(this);
            setupForm.Show();
            if (setupForm.WindowState == FormWindowState.Minimized) setupForm.WindowState = FormWindowState.Normal;
            setupForm.Activate();
        }

        // What the lights show during a call: flashing red/blue, or steady red.
        static string CallState { get { return AppSettings.FlashOnCalls ? LightState.CallFlash : LightState.Red; } }

        // Shows the call color for a few seconds so the user can see the light respond, unless a real call is in progress.
        public void TestLight()
        {
            if (light.OverrideActive) return;
            light.SetOverride(CallState);
            Refresh();
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate
            {
                timer.Dispose();
                if (callApp == null) light.SetOverride(null);
                Refresh();
            };
            timer.Start();
        }

        void OnMicApp(MicApp app)
        {
            bool wasOverriding = light.OverrideActive;
            muteButton.OnCall = app != null;
            // Teams already shows its own calls as Busy; other apps need presence set.
            if (presence != null && !AppSettings.TeamsDisabled)
            {
                if (app != null && !app.IsTeams && AppSettings.TeamsBusyOnCalls) presence.CallStarted();
                else presence.CallEnded();
            }
            var shown = AppSettings.CallsTurnRed ? app : null;
            // In app.log, so what the lights did during a call can be traced afterwards.
            if (app != null) Program.LogEvent("Call started (" + app.Name + ")" + (shown == null ? ", lights not changed (Turn lights red is off)" : ""));
            else if (callApp != null) Program.LogEvent("Call ended (" + callApp.Name + ")");
            callApp = shown;
            light.SetOverride(shown != null ? CallState : null);
            if (shown == null && wasOverriding && light.State == LightState.FollowTeams) HandBackToTeams();
            Refresh();
        }

        // Re-applies call and hotkey settings after they change in the Settings window.
        public void ApplySettings()
        {
            light.ApplySettings();
            calls.Recheck();
            muteButton.Enabled = AppSettings.MuteSyncEnabled;
            K2Key.Disabled = AppSettings.K2Disabled;
            UpdateK2Listener();
            muteButton.ApplySettings();
            if (AppSettings.OverrideTimesOut) StartOverrideTimerIfNeeded();
            else light.SetReturnToTeams(null);
            Refresh();
            for (int id = 0; id < HotkeyStates.Length; id++) UnregisterHotKey(messages.Handle, id);
            if (AppSettings.HotkeysEnabled) RegisterHotkeys();
        }

        // Reopens the main and Settings windows in the current Light/Dark colors, where they were. Runs after the
        // caller's event finishes, since that's usually a control in the Settings window being replaced.
        public void ApplyTheme()
        {
            messages.BeginInvoke((Action)delegate
            {
                if (form != null && !form.IsDisposed)
                {
                    bool shown = form.Visible;
                    var at = form.Location;
                    form.Dispose();
                    form = new ControlForm(this) { StartPosition = FormStartPosition.Manual, Location = at };
                    AttachDock(form);
                    if (shown) form.Show();
                    PlaceDock(form);
                }
                if (settingsForm != null && !settingsForm.IsDisposed)
                {
                    var at = settingsForm.Location;
                    settingsForm.Close(); // Also stops its refresh timer.
                    settingsForm = new SettingsForm(this) { StartPosition = FormStartPosition.Manual, Location = at };
                    settingsForm.Show();
                    settingsForm.Activate();
                }
            });
        }

        public void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this);
            settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
        }

        // Tray menu icons: Segoe MDL2 Assets characters.
        const string OpenGlyph = "", TeamsGlyph = "", UsbGlyph = "", SettingsGlyph = "",
            ChecklistGlyph = "", UpdateGlyph = "", HelpGlyph = "", MailGlyph = "", ExitGlyph = "";

        readonly ModernMenuRenderer menuRenderer = new ModernMenuRenderer();

        ToolStripItem AddItem(ContextMenuStrip menu, string text, string glyph, EventHandler click)
        {
            var item = menu.Items.Add(text, null, click);
            if (glyph != null) menuRenderer.Glyph(item, glyph);
            return item;
        }

        void AddStateItem(ContextMenuStrip menu, string text, string state)
        {
            var item = new ToolStripMenuItem(text, null, delegate { SetState(state); }) { Tag = text };
            string keys;
            if (HotkeyText.TryGetValue(state, out keys)) item.ShortcutKeyDisplayString = keys;
            stateItems[state] = item;
            menu.Items.Add(item);
            menuRenderer.Dot(item, () => DotColors(state));
        }

        // The dot beside a mode in the tray menu, in the colors that mode shows (empty for Disable Lights).
        static Color[] DotColors(string state)
        {
            switch (state)
            {
                case LightState.Red: return new[] { Theme.Soften(PickedColor("BusyColor", Color.Red)) };
                case LightState.Green: return new[] { Theme.Soften(PickedColor("AvailableColor", Color.Lime)) };
                case LightState.CallFlash:
                    return new[] { Theme.Soften(PickedColor("CallColor1", Color.Red)), Theme.Soften(PickedColor("CallColor2", Color.Blue)) };
                case LightState.Party: return new[] { Theme.Red, Color.FromArgb(234, 179, 8), Theme.Green, Theme.CallBlue };
                case LightState.FollowTeams: return new[] { Theme.Teams };
                default: return new Color[0];
            }
        }

        // e.g. "[:15 Min Override]" or "[2 Hr Override]", matching the override time in Settings; "[Override]"
        // when overrides stay on until changed.
        static string OverrideTag
        {
            get
            {
                if (!AppSettings.OverrideTimesOut) return "[Override]";
                int minutes = AppSettings.OverrideMinutes;
                if (minutes < 60) return "[:" + minutes.ToString("00") + " Min Override]";
                return "[" + (minutes / 60) + " Hr Override]";
            }
        }

        // Puts the current override time in front of each override item's name.
        void UpdateStateItemNames()
        {
            string tag = OverrideTag;
            foreach (var pair in stateItems)
            {
                string name = (string)pair.Value.Tag;
                pair.Value.Text = LightController.OverridesTeams(pair.Key) ? tag + " " + name : name;
            }
        }

        void RegisterHotkeys()
        {
            var failed = new List<string>();
            for (int id = 0; id < HotkeyStates.Length; id++)
            {
                if (!RegisterHotKey(messages.Handle, id, MOD_ALT | MOD_NOREPEAT, (uint)HotkeyKeys[id]))
                    failed.Add(HotkeyText[HotkeyStates[id]]);
            }
            if (failed.Count > 0)
                tray.ShowBalloonTip(5000, Program.AppName,
                    string.Join(", ", failed.ToArray()) + " is already used by another program, so that hotkey won't work.", ToolTipIcon.Warning);
        }

        public void ShowWindow()
        {
            if (form == null || form.IsDisposed)
            {
                form = new ControlForm(this);
                AttachDock(form);
            }
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            PlaceDock(form);
        }

        // The "My Schedule Today" panel attached to the right of the BusyLightz window while it's showing.
        AgendaForm dockedAgenda;

        // Keeps the panel with the window as it moves, resizes, hides or minimizes.
        void AttachDock(ControlForm window)
        {
            EventHandler follow = delegate { PlaceDock(window); };
            window.LocationChanged += follow;
            window.SizeChanged += follow;
            window.VisibleChanged += follow;
        }

        void PlaceDock(ControlForm window)
        {
            bool want = AppSettings.AgendaBesideWindow && window != null && !window.IsDisposed && window.Visible
                && window.WindowState == FormWindowState.Normal;
            bool have = dockedAgenda != null && !dockedAgenda.IsDisposed;
            if (!want)
            {
                if (have && dockedAgenda.Visible) dockedAgenda.Hide();
                return;
            }
            if (!have) dockedAgenda = new AgendaForm(() => presence, ShowMeetingAlertsSettings, ShowWindow, true);
            dockedAgenda.DockTo(window.Bounds);
            if (!dockedAgenda.Visible) dockedAgenda.Show(window);
        }

        // From Settings, when "Show today's meetings beside the window" is switched.
        public void UpdateDock()
        {
            PlaceDock(form);
        }

        // Downloads and installs a newer release in the background. Only a manual check reports "up to date" or errors.
        // The user guide lives in the repo's docs folder.
        public void OpenHelp()
        {
            if (Program.UpdateRepo != null) System.Diagnostics.Process.Start("https://github.com/" + Program.UpdateRepo + "/blob/main/docs/README.md");
        }

        // KrabbzDev's tip jar.
        const string CoffeeUrl = "https://buymeacoffee.com/krabbzdev";

        public void OpenCoffee()
        {
            System.Diagnostics.Process.Start(CoffeeUrl);
        }

        // Segoe MDL2 Assets feedback (speech bubble with a pen) icon.
        const string FeedbackGlyph = "";
        FeedbackForm feedbackForm;

        // An idea, problem or question, as a new issue on the project's GitHub page.
        public void ShowFeedback()
        {
            if (feedbackForm == null || feedbackForm.IsDisposed) feedbackForm = new FeedbackForm(this);
            feedbackForm.Show();
            if (feedbackForm.WindowState == FormWindowState.Minimized) feedbackForm.WindowState = FormWindowState.Normal;
            feedbackForm.Activate();
        }

        public void OpenProjectPage()
        {
            if (Program.UpdateRepo != null) System.Diagnostics.Process.Start("https://github.com/" + Program.UpdateRepo);
        }

        readonly System.Windows.Forms.Timer installWatch = new System.Windows.Forms.Timer { Interval = 5000 };

        // For the move to an all-users install (see AllUsersInstall): a just-for-me copy hands over as soon as the
        // all-users copy appears (e.g. pushed by IT), and the all-users copy removes the old one, and closes and
        // reopens around updates its background updater installs.
        void WatchInstall()
        {
            if (AllUsersInstall.IsThisCopy) Program.Background("Removing the old copy", AllUsersInstall.RemoveJustForMeCopies);
            installWatch.Tick += delegate
            {
                bool showWindow = form != null && form.Visible;
                if (AllUsersInstall.ShouldHandOff)
                {
                    installWatch.Stop();
                    Program.LogEvent("Handing over to the copy installed for everyone on this PC");
                    Program.ClearLastSeen();
                    AllUsersInstall.HandOff(showWindow);
                    Program.ForceCloseAfter(10000, "handing over");
                    Application.Exit();
                }
                else if (AllUsersInstall.IsThisCopy && AllUsersInstall.UpdateInProgress)
                {
                    installWatch.Stop();
                    Program.LogEvent("Closing while an update installs");
                    Program.ClearLastSeen();
                    AllUsersInstall.ReopenAfterUpdate(showWindow);
                    // An update can't replace the app while it's still open, so it mustn't get stuck closing.
                    Program.ForceCloseAfter(10000, "for an update");
                    Application.Exit();
                }
            };
            installWatch.Start();
        }

        public void CheckForUpdates(bool manual)
        {
            // The all-users copy is updated by its background updater, which has the admin rights that needs. A manual
            // check starts that updater straight away (Windows asks for permission) when a newer version is out.
            if (AllUsersInstall.IsThisCopy)
            {
                if (manual) ForceUpdate();
                else if (Program.UpdateRepo != null) OfferAllUsersUpdate();
                return;
            }
            if (!manual && !AppSettings.AutoUpdate) return;
            if (Interlocked.Exchange(ref updateCheckRunning, 1) == 1) return;
            Program.Background("Update check", delegate
            {
                UpdateInfo update = null;
                string message;
                try
                {
                    update = Updater.DownloadIfNewer(Program.UpdateRepo, Program.Version);
                    message = "You're on the latest version (" + Program.VersionText + ").";
                }
                catch (Exception ex) { message = "Couldn't check for updates: " + ex.Message; }
                finally { Interlocked.Exchange(ref updateCheckRunning, 0); }

                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        if (update != null)
                        {
                            // Reopen the way the user had it: window showing, or tray only.
                            Program.LogEvent("Closing to install version " + update.Version);
                            Program.ClearLastSeen();
                            Updater.Install(update, Application.ExecutablePath, form != null && form.Visible);
                            Program.ForceCloseAfter(10000, "for an update");
                            Application.Exit();
                        }
                        else if (manual)
                        {
                            tray.ShowBalloonTip(5000, Program.AppName, message, ToolTipIcon.Info);
                        }
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
            });
        }

        // "Update now" in Settings, for the all-users copy: if a newer version is out, starts its background updater
        // straight away instead of waiting for the next run (no prompt, unless Windows refuses, as on an install from before 2.36.2).
        public void ForceUpdate()
        {
            if (!AllUsersInstall.IsThisCopy) { CheckForUpdates(true); return; } // A just-for-me copy installs at once.
            Program.Background("Update now", delegate
            {
                try
                {
                    var latest = Updater.LatestVersion(Program.UpdateRepo);
                    if (latest == null || latest <= new Version(Program.Version.Major, Program.Version.Minor, Program.Version.Build))
                    {
                        ShowBalloon("You're on the latest version (" + Program.VersionText + ").", ToolTipIcon.Info);
                        return;
                    }
                    ShowBalloon("Version " + latest.ToString(3) + " is out. Starting the update now: BusyLightz closes and reopens by itself.", ToolTipIcon.Info);
                    if (AllUsersInstall.RunUpdaterNow()) Program.LogEvent("Update now: started the background updater for version " + latest.ToString(3));
                    else ShowBalloon("The update wasn't started, because the permission prompt was closed.", ToolTipIcon.Warning);
                }
                catch (Exception ex) { ShowBalloon("Couldn't start the update: " + ex.Message, ToolTipIcon.Warning); }
            });
        }

        // The version of the all-users copy that the user was last told about, so each one is offered once per run.
        string offeredUpdate;

        // For the all-users copy, which can't update itself: when a newer version is out, says so with a click to
        // install it now, so nobody waits for the background updater's next run.
        void OfferAllUsersUpdate()
        {
            Program.Background("Update check", delegate
            {
                Version latest;
                try { latest = Updater.LatestVersion(Program.UpdateRepo); }
                catch (Exception) { return; } // Offline or rate limited; the next check tries again.
                if (latest == null || latest <= new Version(Program.Version.Major, Program.Version.Minor, Program.Version.Build)) return;
                string version = latest.ToString(3);
                if (offeredUpdate == version) return;
                offeredUpdate = version;
                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        balloonLink = null;
                        balloonAction = ForceUpdate;
                        tray.ShowBalloonTip(15000, "Update available",
                            "Version " + version + " is out. Click here to install it now. Windows asks for permission first.", ToolTipIcon.Info);
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
            });
        }

        public void SetState(string state)
        {
            light.Set(state);
            // Picking a color during a call wins until the call ends, but going back to Teams status (by hand, or when
            // an override runs out) shouldn't hide a call still in progress: call detection only reports calls as
            // they start, so put the call colors back here.
            if (state == LightState.FollowTeams && callApp != null) light.SetOverride(CallState);
            StartOverrideTimerIfNeeded();
            // Before reading Teams status back, so a Busy this app set isn't mistaken for the user's own.
            UpdateTeamsMessage();
            if (state == LightState.FollowTeams) HandBackToTeams();
            Refresh();
        }

        // Called when the override length changes: a running timer starts over with the new length.
        public void RestartOverrideTimer()
        {
            if (light.ReturnToTeamsAt == null) return;
            light.SetReturnToTeams(null);
            StartOverrideTimerIfNeeded();
            Refresh();
        }

        void StartOverrideTimerIfNeeded()
        {
            if (AppSettings.OverrideTimesOut && LightController.OverridesTeams(light.State) && light.ReturnToTeamsAt == null)
                light.SetReturnToTeams(DateTime.UtcNow.AddMinutes(AppSettings.OverrideMinutes));
        }

        // True while the user's own Red, Green or On A Call choice is overriding Teams status.
        public bool OverrideOn { get { return LightController.OverridesTeams(light.State); } }

        // e.g. "14:52", or null when no override timer is running.
        public string OverrideTimeLeft
        {
            get
            {
                var at = light.ReturnToTeamsAt;
                if (at == null || !AppSettings.OverrideTimesOut) return null;
                var left = at.Value - DateTime.UtcNow;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                return (int)left.TotalMinutes + ":" + left.Seconds.ToString("00");
            }
        }

        // The time left in words for the strip under the modes, rounded up: "15 mins", "1 hr 5 mins", "less than a minute".
        // Null when no override timer is running. The ring and tooltip keep the exact digits (OverrideTimeLeft).
        public string OverrideTimeLeftInWords
        {
            get
            {
                var at = light.ReturnToTeamsAt;
                if (at == null || !AppSettings.OverrideTimesOut) return null;
                var left = at.Value - DateTime.UtcNow;
                if (left.TotalSeconds < 60) return "less than a minute";
                int mins = (int)Math.Ceiling(left.TotalMinutes), hours = mins / 60, rest = mins % 60;
                if (hours == 0) return mins + (mins == 1 ? " min" : " mins");
                return hours + (hours == 1 ? " hr" : " hrs") + (rest > 0 ? " " + rest + (rest == 1 ? " min" : " mins") : "");
            }
        }

        // e.g. "Back to Teams status in 14:52", or null when no override timer is running.
        public string OverrideCountdown
        {
            get
            {
                string left = OverrideTimeLeft;
                return left == null ? null : "Back to Teams status in " + left;
            }
        }

        void UpdateTooltip()
        {
            string tip = Program.AppName + "\n" + StatusText;
            if (OverrideOn)
            {
                string left = OverrideTimeLeft;
                tip += "\nOverride on" + (left != null ? ", Teams in " + left : "");
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void CheckOverride()
        {
            var at = light.ReturnToTeamsAt;
            if (at == null || !AppSettings.OverrideTimesOut) return;
            if (DateTime.UtcNow >= at.Value)
            {
                string overridden = LightState.Describe(light.State);
                SetState(LightState.FollowTeams);
                ShowBalloon("The " + overridden + " override ran for " + AppSettings.OverrideMinutes +
                    " minutes, so the lights are following your Teams status again.", ToolTipIcon.Info);
                return;
            }
            UpdateTooltip();
            if (DisplayChanged != null) DisplayChanged();
        }

        // Signs in (browser) to connect to Teams, or signs out to disconnect.
        // "Re-authenticate Microsoft 365" in the tray menu and Settings: signs in to Microsoft 365 again in the browser,
        // for when the sign-in has expired or been revoked, or its permissions changed. Calendar access is kept if it
        // was allowed. Without a Teams app set up for this organization, it opens the setup checklist instead.
        public void ReauthTeams()
        {
            var signingIn = presence;
            if (signingIn == null) { ShowSetup(); return; }
            presenceItem.Enabled = false;
            presenceItem.Text = "Signing in to Microsoft (check your browser)...";
            Program.Background("Microsoft 365 sign-in", delegate
            {
                string message;
                ToolTipIcon icon = ToolTipIcon.Info;
                try
                {
                    if (signingIn.CalendarGranted) signingIn.GrantCalendarAccess();
                    else signingIn.SignIn();
                    message = "Signed in to Microsoft 365 as " + signingIn.Account + ".";
                    if (callApp != null && !callApp.IsTeams && AppSettings.TeamsBusyOnCalls) signingIn.CallStarted();
                    PollTeams();
                    meetingAlerts.CheckNow();
                }
                catch (Exception ex)
                {
                    message = "Couldn't sign in to Microsoft 365: " + ex.Message;
                    icon = ToolTipIcon.Warning;
                }
                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        presenceItem.Enabled = true;
                        presenceItem.Text = ReauthText;
                        UpdateDisplay();
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
                ShowBalloon(message, icon);
            });
        }

        void TogglePresence()
        {
            if (presence.SignedIn)
            {
                Program.Background("Teams sign-out", delegate
                {
                    presence.SignOut();
                    try
                    {
                        messages.BeginInvoke((Action)delegate
                        {
                            // Back to Embrava Connect for Teams status, if it's installed.
                            teamsAvailability = null;
                            light.SetTeamsColor(null);
                            if (light.State == LightState.FollowTeams) HandBackToTeams();
                            Refresh();
                        });
                    }
                    catch (InvalidOperationException) { } // Window already gone during exit.
                });
                ShowBalloon("Disconnected from Teams. The app won't read or change your Teams status.", ToolTipIcon.Info);
                return;
            }
            presenceItem.Enabled = false;
            presenceItem.Text = "Signing in to Microsoft (check your browser)...";
            Program.Background("Teams sign-in", delegate
            {
                string message;
                ToolTipIcon icon = ToolTipIcon.Info;
                var signingIn = presence;
                try
                {
                    signingIn.SignIn();
                    message = "Connected to Teams as " + signingIn.Account + ". Follow Teams Status now uses your Teams status directly, and Teams shows you as Busy during other calls.";
                    if (callApp != null && !callApp.IsTeams && AppSettings.TeamsBusyOnCalls) signingIn.CallStarted();
                    PollTeams();
                }
                catch (Exception ex)
                {
                    // When the organization has no matching app, Microsoft shows its error in the browser and never
                    // comes back to the app, so the sign-in times out.
                    message = ex.Message + " If Microsoft said the application wasn't found, your organization's Teams app " +
                              "needs setting up: open Setup checklist from the tray menu.";
                    icon = ToolTipIcon.Warning;
                }
                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        presenceItem.Enabled = true;
                        presenceItem.Text = ReauthText;
                        UpdateDisplay();
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
                ShowBalloon(message, icon);
            });
        }

        // Safe to call from any thread.
        void ShowBalloon(string message, ToolTipIcon icon)
        {
            try { messages.BeginInvoke((Action)delegate { tray.ShowBalloonTip(5000, Program.AppName, message, icon); }); }
            catch (InvalidOperationException) { } // Window already gone during exit.
        }

        // Reads Teams presence and colors the light from it, when signed in and following Teams status.
        void PollTeams()
        {
            if (presence == null || !presence.SignedIn || light.State != LightState.FollowTeams || AppSettings.TeamsDisabled) return;
            string availability = presence.ReadAvailability();
            if (availability == null) return;

            if (!embravaWarned && System.Diagnostics.Process.GetProcessesByName("EmbravaConnect").Length > 0)
            {
                embravaWarned = true;
                ShowBalloon("Embrava Connect is also running and may change the light. This app now shows your Teams status itself, so you can quit or uninstall Embrava Connect.", ToolTipIcon.Info);
            }
            try
            {
                messages.BeginInvoke((Action)delegate
                {
                    teamsAvailability = availability;
                    light.SetTeamsColor(ColorForAvailability(availability));
                    Refresh();
                });
            }
            catch (InvalidOperationException) { } // Window already gone during exit.
        }

        static string ColorForAvailability(string availability)
        {
            switch (availability)
            {
                case "Available": case "AvailableIdle": return "green";
                case "Busy": case "BusyIdle": return "red";
                case "DoNotDisturb": return "purple";
                case "Away": case "BeRightBack": return "yellow";
                default: return "off"; // Offline, PresenceUnknown
            }
        }

        static string DescribeAvailability(string availability)
        {
            switch (availability)
            {
                case "Available": case "AvailableIdle": return "Available";
                case "Busy": case "BusyIdle": return "Busy";
                case "DoNotDisturb": return "Do not disturb";
                case "Away": return "Away";
                case "BeRightBack": return "Be right back";
                case "Offline": return "Offline";
                default: return "Unknown";
            }
        }

        // Puts the light back on Teams status: read directly when signed in, otherwise through Embrava Connect
        // (restarted so it repaints from current Teams/Calendar presence).
        void HandBackToTeams()
        {
            if (AppSettings.TeamsDisabled) return;
            if (presence != null && presence.SignedIn)
            {
                Program.Background("Teams status check", delegate { PollTeams(); });
                return;
            }
            Program.Background("Embrava Connect restart", delegate
            {
                bool installed;
                try { installed = EmbravaConnect.Restart(); }
                catch (Exception) { installed = false; }
                if (installed) return;
                try
                {
                    messages.BeginInvoke((Action)delegate
                    {
                        // Not signed in to Teams here, and no Embrava Connect to fall back on: say what to do.
                        tray.ShowBalloonTip(5000, Program.AppName, presence == null
                            ? "Not connected to Microsoft Teams, so your Teams status isn't showing on the light. Open Setup checklist in Settings to set it up."
                            : "Not connected to Microsoft Teams, so your Teams status isn't showing on the light. Connect in Settings > Microsoft Teams.",
                            ToolTipIcon.Warning);
                    });
                }
                catch (InvalidOperationException) { } // Window already gone during exit.
            });
        }

        // While On a call flashes, the tray icon and window flash the same two colors as the lights, at the On a call
        // speed but never faster than twice a second: a large area flashing red and blue faster than that can trigger
        // seizures.
        const int SlowestSafeFlashMs = 500;
        readonly System.Windows.Forms.Timer callFlashTimer = new System.Windows.Forms.Timer();
        bool callFlashSecond;

        void Refresh()
        {
            bool partying = State == LightState.Party && !NoLight;
            if (partying && !flashTimer.Enabled) partyHue = 0;
            flashTimer.Enabled = partying;
            bool calling = State == LightState.CallFlash && !NoLight;
            if (calling) callFlashTimer.Interval = Math.Max(SlowestSafeFlashMs, AppSettings.CallFlashMs);
            if (!calling) callFlashSecond = false;
            callFlashTimer.Enabled = calling;
            UpdateDisplay();
            UpdateTeamsMessage();
        }

        // While the lights are red by the user's choice or because of a call, shows their chosen Teams status message.
        // Picking Busy or On a call also shows them as Busy in Teams, and Open as Available (except during a call).
        void UpdateTeamsMessage()
        {
            if (presence == null) return;
            if (AppSettings.TeamsDisabled)
            {
                // Puts back the user's own Teams status message and status, then leaves Teams alone.
                presence.ShowStatusMessage(null);
                presence.ShowPresence(null);
                return;
            }
            bool picked = light.State == LightState.Red || light.State == LightState.CallFlash;
            bool red = picked || callApp != null;
            presence.ShowStatusMessage(red && AppSettings.TeamsStatusMessageOn ? AppSettings.WithDelayNote(AppSettings.TeamsStatusMessage) : null);
            string status = null;
            if (AppSettings.TeamsBusyWhenPicked)
            {
                if (picked) status = "Busy";
                else if (light.State == LightState.Green && callApp == null) status = "Available";
            }
            // Do Not Disturb takes the place of Busy: when you pick Busy or On a call, and during any call: Teams' own
            // (which would otherwise show In a call) and other apps' (when "Show me as Busy during calls" is on).
            bool inCall = callApp != null && (callApp.IsTeams || AppSettings.TeamsBusyOnCalls);
            if (AppSettings.TeamsDoNotDisturb && (status == "Busy" || inCall))
                status = "DoNotDisturb";
            presence.ShowPresence(status);
        }

        // Control my USB lights in Settings > Light Control, switched off: USB light integration is off (the lights go
        // dark and are left alone), or back on.
        public void SetLightsDisabled(bool disable)
        {
            if (disable == AppSettings.UsbLightsDisabled) return;
            AppSettings.UsbLightsDisabled = disable;
            light.LightsEnabled = !disable;
            Refresh();
            ShowBalloon(disable
                ? "USB lights turned off. Teams status and call detection keep working."
                : "USB lights turned back on.", ToolTipIcon.Info);
        }

        // Sync with Microsoft Teams in Settings > Microsoft Teams, switched off. Your sign-in is kept.
        public void SetTeamsDisabled(bool disable)
        {
            if (disable == AppSettings.TeamsDisabled) return;
            AppSettings.TeamsDisabled = disable;
            if (disable)
            {
                if (presence != null) presence.CallEnded(); // Clears the Busy this app set for a non-Teams call.
                teamsAvailability = null;
                light.SetTeamsColor(null);
            }
            else
            {
                calls.Recheck(); // Sets Busy again if a call is already in progress.
                Program.Background("Teams status check", delegate { PollTeams(); });
            }
            Refresh();
            ShowBalloon(disable
                ? "Teams integration turned off. Your lights and call detection keep working."
                : "Teams integration turned back on.", ToolTipIcon.Info);
        }

        // No light plugged in, while the app is meant to be controlling lights.
        bool NoLight { get { return light.LightsEnabled && light.LightCount == 0; } }

        public string StatusText
        {
            get
            {
                if (NoLight) return "Light not found";
                if (light.OverrideActive) return callApp == null ? "Testing the light" : "On a call (" + callApp.Name + ")";
                if (light.State == LightState.FollowTeams && AppSettings.TeamsDisabled) return "Teams integration disabled";
                if (light.State == LightState.FollowTeams && teamsAvailability != null) return "Teams: " + DescribeAvailability(teamsAvailability);
                return LightState.Describe(State);
            }
        }

        // The tray and window color for a status: the usual softer screen color while the default is picked in
        // Settings > Colors, otherwise the picked color.
        static Color PickedColor(string setting, Color usual)
        {
            string name = AppSettings.LightColorName(setting);
            foreach (var role in AppSettings.LightColorRoles)
                if (role[0] == setting && role[2] == name) return usual;
            var color = BlyncDevice.ColorFor(name);
            return Color.FromArgb(color.R, color.G, color.B);
        }

        void UpdateDisplay()
        {
            if (exiting) return;
            if (NoLight) DisplayColor = Color.Gray;
            else if (State == LightState.Red) DisplayColor = PickedColor("BusyColor", Color.Red);
            // Alternates with the call flash timer, like the lights.
            else if (State == LightState.CallFlash)
                DisplayColor = callFlashSecond ? PickedColor("CallColor2", Color.Blue) : PickedColor("CallColor1", Color.Red);
            else if (State == LightState.Green) DisplayColor = PickedColor("AvailableColor", Color.Lime);
            else if (State == LightState.Party)
            {
                byte r, g, b;
                BlyncDevice.HueToRgb(partyHue, out r, out g, out b);
                DisplayColor = Color.FromArgb(r, g, b);
            }
            // Teams status colors, when the app reads presence itself.
            else if (State == "yellow") DisplayColor = PickedColor("AwayColor", Color.Gold);
            else if (State == "purple") DisplayColor = PickedColor("DndColor", Color.MediumPurple);
            // The real color is up to Embrava Connect; show Teams purple to mean "Teams decides".
            else if (State == LightState.FollowTeams) DisplayColor = Color.FromArgb(98, 100, 167);
            else DisplayColor = Color.Black;

            tray.Icon = IconFor(DisplayColor, OverrideOn);
            UpdateTooltip();
            // Check the user's own choice, even while a call is overriding it.
            foreach (var pair in stateItems) pair.Value.Checked = pair.Key == light.State;
            UpdateStateItemNames();
            if (presenceItem != null)
            {
                presenceItem.Checked = presence != null && presence.SignedIn;
                presenceItem.ToolTipText = presence == null ? "Your organization's Teams app isn't set up yet. Click to open the setup checklist."
                    : presence.SignedIn ? "Signed in as " + presence.Account + ". Click to sign in to Microsoft 365 again, if Teams status or meeting alerts stopped working."
                    : "Sign in to Microsoft so the light follows your Teams status and Teams shows Busy during other calls.";
            }
            NotifyIfChanged();
            if (DisplayChanged != null) DisplayChanged();
        }

        // Shows a notification when what the light shows changes (not for each Party Mode color, and not at startup).
        void NotifyIfChanged()
        {
            string key = NoLight ? "no light" : State + "|" + StatusText;
            if (key == lastNotifiedKey) return;
            bool first = lastNotifiedKey == null;
            lastNotifiedKey = key;
            if (first || !AppSettings.ShowNotifications || !light.LightsEnabled) return; // No light changes to report while USB lights are off.

            string message;
            if (NoLight) message = "No light found. Plug your Blynclight or Luxafor back in.";
            else if (State == LightState.Party) message = "Party Mode: cycling through every color.";
            else if (State == LightState.FollowTeams) message = "Light handed to Embrava Connect to show your Teams status.";
            else if (State == LightState.Off) message = "Light turned off.";
            else if (State == LightState.CallFlash) message = "Light flashing red and blue: " + StatusText + ".";
            else
            {
                string color = State; // "red", "green", "purple", "yellow"
                message = string.Equals(StatusText, color, StringComparison.OrdinalIgnoreCase)
                    ? "Light set to " + color + "."
                    : "Light set to " + color + ": " + StatusText + ".";
            }
            tray.ShowBalloonTip(3000, Program.AppName, message, ToolTipIcon.None);
        }

        // A filled circle of the light's color. While an override is on, a white pin badge sits in the corner.
        Icon IconFor(Color color, bool overrideOn)
        {
            string key = color.ToArgb() + (overrideOn ? "+override" : "");
            Icon icon;
            if (icons.TryGetValue(key, out icon)) return icon;
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var fill = new SolidBrush(color))
                using (var outline = new Pen(Color.FromArgb(60, 60, 60), 2))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(fill, 3, 3, 26, 26);
                    g.DrawEllipse(outline, 3, 3, 26, 26);
                    if (overrideOn)
                    {
                        using (var badge = new SolidBrush(Color.White))
                        using (var badgeOutline = new Pen(Color.FromArgb(40, 40, 40), 2))
                        {
                            g.FillEllipse(badge, 18, 18, 13, 13);
                            g.DrawEllipse(badgeOutline, 18, 18, 13, 13);
                        }
                    }
                }
                icon = Icon.FromHandle(bmp.GetHicon());
            }
            icons[key] = icon;
            return icon;
        }

        // Set while closing, so a display update still queued from a background thread doesn't touch the tray icon
        // after it's gone.
        bool exiting;

        protected override void ExitThreadCore()
        {
            exiting = true;
            Program.ForceCloseAfter(10000, "exit");
            for (int id = 0; id < HotkeyStates.Length; id++) UnregisterHotKey(messages.Handle, id);
            calls.Dispose();
            muteButton.Dispose(); // Turns its ring off.
            deviceCheck.Dispose();
            if (k2Key != null) k2Key.Dispose();
            if (teamsPoll != null) teamsPoll.Dispose();
            if (meetingAlerts != null) meetingAlerts.Dispose();
            if (presence != null) presence.Dispose(); // Clears Busy if a phone call is in progress.
            if (updateTimer != null) updateTimer.Dispose();
            showWait.Unregister(null);
            light.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

}
