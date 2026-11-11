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
        public const string ExampleEmail = "YourEmailHere@spacex.com";
        public static string MacroEmail
        {
            get
            {
                string saved = GetString("MacroEmail");
                return string.IsNullOrWhiteSpace(saved) ? ExampleEmail : saved.Trim();
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
