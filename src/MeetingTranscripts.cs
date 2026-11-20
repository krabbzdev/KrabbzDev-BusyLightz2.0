using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Krabbz.Blynclight
{
    // Teams meeting transcripts, for people who turn it on: every few minutes it looks at the Teams meetings on the user's
    // calendar that have just ended and, when Teams has made a transcript of one, saves a copy as a text file in
    // Documents\BusyLightz Transcripts and says so. BusyLightz doesn't record anything or start transcription: it only
    // saves what Teams already made, and only for meetings the user was invited to (Microsoft decides who can read a
    // transcript). Needs calendar access and transcript access, both allowed in Settings > Meeting Alerts.
    public class MeetingTranscripts : IDisposable
    {
        const int CheckMs = 3 * 60 * 1000;
        const int FirstCheckMs = 90 * 1000;
        // Teams makes the transcript a little after a meeting ends: wait this long first, and keep looking this long.
        const int WaitMinutes = 2;
        const int LookForHours = 6;
        const int RememberLines = 500;

        readonly Func<TeamsPresence> getPresence;
        readonly Action<string, string> ready;
        readonly Timer timer;
        readonly object sync = new object();
        int checking;
        volatile string problem;
        HashSet<string> done;

        // ready(title, path) is called, on a background thread, for each transcript that has been saved.
        public MeetingTranscripts(Func<TeamsPresence> getPresence, Action<string, string> ready)
        {
            this.getPresence = getPresence;
            this.ready = ready;
            timer = new Timer(delegate { Program.Guard("Meeting transcripts", Check); }, null, FirstCheckMs, CheckMs);
        }

        // Why transcripts couldn't be read at the last check, or null.
        public string Problem { get { return problem; } }

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BusyLightz Transcripts"); }
        }

        static string DonePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Krabbz9999 Blynclight\transcripts-done.txt");
            }
        }

        public void CheckNow()
        {
            Program.Background("Meeting transcripts", Check);
        }

        void Check()
        {
            if (Interlocked.Exchange(ref checking, 1) == 1) return;
            try { CheckMeetings(); }
            finally { Interlocked.Exchange(ref checking, 0); }
        }

        void CheckMeetings()
        {
            if (!AppSettings.TranscriptsOn) { problem = null; return; }
            var presence = getPresence();
            if (presence == null || !presence.SignedIn || !presence.CalendarGranted || !presence.TranscriptsGranted) { problem = null; return; }

            var now = DateTime.UtcNow;
            List<CalendarMeeting> meetings;
            try { meetings = presence.GetMeetings(now.AddHours(-LookForHours), now); }
            catch (Exception ex)
            {
                Program.LogEvent("Transcripts: couldn't read the calendar: " + ex.Message);
                return;
            }
            LoadDone();
            foreach (var meeting in meetings)
            {
                if (meeting.IsCancelled || meeting.IsAllDay || meeting.Response == "declined") continue;
                if (!IsTeamsLink(meeting.JoinUrl)) continue;
                if (meeting.EndUtc.AddMinutes(WaitMinutes) > now) continue; // Still going, or only just over.
                try
                {
                    string meetingId = presence.FindOnlineMeetingId(meeting.JoinUrl);
                    if (meetingId == null) continue;
                    foreach (var transcript in presence.ListTranscripts(meetingId))
                    {
                        string key = meeting.Id + "|" + transcript.Id;
                        if (IsDone(key)) continue;
                        string text = presence.GetTranscriptText(meetingId, transcript.Id);
                        string path = Save(meeting, text);
                        MarkDone(key);
                        Program.LogEvent("Saved the transcript of \"" + meeting.Subject + "\"");
                        ready(string.IsNullOrWhiteSpace(meeting.Subject) ? "(no subject)" : meeting.Subject, path);
                    }
                    problem = null;
                }
                catch (InvalidOperationException ex)
                {
                    // Microsoft said no (an administrator hasn't approved it, say): said in Settings, and no point asking
                    // for every other meeting.
                    problem = ex.Message;
                    Program.LogEvent("Transcripts: " + ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    Program.LogEvent("Transcripts: couldn't read the transcript of \"" + meeting.Subject + "\": " + ex.Message);
                }
            }
        }

        static bool IsTeamsLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("teams.live.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Saves the transcript as "2026-11-20 0930 - Weekly planning.txt" (with a number added if there's one already).
        static string Save(CalendarMeeting meeting, string vtt)
        {
            Directory.CreateDirectory(Folder);
            string title = string.IsNullOrWhiteSpace(meeting.Subject) ? "Meeting" : meeting.Subject.Trim();
            foreach (char bad in Path.GetInvalidFileNameChars()) title = title.Replace(bad, ' ');
            if (title.Length > 80) title = title.Substring(0, 80).Trim();
            var start = meeting.StartUtc.ToLocalTime();
            string baseName = start.ToString("yyyy-MM-dd HHmm") + " - " + title;
            string path = Path.Combine(Folder, baseName + ".txt");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, baseName + " (" + i + ").txt");

            var text = new StringBuilder();
            text.AppendLine(meeting.Subject);
            text.AppendLine(start.ToString("dddd, MMMM d, yyyy h:mm tt") + " - " + meeting.EndUtc.ToLocalTime().ToString("h:mm tt"));
            if (!string.IsNullOrWhiteSpace(meeting.Organizer)) text.AppendLine("Organizer: " + meeting.Organizer);
            text.AppendLine("Transcript made by Microsoft Teams, saved by BusyLightz.");
            text.AppendLine();
            text.Append(VttToText(vtt));
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
            return path;
        }

        // WebVTT from Teams -> "[00:01:23] Name: what they said" lines.
        internal static string VttToText(string vtt)
        {
            var text = new StringBuilder();
            string time = null;
            foreach (string raw in (vtt ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("WEBVTT", StringComparison.Ordinal) || line.StartsWith("NOTE", StringComparison.Ordinal)) continue;
                int arrow = line.IndexOf("-->", StringComparison.Ordinal);
                if (arrow > 0)
                {
                    time = line.Substring(0, arrow).Trim();
                    int dot = time.IndexOf('.');
                    if (dot > 0) time = time.Substring(0, dot);
                    continue;
                }
                // A cue's ID line, like "5c6f...-0/0-0".
                if (Regex.IsMatch(line, @"^[0-9a-fA-F-]{20,}(/\d+-\d+)?$")) continue;
                string who = null, words = line;
                var speaker = Regex.Match(line, @"^<v\s+([^>]*)>(.*)$");
                if (speaker.Success) { who = speaker.Groups[1].Value.Trim(); words = speaker.Groups[2].Value; }
                words = System.Net.WebUtility.HtmlDecode(Regex.Replace(words, "<[^>]+>", "")).Trim();
                if (words.Length == 0) continue;
                text.AppendLine((time != null ? "[" + time + "] " : "") + (who != null ? who + ": " : "") + words);
            }
            return text.ToString();
        }

        // Which transcripts have been saved, so each is saved once. Kept in a small file (the newest few hundred).
        void LoadDone()
        {
            lock (sync)
            {
                if (done != null) return;
                done = new HashSet<string>();
                try { if (File.Exists(DonePath)) foreach (string line in File.ReadAllLines(DonePath)) if (line.Length > 0) done.Add(line); }
                catch (IOException) { }
            }
        }

        bool IsDone(string key)
        {
            lock (sync) return done.Contains(key);
        }

        void MarkDone(string key)
        {
            lock (sync)
            {
                done.Add(key);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DonePath));
                    File.AppendAllText(DonePath, key + Environment.NewLine);
                    var lines = File.ReadAllLines(DonePath);
                    if (lines.Length > RememberLines * 2)
                    {
                        var keep = new string[RememberLines];
                        Array.Copy(lines, lines.Length - RememberLines, keep, 0, RememberLines);
                        File.WriteAllLines(DonePath, keep);
                    }
                }
                catch (IOException) { }
            }
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
