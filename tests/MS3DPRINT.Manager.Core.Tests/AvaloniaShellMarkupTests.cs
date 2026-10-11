namespace MS3DPRINT.Manager.Core.Tests;

public sealed class AvaloniaShellMarkupTests
{
    [Theory]
    [InlineData("Accueil")]
    [InlineData("Projets")]
    [InlineData("Clients")]
    [InlineData("Collections")]
    [InlineData("Filaments")]
    [InlineData("Estimations")]
    [InlineData("Imprimantes")]
    [InlineData("Corbeille")]
    [InlineData("Paramètres")]
    public void Main_window_exposes_each_workspace_destination(string label)
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains(label, markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_can_select_a_registered_printer()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("EstimatePrinter", markup, StringComparison.Ordinal);
        Assert.Contains("EstimatePrinter_SelectionChanged", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_a_dashboard_and_command_search()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("Vue d’ensemble", markup, StringComparison.Ordinal);
        Assert.Contains("Ctrl+K", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_wires_navigation_and_the_search_shortcut()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("NavigationButton_Click", markup, StringComparison.Ordinal);
        Assert.Contains("Window_KeyDown", markup, StringComparison.Ordinal);
        Assert.Contains("SearchBox", markup, StringComparison.Ordinal);
        Assert.Contains("SearchBox_KeyUp", markup, StringComparison.Ordinal);
    }

    private static string MainWindowPath()
        => Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
