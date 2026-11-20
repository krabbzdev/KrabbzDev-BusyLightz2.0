using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            var form = new MeetingAlertForm(meeting, delegate { return Headline(meeting); }, SnoozeMinutes);
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

        // The alert's first line, worked out afresh every second so it counts down like a stopwatch. From the user's
        // wording: {when} is "in 4:32" (or "now"), {subject} the title. Once the meeting has started it counts up
        // instead: "Meeting started 0:42 ago".
        static string Headline(CalendarMeeting meeting)
        {
            double seconds = Math.Ceiling((meeting.StartUtc - DateTime.UtcNow).TotalSeconds);
            if (seconds <= -1) return "Meeting started " + Clock((int)-seconds) + " ago";
            string when = seconds <= 0 ? "now" : "in " + Clock((int)seconds);
            string text = AppSettings.MeetingAlertText;
            if (string.IsNullOrWhiteSpace(text)) text = AppSettings.DefaultMeetingAlertText;
            string subject = string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject;
            return text.Replace("{when}", when).Replace("{subject}", subject);
        }

        // Minutes and seconds like a stopwatch: 4:32, 0:09, or 1:02:03 past an hour.
        static string Clock(int totalSeconds)
        {
            int hours = totalSeconds / 3600, minutes = totalSeconds / 60 % 60, seconds = totalSeconds % 60;
            if (hours > 0) return hours + ":" + minutes.ToString("00") + ":" + seconds.ToString("00");
            return minutes + ":" + seconds.ToString("00");
        }

        // Stacks the open alerts up from the bottom right corner of the screen, newest at the bottom.
        void Restack()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            for (int i = 0; i < open.Count; i++)
            {
                int fromBottom = open.Count - i;
                open[i].Location = new Point(
                    area.Right - open[i].Width - MeetingAlertForm.Margin,
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

    // The picture shown on meeting alerts: the one that comes with the app, or one the user picked (Settings > Meeting
    // Alerts). A scaled-down copy of their own is kept in the app's data folder, so moving or deleting the original doesn't matter, and nothing is sent anywhere.
    public static class AlertPicture
    {
        const int MaxSide = 400;

        static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight", "alert-picture.png");
            }
        }

        // Whether the user has picked a picture of their own (rather than the one that comes with the app).
        public static bool HasCustom { get { return System.IO.File.Exists(FilePath); } }

        // The picture to show: the user's own, or else the one built into the app. Null if they chose the plain icon
        // instead, or it can't be read. The caller disposes it.
        public static Bitmap Load()
        {
            try
            {
                if (AppSettings.AlertPictureOff) return null;
                if (HasCustom)
                {
                    using (var stream = new System.IO.FileStream(FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    using (var image = Image.FromStream(stream)) return new Bitmap(image);
                }
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("alert-default.png"))
                {
                    if (stream == null) return null;
                    using (var image = Image.FromStream(stream)) return new Bitmap(image);
                }
            }
            catch (Exception ex)
            {
                Program.LogError("Reading the meeting alert picture", ex);
                return null;
            }
        }

        // Saves a copy of the chosen picture, no bigger than 400 pixels a side. Throws if it isn't a picture Windows can open.
        public static void Set(string file)
        {
            using (var source = Image.FromFile(file))
            {
                double scale = Math.Min(1.0, (double)MaxSide / Math.Max(source.Width, source.Height));
                int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
                using (var copy = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(copy))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, w, h);
                    }
                    string dir = System.IO.Path.GetDirectoryName(FilePath);
                    System.IO.Directory.CreateDirectory(dir);
                    copy.Save(FilePath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        public static void Clear()
        {
            try { if (HasCustom) System.IO.File.Delete(FilePath); }
            catch (Exception ex) { Program.LogError("Removing the meeting alert picture", ex); }
        }
    }

    // The picture beside an alert: fills its box, cropped from the middle if the shape doesn't match.
    class PictureTile : Control
    {
        readonly Bitmap image;

        public PictureTile(Bitmap image)
        {
            this.image = image;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            e.Graphics.DrawImage(image, (Width - w) / 2f, (Height - h) / 2f, w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image.Dispose();
            base.Dispose(disposing);
        }
    }

    // The pop-up for one meeting: what it is, when, where, with Join (online meetings), Snooze and Dismiss. It doesn't
    // take focus from what the user is typing. Its first line ticks every second, and it closes by itself when the
    // meeting starts.
    class MeetingAlertForm : Form
    {
        public const int FormWidth = 380, FormHeight = 176, Gap = 10, Margin = 16;
        // The user's own picture, when they've picked one, goes where the icon is, this many pixels square.
        const int PictureSize = 64;
        // It closes the moment the meeting starts, but an alert that only appears at or after the start (the calendar is
        // read once a minute) stays this long, so there's time to click Join.
        const int LateStaySeconds = 30;
        const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 1000 };

        public event Action Snoozed;

        // headline gives the first line; it's asked again every second.
        public MeetingAlertForm(CalendarMeeting meeting, Func<string> headline, int snoozeMinutes)
        {
            var shownAt = DateTime.UtcNow;
            var closeAtUtc = meeting.StartUtc > shownAt.AddSeconds(LateStaySeconds) ? meeting.StartUtc : shownAt.AddSeconds(LateStaySeconds);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            Font = new Font("Segoe UI", 10);

            ClientSize = new Size(FormWidth, FormHeight);

            // The user's own picture, if they picked one, takes the icon's place on the left, a little bigger.
            var picture = AlertPicture.Load();
            int textLeft = 16;
            if (picture != null)
            {
                Controls.Add(new PictureTile(picture) { Bounds = new Rectangle(16, 16, PictureSize, PictureSize) });
                textLeft = 16 + PictureSize + 12;
                // Online meetings keep a small video camera, on the picture's corner, so they still stand out.
                if (!string.IsNullOrEmpty(meeting.JoinUrl) && Theme.Icons != null)
                {
                    var badge = new Label
                    {
                        Text = "",
                        Font = new Font(Theme.Icons.FontFamily, 11f),
                        ForeColor = Theme.TeamsText,
                        BackColor = Theme.TeamsTint,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Bounds = new Rectangle(16 + PictureSize - 24, 16 + PictureSize - 24, 28, 28),
                    };
                    Controls.Add(badge);
                    badge.BringToFront();
                }
            }

            // An icon tile on the left: a video camera for online meetings, a calendar for the rest. Left out if the
            // icon font isn't there, or if there's a picture instead.
            if (picture == null && Theme.Icons != null)
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

            var headlineLabel = new Label
            {
                Text = headline(),
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = Theme.TeamsText,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(textLeft, 12, textWidth, 22),
            };
            Controls.Add(headlineLabel);
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

            tick.Tick += delegate
            {
                headlineLabel.Text = headline();
                if (DateTime.UtcNow > closeAtUtc) Close();
            };
            Shown += delegate { tick.Start(); };
            FormClosed += delegate { tick.Dispose(); };
        }

        // Opens the meeting link in the browser, which hands it to Teams. Only web links.
        internal static void OpenLink(string url)
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

    // "Today's meetings": a small window by the tray icon with the day's calendar, opened by clicking the tray icon (if
    // that's on in Settings > Meeting Alerts) or from the tray menu. Each meeting shows its time, title and place, the
    // one on now is highlighted, finished ones are dimmed, and online ones have a Join button. It goes away when the
    // user clicks anywhere else, or presses Esc.
    class AgendaForm : Form
    {
        const int FormWidth = 400, BrandHeight = 60, DateHeight = 56, HeaderHeight = BrandHeight + DateHeight;
        const int FooterHeight = 44, RowHeight = 70, MaxHeight = 600, EdgeGap = 12;
        static readonly Font BrandFont = new Font("Segoe UI Semibold", 13f);
        static readonly Font HintFont = new Font("Segoe UI", 8.5f);
        static readonly Font ArrowFont = new Font("Segoe UI", 18f);
        const int DockHeaderHeight = 72;
        static readonly Font DockTitleFont = new Font("Segoe UI Semibold", 15f);
        static readonly Font DockDateFont = new Font("Segoe UI", 9.5f);
        const string CalendarUrl = "https://outlook.office.com/calendar";

        readonly Func<TeamsPresence> getPresence;
        readonly Action openMeetingSettings;
        readonly Action openProgram;
        readonly Panel list = new Panel { AutoScroll = true };
        readonly Panel footer = new Panel();
        // Docked: the panel beside the BusyLightz window, rather than the one opened from the tray icon.
        readonly bool docked;
        // Where the list starts, under the header.
        readonly int top;
        int contentHeight;
        bool armed;

        // getPresence gives the Teams sign-in the calendar is read through; openMeetingSettings opens Settings on the
        // Meeting Alerts page (for when calendar access isn't allowed yet); openProgram opens the BusyLightz window.
        public AgendaForm(Func<TeamsPresence> getPresence, Action openMeetingSettings, Action openProgram, bool docked = false)
        {
            this.docked = docked;
            top = docked ? DockHeaderHeight : HeaderHeight;
            this.getPresence = getPresence;
            this.openMeetingSettings = openMeetingSettings;
            this.openProgram = openProgram;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = !docked;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            Font = new Font("Segoe UI", 10);

            if (docked) AddDockedHeader();
            else AddFloatingHeader();

            Controls.Add(list);
            var calendar = new LinkLabel
            {
                Text = "Open my calendar",
                AutoSize = true,
                LinkColor = AppSettings.DarkMode ? Theme.TeamsText : Theme.Teams,
                ActiveLinkColor = Theme.Teams,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(16, 12),
            };
            calendar.LinkClicked += delegate
            {
                MeetingAlertForm.OpenLink(CalendarUrl);
                Dismiss();
            };
            footer.Controls.Add(calendar);
            footer.Size = new Size(FormWidth, FooterHeight);
            Controls.Add(footer);

            ShowMessage("Loading...");
            Load();

            if (docked)
            {
                // Beside the BusyLightz window it stays up, and reads the calendar again every minute while it's
                // showing, so the meeting on now and the finished ones stay right.
                var again = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
                again.Tick += delegate { if (Visible) Load(); };
                VisibleChanged += delegate
                {
                    if (Visible) { Load(); again.Start(); }
                    else again.Stop();
                };
                FormClosed += delegate { again.Dispose(); };
                return;
            }
            // It closes when the user clicks elsewhere. Not for the first moment, while the click on the tray icon
            // that opened it is still settling.
            Deactivate += delegate { if (armed) Close(); };
            KeyDown += (sender, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate
            {
                Activate();
                var arm = new System.Windows.Forms.Timer { Interval = 400 };
                arm.Tick += delegate { arm.Dispose(); armed = true; };
                arm.Start();
            };
        }

        // The window opened from the tray icon: the product's name and how to open it, then the date.
        void AddFloatingHeader()
        {
            // The top band: the product's name and how to open it. Clicking anywhere on it opens the BusyLightz window.
            var brand = new Panel { Bounds = new Rectangle(0, 0, FormWidth, BrandHeight), BackColor = Theme.TeamsTint, Cursor = Cursors.Hand };
            bool over = false;
            brand.MouseEnter += delegate { over = true; brand.Invalidate(); };
            brand.MouseLeave += delegate { over = false; brand.Invalidate(); };
            brand.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (over) using (var wash = new SolidBrush(Color.FromArgb(34, Theme.Teams))) g.FillRectangle(wash, brand.ClientRectangle);
                TextRenderer.DrawText(g, "BusyLightz 2.0", BrandFont, new Point(14, 8), Theme.TeamsText);
                TextRenderer.DrawText(g, "Click here or double-click the tray icon to open the program", HintFont, new Point(16, 34), Theme.Muted);
                TextRenderer.DrawText(g, "\u203A", ArrowFont, new Rectangle(FormWidth - 40, 0, 30, BrandHeight), Theme.TeamsText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, BrandHeight - 1, FormWidth, BrandHeight - 1);
            };
            brand.Click += delegate
            {
                Close();
                openProgram();
            };
            Controls.Add(brand);

            Controls.Add(new Label
            {
                Text = "Today, " + DateTime.Now.ToString("dddd MMM d"),
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(16, BrandHeight + 14, FormWidth - 110, 28),
            });
            var refresh = new LinkLabel
            {
                Text = "Refresh",
                AutoSize = true,
                LinkColor = AppSettings.DarkMode ? Theme.TeamsText : Theme.Teams,
                ActiveLinkColor = Theme.Teams,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Location = new Point(FormWidth - 70, BrandHeight + 20),
            };
            refresh.LinkClicked += delegate { Load(); };
            Controls.Add(refresh);
        }

        // The panel beside the BusyLightz window: just its title, the date, and Refresh.
        void AddDockedHeader()
        {
            var band = new Panel { Bounds = new Rectangle(0, 0, FormWidth, DockHeaderHeight), BackColor = Theme.TeamsTint };
            band.Paint += (sender, e) =>
            {
                TextRenderer.DrawText(e.Graphics, "My Schedule Today", DockTitleFont, new Point(14, 9), Theme.TeamsText);
                TextRenderer.DrawText(e.Graphics, DateTime.Now.ToString("dddd, MMMM d"), DockDateFont, new Point(17, 42), Theme.Muted);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, DockHeaderHeight - 1, FormWidth, DockHeaderHeight - 1);
            };
            var refresh = new LinkLabel
            {
                Text = "Refresh",
                AutoSize = true,
                LinkColor = AppSettings.DarkMode ? Theme.TeamsText : Theme.Teams,
                ActiveLinkColor = Theme.Teams,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                Location = new Point(FormWidth - 70, 26),
            };
            refresh.LinkClicked += delegate { Load(); };
            band.Controls.Add(refresh);
            Controls.Add(band);
        }

        // Closes the window, unless it's the one beside the BusyLightz window, which stays.
        void Dismiss()
        {
            if (!docked) Close();
        }

        // Docked: sits flush against the right of the BusyLightz window, or its left when the screen's edge is in the
        // way, at the same height.
        public void DockTo(Rectangle window)
        {
            var area = Screen.FromRectangle(window).WorkingArea;
            int x = window.Right;
            if (x + FormWidth > area.Right) x = window.Left - FormWidth >= area.Left ? window.Left - FormWidth : area.Right - FormWidth;
            Bounds = new Rectangle(x, window.Top, FormWidth, window.Height);
            FitToList(contentHeight);
        }

        // Reads today's calendar in the background, then fills the window in.
        void Load()
        {
            Program.Background("Today's meetings", delegate
            {
                List<CalendarMeeting> meetings = null;
                string problem = null;
                var presence = getPresence();
                if (presence == null || !presence.SignedIn) problem = "Connect to Microsoft Teams first, in Settings > Microsoft Teams.";
                else if (!presence.CalendarGranted) problem = "Calendar access isn't allowed yet.";
                else
                {
                    try { meetings = presence.GetMeetings(DateTime.Today.ToUniversalTime(), DateTime.Today.AddDays(1).ToUniversalTime()); }
                    catch (Exception ex) { problem = ex.Message; }
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed) return;
                        if (problem != null) ShowMessage(problem, !(presence != null && presence.CalendarGranted));
                        else Fill(meetings);
                    });
                }
                catch (InvalidOperationException) { } // Closed before the answer came.
            });
        }

        // Replaces the list with one message, and (when settingsLink is set) a button to the Meeting Alerts settings.
        void ShowMessage(string text, bool settingsLink = false)
        {
            ClearList();
            list.Controls.Add(new Label
            {
                Text = text,
                ForeColor = Theme.Muted,
                AutoSize = false,
                BackColor = Color.Transparent,
                Bounds = new Rectangle(16, 14, FormWidth - 32, 44),
            });
            int height = 70;
            if (settingsLink)
            {
                var open = new ModernButton { Text = "Open Meeting Alerts settings", Bounds = new Rectangle(16, 62, 230, 34) };
                open.Click += delegate
                {
                    Dismiss();
                    openMeetingSettings();
                };
                list.Controls.Add(open);
                height = 110;
            }
            FitToList(height);
        }

        void Fill(List<CalendarMeeting> meetings)
        {
            ClearList();
            var today = DateTime.Today;
            var shown = new List<CalendarMeeting>();
            foreach (var meeting in meetings)
            {
                if (meeting.IsCancelled || meeting.Response == "declined") continue;
                // All-day events come as dates at midnight, so compare dates, not times; the end date is exclusive.
                if (meeting.IsAllDay && !(meeting.StartUtc.Date <= today && meeting.EndUtc.Date > today)) continue;
                shown.Add(meeting);
            }
            shown.Sort((a, b) =>
            {
                if (a.IsAllDay != b.IsAllDay) return a.IsAllDay ? -1 : 1;
                return a.StartUtc.CompareTo(b.StartUtc);
            });
            if (shown.Count == 0)
            {
                ShowMessage("No meetings today.");
                return;
            }
            int y = 0;
            foreach (var meeting in shown)
            {
                list.Controls.Add(Row(meeting, y));
                y += RowHeight;
            }
            FitToList(y);
        }

        Panel Row(CalendarMeeting meeting, int y)
        {
            var now = DateTime.UtcNow;
            bool ended = !meeting.IsAllDay && meeting.EndUtc <= now;
            bool current = !meeting.IsAllDay && meeting.StartUtc <= now && now < meeting.EndUtc;
            int width = FormWidth - SystemInformation.VerticalScrollBarWidth;
            var row = new Panel { Bounds = new Rectangle(0, y, width, RowHeight), BackColor = current ? Theme.TeamsTint : Theme.Card };
            row.Paint += (sender, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, RowHeight - 1, width, RowHeight - 1);
                if (current) using (var brush = new SolidBrush(Theme.Teams)) e.Graphics.FillRectangle(brush, 0, 0, 4, RowHeight - 1);
            };
            bool join = !ended && !string.IsNullOrEmpty(meeting.JoinUrl);
            int textWidth = width - 32 - (join ? 84 : 0);
            string time = meeting.IsAllDay
                ? "All day"
                : meeting.StartUtc.ToLocalTime().ToShortTimeString() + " - " + meeting.EndUtc.ToLocalTime().ToShortTimeString() + (current ? "   Now" : "");
            row.Controls.Add(new Label
            {
                Text = time,
                Font = new Font("Segoe UI", 9f),
                ForeColor = current ? Theme.TeamsText : Theme.Muted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Bounds = new Rectangle(16, 8, textWidth, 18),
            });
            row.Controls.Add(new Label
            {
                Text = string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject,
                Font = new Font("Segoe UI Semibold", 10.5f),
                ForeColor = ended ? Theme.Muted : Theme.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Bounds = new Rectangle(16, 27, textWidth, 22),
            });
            row.Controls.Add(new Label
            {
                Text = meeting.Location ?? "",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Bounds = new Rectangle(16, 48, textWidth, 18),
            });
            if (join)
            {
                var button = new ModernButton { Text = "Join", Accent = true, Bounds = new Rectangle(width - 16 - 68, 18, 68, 34) };
                button.Click += delegate
                {
                    MeetingAlertForm.OpenLink(meeting.JoinUrl);
                    Dismiss();
                };
                row.Controls.Add(button);
            }
            return row;
        }

        void ClearList()
        {
            // Copied first: disposing a control takes it out of the collection being walked.
            var all = new Control[list.Controls.Count];
            list.Controls.CopyTo(all, 0);
            list.Controls.Clear();
            foreach (var control in all) control.Dispose();
        }

        // Sizes the window to its list (scrolling past a screenful) and puts it above the taskbar, bottom right.
        void FitToList(int listContentHeight)
        {
            contentHeight = listContentHeight;
            if (docked)
            {
                // Fills the height of the BusyLightz window, and scrolls when there's more than fits.
                int room = Math.Max(0, Height - top - FooterHeight);
                list.Bounds = new Rectangle(0, top, FormWidth, room);
                footer.Top = top + room;
                return;
            }
            int listHeight = Math.Min(listContentHeight, MaxHeight - HeaderHeight - FooterHeight);
            list.Bounds = new Rectangle(0, HeaderHeight, FormWidth, listHeight);
            footer.Top = HeaderHeight + listHeight;
            ClientSize = new Size(FormWidth, HeaderHeight + listHeight + FooterHeight);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - EdgeGap, area.Bottom - Height - EdgeGap);
        }

        // The panel beside the window shouldn't take the keyboard when it appears.
        protected override bool ShowWithoutActivation { get { return docked; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
