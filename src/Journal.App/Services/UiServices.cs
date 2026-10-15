using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Journal.Core.Documents;
using Journal.Core.Services;
using Microsoft.Win32;

namespace Journal.App.Services;

/// <summary>Dialogs used by view models; faked in tests.</summary>
public interface IDialogService
{
    string? OpenFile(string title, string filter);
    string[] OpenFiles(string title, string filter);
    string? SaveFile(string title, string fileName, string filter);
    string? PickFolder(string title, string? initial = null);
    bool Confirm(string title, string message, string confirmText = "OK");
    void Info(string title, string message);
    void Error(string title, string message);
    /// <summary>Asks for a tag name, offering existing tags. Returns null when cancelled.</summary>
    string? PromptTag(string title, IReadOnlyList<string> suggestions, string? initial = null);
    void ShowImage(ImageSource image, string caption);
    void ShowRecoveryCode(string code);
}

/// <summary>Puts text on the Windows clipboard; faked in tests.</summary>
public interface IClipboardService
{
    /// <summary>Returns false when the clipboard is busy (another app has it open).</summary>
    bool TrySetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }
}

/// <summary>Opens files with their default Windows application.</summary>
public interface IShellLauncher
{
    void Open(string path);
}

public sealed class ShellLauncher : IShellLauncher
{
    public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

/// <summary><see cref="IScheduler"/> on the WPF dispatcher.</summary>
public sealed class DispatcherScheduler(Dispatcher dispatcher) : IScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(delay, DispatcherPriority.Background, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            action();
        }, dispatcher);
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

/// <summary>Decodes and caches image blobs for display; cleared on lock.</summary>
public sealed class ImageCache(Func<BlobRef, Task<byte[]>> read)
{
    private readonly Dictionary<(Guid, int), ImageSource> _cache = [];

    public async Task<ImageSource?> LoadAsync(BlobRef blob, int decodePixelWidth)
    {
        if (_cache.TryGetValue((blob.Id, decodePixelWidth), out var cached)) return cached;
        var bytes = await read(blob);
        var image = Decode(bytes, decodePixelWidth);
        _cache[(blob.Id, decodePixelWidth)] = image;
        return image;
    }

    public void Clear() => _cache.Clear();

    public static BitmapSource Decode(byte[] bytes, int decodePixelWidth = 0)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>Validates image files and reads their pixel size (FR-IMG-02).</summary>
public static class ImageProbe
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".webp", ".jxr", ".ico"];

    public static bool LooksLikeImage(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Returns the pixel size, or null when Windows can't decode the data.</summary>
    public static (int Width, int Height)? TryGetSize(Stream stream)
    {
        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        finally
        {
            if (stream.CanSeek) stream.Position = 0;
        }
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

public static class ContentTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv", [".json"] = "application/json",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".mp3"] = "audio/mpeg", [".mp4"] = "video/mp4", [".xml"] = "application/xml",
    };

    public static string For(string fileName) =>
        Map.TryGetValue(Path.GetExtension(fileName), out var t) ? t : "application/octet-stream";
}
