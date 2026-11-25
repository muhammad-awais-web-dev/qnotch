using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QNotch.Core;

/// <summary>Active microphone streams across all enabled capture endpoints. This covers desktop apps
/// that do not write per-app activity to CapabilityAccessManager's consent store.</summary>
internal static class CaptureAudioSessions
{
    public static IReadOnlyList<string> InUse()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        object? enumeratorObject = null, collectionObject = null;
        var step = "create enumerator";
        try
        {
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = typeof(IMMDeviceEnumerator).GUID;
            var hr = CoCreateInstance(ref clsid, 0, 23, ref iid, out IMMDeviceEnumerator enumerator);
            if (hr != 0) { Log.Warn($"Opening microphone endpoint enumerator failed (0x{hr:X8})"); return []; }
            enumeratorObject = enumerator;
            step = "enumerate endpoints";
            if (enumerator.EnumAudioEndpoints(1 /* capture */, 1 /* active */, out var collection) != 0) return [];
            collectionObject = collection;
            step = "count endpoints";
            if (collection.GetCount(out var count) != 0) return [];
            for (var i = 0; i < count; i++)
            {
                step = "get endpoint";
                if (collection.Item(i, out var device) != 0) continue;
                try { step = "read endpoint"; ReadDevice(device, names); }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        catch (Exception ex) { Log.Warn($"Reading microphone sessions failed at {step}", ex); }
        finally
        {
            if (collectionObject is not null) Marshal.ReleaseComObject(collectionObject);
            if (enumeratorObject is not null) Marshal.ReleaseComObject(enumeratorObject);
        }
        return names.ToList();
    }

    static void ReadDevice(IMMDevice device, SortedSet<string> names)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, 23, 0, out var managerObject) != 0) return;
        try
        {
            var manager = (IAudioSessionManager2)managerObject;
            if (manager.GetSessionEnumerator(out var sessions) != 0) return;
            try
            {
                if (sessions.GetCount(out var count) != 0) return;
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var controlObject) != 0) continue;
                    try
                    {
                        if (controlObject is not IAudioSessionControl2 control ||
                            control.GetState(out var state) != 0 || state != 1 ||
                            control.GetProcessId(out var pid) < 0 || pid == 0) continue;
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            var path = process.MainModule?.FileName;
                            var description = path is null ? null : FileVersionInfo.GetVersionInfo(path).FileDescription;
                            names.Add(string.IsNullOrWhiteSpace(description) ? process.ProcessName : description.Trim());
                        }
                        catch (Exception) { names.Add($"App {pid}"); }
                    }
                    finally { Marshal.ReleaseComObject(controlObject); }
                }
            }
            finally { Marshal.ReleaseComObject(sessions); }
        }
        finally { Marshal.ReleaseComObject(managerObject); }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IMMDeviceEnumerator enumerator);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection collection);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl();
        void GetSimpleAudioVolume();
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        void GetDisplayName();
        void SetDisplayName();
        void GetIconPath();
        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();
        void GetSessionIdentifier();
        void GetSessionInstanceIdentifier();
        [PreserveSig] int GetProcessId(out int processId);
    }
}
