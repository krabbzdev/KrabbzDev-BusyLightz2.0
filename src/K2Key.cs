using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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
        const int WH_MOUSE_LL = 14, WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
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
        // Keys a K2-type device sends instead of clicks, by hardware ID (set in Find my key), which device each raw input
        // handle is, and keystrokes seen in raw input but not yet matched to the one Windows acts on: key and when.
        readonly Dictionary<string, int[]> keyMap = AppSettings.K2KeyMap();
        readonly Dictionary<IntPtr, string> idOf = new Dictionary<IntPtr, string>();
        readonly List<KeyValuePair<int, int>> unmatchedKeys = new List<KeyValuePair<int, int>>();
        IntPtr keyHook;
        HookProc keyProc; // Kept so the delegate isn't collected while Windows still calls it.
        // K2 clicks seen in raw input but not yet matched to the click Windows acts on: button flag and when.
        readonly List<KeyValuePair<ushort, int>> unmatched = new List<KeyValuePair<ushort, int>>();
        bool leftDown, rightDown, bothSeen;
        int blocked;

        // Raised on the thread that made this (the UI thread) when a press finishes: Left or Right alone, or Both if
        // the two were held together.
        public event Action<Button> Pressed;

        // How many of the K2's own clicks have been cancelled, for the log and tests.
        public int ClicksBlocked { get { return blocked; } }

        // Set from Use the K2 key in Settings: the K2's clicks go through as ordinary clicks, and no press is
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
                // A key that types letters has them cancelled the same way, so they don't land in the open document.
                if (keyMap.Count > 0)
                {
                    keyProc = OnKey;
                    keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, GetModuleHandle(null), 0);
                    if (keyHook == IntPtr.Zero)
                        Program.LogEvent("Couldn't stop the K2 key's own keystrokes (error " + Marshal.GetLastWin32Error() + ")");
                }
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

        // Called by Windows, on this thread, before it acts on any keystroke. A key from a mapped K2 is cancelled.
        IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !Disabled)
            {
                int message = wParam.ToInt32();
                if (message == WM_KEYDOWN || message == WM_KEYUP || message == WM_SYSKEYDOWN || message == WM_SYSKEYUP)
                {
                    int vkey = Marshal.ReadInt32(lParam);
                    if (IsMappedKey(vkey))
                    {
                        try
                        {
                            MSG msg;
                            while (PeekMessage(out msg, listener.Handle, WM_INPUT, WM_INPUT, PM_REMOVE)) DispatchMessage(ref msg);
                            if (TakeUnmatchedKey(vkey)) return (IntPtr)1;
                        }
                        catch (Exception) { } // Never let a problem here stop the keyboard.
                    }
                }
            }
            return CallNextHookEx(keyHook, code, wParam, lParam);
        }

        bool IsMappedKey(int vkey)
        {
            foreach (var keys in keyMap.Values) if (keys[0] == vkey || keys[1] == vkey) return true;
            return false;
        }

        bool TakeUnmatchedKey(int vkey)
        {
            int now = Environment.TickCount;
            unmatchedKeys.RemoveAll(key => now - key.Value > MatchMs);
            int index = unmatchedKeys.FindIndex(key => key.Key == vkey);
            if (index < 0) return false;
            unmatchedKeys.RemoveAt(index);
            return true;
        }

        // The hardware ID (VID_xxxx&PID_yyyy) of a raw input device, looked up once each.
        string DeviceIdOf(IntPtr device)
        {
            string id;
            if (!idOf.TryGetValue(device, out id))
            {
                var match = Regex.Match(NameOf(device).ToUpperInvariant(), "VID_[0-9A-F]{4}&PID_[0-9A-F]{4}");
                id = match.Success ? match.Value : null;
                idOf[device] = id;
            }
            return id;
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

        // Whether a raw input device name is a K2: the known hardware ID, or one added with Find my key.
        static bool IsK2Name(string name)
        {
            string upper = (name ?? "").ToUpperInvariant();
            if (upper.Contains(DeviceId)) return true;
            foreach (string id in AppSettings.K2ExtraIdList()) if (upper.Contains(id)) return true;
            return false;
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
                    if (IsK2Name(NameOf(device.Device))) return true;
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
                k2 = IsK2Name(NameOf(device));
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
                    // A key set up in Find my key: its own two keys are Left and Right.
                    string deviceId = DeviceIdOf(h.Device);
                    int[] mapped;
                    if (deviceId != null && keyMap.TryGetValue(deviceId, out mapped))
                    {
                        if (mapped[0] != 0 && vkey == mapped[0]) button = Button.Left;
                        else if (mapped[1] != 0 && vkey == mapped[1]) button = Button.Right;
                        if (button != null) unmatchedKeys.Add(new KeyValuePair<int, int>(vkey, Environment.TickCount));
                    }
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

        // "Find my key": lists every device that sends a button press or a key while it's open, with its hardware ID and
        // what it sent. The user's normal mouse and keyboard show up too; they pick their key's row. A key that sends mouse
        // clicks is added by its hardware ID; one that sends keys (letters, say) has its Left and Right keys taught here.
        // Raw input goes to one window at a time, so while this is open the K2 listener isn't getting it; closing restarts
        // that, with whatever was saved, through changed.
        public sealed class Finder : Form
        {
            static readonly Regex IdPattern = new Regex("VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.Compiled);

            // What each row in the list stands for.
            sealed class FinderRow
            {
                public string Id;
                public bool Known;
                public int Key; // The last key it sent (0 if it only sends mouse clicks).
            }

            readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, GridLines = true };
            readonly Dictionary<string, ListViewItem> rows = new Dictionary<string, ListViewItem>();
            readonly ModernButton useKey = new ModernButton { Text = "Use this key", Accent = true, Enabled = false };
            readonly ModernButton useLeft = new ModernButton { Text = "Use as Left button", Enabled = false };
            readonly ModernButton useRight = new ModernButton { Text = "Use as Right button", Enabled = false };
            readonly Label message = new Label { AutoSize = false };
            readonly Action changed;

            public Finder(Action changed)
            {
                this.changed = changed;
                Text = "Find my key";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(560, 500);
                BackColor = Theme.Page;
                ForeColor = Theme.Text;
                Font = new Font("Segoe UI", 9.5f);

                Controls.Add(new Label
                {
                    Text = "Press the buttons on your two-button key. Every device that sends a button press or a key shows up below with " +
                           "its hardware ID and what it sent. Your normal mouse and keyboard show up too: ignore those.\r\n" +
                           "Sends mouse clicks (Left button, Right button)? Select its row and click Use this key.\r\n" +
                           "Sends a key (like Key Z)? Select its row, press the key's LEFT button and click Use as Left button, then press " +
                           "its RIGHT button and click Use as Right button.",
                    Bounds = new Rectangle(16, 10, 528, 112),
                });
                list.Bounds = new Rectangle(16, 128, 528, 190);
                list.BackColor = Theme.Card;
                list.ForeColor = Theme.Text;
                list.Columns.Add("Device", 80);
                list.Columns.Add("Hardware ID", 150);
                list.Columns.Add("Last sent", 130);
                list.Columns.Add("Presses", 60);
                list.Columns.Add("", 90);
                list.SelectedIndexChanged += delegate { UpdateButtons(); };
                Controls.Add(list);

                useLeft.Bounds = new Rectangle(16, 330, 170, 34);
                useLeft.Click += delegate { MapSelected(true); };
                Controls.Add(useLeft);
                useRight.Bounds = new Rectangle(194, 330, 170, 34);
                useRight.Click += delegate { MapSelected(false); };
                Controls.Add(useRight);
                useKey.Bounds = new Rectangle(372, 330, 172, 34);
                useKey.Click += delegate { UseSelected(); };
                Controls.Add(useKey);

                var copy = new ModernButton { Text = "Copy details", Bounds = new Rectangle(16, 372, 120, 34) };
                copy.Click += delegate { CopyDetails(); };
                Controls.Add(copy);
                var forget = new ModernButton { Text = "Forget added keys", Bounds = new Rectangle(144, 372, 150, 34) };
                forget.Click += delegate
                {
                    AppSettings.K2ExtraIds = "";
                    AppSettings.K2KeyMapText = "";
                    Say("Keys you added and keys you taught have been forgotten. Only the original K2 is recognized now (when you close this window).");
                };
                Controls.Add(forget);
                var close = new ModernButton { Text = "Close", Bounds = new Rectangle(440, 372, 104, 34) };
                close.Click += delegate { Close(); };
                Controls.Add(close);
                message.Bounds = new Rectangle(16, 418, 528, 72);
                message.ForeColor = Theme.Muted;
                message.Text = "Nothing yet. Press a button on your key.";
                Controls.Add(message);

                FormClosed += delegate { changed(); };
            }

            // Says what just happened, in the normal text color so it's noticed.
            void Say(string text)
            {
                message.ForeColor = Theme.Text;
                message.Text = text;
            }

            FinderRow Selected()
            {
                return list.SelectedItems.Count == 0 ? null : (FinderRow)list.SelectedItems[0].Tag;
            }

            void UpdateButtons()
            {
                var row = Selected();
                useKey.Enabled = row != null;
                useLeft.Enabled = row != null && row.Key != 0;
                useRight.Enabled = row != null && row.Key != 0;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                var devices = new[]
                {
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = Handle },
                    new RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = RIDEV_INPUTSINK, Target = Handle },
                };
                RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    IntPtr input = m.LParam;
                    Program.Guard("Key finder", () => OnInput(input));
                }
                base.WndProc(ref m);
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
                    if (h.Device == IntPtr.Zero) return;
                    var data = IntPtr.Add(buffer, (int)header);
                    string what = null;
                    int key = 0;
                    if (h.Type == RIM_TYPEMOUSE)
                    {
                        ushort flags = (ushort)Marshal.ReadInt16(data, 4);
                        if ((flags & LeftDown) != 0) what = "Left button";
                        else if ((flags & RightDown) != 0) what = "Right button";
                        else if ((flags & 0x10) != 0) what = "Middle button";
                    }
                    else if (h.Type == RIM_TYPEKEYBOARD)
                    {
                        ushort flags = (ushort)Marshal.ReadInt16(data, 2), vkey = (ushort)Marshal.ReadInt16(data, 6);
                        if ((flags & KeyBreak) == 0) { what = "Key " + (Keys)vkey; key = vkey; }
                    }
                    if (what == null) return;
                    Record(h.Type == RIM_TYPEMOUSE ? "Mouse" : "Keyboard", NameOf(h.Device), what, key);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }

            // Adds a press to the list: a row for each device and kind of input, counting its presses.
            void Record(string kind, string path, string what, int key)
            {
                var match = IdPattern.Match((path ?? "").ToUpperInvariant());
                string id = match.Success ? match.Value : "(no ID)";
                string rowKey = id + "|" + kind;
                ListViewItem row;
                if (!rows.TryGetValue(rowKey, out row))
                {
                    row = new ListViewItem(kind) { Tag = new FinderRow { Id = id, Known = IsK2Name(path) } };
                    row.SubItems.Add(id);
                    row.SubItems.Add(what);
                    row.SubItems.Add("0");
                    row.SubItems.Add(IsK2Name(path) ? "Known K2" : "");
                    rows[rowKey] = row;
                    list.Items.Add(row);
                }
                if (key != 0) ((FinderRow)row.Tag).Key = key;
                row.SubItems[2].Text = what;
                row.SubItems[3].Text = (int.Parse(row.SubItems[3].Text) + 1).ToString();
                message.ForeColor = Theme.Muted;
                message.Text = "Last press: " + kind + " " + id + ", " + what + ".";
                UpdateButtons();
            }

            // The key sends mouse clicks (or F13 and F14): recognize its hardware ID.
            void UseSelected()
            {
                var row = Selected();
                if (row == null) return;
                if (!IdPattern.IsMatch(row.Id)) { Say("That device has no hardware ID, so it can't be added."); return; }
                if (row.Known && row.Key != 0)
                {
                    Say("That key is already recognized, but it sends a key (" + (Keys)row.Key + "), not a mouse click, so its presses are ignored. " +
                        "Press its LEFT button and click Use as Left button, then its RIGHT button and click Use as Right button.");
                    return;
                }
                AppSettings.AddK2Id(row.Id);
                Say("Added " + row.Id + ". Close this window, then press a button on the key: the K2 page's Test your buttons list should show it.");
            }

            // The key sends keys: teach which one is the Left button and which the Right.
            void MapSelected(bool left)
            {
                var row = Selected();
                if (row == null) return;
                if (row.Key == 0 || !IdPattern.IsMatch(row.Id)) { Say("Press the button on your key first, so this row shows the key it sends."); return; }
                AppSettings.AddK2Id(row.Id);
                AppSettings.SetK2Key(row.Id, left, row.Key);
                Say((left ? "Left" : "Right") + " button is now " + (Keys)row.Key + " on " + row.Id + "." +
                    (left ? " Now press the key's RIGHT button, then click Use as Right button." : " Close this window, then press the buttons: Test your buttons on the K2 page should list them."));
            }

            // The list as text, to paste into an email.
            void CopyDetails()
            {
                var text = new StringBuilder("BusyLightz key finder" + Environment.NewLine);
                foreach (ListViewItem row in list.Items)
                    text.AppendLine(row.SubItems[0].Text + "\t" + row.SubItems[1].Text + "\t" + row.SubItems[2].Text + "\t" + row.SubItems[3].Text + "\t" + row.SubItems[4].Text);
                try { Clipboard.SetText(text.ToString()); Say("Copied. Paste it into an email or message."); }
                catch (Exception) { Say("Couldn't copy to the clipboard."); }
            }
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
                    if (owner.keyHook != IntPtr.Zero) UnhookWindowsHookEx(owner.keyHook);
                    owner.keyHook = IntPtr.Zero;
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
