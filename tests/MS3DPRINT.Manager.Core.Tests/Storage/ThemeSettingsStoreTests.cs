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
