using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QNotch.Core;

namespace QNotch.Shell;

public partial class NotchWindow
{
    const double GlanceNoticeDiameter = 24;
    int _glanceNoticeGeneration;

    internal bool HasGlanceNotice => GlanceNotice.Visibility == Visibility.Visible;

    internal void ShowGlanceNotice(string glyph, string text, string brushKey, int holdMs, Action finished)
    {
        HideGlanceNotice();
        var generation = _glanceNoticeGeneration;
        GlanceNoticeGlyph.Text = glyph;
        GlanceNoticeGlyph.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        GlanceNoticeText.Text = text;
        GlanceNotice.Visibility = Visibility.Visible;
        GlanceNoticeContent.Measure(new Size(double.PositiveInfinity, GlanceNoticeDiameter));
        var width = Math.Max(GlanceNoticeDiameter, Math.Ceiling(GlanceNoticeContent.DesiredSize.Width) + 2);
        GlanceNotice.Width = width;

        var size = new DoubleAnimationUsingKeyFrames();
        var hold = Math.Clamp(holdMs, 450, 10000);
        if (Motion.Enabled)
        {
            GlanceNotice.RenderTransformOrigin = new Point(0.5, 0.5);
            var grow = new DoubleAnimation(0.12, 1, TimeSpan.FromMilliseconds(115))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            GlanceNoticeScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            GlanceNoticeScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            var openEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            var closeEase = new CubicEase { EasingMode = EasingMode.EaseInOut };
            size.KeyFrames.Add(new DiscreteDoubleKeyFrame(GlanceNoticeDiameter, At(0)));
            size.KeyFrames.Add(new DiscreteDoubleKeyFrame(GlanceNoticeDiameter, At(115)));
            size.KeyFrames.Add(new EasingDoubleKeyFrame(width, At(330), openEase));
            size.KeyFrames.Add(new DiscreteDoubleKeyFrame(width, At(hold + 210)));
            size.KeyFrames.Add(new EasingDoubleKeyFrame(GlanceNoticeDiameter, At(hold + 440), closeEase));
            FadeNotice(GlanceNoticeContent, (0, 0), (0, 185), (1, 330), (1, hold), (0, hold + 190));
            FadeNotice(GlanceNotice, (0, 0), (1, 85), (1, hold + 440), (0, hold + 540));
        }
        size.KeyFrames.Add(new DiscreteDoubleKeyFrame(Motion.Enabled ? GlanceNoticeDiameter : width, At(hold + 540)));
        size.Completed += (_, _) =>
        {
            if (generation != _glanceNoticeGeneration) return;
            HideGlanceNotice();
            finished();
        };
        GlanceNotice.BeginAnimation(WidthProperty, size);
    }

    internal void HideGlanceNotice()
    {
        ++_glanceNoticeGeneration;
        GlanceNotice.Visibility = Visibility.Collapsed;
        GlanceNotice.BeginAnimation(WidthProperty, null);
        GlanceNotice.BeginAnimation(OpacityProperty, null);
        GlanceNoticeContent.BeginAnimation(OpacityProperty, null);
        GlanceNoticeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GlanceNoticeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    static KeyTime At(int milliseconds) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));

    static void FadeNotice(UIElement element, params (double Opacity, int Milliseconds)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var frame in frames) animation.KeyFrames.Add(new LinearDoubleKeyFrame(frame.Opacity, At(frame.Milliseconds)));
        element.BeginAnimation(OpacityProperty, animation);
    }
}
