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
        const int width = 500, navWidth = 190;
        // Tags the version line, shown in the softer text color.
        const string VersionLabel = "version";
        // Page icons: Segoe MDL2 Assets settings gear, phone, people, microphone and keyboard.
        const string GeneralGlyph = "\uE713", CallsGlyph = "\uE717", TeamsGlyph = "\uE716", MuteSyncGlyph = "\uE720", K2Glyph = "\uE765", AboutGlyph = "\uE946", LightGlyph = "\uE82F";

        readonly TrayContext tray;
        readonly CheckBox startWithWindows, openWindow;
        readonly Label teamsStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0) };
        readonly Label muteButtonStatus = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f) };
        readonly Label k2Status = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f) };
        readonly Button connectButton = new ModernButton();
        readonly Timer refresh = new Timer { Interval = RefreshMs };
        readonly List<SettingsPage> pages = new List<SettingsPage>();
        readonly List<NavItem> navItems = new List<NavItem>();
        // Where the Light Control page is in the list, for the mutesync page's Ring colors button.
        int lightControlPage;
        // The color of the page being built, for its switches.
        Color pageAccent = Theme.Teams;
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

            StartPage("General", GeneralGlyph, PageColors.General, "How BusyLightz looks, starts and updates.");
            Heading(null, null);
            AppearancePickers();
            startWithWindows = Box("Start with Windows", AppSettings.StartWithWindows, v => AppSettings.StartWithWindows = v,
                help: "Starts BusyLightz when you sign in to Windows, so your lights work without you opening it.");
            openWindow = Box("Open the window at sign-in (off: start in the tray only)", AppSettings.OpenWindowAtSignIn,
                v => AppSettings.OpenWindowAtSignIn = v, true,
                "Shows the BusyLightz window when it starts. Off, it starts quietly as an icon in the tray.");
            startWithWindows.CheckedChanged += delegate { openWindow.Enabled = startWithWindows.Checked; };
            openWindow.Enabled = startWithWindows.Checked;
            // Red, Green and On A Call override Teams status until this runs out.
            var timesOut = Box("Go back to Follow Teams Status after",
                AppSettings.OverrideTimesOut, v => { AppSettings.OverrideTimesOut = v; tray.ApplySettings(); },
                help: "When you pick Busy, Available or On a call yourself, your lights go back to following your Teams status after this long. Off, they stay until you change them.");
            var minutes = OverrideMinutesPicker(timesOut);
            timesOut.CheckedChanged += delegate { minutes.Enabled = timesOut.Checked; };
            minutes.Enabled = timesOut.Checked;
            Box("Show a notification when the lights change", AppSettings.ShowNotifications, v => AppSettings.ShowNotifications = v,
                help: "Shows a small Windows notification each time your lights change color.");
            Box("Hotkeys: Alt+R for Busy, Alt+G for Available", AppSettings.HotkeysEnabled, v => { AppSettings.HotkeysEnabled = v; tray.ApplySettings(); },
                help: "Press Alt+R for Busy or Alt+G for Available from any app, without opening BusyLightz. Turn off if another app uses these keys.");
            var updates = Box("Install updates automatically", AppSettings.AutoUpdate, v => AppSettings.AutoUpdate = v,
                help: "Downloads and installs new versions of BusyLightz by itself. Check now looks for one right away.");
            if (Program.UpdateRepo != null) SideButton(updates, "Check now", delegate { tray.CheckForUpdates(true); });
            // Updates are automatic for the all-users copy (its background updater installs them).
            if (AllUsersInstall.IsThisCopy) updates.Enabled = false;
            else if (Program.UpdateRepo != null) InstallForEveryoneRow();
            // The project on GitHub: what it is, the code, release notes and the guide.
            if (Program.UpdateRepo != null)
            {
                Heading("About", AboutGlyph);
                var project = new ModernButton
                {
                    Text = "Learn more about this project here",
                    Accent = true,
                    Glyph = "", // open in new window
                    Bounds = new Rectangle(24, y, 300, 36),
                };
                project.Click += delegate { tray.OpenProjectPage(); };
                Add(project);
                y += 48;
            }
            EndPage();

            StartPage("Calls", CallsGlyph, PageColors.Calls, "What your lights do while any app is using your microphone.");
            Heading(null, null);
            var red = Box("Turn lights red when my mic is in use", AppSettings.CallsTurnRed,
                v => { AppSettings.CallsTurnRed = v; tray.ApplySettings(); },
                help: "When any app uses your microphone (Teams, Zoom, Cytracom and other softphones), your lights turn red until it stops. Ignore apps lists apps that shouldn't count.");
            SideButton(red, "Ignore apps...", delegate
            {
                using (var dialog = new IgnoreAppsDialog()) dialog.ShowDialog(this);
                tray.ApplySettings();
            });
            var flash = Box("Flash red and blue instead of steady red", AppSettings.FlashOnCalls,
                v => { AppSettings.FlashOnCalls = v; tray.ApplySettings(); }, true,
                "During calls your lights switch between red and blue instead of staying red. Change the colors and speed in Light Control. Test microphone checks that calls are detected.");
            SideButton(flash, "Test microphone...", delegate
            {
                using (var dialog = new MicTestForm()) dialog.ShowDialog(this);
                tray.ApplySettings();
            });
            red.CheckedChanged += delegate { flash.Enabled = red.Checked; };
            flash.Enabled = red.Checked;
            Box("Show me as Busy in Teams during calls in other apps", AppSettings.TeamsBusyOnCalls,
                v => { AppSettings.TeamsBusyOnCalls = v; tray.ApplySettings(); },
                help: "Sets your Teams status to Busy while you're on a call in another app, like Zoom or a softphone, and back afterwards. Needs BusyLightz connected to Teams.");
            EndPage();

            StartPage("Microsoft Teams", TeamsGlyph, PageColors.Teams, "Follow your Teams status, and keep Teams in step with your lights.");
            Heading(null, null);
            // The same setting as Disable Teams Integration in the tray menu.
            var teamsOff = DisableSwitch("Disable Teams Integration", AppSettings.TeamsDisabled,
                v => tray.SetTeamsDisabled(v),
                "BusyLightz stops reading your Teams status and setting Busy or a status message. Your lights and call detection keep working, and your sign-in is kept.");
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
            Info(orgApp, "Connect signs in with your work account so your lights follow your Teams status. " +
                "Your IT admin sets up your organization's Teams app once; this is where its details go.");
            y += 32 + RowGap;
            // Only the options below are greyed out while integration is off; Connect and the org app stay usable.
            int firstTeamsControl = page.Controls.Count;
            Box("Match my Teams status when I pick Busy, Available or On a call", AppSettings.TeamsBusyWhenPicked,
                v => { AppSettings.TeamsBusyWhenPicked = v; tray.ApplySettings(); },
                help: "When you pick Busy, Available or On a call in BusyLightz, your Teams status changes to match.");
            Box("Use Do Not Disturb instead of Busy", AppSettings.TeamsDoNotDisturb,
                v => { AppSettings.TeamsDoNotDisturb = v; tray.ApplySettings(); },
                help: "Shows Do Not Disturb in Teams, instead of Busy, when you pick Busy or On a call and during calls in other apps. Do Not Disturb also silences Teams notifications, except from your priority contacts.");
            var messageOn = Box("Set my Teams status message while my lights are red:",
                AppSettings.TeamsStatusMessageOn, v => { AppSettings.TeamsStatusMessageOn = v; tray.ApplySettings(); },
                help: "Shows a status message in Teams while your lights are red, so people know you're busy. It's cleared when your lights change back.");
            var message = StatusMessagePicker(24 + 46);
            messageOn.CheckedChanged += delegate { message.Enabled = messageOn.Checked; };
            message.Enabled = messageOn.Checked;
            GreyOutWhile(teamsOff, firstTeamsControl);
            EndPage();

            StartPage("Mutesync Button", MuteSyncGlyph, PageColors.MuteSync, "Mute and unmute your mic with one press, with a ring that shows which.");
            // Pressing a mutesync button mutes or unmutes the mic; its ring shows red when muted, green when live.
            Heading(null, null);
            DevicePicture(new MuteSyncPicture(), muteButtonStatus,
                "Its ring here shows what your button is showing right now.");
            var muteSyncOff = DisableSwitch("Disable Mutesync Button Functions", !AppSettings.MuteSyncEnabled,
                v => { AppSettings.MuteSyncEnabled = !v; tray.ApplySettings(); RefreshDevices(); },
                "BusyLightz lets go of the button: pressing it won't mute your mic, and its ring goes dark. Turn this on to use the MuteMe app instead.");
            int firstRingControl = page.Controls.Count;
            RingSection();
            GreyOutWhile(muteSyncOff, firstRingControl);
            EndPage();

            StartPage("K2 USBC Key", K2Glyph, PageColors.K2, "Two buttons for anything you like: type, mute, set your status.");
            K2Section();
            EndPage();

            // Brightness, call flash speed, colors and the mutesync ring: the Light Control window, shown in its page.
            lightControlPage = pages.Count;
            StartPage("Light Control", LightGlyph, PageColors.Light, "Brightness, call flashing and the color for each status.");
            var lights = new LightsDialog(tray, true) { Location = new Point(12, y - 8) };
            Add(lights);
            lights.Show();
            y = lights.Bottom + 8;
            EndPage();
            RefreshDevices();
            PlaceInfoIcons();

            // Below the pages, across the whole window.
            int bottom = 0;
            foreach (var p in pages) bottom = Math.Max(bottom, p.ContentHeight);
            bottom = Math.Max(bottom, 12 + navItems.Count * 44);
            int room = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80 - 92;
            // No taller than the General page needs; longer pages (Light Control) scroll.
            const int MaxPageHeight = 790;
            int pageHeight = Math.Min(Math.Min(bottom, MaxPageHeight), room);
            foreach (var p in pages)
            {
                p.Bounds = new Rectangle(navWidth, 0, width, pageHeight);
                // A page that scrolls loses room to its scroll bar; keep everything inside it so there's no sideways bar.
                if (p.ContentHeight <= pageHeight) continue;
                int right = width - SystemInformation.VerticalScrollBarWidth - 4;
                foreach (Control control in p.Controls)
                    if (control.Right > right) control.Width = Math.Max(10, right - control.Left);
            }
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

        readonly K2Picture k2Picture = new K2Picture();

        // A "Disable ... Functions" switch in bold, with what it does underneath; on means the device is left alone.
        CheckBox DisableSwitch(string text, bool value, Action<bool> save, string note)
        {
            var box = new ToggleBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10.5f),
                Location = new Point(24, y),
                Accent = Theme.Red,
            };
            box.CheckedChanged += delegate { save(box.Checked); };
            Add(box);
            y += 28;
            Add(new Label
            {
                Text = note,
                AutoSize = true,
                MaximumSize = new Size(width - 24 - 70 - 24, 0),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(24 + 46, y),
                Tag = VersionLabel,
            });
            y += 40 + RowGap;
            return box;
        }

        // Greys out the controls added to this page from index first on, while the switch is on.
        void GreyOutWhile(CheckBox off, int first)
        {
            var controls = new List<Control>();
            for (int i = first; i < page.Controls.Count; i++) controls.Add(page.Controls[i]);
            Action apply = () => { foreach (var control in controls) control.Enabled = !off.Checked; };
            off.CheckedChanged += delegate { apply(); };
            apply();
        }

        // A device's picture on the left, with its status and a short note beside it.
        void DevicePicture(Control picture, Label status, string note)
        {
            const int Size = 116;
            picture.Bounds = new Rectangle(24, y, Size, Size);
            Add(picture);
            status.Font = new Font("Segoe UI Semibold", 10.5f);
            status.Location = new Point(24 + Size + 20, y + 30);
            Add(status);
            Add(new Label
            {
                Text = note,
                AutoSize = true,
                MaximumSize = new Size(width - 24 - Size - 20 - 40, 0),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(24 + Size + 20, y + 58),
                Tag = VersionLabel,
            });
            y += Size + RowGap;
        }

        // The mutesync button's ring: brightness, its muted and live colors, and whether it's lit only during calls.
        // Each change shows on the button for a few seconds.
        void RingSection()
        {
            const int PreviewMs = 4000;
            Action preview = () =>
            {
                tray.MuteButton.ApplySettings();
                tray.MuteButton.Preview(PreviewMs);
            };
            Heading("Ring", MuteSyncGlyph);

            var name = new Label { Text = "Ring brightness", AutoSize = true, Location = new Point(24, y) };
            Add(name);
            Info(name, "How bright the button's ring glows. 25% matches the MuteMe app.");
            var value = new Label { Text = AppSettings.MuteSyncBrightness + "%", AutoSize = false, TextAlign = ContentAlignment.TopRight, Bounds = new Rectangle(width - 120, y, 96, 22) };
            Add(value);
            var slider = new ModernSlider
            {
                Minimum = AppSettings.MinBrightness / 5,
                Maximum = 20,
                Accent = PageColors.MuteSync,
                Bounds = new Rectangle(18, y + 24, width - 48, 30),
            };
            slider.Value = Math.Max(AppSettings.MinBrightness / 5, Math.Min(20, AppSettings.MuteSyncBrightness / 5));
            slider.ValueChanged += delegate
            {
                value.Text = slider.Value * 5 + "%";
                AppSettings.MuteSyncBrightness = slider.Value * 5;
                preview();
            };
            Add(slider);
            y += 56 + RowGap;

            RingColor("Muted", AppSettings.MuteSyncMutedColor, name2 => AppSettings.MuteSyncMutedColor = name2, preview,
                "The ring's color while your mic is muted.");
            RingColor("Live (not muted)", AppSettings.MuteSyncLiveColor, name2 => AppSettings.MuteSyncLiveColor = name2, preview,
                "The ring's color while your mic is on.");
            Box("Only light the ring while an app is using my mic", AppSettings.MuteSyncOnlyOnCalls,
                v => { AppSettings.MuteSyncOnlyOnCalls = v; preview(); },
                help: "Keeps the ring dark except during calls (while any app is using your microphone), so it isn't glowing all day.");
        }

        void RingColor(string label, string current, Action<string> save, Action preview, string help)
        {
            var name = new Label { Text = label, AutoSize = true, Location = new Point(24, y + 6) };
            Add(name);
            Info(name, help);
            var picker = new ColorSwatches(BlyncDevice.ColorNames) { Location = new Point(width - 24 - BlyncDevice.ColorNames.Length * ColorSwatches.Spacing, y) };
            picker.Selected = current;
            picker.SelectedChanged += delegate
            {
                save(picker.Selected);
                preview();
            };
            Add(picker);
            y += picker.Height + RowGap;
        }

        // The K2 macro key: an action for each button and for both together, and the email address it can type.
        void K2Section()
        {
            Heading(null, null);
            DevicePicture(k2Picture, k2Status, "Press a button on your K2 and it lights up here.");
            tray.K2Pressed += k2Picture.Flash;
            FormClosed += delegate { tray.K2Pressed -= k2Picture.Flash; };
            var k2Off = DisableSwitch("Disable K2 Functions", AppSettings.K2Disabled,
                v => { AppSettings.K2Disabled = v; tray.ApplySettings(); },
                "BusyLightz leaves the K2 alone: no actions run, and its buttons are ordinary left and right mouse clicks again.");
            int firstK2Control = page.Controls.Count;
            K2Row("Left button", K2Key.Button.Left, "What happens when you press the K2 USBC Key's left button on its own.");
            K2Row("Right button", K2Key.Button.Right, "What happens when you press the K2 USBC Key's right button on its own.");
            K2Row("Both together", K2Key.Button.Both, "What happens when you press both buttons at the same time. It counts once, so it doesn't also run Left or Right.");
            y += 4;
            var emailName = new Label { Text = "My email address", AutoSize = true, Location = new Point(24, y + 4) };
            Add(emailName);
            Info(emailName, "The address the \"Type my email address\" action types. The setup checklist asks for it too.");
            var email = new TextBox { Bounds = new Rectangle(190, y, 290, 28), Text = AppSettings.MacroEmail };
            email.TextChanged += delegate { AppSettings.MacroEmail = email.Text; };
            Add(email);
            y += 28 + RowGap + 4;
            GreyOutWhile(k2Off, firstK2Control);
            Add(new Label
            {
                Text = "No K2 software needed. Out of the box the K2 USBC Key's buttons are mouse clicks; BusyLightz cancels those clicks " +
                       "so a press only runs your action. Your real mouse isn't affected.",
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
        void K2Row(string label, K2Key.Button button, string help)
        {
            var name = new Label { Text = label, AutoSize = true, Location = new Point(24, y + 4) };
            Add(name);
            Info(name, help);
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(150, y, 192, 30) };
            foreach (var action in K2Actions.All) picker.Items.Add(action[1]);
            string current = AppSettings.K2Action(button);
            for (int i = 0; i < K2Actions.All.Length; i++) if (K2Actions.All[i][0] == current) picker.SelectedIndex = i;
            var value = new TextBox { Bounds = new Rectangle(350, y + 1, 130, 28), Text = AppSettings.K2Value(button) };
            Action showValue = () =>
            {
                string action = K2Actions.All[picker.SelectedIndex][0];
                // Read-only (not disabled) when the action needs no text, so Disable K2 Functions can grey the row and
                // turning it off again leaves this as the action wants it.
                bool needs = K2Actions.NeedsValue(action);
                value.ReadOnly = !needs;
                value.TabStop = needs;
                value.ForeColor = needs ? Theme.Text : Theme.Muted;
                // A hint in the empty box for what goes there; the email action shows the address it types.
                value.Text = needs ? AppSettings.K2Value(button) : action == "TypeEmail" ? AppSettings.MacroEmail : "";
                SetCue(value, K2Actions.Hint(action));
            };
            picker.SelectedIndexChanged += delegate
            {
                AppSettings.SetK2Action(button, K2Actions.All[picker.SelectedIndex][0]);
                showValue();
            };
            value.TextChanged += delegate { if (!value.ReadOnly) AppSettings.SetK2Value(button, value.Text); };
            showValue();
            Add(picker);
            Add(value);
            y += 30 + RowGap;
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
            // Ideas and problems, as an issue on the project's GitHub page.
            if (Program.UpdateRepo != null)
            {
                var feedback = new ModernButton { Text = "Send feedback...", Glyph = "", Bounds = new Rectangle(258, y, 160, 34) };
                feedback.Click += delegate { tray.ShowFeedback(); };
                Controls.Add(feedback);
            }
            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(full - 112, y, 100, 34) };
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
            ClientSize = new Size(full, close.Bottom + 14);
        }

        // Starts a page and its entry in the list on the left; the helpers below add to it until EndPage.
        // color is the page's own color, for its icon in the list and its header (Color.Empty for the rainbow one);
        // summary is the header's one line about the page.
        void StartPage(string title, string glyph, Color color, string summary)
        {
            page = new SettingsPage { Visible = false };
            pageAccent = color;
            pages.Add(page);
            Controls.Add(page);
            int index = navItems.Count;
            var item = new NavItem { Text = title, Glyph = glyph, Accent = color, Bounds = new Rectangle(8, 12 + index * 46, navWidth - 16, 40) };
            item.Click += delegate { ShowPage(index); };
            navItems.Add(item);
            Controls.Add(item);
            y = 8;
            cardTop = -1;
            var header = new PageHeader { Title = title, Glyph = glyph, Accent = color, Summary = summary, Bounds = new Rectangle(12, y, width - 24, 86) };
            Add(header);
            y = header.Bottom + 6;
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
            muteButtonStatus.Text = tray.MuteButton.Status;
            k2Status.Text = tray.K2Connected ? "Connected" : "Not plugged in";
            if (k2Picture.Connected != tray.K2Connected) { k2Picture.Connected = tray.K2Connected; k2Picture.Invalidate(); }
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

        // A section title with its icon, starting a new card for the rows below it. With no text, just starts a card
        // (the page header names the first one).
        void Heading(string text, string glyph)
        {
            CloseCard();
            y += page.Cards.Count == 0 ? 4 : 12;
            if (text == null)
            {
                y += 4;
                cardTop = y;
                y += 18;
                return;
            }
            int x = 16;
            if (Theme.Icons != null)
            {
                Add(new Label { Text = glyph, Font = new Font(Theme.Icons.FontFamily, 12f), AutoSize = true, Location = new Point(x, y + 4) });
                x += 26;
            }
            Add(new Label { Text = text, Font = new Font("Segoe UI Semibold", 11f), AutoSize = true, Location = new Point(x, y) });
            y += 34;
            cardTop = y;
            y += 18;
        }

        void CloseCard()
        {
            if (cardTop < 0) return;
            y += 6;
            page.Cards.Add(new Rectangle(12, cardTop, width - 24, y - cardTop));
            y += 6;
            cardTop = -1;
        }

        // Room kept beside a row's text for its "i".
        const int InfoRoom = 28;
        // Gap between rows.
        const int RowGap = 16;

        // An on/off switch row; indented ones line up under the text of the row above, which they depend on. help is
        // what its "i" says.
        CheckBox Box(string text, bool value, Action<bool> save, bool indented = false, string help = null)
        {
            int x = indented ? 24 + 46 : 24;
            var box = new ToggleBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y), MaximumSize = new Size(width - 24 - x - InfoRoom, 0), Accent = PageColors.Solid(pageAccent) };
            box.CheckedChanged += delegate { save(box.Checked); };
            Add(box);
            y += Math.Max(32, box.Height) + RowGap;
            if (help != null) Info(box, help);
            return box;
        }

        // Explanations for the "i" beside each setting: shown on hover, or on click.
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 150, ReshowDelay = 100 };
        // Each "i" and the control it follows; placed once every row has its final width (see PlaceInfoIcons).
        readonly List<KeyValuePair<InfoIcon, Control>> infoIcons = new List<KeyValuePair<InfoIcon, Control>>();

        // A small "i" just after a control's text, explaining the setting.
        void Info(Control anchor, string help)
        {
            var icon = new InfoIcon { Size = new Size(16, 16) };
            tips.SetToolTip(icon, help);
            icon.Click += delegate { tips.Show(help, icon, 20, 0, 12000); };
            Add(icon);
            infoIcons.Add(new KeyValuePair<InfoIcon, Control>(icon, anchor));
        }

        void PlaceInfoIcons()
        {
            foreach (var pair in infoIcons)
            {
                var anchor = pair.Value;
                // Level with the first line of text (a switch's text sits a little lower than its top).
                int top = anchor is CheckBox ? anchor.Top + 4 : anchor.Top + (Math.Min(anchor.Height, 24) - 16) / 2;
                pair.Key.Location = new Point(anchor.Right + 6, top);
            }
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
                index => AppSettings.WindowStyle = WindowStyles.All[index],
                "How the main window looks: Orbit (a big status ring), Color block or Fluent (a list, like Windows Settings).");
            Picker("Appearance", new[] { "Light", "Dark" }, AppSettings.DarkMode ? 1 : 0, index => AppSettings.DarkMode = index == 1,
                "Light or Dark colors for BusyLightz's windows.");
        }

        void Picker(string label, string[] choices, int current, Action<int> save, string help)
        {
            var name = new Label { Text = label, AutoSize = true, Location = new Point(24, y + 4) };
            Add(name);
            Info(name, help);
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(350, y, 130, 30) };
            picker.Items.AddRange(choices);
            picker.SelectedIndex = current;
            picker.SelectedIndexChanged += delegate
            {
                save(picker.SelectedIndex);
                tray.ApplyTheme();
            };
            Add(picker);
            y += 30 + RowGap;
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
            row.MaximumSize = new Size(maxWidth - InfoRoom, 0);
            row.Size = row.GetPreferredSize(Size.Empty);
            y = Math.Max(y, row.Top + Math.Max(32, row.Height) + RowGap);
        }
    }

    // A large drawing of the Mutesync Button whose ring shows the color the real one is showing right now (grey when
    // it isn't connected). Redraws when the ring changes.
    class MuteSyncPicture : Control
    {
        readonly Timer watch = new Timer { Interval = 300 };
        Color? shown;

        public MuteSyncPicture()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            watch.Tick += delegate
            {
                var ring = MuteSyncButton.Ring;
                if (ring != shown) { shown = ring; Invalidate(); }
            };
            watch.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            shown = MuteSyncButton.Ring;
            DeviceTiles.DrawMuteSyncLarge(e.Graphics, ClientRectangle, shown);
        }
    }

    // A Blynclight and a Luxafor side by side, glowing in the color the real lights are showing now (including
    // previews and call flashing). Redraws when it changes.
    class LightsPicture : Control
    {
        readonly Func<Color> color;
        readonly Timer watch = new Timer { Interval = 150 };
        Color shown;

        public LightsPicture(Func<Color> color)
        {
            this.color = color;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            watch.Tick += delegate
            {
                var now = color();
                if (now.ToArgb() != shown.ToArgb()) { shown = now; Invalidate(); }
            };
            watch.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            shown = color();
            DeviceTiles.DrawLightsLarge(e.Graphics, ClientRectangle, shown);
        }
    }

    // A large drawing of the K2 USBC Key; a key lights up for a moment when it's pressed on the real one.
    class K2Picture : Control
    {
        readonly Timer fade = new Timer { Interval = 700 };
        bool left, right;
        public bool Connected;

        public K2Picture()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            fade.Tick += delegate { fade.Stop(); left = right = false; Invalidate(); };
        }

        public void Flash(K2Key.Button button)
        {
            left = button != K2Key.Button.Right;
            right = button != K2Key.Button.Left;
            Invalidate();
            fade.Stop();
            fade.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) fade.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            DeviceTiles.DrawK2Large(e.Graphics, ClientRectangle, Connected, left, right);
        }
    }

    // A small round "i" beside a setting; hovering or clicking shows what the setting does.
    class InfoIcon : Control
    {
        static readonly Font Letter = new Font("Segoe UI Semibold", 7.5f);
        bool hover;

        public InfoIcon()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Cursor = Cursors.Help;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = hover ? Theme.Teams : Theme.Muted;
            var circle = new Rectangle(1, 1, Width - 3, Height - 3);
            if (hover) using (var fill = new SolidBrush(Theme.Wash(Theme.Teams, 0.85))) g.FillEllipse(fill, circle);
            using (var pen = new Pen(color, 1.3f)) g.DrawEllipse(pen, circle);
            TextRenderer.DrawText(g, "i", Letter, new Rectangle(0, 0, Width, Height - 1), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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

    // Each Settings page's own color, for its icon tile in the page list and its header.
    static class PageColors
    {
        public static readonly Color General = Color.FromArgb(96, 110, 140);
        public static readonly Color Calls = Color.FromArgb(229, 72, 77);
        public static readonly Color Teams = Theme.Teams;
        public static readonly Color MuteSync = Color.FromArgb(48, 164, 108);
        public static readonly Color K2 = Color.FromArgb(247, 107, 21);
        // Light Control's tile is a rainbow.
        public static readonly Color Light = Color.Empty;
        static readonly Color[] Rainbow = { Theme.Red, Color.FromArgb(247, 99, 12), Color.FromArgb(234, 179, 8), Theme.Green, Theme.CallBlue, Color.FromArgb(136, 23, 152) };

        // One color to stand for the page (the rainbow's is purple).
        public static Color Solid(Color accent) { return accent.IsEmpty ? Color.FromArgb(136, 23, 152) : accent; }

        // A rounded tile in the page's color: a soft top-to-bottom shade, or the rainbow.
        public static void FillTile(Graphics g, Rectangle r, Color accent, int radius)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(r, radius))
            {
                if (accent.IsEmpty)
                {
                    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, Rainbow[0], Rainbow[Rainbow.Length - 1], 45f))
                    {
                        var blend = new System.Drawing.Drawing2D.ColorBlend { Colors = Rainbow, Positions = new float[Rainbow.Length] };
                        for (int i = 0; i < Rainbow.Length; i++) blend.Positions[i] = i / (float)(Rainbow.Length - 1);
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, path);
                    }
                }
                else
                    using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, Theme.Mix(accent, Color.White, 0.18), Theme.Mix(accent, Color.Black, 0.12), 90f))
                        g.FillPath(brush, path);
            }
        }
    }

    // The top of each Settings page: a big icon tile in the page's color, its name, and a line about what's on it,
    // on a card washed with the page's color.
    class PageHeader : Control
    {
        public string Title, Glyph, Summary;
        public Color Accent;
        static readonly Font TitleFont = new Font("Segoe UI Semibold", 15f);
        static readonly Font SummaryFont = new Font("Segoe UI", 9.5f);

        public PageHeader()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Page);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            var accent = PageColors.Solid(Accent);
            using (var path = Theme.Rounded(r, 10))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, Theme.Wash(accent, 0.72), Theme.Card, 0f))
                g.FillPath(brush, path);
            using (var path = Theme.Rounded(r, 10))
            using (var pen = new Pen(Theme.Wash(accent, 0.6))) g.DrawPath(pen, path);
            var tile = new Rectangle(14, (Height - 48) / 2, 48, 48);
            PageColors.FillTile(g, tile, Accent, 12);
            if (Theme.Icons != null)
                using (var icon = new Font(Theme.Icons.FontFamily, 18f))
                    TextRenderer.DrawText(g, Glyph, icon, tile, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = tile.Right + 16;
            TextRenderer.DrawText(g, Title, TitleFont, new Point(x, 12), Theme.Text);
            TextRenderer.DrawText(g, Summary, SummaryFont, new Rectangle(x, 40, Width - x - 12, 40), Theme.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // An entry in Settings' page list: its icon on a tile in the page's color, and its name; tinted, with a bar in the
    // page's color, while its page is showing.
    class NavItem : Control
    {
        public string Glyph;
        // The page's color (Color.Empty: rainbow).
        public Color Accent;
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
            var accent = PageColors.Solid(Accent);
            if (selected) Theme.FillRounded(g, r, 8, Theme.Wash(accent, 0.82), Theme.Wash(accent, 0.7));
            else if (hover) Theme.FillRounded(g, r, 8, Theme.Card, Theme.Card);
            if (selected)
                using (var bar = new SolidBrush(accent)) g.FillRectangle(bar, 0, Height / 2 - 9, 3, 18);
            // The page's icon, white on a rounded tile in the page's color.
            var tile = new Rectangle(10, (Height - 26) / 2, 26, 26);
            PageColors.FillTile(g, tile, Accent, 7);
            if (Theme.Icons != null)
                using (var icon = new Font(Theme.Icons.FontFamily, 10f))
                    TextRenderer.DrawText(g, Glyph, icon, tile, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = tile.Right + 10;
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
        readonly List<ColorSwatches> colorPickers = new List<ColorSwatches>();

        // A short name for each status color row, so its color dots fit beside it.
        static string ShortColorName(string setting, string full)
        {
            switch (setting)
            {
                case "BusyColor": return "Busy";
                case "AvailableColor": return "Available";
                case "AwayColor": return "Away";
                case "DndColor": return "Do not disturb";
                case "CallColor1": return "On a call: 1st";
                case "CallColor2": return "On a call: 2nd";
                default: return full;
            }
        }
        int y = 16;

        // embedded: shown inside Settings as its Light Control page, with no border or Close button.
        public LightsDialog(TrayContext tray, bool embedded = false)
        {
            this.tray = tray;
            Text = "Light Control";
            FormBorderStyle = embedded ? FormBorderStyle.None : FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);
            if (embedded)
            {
                TopLevel = false;
                BackColor = Theme.Page;
            }

            // Your lights, glowing in the color the real ones are showing, with a note beside them.
            var lights = new LightsPicture(() => tray.DisplayColor) { Bounds = new Rectangle(20, y, 150, 96) };
            Controls.Add(lights);
            Controls.Add(new Label
            {
                Text = "Changes show on your lights for a few seconds as you make them, and on the lights here too.",
                AutoSize = true,
                MaximumSize = new Size(width - lights.Right - 36, 0),
                Location = new Point(lights.Right + 16, y + 26),
            });
            y += lights.Height + 18;

            // The same setting as Disable USB Light Integration in the tray menu. While on, the rest of this window
            // has nothing to show, so it's greyed out.
            var disableAll = new ToggleBox
            {
                Text = "Disable All Light Functions",
                Accent = Theme.Red,
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
            // A row of color dots for each status; the full name (e.g. "Busy (and Teams: Busy)") is its tooltip.
            var names = new ToolTip();
            foreach (var role in AppSettings.LightColorRoles)
            {
                string setting = role[0], previewState = role[3];
                var name = new Label { Text = ShortColorName(setting, role[1]), AutoSize = true, Location = new Point(24, y + 6) };
                names.SetToolTip(name, role[1]);
                Controls.Add(name);
                var picker = new ColorSwatches(BlyncDevice.ColorNames) { Location = new Point(width - 20 - BlyncDevice.ColorNames.Length * ColorSwatches.Spacing, y) };
                picker.Selected = AppSettings.LightColorName(setting);
                picker.SelectedChanged += delegate
                {
                    AppSettings.SetLightColorName(setting, picker.Selected);
                    tray.ApplySettings();
                    tray.Light.Preview(previewState, PreviewMs);
                };
                picker.Tag = role[2];
                colorPickers.Add(picker);
                Controls.Add(picker);
                y += 40;
            }

            y += 12;
            var reset = new ModernButton { Text = "Reset colors", Bounds = new Rectangle(20, y, 130, 34) };
            reset.Click += delegate { foreach (var picker in colorPickers) picker.Selected = (string)picker.Tag; };
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

            ClientSize = new Size(width, y);
            if (embedded)
            {
                // The Settings page scrolls instead.
                Load += delegate { ApplyColors(); };
                return;
            }
            var close = new ModernButton { Text = "Close", Accent = true, DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 120, buttonsY, 100, 34) };
            Controls.Add(close);
            CancelButton = close;
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
                ApplyColors();
            };
        }

        void ApplyColors()
        {
            if (TopLevel == false && AppSettings.DarkMode) foreach (Control control in Controls) control.ForeColor = Theme.Text;
            foreach (Control control in Controls)
            {
                // Rounded buttons show the window behind their corners.
                if (control is LightsPicture || control is ModernSlider || control is ColorSwatches || control is ModernButton || control is Label || control is CheckBox) control.BackColor = BackColor;
                if (MutedText.Equals(control.Tag)) control.ForeColor = Theme.Muted;
            }
        }

        // Tags a label shown in the softer text color.
        const string MutedText = "muted";

        // A labelled slider from min to max with the current value shown on the right. save runs as it moves.
        void Slider(string label, int min, int max, int current, Func<int, string> describe, Action<int> save)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(24, y) });
            var value = new Label { Text = describe(current), AutoSize = false, TextAlign = ContentAlignment.TopRight, Bounds = new Rectangle(width - 120, y, 100, 22) };
            Controls.Add(value);
            var slider = new ModernSlider
            {
                Minimum = min,
                Maximum = max,
                Accent = PageColors.Solid(PageColors.Light),
                Bounds = new Rectangle(18, y + 24, width - 36, 30),
            };
            slider.Value = Math.Max(min, Math.Min(max, current));
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
