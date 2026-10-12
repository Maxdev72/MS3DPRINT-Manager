using System.Text.Json;
using Avalonia;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Size, state and (when known) top-left corner in screen pixels of the main window; a file written before positions were kept has none.</summary>
public sealed record WindowPlacement(double Width, double Height, bool Maximized, int? X = null, int? Y = null);

/// <summary>Remembers the size, state and position of the main window between two launches.</summary>
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

    /// <summary>
    /// A window is worth restoring at its saved place only if its title bar can still be reached: a monitor that was unplugged
    /// or a resolution change must not leave it out of sight.
    /// </summary>
    public static bool IsReachable(PixelRect window, IEnumerable<PixelRect> workAreas)
    {
        var titleBar = new PixelRect(window.X, window.Y, Math.Max(1, window.Width), 40);
        return workAreas.Any(area =>
        {
            var overlap = area.Intersect(titleBar);
            return overlap.Width >= 120 && overlap.Height >= 30;
        });
    }

    /// <summary>Top-left corner that centres a window of this size in a work area.</summary>
    public static PixelPoint CentreIn(PixelRect workArea, PixelSize size)
        => new(workArea.X + Math.Max(0, (workArea.Width - size.Width) / 2), workArea.Y + Math.Max(0, (workArea.Height - size.Height) / 2));

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(placement));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the placement must never get in the way of closing the window.
        }
    }
}
