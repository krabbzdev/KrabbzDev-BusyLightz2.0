using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // All of the app's settings in one place, in the style of Windows 11 Settings: each section is a rounded card,
    // with on/off switches. Changes take effect as soon as a switch is flipped.
    class SettingsForm : Form
    {
        const int RefreshMs = 2000;
        const int width = 500;
        // Tags the version line, shown in the softer text color.
        const string VersionLabel = "version";
        // Section icons: Segoe MDL2 Assets settings gear, phone and people.
        const string GeneralGlyph = "", CallsGlyph = "", TeamsGlyph = "";

        readonly TrayContext tray;
        readonly CheckBox startWithWindows, openWindow;
        readonly Label teamsStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0) };
        readonly Button connectButton = new ModernButton();
        readonly Timer refresh = new Timer { Interval = RefreshMs };
        // The cards drawn behind each section, and where the open one started (-1 when none is open).
        readonly List<Rectangle> cards = new List<Rectangle>();
        int cardTop = -1;
        int y = 8;

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

            Heading("Microsoft Teams", TeamsGlyph);
            teamsStatus.Location = new Point(24, y + 6);
            Controls.Add(teamsStatus);
            connectButton.Bounds = new Rectangle(350, y, 130, 32);
            connectButton.Click += delegate
            {
                if (tray.Presence != null && tray.Presence.SignedIn) tray.DisconnectTeams();
                else if (tray.Presence != null) tray.ConnectTeams();
                else ShowTeamsAppDialog();
                RefreshTeams();
            };
            Controls.Add(connectButton);
            y += 44;
            var orgApp = new ModernButton { Text = "Organization's Teams app...", Bounds = new Rectangle(24, y, 220, 32) };
            orgApp.Click += delegate { ShowTeamsAppDialog(); };
            Controls.Add(orgApp);
            y += 44;
            Box("Match my Teams status when I pick Busy, Available or On a call", AppSettings.TeamsBusyWhenPicked,
                v => { AppSettings.TeamsBusyWhenPicked = v; tray.ApplySettings(); });
            var messageOn = Box("Set my Teams status message while my lights are red:",
                AppSettings.TeamsStatusMessageOn, v => { AppSettings.TeamsStatusMessageOn = v; tray.ApplySettings(); });
            var message = StatusMessagePicker(24 + 46);
            messageOn.CheckedChanged += delegate { message.Enabled = messageOn.Checked; };
            message.Enabled = messageOn.Checked;
            CloseCard();
            y += 6;

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
            coffee.Location = new Point(width - 12 - coffee.Width, y);
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
            var version = new Label
            {
                Text = "Version " + Program.VersionText + "  ·  Last updated " + date,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(crashLog.Right + 4, y, coffee.Left - crashLog.Right - 8, 20),
                Tag = VersionLabel,
            };
            Controls.Add(version);
            y += 32;

            var setup = new ModernButton { Text = "Setup checklist...", Bounds = new Rectangle(12, y, 150, 34) };
            setup.Click += delegate { tray.ShowSetup(); };
            var help = new ModernButton { Text = "Help", Bounds = new Rectangle(170, y, 80, 34) };
            help.Click += delegate { tray.OpenHelp(); };
            Controls.Add(help);
            // Brightness, call flash speed and colors, in their own window to keep this one short.
            // In the Party Mode colors, with a light bulb, so it stands out as the fun one.
            var lights = new ModernButton { Text = "Light Control", Rainbow = true, Glyph = "", Bounds = new Rectangle(258, y, 124, 34) };
            lights.Click += delegate
            {
                using (var dialog = new LightsDialog(tray)) dialog.ShowDialog(this);
            };
            Controls.Add(lights);
            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 112, y, 100, 34) };
            close.Click += delegate { Close(); };
            Controls.Add(setup);
            Controls.Add(close);
            CancelButton = close;
            FitToScreen(close.Bottom + 14);

            Load += delegate
            {
                Theme.Apply(this);
                ColorSurfaces();
            };
            refresh.Tick += delegate { RefreshTeams(); };
            Shown += delegate { RefreshTeams(); refresh.Start(); };
            FormClosed += delegate { refresh.Dispose(); };
        }

        // A just-for-me copy (in AppData) offers to install for everyone in Program Files, with a Windows admin
        // prompt. Once installed, this copy hands over to it (see TrayContext.WatchInstall).
        void InstallForEveryoneRow()
        {
            Controls.Add(new Label
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
            Controls.Add(install);
            y += 46;
        }

        void ShowTeamsAppDialog()
        {
            using (var dialog = new TeamsAppDialog(tray)) dialog.ShowDialog(this);
            RefreshTeams();
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
            y += cards.Count == 0 ? 4 : 12;
            int x = 16;
            if (Theme.Icons != null)
            {
                Controls.Add(new Label { Text = glyph, Font = new Font(Theme.Icons.FontFamily, 12f), AutoSize = true, Location = new Point(x, y + 4) });
                x += 26;
            }
            Controls.Add(new Label { Text = text, Font = new Font("Segoe UI Semibold", 11f), AutoSize = true, Location = new Point(x, y) });
            y += 32;
            cardTop = y;
            y += 12;
        }

        void CloseCard()
        {
            if (cardTop < 0) return;
            cards.Add(new Rectangle(12, cardTop, width - 24, y + 2 - cardTop));
            y += 4;
            cardTop = -1;
        }

        // An on/off switch row; indented ones line up under the text of the row above, which they depend on.
        CheckBox Box(string text, bool value, Action<bool> save, bool indented = false)
        {
            int x = indented ? 24 + 46 : 24;
            var box = new ToggleBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y), MaximumSize = new Size(width - 24 - x, 0) };
            box.CheckedChanged += delegate { save(box.Checked); };
            Controls.Add(box);
            y += Math.Max(32, box.Height + 10);
            return box;
        }

        // Puts the page and card colors behind the controls (Theme.Apply only colors them in Dark mode, and with
        // the page color), so each control blends into the card it sits on.
        void ColorSurfaces()
        {
            foreach (Control control in Controls)
            {
                if (Theme.KeepColors.Equals(control.Tag) || control is ComboBox || control is TextBox) continue;
                bool onCard = cards.Exists(c => c.Contains(control.Location));
                control.BackColor = onCard ? Theme.Card : Theme.Page;
                if (control is Label && !(control is LinkLabel))
                    control.ForeColor = VersionLabel.Equals(control.Tag) ? Theme.Muted : Theme.Text;
            }
            Invalidate();
        }

        // As tall as the content, or as tall as the screen allows with a scroll bar.
        void FitToScreen(int contentHeight)
        {
            int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80;
            if (contentHeight <= room) ClientSize = new Size(width, contentHeight);
            else
            {
                AutoScroll = true;
                ClientSize = new Size(width + SystemInformation.VerticalScrollBarWidth, room);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            foreach (var card in cards) Theme.FillRounded(g, card, 8, Theme.Card, Theme.Border);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
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
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y + 3) });
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(350, y, 130, 30) };
            picker.Items.AddRange(choices);
            picker.SelectedIndex = current;
            picker.SelectedIndexChanged += delegate
            {
                save(picker.SelectedIndex);
                tray.ApplyTheme();
            };
            Controls.Add(picker);
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
            Controls.Add(picker);
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
            Controls.Add(panel);
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
            Controls.Add(button);
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

            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 120, y, 100, 34) };
            Controls.Add(close);
            CancelButton = close;
            ClientSize = new Size(width, close.Bottom + 16);
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
