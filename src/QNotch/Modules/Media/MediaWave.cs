using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QNotch.Core;
using QNotch.Modules.Volume;

namespace QNotch.Modules.Media;

/// <summary>Recent speaker peaks shown as a tiny waveform beside the current track.</summary>
internal sealed class MediaWave : StackPanel
{
    readonly ScaleTransform[] _bars = new ScaleTransform[5];
    CancellationTokenSource? _watch;

    public MediaWave(bool demo)
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(8, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        double[] idle = [0.22, 0.38, 0.6, 0.38, 0.22];
        for (var index = 0; index < _bars.Length; index++)
        {
            var scale = _bars[index] = new ScaleTransform(1, idle[index]);
            var bar = new Border { Width = 3, Height = 14, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, index == _bars.Length - 1 ? 0 : 2, 0),
                RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5) };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            Children.Add(bar);
        }
        if (demo) return;
        Loaded += (_, _) => { Motion.Changed += UpdateWatch; UpdateWatch(); };
        Unloaded += (_, _) => { Motion.Changed -= UpdateWatch; StopWatch(); };
        IsVisibleChanged += (_, _) => UpdateWatch();
    }

    void UpdateWatch()
    {
        if (!IsLoaded || !IsVisible || !Motion.Enabled) { StopWatch(); return; }
        if (_watch is not null) return;
        var watch = _watch = new CancellationTokenSource();
        _ = Task.Run(() => Watch(watch));
    }

    void StopWatch()
    {
        _watch?.Cancel();
        _watch = null;
    }

    async Task Watch(CancellationTokenSource watch)
    {
        var ct = watch.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(65));
        VolumeCom.IAudioMeterInformation? meter = null;
        var readUntil = DateTime.UtcNow;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float peak = 0;
                try
                {
                    if (DateTime.UtcNow >= readUntil)
                    {
                        readUntil = DateTime.UtcNow.AddSeconds(5);
                        meter = VolumeCom.OpenPlaybackMeter();
                    }
                    if (meter is not null && meter.GetPeakValue(out peak) < 0) peak = 0;
                }
                catch (COMException) { meter = null; }
                var height = 0.2 + Math.Sqrt(Math.Clamp(peak, 0, 1)) * 0.8;
                await Dispatcher.InvokeAsync(() =>
                {
                    for (var index = 0; index < _bars.Length - 1; index++)
                        _bars[index].ScaleY = _bars[index].ScaleY * 0.3 + _bars[index + 1].ScaleY * 0.7;
                    _bars[^1].ScaleY = _bars[^1].ScaleY * 0.25 + height * 0.75;
                }, DispatcherPriority.Background, ct).Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
        finally { watch.Dispose(); }
    }
}
