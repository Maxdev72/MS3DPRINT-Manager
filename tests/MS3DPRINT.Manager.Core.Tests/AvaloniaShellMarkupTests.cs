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
    public void Estimate_workspace_wires_saved_estimate_loading()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("LoadSavedEstimate_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_updates_the_loaded_estimate_without_creating_a_duplicate()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("_loadedEstimate", source, StringComparison.Ordinal);
        Assert.Contains("store.Update", source, StringComparison.Ordinal);
        Assert.Contains("EstimateProject.SelectedItem", source, StringComparison.Ordinal);
        Assert.Contains("EstimateFilament.SelectedItem", source, StringComparison.Ordinal);
        Assert.Contains("EstimatePrinter.SelectedItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_templates_tolerate_empty_items()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("f?.Brand", source, StringComparison.Ordinal);
        Assert.Contains("printer?.Name", source, StringComparison.Ordinal);
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
    public void Main_window_keeps_only_the_central_workspace_within_a_1200_pixel_reading_width()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("<Grid x:Name=\"CentralWorkspace\" Grid.Column=\"1\" Margin=\"34,28\" Width=\"1200\"", markup, StringComparison.Ordinal);
        Assert.Contains("Window_SizeChanged", markup, StringComparison.Ordinal);
        Assert.Contains("UpdateCentralWorkspaceWidth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid ColumnDefinitions=\"232,*\" MaxWidth=\"1200\"", markup, StringComparison.Ordinal);
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

    [Fact]
    public void Main_window_browses_non_project_folders_inside_the_main_workspace()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("FolderWorkspacePanel", markup, StringComparison.Ordinal);
        Assert.Contains("LoadFolderWorkspaceAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new FolderBrowserWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_cards_fill_the_available_workspace_and_open_on_click()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));

        Assert.Contains("<ScrollViewer Grid.Row=\"1\" HorizontalScrollBarVisibility=\"Disabled\" HorizontalContentAlignment=\"Stretch\">", markup, StringComparison.Ordinal);
        Assert.Contains("<StackPanel x:Name=\"ProjectList\" HorizontalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\" HorizontalScrollBarVisibility=\"Disabled\" HorizontalContentAlignment=\"Stretch\"><StackPanel x:Name=\"ClientList\" HorizontalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("button.Click += ProjectEntry_Click", source, StringComparison.Ordinal);
        Assert.Contains("projectButton.Click += ProjectEntry_Click", source, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"workspace-card\" }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_folder_commands_are_grouped_in_a_single_toolbar()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("var toolbar = new Grid", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(actions, 1)", source, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"primary-action\" }", source, StringComparison.Ordinal);
        Assert.Contains("Button.primary-action", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_lists_use_compact_table_headers_and_rows()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("CreateCompactTableHeader", source, StringComparison.Ordinal);
        Assert.Contains("\"PROJET\", \"CLIENT\", \"STATUT\"", source, StringComparison.Ordinal);
        Assert.Contains("\"CLIENT\", \"FICHE\", \"PROJETS\"", source, StringComparison.Ordinal);
        Assert.Contains("\"table-row\"", source, StringComparison.Ordinal);
        Assert.Contains("Button.table-row", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_action_sets_use_a_compact_overflow_menu()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml.cs"));
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("CreateOverflowActions", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"⋯\"", source, StringComparison.Ordinal);
        Assert.Contains("Button.icon-action", styles, StringComparison.Ordinal);
        Assert.Contains("Button.menu-action", styles, StringComparison.Ordinal);
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
