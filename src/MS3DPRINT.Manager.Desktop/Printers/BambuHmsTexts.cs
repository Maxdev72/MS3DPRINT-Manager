using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop.Printers;

/// <summary>
/// French texts of the printers' alerts, from Bambu Lab's public catalogue. Kept in the user's profile and refreshed
/// once a week; without network the last copy is used, and without any copy the alert shows its code.
/// </summary>
public static class BambuHmsTexts
{
    private const string Source = "https://e.bambulab.com/query.php?lang=fr";
    private static readonly string CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "MS3DPRINT Manager", "bambu-hms-fr.json");
    private static Task<IReadOnlyDictionary<string, string>>? _loading;

    /// <summary>Empty until the first load ends; <see cref="LoadAsync"/> fills it.</summary>
    public static IReadOnlyDictionary<string, string> Current { get; private set; } = new Dictionary<string, string>();

    public static Task<IReadOnlyDictionary<string, string>> LoadAsync() => _loading ??= LoadCoreAsync();

    private static async Task<IReadOnlyDictionary<string, string>> LoadCoreAsync()
    {
        string? json = null;
        try { if (File.Exists(CachePath)) json = await File.ReadAllTextAsync(CachePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        var stale = json is null || File.GetLastWriteTimeUtc(CachePath) < DateTime.UtcNow.AddDays(-7);
        if (stale)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var fresh = await http.GetStringAsync(Source);
                if (BambuHmsCatalog.Parse(fresh).Count > 0)
                {
                    json = fresh;
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    await File.WriteAllTextAsync(CachePath, fresh);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
        }
        Current = json is null ? Current : BambuHmsCatalog.Parse(json);
        return Current;
    }
}
