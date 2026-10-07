using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Storage;

public sealed class ThemeSettingsStore
{
    private readonly string _dataDirectory;
    private readonly string _path;

    public ThemeSettingsStore(string? dataDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data"));
        _path = Path.Combine(_dataDirectory, "settings.json");
    }

    public ThemePreference Load()
        => LoadAppearance().Theme;

    public ThemeAppearance LoadAppearance()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = JsonSerializer.Deserialize<ThemeAppearance>(stream) ?? new();
            return new(
                Enum.IsDefined(settings.Theme) ? settings.Theme : ThemePreference.Automatic,
                Enum.IsDefined(settings.Accent) ? settings.Accent : AccentPreference.Blue);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(ThemePreference preference)
        => Save(LoadAppearance() with { Theme = preference });

    public void Save(ThemeAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (!Enum.IsDefined(appearance.Theme)) throw new ArgumentOutOfRangeException(nameof(appearance));
        if (!Enum.IsDefined(appearance.Accent)) throw new ArgumentOutOfRangeException(nameof(appearance));
        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = Path.Combine(_dataDirectory, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, appearance, new JsonSerializerOptions { WriteIndented = true });
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
