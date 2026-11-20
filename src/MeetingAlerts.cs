using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Threading;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // One event from the user's calendar.
    public class CalendarMeeting
    {
        public string Id, Subject, Location, Organizer, JoinUrl, Response;
        public DateTime StartUtc, EndUtc;
        public bool IsAllDay, IsCancelled;
    }

    // Meeting alerts: once a minute, reads the next few minutes of the user's calendar (through the Microsoft sign-in
    // that already shows their Teams status) and pops up a small window shortly before each meeting, with its details
    // and a Join button when it's an online meeting. Needs calendar access, which the user allows separately in
    // Settings > Meeting Alerts.
    public class MeetingAlerts : IDisposable
    {
        const int CheckMs = 60 * 1000;
        const int FirstCheckMs = 20 * 1000;
        const int SnoozeMinutes = 2;
        // A meeting that started longer ago than this isn't alerted about (say, the app was only just started).
        const int LateMinutes = 5;

        readonly Func<TeamsPresence> getPresence;
        readonly Control ui;
        readonly Func<bool> onCall;
        readonly System.Threading.Timer timer;
        readonly object sync = new object();
        // Meetings already alerted about (by calendar ID and start), and the ones snoozed until a time.
        readonly HashSet<string> alerted = new HashSet<string>();
        readonly Dictionary<string, DateTime> snoozed = new Dictionary<string, DateTime>();
        readonly List<MeetingAlertForm> open = new List<MeetingAlertForm>();
        string problem;
        int checking;

        // Why the calendar couldn't be read, for Settings to show; null when it's fine.
        public string Problem { get { lock (sync) return problem; } }

        // getPresence gives the current Teams sign-in (it can be replaced), ui is a window to run pop-ups on, and
        // onCall says whether a call is going on, when a sound would only be in the way.
        public MeetingAlerts(Func<TeamsPresence> getPresence, Control ui, Func<bool> onCall)
        {
            this.getPresence = getPresence;
            this.ui = ui;
            this.onCall = onCall;
            timer = new System.Threading.Timer(delegate { Program.Guard("Meeting alerts", Check); }, null, FirstCheckMs, CheckMs);
        }

        // Looks at the calendar now, in the background (after calendar access has just been allowed, say).
        public void CheckNow()
        {
            Program.Background("Meeting alerts", Check);
        }

        void Check()
        {
            if (Interlocked.Exchange(ref checking, 1) == 1) return;
            try { CheckCalendar(); }
            finally { Interlocked.Exchange(ref checking, 0); }
        }

        void CheckCalendar()
        {
            if (!AppSettings.MeetingAlertsOn) return;
            var presence = getPresence();
            if (presence == null || !presence.SignedIn || !presence.CalendarGranted) { SetProblem(null); return; }

            int lead = AppSettings.MeetingAlertMinutes;
            var now = DateTime.UtcNow;
            List<CalendarMeeting> meetings;
            try
            {
                meetings = presence.GetMeetings(now.AddMinutes(-LateMinutes), now.AddMinutes(lead + 2));
                SetProblem(null);
            }
            catch (Exception ex)
            {
                // Said in Settings; kept out of the balloons, since it would repeat every minute.
                SetProblem(ex.Message);
                Program.LogEvent("Couldn't read the calendar for meeting alerts: " + ex.Message);
                return;
            }

            foreach (var meeting in meetings)
            {
                if (meeting.IsCancelled || meeting.IsAllDay || meeting.Response == "declined") continue;
                if (now < meeting.StartUtc.AddMinutes(-lead)) continue;

                string key = meeting.Id + "|" + meeting.StartUtc.Ticks;
                bool wasSnoozed;
                lock (sync)
                {
                    DateTime until;
                    wasSnoozed = snoozed.TryGetValue(key, out until);
                    if (wasSnoozed)
                    {
                        if (now < until) continue;                    // Still snoozed.
                        if (now > meeting.EndUtc) { snoozed.Remove(key); continue; }
                    }
                    else
                    {
                        if (alerted.Contains(key)) continue;
                        if (now > meeting.StartUtc.AddMinutes(LateMinutes)) continue;
                    }
                    snoozed.Remove(key);
                    alerted.Add(key);
                }
                var shown = meeting;
                var shownKey = key;
                try { ui.BeginInvoke((Action)delegate { ShowAlert(shown, shownKey); }); }
                catch (InvalidOperationException) { } // Window already gone during exit.
            }
        }

        void SetProblem(string text)
        {
            lock (sync) problem = text;
        }

        // Shows an example alert, so the wording and sound can be tried out from Settings.
        public void ShowSample()
        {
            var now = DateTime.UtcNow;
            var sample = new CalendarMeeting
            {
                Id = "sample",
                Subject = "Weekly team meeting (example)",
                StartUtc = now.AddMinutes(AppSettings.MeetingAlertMinutes),
                EndUtc = now.AddMinutes(AppSettings.MeetingAlertMinutes + 30),
                Location = "Microsoft Teams Meeting",
                Organizer = "Your organizer",
                JoinUrl = "https://teams.microsoft.com",
            };
            try { ui.BeginInvoke((Action)delegate { ShowAlert(sample, null); }); }
            catch (InvalidOperationException) { }
        }

        // On the UI thread. key is null for the example alert, which can't be snoozed.
        void ShowAlert(CalendarMeeting meeting, string key)
        {
            var form = new MeetingAlertForm(meeting, Headline(meeting), SnoozeMinutes);
            form.Snoozed += delegate
            {
                if (key == null) return;
                lock (sync) snoozed[key] = DateTime.UtcNow.AddMinutes(SnoozeMinutes);
            };
            form.FormClosed += delegate
            {
                open.Remove(form);
                Restack();
            };
            open.Add(form);
            Restack();
            form.Show();
            // No sound during a call: it would play over the people the user is talking to.
            if (AppSettings.MeetingAlertSound && !onCall()) SystemSounds.Exclamation.Play();
        }

        // The alert's first line, from the user's wording: {when} is "in 5 minutes" or "now", {subject} the title.
        static string Headline(CalendarMeeting meeting)
        {
            int minutes = (int)Math.Ceiling((meeting.StartUtc - DateTime.UtcNow).TotalMinutes);
            string when = minutes <= 0 ? "now" : "in " + minutes + (minutes == 1 ? " minute" : " minutes");
            string text = AppSettings.MeetingAlertText;
            if (string.IsNullOrWhiteSpace(text)) text = AppSettings.DefaultMeetingAlertText;
            string subject = string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject;
            return text.Replace("{when}", when).Replace("{subject}", subject);
        }

        // Stacks the open alerts up from the bottom right corner of the screen, newest at the bottom.
        void Restack()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            for (int i = 0; i < open.Count; i++)
            {
                int fromBottom = open.Count - i;
                open[i].Location = new Point(
                    area.Right - MeetingAlertForm.FormWidth - MeetingAlertForm.Margin,
                    area.Bottom - MeetingAlertForm.Margin - fromBottom * MeetingAlertForm.FormHeight - (fromBottom - 1) * MeetingAlertForm.Gap);
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            try
            {
                ui.BeginInvoke((Action)delegate
                {
                    foreach (var form in open.ToArray()) form.Close();
                });
            }
            catch (InvalidOperationException) { }
        }
    }

    // The pop-up for one meeting: what it is, when, where, with Join (online meetings), Snooze and Dismiss. It doesn't
    // take focus from what the user is typing, and goes away by itself after a few minutes.
    class MeetingAlertForm : Form
    {
        public const int FormWidth = 380, FormHeight = 176, Gap = 10, Margin = 16;
        const int AutoCloseMs = 5 * 60 * 1000;
        const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        readonly System.Windows.Forms.Timer autoClose = new System.Windows.Forms.Timer { Interval = AutoCloseMs };

        public event Action Snoozed;

        public MeetingAlertForm(CalendarMeeting meeting, string headline, int snoozeMinutes)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(FormWidth, FormHeight);
            BackColor = Theme.Card;
            Font = new Font("Segoe UI", 10);

            // An icon tile on the left: a video camera for online meetings, a calendar for the rest. Left out if the
            // icon font isn't there.
            int textLeft = 16;
            if (Theme.Icons != null)
            {
                Controls.Add(new Label
                {
                    Text = string.IsNullOrEmpty(meeting.JoinUrl) ? "\uE787" : "\uE714",
                    Font = new Font(Theme.Icons.FontFamily, 20f),
                    ForeColor = Theme.TeamsText,
                    BackColor = Theme.TeamsTint,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Bounds = new Rectangle(16, 16, 48, 48),
                });
                textLeft = 76;
            }
            int textWidth = FormWidth - textLeft - 16;

            Controls.Add(new Label
            {
                Text = headline,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Theme.TeamsText,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(textLeft, 12, textWidth, 22),
            });
            Controls.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject,
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Theme.Text,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(textLeft, 36, textWidth, 30),
            });
            Controls.Add(new Label
            {
                Text = meeting.StartUtc.ToLocalTime().ToShortTimeString() + " - " + meeting.EndUtc.ToLocalTime().ToShortTimeString(),
                ForeColor = Theme.Text,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(textLeft, 68, textWidth, 22),
            });
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(meeting.Location)) details.Add(meeting.Location);
            if (!string.IsNullOrWhiteSpace(meeting.Organizer)) details.Add("Organizer: " + meeting.Organizer);
            Controls.Add(new Label
            {
                Text = string.Join("  |  ", details.ToArray()),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.Muted,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(textLeft, 92, textWidth, 20),
            });

            int x = 16;
            if (!string.IsNullOrEmpty(meeting.JoinUrl))
            {
                var join = new ModernButton { Text = "Join", Accent = true, Bounds = new Rectangle(x, 128, 104, 36) };
                join.Click += delegate
                {
                    OpenLink(meeting.JoinUrl);
                    Close();
                };
                Controls.Add(join);
                x += 104 + 8;
            }
            var snooze = new ModernButton { Text = "Snooze " + snoozeMinutes + " min", Bounds = new Rectangle(x, 128, 132, 36) };
            snooze.Click += delegate
            {
                var handler = Snoozed;
                if (handler != null) handler();
                Close();
            };
            Controls.Add(snooze);
            x += 132 + 8;
            var dismiss = new ModernButton { Text = "Dismiss", Bounds = new Rectangle(x, 128, 100, 36) };
            dismiss.Click += delegate { Close(); };
            Controls.Add(dismiss);

            autoClose.Tick += delegate { Close(); };
            Shown += delegate { autoClose.Start(); };
            FormClosed += delegate { autoClose.Dispose(); };
        }

        // Opens the meeting link in the browser, which hands it to Teams. Only web links.
        static void OpenLink(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;
            try { Process.Start(uri.AbsoluteUri); }
            catch (Exception ex) { Program.LogError("Opening the meeting link", ex); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Not activated and not in Alt+Tab, so it never takes the keyboard from what the user is typing.
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
