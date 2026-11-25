using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using QNotch.Theme;

namespace QNotch.Modules.Media;

/// <summary>Pill and game bar views of the Media module. Built in code: no XAML parse on the cold start path.</summary>
internal static class MediaSegments
{
    const string NoteGlyph = "";

    /// <summary>Pill: artwork and track details during playback, local time otherwise.</summary>
    public static FrameworkElement Pill(MediaState m, bool demo)
    {
        var glyph = UiKit.Glyph(NoteGlyph, 11);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var placeholder = new Border { CornerRadius = new CornerRadius(6), Child = glyph };
        placeholder.SetResourceReference(Border.BackgroundProperty, "ControlHoverBrush");
        var art = new ImageBrush { Stretch = Stretch.UniformToFill };
        UiKit.Bind(art, ImageBrush.ImageSourceProperty, m, nameof(MediaState.Artwork));
        var tile = new Grid { Width = 22, Height = 22 };
        tile.Children.Add(placeholder);
        tile.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = art });

        var title = new TextBlock { FontWeight = FontWeights.SemiBold, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        UiKit.Bind(title, TextBlock.TextProperty, m, nameof(MediaState.Title));
        var artist = new TextBlock { MaxWidth = 80, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        artist.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        UiKit.Bind(artist, TextBlock.TextProperty, m, nameof(MediaState.Artist));

        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(tile);
        p.Children.Add(title);
        p.Children.Add(artist);
        p.Children.Add(new MediaWave(demo));
        var clock = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var clockStyle = new Style(typeof(TextBlock));
        var playing = new DataTrigger { Binding = new Binding(nameof(MediaState.IsNowPlaying)) { Source = m }, Value = true };
        playing.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        clockStyle.Triggers.Add(playing);
        clock.Style = clockStyle;

        var timer = new DispatcherTimer(DispatcherPriority.Background, clock.Dispatcher);
        void UpdateClock()
        {
            var now = DateTime.Now;
            clock.Text = now.ToShortTimeString();
            timer.Interval = TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(now.Ticks % TimeSpan.TicksPerMinute);
        }
        timer.Tick += (_, _) => UpdateClock();
        clock.IsVisibleChanged += (_, _) =>
        {
            if (clock.IsVisible)
            {
                UpdateClock();
                timer.Start();
            }
            else timer.Stop();
        };
        UpdateClock();

        var pill = new Grid();
        pill.Children.Add(UiKit.BindVisible(p, m, nameof(MediaState.IsNowPlaying)));
        pill.Children.Add(clock);
        return pill;
    }

    /// <summary>Game bar: the same text line, no artwork. Font size comes from the bar.</summary>
    public static FrameworkElement Game(MediaState m)
    {
        var t = new TextBlock { FontWeight = FontWeights.SemiBold, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        UiKit.Bind(t, TextBlock.TextProperty, m, nameof(MediaState.NowPlayingText));
        return UiKit.BindVisible(t, m, nameof(MediaState.IsNowPlaying));
    }
}
