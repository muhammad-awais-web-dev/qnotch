using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using QNotch.Interop;
using QNotch.Shell;
using QNotch.Theme;

namespace QNotch.Modules.Screenshots;

internal sealed class ScreenshotsTab : DockPanel
{
    readonly IShell _shell;
    readonly ScreenshotSettings _settings;
    readonly WrapPanel _tiles = new();
    readonly Grid _body = new();
    readonly TextBlock _status = UiKit.Text("", "Muted");
    readonly ScrollViewer _shelf;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    ScreenshotFile[] _files = [];
    string? _loadedFolder;
    bool _reading;

    string Folder => _settings.Folder ?? ScreenshotFiles.DefaultFolder();

    public ScreenshotsTab(IShell shell, ScreenshotSettings settings, Action saveSettings, bool demo)
    {
        _shell = shell;
        _settings = settings;
        Margin = new Thickness(20, 3, 17, 10);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var folder = Button("Choose folder", () =>
        {
            var picker = new OpenFolderDialog { Title = "Choose your Snipping Tool screenshots folder", InitialDirectory = Folder };
            using (_shell.HoldOpen())
            {
                if (picker.ShowDialog() != true) return;
                _settings.Folder = picker.FolderName;
                saveSettings();
                Refresh();
            }
        });
        folder.IsEnabled = !demo;
        DockPanel.SetDock(folder, Dock.Right);
        header.Children.Add(folder);
        var save = Button("Save clipboard image", SaveClipboard);
        save.IsEnabled = !demo;
        save.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(save, Dock.Right);
        header.Children.Add(save);
        var title = UiKit.Text("Screenshots", "Title");
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);
        DockPanel.SetDock(_status, Dock.Bottom);
        Children.Add(_status);
        _shelf = new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _body.Children.Add(_shelf);
        Children.Add(_body);
        if (demo)
        {
            for (var index = 0; index < 3; index++)
            {
                var swatch = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(31, (byte)(56 + 17 * index), 98)), null,
                    new RectangleGeometry(new Rect(0, 0, 196, 96)));
                AddTile(new DrawingImage(swatch), $"Screenshot {index + 1}.png", null);
            }
            return;
        }

        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); _timer.Start(); }
            else _timer.Stop();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    async void Refresh()
    {
        if (_reading) return;
        _reading = true;
        var folder = Folder;
        var previousFiles = _files;
        var previousFolder = _loadedFolder;
        try
        {
            var result = await Task.Run(() =>
            {
                var files = ScreenshotFiles.Recent(folder);
                if (previousFolder == folder && previousFiles.SequenceEqual(files)) return null;
                return files.Select(file => (File: file, Thumb: LoadThumbnail(file.Path))).Where(item => item.Thumb is not null).ToArray();
            });
            if (folder != Folder || result is null) return;
            _loadedFolder = folder;
            _files = result.Select(item => item.File).ToArray();
            _tiles.Children.Clear();
            foreach (var item in result) AddTile(item.Thumb!, Path.GetFileName(item.File.Path), item.File);
            _status.Text = result.Length == 0 ? "Take a snip with Win+Shift+S, or choose the folder where Snipping Tool saves it." : "Drag a screenshot to another app, or click to preview.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = "The Screenshots folder could not be read.";
        }
        finally { _reading = false; }
    }

    static BitmapSource? LoadThumbnail(string path)
    {
        try { return ScreenshotFiles.Decode(path, 420); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException) { return null; }
    }

    void AddTile(ImageSource thumbnail, string name, ScreenshotFile? file)
    {
        var image = new Image { Source = thumbnail, Height = 88, Stretch = Stretch.UniformToFill };
        var caption = new TextBlock { Text = name, FontSize = 11, Margin = new Thickness(8, 6, 8, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new StackPanel { Width = 200, ClipToBounds = true };
        content.Children.Add(image);
        content.Children.Add(caption);
        var tile = new Button { Content = content, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 10), ToolTip = name };
        AutomationProperties.SetName(tile, name);
        _tiles.Children.Add(tile);
        if (file is null) return;
        Point? dragStart = null;
        tile.Click += (_, _) => Preview(file);
        tile.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(this);
        tile.PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var current = e.GetPosition(this);
            if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;
            tile.ReleaseMouseCapture();
            DragFile(tile, file, thumbnail);
        };
    }

    void DragFile(Button tile, ScreenshotFile file, ImageSource thumbnail)
    {
        if (!File.Exists(file.Path)) { Refresh(); return; }
        var preview = new Popup
        {
            Placement = PlacementMode.Relative, PlacementTarget = this, AllowsTransparency = true, StaysOpen = true,
            Child = new Border { Child = new Image { Source = thumbnail, MaxWidth = 92, MaxHeight = 64 },
                Background = Brushes.Black, Padding = new Thickness(3), Opacity = 0.85, IsHitTestVisible = false },
        };
        GiveFeedbackEventHandler follow = (_, _) =>
        {
            if (!Native.GetCursorPos(out var cursor)) return;
            var point = PointFromScreen(new Point(cursor.X, cursor.Y));
            preview.HorizontalOffset = point.X + 18;
            preview.VerticalOffset = point.Y + 18;
            preview.IsOpen = true;
        };
        tile.GiveFeedback += follow;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { file.Path });
            using (_shell.HoldOpen()) DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy);
        }
        finally
        {
            preview.IsOpen = false;
            tile.GiveFeedback -= follow;
        }
    }

    void Preview(ScreenshotFile file)
    {
        var bitmap = LoadThumbnail(file.Path);
        if (bitmap is null) { Refresh(); return; }
        var view = new DockPanel();
        var actions = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var back = Button("Back", () => { _body.Children.Remove(view); _shelf.Visibility = Visibility.Visible; });
        var copy = Button("Copy image", () =>
        {
            try { System.Windows.Clipboard.SetImage(ScreenshotFiles.Decode(file.Path)); _status.Text = "Screenshot copied."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ExternalException)
            { _status.Text = "Could not copy the screenshot."; }
        });
        DockPanel.SetDock(back, Dock.Left);
        DockPanel.SetDock(copy, Dock.Right);
        actions.Children.Add(back);
        actions.Children.Add(copy);
        actions.Children.Add(new TextBlock { Text = Path.GetFileName(file.Path), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(actions, Dock.Top);
        view.Children.Add(actions);
        view.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
        _shelf.Visibility = Visibility.Collapsed;
        _body.Children.Add(view);
    }

    async void SaveClipboard()
    {
        try
        {
            var bitmap = System.Windows.Clipboard.GetImage();
            if (bitmap is null) { _status.Text = "The clipboard has no image."; return; }
            bitmap.Freeze();
            var folder = Folder;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}.png");
                using var stream = File.Open(path, FileMode.CreateNew);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            });
            Refresh();
            _status.Text = "Clipboard image saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        { _status.Text = "Could not save the clipboard image."; }
    }

    static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5) };
        button.Click += (_, _) => click();
        return button;
    }
}
