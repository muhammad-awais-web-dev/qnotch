using System.Runtime.InteropServices;
using QNotch.Core;

namespace QNotch.Modules.Volume;

/// <summary>Master volume of the default playback device (Core Audio). Changes made elsewhere arrive through <see cref="Changed"/> on an audio thread.</summary>
internal sealed class VolumeCom : VolumeCom.IAudioEndpointVolumeCallback, VolumeCom.IMMNotificationClient
{
    static Guid _self = Guid.NewGuid(); // tags our own changes so their echo is ignored
    readonly object _gate = new();
    IAudioEndpointVolume? _vol;
    IMMDeviceEnumerator? _enumerator;

    /// <summary>Level 0..1 and mute, changed by another app or the keyboard.</summary>
    public event Action<float, bool>? Changed;
    public event Action? DefaultChanged;

    internal static IAudioMeterInformation? OpenPlaybackMeter()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return null;
        var id = typeof(IAudioMeterInformation).GUID;
        return device.Activate(ref id, 23, 0, out var meter) == 0 ? meter as IAudioMeterInformation : null;
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    /// <summary>(Re)opens the default device, which may have changed, and reads it. Null without a playback device. Thread pool.</summary>
    public (float Level, bool Muted)? Open()
    {
        lock (_gate)
        {
            try
            {
                _vol?.UnregisterControlChangeNotify(this);
                _vol = null;
                if (_enumerator is null)
                {
                    _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                    _enumerator.RegisterEndpointNotificationCallback(this);
                }
                if (_enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev) != 0) return null;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out var o) != 0) return null;
                var vol = (IAudioEndpointVolume)o;
                vol.GetMasterVolumeLevelScalar(out var level);
                vol.GetMute(out var muted);
                vol.RegisterControlChangeNotify(this);
                _vol = vol;
                return (level, muted);
            }
            catch (Exception ex) { Log.Warn("Volume endpoint unavailable", ex); return null; }
        }
    }

    public void SetLevel(float level) => Try(v => v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), ref _self));
    public void SetMute(bool mute) => Try(v => v.SetMute(mute, ref _self));

    void Try(Action<IAudioEndpointVolume> f)
    {
        try { if (_vol is { } v) f(v); }
        catch (Exception ex) { Log.Warn("Volume change failed", ex); }
    }

    unsafe int IAudioEndpointVolumeCallback.OnNotify(nint data)
    {
        var d = (Notification*)data;
        if (d->Context != _self) Changed?.Invoke(d->Level, d->Muted != 0);
        return 0;
    }

    int IMMNotificationClient.OnDeviceStateChanged(nint id, uint state) => 0;
    int IMMNotificationClient.OnDeviceAdded(nint id) => 0;
    int IMMNotificationClient.OnDeviceRemoved(nint id) => 0;
    int IMMNotificationClient.OnPropertyValueChanged(nint id, PropertyKey key) => 0;
    int IMMNotificationClient.OnDefaultDeviceChanged(int flow, int role, nint id)
    {
        if (flow == 0 && role == 1) DefaultChanged?.Invoke();
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey { public Guid Format; public uint Id; }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged(nint id, uint state);
        [PreserveSig] int OnDeviceAdded(nint id);
        [PreserveSig] int OnDeviceRemoved(nint id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, nint id);
        [PreserveSig] int OnPropertyValueChanged(nint id, PropertyKey key);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Notification { public Guid Context; public int Muted; public float Level; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice(nint id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    // Vtable order matters: the unused slots stay as placeholders.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback cb);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback cb);
        void GetChannelCount(out int count);
        void SetMasterVolumeLevel(float db, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float db);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(int channel, float db, ref Guid context);
        void SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        void GetChannelVolumeLevel(int channel, out float db);
        void GetChannelVolumeLevelScalar(int channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(nint data);
    }
}
