using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Krabbz.Blynclight
{
    // Asks Windows audio which apps are recording from a microphone right now. A second source for call detection:
    // Windows' microphone usage record (what CallDetector reads first) is missed for some apps on some PCs, even
    // while Windows Settings shows them using the mic. This sees any app with an active recording stream.
    static class AudioSessions
    {
        const int eCapture = 1, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23, AudioSessionStateActive = 1;
        static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        // The full exe path of every process recording right now. Empty if Windows audio can't be asked.
        public static List<string> RecordingApps()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object enumeratorObject = null;
            IMMDeviceCollection devices = null;
            try
            {
                enumeratorObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                if (enumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, out devices) != 0) return paths;
                uint count;
                devices.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device;
                    if (devices.Item(i, out device) != 0) continue;
                    try { AddRecording(device, paths, seen); }
                    finally { Release(device); }
                }
            }
            catch (Exception) { } // No audio service, or no microphone: nothing is recording.
            finally
            {
                Release(devices);
                Release(enumeratorObject);
            }
            return paths;
        }

        static void AddRecording(IMMDevice device, List<string> paths, HashSet<string> seen)
        {
            object managerObject = null;
            IAudioSessionEnumerator sessions = null;
            try
            {
                var iid = IID_IAudioSessionManager2;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out managerObject) != 0) return;
                if (((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions) != 0) return;
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 session;
                    if (sessions.GetSession(i, out session) != 0) continue;
                    try
                    {
                        int state;
                        uint pid;
                        if (session.GetState(out state) != 0 || state != AudioSessionStateActive) continue;
                        if (session.GetProcessId(out pid) != 0 || pid == 0) continue;
                        string path = ProcessPath(pid);
                        if (path != null && seen.Add(path)) paths.Add(path);
                    }
                    finally { Release(session); }
                }
            }
            finally
            {
                Release(sessions);
                Release(managerObject);
            }
        }

        // Works for apps running as administrator too, which a normal Process lookup can't read.
        static string ProcessPath(uint pid)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(1024);
                int size = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        // Polled every second, so COM objects are released right away instead of waiting for the garbage collector.
        static void Release(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
        }

        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            // The rest of the interface isn't used.
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }
    }
}
