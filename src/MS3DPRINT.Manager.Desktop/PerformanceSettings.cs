using System.Text.Json;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>The performance mode chosen by the user, kept next to the other settings in <c>data/performance.json</c>.</summary>
public sealed class PerformanceSettings
{
    private sealed record Stored(string? Mode);

    private readonly string _directory;
    private readonly string _path;

    public PerformanceSettings(string? dataDirectory = null)
    {
        _directory = Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
        _path = Path.Combine(_directory, "performance.json");
    }

    /// <summary>The saved mode; Auto when nothing is saved or the file cannot be read.</summary>
    public PerformanceMode Load()
    {
        try
        {
            if (!File.Exists(_path)) return PerformanceMode.Auto;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            return Enum.TryParse<PerformanceMode>(stored?.Mode, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : PerformanceMode.Auto;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return PerformanceMode.Auto;
        }
    }

    public void Save(PerformanceMode mode)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, "performance." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Stored(mode.ToString()), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }

    public static string Label(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Economy => "Économie",
        PerformanceMode.Maximum => "Maximum",
        _ => "Automatique"
    };

    public static string Description(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Economy => "Un quart des threads du processeur et une miniature à la fois. Pour un portable sur batterie ou un ordinateur déjà occupé.",
        PerformanceMode.Maximum => "Tous les threads du processeur. Les estimations, l’orientation automatique et l’aperçu logiciel sont les plus rapides.",
        _ => "La moitié des threads du processeur. Le compromis conseillé : rapide, sans gêner le reste de l’ordinateur."
    };
}
