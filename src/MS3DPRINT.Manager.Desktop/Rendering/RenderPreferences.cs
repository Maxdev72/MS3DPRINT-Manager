using System.Text.Json;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>The render page as it was left: the user's own colours, the last choices (list positions on the page) and the camera.</summary>
public sealed record RenderPreferences(IReadOnlyList<RenderColor> CustomColors, string Hex = "#D93A3A", int Finish = 0, int Layer = 3, int Scene = 0,
    string Background = "#2F6FD6", int Format = 0, string? Environment = null, double EnvironmentRotation = 0, double Yaw = -0.6, double Pitch = 0.35, double Zoom = 1)
{
    public static RenderPreferences Default { get; } = new([]);
}

/// <summary>Kept on this PC, next to the window placement.</summary>
// shortcut: custom colours are per PC, not per workshop; move them into the workshop metadata if several PCs share one workshop.
public sealed class RenderPreferencesStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "render.json");

    public RenderPreferences Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<RenderPreferences>(File.ReadAllText(_path)) ?? RenderPreferences.Default : RenderPreferences.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return RenderPreferences.Default;
        }
    }

    public void Save(RenderPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the page's last choices must never stop a render.
        }
    }
}
