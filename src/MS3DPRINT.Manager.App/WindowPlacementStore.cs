using System.Text.Json;

namespace MS3DPRINT.Manager.App;

public sealed record WindowPlacement(double Width, double Height);

public sealed class WindowPlacementStore
{
    private readonly string _dataDirectory;
    private readonly string _path;

    public WindowPlacementStore(string? dataDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
        _path = Path.Combine(_dataDirectory, "window-placement.json");
    }

    public WindowPlacement? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var placement = JsonSerializer.Deserialize<WindowPlacement>(stream);
            return placement is { Width: > 0, Height: > 0 } && double.IsFinite(placement.Width) && double.IsFinite(placement.Height)
                ? placement
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Les dimensions de fenêtre doivent être positives.");

        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = Path.Combine(_dataDirectory, "window-placement." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new WindowPlacement(width, height));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
