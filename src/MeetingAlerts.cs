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
            // The first line turns red for the last minute before the meeting (and stays red once it has started).
            Action colorHeadline = delegate
            {
                bool urgent = (meeting.StartUtc - DateTime.UtcNow).TotalSeconds <= 60;
                headlineLabel.ForeColor = urgent ? (AppSettings.DarkMode ? Color.FromArgb(255, 107, 107) : Theme.Red) : Theme.TeamsText;
            };
            colorHeadline();
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
                colorHeadline();
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

    // Drawing helpers for the schedule's custom-drawn pieces.
    static class AgendaDraw
    {
        const TextFormatFlags Base = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Left(Graphics g, string text, Font font, Color color, Rectangle r)
        {
            TextRenderer.DrawText(g, text, font, r, color, Base | TextFormatFlags.Left);
        }

        public static void Right(Graphics g, string text, Font font, Color color, Rectangle r)
        {
            TextRenderer.DrawText(g, text, font, r, color, Base | TextFormatFlags.Right);
        }

        public static void Center(Graphics g, string text, Font font, Color color, Rectangle r)
        {
            TextRenderer.DrawText(g, text, font, r, color, Base | TextFormatFlags.HorizontalCenter);
        }

        // 3h 30m, 2h, 45m.
        public static string Span(double minutes)
        {
            int m = (int)Math.Round(Math.Max(0, minutes));
            int h = m / 60;
            m = m % 60;
            if (h > 0 && m > 0) return h + "h " + m + "m";
            if (h > 0) return h + "h";
            return m + "m";
        }

        // 8 AM, 12 PM, 6 PM.
        public static string Hour(int hour)
        {
            hour = hour % 24;
            return (hour % 12 == 0 ? 12 : hour % 12) + (hour < 12 ? " AM" : " PM");
        }

        // The meeting's place in a few friendly words: Teams for a Teams meeting, Zoom, Calendly and so on for a link,
        // else what the calendar says. "Online" for a meeting with a join link and no place.
        public static string Place(CalendarMeeting meeting)
        {
            string location = (meeting.Location ?? "").Trim();
            if (location.Length == 0) return string.IsNullOrEmpty(meeting.JoinUrl) ? "" : "Online";
            if (location.StartsWith("Microsoft Teams", StringComparison.OrdinalIgnoreCase)) return "Teams";
            Uri uri;
            if (Uri.TryCreate(location, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                string host = uri.Host.ToLowerInvariant();
                if (host.EndsWith("zoom.us")) return "Zoom";
                if (host.Contains("teams.microsoft.com") || host.Contains("teams.live.com")) return "Teams";
                if (host.Contains("calendly.com")) return "Calendly";
                if (host.Contains("meet.google.com")) return "Google Meet";
                if (host.Contains("webex.com")) return "Webex";
                return host.StartsWith("www.") ? host.Substring(4) : host;
            }
            return location;
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        // Dark scrollbars on a control (Windows 10 and later); left as they are if Windows won't.
        public static void DarkScrollbars(Control control)
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch (Exception) { }
        }

        public static string Subject(CalendarMeeting meeting)
        {
            return string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject;
        }

        public static string Times(CalendarMeeting meeting)
        {
            if (meeting.IsAllDay) return "All day";
            return meeting.StartUtc.ToLocalTime().ToShortTimeString() + " - " + meeting.EndUtc.ToLocalTime().ToShortTimeString();
        }

        // Hours into today, local time (0 to 24), for placing a time on the day's timeline.
        public static double HourOfDay(DateTime utc)
        {
            var local = utc.ToLocalTime();
            if (local.Date < DateTime.Today) return 0;
            if (local.Date > DateTime.Today) return 24;
            return local.TimeOfDay.TotalHours;
        }
    }

    // A panel that paints without flicker, for the schedule's custom-drawn pieces.
    class Surface : Panel
    {
        public Surface()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    // A rounded "pill" button, the schedule's Join: the brand gradient, or white on a colored card.
    class PillButton : Control
    {
        static readonly Font TextFont = new Font("Segoe UI Semibold", 10f);
        static readonly Font GlyphFont = Theme.Icons == null ? null : new Font(Theme.Icons.FontFamily, 10f);
        readonly bool white;
        bool over;

        public PillButton(string text, bool white)
        {
            Text = text;
            this.white = white;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = AgendaDraw.Round(rect, Height / 2f))
            {
                if (white)
                {
                    using (var brush = new SolidBrush(over ? Color.FromArgb(232, 234, 255) : Color.White)) g.FillPath(brush, path);
                }
                else
                {
                    var from = over ? Color.FromArgb(126, 130, 238) : Color.FromArgb(106, 110, 224);
                    var to = over ? Color.FromArgb(94, 98, 198) : Color.FromArgb(74, 78, 176);
                    using (var brush = new LinearGradientBrush(rect, from, to, LinearGradientMode.Horizontal)) g.FillPath(brush, path);
                }
            }
            Color ink = white ? Color.FromArgb(59, 63, 143) : Color.White;
            int textWidth = TextRenderer.MeasureText(g, Text, TextFont, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
            int glyphWidth = GlyphFont == null ? 0 : 22;
            int x = Math.Max(4, (Width - textWidth - glyphWidth) / 2);
            if (GlyphFont != null)
                TextRenderer.DrawText(g, "", GlyphFont, new Rectangle(x, 0, glyphWidth, Height), ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, TextFont, new Rectangle(x + glyphWidth, 0, textWidth + 6, Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // "My Schedule Today" / "Today's meetings": the day's calendar, in a panel beside the BusyLightz window or in a small
    // window by the tray icon (from the tray menu). A header shows the date, the day as a timeline with a marker for now,
    // and how much is booked and free. Below, finished meetings are small and dimmed, the meeting on now is a
    // highlighted card with how long is left, free time between meetings is called out, and the rest are cards with a
    // Join button when they're online. The window by the tray icon goes away when the user clicks anywhere else, or
    // presses Esc; the panel beside the window stays.
    class AgendaForm : Form
    {
        // Raised when the user clicks the × (the panel hides itself).
        public event Action Dismissed;

        const int FormWidth = 400, BrandHeight = 60, BandHeight = 146;
        const int FooterHeight = 56, MaxHeight = 700, EdgeGap = 12, Pad = 12;
        // The outline around the whole window, and the title bar across the top of the panel beside the BusyLightz window.
        const int Frame = 1, TitleHeight = 32, OuterWidth = FormWidth + 2 * Frame;
        static readonly Font BrandFont = new Font("Segoe UI Semibold", 13f);
        static readonly Font HintFont = new Font("Segoe UI", 8.5f);
        static readonly Font ArrowFont = new Font("Segoe UI", 18f);
        static readonly Font TitleFont = new Font("Segoe UI Semibold", 16f);
        static readonly Font DateFont = new Font("Segoe UI", 9.5f);
        static readonly Font SummaryFont = new Font("Segoe UI Semibold", 9f);
        static readonly Font AxisFont = new Font("Segoe UI", 7.5f);
        static readonly Font NowFont = new Font("Segoe UI Semibold", 7.5f);
        static readonly Font PillFont = new Font("Segoe UI Semibold", 7.5f);
        static readonly Font HeroTitleFont = new Font("Segoe UI Semibold", 12.5f);
        static readonly Font SmallFont = new Font("Segoe UI", 9f);
        static readonly Font SmallStrongFont = new Font("Segoe UI Semibold", 8.5f);
        static readonly Font CardTitleFont = new Font("Segoe UI Semibold", 10.5f);
        static readonly Font MiniFont = new Font("Segoe UI Semibold", 9.5f);
        static readonly Font TinyFont = new Font("Segoe UI", 8.5f);
        static readonly Font IconFont = Theme.Icons == null ? null : new Font(Theme.Icons.FontFamily, 12f);
        static readonly Font SmallIconFont = Theme.Icons == null ? null : new Font(Theme.Icons.FontFamily, 9f);
        static readonly Color Teal = Color.FromArgb(20, 184, 166);
        static readonly Color Yellow = Color.FromArgb(253, 224, 71);
        static readonly Color Indigo = Color.FromArgb(59, 63, 143);
        static readonly Rectangle RefreshRect = new Rectangle(FormWidth - 52, 14, 34, 34);
        const string CalendarUrl = "https://outlook.office.com/calendar";

        readonly Func<TeamsPresence> getPresence;
        readonly Action openMeetingSettings;
        readonly Action openProgram;
        readonly Panel list = new Panel { AutoScroll = true };
        readonly Surface header = new Surface();
        readonly Surface titleBar = new Surface();
        readonly Surface footer = new Surface();
        // Docked: the panel beside the BusyLightz window, rather than the one opened from the tray icon.
        readonly bool docked;
        // Where the list starts, under the header.
        readonly int top;
        int contentHeight;
        bool armed, footerOver;
        // Today's meetings as last read (null while loading, or when they couldn't be read), for the header's timeline.
        List<CalendarMeeting> todays;

        // getPresence gives the Teams sign-in the calendar is read through; openMeetingSettings opens Settings on the
        // Meeting Alerts page (for when calendar access isn't allowed yet); openProgram opens the BusyLightz window.
        public AgendaForm(Func<TeamsPresence> getPresence, Action openMeetingSettings, Action openProgram, bool docked = false)
        {
            this.docked = docked;
            int headerTop = docked ? TitleHeight : BrandHeight;
            top = Frame + headerTop + BandHeight;
            this.getPresence = getPresence;
            this.openMeetingSettings = openMeetingSettings;
            this.openProgram = openProgram;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = !docked;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Page;
            Font = new Font("Segoe UI", 10);

            if (docked) AddTitleBar();
            else AddBrandBand();
            AddHeader(headerTop);

            list.BackColor = Theme.Page;
            Controls.Add(list);
            // The scrollbar matches the window in Dark mode, instead of a bright white one.
            list.HandleCreated += delegate { if (AppSettings.DarkMode) AgendaDraw.DarkScrollbars(list); };
            AddFooter();

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
                    if (Visible) { scrolledToNext = false; Load(); again.Start(); }
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

        // The window opened from the tray icon: a band with the product's name and how to open it. Clicking anywhere on
        // it opens the BusyLightz window.
        void AddBrandBand()
        {
            var brand = new Panel { Bounds = new Rectangle(Frame, Frame, FormWidth, BrandHeight), BackColor = Theme.TeamsTint, Cursor = Cursors.Hand };
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
                TextRenderer.DrawText(g, "›", ArrowFont, new Rectangle(FormWidth - 40, 0, 30, BrandHeight), Theme.TeamsText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, BrandHeight - 1, FormWidth, BrandHeight - 1);
            };
            brand.Click += delegate
            {
                Close();
                openProgram();
            };
            Controls.Add(brand);
        }

        // The title bar across the top of the panel beside the window, like the BusyLightz window's own: the app's
        // red and blue icon and its name.
        void AddTitleBar()
        {
            titleBar.Bounds = new Rectangle(Frame, Frame, FormWidth, TitleHeight);
            var closeRect = new Rectangle(FormWidth - 46, 0, 46, TitleHeight - 1);
            bool overClose = false;
            titleBar.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BarColor);
                using (var red = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillPie(red, 12, 9, 14, 14, 90, 180);
                using (var blue = new SolidBrush(Color.FromArgb(0, 120, 212))) g.FillPie(blue, 12, 9, 14, 14, 270, 180);
                AgendaDraw.Left(g, Program.AppName, SmallFont, Theme.Text, new Rectangle(36, 0, FormWidth - 90, TitleHeight));
                // The close button, red under the pointer like Windows' own.
                if (overClose) using (var hot = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillRectangle(hot, closeRect);
                using (var pen = new Pen(overClose ? Color.White : Theme.Text, 1.2f))
                {
                    int cx = closeRect.X + closeRect.Width / 2, cy = closeRect.Y + closeRect.Height / 2;
                    g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(pen, cx - 5, cy + 5, cx + 5, cy - 5);
                }
                using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, TitleHeight - 1, FormWidth, TitleHeight - 1);
            };
            titleBar.MouseMove += (sender, e) =>
            {
                bool inside = closeRect.Contains(e.Location);
                if (inside != overClose) { overClose = inside; titleBar.Invalidate(); }
            };
            titleBar.MouseLeave += delegate { if (overClose) { overClose = false; titleBar.Invalidate(); } };
            // The × only hides the panel (the setting stays on). It comes back from the Calendar tile on the main window,
            // the tray menu's "Today's meetings", or the next time BusyLightz starts.
            titleBar.MouseClick += (sender, e) =>
            {
                if (!closeRect.Contains(e.Location)) return;
                Hide();
                if (Dismissed != null) Dismissed();
            };
            Controls.Add(titleBar);
        }

        // The title bar's and bottom bar's color: a little off the page, as the main window's are.
        static Color BarColor { get { return AppSettings.DarkMode ? Color.FromArgb(38, 38, 42) : Color.FromArgb(240, 240, 244); } }

        // The header: a gradient with the title, the date, a refresh button, the day's timeline and a summary line.
        void AddHeader(int y)
        {
            header.Bounds = new Rectangle(Frame, Frame + y, FormWidth, BandHeight);
            header.Paint += (sender, e) => PaintHeader(e.Graphics);
            header.MouseMove += (sender, e) => { header.Cursor = RefreshRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default; };
            header.MouseClick += (sender, e) => { if (RefreshRect.Contains(e.Location)) Load(); };
            Controls.Add(header);
        }

        void PaintHeader(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(0, 0, FormWidth, BandHeight);
            using (var brush = new LinearGradientBrush(area, Color.FromArgb(106, 110, 224), Color.FromArgb(52, 62, 170), 35f))
                g.FillRectangle(brush, area);
            using (var soft = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                g.FillEllipse(soft, FormWidth - 150, -80, 200, 200);
                g.FillEllipse(soft, FormWidth - 100, 20, 90, 90);
            }
            TextRenderer.DrawText(g, "My Schedule Today", TitleFont, new Point(16, 10), Color.White);
            TextRenderer.DrawText(g, DateTime.Now.ToString("dddd, MMMM d"), DateFont, new Point(18, 42), Color.FromArgb(226, 232, 255));

            using (var circle = new SolidBrush(Color.FromArgb(56, 255, 255, 255))) g.FillEllipse(circle, RefreshRect);
            AgendaDraw.Center(g, Theme.Icons == null ? "R" : "", Theme.Icons == null ? SmallFont : IconFont, Color.White, RefreshRect);

            if (todays == null) return;
            var timed = new List<CalendarMeeting>();
            foreach (var meeting in todays) if (!meeting.IsAllDay) timed.Add(meeting);
            if (timed.Count == 0)
            {
                TextRenderer.DrawText(g, todays.Count == 0 ? "Nothing on the calendar today" : "Only all-day events today", SummaryFont,
                    new Point(18, 118), Color.White);
                return;
            }

            // The day runs 8 AM to 6 PM, wider if a meeting starts earlier or ends later.
            double startHour = 8, endHour = 18;
            foreach (var meeting in timed)
            {
                startHour = Math.Min(startHour, Math.Floor(AgendaDraw.HourOfDay(meeting.StartUtc)));
                endHour = Math.Max(endHour, Math.Ceiling(AgendaDraw.HourOfDay(meeting.EndUtc)));
            }
            startHour = Math.Max(0, startHour);
            endHour = Math.Min(24, Math.Max(endHour, startHour + 4));
            double span = endHour - startHour;
            const int barX = 18, barY = 82, barH = 10;
            int barW = FormWidth - 2 * barX;
            Func<double, float> at = hour => (float)(barX + (hour - startHour) / span * barW);

            using (var track = new SolidBrush(Color.FromArgb(56, 255, 255, 255)))
            using (var trackPath = AgendaDraw.Round(new RectangleF(barX, barY, barW, barH), 5))
                g.FillPath(track, trackPath);
            var now = DateTime.UtcNow;
            foreach (var meeting in timed)
            {
                float x0 = at(Math.Max(startHour, AgendaDraw.HourOfDay(meeting.StartUtc)));
                float x1 = at(Math.Min(endHour, AgendaDraw.HourOfDay(meeting.EndUtc)));
                if (x1 - x0 < 4) x1 = x0 + 4;
                Color fill;
                if (meeting.EndUtc <= now) fill = Color.FromArgb(120, 255, 255, 255);
                else if (string.IsNullOrEmpty(meeting.JoinUrl)) fill = Color.FromArgb(94, 234, 212);
                else fill = Color.White;
                using (var brush = new SolidBrush(fill))
                using (var path = AgendaDraw.Round(new RectangleF(x0, barY, x1 - x0, barH), 5))
                    g.FillPath(brush, path);
            }

            double nowHour = AgendaDraw.HourOfDay(now);
            if (nowHour >= startHour && nowHour <= endHour)
            {
                float nx = at(nowHour);
                using (var marker = new SolidBrush(Yellow)) g.FillRectangle(marker, nx - 1.5f, barY - 6, 3, barH + 12);
                AgendaDraw.Center(g, "NOW", NowFont, Yellow, new Rectangle((int)nx - 24, barY - 22, 48, 14));
            }

            // Hour labels at the ends and, when there's room, at noon.
            AgendaDraw.Left(g, AgendaDraw.Hour((int)startHour), AxisFont, Color.FromArgb(214, 220, 250), new Rectangle(barX, barY + 14, 60, 14));
            AgendaDraw.Right(g, AgendaDraw.Hour((int)endHour), AxisFont, Color.FromArgb(214, 220, 250), new Rectangle(barX + barW - 60, barY + 14, 60, 14));
            if (startHour < 12 && endHour > 12)
            {
                float nx = at(12);
                if (nx - barX > 70 && barX + barW - nx > 70)
                    AgendaDraw.Center(g, "12 PM", AxisFont, Color.FromArgb(214, 220, 250), new Rectangle((int)nx - 30, barY + 14, 60, 14));
            }

            // How much of the day is booked, with meetings that overlap counted once.
            var ranges = new List<double[]>();
            foreach (var meeting in timed)
                ranges.Add(new double[] { AgendaDraw.HourOfDay(meeting.StartUtc), AgendaDraw.HourOfDay(meeting.EndUtc) });
            ranges.Sort((a, b) => a[0].CompareTo(b[0]));
            double bookedHours = 0, runStart = ranges[0][0], runEnd = ranges[0][1];
            for (int i = 1; i < ranges.Count; i++)
            {
                if (ranges[i][0] <= runEnd) runEnd = Math.Max(runEnd, ranges[i][1]);
                else
                {
                    bookedHours += runEnd - runStart;
                    runStart = ranges[i][0];
                    runEnd = ranges[i][1];
                }
            }
            bookedHours += runEnd - runStart;
            double freeHours = Math.Max(0, span - bookedHours);
            string summary = timed.Count + (timed.Count == 1 ? " meeting" : " meetings") + "  ·  " + AgendaDraw.Span(bookedHours * 60) + " booked  ·  "
                + AgendaDraw.Span(freeHours * 60) + " free";
            TextRenderer.DrawText(g, summary, SummaryFont, new Point(18, 118), Color.White);
        }

        // The footer: an outlined "Open my calendar" button.
        void AddFooter()
        {
            footer.Size = new Size(FormWidth, FooterHeight);
            footer.Left = Frame;
            footer.BackColor = BarColor;
            var pill = new Rectangle((FormWidth - 200) / 2, 11, 200, 34);
            Color ink = AppSettings.DarkMode ? Theme.TeamsText : Theme.Teams;
            footer.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BarColor);
                using (var line = new Pen(Theme.Border)) g.DrawLine(line, 0, 0, FormWidth, 0);
                var rect = new Rectangle(pill.X, pill.Y, pill.Width - 1, pill.Height - 1);
                using (var path = AgendaDraw.Round(rect, 17f))
                {
                    if (footerOver) using (var wash = new SolidBrush(Color.FromArgb(30, Theme.Teams))) g.FillPath(wash, path);
                    using (var pen = new Pen(ink, 1.5f)) g.DrawPath(pen, path);
                }
                AgendaDraw.Center(g, "Open my calendar  \u2197", MiniFont, ink, pill);
            };
            footer.MouseMove += (sender, e) =>
            {
                bool inside = pill.Contains(e.Location);
                footer.Cursor = inside ? Cursors.Hand : Cursors.Default;
                if (inside != footerOver) { footerOver = inside; footer.Invalidate(); }
            };
            footer.MouseLeave += delegate { if (footerOver) { footerOver = false; footer.Invalidate(); } };
            footer.MouseClick += (sender, e) =>
            {
                if (!pill.Contains(e.Location)) return;
                MeetingAlertForm.OpenLink(CalendarUrl);
                Dismiss();
            };
            Controls.Add(footer);
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
            if (x + OuterWidth > area.Right) x = window.Left - OuterWidth >= area.Left ? window.Left - OuterWidth : area.Right - OuterWidth;
            Bounds = new Rectangle(x, window.Top, OuterWidth, window.Height);
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
            todays = null;
            header.Invalidate();
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

        // Whether the list has been scrolled past the finished meetings yet; reset each time the panel is shown, so it
        // opens on the meeting on now or the next one. After that, a refresh keeps wherever the user has scrolled to.
        bool scrolledToNext;

        void Fill(List<CalendarMeeting> meetings)
        {
            int keepScroll = -list.AutoScrollPosition.Y;
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
            todays = shown;
            header.Invalidate();
            if (shown.Count == 0)
            {
                ShowMessage("No meetings today.");
                todays = shown;
                header.Invalidate();
                return;
            }

            int width = FormWidth - SystemInformation.VerticalScrollBarWidth - 2 * Pad;
            var now = DateTime.UtcNow;
            int y = Pad;
            DateTime busyUntil = DateTime.MinValue;
            bool anyTimed = false;
            int nextY = -1; // Where the meeting on now, or the next one, starts (with its free time above it).
            foreach (var meeting in shown)
            {
                bool ended = !meeting.IsAllDay && meeting.EndUtc <= now;
                bool current = !meeting.IsAllDay && meeting.StartUtc <= now && now < meeting.EndUtc;
                if (nextY < 0 && !meeting.IsAllDay && !ended) nextY = y;
                // Free time before this meeting, from the later of the last one's end and now.
                if (!meeting.IsAllDay && anyTimed && meeting.StartUtc > now)
                {
                    var from = busyUntil > now ? busyUntil : now;
                    double free = (meeting.StartUtc - from).TotalMinutes;
                    if (free >= 15)
                    {
                        list.Controls.Add(FreeRow(AgendaDraw.Span(free) + " free", y, width));
                        y += 28;
                    }
                }
                Control row;
                int height;
                if (ended) { row = MiniRow(meeting, y, width); height = 50; }
                else if (current) { row = HeroCard(meeting, y, width); height = 116; }
                else { row = Card(meeting, y, width); height = 68; }
                list.Controls.Add(row);
                y += height + 8;
                if (!meeting.IsAllDay)
                {
                    anyTimed = true;
                    if (meeting.EndUtc > busyUntil) busyUntil = meeting.EndUtc;
                }
            }
            FitToList(y + 4);
            // The finished meetings scroll out of sight at the top, so the list opens on the one on now or the next one.
            int target = scrolledToNext ? keepScroll : (nextY > Pad ? nextY - Pad : 0);
            scrolledToNext = true;
            if (target > 0 && IsHandleCreated)
                BeginInvoke(new Action(() => { if (!list.IsDisposed) list.AutoScrollPosition = new Point(0, target); }));
        }

        // A finished meeting: small and dimmed, with a check.
        Control MiniRow(CalendarMeeting meeting, int y, int width)
        {
            var row = new Surface { Bounds = new Rectangle(Pad, y, width, 50) };
            row.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Page);
                using (var path = AgendaDraw.Round(new Rectangle(0, 0, width - 1, 49), 12))
                using (var fill = new SolidBrush(Theme.Mix(Theme.Page, Theme.Border, 0.4)))
                    g.FillPath(fill, path);
                var green = Color.FromArgb(46, 160, 67);
                using (var disc = new SolidBrush(Color.FromArgb(46, green))) g.FillEllipse(disc, 14, 13, 24, 24);
                AgendaDraw.Center(g, Theme.Icons == null ? "✓" : "", Theme.Icons == null ? SmallFont : SmallIconFont, green, new Rectangle(14, 13, 24, 24));
                string when = meeting.StartUtc.ToLocalTime().ToShortTimeString() + " · done";
                AgendaDraw.Right(g, when, TinyFont, Theme.Muted, new Rectangle(width - 14 - 110, 0, 110, 50));
                AgendaDraw.Left(g, AgendaDraw.Subject(meeting), MiniFont, Theme.Muted, new Rectangle(48, 0, width - 48 - 14 - 116, 50));
            };
            return row;
        }

        // Free time between two meetings: a dashed line with how long it is.
        Control FreeRow(string text, int y, int width)
        {
            var row = new Surface { Bounds = new Rectangle(Pad, y, width, 28) };
            row.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.Clear(Theme.Page);
                int textWidth = TextRenderer.MeasureText(g, text, TinyFont, new Size(1000, 100), TextFormatFlags.NoPadding).Width;
                int left = (width - textWidth) / 2;
                using (var pen = new Pen(Theme.Border, 1.5f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, 8, 14, left - 10, 14);
                    g.DrawLine(pen, left + textWidth + 10, 14, width - 8, 14);
                }
                AgendaDraw.Center(g, text, TinyFont, Theme.Muted, new Rectangle(left - 4, 0, textWidth + 8, 28));
            };
            return row;
        }

        // The meeting on right now: a highlighted card with how long is left and a progress bar.
        Control HeroCard(CalendarMeeting meeting, int y, int width)
        {
            bool join = !string.IsNullOrEmpty(meeting.JoinUrl);
            var hero = new Surface { Bounds = new Rectangle(Pad, y, width, 116) };
            hero.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Page);
                var rect = new Rectangle(0, 0, width - 1, 115);
                using (var path = AgendaDraw.Round(rect, 14))
                {
                    using (var brush = new LinearGradientBrush(rect, Color.FromArgb(111, 115, 234), Color.FromArgb(59, 63, 143), 35f))
                        g.FillPath(brush, path);
                    g.SetClip(path);
                    using (var glow = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillEllipse(glow, width - 110, -60, 150, 150);
                    g.ResetClip();
                }
                var pill = new Rectangle(14, 12, 108, 18);
                using (var pillPath = AgendaDraw.Round(pill, 9))
                using (var yellow = new SolidBrush(Yellow))
                    g.FillPath(yellow, pillPath);
                AgendaDraw.Center(g, "HAPPENING NOW", PillFont, Indigo, pill);

                int textWidth = width - 28 - (join ? 100 : 0);
                AgendaDraw.Left(g, AgendaDraw.Subject(meeting), HeroTitleFont, Color.White, new Rectangle(14, 36, textWidth, 24));
                string place = AgendaDraw.Place(meeting);
                string sub = AgendaDraw.Times(meeting) + (place.Length > 0 ? "  ·  " + place : "");
                AgendaDraw.Left(g, sub, SmallFont, Color.FromArgb(226, 230, 255), new Rectangle(14, 62, textWidth, 18));

                var now = DateTime.UtcNow;
                double total = Math.Max(1, (meeting.EndUtc - meeting.StartUtc).TotalSeconds);
                double done = Math.Min(1, Math.Max(0, (now - meeting.StartUtc).TotalSeconds / total));
                AgendaDraw.Right(g, "ends in " + AgendaDraw.Span((meeting.EndUtc - now).TotalMinutes), TinyFont, Color.White,
                    new Rectangle(width - 14 - 140, 80, 140, 16));
                var bar = new RectangleF(14, 100, width - 28, 6);
                using (var track = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                using (var trackPath = AgendaDraw.Round(bar, 3))
                    g.FillPath(track, trackPath);
                if (done > 0)
                    using (var fill = new SolidBrush(Yellow))
                    using (var fillPath = AgendaDraw.Round(new RectangleF(bar.X, bar.Y, Math.Max(6f, (float)(bar.Width * done)), bar.Height), 3))
                        g.FillPath(fill, fillPath);
            };
            if (join)
            {
                var button = new PillButton("Join", true) { Bounds = new Rectangle(width - 14 - 84, 34, 84, 36) };
                button.Click += delegate
                {
                    MeetingAlertForm.OpenLink(meeting.JoinUrl);
                    Dismiss();
                };
                hero.Controls.Add(button);
            }
            return hero;
        }

        // An upcoming (or all-day) meeting: a card with an accent stripe and icon, when it starts, and Join if online.
        Control Card(CalendarMeeting meeting, int y, int width)
        {
            bool join = !string.IsNullOrEmpty(meeting.JoinUrl);
            string place = AgendaDraw.Place(meeting);
            Color accent = join ? Theme.Teams : Teal;
            var card = new Surface { Bounds = new Rectangle(Pad, y, width, 68) };
            card.Paint += (sender, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Page);
                var rect = new Rectangle(0, 0, width - 1, 67);
                using (var path = AgendaDraw.Round(rect, 12))
                {
                    using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                    g.SetClip(path);
                    using (var stripe = new SolidBrush(accent)) g.FillRectangle(stripe, 0, 0, 5, 68);
                    g.ResetClip();
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                }
                using (var disc = new SolidBrush(Color.FromArgb(40, accent))) g.FillEllipse(disc, 18, 16, 36, 36);
                string glyph = meeting.IsAllDay ? "" : join ? "" : place.Length > 0 ? "" : "";
                if (IconFont != null) AgendaDraw.Center(g, glyph, IconFont, accent, new Rectangle(18, 16, 36, 36));

                int textWidth = width - 66 - (join ? 104 : 16);
                AgendaDraw.Left(g, AgendaDraw.Subject(meeting), CardTitleFont, Theme.Text, new Rectangle(66, 8, textWidth, 22));
                AgendaDraw.Left(g, AgendaDraw.Times(meeting), SmallFont, Theme.Muted, new Rectangle(66, 30, textWidth, 16));
                string third = place;
                if (!meeting.IsAllDay)
                {
                    double minutes = (meeting.StartUtc - DateTime.UtcNow).TotalMinutes;
                    third = (minutes > 0 ? "in " + AgendaDraw.Span(minutes) : "starting now") + (place.Length > 0 ? "  ·  " + place : "");
                }
                AgendaDraw.Left(g, third, SmallStrongFont, accent, new Rectangle(66, 46, textWidth, 16));
            };
            if (join)
            {
                var button = new PillButton("Join", false) { Bounds = new Rectangle(width - 12 - 84, 16, 84, 36) };
                button.Click += delegate
                {
                    MeetingAlertForm.OpenLink(meeting.JoinUrl);
                    Dismiss();
                };
                card.Controls.Add(button);
            }
            return card;
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
                int room = Math.Max(0, Height - top - FooterHeight - Frame);
                list.Bounds = new Rectangle(Frame, top, FormWidth, room);
                footer.Top = top + room;
                return;
            }
            int listHeight = Math.Min(listContentHeight, MaxHeight - top - FooterHeight - Frame);
            list.Bounds = new Rectangle(Frame, top, FormWidth, listHeight);
            footer.Top = top + listHeight;
            ClientSize = new Size(OuterWidth, top + listHeight + FooterHeight + Frame);
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - EdgeGap, area.Bottom - Height - EdgeGap);
        }

        // The panel beside the window shouldn't take the keyboard when it appears.
        protected override bool ShowWithoutActivation { get { return docked; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderHover)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
