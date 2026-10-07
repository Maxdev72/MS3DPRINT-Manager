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
    {
        if (!File.Exists(_path)) return ThemePreference.Automatic;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = JsonSerializer.Deserialize<ThemeSettings>(stream);
            return settings?.Theme ?? ThemePreference.Automatic;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return ThemePreference.Automatic;
        }
    }

    public void Save(ThemePreference preference)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = Path.Combine(_dataDirectory, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new ThemeSettings(preference), new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record ThemeSettings(ThemePreference Theme);
}
