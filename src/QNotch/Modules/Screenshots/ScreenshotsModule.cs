using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace QNotch.Modules.Screenshots;

public sealed class ScreenshotSettings
{
    public string? Folder { get; set; }
}

public sealed class ScreenshotsModule : INotchModule
{
    internal const string Icon = "";
    const string Id = "screenshots";

    public void Initialize(ModuleContext ctx)
    {
        var settings = ctx.Settings.Get<ScreenshotSettings>(Id);
        ctx.Tabs.Register(new TabDescriptor(Id, "Screenshots", Icon, 42,
            () => new ScreenshotsTab(ctx.Shell, settings, () => ctx.Settings.Save(Id, settings), ctx.Settings.ReadOnly)));
    }
}

internal sealed record ScreenshotFile(string Path, DateTime Modified, long Length);

internal static partial class ScreenshotFiles
{
    static readonly Guid FolderId = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    internal static string DefaultFolder()
    {
        SHGetKnownFolderPath(FolderId, 0x4000, 0, out var path);
        try
        {
            return Marshal.PtrToStringUni(path) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    internal static ScreenshotFile[] Recent(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return new DirectoryInfo(folder).EnumerateFiles()
            .Where(file => file.Extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(12)
            .Select(file => new ScreenshotFile(file.FullName, file.LastWriteTime, file.Length))
            .ToArray();
    }

    internal static BitmapSource Decode(string path, int width = 0)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) image.DecodePixelWidth = width;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint user, out nint path);
}
