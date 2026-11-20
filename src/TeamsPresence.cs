using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

namespace Krabbz.Blynclight
{
    // Reads the user's Teams presence, and shows them as Busy / In a call during phone calls, through Microsoft Graph.
    // Signs in with the system browser (authorization code + PKCE, loopback redirect) as a public client, so there's
    // no client secret. Only a refresh token is kept, encrypted to the Windows user with DPAPI.
    public class TeamsPresence : IDisposable
    {
        const string Scopes = "openid profile offline_access https://graph.microsoft.com/Presence.ReadWrite";
        // Asked for separately, only when the user turns on Meeting Alerts, so the normal sign-in never needs it.
        const string CalendarScope = "https://graph.microsoft.com/Calendars.Read";
        // Reading the transcripts Teams makes of meetings the user was in (Teams Meeting Transcripts). The transcript
        // permission needs approval from an administrator of the user's Microsoft 365.
        const string TranscriptScope = "https://graph.microsoft.com/OnlineMeetings.Read https://graph.microsoft.com/OnlineMeetingTranscript.Read.All";
        const string GraphUsers = "https://graph.microsoft.com/v1.0/users/";
        // Microsoft drops the Busy status on its own after this, so a crash or sleep never leaves it stuck.
        const string BusyExpiration = "PT5M";
        // Renew before the 5-minute expiration while the call continues.
        const int RenewMs = 4 * 60 * 1000;
        const int SignInTimeoutMs = 5 * 60 * 1000;
        // The status message expires on its own after this, so a crash or sleep never leaves it stuck for long.
        const int MessageExpiryMinutes = 60;
        // Renew well before it expires while the lights stay red.
        const int MessageRenewMs = 30 * 60 * 1000;

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        readonly string clientId;
        readonly string tenantId;
        readonly Action<string> reportProblem;
        readonly string tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight", "teams-presence.dat");
        readonly object sync = new object();
        // Serializes Graph calls so a renewal can't land after the call has been cleared.
        readonly object graphGate = new object();
        // Serializes token refreshes so two threads can't race on a rotated refresh token.
        readonly object tokenGate = new object();
        readonly Timer renewTimer;
        string refreshToken;
        string userId;
        string account;
        string accessToken;
        DateTime accessTokenExpires;
        // True once the user has allowed reading their calendar (see GrantCalendarAccess).
        bool calendarGranted;
        // True once the user has allowed reading their Teams meeting transcripts (see GrantTranscriptAccess).
        bool transcriptsGranted;
        bool onCall;
        bool problemReported;
        readonly Timer messageTimer;
        // The status message that should show now (null for none), and the one this app last set.
        string wantedMessage;
        string shownMessage;
        // The user's own status message from before ours, put back afterwards.
        Dictionary<string, object> previousMessage;
        readonly Timer busyTimer;
        // The Teams status the user picked here ("Busy", "Available", or null for automatic), and the one this app set.
        string wantedPresence;
        string shownPresence;

