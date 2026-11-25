using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using QNotch.Core;
using QNotch.Theme;

namespace QNotch.Modules.Capture;

internal sealed partial class CaptureState : ObservableObject
{
    [ObservableProperty] bool _micInUse;
    [ObservableProperty] bool _cameraInUse;
    [ObservableProperty] bool _anyInUse;
    /// <summary>"Microphone: Teams. Camera: Teams." for screen readers and the tooltip.</summary>
    [ObservableProperty] string _description = "";
}

/// <summary>
/// Mic and camera indicators in the small dock, with app names on Home. Two <see cref="CaptureWatch"/>es report
/// changes from the registry; there is no timer.
/// </summary>
public sealed class CaptureModule : INotchModule
{
    const string Mic = "", Cam = "";

    readonly CaptureState _st = new();
    IReadOnlyList<string> _mic = [], _cam = [];
    CaptureWatch? _micWatch, _camWatch; // kept alive for the app's lifetime

    public void Initialize(ModuleContext ctx)
    {
        ctx.Segments.Register(new SegmentDescriptor("capture.pill", SegmentSlot.Glance, 5, () => Segment(labeled: true)));
        ctx.Segments.Register(new SegmentDescriptor("capture.home", SegmentSlot.HomeStatus, 0, Details));
        ctx.Segments.Register(new SegmentDescriptor("capture.game", SegmentSlot.GameBar, 12, () => Segment(),
            "Mic and camera", "Only while an app uses the microphone or camera."));

        if (ctx.Settings.ReadOnly) // snapshot run: demo data, no watchers
        {
            Apply(["Teams"], ["Teams"]);
            ctx.Demos.Register(new DemoState("capture.idle", "idle", 20, () => Demo([], [])));
            ctx.Demos.Register(new DemoState("capture.capture", "capture", 20, () => Demo(["Teams"], ["Teams"])));
            return;
        }
        _micWatch = new CaptureWatch(CaptureWatch.Microphone, apps => ctx.Bus.Run(() => Apply(apps, _cam)));
        _camWatch = new CaptureWatch(CaptureWatch.Camera, apps => ctx.Bus.Run(() => Apply(_mic, apps)));
    }

    Action Demo(IReadOnlyList<string> mic, IReadOnlyList<string> cam)
    {
        var (m, c) = (_mic, _cam);
        Apply(mic, cam);
        return () => Apply(m, c);
    }

    void Apply(IReadOnlyList<string> mic, IReadOnlyList<string> cam)
    {
        (_mic, _cam) = (mic, cam);
        _st.MicInUse = mic.Count > 0;
        _st.CameraInUse = cam.Count > 0;
        _st.AnyInUse = _st.MicInUse || _st.CameraInUse;
        var parts = new List<string>();
        if (mic.Count > 0) parts.Add($"Microphone: {string.Join(", ", mic)}.");
        if (cam.Count > 0) parts.Add($"Camera: {string.Join(", ", cam)}.");
        _st.Description = string.Join(" ", parts);
    }

    FrameworkElement Details()
    {
        var description = UiKit.Text("", "Muted");
        description.TextWrapping = TextWrapping.Wrap;
        UiKit.Bind(description, TextBlock.TextProperty, _st, nameof(CaptureState.Description));
        var row = new DockPanel();
        var icons = Segment();
        icons.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icons);
        row.Children.Add(description);
        var card = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        return UiKit.BindVisible(card, _st, nameof(CaptureState.AnyInUse));
    }

    FrameworkElement Segment(bool labeled = false)
    {
        FrameworkElement Device(string glyph, string brush, string label, string active)
        {
            var icon = UiKit.Glyph(glyph, 12, brush);
            icon.VerticalAlignment = VerticalAlignment.Center;
            if (!labeled) return UiKit.BindVisible(icon, _st, active);
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 3, 0) };
            item.Children.Add(icon);
            item.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return UiKit.BindVisible(item, _st, active);
        }
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Device(Mic, "WarningBrush", "Mic in use", nameof(CaptureState.MicInUse)));
        p.Children.Add(Device(Cam, "SuccessBrush", "Camera in use", nameof(CaptureState.CameraInUse)));
        UiKit.Bind(p, AutomationProperties.NameProperty, _st, nameof(CaptureState.Description));
        UiKit.Bind(p, FrameworkElement.ToolTipProperty, _st, nameof(CaptureState.Description));
        return UiKit.BindVisible(p, _st, nameof(CaptureState.AnyInUse));
    }
}
