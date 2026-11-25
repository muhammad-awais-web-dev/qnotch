using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using QNotch.Core;
using QNotch.Theme;

namespace QNotch.Modules.Fdm;

internal sealed record DownloadTransfer(string Source, double Percent, string? Speed = null, string? Remaining = null, string? Status = null);

internal sealed partial class FdmState : ObservableObject
{
    [ObservableProperty] double _progress;
    [ObservableProperty] bool _active;
    [ObservableProperty] DownloadTransfer? _transfer;
}

/// <summary>Reads active FDM progress through its Windows UI Automation progress bars.</summary>
public sealed class FdmModule : INotchModule, ICadenceAware
{
    readonly FdmState _state = new();
    readonly FdmCheckpoints _checkpoints = new();
    DispatcherTimer? _timer;
    bool _reading;

    public void Initialize(ModuleContext ctx)
    {
        ctx.Segments.Register(new SegmentDescriptor("fdm.progress", SegmentSlot.PillProgress, 0, () => Progress(_state, line: false)));
        ctx.Segments.Register(new SegmentDescriptor("fdm.home-downloads", SegmentSlot.HomeDownloads, 0, () => Downloads(_state)));
        if (ctx.Settings.ReadOnly)
        {
            _state.Transfer = new DownloadTransfer("FDM", 58, "1.3 MB/s", "24m 10s");
            _state.Progress = 58;
            _state.Active = true;
            ctx.Demos.Register(new DemoState("fdm.idle", "idle", 40, () =>
            {
                _state.Active = false;
                return () => _state.Active = true;
            }));
            ctx.Demos.Register(new DemoState("fdm.progress", "download", 40, () =>
            {
                _state.Progress = 58;
                _state.Active = true;
                return () => _state.Active = false;
            }));
            ctx.Demos.Register(new DemoState("fdm.near-complete", "download-near-complete", 40, () =>
            {
                _state.Progress = 99;
                _state.Active = true;
                return () => { _state.Progress = 58; _state.Active = false; };
            }));
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background, ctx.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _timer.Tick += (_, _) => ReadProgress(ctx);
        _timer.Start();
        ReadProgress(ctx);
    }

    public void SetCadence(Cadence cadence) { }

    static FrameworkElement Downloads(FdmState state)
    {
        var heading = new TextBlock { Text = "Downloads", FontSize = 12, FontWeight = FontWeights.SemiBold };
        var source = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        source.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var label = new Border { Child = source, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4) };
        label.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        var details = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        details.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var status = new DockPanel { Margin = new Thickness(0, 5, 0, 6) };
        DockPanel.SetDock(label, Dock.Left);
        label.Margin = new Thickness(0, 0, 8, 0);
        status.Children.Add(label);
        status.Children.Add(details);
        var content = new StackPanel();
        content.Children.Add(heading);
        content.Children.Add(status);
        var progress = Progress(state, line: true);
        progress.Height = 5;
        content.Children.Add(progress);
        var card = new Border { Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        void Refresh()
        {
            if (state.Transfer is not { } transfer) return;
            source.Text = transfer.Source;
            var parts = new List<string> { $"{transfer.Percent:0.#}%" };
            if (transfer.Speed is { } speed) parts.Add(speed);
            if (transfer.Remaining is { } remaining) parts.Add($"{remaining} left");
            if (transfer.Status is { } status) parts.Add(status);
            details.Text = string.Join("  ·  ", parts);
        }
        state.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FdmState.Transfer)) Refresh(); };
        Refresh();
        return UiKit.BindVisible(card, state, nameof(FdmState.Active));
    }

    static FrameworkElement Progress(FdmState state, bool line)
    {
        var track = new Grid { ClipToBounds = true };
        if (line) track.SetResourceReference(Panel.BackgroundProperty, "ControlBrush");
        var corners = line ? new CornerRadius(1) : new CornerRadius(0, 0, 0, 16);
        var progressScale = new ScaleTransform(0, 1);
        var completed = new Grid { RenderTransform = progressScale };
        var fill = new Border { CornerRadius = corners, Opacity = line ? 0.72 : 0.28 };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        completed.Children.Add(fill);
        track.Children.Add(completed);
        var drift = new TranslateTransform();
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, line ? 0.5 : 0.85), EndPoint = new Point(1, line ? 0.5 : 0.15),
            RelativeTransform = drift,
            GradientStops = new GradientStopCollection
            {
                new(Colors.Transparent, 0.22),
                new(Color.FromArgb(line ? (byte)86 : (byte)38, 255, 255, 255), 0.5),
                new(Colors.Transparent, 0.78),
            },
        };
        var wave = new Border { CornerRadius = corners, Background = sheen };
        completed.Children.Add(wave);
        void UpdateProgress(bool animate)
        {
            var target = Math.Clamp(state.Progress / 100d, 0, 1);
            var from = progressScale.ScaleX;
            progressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            progressScale.ScaleX = target;
            if (!animate || !Motion.Enabled || !track.IsVisible || !state.Active) return;
            progressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(650))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.Stop,
                });
        }
        void UpdateMotion()
        {
            var animate = track.IsVisible && track.IsLoaded && Motion.Enabled;
            if (!animate) UpdateProgress(animate: false);
            wave.Opacity = animate ? 1 : 0;
            drift.BeginAnimation(TranslateTransform.XProperty, animate
                ? new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(line ? 2.8 : 3.2)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
        }
        track.IsVisibleChanged += (_, _) => UpdateMotion();
        track.Loaded += (_, _) => { Motion.Changed += UpdateMotion; UpdateMotion(); };
        track.Unloaded += (_, _) => { Motion.Changed -= UpdateMotion; drift.BeginAnimation(TranslateTransform.XProperty, null); };
        state.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FdmState.Progress)) UpdateProgress(animate: true); };
        UpdateProgress(animate: false);
        return UiKit.BindVisible(track, state, nameof(FdmState.Active));
    }

    void ReadProgress(ModuleContext ctx)
    {
        if (_reading) return;
        _reading = true;
        _ = Task.Run(ReadFdm).ContinueWith(task => ctx.Dispatcher.BeginInvoke(() =>
        {
            _reading = false;
            if (task.Status != TaskStatus.RanToCompletion) return;
            _state.Transfer = task.Result;
            if (task.Result is { } transfer) _state.Progress = transfer.Percent;
            _state.Active = task.Result is not null;
        }), TaskScheduler.Default);
    }

    static readonly Regex Speed = new(@"\d+(?:[.,]\d+)?\s*(?:[KMGT]i?)?B/s\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex Eta = new(@"^(?:\d+\s*[dhms]\s*)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    DownloadTransfer? ReadFdm()
    {
        var processes = Process.GetProcessesByName("fdm");
        foreach (var process in processes)
        {
            using (process)
            {
                if (process.MainWindowHandle == 0) continue;
                try
                {
                    var window = AutomationElement.FromHandle(process.MainWindowHandle);
                    var bars = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar));
                    var progress = bars.Cast<AutomationElement>()
                        .Select(bar => bar.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern)
                            ? ((RangeValuePattern)pattern).Current.Value
                            : -1d)
                        .FirstOrDefault(value => value is >= 0 and < 100, -1d);
                    if (progress < 0) continue;
                    var labels = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                    var text = labels.Cast<AutomationElement>().Select(label => label.Current.Name.Trim()).ToArray();
                    var speed = text.Select(label => Speed.Match(label)).FirstOrDefault(match => match.Success)?.Value;
                    var etaIndex = Array.FindIndex(text, label => label.Equals("Remaining:", StringComparison.OrdinalIgnoreCase));
                    var remaining = etaIndex >= 0 && etaIndex + 1 < text.Length && Eta.IsMatch(text[etaIndex + 1])
                        ? text[etaIndex + 1] : null;
                    return new DownloadTransfer("FDM", progress, speed, remaining);
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
            }
        }
        return processes.Length > 0 ? _checkpoints.Read() : null;
    }
}