        // reportProblem is called on a background thread with a message the user should see.
        public TeamsPresence(string clientId, string tenantId, Action<string> reportProblem)
        {
            this.clientId = clientId;
            this.tenantId = tenantId;
            this.reportProblem = reportProblem;
            Load();
            renewTimer = new Timer(delegate { Program.Guard("Teams Busy renewal", Renew); }, null, Timeout.Infinite, Timeout.Infinite);
            messageTimer = new Timer(delegate { Program.Guard("Teams status message", UpdateStatusMessage); }, null, Timeout.Infinite, Timeout.Infinite);
            busyTimer = new Timer(delegate { Program.Guard("Teams status update", UpdatePresence); }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool SignedIn { get { lock (sync) return refreshToken != null; } }
        public string Account { get { lock (sync) return account; } }
        public bool CalendarGranted { get { lock (sync) return calendarGranted; } }
        public bool TranscriptsGranted { get { lock (sync) return transcriptsGranted; } }
        string CurrentScopes
        {
            get { return Scopes + (CalendarGranted ? " " + CalendarScope : "") + (TranscriptsGranted ? " " + TranscriptScope : ""); }
        }

        string Authority { get { return "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/"; } }

        // Opens the browser for Microsoft sign-in and blocks until it finishes. Throws with a readable message on failure.
        public void SignIn()
        {
            SignInWith(Scopes);
            lock (sync) { calendarGranted = false; transcriptsGranted = false; }
            Save();
        }

        // Signs in again, this time also asking Microsoft to let BusyLightz read the user's calendar (for Meeting Alerts).
        // Only the user's own calendar, read-only. Throws with a readable message if it isn't allowed.
        public void GrantCalendarAccess()
        {
            SignInWith(Scopes + " " + CalendarScope + (TranscriptsGranted ? " " + TranscriptScope : ""));
            lock (sync) calendarGranted = true;
            Save();
            problemReported = false;
        }

        // Signs in again, this time also asking Microsoft to let BusyLightz read the transcripts of the user's Teams
        // meetings (read-only). Needs approval from an administrator of the user's Microsoft 365. Throws with a readable
        // message if it isn't allowed.
        public void GrantTranscriptAccess()
        {
            SignInWith(Scopes + (CalendarGranted ? " " + CalendarScope : "") + " " + TranscriptScope);
            lock (sync) transcriptsGranted = true;
            Save();
            problemReported = false;
        }

        void SignInWith(string scopes)
        {
            string verifier = Base64Url(RandomBytes(32));
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomBytes(16));

            // Browsers may resolve localhost to either loopback address, so listen on both.
            var listeners = new List<TcpListener> { new TcpListener(IPAddress.Loopback, 0) };
            listeners[0].Start();
            int port = ((IPEndPoint)listeners[0].LocalEndpoint).Port;
            try
            {
                try
                {
                    var v6 = new TcpListener(IPAddress.IPv6Loopback, port);
                    v6.Start();
                    listeners.Add(v6);
                }
                catch (SocketException) { } // No IPv6, or port taken there; IPv4 is enough.

                string redirectUri = "http://localhost:" + port;
                Process.Start(Authority + "authorize" +
                    "?client_id=" + Uri.EscapeDataString(clientId) +
                    "&response_type=code&response_mode=query" +
                    "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                    "&scope=" + Uri.EscapeDataString(scopes) +
                    "&state=" + state +
                    "&code_challenge=" + challenge + "&code_challenge_method=S256" +
                    "&prompt=select_account");

                NameValueCollection reply = WaitForRedirect(listeners, SignInTimeoutMs);
                if (reply == null) throw new InvalidOperationException("Sign-in timed out.");
                if (reply["state"] != state) throw new InvalidOperationException("Sign-in reply didn't match this request.");
                if (reply["error"] != null) throw new InvalidOperationException("Sign-in failed: " + (reply["error_description"] ?? reply["error"]));

                var tokens = RequestTokens(new NameValueCollection
                {
                    { "grant_type", "authorization_code" },
                    { "code", reply["code"] },
                    { "redirect_uri", redirectUri },
                    { "code_verifier", verifier },
                }, scopes);
                var claims = JwtPayload((string)tokens["id_token"]);
                lock (sync)
                {
                    userId = (string)claims["oid"];
                    object name;
                    account = claims.TryGetValue("preferred_username", out name) ? name as string : null;
                }
                StoreTokens(tokens);
                problemReported = false;
            }
            finally
            {
                foreach (var listener in listeners) listener.Stop();
            }
        }

        // Clears any Busy status and status message this app set and forgets the stored token.
        public void SignOut()
        {
            bool wasOnCall;
            lock (sync) { wasOnCall = onCall; onCall = false; }
            renewTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (wasOnCall) TryClear();
            TakeDownStatusMessage();
            TakeDownPresence();
            lock (sync) { refreshToken = null; accessToken = null; userId = null; account = null; calendarGranted = false; transcriptsGranted = false; }
            try { File.Delete(tokenPath); } catch (IOException) { }
        }

        public void CallStarted()
        {
            lock (sync)
            {
                if (refreshToken == null || onCall) return;
                onCall = true;
            }
            renewTimer.Change(0, RenewMs);
        }

        public void CallEnded()
        {
            lock (sync)
            {
                if (!onCall) return;
                onCall = false;
            }
            renewTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Program.Background("Clearing Teams Busy", TryClear);
        }

        void Renew()
        {
            lock (graphGate)
            {
                lock (sync) if (!onCall) return;
                try
                {
                    PostPresence("setPresence", new Dictionary<string, object>
                    {
                        { "sessionId", clientId },
                        { "availability", "Busy" },
                        { "activity", "InACall" },
                        { "expirationDuration", BusyExpiration },
                    });
                    problemReported = false;
                }
                catch (Exception ex) { Report("Couldn't set Teams to Busy: " + ex.Message); }
            }
        }

        void TryClear()
        {
            lock (graphGate)
            {
                try { PostPresence("clearPresence", new Dictionary<string, object> { { "sessionId", clientId } }); }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (response == null || response.StatusCode != HttpStatusCode.NotFound) // 404: already expired.
                        Report("Couldn't clear Teams Busy status (it expires on its own within 5 minutes): " + ex.Message);
                }
                catch (Exception ex) { Report("Couldn't clear Teams Busy status (it expires on its own within 5 minutes): " + ex.Message); }
            }
        }

