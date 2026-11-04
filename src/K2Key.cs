using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Krabbz.Blynclight
{
    // The K2 two-button USB macro key. Out of the box its buttons are a left and a right mouse click; it shows up as
    // USB VID 04D9 / PID A2D0 (a Holtek chip that calls itself a "USB Gaming Mouse"). For now this only tells the
    // window whether one is plugged in.
    static class K2Key
    {
        const string DeviceId = "VID_04D9&PID_A2D0";

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICELIST { public IntPtr Device; public uint Type; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
        const uint RIDI_DEVICENAME = 0x20000007;

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
                {
                    uint length = 0;
                    GetRawInputDeviceInfo(device.Device, RIDI_DEVICENAME, null, ref length);
                    if (length == 0) continue;
                    var name = new StringBuilder((int)length + 1);
                    GetRawInputDeviceInfo(device.Device, RIDI_DEVICENAME, name, ref length);
                    if (name.ToString().ToUpperInvariant().Contains(DeviceId)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
