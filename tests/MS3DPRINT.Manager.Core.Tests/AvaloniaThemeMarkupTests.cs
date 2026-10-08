namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AvaloniaThemeMarkupTests
{
    [Theory]
    [InlineData("CanvasBrush")]
    [InlineData("SurfaceBrush")]
    [InlineData("RaisedSurfaceBrush")]
    [InlineData("AccentBrush")]
    [InlineData("TextBrush")]
    [InlineData("MutedTextBrush")]
    [InlineData("FocusBorderBrush")]
    public void Theme_defines_required_semantic_resource(string resourceKey)
    {
        var path = ThemePath();
        Assert.True(File.Exists(path), "The Avalonia theme file must exist.");
        var markup = File.ReadAllText(path);

        Assert.Contains($"x:Key=\"{resourceKey}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_does_not_offer_a_light_mode()
    {
        var path = ThemePath();
        Assert.True(File.Exists(path), "The Avalonia theme file must exist.");
        var markup = File.ReadAllText(path);

        Assert.DoesNotContain("LightTheme", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ThemeVariant.Light", markup, StringComparison.OrdinalIgnoreCase);
    }

    private static string ThemePath()
        => Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Theme.axaml");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
