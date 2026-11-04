using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // Send feedback: an idea, a problem or anything else, as a new issue on the project's GitHub page. BusyLightz
    // fills the issue in and opens it in the browser, where the user signs in to GitHub and submits it. Issues are
    // public, so it only adds technical details (version, Windows, which devices are plugged in), never logs, names
    // or accounts, and only if the user leaves that ticked.
    class FeedbackForm : Form
    {
        const int width = 520;
        static readonly string[] Kinds = { "Idea", "Problem", "Other" };

        readonly TrayContext tray;
        readonly ComboBox kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox title = new TextBox { MaxLength = 120 };
        readonly TextBox details = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        readonly CheckBox includeDetails = new ToggleBox { Text = "Add my BusyLightz version, Windows version and connected devices", Checked = true, AutoSize = true };
        readonly Button send = new ModernButton { Text = "Continue on GitHub", Accent = true, Glyph = "" };

        public FeedbackForm(TrayContext tray)
        {
            this.tray = tray;
            Text = "Send feedback - " + Program.AppName;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            BackColor = Theme.Page;

            int y = 18;
            Controls.Add(new Label { Text = "Send feedback", Font = new Font("Segoe UI Semibold", 13f), AutoSize = true, Location = new Point(20, y) });
            y += 36;
            Controls.Add(new Label
            {
                Text = "Ideas, problems and questions all help. Your feedback becomes an issue on the project's GitHub page, " +
                       "which anyone can see. You'll need a free GitHub account to submit it.",
                AutoSize = true,
                MaximumSize = new Size(width - 40, 0),
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(20, y),
                Tag = "muted",
            });
            y += 72;

            Controls.Add(new Label { Text = "What kind of feedback?", AutoSize = true, Location = new Point(20, y + 4) });
            kind.Items.AddRange(Kinds);
            kind.SelectedIndex = 0;
            kind.Bounds = new Rectangle(width - 20 - 180, y, 180, 30);
            Controls.Add(kind);
            y += 46;

            Controls.Add(new Label { Text = "Title", AutoSize = true, Location = new Point(20, y) });
            y += 24;
            title.Bounds = new Rectangle(20, y, width - 40, 28);
            Controls.Add(title);
            y += 44;

            Controls.Add(new Label { Text = "Details", AutoSize = true, Location = new Point(20, y) });
            y += 24;
            details.Bounds = new Rectangle(20, y, width - 40, 150);
            Controls.Add(details);
            y += 166;

            includeDetails.Location = new Point(20, y);
            includeDetails.MaximumSize = new Size(width - 40, 0);
            Controls.Add(includeDetails);
            y += 46;

            var cancel = new ModernButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 20 - 100, y, 100, 36) };
            send.Bounds = new Rectangle(cancel.Left - 10 - 190, y, 190, 36);
            send.Click += delegate { Send(); };
            Controls.Add(send);
            Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(width, cancel.Bottom + 18);

            Action update = () => send.Enabled = title.Text.Trim().Length > 0;
            title.TextChanged += delegate { update(); };
            update();
            kind.SelectedIndexChanged += delegate { SetCue(); };
            SetCue();

            Load += delegate
            {
                Theme.Apply(this);
                foreach (Control control in Controls)
                {
                    if (control is ModernButton || control is Label || control is CheckBox) control.BackColor = BackColor;
                    if ("muted".Equals(control.Tag)) control.ForeColor = Theme.Muted;
                }
            };
            Shown += delegate { title.Focus(); };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        // A hint in the empty title box that fits the kind of feedback.
        void SetCue()
        {
            const int EM_SETCUEBANNER = 0x1501;
            string cue = kind.SelectedIndex == 0 ? "e.g. Let me pick a color for Away"
                : kind.SelectedIndex == 1 ? "e.g. The light stays red after my call ends"
                : "A short summary";
            if (title.IsHandleCreated) SendMessage(title.Handle, EM_SETCUEBANNER, IntPtr.Zero, cue);
            else title.HandleCreated += delegate { SendMessage(title.Handle, EM_SETCUEBANNER, IntPtr.Zero, cue); };
        }

        void Send()
        {
            string repo = Program.UpdateRepo;
            if (repo == null) return;
            string kindName = Kinds[kind.SelectedIndex];
            var body = new StringBuilder();
            body.AppendLine(details.Text.Trim().Length > 0 ? details.Text.Trim() : "(No details given.)");
            if (includeDetails.Checked)
            {
                body.AppendLine();
                body.AppendLine("---");
                body.AppendLine("- BusyLightz " + Program.VersionText + (AllUsersInstall.IsThisCopy ? " (installed for everyone)" : " (installed just for me)"));
                body.AppendLine("- " + WindowsVersion());
                body.AppendLine("- Lights: " + tray.Light.LightSummary);
                body.AppendLine("- mutesync button: " + tray.MuteButton.Status);
                body.AppendLine("- K2 key: " + (tray.K2Connected ? "plugged in" : "not plugged in"));
                body.AppendLine("- Window style: " + AppSettings.WindowStyle + (AppSettings.DarkMode ? ", Dark" : ", Light"));
            }
            body.AppendLine();
            body.AppendLine("_Sent from BusyLightz's Send feedback window._");

            string label = kindName == "Idea" ? "enhancement" : kindName == "Problem" ? "bug" : "question";
            string url = "https://github.com/" + repo + "/issues/new" +
                "?title=" + Uri.EscapeDataString("[" + kindName + "] " + title.Text.Trim()) +
                "&body=" + Uri.EscapeDataString(body.ToString()) +
                "&labels=" + Uri.EscapeDataString(label);
            try
            {
                Process.Start(url);
                Program.LogEvent("Opened a " + kindName.ToLowerInvariant() + " feedback issue on GitHub");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't open your browser: " + ex.Message, Program.AppName);
            }
        }

        // e.g. "Windows 11 (build 26200)".
        static string WindowsVersion()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string build = key == null ? null : key.GetValue("CurrentBuildNumber") as string;
                    int number;
                    if (build != null && int.TryParse(build, out number))
                        return (number >= 22000 ? "Windows 11" : "Windows 10") + " (build " + build + ")";
                }
            }
            catch (Exception) { }
            return Environment.OSVersion.VersionString;
        }
    }
}
