using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // "Test microphone": shows live what Windows reports about microphone use, which is all call detection sees.
    // Start a call in an app and watch for it to show In use now; if it never does, the cause is on the Windows side.
    class MicTestForm : Form
    {
        const int RefreshMs = 1000;
        const string MicSettingsUri = "ms-settings:privacy-microphone";

        readonly Label access = new Label { AutoSize = true, MaximumSize = new Size(400, 0) };
        readonly Button openSettings = new Button { Text = "Open Windows settings" };
        readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        readonly Label verdict = new Label { AutoSize = true, MaximumSize = new Size(580, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        readonly Timer refresh = new Timer { Interval = RefreshMs };
        readonly Font inUseFont = new Font("Segoe UI", 10, FontStyle.Bold);
        string shown;

        public MicTestForm()
        {
            Text = "Test microphone";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);
            const int width = 620;

            var intro = new Label
            {
                Text = "Start a call or recording in the app you want to test (Cytracom, Zoom, Loom and so on). " +
                       "Within a second it should show In use now. This list is what Windows reports, and it's all the app can see.",
                AutoSize = true,
                MaximumSize = new Size(width - 40, 0),
                Location = new Point(20, 16),
            };
            Controls.Add(intro);

            access.Location = new Point(20, intro.Bottom + 14);
            Controls.Add(access);
            openSettings.Bounds = new Rectangle(width - 210, intro.Bottom + 8, 190, 32);
            openSettings.Click += delegate { Process.Start(MicSettingsUri); };
            Controls.Add(openSettings);

            list.Bounds = new Rectangle(20, intro.Bottom + 52, width - 40, 230);
            list.Columns.Add("App", 130);
            list.Columns.Add("Microphone", 170);
            list.Columns.Add("Recorded as", list.Width - 130 - 170 - 24);
            Controls.Add(list);

            verdict.Location = new Point(20, list.Bottom + 12);
            Controls.Add(verdict);
            var tips = new Label
            {
                Text = "App not listed during a call? Check that it isn't running as administrator, and that the call isn't " +
                       "in a browser (it shows as Chrome or Edge) or in a remote desktop or Citrix session, where Windows records it on the other PC.",
                AutoSize = true,
                MaximumSize = new Size(width - 40, 0),
                Font = new Font("Segoe UI", 9),
                Location = new Point(20, list.Bottom + 66),
            };
            Controls.Add(tips);

            int buttonsY = tips.Bottom + 16;
            var ignore = new Button { Text = "Ignore apps...", Bounds = new Rectangle(20, buttonsY, 130, 32) };
            ignore.Click += delegate
            {
                using (var dialog = new IgnoreAppsDialog()) dialog.ShowDialog(this);
                shown = null;
                RefreshList();
            };
            var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(width - 116, buttonsY, 96, 32) };
            Controls.Add(ignore);
            Controls.Add(close);
            CancelButton = close;
            ClientSize = new Size(width, close.Bottom + 16);

            Load += delegate { Theme.Apply(this); tips.ForeColor = Theme.Muted; RefreshList(); };
            refresh.Tick += delegate { Program.Guard("Microphone test", RefreshList); };
            Shown += delegate { refresh.Start(); };
            FormClosed += delegate { refresh.Dispose(); };
        }

        void RefreshList()
        {
            var problems = CallDetector.MicAccessProblems();
            access.ForeColor = problems.Count == 0 ? Color.SeaGreen : Color.DarkOrange;
            access.Text = problems.Count == 0 ? "Windows lets apps use the microphone." : string.Join(".\r\n", problems.ToArray()) + ".";
            openSettings.Visible = problems.Count > 0;

            var apps = CallDetector.AppsUsingMic();
            var ignoredIds = CallDetector.IgnoredIds();
            var ignored = new HashSet<string>(ignoredIds, StringComparer.OrdinalIgnoreCase);
            var rows = new List<string[]>();
            foreach (var app in apps)
            {
                string use = app.InUse ? "In use now" : "Last used " + app.LastUsed.ToString("MMM d, h:mm tt");
                if (ignored.Contains(app.Id)) use += " (ignored)";
                rows.Add(new[] { app.Name, use, app.Id.Replace('#', '\\') });
            }

            // Only rebuild when something changed, so the list doesn't flicker or lose its scroll position.
            string key = string.Join("|", rows.ConvertAll(r => string.Join(";", r)).ToArray());
            if (key != shown)
            {
                shown = key;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var row in rows)
                {
                    var item = new ListViewItem(row);
                    if (row[1].StartsWith("In use now")) item.Font = inUseFont;
                    list.Items.Add(item);
                }
                list.EndUpdate();
            }

            var call = CallDetector.CallApp(apps, ignoredIds);
            bool anyInUse = apps.Exists(a => a.InUse);
            if (!AppSettings.CallsTurnRed)
                verdict.Text = "Turn lights red when my mic is in use is off in Settings, so calls won't change the lights.";
            else if (call != null)
                verdict.Text = call.Name + " is using the microphone, so the app sees a call right now.";
            else if (anyInUse)
                verdict.Text = "Only ignored apps are using the microphone, so this doesn't count as a call.";
            else
                verdict.Text = "No app is using the microphone right now. If you're on a call, Windows isn't reporting it.";
            verdict.ForeColor = call != null && AppSettings.CallsTurnRed ? Color.SeaGreen : Theme.Text;
        }
    }
}
