using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Krabbz.Blynclight
{
    // The K2 two-button USB macro key (USB VID 04D9 / PID A2D0, a Holtek chip that calls itself a "USB Gaming Mouse").
    // Each of its buttons, and both pressed together, runs a macro picked in Settings (see K2Actions).
    //
    // Out of the box its buttons are a left and a right mouse click. BusyLightz reads them through Windows raw input,
    // which says which device sent each click, so a normal mouse never runs a macro. Raw input can't stop a click,
    // so a low-level mouse hook does: when Windows is about to act on a click, the hook first reads any raw input
    // waiting for it, and if that click came from the K2, cancels it. Clicks from any other mouse go through
    // untouched. If the K2 has been set to F13 and F14 with its own tool, those keys work too (F13 left, F14 right).
    //
    // All of this runs on a thread of its own, so a busy window never slows the mouse down.
    sealed class K2Key : IDisposable
    {
        const string DeviceId = "VID_04D9&PID_A2D0";
        public enum Button { Left, Right, Both }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICELIST { public IntPtr Device; public uint Type; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, WParam; }
        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr Window; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern bool PeekMessage(out MSG msg, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);

        const int WM_INPUT = 0xFF, WM_APP_STOP = 0x8000 + 1;
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        const uint PM_REMOVE = 1;
        const uint RIDI_DEVICENAME = 0x20000007, RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x100;
        const uint RIM_TYPEMOUSE = 0, RIM_TYPEKEYBOARD = 1;
        const ushort LeftDown = 0x1, LeftUp = 0x2, RightDown = 0x4, RightUp = 0x8;
        const ushort KeyBreak = 0x1;
        // A K2 click is matched to the click Windows is about to act on within this long; one left unmatched (its
        // click already went through) is dropped after it, so it can't swallow a later click from a real mouse.
        const int MatchMs = 250;

        readonly SynchronizationContext ui;
        readonly Thread thread;
        readonly ManualResetEvent started = new ManualResetEvent(false);
        Listener listener;
        IntPtr hook;
        HookProc hookProc; // Kept so the delegate isn't collected while Windows still calls it.
        // Which raw input devices are a K2, by handle (looked up once each).
        readonly Dictionary<IntPtr, bool> isK2 = new Dictionary<IntPtr, bool>();
        // K2 clicks seen in raw input but not yet matched to the click Windows acts on: button flag and when.
        readonly List<KeyValuePair<ushort, int>> unmatched = new List<KeyValuePair<ushort, int>>();
        bool leftDown, rightDown, bothSeen;
        int blocked;

        // Raised on the thread that made this (the UI thread) when a press finishes: Left or Right alone, or Both if
        // the two were held together.
        public event Action<Button> Pressed;

        // How many of the K2's own clicks have been cancelled, for the log and tests.
        public int ClicksBlocked { get { return blocked; } }

        // Set from Disable K2 Functions in Settings: the K2's clicks go through as ordinary clicks, and no press is
        // reported, so no action runs.
        public static volatile bool Disabled;

        public K2Key()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            thread = new Thread(Run) { IsBackground = true, Name = "K2 key" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            started.WaitOne(5000);
        }

        void Run()
        {
            Program.Guard("K2 key", delegate
            {
                listener = new Listener(this);
                // Mouse and keyboard input from every device comes here, even while another app is in front; only
                // the K2's is acted on, and nothing is recorded.
                var devices = new[]
                {
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = listener.Handle },
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = RIDEV_INPUTSINK, Target = listener.Handle },
                };
                if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                    Program.LogEvent("Couldn't listen for the K2 key (error " + Marshal.GetLastWin32Error() + ")");
                hookProc = OnMouse;
                hook = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    Program.LogEvent("Couldn't stop the K2 key's own clicks (error " + Marshal.GetLastWin32Error() + ")");
            });
            started.Set();
            Application.Run();
        }

        public void Dispose()
        {
            if (listener != null && listener.Handle != IntPtr.Zero)
                PostMessage(listener.Handle, WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);
            thread.Join(2000);
        }

        // Called by Windows, on this thread, before it acts on any mouse event.
        IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int message = wParam.ToInt32();
                ushort flag = message == WM_LBUTTONDOWN ? LeftDown : message == WM_LBUTTONUP ? LeftUp
                    : message == WM_RBUTTONDOWN ? RightDown : message == WM_RBUTTONUP ? RightUp : (ushort)0;
                if (flag != 0 && !Disabled)
                {
                    try
                    {
                        // The raw input for this click is already waiting; read it first.
                        MSG msg;
                        while (PeekMessage(out msg, listener.Handle, WM_INPUT, WM_INPUT, PM_REMOVE)) DispatchMessage(ref msg);
                        if (TakeUnmatched(flag))
                        {
                            blocked++;
                            return (IntPtr)1;
                        }
                    }
                    catch (Exception) { } // Never let a problem here stop the mouse.
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        bool TakeUnmatched(ushort flag)
        {
            int now = Environment.TickCount;
            unmatched.RemoveAll(click => now - click.Value > MatchMs);
            int index = unmatched.FindIndex(click => click.Key == flag);
            if (index < 0) return false;
            unmatched.RemoveAt(index);
            return true;
        }

        // Whether a K2 is plugged in now. Cheap enough to call every few seconds.
        public static bool Present()
        {
            try
            {
                uint count = 0, size = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
                GetRawInputDeviceList(null, ref count, size);
                if (count == 0) return false;
                var list = new RAWINPUTDEVICELIST[count];
                if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return false;
                foreach (var device in list)
                    if (NameOf(device.Device).ToUpperInvariant().Contains(DeviceId)) return true;
            }
            catch (Exception) { }
            return false;
        }

        static string NameOf(IntPtr device)
        {
            uint length = 0;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref length);
            if (length == 0) return "";
            var name = new StringBuilder((int)length + 1);
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, name, ref length);
            return name.ToString();
        }

        bool FromK2(IntPtr device)
        {
            if (device == IntPtr.Zero) return false; // Input made up by software (SendInput), including our own typing.
            bool k2;
            if (!isK2.TryGetValue(device, out k2))
            {
                k2 = NameOf(device).ToUpperInvariant().Contains(DeviceId);
                isK2[device] = k2;
            }
            return k2;
        }

        void OnInput(IntPtr input)
        {
            uint size = 0, header = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(input, RID_INPUT, IntPtr.Zero, ref size, header);
            if (size == 0) return;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(input, RID_INPUT, buffer, ref size, header) != size) return;
                var h = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
                if (!FromK2(h.Device)) return;
                var data = IntPtr.Add(buffer, (int)header);
                if (h.Type == RIM_TYPEMOUSE)
                {
                    // RAWMOUSE: usFlags, then the button flags at offset 4.
                    ushort flags = (ushort)Marshal.ReadInt16(data, 4);
                    int now = Environment.TickCount;
                    foreach (var flag in new[] { LeftDown, RightDown, LeftUp, RightUp })
                        if ((flags & flag) != 0) unmatched.Add(new KeyValuePair<ushort, int>(flag, now));
                    if ((flags & LeftDown) != 0) Down(Button.Left);
                    if ((flags & RightDown) != 0) Down(Button.Right);
                    if ((flags & LeftUp) != 0) Up(Button.Left);
                    if ((flags & RightUp) != 0) Up(Button.Right);
                }
                else if (h.Type == RIM_TYPEKEYBOARD)
                {
                    // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey.
                    ushort flags = (ushort)Marshal.ReadInt16(data, 2), vkey = (ushort)Marshal.ReadInt16(data, 6);
                    Button? button = vkey == (ushort)Keys.F13 ? Button.Left : vkey == (ushort)Keys.F14 ? Button.Right : (Button?)null;
                    if (button == null) return;
                    if ((flags & KeyBreak) != 0) Up(button.Value);
                    else Down(button.Value); // Held keys repeat; Down ignores the repeats.
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        void Down(Button button)
        {
            if (button == Button.Left) leftDown = true; else rightDown = true;
            if (leftDown && rightDown) bothSeen = true;
        }

        // A press counts once both buttons are up again, so pressing the two together is one Both, not a Left too.
        void Up(Button button)
        {
            bool wasDown = button == Button.Left ? leftDown : rightDown;
            if (button == Button.Left) leftDown = false; else rightDown = false;
            if (!wasDown || leftDown || rightDown) return;
            var pressed = bothSeen ? Button.Both : button;
            bothSeen = false;
            if (Disabled) return;
            ui.Post(delegate { if (Pressed != null) Pressed(pressed); }, null);
        }

        // The hidden window raw input is sent to.
        sealed class Listener : NativeWindow
        {
            readonly K2Key owner;

            public Listener(K2Key owner)
            {
                this.owner = owner;
                CreateHandle(new CreateParams { Caption = "BusyLightz K2 key" });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    IntPtr input = m.LParam;
                    Program.Guard("K2 key", () => owner.OnInput(input));
                }
                else if (m.Msg == WM_APP_STOP)
                {
                    if (owner.hook != IntPtr.Zero) UnhookWindowsHookEx(owner.hook);
                    owner.hook = IntPtr.Zero;
                    DestroyHandle();
                    Application.ExitThread();
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }


    // What a K2 button does, picked in Settings for Left, Right and Both.
    static class K2Actions
    {
        // Saved name, and what Settings shows. Those marked with a value need text: what to type, the keys to press
        // (e.g. "Ctrl+Shift+M"), or a program, file or web address to open.
        public static readonly string[][] All =
        {
            new[] { "None", "Do nothing" },
            new[] { "TypeEmail", "Type my email address" },
            new[] { "TypeText", "Type text:" },
            new[] { "Keys", "Press keys:" },
            new[] { "Mute", "Mute / unmute my mic" },
            new[] { "BusyAvailable", "Switch Busy / Available" },
            new[] { "Busy", "Busy (red)" },
            new[] { "Available", "Available (green)" },
            new[] { "OnACall", "On a call" },
            new[] { "FollowTeams", "Follow Teams Status" },
            new[] { "Party", "Party Mode" },
            new[] { "Open", "Open:" },
        };

        public static bool NeedsValue(string action) { return action == "TypeText" || action == "Keys" || action == "Open"; }

        // What the value box suggests for each action that has one.
        public static string Hint(string action)
        {
            if (action == "TypeText") return "Text to type";
            if (action == "Keys") return "e.g. Ctrl+Shift+M";
            if (action == "Open") return "Program, file or https:// address";
            return "";
        }

        public static void Run(K2Key.Button button, TrayContext tray)
        {
            string action = AppSettings.K2Action(button), value = AppSettings.K2Value(button);
            Program.LogEvent("K2 key " + button.ToString().ToLowerInvariant() + " pressed: " + action);
            switch (action)
            {
                case "TypeEmail": TypeText(AppSettings.MacroEmail); break;
                case "TypeText": TypeText(value); break;
                case "Keys": PressKeys(value); break;
                case "Mute":
                    bool? muted = MicMute.IsMuted();
                    if (muted.HasValue) MicMute.SetMuted(!muted.Value, "the K2 USBC Key");
                    break;
                case "BusyAvailable":
                    tray.SetState(tray.Light.State == LightState.Red ? LightState.Green : LightState.Red);
                    break;
                case "Busy": tray.SetState(LightState.Red); break;
                case "Available": tray.SetState(LightState.Green); break;
                case "OnACall": tray.SetState(LightState.CallFlash); break;
                case "FollowTeams": tray.SetState(LightState.FollowTeams); break;
                case "Party": tray.SetState(LightState.Party); break;
                case "Open":
                    if (string.IsNullOrWhiteSpace(value)) break;
                    try { Process.Start(value.Trim()); }
                    catch (Exception ex) { Program.LogEvent("K2 key couldn't open " + value.Trim() + ": " + ex.Message); }
                    break;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint Type; public KEYBDINPUT Key; public uint Pad1, Pad2; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort VKey, Scan; public uint Flags, Time; public IntPtr ExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, INPUT[] inputs, int size);
        const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

        // Types text into whatever has the keyboard focus, as if typed.
        static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var inputs = new List<INPUT>();
            foreach (char c in text.Replace("\r\n", "\r").Replace('\n', '\r'))
            {
                inputs.Add(Key(0, c, KEYEVENTF_UNICODE));
                inputs.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            Send(inputs);
        }

        // Presses a shortcut like "Ctrl+Shift+M" or "Win+D": the modifiers, then the key, then lets go in reverse.
        static void PressKeys(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            var keys = new List<ushort>();
            foreach (string part in combo.Split('+'))
            {
                string name = part.Trim();
                if (name.Length == 0) continue;
                ushort vk = KeyCode(name);
                if (vk == 0) { Program.LogEvent("K2 key: don't know the key \"" + name + "\" in " + combo); return; }
                keys.Add(vk);
            }
            var inputs = new List<INPUT>();
            foreach (var vk in keys) inputs.Add(Key(vk, '\0', 0));
            for (int i = keys.Count - 1; i >= 0; i--) inputs.Add(Key(keys[i], '\0', KEYEVENTF_KEYUP));
            Send(inputs);
        }

        static ushort KeyCode(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "ctrl": case "control": return (ushort)Keys.ControlKey;
                case "shift": return (ushort)Keys.ShiftKey;
                case "alt": return (ushort)Keys.Menu;
                case "win": case "windows": return (ushort)Keys.LWin;
                case "enter": return (ushort)Keys.Enter;
                case "esc": case "escape": return (ushort)Keys.Escape;
                case "del": return (ushort)Keys.Delete;
                case "space": return (ushort)Keys.Space;
            }
            if (name.Length == 1 && char.IsLetterOrDigit(name[0])) return (ushort)char.ToUpperInvariant(name[0]);
            Keys key;
            return Enum.TryParse(name, true, out key) ? (ushort)key : (ushort)0;
        }

        static INPUT Key(ushort vk, char c, uint flags)
        {
            return new INPUT { Type = INPUT_KEYBOARD, Key = new KEYBDINPUT { VKey = vk, Scan = c, Flags = flags } };
        }

        static void Send(List<INPUT> inputs)
        {
            if (inputs.Count == 0) return;
            if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT))) != inputs.Count)
                Program.LogEvent("K2 key: Windows blocked the typing (error " + Marshal.GetLastWin32Error() + ")");
        }
    }
}
