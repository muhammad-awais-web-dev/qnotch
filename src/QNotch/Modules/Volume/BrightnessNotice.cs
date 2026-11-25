using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace QNotch.Modules.Volume;

internal static partial class BrightnessNotice
{
    static readonly Guid CurrentBrightness = new("8FFEE2C6-2D01-46BE-ADB9-398ADDC5B4FF");

    internal static void Register(ModuleContext ctx)
    {
        const int PowerBroadcast = 0x0218, SettingChanged = 0x8013;
        var started = Stopwatch.GetTimestamp();
        int? previous = null;
        var overlay = new DispatcherTimer(DispatcherPriority.Normal, ctx.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
        overlay.Tick += (_, _) =>
        {
            overlay.Stop();
            ctx.Shell.ShowGlanceNotice("", $"Brightness {previous}%", "AccentBrush", 1250);
        };
        ctx.Shell.AddHwndHook((nint window, int message, nint wp, nint lp, ref bool handled) =>
        {
            if (message != PowerBroadcast || wp != SettingChanged || lp == 0 || Marshal.PtrToStructure<Guid>(lp) != CurrentBrightness) return 0;
            var length = Marshal.ReadInt32(lp, 16);
            var level = length switch { 1 => Marshal.ReadByte(lp, 20), 4 => Marshal.ReadInt32(lp, 20), _ => -1 };
            if (level is < 0 or > 100 || previous == level) return 0;
            previous = level;
            if (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(1)) return 0;
            overlay.Stop();
            overlay.Start();
            return 0;
        });
        var registration = RegisterPowerSettingNotification(ctx.Shell.Hwnd, CurrentBrightness, 0);
        if (registration != 0) ctx.Dispatcher.ShutdownStarted += (_, _) => UnregisterPowerSettingNotification(registration);
    }

    [LibraryImport("user32.dll")]
    private static partial nint RegisterPowerSettingNotification(nint recipient, in Guid setting, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterPowerSettingNotification(nint registration);
}
