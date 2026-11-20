using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Krabbz.Blynclight
{
    // The main window's layouts, as saved in the settings, with the names Settings shows for them.
    public static class WindowStyles
    {
        public const string Orbit = "Orbit", ColorBlock = "ColorBlock", Fluent = "Fluent";
        public static readonly string[] All = { Orbit, ColorBlock, Fluent };
        public static readonly string[] Names = { "Orbit", "Color block", "Fluent list" };
    }

    // User settings, stored under HKCU\Software\Krabbz9999\Blynclight App Control. On/off settings default to on unless noted.
    public static class AppSettings
    {
        const string SettingsKey = @"Software\Krabbz9999\Blynclight App Control";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "Krabbz9999 Blynclight App Control";

        public static bool ShowNotifications { get { return Get("ShowNotifications"); } set { Set("ShowNotifications", value); } }
        public static bool HotkeysEnabled { get { return Get("HotkeysEnabled"); } set { Set("HotkeysEnabled", value); } }
        public static bool AutoUpdate { get { return Get("AutoUpdate"); } set { Set("AutoUpdate", value); } }
        public static bool CallsTurnRed { get { return Get("CallsTurnRed"); } set { Set("CallsTurnRed", value); } }
        public static bool OverrideTimesOut { get { return Get("OverrideTimesOut"); } set { Set("OverrideTimesOut", value); } }
        // How long Red, Green and On A Call last before going back to Follow Teams Status.
        public static readonly int[] OverrideMinuteChoices = { 5, 10, 15, 30, 60, 120, 240 };
        public const int DefaultOverrideMinutes = 15;
        public static int OverrideMinutes
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    object value = key == null ? null : key.GetValue("OverrideMinutes");
                    return value is int && (int)value > 0 ? (int)value : DefaultOverrideMinutes;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("OverrideMinutes", value, RegistryValueKind.DWord);
            }
        }
        public static bool FlashOnCalls { get { return Get("FlashOnCalls"); } set { Set("FlashOnCalls", value); } }
        public static bool TeamsBusyOnCalls { get { return Get("TeamsBusyOnCalls"); } set { Set("TeamsBusyOnCalls", value); } }
        // Show Do Not Disturb in Teams wherever the app would have shown Busy. On unless changed.
        public static bool TeamsDoNotDisturb { get { return Get("TeamsDoNotDisturb"); } set { Set("TeamsDoNotDisturb", value); } }
        public static bool TeamsBusyWhenPicked { get { return Get("TeamsBusyWhenPicked"); } set { Set("TeamsBusyWhenPicked", value); } }
        public static bool StartWithWindows { get { return Get("StartWithWindows"); } set { Set("StartWithWindows", value); ApplyStartup(); } }
        public static bool OpenWindowAtSignIn { get { return Get("OpenWindowAtSignIn"); } set { Set("OpenWindowAtSignIn", value); ApplyStartup(); } }

        // Makes the sign-in startup entry match the settings. Called at every launch too, because each install or
        // update rewrites the entry the installer owns.
        // The all-users install starts the app for everyone from its own entry (with --autostart, which checks these
        // settings), so it only removes a just-for-me copy's entry.
        public static void ApplyStartup()
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (AllUsersInstall.IsThisCopy || !StartWithWindows) run.DeleteValue(RunValue, false);
                else run.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"" + (OpenWindowAtSignIn ? "" : " --tray"));
            }
        }

        // Teams status message shown while the lights are red.
        public static readonly string[] TeamsStatusMessageChoices =
            { "On a video call", "In a meeting", "On a phone call", "Heads down, please message me", "Back in a few minutes" };
        public const int TeamsStatusMessageMaxLength = 280;
        // Added to the end of every status message sent to Teams, so people know not to expect a quick answer.
        public const string TeamsStatusMessageSuffix = " - responses may be delayed";
        // The message as Teams gets it: the chosen text plus the suffix, cut short if needed to fit Teams' limit.
        public static string WithDelayNote(string message)
        {
            message = (message ?? "").Trim();
            if (message.EndsWith(TeamsStatusMessageSuffix.Trim(), StringComparison.OrdinalIgnoreCase)) return message;
            int room = TeamsStatusMessageMaxLength - TeamsStatusMessageSuffix.Length;
            if (message.Length > room) message = message.Substring(0, room).TrimEnd();
            return message + TeamsStatusMessageSuffix;
        }
        public static bool TeamsStatusMessageOn { get { return Get("TeamsStatusMessageOn"); } set { Set("TeamsStatusMessageOn", value); } }
        // How the main window is laid out: one of WindowStyles. Orbit unless changed.
        public static string WindowStyle
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    string value = key == null ? null : key.GetValue("WindowStyle") as string;
                    return Array.IndexOf(WindowStyles.All, value) >= 0 ? value : WindowStyles.Orbit;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("WindowStyle", value, RegistryValueKind.String);
            }
        }
        // Light (the default) or Dark colors for the app's windows.
        public static bool DarkMode { get { return Get("DarkMode", false); } set { Set("DarkMode", value); } }
        // How long On a call shows each of its two flash colors: 0.1 to 2 seconds.
        public const int MinCallFlashMs = 100, MaxCallFlashMs = 2000, DefaultCallFlashMs = 1000;
        public static int CallFlashMs
        {
            get { return Ranged("CallFlashMs", MinCallFlashMs, MaxCallFlashMs, DefaultCallFlashMs); }
            set { SetInt("CallFlashMs", value); }
        }
        // Light brightness, in percent: 5 to 100.
        public const int MinBrightness = 5;
        public static int Brightness
        {
            get { return Ranged("Brightness", MinBrightness, 100, 100); }
            set { SetInt("Brightness", value); }
        }

        // The color for each status, by name from BlyncDevice.ColorNames: setting name, label, default, and the
        // state Light Control shows on the lights while it's being picked.
        public static readonly string[][] LightColorRoles =
        {
            new[] { "BusyColor", "Busy (and Teams: Busy)", "Red", "red" },
            new[] { "AvailableColor", "Available (and Teams: Available)", "Green", "green" },
            new[] { "AwayColor", "Teams: Away / Be right back", "Yellow", "yellow" },
            new[] { "DndColor", "Teams: Do not disturb", "Purple", "purple" },
            new[] { "CallColor1", "On a call: first flash color", "Red", "call-flash" },
            new[] { "CallColor2", "On a call: second flash color", "Blue", "call-flash" },
        };

        public static string LightColorName(string setting)
        {
            string fallback = null;
            foreach (var role in LightColorRoles) if (role[0] == setting) fallback = role[2];
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                string value = key == null ? null : key.GetValue(setting) as string;
                return Array.IndexOf(BlyncDevice.ColorNames, value) >= 0 ? value : fallback;
            }
        }

        public static void SetLightColorName(string setting, string name)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(setting, name, RegistryValueKind.String);
        }

        // A saved number if it's from min to max, otherwise the default.
        static int Ranged(string name, int min, int max, int defaultValue)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object value = key == null ? null : key.GetValue(name);
                return value is int && (int)value >= min && (int)value <= max ? (int)value : defaultValue;
            }
        }

        static void SetInt(string name, int value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(name, value, RegistryValueKind.DWord);
        }

        // Leaves USB lights alone, for Teams status and call detection only. Off unless changed.
        public static bool UsbLightsDisabled { get { return Get("UsbLightsDisabled", false); } set { Set("UsbLightsDisabled", value); } }
        // Meeting alerts: a pop-up shortly before each calendar meeting (needs calendar access, allowed in Settings). On unless changed.
        public static bool MeetingAlertsOn { get { return Get("MeetingAlertsOn"); } set { Set("MeetingAlertsOn", value); } }
        // When the user was last reminded, in a notice, that Meeting Alerts still needs calendar access. A reminder comes
        // at most once a week, and only while alerts are on and calendar access isn't allowed.
        const int ReminderDays = 7;
        public static bool MeetingAlertsReminderDue
        {
            get
            {
                DateTime last;
                string saved = GetString("MeetingAlertsReminded");
                if (string.IsNullOrEmpty(saved) || !DateTime.TryParse(saved, null, System.Globalization.DateTimeStyles.RoundtripKind, out last)) return true;
                return DateTime.UtcNow - last.ToUniversalTime() >= TimeSpan.FromDays(ReminderDays);
            }
        }
        public static void MarkMeetingAlertsReminded() { SetString("MeetingAlertsReminded", DateTime.UtcNow.ToString("o")); }
        // Hardware IDs (VID_xxxx&PID_xxxx, comma separated) of other two-button keys to treat as a K2, added with Find my
        // key on the K2 page.
        public static string K2ExtraIds { get { return GetString("K2ExtraIds") ?? ""; } set { SetString("K2ExtraIds", value ?? ""); } }

        public static System.Collections.Generic.List<string> K2ExtraIdList()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (string part in K2ExtraIds.Split(','))
            {
                string id = part.Trim().ToUpperInvariant();
                if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        // Keys a two-button key sends instead of mouse clicks, by hardware ID: "VID_xxxx&PID_yyyy=leftKey/rightKey;..." (virtual
        // key numbers: left, right, and the key sent for both together; 0 for not set). Set with Use as Left, Right and Both in Remap Custom Keys.
        public static string K2KeyMapText { get { return GetString("K2KeyMap") ?? ""; } set { SetString("K2KeyMap", value ?? ""); } }

        public static System.Collections.Generic.Dictionary<string, int[]> K2KeyMap()
        {
            var map = new System.Collections.Generic.Dictionary<string, int[]>();
            foreach (string entry in K2KeyMapText.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string id = entry.Substring(0, eq).Trim().ToUpperInvariant();
                string[] keys = entry.Substring(eq + 1).Split('/');
                var numbers = new int[3];
                for (int i = 0; i < 3 && i < keys.Length; i++) int.TryParse(keys[i], out numbers[i]);
                if (id.Length > 0) map[id] = numbers;
            }
            return map;
        }

        // slot: 0 for the Left button, 1 for Right, 2 for the key the device sends when both are pressed together.
        public static void SetK2Key(string id, int slot, int vkey)
        {
            var map = K2KeyMap();
            id = id.Trim().ToUpperInvariant();
            int[] keys;
            if (!map.TryGetValue(id, out keys)) keys = new int[3];
            keys[slot] = vkey;
            map[id] = keys;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var pair in map) parts.Add(pair.Key + "=" + pair.Value[0] + "/" + pair.Value[1] + "/" + pair.Value[2]);
            K2KeyMapText = string.Join(";", parts.ToArray());
        }

        public static void AddK2Id(string id)
        {
            var ids = K2ExtraIdList();
            id = id.Trim().ToUpperInvariant();
            if (id.Length == 0 || ids.Contains(id)) return;
            ids.Add(id);
            K2ExtraIds = string.Join(",", ids.ToArray());
        }

        // The meeting alert shows the plain icon instead of a picture. Off unless changed.
        public static bool AlertPictureOff { get { return Get("AlertPictureOff", false); } set { Set("AlertPictureOff", value); } }
        // Set once the one-time "New: Meeting Alerts" notice has been shown (or isn't needed), so it never comes back.
        public static bool MeetingAlertsIntroShown { get { return Get("MeetingAlertsIntroShown", false); } set { Set("MeetingAlertsIntroShown", value); } }
        // Today's meetings in a panel attached to the right of the BusyLightz window while it's open. On unless changed.
        public static bool AgendaBesideWindow { get { return Get("AgendaBesideWindow"); } set { Set("AgendaBesideWindow", value); } }
        public static bool MeetingAlertSound { get { return Get("MeetingAlertSound"); } set { Set("MeetingAlertSound", value); } }
        // Save a copy of the transcript Teams makes of each Teams meeting the user was in. Off unless turned on (it needs an
        // administrator's approval in Microsoft 365 as well).
        public static bool TranscriptsOn { get { return Get("TranscriptsOn", false); } set { Set("TranscriptsOn", value); } }
        // How often to look for a new version: a Daily check (the default) or a Weekly one. A copy installed for everyone
        // keeps this machine-wide, because its background updater runs as SYSTEM and can't see a user's settings; changing it
        // there needs an administrator (AllUsersInstall.SetMachineUpdateSchedule).
        public static readonly string[] UpdateScheduleChoices = { "Daily", "Weekly" };
        public static string UpdateSchedule
        {
            get
            {
                string value = null;
                try
                {
                    using (var key = (AllUsersInstall.IsThisCopy ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(SettingsKey))
                        value = key == null ? null : key.GetValue("UpdateSchedule") as string;
                }
                catch (Exception) { }
                return string.Equals(value, "Weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" : "Daily";
            }
            set { SetString("UpdateSchedule", value == "Weekly" ? "Weekly" : "Daily"); }
        }

        public static TimeSpan UpdateInterval(string schedule)
        {
            return schedule == "Weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        }

        // When this user's copy last looked for a new version on its own schedule (MinValue if never).
        public static DateTime LastUpdateCheckUtc
        {
            get
            {
                DateTime when;
                string saved = GetString("LastUpdateCheckUtc");
                return !string.IsNullOrEmpty(saved) && DateTime.TryParse(saved, null, System.Globalization.DateTimeStyles.RoundtripKind, out when)
                    ? when.ToUniversalTime() : DateTime.MinValue;
            }
            set { SetString("LastUpdateCheckUtc", value.ToUniversalTime().ToString("o")); }
        }

        // How long an app has to hold the microphone before it counts as a call, so a short burst like dictation doesn't flip
        // your lights and Teams status. 0 counts it right away.
        public static readonly int[] CallMinSecondChoices = { 0, 5, 10, 15, 30 };
        public const int DefaultCallMinSeconds = 10;
        public static int CallMinSeconds
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    object value = key == null ? null : key.GetValue("CallMinSeconds");
                    return value is int && Array.IndexOf(CallMinSecondChoices, (int)value) >= 0 ? (int)value : DefaultCallMinSeconds;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("CallMinSeconds", value, RegistryValueKind.DWord);
            }
        }
        // How many minutes before a meeting the alert shows.
        public static readonly int[] MeetingAlertMinuteChoices = { 1, 2, 5, 10, 15 };
        public const int DefaultMeetingAlertMinutes = 5;
        public static int MeetingAlertMinutes
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    object value = key == null ? null : key.GetValue("MeetingAlertMinutes");
                    return value is int && Array.IndexOf(MeetingAlertMinuteChoices, (int)value) >= 0 ? (int)value : DefaultMeetingAlertMinutes;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("MeetingAlertMinutes", value, RegistryValueKind.DWord);
            }
        }
        // What the alert says on its first line; {when} becomes "in 4:32" (counting down live) or "now", {subject} the meeting's title.
        public const string DefaultMeetingAlertText = "Meeting starting {when}";
        public const int MeetingAlertTextMaxLength = 120;
        public static string MeetingAlertText
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    string value = key == null ? null : key.GetValue("MeetingAlertText") as string;
                    return string.IsNullOrWhiteSpace(value) ? DefaultMeetingAlertText : value;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("MeetingAlertText", value ?? "", RegistryValueKind.String);
            }
        }
        // Leaves Microsoft Teams alone: no status reading, no Busy during calls, no status message. Off unless changed.
        public static bool TeamsDisabled { get { return Get("TeamsDisabled", false); } set { Set("TeamsDisabled", value); } }
        // Use a plugged-in mutesync button to mute and unmute the mic (see MuteSyncButton).
        public static bool MuteSyncEnabled { get { return Get("MuteSyncEnabled"); } set { Set("MuteSyncEnabled", value); } }
        // Its ring: the color while muted and while live (names from BlyncDevice.ColorNames), brightness in percent
        // (25 matches the MuteMe app), and whether it's lit only while an app is using the mic.
        public const string DefaultMuteSyncMutedColor = "Red", DefaultMuteSyncLiveColor = "Green";
        public const int DefaultMuteSyncBrightness = 25;
        public static string MuteSyncMutedColor
        {
            get { return ColorName("MuteSyncMutedColor", DefaultMuteSyncMutedColor); }
            set { SetLightColorName("MuteSyncMutedColor", value); }
        }
        public static string MuteSyncLiveColor
        {
            get { return ColorName("MuteSyncLiveColor", DefaultMuteSyncLiveColor); }
            set { SetLightColorName("MuteSyncLiveColor", value); }
        }
        public static int MuteSyncBrightness
        {
            get { return Ranged("MuteSyncBrightness", MinBrightness, 100, DefaultMuteSyncBrightness); }
            set { SetInt("MuteSyncBrightness", value); }
        }
        public static bool MuteSyncOnlyOnCalls { get { return Get("MuteSyncOnlyOnCalls", false); } set { Set("MuteSyncOnlyOnCalls", value); } }

        // Leaves the K2 key alone: no actions, and its buttons are ordinary mouse clicks again. Off unless changed.
        public static bool K2Disabled { get { return Get("K2Disabled", false); } set { Set("K2Disabled", value); } }

        // K2 macro key: what each button (and both together) does, from K2Actions.All, and the text it needs.
        internal static string K2Action(K2Key.Button button)
        {
            string saved = GetString("K2" + button + "Action");
            foreach (var action in K2Actions.All) if (action[0] == saved) return saved;
            return button == K2Key.Button.Left ? "Mute" : button == K2Key.Button.Right ? "BusyAvailable" : "TypeEmail";
        }
        internal static void SetK2Action(K2Key.Button button, string action) { SetString("K2" + button + "Action", action); }
        internal static string K2Value(K2Key.Button button) { return GetString("K2" + button + "Value") ?? ""; }
        internal static void SetK2Value(K2Key.Button button, string value) { SetString("K2" + button + "Value", value); }

        // The email address "Type my email address" types; asked for in the setup checklist. Until it's set, an
        // example address.
        public const string ExampleEmail = "ChangeMe@ChangeMe.Com";
        // The placeholder earlier versions used; still treated as "not set yet".
        const string OldExampleEmail = "YourEmailHere@spacex.com";
        public static string MacroEmail
        {
            get
            {
                string saved = GetString("MacroEmail");
                return string.IsNullOrWhiteSpace(saved) || string.Equals(saved.Trim(), OldExampleEmail, StringComparison.OrdinalIgnoreCase) ? ExampleEmail : saved.Trim();
            }
            set { SetString("MacroEmail", value == null ? "" : value.Trim()); }
        }
        public static bool MacroEmailSet { get { return MacroEmail != ExampleEmail; } }

        static string GetString(string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return key == null ? null : key.GetValue(name) as string;
        }

        static void SetString(string name, string value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(name, value ?? "", RegistryValueKind.String);
        }

        // A saved color name if it's one of BlyncDevice.ColorNames, otherwise fallback.
        static string ColorName(string setting, string fallback)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                string value = key == null ? null : key.GetValue(setting) as string;
                return Array.IndexOf(BlyncDevice.ColorNames, value) >= 0 ? value : fallback;
            }
        }
        public static string TeamsStatusMessage
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    string value = key == null ? null : key.GetValue("TeamsStatusMessage") as string;
                    return string.IsNullOrWhiteSpace(value) ? TeamsStatusMessageChoices[0] : value;
                }
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("TeamsStatusMessage", value ?? "", RegistryValueKind.String);
            }
        }

        // The version that last ran, so an update (automatic or a manual install) can say what changed. Null before the first run.
        public static string LastVersion
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    return key == null ? null : key.GetValue("LastVersion") as string;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("LastVersion", value, RegistryValueKind.String);
            }
        }

        static bool Get(string name, bool defaultValue = true)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object value = key == null ? null : key.GetValue(name);
                return value is int ? (int)value != 0 : defaultValue;
            }
        }

        static void Set(string name, bool value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