        // Shows text as the Teams status message, or null to take it away again. Runs in the background.
        public void ShowStatusMessage(string text)
        {
            lock (sync)
            {
                if (text == wantedMessage) return;
                wantedMessage = text;
            }
            messageTimer.Change(0, text == null ? Timeout.Infinite : MessageRenewMs);
        }

        void UpdateStatusMessage()
        {
            lock (graphGate)
            {
                string wanted, shown;
                lock (sync)
                {
                    if (refreshToken == null) return;
                    wanted = wantedMessage;
                    shown = shownMessage;
                }
                try
                {
                    if (wanted != null)
                    {
                        if (shown == null)
                        {
                            // Remember the user's own message to put back later, unless it's a leftover of ours.
                            var current = CurrentStatusMessage();
                            previousMessage = current != null && ContentOf(current) != wanted ? current : null;
                        }
                        PostStatusMessage(new Dictionary<string, object>
                        {
                            { "message", new Dictionary<string, object> { { "content", wanted }, { "contentType", "text" } } },
                            { "expiryDateTime", GraphTime(DateTime.UtcNow.AddMinutes(MessageExpiryMinutes)) },
                        });
                        lock (sync) shownMessage = wanted;
                    }
                    else if (shown != null)
                    {
                        lock (sync) shownMessage = null;
                        RestoreStatusMessage(shown);
                    }
                    problemReported = false;
                }
                catch (Exception ex) { Report("Couldn't update your Teams status message: " + ex.Message); }
            }
        }

        // Puts back the user's own status message (or clears ours) now. Used when signing out or exiting.
        void TakeDownStatusMessage()
        {
            string shown;
            lock (sync) { wantedMessage = null; shown = shownMessage; shownMessage = null; }
            messageTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (shown == null) return;
            lock (graphGate)
            {
                try { RestoreStatusMessage(shown); }
                catch (Exception ex) { Report("Couldn't clear your Teams status message (it expires on its own within an hour): " + ex.Message); }
            }
        }

