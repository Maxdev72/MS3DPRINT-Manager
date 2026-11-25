using System.Text.Json;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>A camera the user saved for a part: the 3D view's turn, height, zoom and pan.</summary>
public sealed record SavedCamera(string Name, double Yaw, double Pitch, double Zoom, double PanX, double PanY);

/// <summary>The render page as it was left: colours, choices (list positions on the page), camera, and per-part orientation and cameras.</summary>
public sealed record RenderPreferences
{
    public static RenderPreferences Default { get; } = new();

    public IReadOnlyList<RenderColor> CustomColors { get; init; } = [];
    /// <summary>Palette and workshop colours the user removed, as "#RRGGBB|finish".</summary>
    public IReadOnlyList<string> HiddenColors { get; init; } = [];
    public string Hex { get; init; } = "#DE4343";
    public int Finish { get; init; }
    public int Layer { get; init; } = 3;
    public int Scene { get; init; }
    public string Background { get; init; } = "#2F6FD6";
    public int Format { get; init; }
    public string? Environment { get; init; }
    public double EnvironmentRotation { get; init; }
    public double Yaw { get; init; } = -0.6;
    public double Pitch { get; init; } = 0.35;
    public double Zoom { get; init; } = 1;
    public int Tab { get; init; }
    public double RotationStep { get; init; } = 15;
    public bool Shadow { get; init; } = true;
    public double ShadowStrength { get; init; } = 0.3;
    public double ShadowSoftness { get; init; } = 0.5;
    public double LightAngle { get; init; }
    public double LightHeight { get; init; } = 39;
    public bool GlowDark { get; init; }
    /// <summary>Orientation of each part (by file path) as a quaternion X, Y, Z, W.</summary>
    public IReadOnlyDictionary<string, float[]> Orientations { get; init; } = new Dictionary<string, float[]>();
    /// <summary>Cameras saved for each part (by file path).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SavedCamera>> Cameras { get; init; } = new Dictionary<string, IReadOnlyList<SavedCamera>>();
}

/// <summary>Kept on this PC, next to the window placement.</summary>
// shortcut: custom colours are per PC, not per workshop; move them into the workshop metadata if several PCs share one workshop.
public sealed class RenderPreferencesStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData, System.Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "render.json");

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
