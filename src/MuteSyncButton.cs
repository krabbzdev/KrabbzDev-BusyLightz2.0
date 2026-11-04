using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Krabbz.Blynclight
{
    // Drives a mutesync USB mute button (a Silicon Labs CP210x serial bridge, USB VID 10C4 / PID EA60, that reports
    // itself as "MuteSync Button"): pressing it toggles the Windows microphone mute, and its light ring shows red while
    // muted and green while live.
    //
    // The button talks at 9600 baud. It sends 0x33 when pressed and 0x34 when let go. Its ring takes 13 bytes:
    // 0x41 then a red, green, blue triple (0-255) for each of its 4 LEDs, and goes dark unless the color is sent again
    // every few seconds. Only one app can open the port, so the MuteMe app has to be closed for this to work.
    sealed class MuteSyncButton : IDisposable
    {
        const int Baud = 9600;
        const byte Pressed = 0x33, Released = 0x34, SetColor = 0x41;
        // How often to check the mic mute state; the ring color is sent again at least this often.
        const int TickMs = 500;
        const int KeepAliveMs = 2000;
        // How often to look for the button while it isn't connected.
        const int SearchMs = 10000;
        readonly object gate = new object();
        readonly Timer timer;
        SerialPort port;
        bool enabled;
        // The ring color last sent (at full brightness; black is off), or null before the first.
        Color? shownRing;
        DateTime lastSent, lastSearch = DateTime.MinValue;
        bool buttonDown;
        string status = "Not found";
        bool warnedInUse;
        // Ring settings from Light Control, read once rather than from the registry twice a second.
        string mutedColor, liveColor;
        int brightness;
        bool onlyOnCalls;
        bool onCall;
        // Until then the ring stays lit even with "only during calls" on, so Light Control changes can be seen.
        DateTime previewUntil;

        // What the window's mutesync tile shows: the ring's color (black when it's off), or null when no button is
        // connected.
        public static Color? Ring { get; private set; }

        // Raised (on a background thread) when the connection status or the ring color changes.
        public event Action StatusChanged;

        public MuteSyncButton(bool enabled)
        {
            this.enabled = enabled;
            LoadSettings();
            timer = new Timer(delegate { Program.Guard("mutesync button", Tick); }, null, 1000, TickMs);
        }

        public string Status { get { lock (gate) return enabled ? status : "Turned off"; } }
        public bool Connected { get { lock (gate) return port != null; } }

        void LoadSettings()
        {
            mutedColor = AppSettings.MuteSyncMutedColor;
            liveColor = AppSettings.MuteSyncLiveColor;
            brightness = AppSettings.MuteSyncBrightness;
            onlyOnCalls = AppSettings.MuteSyncOnlyOnCalls;
        }

        // Picks up changed ring settings and shows them now.
        public void ApplySettings()
        {
            lock (gate)
            {
                LoadSettings();
                lastSent = DateTime.MinValue;
            }
        }

        // Keeps the ring lit for a few seconds even if it's set to light only during calls.
        public void Preview(int ms)
        {
            lock (gate)
            {
                previewUntil = DateTime.Now.AddMilliseconds(ms);
                lastSent = DateTime.MinValue;
            }
        }

        // Whether an app is using the mic, for "only light the ring during calls".
        public bool OnCall
        {
            set
            {
                lock (gate)
                {
                    if (onCall == value) return;
                    onCall = value;
                    lastSent = DateTime.MinValue;
                }
            }
        }

        public bool Enabled
        {
            set
            {
                lock (gate)
                {
                    if (enabled == value) return;
                    enabled = value;
                    if (!value) Close(true);
                    lastSearch = DateTime.MinValue;
                }
                RaiseStatus();
            }
        }

        void Tick()
        {
            bool changed = false;
            lock (gate)
            {
                if (!enabled) return;
                if (port == null)
                {
                    if ((DateTime.Now - lastSearch).TotalMilliseconds < SearchMs) return;
                    lastSearch = DateTime.Now;
                    changed = Open();
                    if (port == null) { if (changed) RaiseStatusLater(); return; }
                }
                ShowMuted(MicMute.IsMuted());
            }
            if (changed) RaiseStatus();
        }

        // Looks for the button and opens it. Returns true if the status changed.
        bool Open()
        {
            string old = status;
            string name = FindPort();
            if (name == null) { status = "Not found"; return status != old; }
            var serial = new SerialPort(name, Baud, Parity.None, 8, StopBits.One);
            try { serial.Open(); }
            catch (UnauthorizedAccessException)
            {
                // Another app has it, most likely the MuteMe app.
                serial.Dispose();
                status = "In use by another app (close the MuteMe app)";
                if (!warnedInUse) Program.LogEvent("mutesync button found on " + name + " but another app is using it");
                warnedInUse = true;
                return status != old;
            }
            catch (IOException) { serial.Dispose(); status = "Not found"; return status != old; }
            serial.DataReceived += OnData;
            port = serial;
            shownRing = null;
            buttonDown = false;
            warnedInUse = false;
            status = "Connected on " + name;
            Program.LogEvent("mutesync button connected on " + name);
            return true;
        }

        void OnData(object sender, SerialDataReceivedEventArgs e)
        {
            Program.Guard("mutesync button press", delegate
            {
                lock (gate)
                {
                    var serial = (SerialPort)sender;
                    if (serial != port) return;
                    byte[] data;
                    try
                    {
                        data = new byte[serial.BytesToRead];
                        serial.Read(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        if (ex is IOException || ex is InvalidOperationException || ex is TimeoutException) return;
                        throw;
                    }
                    foreach (byte b in data)
                    {
                        if (b == Pressed && !buttonDown)
                        {
                            buttonDown = true;
                            // Mute or unmute as soon as it's pressed, like a key.
                            bool? muted = MicMute.IsMuted();
                            if (muted.HasValue && MicMute.SetMuted(!muted.Value)) ShowMuted(!muted.Value);
                        }
                        else if (b == Released) buttonDown = false;
                    }
                }
            });
        }

        // The muted color while muted and the live color while live (red and green unless changed in Light Control);
        // off with no microphone, or outside calls if it's set to light only during calls. Sent when it changes,
        // and again every couple of seconds so the ring stays lit.
        void ShowMuted(bool? muted)
        {
            var want = Color.Black;
            bool lit = muted.HasValue && (!onlyOnCalls || onCall || DateTime.Now < previewUntil);
            if (lit)
            {
                var c = BlyncDevice.ColorFor(muted.Value ? mutedColor : liveColor);
                want = Color.FromArgb(c.R, c.G, c.B);
            }
            if (shownRing == want && (DateTime.Now - lastSent).TotalMilliseconds < KeepAliveMs) return;
            bool changed = shownRing != want;
            if (!Write(want.R * brightness / 100, want.G * brightness / 100, want.B * brightness / 100)) return;
            shownRing = want;
            Ring = want;
            if (changed) RaiseStatusLater();
        }

        bool Write(int r, int g, int b)
        {
            if (port == null) return false;
            var data = new byte[13];
            data[0] = SetColor;
            for (int led = 0; led < 4; led++)
            {
                data[1 + led * 3] = (byte)r;
                data[2 + led * 3] = (byte)g;
                data[3 + led * 3] = (byte)b;
            }
            try
            {
                port.Write(data, 0, data.Length);
                lastSent = DateTime.Now;
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidOperationException || ex is TimeoutException || ex is UnauthorizedAccessException)) throw;
                // Unplugged.
                Program.LogEvent("mutesync button disconnected");
                Close(false);
                status = "Not found";
                RaiseStatusLater();
                return false;
            }
        }

        // Turns the ring off (if it's still there) and lets go of the port.
        void Close(bool darken)
        {
            if (port == null) return;
            var serial = port;
            if (darken) Write(0, 0, 0);
            port = null;
            shownRing = null;
            Ring = null;
            RaiseStatusLater();
            serial.DataReceived -= OnData;
            try { serial.Close(); } catch (Exception) { }
            serial.Dispose();
            status = "Not found";
        }

        void RaiseStatus()
        {
            var handler = StatusChanged;
            if (handler != null) handler();
        }

        // For changes made while holding the lock, which handlers mustn't run under.
        void RaiseStatusLater()
        {
            ThreadPool.QueueUserWorkItem(delegate { Program.Guard("mutesync status", RaiseStatus); });
        }

        // The COM port of a plugged-in mutesync button, e.g. "COM3", or null. Other CP210x devices (lots of gadgets
        // use the same chip) are left alone: only one whose USB description says "MuteSync Button" counts.
        static string FindPort()
        {
            try
            {
                using (var search = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\VID_10C4&PID_EA60%'"))
                {
                    foreach (ManagementObject device in search.Get())
                    {
                        using (device)
                        {
                            var match = Regex.Match(Convert.ToString(device["Name"]) ?? "", @"\((COM\d+)\)");
                            if (match.Success && UsbDescription(device).IndexOf("MuteSync", StringComparison.OrdinalIgnoreCase) >= 0)
                                return match.Groups[1].Value;
                        }
                    }
                }
            }
            catch (ManagementException) { }
            catch (COMException) { }
            return null;
        }

        static string UsbDescription(ManagementObject device)
        {
            try
            {
                var input = device.GetMethodParameters("GetDeviceProperties");
                input["devicePropertyKeys"] = new[] { "DEVPKEY_Device_BusReportedDeviceDesc" };
                var output = device.InvokeMethod("GetDeviceProperties", input, null);
                var properties = output["deviceProperties"] as ManagementBaseObject[];
                if (properties != null)
                    foreach (var property in properties)
                        if (property["Data"] != null) return property["Data"].ToString();
            }
            catch (ManagementException) { }
            catch (COMException) { }
            return "";
        }

        public void Dispose()
        {
            using (var done = new ManualResetEvent(false))
            {
                timer.Dispose(done);
                done.WaitOne(2000);
            }
            lock (gate) Close(true);
        }
    }

    // The Windows mute switch of the default microphone (the one apps use for calls, and the everyday default if it's
    // a different one).
    static class MicMute
    {
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        const int Capture = 1, ConsoleRole = 0, CommunicationsRole = 2, ClsctxAll = 23;
        static readonly Guid EnumeratorClass = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        // The communications mic's mute state, or null with no microphone.
        public static bool? IsMuted()
        {
            var volume = Volume(CommunicationsRole) ?? Volume(ConsoleRole);
            if (volume == null) return null;
            try
            {
                bool muted;
                return volume.GetMute(out muted) == 0 ? muted : (bool?)null;
            }
            finally { Marshal.ReleaseComObject(volume); }
        }

        // Mutes or unmutes both default mics. Returns false with no microphone.
        public static bool SetMuted(bool muted)
        {
            bool any = false;
            foreach (int role in new[] { CommunicationsRole, ConsoleRole })
            {
                var volume = Volume(role);
                if (volume == null) continue;
                try
                {
                    var context = Guid.Empty;
                    if (volume.SetMute(muted, ref context) >= 0) any = true;
                }
                finally { Marshal.ReleaseComObject(volume); }
            }
            if (any) Program.LogEvent(muted ? "Mic muted with the mutesync button" : "Mic unmuted with the mutesync button");
            return any;
        }

        static IAudioEndpointVolume Volume(int role)
        {
            object enumeratorObject = null;
            IMMDevice device = null;
            try
            {
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClass));
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                if (enumerator.GetDefaultAudioEndpoint(Capture, role, out device) != 0 || device == null) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                object volume;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out volume) != 0) return null;
                return (IAudioEndpointVolume)volume;
            }
            catch (COMException) { return null; }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumeratorObject != null) Marshal.ReleaseComObject(enumeratorObject);
            }
        }
    }
}
