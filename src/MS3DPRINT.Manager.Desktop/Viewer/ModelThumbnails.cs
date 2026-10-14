using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>
/// Small pictures of STL and OBJ files for the file lists. They are drawn by the CPU renderer in the background (two at a time),
/// kept on disk until the file changes, and skipped for very large files.
/// </summary>
public static class ModelThumbnails
{
    public const int Size = 112;
    /// <summary>The larger picture shown when the pointer rests on a model in a list.</summary>
    public const int PreviewSize = 360;
    private const long MaxFileBytes = 40L * 1024 * 1024;
    private const uint Background = 0xFF0E1522;
    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);
    private static int _pruned;

    public static string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "thumbnails");

    /// <summary>Where the picture of this version of the file is kept; another size or date gives another name.</summary>
    public static string CachePath(string modelPath, long length, DateTime lastWriteUtc, int size = Size)
    {
        var key = $"{Path.GetFullPath(modelPath).ToUpperInvariant()}|{length}|{lastWriteUtc.Ticks}|{size}";
        return Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".png");
    }

    /// <summary>Deletes pictures not used for three months; runs once per launch, in the background.</summary>
    public static void PruneOnce()
    {
        if (Interlocked.Exchange(ref _pruned, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;
                foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*.png"))
                    if (DateTime.UtcNow - File.GetLastAccessTimeUtc(file) > MaxAge && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge) File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* a cache that cannot be tidied is harmless */ }
        });
    }

    public static bool IsEligible(string path)
        => ThreeDFileSupport.IsPreviewable(path) && new FileInfo(path) is { Exists: true, Length: > 0 and <= MaxFileBytes };

    /// <summary>The thumbnail of a model, or null when it cannot be made (too large, unreadable, not a model).</summary>
    public static async Task<Bitmap?> GetAsync(string path, CancellationToken cancellationToken = default, int size = Size)
    {
        try
        {
            if (!IsEligible(path)) return null;
            PruneOnce();
            var info = new FileInfo(path);
            var cached = CachePath(path, info.Length, info.LastWriteTimeUtc, size);
            if (File.Exists(cached)) return new Bitmap(cached);
            await Gate.WaitAsync(cancellationToken);
            try { return await Task.Run(() => Render(path, cached, size, cancellationToken), cancellationToken); }
            finally { Gate.Release(); }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or FormatException
            or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            return null;
        }
    }

    private static Bitmap Render(string path, string cached, int size, CancellationToken cancellationToken)
    {
        var mesh = MeshLoader.Load(path, cancellationToken);
        var pixels = SoftwareRenderer.Render(mesh, new ViewCamera { Zoom = size > Size ? 1.15 : 1.6 }, new RenderOptions(size, size, false, 0xFF58A6FF, Background), null, cancellationToken);
        using var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
            for (var row = 0; row < size; row++)
                System.Runtime.InteropServices.Marshal.Copy((int[])(object)pixels, row * size, buffer.Address + row * buffer.RowBytes, size);
        Directory.CreateDirectory(CacheDirectory);
        var temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            bitmap.Save(temporary, PngBitmapEncoderOptions.Default);
            File.Move(temporary, cached, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new Bitmap(cached);
    }
}