        // shown is the message this app set. Leaves things alone if the user changed it in Teams meanwhile.
        void RestoreStatusMessage(string shown)
        {
            var previous = previousMessage;
            previousMessage = null;
            if (previous != null && Expired(previous)) previous = null;
            var current = CurrentStatusMessage();
            if (current != null && !string.Equals(ContentOf(current), shown.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Log("Left the Teams status message alone, since it was changed in Teams: " + ContentOf(current));
                return;
            }
            PostStatusMessage(previous ?? new Dictionary<string, object>
            {
                { "message", new Dictionary<string, object> { { "content", "" }, { "contentType", "text" } } },
            });
        }

        // Sets the user's Teams status as if they had picked it there ("Busy" or "Available"), or null to put Teams
        // back to automatic. Runs in the background. It expires on its own after an hour, renewed while still wanted.
        public void ShowPresence(string availability)
        {
            lock (sync)
            {
                if (availability == wantedPresence) return;
                wantedPresence = availability;
            }
            busyTimer.Change(0, availability != null ? MessageRenewMs : Timeout.Infinite);
        }

        void UpdatePresence()
        {
            lock (graphGate)
            {
                string want, shown;
                lock (sync)
                {
                    if (refreshToken == null) return;
                    want = wantedPresence;
                    shown = shownPresence;
                }
                try
                {
                    if (want != null)
                    {
                        PostPresence("setUserPreferredPresence", new Dictionary<string, object>
                        {
                            { "availability", want },
                            { "activity", want },
                            { "expirationDuration", "PT" + MessageExpiryMinutes + "M" },
                        });
                        lock (sync) shownPresence = want;
                    }
                    else if (shown != null)
                    {
                        lock (sync) shownPresence = null;
                        PostPresence("clearUserPreferredPresence", new Dictionary<string, object>());
                    }
                    problemReported = false;
                }
                catch (Exception ex) { Report("Couldn't set your Teams status to " + (want ?? "automatic") + ": " + ex.Message); }
            }
        }

        // Puts Teams back to automatic now, if this app set a status. Used when signing out or exiting.
        void TakeDownPresence()
        {
            string shown;
            lock (sync) { wantedPresence = null; shown = shownPresence; shownPresence = null; }
            busyTimer.Change(Timeout.Infinite, Timeout.Infinite);
            if (shown == null) return;
            lock (graphGate)
            {
                try { PostPresence("clearUserPreferredPresence", new Dictionary<string, object>()); }
                catch (Exception ex) { Report("Couldn't put your Teams status back to automatic (it goes back on its own within an hour): " + ex.Message); }
            }
        }

        // The user's current status message ({message, expiryDateTime}), or null when there isn't one.
        Dictionary<string, object> CurrentStatusMessage()
        {
            object value;
            var status = ReadPresence().TryGetValue("statusMessage", out value) ? value as Dictionary<string, object> : null;
            if (status == null || string.IsNullOrEmpty(ContentOf(status))) return null;
            var kept = new Dictionary<string, object> { { "message", status["message"] } };
            object expiry;
            if (status.TryGetValue("expiryDateTime", out expiry) && expiry != null) kept["expiryDateTime"] = expiry;
            return kept;
        }

        // True when a saved status message's expiry (in UTC) has passed, so it shouldn't be put back.
        static bool Expired(Dictionary<string, object> status)
        {
            object value;
            var expiry = status.TryGetValue("expiryDateTime", out value) ? value as Dictionary<string, object> : null;
            if (expiry == null || !"UTC".Equals(expiry["timeZone"] as string, StringComparison.OrdinalIgnoreCase)) return false;
            DateTime at;
            return DateTime.TryParse(expiry["dateTime"] as string, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out at)
                && at <= DateTime.UtcNow;
        }

        // The message as plain text. Teams can hand back what this app set as HTML (e.g. "<p>In a meeting</p>"),
        // so tags and entities are stripped before comparing.
        static string ContentOf(Dictionary<string, object> status)
        {
            object message, content;
            var inner = status.TryGetValue("message", out message) ? message as Dictionary<string, object> : null;
            if (inner == null || !inner.TryGetValue("content", out content)) return null;
            string text = System.Text.RegularExpressions.Regex.Replace((content as string) ?? "", "<[^>]*>", "");
            return HttpUtility.HtmlDecode(text).Replace(' ', ' ').Trim();
        }

        void PostStatusMessage(Dictionary<string, object> statusMessage)
        {
            PostPresence("setStatusMessage", new Dictionary<string, object> { { "statusMessage", statusMessage } });
        }

        static Dictionary<string, object> GraphTime(DateTime utc)
        {
            return new Dictionary<string, object> { { "dateTime", utc.ToString("yyyy-MM-ddTHH:mm:ss") }, { "timeZone", "UTC" } };
        }

        // Shows one message per run of failures, so a dropped network doesn't pop up a balloon every renewal.
        // Every failure goes to app.log, so a missed balloon can still be looked up.
        void Report(string message)
        {
            Log(message);
            if (problemReported) return;
            problemReported = true;
            reportProblem(message);
        }

        void PostPresence(string action, Dictionary<string, object> body)
        {
            string token = AccessToken();
            string user;
            lock (sync) user = userId;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var web = new GraphWebClient())
            {
                web.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                web.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                web.Encoding = Encoding.UTF8; // Custom status messages can hold any characters.
                web.UploadString(GraphUsers + user + "/presence/" + action, "POST", Json.Serialize(body));
            }
        }

