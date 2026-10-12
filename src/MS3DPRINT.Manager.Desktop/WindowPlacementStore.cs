using System.Text.Json;

namespace MS3DPRINT.Manager.Desktop;

public sealed record WindowPlacement(double Width, double Height, bool Maximized);

/// <summary>Remembers the size of the main window between two launches.</summary>
public sealed class WindowPlacementStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "window.json");

    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_path));
            return placement is { Width: > 100 and < 20000, Height: > 100 and < 20000 } ? placement : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the size must never get in the way of closing the window.
        }
    }
}
