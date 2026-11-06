using System.Text.Json;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Storage;

public enum LegacySettingsMigrationOutcome { NothingToImport, Imported, StableSettingsAlreadyPresent, AlreadyImported }

public sealed record LegacySettingsMigrationResult(LegacySettingsMigrationOutcome Outcome, string? BackupDirectory,
    IReadOnlyList<string> Imported, IReadOnlyList<string> Skipped);

/// <summary>
/// One-time import of the machine preferences once kept in <c>data/</c> next to the executable into the stable profile
/// folder. The originals stay in place, a backup is taken first, and nothing is imported once stable settings exist.
/// Workspace records (<c>.ms3dprint-manager</c>, documents) are never touched here.
/// </summary>
public static partial class LegacySettingsMigration
{
    private const string BackupFolder = "legacy-import";

    [GeneratedRegex(@"^(workspace|settings|performance|client-codes(\.previous(\.[0-9a-f]{32})?)?)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex KnownFile();

    public static LegacySettingsMigrationResult Run(string legacyDirectory, string stableDirectory)
    {
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        var stable = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stableDirectory));
        var candidates = !string.Equals(legacy, stable, StringComparison.OrdinalIgnoreCase) && Directory.Exists(legacy)
            ? Directory.EnumerateFiles(legacy).Where(path => KnownFile().IsMatch(Path.GetFileName(path))).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        if (candidates.Count == 0) return new(LegacySettingsMigrationOutcome.NothingToImport, null, [], []);
        var receipt = Path.Combine(stable, BackupFolder, "receipt.json");
        if (File.Exists(receipt)) return new(LegacySettingsMigrationOutcome.AlreadyImported, null, [], []);
        if (Directory.Exists(stable) && Directory.EnumerateFiles(stable).Any(path => KnownFile().IsMatch(Path.GetFileName(path))))
            return new(LegacySettingsMigrationOutcome.StableSettingsAlreadyPresent, null, [], []);

        var backup = Path.Combine(stable, BackupFolder, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"));
        Directory.CreateDirectory(backup);
        foreach (var path in candidates) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));

        var imported = new List<string>();
        var skipped = new List<string>();
        foreach (var path in candidates)
        {
            var name = Path.GetFileName(path);
            var target = Path.Combine(stable, name);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = File.ReadAllBytes(path);
                using (JsonDocument.Parse(bytes)) { }
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, target, overwrite: false);
                imported.Add(name);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                skipped.Add(name);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Written last: an interrupted import leaves no receipt, and the stable files it already wrote stop a second import.
        File.WriteAllText(receipt, JsonSerializer.Serialize(new { Source = legacy, Backup = backup, Imported = imported, Skipped = skipped }));
        return new(LegacySettingsMigrationOutcome.Imported, backup, imported, skipped);
    }
}
