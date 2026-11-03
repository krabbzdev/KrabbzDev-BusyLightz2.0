using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Krabbz.Blynclight
{
    // Owns the light: remembers its state, cycles colors in Party Mode, and re-applies the state when a light is plugged in.
    // Every write to a light happens on its own background thread, so a light that stops answering can't freeze the app.
    public class LightController : IDisposable
    {
        // How often the light thread runs: Party Mode and call flashing move on this often.
        public const int PartyStepMs = 100;
        // Party Mode goes once round every color in this time.
        const int PartyCycleMs = 250;
        // Look for newly plugged-in or removed lights, and re-send the current color, this often.
        const int DeviceCheckMs = 2000;

        readonly object sync = new object();
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        // Set to have the light thread show a change right away instead of at its next tick.
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight");
        readonly Thread lightThread;
        string state;
        // While set (e.g. red during a phone call), shown instead of the saved state; not saved.
        string callOverride;
        // Color for the user's Teams status, shown in FollowTeams when the app reads presence itself.
        // Null means Embrava Connect is driving the light instead.
        string teamsColor;
        // When a color picked by hand (Red, Green, On A Call) goes back to following Teams, in UTC; null for never.
        // Saved, so the timer keeps running across restarts.
        DateTime? returnToTeamsAt;
        List<LightDevice> knownLights = new List<LightDevice>();
        string lastLogged;
        // Shown for a few seconds while a setting is changed in Light Control, ahead of everything else; until
        // previewEnds (Environment.TickCount).
        string preview;
        int previewEnds;
        // Set when something changed that the light thread should show; turnOffPending when lights were just disabled.
        bool applyPending, turnOffPending;
        // From Settings, read again by ApplySettings: brightness in percent, how long a call flash shows each color,
        // and the color per state.
        int brightness, callFlashMs;
        Dictionary<string, LightColor> colors;

        // Raised on a background thread when a light is plugged in or removed.
        public event Action LightsChanged;

        // False while USB light integration is turned off: no scanning and no writes.
        bool lightsEnabled;

        public LightController(bool lightsEnabled)
        {
            this.lightsEnabled = lightsEnabled;
            Directory.CreateDirectory(dataDir);
            state = LoadState();
            returnToTeamsAt = LoadReturnToTeams();
            LoadSettings();
            lightThread = new Thread(LightLoop);
            lightThread.IsBackground = true;
            lightThread.Start();
        }

        // The state the user picked (saved across restarts).
        public string State { get { lock (sync) return state; } }
        // What the light is actually showing: the call override if one is active, then the Teams color
        // when following Teams, otherwise State.
        public string ShownState
        {
            get
            {
                lock (sync)
                {
                    if (preview != null) return preview;
                    if (callOverride != null) return callOverride;
                    if (state == LightState.FollowTeams && teamsColor != null) return teamsColor;
                    return state;
                }
            }
        }
        public bool OverrideActive { get { lock (sync) return callOverride != null; } }
        public int LightCount { get { lock (sync) return knownLights.Count; } }
        // How far round the color wheel Party Mode moves every PartyStepMs.
        public int PartyHueStep { get { return 360 * PartyStepMs / PartyCycleMs % 360; } }

        // Turning USB lights off switches connected lights off and then leaves them alone; turning them back on
        // finds them again on the next device check and shows the current state.
        public bool LightsEnabled
        {
            get { lock (sync) return lightsEnabled; }
            set
            {
                lock (sync)
                {
                    if (lightsEnabled == value) return;
                    lightsEnabled = value;
                    if (!value) turnOffPending = true;
                }
                wake.Set();
                if (LightsChanged != null) LightsChanged();
            }
        }

        // e.g. "Blynclight + 2 Luxafor", "none", or "turned off" when USB lights are disabled.
        public string LightSummary
        {
            get
            {
                int blync, lux;
                lock (sync)
                {
                    if (!lightsEnabled) return "turned off";
                    blync = knownLights.FindAll(l => l.Kind == LightKind.Blynclight).Count;
                    lux = knownLights.Count - blync;
                }
                var parts = new List<string>();
                if (blync > 0) parts.Add((blync > 1 ? blync + " " : "") + "Blynclight");
                if (lux > 0) parts.Add((lux > 1 ? lux + " " : "") + "Luxafor");
                return parts.Count == 0 ? "none" : string.Join(" + ", parts.ToArray());
            }
        }

        // States picked by hand that override Teams status, so they go back to following Teams after a while.
        // Every state but Follow Teams Status, so the tray menu's "[:15 Min Override]" holds for all of them.
        public static bool OverridesTeams(string state)
        {
            return state == LightState.Red || state == LightState.Green || state == LightState.CallFlash
                || state == LightState.Party || state == LightState.Off;
        }

        public DateTime? ReturnToTeamsAt { get { lock (sync) return returnToTeamsAt; } }

        // Picking a state by hand also cancels any call override, so a manual choice always wins.
        // It also cancels any return-to-Teams timer; the caller starts a new one if needed.
        public void Set(string newState)
        {
            lock (sync)
            {
                state = newState;
                callOverride = null;
            }
            SaveState(newState);
            SetReturnToTeams(null);
            RequestApply();
        }

        public void SetReturnToTeams(DateTime? utc)
        {
            lock (sync) returnToTeamsAt = utc;
            try
            {
                if (utc == null) File.Delete(ReturnToTeamsPath);
                else File.WriteAllText(ReturnToTeamsPath, utc.Value.Ticks.ToString());
            }
            catch (Exception ex) { Log("Could not save the return-to-Teams time: " + ex.Message); }
        }

        // Shows overrideState until cleared with null, then returns to the saved state.
        public void SetOverride(string overrideState)
        {
            lock (sync)
            {
                if (callOverride == overrideState) return;
                callOverride = overrideState;
            }
            RequestApply();
        }

        // Sets the color for the user's Teams status (e.g. "green", "yellow"); null hands the light back to
        // Embrava Connect. Only shown while the saved state is FollowTeams and no call override is active.
        public void SetTeamsColor(string color)
        {
            lock (sync)
            {
                if (teamsColor == color) return;
                teamsColor = color;
            }
            RequestApply();
        }

        // Reads brightness, call flash speed and colors from Settings again and shows the change.
        public void ApplySettings()
        {
            LoadSettings();
            RequestApply();
        }

        // Shows shown (a state or Teams status name) for ms, then goes back to what was showing. Lets a setting
        // be seen as it's changed, whatever the lights are doing; null shows whatever is lit now (red if nothing).
        public void Preview(string shown, int ms)
        {
            if (shown == null)
            {
                string now = ShownState;
                shown = now == LightState.Off || now == LightState.FollowTeams || now == "off" ? LightState.Red : now;
            }
            lock (sync)
            {
                preview = shown;
                previewEnds = Environment.TickCount + ms;
            }
            RequestApply();
        }

        void LoadSettings()
        {
            var loaded = new Dictionary<string, LightColor>();
            // The names light states and Teams status use, mapped to the setting that picks their color.
            loaded["red"] = BlyncDevice.ColorFor(AppSettings.LightColorName("BusyColor"));
            loaded["green"] = BlyncDevice.ColorFor(AppSettings.LightColorName("AvailableColor"));
            loaded["yellow"] = BlyncDevice.ColorFor(AppSettings.LightColorName("AwayColor"));
            loaded["purple"] = BlyncDevice.ColorFor(AppSettings.LightColorName("DndColor"));
            loaded["call1"] = BlyncDevice.ColorFor(AppSettings.LightColorName("CallColor1"));
            loaded["call2"] = BlyncDevice.ColorFor(AppSettings.LightColorName("CallColor2"));
            int percent = AppSettings.Brightness, flash = AppSettings.CallFlashMs;
            lock (sync)
            {
                colors = loaded;
                brightness = percent;
                callFlashMs = flash;
            }
        }

        // The color for a state or Teams status name, as picked in Settings; anything unknown is off.
        LightColor ColorOf(string name)
        {
            LightColor color;
            lock (sync) return colors.TryGetValue(name, out color) ? color : LightColor.Off;
        }

        void RequestApply()
        {
            lock (sync) applyPending = true;
            wake.Set();
        }

        // Stops the background loop. Party Mode and call flashing can't continue without it, so the light is turned
        // off in those cases; the saved state is kept so it resumes next time the app starts.
        public void Dispose()
        {
            stopEvent.Set();
            lightThread.Join(2000);
            if (!Animated(ShownState)) return;
            // On its own thread, so a light that doesn't answer can't hold up closing the app.
            var off = new Thread(delegate () { Program.Guard("Turning the light off", () => WriteAll(LightColor.Off)); });
            off.IsBackground = true;
            off.Start();
            off.Join(2000);
        }

        static bool Animated(string shown)
        {
            return shown == LightState.Party || shown == LightState.CallFlash;
        }

        void LightLoop()
        {
            double hue = 0;
            int flashMs = 0;
            int sinceDeviceCheck = DeviceCheckMs;
            int lastTick = Environment.TickCount;
            string lastError = null;
            var handles = new WaitHandle[] { stopEvent, wake };
            do
            {
                int now = Environment.TickCount, elapsed = now - lastTick;
                lastTick = now;
                // An error here (e.g. a light or dock vanishing mid-scan) would close the app; log it once and keep going.
                try { LightStep(elapsed, ref hue, ref flashMs, ref sinceDeviceCheck); lastError = null; }
                catch (Exception ex)
                {
                    if (ex.Message != lastError) Program.LogError("Light update", ex);
                    lastError = ex.Message;
                }
            } while (WaitHandle.WaitAny(handles, PartyStepMs) != 0);
        }

        // One tick of LightLoop: show any change, scan for lights every DeviceCheckMs, and animate Party Mode and
        // call flashing. elapsed is the time since the last tick, which is shorter when woken for a change.
        void LightStep(int elapsed, ref double hue, ref int flashMs, ref int sinceDeviceCheck)
        {
            bool enabled, turnOff, apply;
            int flashEvery;
            lock (sync)
            {
                enabled = lightsEnabled;
                turnOff = turnOffPending;
                apply = applyPending;
                turnOffPending = applyPending = false;
                flashEvery = callFlashMs;
                if (preview != null && Environment.TickCount - previewEnds >= 0)
                {
                    preview = null;
                    apply = true;
                }
            }
            if (turnOff)
            {
                WriteAll(LightColor.Off);
                lock (sync) if (!lightsEnabled) knownLights = new List<LightDevice>();
            }
            // Scan right away when lights are turned back on.
            if (!enabled) { sinceDeviceCheck = DeviceCheckMs; return; }

            sinceDeviceCheck += elapsed;
            if (sinceDeviceCheck >= DeviceCheckMs)
            {
                sinceDeviceCheck = 0;
                var found = BlyncDevice.FindLights();
                bool newLight = false, countChanged;
                lock (sync)
                {
                    foreach (var light in found)
                        if (!knownLights.Exists(known => known.Path == light.Path)) newLight = true;
                    countChanged = found.Count != knownLights.Count;
                    knownLights = found;
                }
                // Re-send the solid color every check, not just when a light is plugged in: another program
                // (or a write that raced the first device scan) can leave a light showing the wrong color.
                apply = true;
                if ((newLight || countChanged) && LightsChanged != null) LightsChanged();
            }

            string current = ShownState;
            // Party Mode and call flashing are written every tick below; solid states only when something changed.
            // In FollowTeams without a Teams color the light belongs to Embrava Connect, so nothing is written.
            if (apply && !Animated(current) && current != LightState.FollowTeams) WriteAll(ColorOf(current));

            if (current == LightState.Party)
            {
                byte r, g, b;
                BlyncDevice.HueToRgb((int)hue, out r, out g, out b);
                WriteAll(LightColor.Rgb(r, g, b));
                hue = (hue + 360.0 * elapsed / PartyCycleMs) % 360;
            }
            if (current == LightState.CallFlash)
            {
                // Write every tick so a light plugged in mid-call joins in; the color flips every flashEvery ms.
                bool first = (flashMs / flashEvery) % 2 == 0;
                WriteAll(ColorOf(first ? "call1" : "call2"));
                flashMs += elapsed;
            }
            else flashMs = 0;
        }

        // Sends the color, at the brightness from Settings, to every connected light, each in its own report format.
        void WriteAll(LightColor color)
        {
            List<LightDevice> lights;
            int percent;
            lock (sync)
            {
                lights = knownLights;
                percent = brightness;
            }
            color = BlyncDevice.Dim(color, percent);
            foreach (var light in lights)
            {
                try { BlyncDevice.Write(light.Path, BlyncDevice.ReportFor(light.Kind, color)); }
                catch (Exception ex) { Log("Could not update " + light.Kind + ": " + ex.Message); }
            }
        }

        string StatePath { get { return Path.Combine(dataDir, "state.txt"); } }
        string ReturnToTeamsPath { get { return Path.Combine(dataDir, "return-to-teams.txt"); } }

        DateTime? LoadReturnToTeams()
        {
            try
            {
                long ticks;
                if (long.TryParse(File.ReadAllText(ReturnToTeamsPath).Trim(), out ticks)) return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (IOException) { }
            return null;
        }

        string LoadState()
        {
            try
            {
                string saved = File.ReadAllText(StatePath).Trim();
                if (saved == LightState.LegacyFlashRedBlue) return LightState.Party;
                if (LightState.IsValid(saved)) return saved;
            }
            catch (IOException) { }
            // New installs follow Teams status until the user picks something else.
            return LightState.FollowTeams;
        }

        void SaveState(string newState)
        {
            try { File.WriteAllText(StatePath, newState); }
            catch (Exception ex) { Log("Could not save state: " + ex.Message); }
        }

        // Appends to app.log, skipping repeats so a missing light doesn't flood the file.
        void Log(string message)
        {
            if (message == lastLogged) return;
            lastLogged = message;
            try { File.AppendAllText(Path.Combine(dataDir, "app.log"), DateTime.Now.ToString("s") + "  " + message + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