        string AccessToken()
        {
            lock (tokenGate)
            {
                string refresh;
                lock (sync)
                {
                    if (accessToken != null && DateTime.UtcNow < accessTokenExpires) return accessToken;
                    if (refreshToken == null) throw new InvalidOperationException("Not signed in to Microsoft.");
                    refresh = refreshToken;
                }
                try
                {
                    try { StoreTokens(RequestTokens(RefreshForm(refresh), CurrentScopes)); }
                    catch (TokenException extraEx)
                    {
                        // Calendar or transcript access was taken back (or never took): carry on without it, so Teams status
                        // keeps working. Transcripts go first (they need an administrator's approval), keeping the calendar.
                        if (!CalendarGranted && !TranscriptsGranted) throw;
                        Log("Calendar or transcript access is no longer allowed (" + extraEx.Error + "); carrying on without it.");
                        bool recovered = false;
                        if (TranscriptsGranted && CalendarGranted)
                        {
                            lock (sync) transcriptsGranted = false;
                            Save();
                            try { StoreTokens(RequestTokens(RefreshForm(refresh), CurrentScopes)); recovered = true; }
                            catch (TokenException) { }
                        }
                        if (!recovered)
                        {
                            lock (sync) { calendarGranted = false; transcriptsGranted = false; }
                            Save();
                            StoreTokens(RequestTokens(RefreshForm(refresh), Scopes));
                        }
                    }
                }
                catch (TokenException ex)
                {
                    // The sign-in was revoked or expired; the user has to sign in again from the tray menu.
                    // Microsoft's description (with its AADSTS code) says why, so keep it in app.log.
                    Log("Microsoft token refresh failed (" + ex.Error + "): " + ex.Message);
                    if (ex.Error == "invalid_grant") SignOut();
                    throw;
                }
                lock (sync) return accessToken;
            }
        }

        // Returns the user's Teams availability (e.g. "Available", "Busy", "DoNotDisturb", "Away"),
        // or null if it couldn't be read (the problem is reported once).
        public string ReadAvailability()
        {
            try
            {
                // Take off a status this app set first, or it would be read back as the user's own.
                bool pendingClear;
                lock (sync) pendingClear = shownPresence != null && wantedPresence == null;
                if (pendingClear) UpdatePresence();
                var result = ReadPresence();
                problemReported = false;
                return result["availability"] as string;
            }
            catch (Exception ex)
            {
                Report("Couldn't read your Teams status: " + ex.Message);
                return null;
            }
        }

        // The user's calendar events that overlap the time from fromUtc to toUtc, soonest first, from Microsoft Graph.
        // Needs calendar access (GrantCalendarAccess). Throws with a readable message if it can't be read.
        public List<CalendarMeeting> GetMeetings(DateTime fromUtc, DateTime toUtc)
        {
            if (!CalendarGranted) throw new InvalidOperationException("Calendar access isn't allowed yet.");
            string token = AccessToken();
            string url = "https://graph.microsoft.com/v1.0/me/calendarView" +
                "?startDateTime=" + fromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                "&endDateTime=" + toUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                "&$orderby=start/dateTime&$top=50" +
                "&$select=id,subject,start,end,isAllDay,isCancelled,isOnlineMeeting,onlineMeeting,location,organizer,responseStatus";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string body;
            try
            {
                using (var web = new GraphWebClient())
                {
                    web.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                    web.Headers["Prefer"] = "outlook.timezone=\"UTC\"";
                    web.Encoding = Encoding.UTF8;
                    body = web.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized))
                    throw new InvalidOperationException("Microsoft didn't allow reading your calendar. Use Allow access in Settings > Meeting Alerts.");
                throw;
            }

            var meetings = new List<CalendarMeeting>();
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            object values;
            if (root == null || !root.TryGetValue("value", out values) || !(values is System.Collections.IEnumerable)) return meetings;
            foreach (object item in (System.Collections.IEnumerable)values)
            {
                var e = item as Dictionary<string, object>;
                if (e == null) continue;
                var online = Child(e, "onlineMeeting");
                var meeting = new CalendarMeeting
                {
                    Id = Text(e, "id"),
                    Subject = Text(e, "subject"),
                    StartUtc = GraphDate(Child(e, "start")),
                    EndUtc = GraphDate(Child(e, "end")),
                    IsAllDay = Flag(e, "isAllDay"),
                    IsCancelled = Flag(e, "isCancelled"),
                    Location = Text(Child(e, "location"), "displayName"),
                    Organizer = Text(Child(Child(e, "organizer"), "emailAddress"), "name"),
                    Response = Text(Child(e, "responseStatus"), "response"),
                    JoinUrl = Flag(e, "isOnlineMeeting") ? Text(online, "joinUrl") : "",
                };
                if (meeting.StartUtc != DateTime.MinValue) meetings.Add(meeting);
            }
            return meetings;
        }

