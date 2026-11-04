using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // All of the app's settings, in the style of Windows 11 Settings: a list of pages on the left (General, Calls,
    // Microsoft Teams, Devices), and the picked page on the right, with each section a rounded card of on/off
    // switches. Changes take effect as soon as a switch is flipped.
    class SettingsForm : Form
    {
        const int RefreshMs = 2000;
        // The page area's width, and the page list's to its left.
        const int width = 500, navWidth = 170;
        // Tags the version line, shown in the softer text color.
        const string VersionLabel = "version";
        // Page icons: Segoe MDL2 Assets settings gear, phone, people, microphone and keyboard.
        const string GeneralGlyph = "\uE713", CallsGlyph = "\uE717", TeamsGlyph = "\uE716", MuteSyncGlyph = "\uE720", K2Glyph = "\uE765";

        readonly TrayContext tray;
        readonly CheckBox startWithWindows, openWindow;
        readonly Label teamsStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0) };
        readonly Label muteButtonStatus = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f) };
        readonly Label k2Status = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f) };
        readonly Button connectButton = new ModernButton();
        readonly Timer refresh = new Timer { Interval = RefreshMs };
        readonly List<SettingsPage> pages = new List<SettingsPage>();
        readonly List<NavItem> navItems = new List<NavItem>();
        // The page being filled in while building, its cards, and where the open card started (-1 when none is open).
        SettingsPage page;
        int cardTop = -1;
        int y;

        public SettingsForm(TrayContext tray)
        {
            this.tray = tray;
            Text = "Settings - " + Program.AppName;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            BackColor = Theme.Page;
            DoubleBuffered = true;

            StartPage("General", GeneralGlyph);
            Heading("General", GeneralGlyph);
            AppearancePickers();
            startWithWindows = Box("Start with Windows", AppSettings.StartWithWindows, v => AppSettings.StartWithWindows = v);
            openWindow = Box("Open the window at sign-in (off: start in the tray only)", AppSettings.OpenWindowAtSignIn,
                v => AppSettings.OpenWindowAtSignIn = v, true);
            startWithWindows.CheckedChanged += delegate { openWindow.Enabled = startWithWindows.Checked; };
            openWindow.Enabled = startWithWindows.Checked;
            // Red, Green and On A Call override Teams status until this runs out.
            var timesOut = Box("Go back to Follow Teams Status after",
                AppSettings.OverrideTimesOut, v => { AppSettings.OverrideTimesOut = v; tray.ApplySettings(); });
            var minutes = OverrideMinutesPicker(timesOut);
            timesOut.CheckedChanged += delegate { minutes.Enabled = timesOut.Checked; };
            minutes.Enabled = timesOut.Checked;
            Box("Show a notification when the lights change", AppSettings.ShowNotifications, v => AppSettings.ShowNotifications = v);
            Box("Hotkeys: Alt+R for Busy, Alt+G for Available", AppSettings.HotkeysEnabled, v => { AppSettings.HotkeysEnabled = v; tray.ApplySettings(); });
            var updates = Box("Install updates automatically", AppSettings.AutoUpdate, v => AppSettings.AutoUpdate = v);
            if (Program.UpdateRepo != null) SideButton(updates, "Check now", delegate { tray.CheckForUpdates(true); });
            // Updates are automatic for the all-users copy (its background updater installs them).
            if (AllUsersInstall.IsThisCopy) updates.Enabled = false;
            else if (Program.UpdateRepo != null) InstallForEveryoneRow();
            EndPage();

            StartPage("Calls", CallsGlyph);
            Heading("Calls", CallsGlyph);
            var red = Box("Turn lights red when my mic is in use", AppSettings.CallsTurnRed,
                v => { AppSettings.CallsTurnRed = v; tray.ApplySettings(); });
            SideButton(red, "Ignore apps...", delegate
            {
                using (var dialog = new IgnoreAppsDialog()) dialog.ShowDialog(this);
                tray.ApplySettings();
            });
            var flash = Box("Flash red and blue instead of steady red", AppSettings.FlashOnCalls,
                v => { AppSettings.FlashOnCalls = v; tray.ApplySettings(); }, true);
            SideButton(flash, "Test microphone...", delegate
            {
                using (var dialog = new MicTestForm()) dialog.ShowDialog(this);
                tray.ApplySettings();
            });
            red.CheckedChanged += delegate { flash.Enabled = red.Checked; };
            flash.Enabled = red.Checked;
            Box("Show me as Busy in Teams during calls in other apps", AppSettings.TeamsBusyOnCalls,
                v => { AppSettings.TeamsBusyOnCalls = v; tray.ApplySettings(); });
            EndPage();

            StartPage("Microsoft Teams", TeamsGlyph);
            Heading("Microsoft Teams", TeamsGlyph);
            teamsStatus.Location = new Point(24, y + 6);
            Add(teamsStatus);
            connectButton.Bounds = new Rectangle(350, y, 130, 32);
            connectButton.Click += delegate
            {
                if (tray.Presence != null && tray.Presence.SignedIn) tray.DisconnectTeams();
                else if (tray.Presence != null) tray.ConnectTeams();
                else ShowTeamsAppDialog();
                RefreshTeams();
            };
            Add(connectButton);
            y += 44;
            var orgApp = new ModernButton { Text = "Organization's Teams app...", Bounds = new Rectangle(24, y, 220, 32) };
            orgApp.Click += delegate { ShowTeamsAppDialog(); };
            Add(orgApp);
            y += 44;
            Box("Match my Teams status when I pick Busy, Available or On a call", AppSettings.TeamsBusyWhenPicked,
                v => { AppSettings.TeamsBusyWhenPicked = v; tray.ApplySettings(); });
            var messageOn = Box("Set my Teams status message while my lights are red:",
                AppSettings.TeamsStatusMessageOn, v => { AppSettings.TeamsStatusMessageOn = v; tray.ApplySettings(); });
            var message = StatusMessagePicker(24 + 46);
            messageOn.CheckedChanged += delegate { message.Enabled = messageOn.Checked; };
            message.Enabled = messageOn.Checked;
            EndPage();

            StartPage("mutesync", MuteSyncGlyph);
            // Pressing a mutesync button mutes or unmutes the mic; its ring shows red when muted, green when live.
            Heading("mutesync button", MuteSyncGlyph);
            var muteSync = Box("Mute and unmute my mic with a mutesync button", AppSettings.MuteSyncEnabled,
                v => { AppSettings.MuteSyncEnabled = v; tray.ApplySettings(); RefreshDevices(); });
            SideButton(muteSync, "Ring colors...", delegate
            {
                using (var dialog = new LightsDialog(tray)) dialog.ShowDialog(this);
            });
            y -= 6;
            muteButtonStatus.Location = new Point(24 + 46, y);
            Add(muteButtonStatus);
            y += 30;
            EndPage();

            StartPage("K2 Key", K2Glyph);
            K2Section();
            EndPage();
            RefreshDevices();

            // Below the pages, across the whole window.
            int bottom = 0;
            foreach (var p in pages) bottom = Math.Max(bottom, p.ContentHeight);
            bottom = Math.Max(bottom, 12 + navItems.Count * 44);
            int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80 - 92;
            int pageHeight = Math.Min(bottom, room);
            foreach (var p in pages) p.Bounds = new Rectangle(navWidth, 0, width, pageHeight);
            y = pageHeight + 8;
            Footer();
            ShowPage(0);

            Load += delegate
            {
                Theme.Apply(this);
                ColorSurfaces();
            };
            refresh.Tick += delegate { RefreshTeams(); RefreshDevices(); };
            Shown += delegate { RefreshTeams(); refresh.Start(); };
            FormClosed += delegate { refresh.Dispose(); };
        }

        // The K2 macro key: an action for each button and for both together, and the email address it can type.
        void K2Section()
        {
            Heading("K2 Key", K2Glyph);
            k2Status.Location = new Point(24, y);
            Add(k2Status);
            y += 28;
            K2Row("Left button", K2Key.Button.Left);
            K2Row("Right button", K2Key.Button.Right);
            K2Row("Both together", K2Key.Button.Both);
            y += 4;
            Add(new Label { Text = "My email address", AutoSize = true, Location = new Point(24, y + 4) });
            var email = new TextBox { Bounds = new Rectangle(190, y, 290, 28), Text = AppSettings.MacroEmail };
            email.TextChanged += delegate { AppSettings.MacroEmail = email.Text; };
            Add(email);
            y += 40;
            Add(new Label
            {
                Text = "Out of the box the K2's buttons are also mouse clicks, so each press clicks where your pointer is too. " +
                       "To stop that, set them to F13 and F14 with the K2's own setup tool.",
                AutoSize = true,
                MaximumSize = new Size(width - 48, 0),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(24, y),
                Tag = VersionLabel,
            });
            y += 56;
            Add(new Label
            {
                Text = "Tested with the Motospeed K2 2 Button RGB Keyboard.",
                AutoSize = true,
                MaximumSize = new Size(width - 48, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                Location = new Point(24, y),
                Tag = VersionLabel,
            });
            y += 26;
        }

        // A K2 button's action, and a box for the text it needs (greyed out for actions that need none).
        void K2Row(string label, K2Key.Button button)
        {
            Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y + 4) });
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(140, y, 200, 30) };
            foreach (var action in K2Actions.All) picker.Items.Add(action[1]);
            string current = AppSettings.K2Action(button);
            for (int i = 0; i < K2Actions.All.Length; i++) if (K2Actions.All[i][0] == current) picker.SelectedIndex = i;
            var value = new TextBox { Bounds = new Rectangle(348, y + 1, 132, 28), Text = AppSettings.K2Value(button) };
            Action showValue = () =>
            {
                string action = K2Actions.All[picker.SelectedIndex][0];
                value.Enabled = K2Actions.NeedsValue(action);
                // A hint in the empty box for what goes there; the email action shows the address it types.
                value.Text = value.Enabled ? AppSettings.K2Value(button) : action == "TypeEmail" ? AppSettings.MacroEmail : "";
                SetCue(value, K2Actions.Hint(action));
            };
            picker.SelectedIndexChanged += delegate
            {
                AppSettings.SetK2Action(button, K2Actions.All[picker.SelectedIndex][0]);
                showValue();
            };
            value.TextChanged += delegate { if (value.Enabled) AppSettings.SetK2Value(button, value.Text); };
            showValue();
            Add(picker);
            Add(value);
            y += 40;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        // Grey hint text shown in an empty box.
        static void SetCue(TextBox box, string cue)
        {
            const int EM_SETCUEBANNER = 0x1501;
            if (box.IsHandleCreated) SendMessage(box.Handle, EM_SETCUEBANNER, IntPtr.Zero, cue);
            else box.HandleCreated += delegate { SendMessage(box.Handle, EM_SETCUEBANNER, IntPtr.Zero, cue); };
        }

        // Version, crash log and coffee on one line, then Setup checklist, Help, Light Control and Close.
        void Footer()
        {
            int full = navWidth + width;
            // Small and yellow, on its own line above the bottom buttons.
            var yellow = Color.FromArgb(255, 221, 0);
            var coffee = new Button
            {
                Text = "Buy me a coffee",
                Tag = Theme.KeepColors,
                FlatStyle = FlatStyle.Flat,
                BackColor = yellow,
                ForeColor = Color.FromArgb(24, 24, 28),
                Font = Theme.Tiny,
                Cursor = Cursors.Hand,
            };
            coffee.FlatAppearance.BorderSize = 0;
            coffee.FlatAppearance.MouseOverBackColor = Theme.Mix(yellow, Color.Black, 0.08);
            coffee.Size = new Size(TextRenderer.MeasureText(coffee.Text, coffee.Font).Width + 8, 20);
            coffee.Location = new Point(full - 12 - coffee.Width, y);
            coffee.Click += delegate { tray.OpenCoffee(); };
            Controls.Add(coffee);
            // On the same line, at the left: for sending what went wrong after a crash.
            var crashLog = new LinkLabel
            {
                Text = "Email crash log...",
                AutoSize = true,
                Location = new Point(14, y + 2),
                // Lightened in Dark mode, where the plain accent is hard to read on the dark page.
                LinkColor = AppSettings.DarkMode ? Theme.Mix(Theme.Teams, Color.White, 0.45) : Theme.Teams,
                ActiveLinkColor = Theme.Teams,
                LinkBehavior = LinkBehavior.HoverUnderline,
            };
            crashLog.LinkClicked += delegate { CrashReport.Email(); };
            Controls.Add(crashLog);
            // Between the two, which version this is and when it was built, e.g. "Version 2.24.0 · Last updated Nov 3, 2026".
            DateTime built;
            string date = DateTime.TryParse(Program.BuildDate, out built) ? built.ToString("MMM d, yyyy") : Program.BuildDate;
            Controls.Add(new Label
            {
                Text = "Version " + Program.VersionText + "  ·  Last updated " + date,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(crashLog.Right + 4, y, coffee.Left - crashLog.Right - 8, 20),
                Tag = VersionLabel,
            });
            y += 32;

            var setup = new ModernButton { Text = "Setup checklist...", Bounds = new Rectangle(12, y, 150, 34) };
            setup.Click += delegate { tray.ShowSetup(); };
            Controls.Add(setup);
            var help = new ModernButton { Text = "Help", Bounds = new Rectangle(170, y, 80, 34) };
            help.Click += delegate { tray.OpenHelp(); };
            Controls.Add(help);
            // Brightness, call flash speed and colors, in their own window.
            // In the Party Mode colors, with a light bulb, so it stands out as the fun one.
            var lights = new ModernButton { Text = "Light Control", Rainbow = true, Glyph = "\uE82F", Bounds = new Rectangle(258, y, 124, 34) };
            lights.Click += delegate
            {
                using (var dialog = new LightsDialog(tray)) dialog.ShowDialog(this);
            };
            Controls.Add(lights);
            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(full - 112, y, 100, 34) };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
            ClientSize = new Size(full, close.Bottom + 14);
        }

        // Starts a page and its entry in the list on the left; the helpers below add to it until EndPage.
        void StartPage(string title, string glyph)
        {
            page = new SettingsPage { Visible = false };
            pages.Add(page);
            Controls.Add(page);
            int index = navItems.Count;
            var item = new NavItem { Text = title, Glyph = glyph, Bounds = new Rectangle(8, 12 + index * 44, navWidth - 16, 38) };
            item.Click += delegate { ShowPage(index); };
            navItems.Add(item);
            Controls.Add(item);
            y = 8;
            cardTop = -1;
        }

        void EndPage()
        {
            CloseCard();
            page.ContentHeight = y + 8;
        }

        void ShowPage(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navItems[i].Selected = i == index;
            }
        }

        void Add(Control control) { page.Controls.Add(control); }

        // A just-for-me copy (in AppData) offers to install for everyone in Program Files, with a Windows admin
        // prompt. Once installed, this copy hands over to it (see TrayContext.WatchInstall).
        void InstallForEveryoneRow()
        {
            Add(new Label
            {
                Text = "Installed just for you. Install for everyone on this PC in Program Files (needs an admin).",
                AutoSize = true,
                MaximumSize = new Size(350 - 24 - 8, 0),
                Location = new Point(24, y),
            });
            var install = new ModernButton { Text = "Install for everyone", Bounds = new Rectangle(350, y, 130, 30) };
            install.Click += delegate
            {
                install.Enabled = false;
                install.Text = "Installing...";
                Program.Background("Installing for everyone", delegate
                {
                    string result;
                    try
                    {
                        result = AllUsersInstall.InstallForEveryone()
                            ? "Installed for everyone on this PC. BusyLightz reopens from Program Files in a few seconds."
                            : null;
                    }
                    catch (Exception ex) { result = "Couldn't install for everyone: " + ex.Message; }
                    BeginInvoke((Action)delegate
                    {
                        install.Enabled = true;
                        install.Text = "Install for everyone";
                        if (result != null) MessageBox.Show(this, result, Program.AppName);
                    });
                });
            };
            Add(install);
            y += 46;
        }

        void ShowTeamsAppDialog()
        {
            using (var dialog = new TeamsAppDialog(tray)) dialog.ShowDialog(this);
            RefreshTeams();
        }

        // e.g. "Connected on COM3", and whether a K2 is plugged in.
        void RefreshDevices()
        {
            muteButtonStatus.Text = "Status: " + tray.MuteButton.Status;
            k2Status.Text = tray.K2Connected ? "Status: Connected" : "Status: Not plugged in";
        }

        void RefreshTeams()
        {
            var presence = tray.Presence;
            if (presence == null)
            {
                teamsStatus.Text = "Your organization's Teams app isn't set up yet.";
                connectButton.Text = "Set up...";
            }
            else if (presence.SignedIn)
            {
                teamsStatus.Text = "Connected as " + presence.Account + ".";
                connectButton.Text = "Disconnect";
            }
            else
            {
                teamsStatus.Text = "Not connected. Connect to show your Teams status on the lights.";
                connectButton.Text = "Connect";
            }
        }

        // A section title with its icon, starting a new card for the rows below it.
        void Heading(string text, string glyph)
        {
            CloseCard();
            y += page.Cards.Count == 0 ? 4 : 12;
            int x = 16;
            if (Theme.Icons != null)
            {
                Add(new Label { Text = glyph, Font = new Font(Theme.Icons.FontFamily, 12f), AutoSize = true, Location = new Point(x, y + 4) });
                x += 26;
            }
            Add(new Label { Text = text, Font = new Font("Segoe UI Semibold", 11f), AutoSize = true, Location = new Point(x, y) });
            y += 32;
            cardTop = y;
            y += 12;
        }

        void CloseCard()
        {
            if (cardTop < 0) return;
            page.Cards.Add(new Rectangle(12, cardTop, width - 24, y + 2 - cardTop));
            y += 4;
            cardTop = -1;
        }

        // An on/off switch row; indented ones line up under the text of the row above, which they depend on.
        CheckBox Box(string text, bool value, Action<bool> save, bool indented = false)
        {
            int x = indented ? 24 + 46 : 24;
            var box = new ToggleBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y), MaximumSize = new Size(width - 24 - x, 0) };
            box.CheckedChanged += delegate { save(box.Checked); };
            Add(box);
            y += Math.Max(32, box.Height + 10);
            return box;
        }

        // Puts the page and card colors behind the controls (Theme.Apply only colors them in Dark mode, and with
        // the page color), so each control blends into the card it sits on.
        void ColorSurfaces()
        {
            foreach (var p in pages)
            {
                p.BackColor = Theme.Page;
                foreach (Control control in p.Controls) ColorSurface(control, p.Cards.Exists(c => c.Contains(control.Location)));
            }
            foreach (Control control in Controls) if (!(control is SettingsPage)) ColorSurface(control, false);
            Invalidate(true);
        }

        static void ColorSurface(Control control, bool onCard)
        {
            if (Theme.KeepColors.Equals(control.Tag) || control is ComboBox || control is TextBox || control is NavItem) return;
            control.BackColor = onCard ? Theme.Card : Theme.Page;
            if (control is Label && !(control is LinkLabel))
                control.ForeColor = VersionLabel.Equals(control.Tag) ? Theme.Muted : Theme.Text;
        }

        // A line between the page list and the pages.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int bottom = pages.Count > 0 ? pages[0].Bottom : 0;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, navWidth - 1, 8, navWidth - 1, bottom - 4);
        }

        // The window style and Light or Dark colors. A change reopens the app's windows in the new look.
        void AppearancePickers()
        {
            Picker("Window style", WindowStyles.Names, Array.IndexOf(WindowStyles.All, AppSettings.WindowStyle),
                index => AppSettings.WindowStyle = WindowStyles.All[index]);
            Picker("Appearance", new[] { "Light", "Dark" }, AppSettings.DarkMode ? 1 : 0, index => AppSettings.DarkMode = index == 1);
        }

        void Picker(string label, string[] choices, int current, Action<int> save)
        {
            Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y + 3) });
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(350, y, 130, 30) };
            picker.Items.AddRange(choices);
            picker.SelectedIndex = current;
            picker.SelectedIndexChanged += delegate
            {
                save(picker.SelectedIndex);
                tray.ApplyTheme();
            };
            Add(picker);
            y += 36;
        }

        // How long an override lasts, next to its checkbox. A change restarts a running timer with the new length.
        ComboBox OverrideMinutesPicker(CheckBox row)
        {
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(350, row.Top - 3, 130, 30) };
            int current = AppSettings.OverrideMinutes;
            if (Array.IndexOf(AppSettings.OverrideMinuteChoices, current) < 0) current = AppSettings.DefaultOverrideMinutes;
            foreach (int choice in AppSettings.OverrideMinuteChoices)
            {
                picker.Items.Add(DescribeMinutes(choice));
                if (choice == current) picker.SelectedIndex = picker.Items.Count - 1;
            }
            picker.SelectedIndexChanged += delegate
            {
                AppSettings.OverrideMinutes = AppSettings.OverrideMinuteChoices[picker.SelectedIndex];
                tray.RestartOverrideTimer();
            };
            Add(picker);
            Narrow(row, picker.Left - row.Left - 8);
            return picker;
        }

        // The status message choices, plus "Custom message" with a box to type one. Returns a panel holding both.
        Panel StatusMessagePicker(int x)
        {
            const string Custom = "Custom message...";
            var panel = new Panel { Bounds = new Rectangle(x, y, 480 - x, 32) };
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(0, 0, 230, 30) };
            var custom = new TextBox { Bounds = new Rectangle(238, 1, panel.Width - 238, 28), MaxLength = AppSettings.TeamsStatusMessageMaxLength };
            picker.Items.AddRange(AppSettings.TeamsStatusMessageChoices);
            picker.Items.Add(Custom);

            string saved = AppSettings.TeamsStatusMessage;
            int index = Array.IndexOf(AppSettings.TeamsStatusMessageChoices, saved);
            picker.SelectedIndex = index >= 0 ? index : picker.Items.Count - 1;
            custom.Text = index >= 0 ? "" : saved;
            custom.Visible = index < 0;

            picker.SelectedIndexChanged += delegate
            {
                bool isCustom = (string)picker.SelectedItem == Custom;
                custom.Visible = isCustom;
                if (isCustom) { custom.Focus(); SaveCustom(custom); }
                else
                {
                    AppSettings.TeamsStatusMessage = (string)picker.SelectedItem;
                    tray.ApplySettings();
                }
            };
            // Saved as you type; Teams is updated once you leave the box.
            custom.TextChanged += delegate { if (custom.Text.Trim().Length > 0) AppSettings.TeamsStatusMessage = custom.Text.Trim(); };
            custom.Leave += delegate { SaveCustom(custom); };

            panel.Controls.Add(picker);
            panel.Controls.Add(custom);
            Add(panel);
            y += 36;
            return panel;
        }

        void SaveCustom(TextBox custom)
        {
            if (custom.Text.Trim().Length == 0) return; // Keeps the last message until something is typed.
            AppSettings.TeamsStatusMessage = custom.Text.Trim();
            tray.ApplySettings();
        }

        static string DescribeMinutes(int minutes)
        {
            if (minutes < 60) return minutes + " minutes";
            return minutes == 60 ? "1 hour" : minutes / 60 + " hours";
        }

        void SideButton(CheckBox row, string text, EventHandler click)
        {
            var button = new ModernButton { Text = text, Bounds = new Rectangle(350, row.Top - 4, 130, 30) };
            button.Click += click;
            Add(button);
            Narrow(row, button.Left - row.Left - 8);
        }

        // Makes room for a control beside the row just added; text that now wraps pushes the next row down.
        void Narrow(CheckBox row, int maxWidth)
        {
            row.MaximumSize = new Size(maxWidth, 0);
            row.Size = row.GetPreferredSize(Size.Empty);
            y = Math.Max(y, row.Top + Math.Max(32, row.Height + 10));
        }
    }

    // One page of Settings: its cards drawn behind its controls, scrolling if it's taller than the window.
    class SettingsPage : Panel
    {
        public readonly List<Rectangle> Cards = new List<Rectangle>();
        public int ContentHeight;

        public SettingsPage()
        {
            DoubleBuffered = true;
            AutoScroll = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            foreach (var card in Cards) Theme.FillRounded(g, card, 8, Theme.Card, Theme.Border);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }
    }

    // An entry in Settings' page list: an icon and name, tinted with an accent bar while its page is showing.
    class NavItem : Control
    {
        public string Glyph;
        bool selected, hover;
        static readonly Font SelectedFont = new Font("Segoe UI Semibold", 10f);

        public NavItem()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 10f);
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.Page);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (selected) Theme.FillRounded(g, r, 6, Theme.Card, Theme.Card);
            else if (hover) Theme.FillRounded(g, r, 6, Theme.Wash(Theme.Teams, 0.9), Theme.Wash(Theme.Teams, 0.9));
            if (selected)
                using (var bar = new SolidBrush(Theme.Teams)) g.FillRectangle(bar, 0, Height / 2 - 8, 3, 16);
            int x = 14;
            if (Theme.Icons != null)
            {
                using (var icon = new Font(Theme.Icons.FontFamily, 11f))
                    TextRenderer.DrawText(g, Glyph, icon, new Rectangle(x, 0, 22, Height), Theme.Text, TextFormatFlags.VerticalCenter);
                x += 28;
            }
            TextRenderer.DrawText(g, Text, selected ? SelectedFont : Font, new Rectangle(x, 0, Width - x, Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Light Control: brightness, call flash speed and the color for each status. Each change is saved at once and
    // shown on the lights for a few seconds, whatever they were showing.
    class LightsDialog : Form
    {
        const int PreviewMs = 4000;
        const int width = 460;
        // A TrackBar can't be see-through, so Load gives each the window's color (Dark mode included).
        readonly TrayContext tray;
        readonly List<ComboBox> colorPickers = new List<ComboBox>();
        int y = 16;

        public LightsDialog(TrayContext tray)
        {
            this.tray = tray;
            Text = "Light Control";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);

            Controls.Add(new Label
            {
                Text = "Changes show on your lights for a few seconds as you make them.",
                AutoSize = true,
                MaximumSize = new Size(width - 40, 0),
                Location = new Point(20, y),
            });
            y += 34;

            // The same setting as Disable USB Light Integration in the tray menu. While on, the rest of this window
            // has nothing to show, so it's greyed out.
            var disableAll = new ToggleBox
            {
                Text = "Disable All Light Functions",
                Checked = AppSettings.UsbLightsDisabled,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10),
                Location = new Point(20, y),
            };
            Controls.Add(disableAll);
            y += 26;
            Controls.Add(new Label
            {
                Text = "Turns your USB lights off and stops controlling them. Teams status and call detection keep working.",
                AutoSize = true,
                MaximumSize = new Size(width - 66, 0),
                Font = new Font("Segoe UI", 9),
                Location = new Point(66, y),
                Tag = MutedText,
            });
            y += 48;
            int firstLightControl = Controls.Count;

            // Brightness in steps of 5%; the preview shows whatever is lit now.
            Slider("Brightness", AppSettings.MinBrightness / 5, 20, AppSettings.Brightness / 5, v => v * 5 + "%", v =>
            {
                AppSettings.Brightness = v * 5;
                tray.ApplySettings();
                tray.Light.Preview(null, PreviewMs);
            });
            // On a call's flash, in steps of a tenth of a second per color; the preview flashes at the new speed.
            Slider("On a call: switch colors every", AppSettings.MinCallFlashMs / 100, AppSettings.MaxCallFlashMs / 100,
                AppSettings.CallFlashMs / 100, v => (v * 0.1).ToString("0.0") + (v == 10 ? " second" : " seconds"), v =>
            {
                AppSettings.CallFlashMs = v * 100;
                tray.ApplySettings();
                tray.Light.Preview(LightState.CallFlash, PreviewMs);
            });

            y += 8;
            Controls.Add(new Label { Text = "Colors", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), AutoSize = true, Location = new Point(16, y) });
            y += 30;
            foreach (var role in AppSettings.LightColorRoles)
            {
                string setting = role[0], previewState = role[3];
                Controls.Add(new Label { Text = role[1], AutoSize = true, Location = new Point(24, y + 3) });
                var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(width - 150, y, 130, 30) };
                picker.Items.AddRange(BlyncDevice.ColorNames);
                picker.SelectedItem = AppSettings.LightColorName(setting);
                picker.SelectedIndexChanged += delegate
                {
                    AppSettings.SetLightColorName(setting, (string)picker.SelectedItem);
                    tray.ApplySettings();
                    tray.Light.Preview(previewState, PreviewMs);
                };
                picker.Tag = role[2];
                colorPickers.Add(picker);
                Controls.Add(picker);
                y += 36;
            }

            y += 12;
            var reset = new ModernButton { Text = "Reset colors", Bounds = new Rectangle(20, y, 130, 34) };
            reset.Click += delegate { foreach (var picker in colorPickers) picker.SelectedItem = picker.Tag; };
            Controls.Add(reset);
            var lightControls = new List<Control>();
            for (int i = firstLightControl; i < Controls.Count; i++) lightControls.Add(Controls[i]);
            Action showEnabled = () => { foreach (var control in lightControls) control.Enabled = !disableAll.Checked; };
            disableAll.CheckedChanged += delegate
            {
                tray.SetLightsDisabled(disableAll.Checked);
                showEnabled();
            };
            showEnabled();
            // Close sits on the same line as Reset colors.
            int buttonsY = y;
            y += 50;

            // The mutesync button's ring, which isn't a USB light, so it's left on by Disable All Light Functions.
            // Reset colors puts these back too.
            Controls.Add(new Label { Text = "mutesync button ring", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), AutoSize = true, Location = new Point(16, y) });
            y += 30;
            Slider("Ring brightness", AppSettings.MinBrightness / 5, 20, AppSettings.MuteSyncBrightness / 5, v => v * 5 + "%", v =>
            {
                AppSettings.MuteSyncBrightness = v * 5;
                PreviewRing();
            });
            RingColorPicker("Muted", AppSettings.MuteSyncMutedColor, AppSettings.DefaultMuteSyncMutedColor, name => AppSettings.MuteSyncMutedColor = name);
            RingColorPicker("Live (not muted)", AppSettings.MuteSyncLiveColor, AppSettings.DefaultMuteSyncLiveColor, name => AppSettings.MuteSyncLiveColor = name);
            var onlyOnCalls = new ToggleBox
            {
                Text = "Only light the ring while an app is using my mic",
                Checked = AppSettings.MuteSyncOnlyOnCalls,
                AutoSize = true,
                Location = new Point(24, y + 4),
            };
            onlyOnCalls.CheckedChanged += delegate
            {
                AppSettings.MuteSyncOnlyOnCalls = onlyOnCalls.Checked;
                PreviewRing();
            };
            Controls.Add(onlyOnCalls);
            y += 44;
            Controls.Add(new Label
            {
                Text = "Pressing the button mutes and unmutes your mic in Windows. Close the MuteMe app so BusyLightz can use it.",
                AutoSize = true,
                MaximumSize = new Size(width - 48, 0),
                Font = new Font("Segoe UI", 9),
                Location = new Point(24, y),
                Tag = MutedText,
            });
            y += 48;

            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 120, buttonsY, 100, 34) };
            Controls.Add(close);
            CancelButton = close;
            ClientSize = new Size(width, y);
            // Scrolls on short screens rather than running off the bottom.
            int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 40 - (Height - ClientSize.Height);
            if (y > room)
            {
                AutoScroll = true;
                ClientSize = new Size(width + SystemInformation.VerticalScrollBarWidth, room);
            }
            Load += delegate
            {
                Theme.Apply(this);
                foreach (Control control in Controls)
                {
                    // Rounded buttons show the window behind their corners.
                    if (control is TrackBar || control is ModernButton) control.BackColor = BackColor;
                    if (MutedText.Equals(control.Tag)) control.ForeColor = Theme.Muted;
                }
            };
        }

        // Tags a label shown in the softer text color.
        const string MutedText = "muted";

        // Shows ring changes on the button for a few seconds, even if it's set to light only during calls.
        void PreviewRing()
        {
            tray.MuteButton.ApplySettings();
            tray.MuteButton.Preview(PreviewMs);
        }

        // A labelled color list for the mutesync ring; Reset colors puts it back to fallback.
        void RingColorPicker(string label, string current, string fallback, Action<string> save)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y + 3) });
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(width - 150, y, 130, 30) };
            picker.Items.AddRange(BlyncDevice.ColorNames);
            picker.SelectedItem = current;
            picker.SelectedIndexChanged += delegate
            {
                save((string)picker.SelectedItem);
                PreviewRing();
            };
            picker.Tag = fallback;
            colorPickers.Add(picker);
            Controls.Add(picker);
            y += 36;
        }

        // A labelled slider from min to max with the current value shown on the right. save runs as it moves.
        void Slider(string label, int min, int max, int current, Func<int, string> describe, Action<int> save)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y) });
            var value = new Label { Text = describe(current), AutoSize = false, TextAlign = ContentAlignment.TopRight, Bounds = new Rectangle(width - 120, y, 100, 22) };
            Controls.Add(value);
            var slider = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, current)),
                TickStyle = TickStyle.None,
                AutoSize = false,
                Bounds = new Rectangle(18, y + 24, width - 36, 30),
            };
            slider.ValueChanged += delegate
            {
                value.Text = describe(slider.Value);
                save(slider.Value);
            };
            Controls.Add(slider);
            y += 66;
        }
    }
}
