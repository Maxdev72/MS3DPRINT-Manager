using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class LegacySettingsMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-legacy-settings-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_root, "install", "data");
    private string Stable => Path.Combine(_root, "profile", "data");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void WriteLegacy(string name, string content)
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, name), content);
    }

    [Fact]
    public void Settings_next_to_the_program_are_imported_backed_up_and_left_in_place()
    {
        WriteLegacy("settings.json", """{"Theme":2}""");
        WriteLegacy("client-codes.json", """{"a":"A"}""");

        var result = LegacySettingsMigration.Run(Legacy, Stable);

        Assert.Equal(LegacySettingsMigrationOutcome.Imported, result.Outcome);
        Assert.Equal("""{"Theme":2}""", File.ReadAllText(Path.Combine(Stable, "settings.json")));
        Assert.Equal("""{"a":"A"}""", File.ReadAllText(Path.Combine(Stable, "client-codes.json")));
        Assert.True(File.Exists(Path.Combine(Legacy, "settings.json")));
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "settings.json")));
    }

    [Fact]
    public void Existing_stable_settings_are_never_overwritten()
    {
        WriteLegacy("workspace.json", """{"Root":"C:\\autre"}""");
        Directory.CreateDirectory(Stable);
        File.WriteAllText(Path.Combine(Stable, "workspace.json"), """{"Root":"C:\\atelier"}""");

        var result = LegacySettingsMigration.Run(Legacy, Stable);

        Assert.Equal(LegacySettingsMigrationOutcome.StableSettingsAlreadyPresent, result.Outcome);
        Assert.Equal("""{"Root":"C:\\atelier"}""", File.ReadAllText(Path.Combine(Stable, "workspace.json")));
    }

    [Fact]
    public void An_unreadable_file_is_skipped_and_reported()
    {
        WriteLegacy("settings.json", """{"Theme":1}""");
        WriteLegacy("performance.json", "{ pas du json");

        var result = LegacySettingsMigration.Run(Legacy, Stable);

        Assert.True(File.Exists(Path.Combine(Stable, "settings.json")));
        Assert.False(File.Exists(Path.Combine(Stable, "performance.json")));
        Assert.Contains("performance.json", result.Skipped);
    }

    [Fact]
    public void Only_known_preference_files_are_imported()
    {
        WriteLegacy("settings.json", "{}");
        WriteLegacy("projet-client.json", "{}");
        Directory.CreateDirectory(Path.Combine(Legacy, ".ms3dprint-manager"));

        LegacySettingsMigration.Run(Legacy, Stable);

        Assert.False(File.Exists(Path.Combine(Stable, "projet-client.json")));
        Assert.False(Directory.Exists(Path.Combine(Stable, ".ms3dprint-manager")));
    }

    [Fact]
    public void The_import_runs_only_once()
    {
        WriteLegacy("settings.json", """{"Theme":1}""");
        LegacySettingsMigration.Run(Legacy, Stable);
        File.Delete(Path.Combine(Stable, "settings.json"));

        var second = LegacySettingsMigration.Run(Legacy, Stable);

        Assert.Equal(LegacySettingsMigrationOutcome.AlreadyImported, second.Outcome);
        Assert.False(File.Exists(Path.Combine(Stable, "settings.json")));
    }

    [Fact]
    public void Nothing_happens_without_legacy_settings_or_when_both_folders_are_the_same()
    {
        Assert.Equal(LegacySettingsMigrationOutcome.NothingToImport, LegacySettingsMigration.Run(Legacy, Stable).Outcome);
        WriteLegacy("settings.json", "{}");
        Assert.Equal(LegacySettingsMigrationOutcome.NothingToImport, LegacySettingsMigration.Run(Legacy, Legacy + Path.DirectorySeparatorChar).Outcome);
        Assert.False(Directory.Exists(Stable));
    }
}