        // One Teams meeting transcript: its ID and when Teams made it.
        public class TranscriptInfo
        {
            public string Id;
            public DateTime CreatedUtc;
        }

        // What a Graph call that failed said.
        public class GraphException : Exception
        {
            public readonly int Status;
            public readonly string Body;
            public GraphException(int status, string body, string message) : base(message) { Status = status; Body = body ?? ""; }
        }

        // A Graph GET with the user's sign-in; throws GraphException (with the status and body) if Microsoft says no.
        string GraphGetText(string url, string accept)
        {
            string token = AccessToken();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try
            {
                using (var web = new GraphWebClient())
                {
                    web.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                    if (accept != null) web.Headers[HttpRequestHeader.Accept] = accept;
                    web.Encoding = Encoding.UTF8;
                    return web.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                string body = "";
                if (response != null)
                {
                    try { using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd(); }
                    catch (Exception) { }
                }
                throw new GraphException(response == null ? 0 : (int)response.StatusCode, body, ex.Message);
            }
        }

        // The Graph ID of a Teams meeting from the link to join it, or null. A meeting the user organized is found by its link;
        // one someone else organized has its ID worked out from the link, as Teams builds it.
        public string FindOnlineMeetingId(string joinUrl)
        {
            if (!TranscriptsGranted) throw new InvalidOperationException("Transcript access isn't allowed yet.");
            try
            {
                string body = GraphGetText("https://graph.microsoft.com/v1.0/me/onlineMeetings?$select=id&$filter=" +
                    Uri.EscapeDataString("JoinWebUrl eq '" + joinUrl.Replace("'", "''") + "'"), null);
                var root = Json.DeserializeObject(body) as Dictionary<string, object>;
                object values;
                if (root != null && root.TryGetValue("value", out values) && values is System.Collections.IEnumerable)
                    foreach (object item in (System.Collections.IEnumerable)values)
                    {
                        string id = Text(item as Dictionary<string, object>, "id");
                        if (id.Length > 0) return id;
                    }
            }
            catch (GraphException ex)
            {
                // Not the user's own meeting (or not found that way): fall back to the link.
                if (ex.Status != 400 && ex.Status != 403 && ex.Status != 404) throw ExplainTranscriptError(ex);
            }
            return MeetingIdFromJoinUrl(joinUrl);
        }

        // "https://teams.microsoft.com/l/meetup-join/19%3ameeting_XXXX%40thread.v2/0?context={"Tid":"...","Oid":"<organizer>"}"
        // -> the Graph meeting ID, which is "1*<organizer>*0**<thread>" encoded as base64.
        static string MeetingIdFromJoinUrl(string joinUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out uri)) return null;
            var thread = Regex.Match(Uri.UnescapeDataString(uri.AbsolutePath), @"meetup-join/(19:meeting_[^/]+@thread\.v2)");
            var organizer = Regex.Match(Uri.UnescapeDataString(uri.Query), "\"Oid\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"");
            if (!thread.Success || !organizer.Success) return null;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("1*" + organizer.Groups[1].Value + "*0**" + thread.Groups[1].Value));
        }

        // The transcripts Teams has made of a meeting, oldest first. Empty if there are none (yet).
        public List<TranscriptInfo> ListTranscripts(string meetingId)
        {
            var found = new List<TranscriptInfo>();
            string body;
            try { body = GraphGetText("https://graph.microsoft.com/v1.0/me/onlineMeetings/" + Uri.EscapeDataString(meetingId) + "/transcripts", null); }
            catch (GraphException ex)
            {
                if (ex.Status == 404) return found; // No such meeting, or no transcript.
                throw ExplainTranscriptError(ex);
            }
            var root = Json.DeserializeObject(body) as Dictionary<string, object>;
            object values;
            if (root == null || !root.TryGetValue("value", out values) || !(values is System.Collections.IEnumerable)) return found;
            foreach (object item in (System.Collections.IEnumerable)values)
            {
                var entry = item as Dictionary<string, object>;
                string id = Text(entry, "id");
                if (id.Length == 0) continue;
                DateTime created;
                if (!DateTime.TryParse(Text(entry, "createdDateTime"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created)) created = DateTime.MinValue;
                found.Add(new TranscriptInfo { Id = id, CreatedUtc = created });
            }
            found.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
            return found;
        }

        // The text of one transcript, as WebVTT (times, speakers and what they said).
        public string GetTranscriptText(string meetingId, string transcriptId)
        {
            try
            {
                return GraphGetText("https://graph.microsoft.com/v1.0/me/onlineMeetings/" + Uri.EscapeDataString(meetingId) +
                    "/transcripts/" + Uri.EscapeDataString(transcriptId) + "/content", "text/vtt");
            }
            catch (GraphException ex) { throw ExplainTranscriptError(ex); }
        }

        // Microsoft's refusal, in words the user can act on.
        static Exception ExplainTranscriptError(GraphException ex)
        {
            if (ex.Status == 403 && ex.Body.IndexOf("GraphAccessToTranscriptsDisabled", StringComparison.OrdinalIgnoreCase) >= 0)
                return new InvalidOperationException("Your organization has turned off access to transcripts for apps (a Teams admin setting).");
            if (ex.Status == 401 || ex.Status == 403)
                return new InvalidOperationException("Microsoft didn't allow reading transcripts. An administrator needs to approve OnlineMeetingTranscript.Read.All for BusyLightz.");
            return new InvalidOperationException("Microsoft returned an error (" + ex.Status + ") reading a transcript.");
        }

        static Dictionary<string, object> Child(Dictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        static string Text(Dictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        static bool Flag(Dictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        // Graph's {"dateTime": "2026-11-20T14:00:00.0000000", "timeZone": "UTC"}, asked for in UTC; MinValue if unreadable.
        static DateTime GraphDate(Dictionary<string, object> value)
        {
            DateTime parsed;
            string text = Text(value, "dateTime");
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return parsed;
            return DateTime.MinValue;
        }

        Dictionary<string, object> ReadPresence()
        {
            string token = AccessToken();
            string user;
            lock (sync) user = userId;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var web = new GraphWebClient())
            {
                web.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                web.Encoding = Encoding.UTF8;
                return (Dictionary<string, object>)Json.DeserializeObject(web.DownloadString(GraphUsers + user + "/presence"));
            }
        }

        static NameValueCollection RefreshForm(string refresh)
        {
            return new NameValueCollection { { "grant_type", "refresh_token" }, { "refresh_token", refresh } };
        }

        Dictionary<string, object> RequestTokens(NameValueCollection form, string scopes)
        {
            form["client_id"] = clientId;
            form["scope"] = scopes;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try
            {
                using (var web = new GraphWebClient())
                    return (Dictionary<string, object>)Json.DeserializeObject(Encoding.UTF8.GetString(web.UploadValues(Authority + "token", form)));
            }
            catch (WebException ex)
            {
                if (ex.Response == null) throw;
                string body;
                using (var reader = new StreamReader(ex.Response.GetResponseStream())) body = reader.ReadToEnd();
                var error = Json.DeserializeObject(body) as Dictionary<string, object>;
                if (error == null || !error.ContainsKey("error")) throw;
                throw new TokenException((string)error["error"],
                    error.ContainsKey("error_description") ? (string)error["error_description"] : (string)error["error"]);
            }
        }

        void StoreTokens(Dictionary<string, object> tokens)
        {
            object rotated, expiresIn;
            lock (sync)
            {
                accessToken = (string)tokens["access_token"];
                int seconds = tokens.TryGetValue("expires_in", out expiresIn) ? Convert.ToInt32(expiresIn) : 3600;
                // Refresh a couple of minutes early so a request never goes out with a token about to expire.
                accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
                if (tokens.TryGetValue("refresh_token", out rotated)) refreshToken = (string)rotated;
            }
            Save();
        }

        // Saves the refresh token and user ID, encrypted so only this Windows user can read them.
        void Save()
        {
            string json;
            lock (sync)
            {
                if (refreshToken == null) return;
                json = Json.Serialize(new Dictionary<string, object>
                {
                    { "refresh_token", refreshToken }, { "oid", userId }, { "account", account },
                    { "client_id", clientId }, { "tenant_id", tenantId }, { "calendar", calendarGranted }, { "transcripts", transcriptsGranted },
                });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath));
            File.WriteAllBytes(tokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
        }

        void Load()
        {
            try
            {
                if (!File.Exists(tokenPath)) return;
                string json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(tokenPath), null, DataProtectionScope.CurrentUser));
                var saved = (Dictionary<string, object>)Json.DeserializeObject(json);
                // A sign-in made with a different organization's app doesn't carry over; sign in again.
                object savedClient, savedTenant;
                if ((saved.TryGetValue("client_id", out savedClient) && !string.Equals(savedClient as string, clientId, StringComparison.OrdinalIgnoreCase))
                    || (saved.TryGetValue("tenant_id", out savedTenant) && !string.Equals(savedTenant as string, tenantId, StringComparison.OrdinalIgnoreCase)))
                {
                    Log("Saved Microsoft sign-in was made with a different Teams app; sign in again.");
                    return;
                }
                refreshToken = saved["refresh_token"] as string;
                userId = saved["oid"] as string;
                account = saved["account"] as string;
                object calendar;
                calendarGranted = saved.TryGetValue("calendar", out calendar) && calendar is bool && (bool)calendar;
                object transcripts;
                transcriptsGranted = saved.TryGetValue("transcripts", out transcripts) && transcripts is bool && (bool)transcripts;
            }
            catch (Exception ex)
            {
                // Unreadable (e.g. copied from another user); sign in again.
                refreshToken = null;
                Log("Couldn't read the saved Microsoft sign-in: " + ex.Message);
            }
        }

        void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(tokenPath), "app.log"), DateTime.Now.ToString("s") + "  " + message + Environment.NewLine); }
            catch (IOException) { }
        }

        // Accepts connections on the loopback listeners until the browser arrives with the sign-in reply.
        // Returns the reply's query parameters, or null on timeout.
        internal static NameValueCollection WaitForRedirect(List<TcpListener> listeners, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var listener in listeners)
                {
                    if (!listener.Pending()) continue;
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 5000;
                        string requestLine;
                        try { requestLine = new StreamReader(stream, Encoding.ASCII).ReadLine() ?? ""; }
                        catch (IOException) { continue; }
                        // "GET /?code=...&state=... HTTP/1.1"
                        string[] parts = requestLine.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "";
                        int q = target.IndexOf('?');
                        var query = HttpUtility.ParseQueryString(q >= 0 ? target.Substring(q + 1) : "");
                        bool isReply = query["state"] != null;
                        Respond(stream, isReply);
                        if (isReply) return query;
                    }
                }
                Thread.Sleep(100);
            }
            return null;
        }

        static void Respond(Stream stream, bool isReply)
        {
            string html = isReply
                ? "<html><body style=\"font-family:Segoe UI,sans-serif;padding:40px\"><h2>KrabbzDev-BusyLightz2.0</h2>" +
                  "<p>Sign-in finished. You can close this tab.</p></body></html>"
                : "";
            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] head = Encoding.ASCII.GetBytes((isReply ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") +
                "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            try
            {
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
            }
            catch (IOException) { } // Browser closed the connection early.
        }

        static Dictionary<string, object> JwtPayload(string jwt)
        {
            string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return (Dictionary<string, object>)Json.DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        }

        static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public void Dispose()
        {
            renewTimer.Dispose();
            bool wasOnCall;
            lock (sync) { wasOnCall = onCall; onCall = false; }
            if (wasOnCall) TryClear();
            TakeDownStatusMessage();
            TakeDownPresence();
            messageTimer.Dispose();
            busyTimer.Dispose();
        }

        class TokenException : Exception
        {
            public readonly string Error;
            public TokenException(string error, string description) : base(description) { Error = error; }
        }

        // WebClient waits up to 100 seconds by default. Exiting and signing out run these calls on the window's
        // thread, so a slow network would freeze the app for that long.
        class GraphWebClient : WebClient
        {
            const int TimeoutMs = 20000;

            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                request.Timeout = TimeoutMs;
                var http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = TimeoutMs;
                return request;
            }
        }
    }
}
