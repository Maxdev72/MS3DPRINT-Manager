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
        var source = MainWindowSource();

        Assert.Contains("LoadSavedEstimate_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_updates_the_loaded_estimate_without_creating_a_duplicate()
    {
        var source = MainWindowSource();

        Assert.Contains("_loadedEstimate", source, StringComparison.Ordinal);
        Assert.Contains("store.Update", source, StringComparison.Ordinal);
        Assert.Contains("EstimateProject.SelectedItem", source, StringComparison.Ordinal);
        Assert.Contains("EstimateFilament.SelectedItem", source, StringComparison.Ordinal);
        Assert.Contains("EstimatePrinter.SelectedItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_templates_tolerate_empty_items()
    {
        var source = MainWindowSource();

        Assert.Contains("f?.Brand", source, StringComparison.Ordinal);
        Assert.Contains("printer is null", source, StringComparison.Ordinal);
        Assert.Contains("printer.DisplayName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_can_select_a_registered_printer()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("EstimatePrinter", markup, StringComparison.Ordinal);
        Assert.Contains("EstimatePrinter_SelectionChanged", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_exposes_pricing_inputs_and_a_modelling_only_mode()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = MainWindowSource();

        Assert.Contains("EstimateKwhPrice", markup, StringComparison.Ordinal);
        Assert.Contains("EstimateScope", markup, StringComparison.Ordinal);
        Assert.Contains("Modélisation seule", markup, StringComparison.Ordinal);
        Assert.Contains("EstimatePrintInputs", markup, StringComparison.Ordinal);
        Assert.Contains("EstimateScope_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_values_are_read_on_the_ui_thread_before_background_work()
    {
        var offenders = new List<string>();
        foreach (var file in MainWindowSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("Task.Run(", StringComparison.Ordinal)) continue;
                // Inspect only the lines of this Task.Run statement, up to the one that closes it.
                for (var offset = 0; offset < 4 && index + offset < lines.Length; offset++)
                {
                    var line = lines[index + offset];
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(dialog|completion|naming)\.[A-Z]"))
                        offenders.Add($"{Path.GetFileName(file)} ligne {index + offset + 1}: {line.Trim()}");
                    if (line.TrimEnd().EndsWith(");", StringComparison.Ordinal)) break;
                }
            }
        }

        Assert.True(offenders.Count == 0, "Lecture d'un contrôle de fenêtre dans Task.Run : " + string.Join(" | ", offenders));
    }

    [Fact]
    public void Client_sheet_can_create_a_project_for_that_client()
    {
        var source = MainWindowSource();
        var dialog = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "CreateProjectWindow.cs"));

        Assert.Contains("Nouveau projet", source, StringComparison.Ordinal);
        Assert.Contains("CreateProjectForClient_Click", source, StringComparison.Ordinal);
        Assert.Contains("preselected", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_files_offer_an_open_with_action()
    {
        var source = MainWindowSource();
        var host = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Windows.App", "Program.cs"));

        Assert.Contains("Ouvrir avec…", source, StringComparison.Ordinal);
        Assert.Contains("OpenWithFile_Click", source, StringComparison.Ordinal);
        Assert.Contains("\"openas\"", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_workspace_keeps_units_visible_beside_numeric_values()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var printerDialog = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "CreatePrinterWindow.cs"));
        var filamentDialog = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "CreateFilamentWindow.cs"));

        Assert.Contains("Text=\"g\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"€/h\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"%\"", markup, StringComparison.Ordinal);
        Assert.Contains("CreateUnitField", printerDialog, StringComparison.Ordinal);
        Assert.Contains("€/kg", filamentDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_exposes_a_dashboard_and_command_search()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("Vue d’ensemble", markup, StringComparison.Ordinal);
        Assert.Contains("Ctrl+K", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_opens_centered_on_the_screen()
    {
        var markup = File.ReadAllText(MainWindowPath());

        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_keeps_only_the_central_workspace_within_a_1200_pixel_reading_width()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = MainWindowSource();

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
        var source = MainWindowSource();

        Assert.Contains("FolderWorkspacePanel", markup, StringComparison.Ordinal);
        Assert.Contains("LoadFolderWorkspaceAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new FolderBrowserWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_cards_fill_the_available_workspace_and_open_on_click()
    {
        var markup = File.ReadAllText(MainWindowPath());
        var source = MainWindowSource();

        Assert.Contains("<ScrollViewer Grid.Row=\"1\" HorizontalScrollBarVisibility=\"Disabled\" HorizontalContentAlignment=\"Stretch\">", markup, StringComparison.Ordinal);
        Assert.Contains("<StackPanel x:Name=\"ProjectList\" HorizontalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"2\" HorizontalScrollBarVisibility=\"Disabled\" HorizontalContentAlignment=\"Stretch\"><StackPanel x:Name=\"ClientList\" HorizontalAlignment=\"Stretch\"", markup, StringComparison.Ordinal);
        Assert.Contains("open.Click += ProjectEntry_Click", source, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"workspace-card\" }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_folder_commands_are_grouped_in_a_single_toolbar()
    {
        var source = MainWindowSource();
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("var toolbar = new Grid", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(actions, 1)", source, StringComparison.Ordinal);
        Assert.Contains("Classes = { \"primary-action\" }", source, StringComparison.Ordinal);
        Assert.Contains("Button.primary-action", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_lists_use_compact_table_headers_and_rows()
    {
        var source = MainWindowSource();
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("CreateCompactTableHeader", source, StringComparison.Ordinal);
        Assert.Contains("\"PROJET\", \"CLIENT\", \"STATUT\"", source, StringComparison.Ordinal);
        Assert.Contains("\"CLIENT\", \"CODE\", \"FICHE\", \"PROJETS\"", source, StringComparison.Ordinal);
        Assert.Contains("\"table-row\"", source, StringComparison.Ordinal);
        Assert.Contains("Button.table-row", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_action_sets_use_a_compact_overflow_menu()
    {
        var source = MainWindowSource();
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("CreateOverflowActions", source, StringComparison.Ordinal);
        Assert.Contains("IconMore", source, StringComparison.Ordinal);
        Assert.Contains("Button.icon-action", styles, StringComparison.Ordinal);
        Assert.Contains("Button.menu-action", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Overflow_action_menu_does_not_render_a_second_presenter_surface()
    {
        var source = MainWindowSource();
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "Styles", "Controls.axaml"));

        Assert.Contains("FlyoutPresenterClasses.Add(\"action-menu-flyout\")", source, StringComparison.Ordinal);
        Assert.Contains("FlyoutPresenter.action-menu-flyout", styles, StringComparison.Ordinal);
    }

    private static string MainWindowPath()
        => Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop", "MainWindow.axaml");

    /// <summary>The code-behind of the main window is split in several partial files: all of them, in a stable order.</summary>
    private static string[] MainWindowSourceFiles()
        => Directory.GetFiles(Path.Combine(RepositoryRoot(), "src", "MS3DPRINT.Manager.Desktop"), "MainWindow*.cs").OrderBy(file => file, StringComparer.Ordinal).ToArray();

    private static string MainWindowSource() => string.Join(Environment.NewLine, MainWindowSourceFiles().Select(File.ReadAllText));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS3DPRINT.Manager.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
