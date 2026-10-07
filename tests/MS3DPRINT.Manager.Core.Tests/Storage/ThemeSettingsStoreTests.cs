using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class ThemeSettingsStoreTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "MS3DPRINT-theme-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsAutomaticWhenNoPreferenceHasBeenSaved()
    {
        Assert.Equal(ThemePreference.Automatic, new ThemeSettingsStore(_dataDirectory).Load());
    }

    [Fact]
    public void Save_PersistsTheSelectedThemeForANewStore()
    {
        new ThemeSettingsStore(_dataDirectory).Save(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, new ThemeSettingsStore(_dataDirectory).Load());
    }

    [Fact]
    public void Save_RestoresTheThemeAndAccentTogetherForANewStore()
    {
        new ThemeSettingsStore(_dataDirectory).Save(new ThemeAppearance(ThemePreference.Paper, AccentPreference.Yellow));
        var reloaded = new ThemeSettingsStore(_dataDirectory).LoadAppearance();
        Assert.Equal(ThemePreference.Paper, reloaded.Theme);
        Assert.Equal(AccentPreference.Yellow, reloaded.Accent);
    }

    [Fact]
    public void LoadAppearance_OldSettingsKeepTheirThemeWithDefaultBlueAccent()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(Path.Combine(_dataDirectory, "settings.json"), "{\"Theme\":2}");
        var appearance = new ThemeSettingsStore(_dataDirectory).LoadAppearance();
        Assert.Equal(ThemePreference.Dark, appearance.Theme);
        Assert.Equal(AccentPreference.Blue, appearance.Accent);
    }

    [Fact]
    public void LoadAppearance_UnknownAccentKeepsTheThemeWithDefaultBlueAccent()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(Path.Combine(_dataDirectory, "settings.json"), "{\"Theme\":3,\"Accent\":999}");
        var appearance = new ThemeSettingsStore(_dataDirectory).LoadAppearance();
        Assert.Equal(ThemePreference.Amoled, appearance.Theme);
        Assert.Equal(AccentPreference.Blue, appearance.Accent);
    }

    [Fact]
    public void Save_ThemeOnlyPreservesThePreviouslySelectedAccent()
    {
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(_dataDirectory, "settings.json");
        File.WriteAllText(path, "{\"Theme\":2,\"Accent\":3}");

        new ThemeSettingsStore(_dataDirectory).Save(ThemePreference.Light);

        using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(3, saved.RootElement.GetProperty("Accent").GetInt32());
        Assert.Equal(ThemePreference.Light, new ThemeSettingsStore(_dataDirectory).Load());
    }

    [Fact]
    public void Load_UnknownThemeFallsBackToAutomatic()
    {
        Directory.CreateDirectory(_dataDirectory);
        File.WriteAllText(Path.Combine(_dataDirectory, "settings.json"), "{\"Theme\":999}");
        Assert.Equal(ThemePreference.Automatic, new ThemeSettingsStore(_dataDirectory).Load());
    }

    [Fact]
    public void Load_ReturnsAutomaticAndPreservesTheFileWhenSavedSettingsAreInvalid()
    {
        Directory.CreateDirectory(_dataDirectory);
        var settingsPath = Path.Combine(_dataDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{");

        var theme = new ThemeSettingsStore(_dataDirectory).Load();

        Assert.Equal(ThemePreference.Automatic, theme);
        Assert.Equal("{", File.ReadAllText(settingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }
}
