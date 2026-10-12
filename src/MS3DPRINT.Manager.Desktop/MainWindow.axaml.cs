using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow : Window
{
    private sealed record ProjectDirectoryContext(ProjectSummary Project, string Directory, string Path);
    private const string DefaultWorkspaceRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
    private CancellationTokenSource? _searchCancellation;
    private Button? _selectedNavigationButton;
    private EstimateInputs? _latestEstimateInputs;
    private EstimateResult? _latestEstimateResult;
    private EstimateProfile? _loadedEstimate;
    private string? _folderWorkspaceRoot;
    private string? _folderWorkspaceDirectory;
    private string? _folderWorkspaceReturnTitle;
    private string? _folderWorkspaceReturnSubtitle;

    public MainWindow() => InitializeComponent();

    private bool _preloaded;
    private string _collectionCategoryFilter = string.Empty;

    private async void Window_Opened(object? sender, EventArgs e)
    {
        UpdateCentralWorkspaceWidth(Bounds.Width);
        var shortcut = 1;
        foreach (var navigation in NavigationButtons().Take(9))
            ToolTip.SetTip(navigation, $"{navigation.Tag} (Ctrl+{shortcut++})");
        await PreloadAsync();
        // MS3DPRINT_START_PAGE=Clients opens that page directly (screenshots, demos).
        if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PAGE") is { Length: > 0 } page &&
            NavigationButtons().FirstOrDefault(button => button.Tag as string == page) is { } target)
            NavigationButton_Click(target, new RoutedEventArgs());
        // MS3DPRINT_START_PROJECT=<reference> opens that project (screenshots, demos).
        if (Environment.GetEnvironmentVariable("MS3DPRINT_START_PROJECT") is { Length: > 0 } reference)
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root));
            if (projects.FirstOrDefault(project => project.Reference == reference) is { } found)
            {
                _projectOriginClient = null;
                ShowWorkspace("Projets");
                PageTitle.Text = found.Title();
                PageSubtitle.Text = found.Reference;
                await LoadProjectDirectoryAsync(found, found.ProjectPath);
            }
        }
        // MS3DPRINT_START_DIALOG=client|project|printer|filament|collection|toast opens that dialog (screenshots, demos).
        switch (Environment.GetEnvironmentVariable("MS3DPRINT_START_DIALOG"))
        {
            case "client": CreateClient_Click(this, new RoutedEventArgs()); break;
            case "project": CreateProject_Click(this, new RoutedEventArgs()); break;
            case "printer": CreatePrinter_Click(this, new RoutedEventArgs()); break;
            case "filament": CreateFilament_Click(this, new RoutedEventArgs()); break;
            case "collection": CreateCollection_Click(this, new RoutedEventArgs()); break;
            case "toast":
                Notify("Projet créé avec succès.");
                Notify("Impossible d’écrire dans ce dossier : accès refusé.", error: true);
                break;
        }
    }

    /// <summary>Reads the dashboard data once, so a splash screen can wait for it before showing the window.</summary>
    public async Task PreloadAsync(bool force = false)
    {
        if (_preloaded && !force) return;
        _preloaded = true;
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;

        try
        {
            var dashboard = await Task.Run(() => new WorkspaceDashboardReader().Load(workspaceRoot));
            ProjectCountText.Text = dashboard.ProjectCount.ToString();
            QuoteCountText.Text = dashboard.QuoteCount.ToString();
            InProgressCountText.Text = dashboard.InProgressCount.ToString();
            ClientCountText.Text = dashboard.ClientCount switch { 0 => "Aucun client", 1 => "1 client", var count => $"{count} clients" };
            WorkspaceStatusText.Text = dashboard.CompletedCount switch { 0 => "Aucun projet terminé pour l’instant.", 1 => "1 projet terminé.", var count => $"{count} projets terminés." };
            BuildRecentProjects(dashboard.RecentProjects);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            WorkspaceStatusText.Text = "Impossible de lire l’espace de travail.";
        }
    }

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateCentralWorkspaceWidth(e.NewSize.Width);

    private void UpdateCentralWorkspaceWidth(double windowWidth)
    {
        const double sidebarWidth = 232;
        const double centralMargins = 68;
        CentralWorkspace.Width = Math.Min(1200, Math.Max(0, windowWidth - sidebarWidth - centralMargins));
    }

    private ClientSummary? _projectOriginClient;
    private ClientSummary? _currentClientDetail;

    /// <summary>Shows exactly one workspace panel and highlights its menu entry.</summary>
    private void ShowWorkspace(string destination)
    {
        foreach (var navigationButton in NavigationButtons())
        {
            if (navigationButton.Tag as string == destination) navigationButton.Classes.Add("selected");
            else navigationButton.Classes.Remove("selected");
        }
        var dashboard = destination == "Accueil";
        DashboardStats.IsVisible = dashboard;
        DashboardContent.IsVisible = dashboard;
        ProjectsWorkspacePanel.IsVisible = destination == "Projets";
        ClientsWorkspacePanel.IsVisible = destination == "Clients";
        CollectionsWorkspacePanel.IsVisible = destination == "Collections";
        FilamentsWorkspacePanel.IsVisible = destination == "Filaments";
        EstimatesWorkspacePanel.IsVisible = destination == "Estimations";
        PrintersWorkspacePanel.IsVisible = destination == "Imprimantes";
        TrashWorkspacePanel.IsVisible = destination == "Corbeille";
        SettingsWorkspacePanel.IsVisible = destination == "Paramètres";
        FolderWorkspacePanel.IsVisible = false;
        _selectedNavigationButton = NavigationButtons().FirstOrDefault(button => button.Tag as string == destination);
    }

    private async void NavigationButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string destination)
            return;

        ShowWorkspace(destination);
        PageTitle.Text = destination;
        PageSubtitle.Text = PageSubtitles.GetValueOrDefault(destination, string.Empty);
        _projectOriginClient = null;
        var projectsVisible = destination == "Projets";
        var clientsVisible = destination == "Clients";
        var collectionsVisible = destination == "Collections";
        var filamentsVisible = destination == "Filaments";
        var estimatesVisible = destination == "Estimations";
        var printersVisible = destination == "Imprimantes";
        var trashVisible = destination == "Corbeille";
        var settingsVisible = destination == "Paramètres";

        BeginBusy();
        try
        {
            if (destination == "Accueil")
                await PreloadAsync(force: true);
            else if (projectsVisible)
                await LoadProjectsAsync();
            else if (clientsVisible)
                await LoadClientsAsync();
            else if (collectionsVisible)
                await LoadCollectionsAsync();
            else if (filamentsVisible)
                await LoadFilamentsAsync();
            else if (estimatesVisible)
                await LoadEstimateFilamentsAsync();
            else if (printersVisible)
                await LoadPrintersAsync();
            else if (trashVisible)
                await LoadTrashAsync();
            else if (settingsVisible)
                await LoadSettingsAsync();
        }
        finally { EndBusy(); }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchResultsPanel.IsVisible)
        {
            SearchResultsPanel.IsVisible = false;
            SearchResults.Children.Clear();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        if (e.Key == Key.K)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            CreateProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key is >= Key.D1 and <= Key.D9 &&
                 NavigationButtons().ElementAtOrDefault(e.Key - Key.D1) is { } target)
        {
            NavigationButton_Click(target, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void SearchBox_KeyUp(object? sender, KeyEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        if (query.Length < 2)
        {
            SearchResultsPanel.IsVisible = false;
            SearchResults.Children.Clear();
            return;
        }

        var cancellationToken = _searchCancellation.Token;
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try
        {
            var results = await Task.Run(
                () => new WorkspaceSearchReader().Search(workspaceRoot, query, cancellationToken),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            SearchResults.Children.Clear();
            foreach (var result in results.Take(8))
            {
                var item = new Button
                {
                    Content = $"{result.Title}\n{result.Detail}",
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Tag = result
                };
                item.Click += SearchResult_Click;
                SearchResults.Children.Add(item);
            }

            SearchResultsPanel.IsVisible = SearchResults.Children.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SearchResultsPanel.IsVisible = false;
        }
    }

    private async void SearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Search.GlobalSearchResult result }) return;
        SearchResultsPanel.IsVisible = false;
        SearchBox.Text = string.Empty;
        if (result.Project is { } project)
        {
            _projectOriginClient = null;
            ShowWorkspace("Projets");
            PageTitle.Text = project.Title();
            PageSubtitle.Text = project.Reference;
            await LoadProjectDirectoryAsync(project, project.ProjectPath);
        }
        else if (result.Client is not null)
        {
            ShowWorkspace("Clients");
            PageTitle.Text = "Clients";
            PageSubtitle.Text = result.Title;
            await LoadClientsAsync();
        }
        else if (result.Kind == MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.File)
        {
            await OpenFileAsync(result.Path);
        }
    }

    private void BuildRecentProjects(IReadOnlyList<ProjectSummary> projects)
    {
        RecentProjectsList.Children.Clear();
        if (projects.Count == 0)
        {
            RecentProjectsList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour l’instant",
                "Créez votre premier projet pour le retrouver ici.", "Nouveau projet", CreateProject_Click));
            return;
        }
        foreach (var project in projects)
        {
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"{project.Reference} · {project.Title()}", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = project.ClientTitle(), Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            var badge = CreateStatusBadge(project.StatusLabel);
            Grid.SetColumn(badge, 1);
            content.Children.Add(text);
            content.Children.Add(badge);
            var row = new Button { Classes = { "workspace-card" }, Tag = project, Content = content };
            row.Click += RecentProject_Click;
            RecentProjectsList.Children.Add(row);
        }
    }

    /// <summary>Coloured pill for a project status label (DEVIS, EN_COURS, TERMINE or "À compléter").</summary>
    private static Border CreateStatusBadge(string statusLabel)
    {
        var (text, variant) = statusLabel switch
        {
            "DEVIS" => ("Devis", "warning"),
            "EN_COURS" => ("En cours", "info"),
            "TERMINE" => ("Terminé", "success"),
            _ => (statusLabel, "neutral")
        };
        return new Border { Classes = { "badge", variant }, Child = new TextBlock { Text = text } };
    }

    /// <summary>Friendly empty state: icon, explanation and, when useful, the action that fills the list.</summary>
    private static Control CreateEmptyState(string iconKey, string title, string message, string? actionLabel = null, EventHandler<RoutedEventArgs>? action = null)
    {
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 36) };
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon", "icon-accent" }, Width = 30, Height = 30 };
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        stack.Children.Add(new Border { Classes = { "icon-chip" }, Width = 56, Height = 56, CornerRadius = new Avalonia.CornerRadius(14), HorizontalAlignment = HorizontalAlignment.Center, Child = icon });
        stack.Children.Add(new TextBlock { Text = title, Classes = { "card-title" }, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = message, Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        if (actionLabel is not null && action is not null)
        {
            var button = new Button { Content = actionLabel, Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
            button.Click += action;
            stack.Children.Add(button);
        }
        return stack;
    }

    private int _busy;

    private void BeginBusy()
    {
        _busy++;
        BusyBar.IsVisible = true;
    }

    private void EndBusy()
    {
        if (--_busy <= 0) { _busy = 0; BusyBar.IsVisible = false; }
    }

    /// <summary>Short feedback bubble in the bottom-right corner; it fades away on its own.</summary>
    private void Notify(string message, bool error = false)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon", error ? "icon-danger" : "icon-success" }, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Avalonia.Thickness(0, 1, 0, 0) };
        if (Avalonia.Application.Current?.TryFindResource(error ? "IconAlert" : "IconCheck", out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        var close = new Button { Content = "✕", Classes = { "icon-action" }, MinWidth = 24, MinHeight = 24, Padding = new Avalonia.Thickness(4, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(close, "Fermer");
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(text, 1); Grid.SetColumn(close, 2);
        layout.Children.Add(icon); layout.Children.Add(text); layout.Children.Add(close);
        var toast = new Border { Classes = { "toast", error ? "error" : "success" }, Child = layout };
        void Dismiss() => ToastHost.Children.Remove(toast);
        close.Click += (_, _) => Dismiss();
        ToastHost.Children.Add(toast);
        while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);
        DispatcherTimer.RunOnce(Dismiss, TimeSpan.FromSeconds(error ? 8 : 4.5));
    }

    private IEnumerable<Button> NavigationButtons()
        => NavigationItems.Children.OfType<Button>().Concat(FooterNavigation.Children.OfType<Button>()).Where(button => button.Tag is string);

    private static readonly Dictionary<string, string> PageSubtitles = new()
    {
        ["Accueil"] = "Bienvenue dans votre atelier. Voici ce qui mérite votre attention.",
        ["Projets"] = "Suivez vos devis, vos fabrications et vos livraisons.",
        ["Clients"] = "Particuliers et professionnels, avec leurs projets.",
        ["Collections"] = "Modèles 3D, produits et fournisseurs.",
        ["Filaments"] = "Votre stock de matières et leurs coûts.",
        ["Estimations"] = "Calculez un prix de vente à partir de vos coûts réels.",
        ["Imprimantes"] = "Votre parc machines et leurs coûts horaires.",
        ["Corbeille"] = "Retrouvez et restaurez ce que vous avez supprimé.",
        ["Paramètres"] = "Stockage, arborescence et journal des modifications."
    };

    private async void DashboardCreateClient_Click(object? sender, RoutedEventArgs e) => CreateClient_Click(sender, e);

    private void DashboardEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (NavigationButtons().FirstOrDefault(button => button.Tag as string == "Estimations") is { } target)
            NavigationButton_Click(target, e);
    }

    private static Border CreateCompactTableHeader(string columns, params string[] headings)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16 };
        for (var index = 0; index < headings.Length; index++)
        {
            var heading = new TextBlock { Text = headings[index], FontSize = 11, FontWeight = FontWeight.SemiBold, Classes = { "muted" } };
            Grid.SetColumn(heading, index);
            grid.Children.Add(heading);
        }
        return new Border { Background = Avalonia.Media.Brushes.Transparent, Padding = new Avalonia.Thickness(14, 6), Child = grid };
    }

    /// <summary>Name with a leading icon, for file and folder rows.</summary>
    private static Control CreateNameCell(string iconKey, string text, bool accentIcon)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" }, Width = 17, Height = 17 };
        if (accentIcon) icon.Classes.Add("icon-accent");
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontWeight = accentIcon ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static TextBlock CreateCompactTableCell(string value, bool prominent = false, bool accent = false)
    {
        var cell = new TextBlock
        {
            Text = value,
            FontWeight = prominent ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (accent)
            cell.Foreground = new SolidColorBrush(Color.Parse("#9BC0FF"));

        return cell;
    }

    private const string ActionRowColumns = "2*,*,110,64";

    /// <summary>
    /// Table row opened by clicking it, with a separate actions menu on the right.
    /// The row button sits behind the cells (which ignore the pointer) so the menu is never nested in it.
    /// </summary>
    private static Control CreateActionRow(Button? open, Button? actions, params Control[] cells)
        => CreateActionRow(ActionRowColumns, open, actions, cells);

    private static Control CreateActionRow(string columns, Button? open, Button? actions, params Control[] cells)
    {
        var columnCount = columns.Split(',').Length;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 16, Margin = new Avalonia.Thickness(14, 0), MinHeight = 46 };
        if (open is not null)
        {
            open.Classes.Add("table-row");
            open.Margin = new Avalonia.Thickness(-14, 0);
            Grid.SetColumnSpan(open, columnCount);
            row.Children.Add(open);
        }
        for (var index = 0; index < cells.Length; index++)
        {
            cells[index].IsHitTestVisible = open is null;
            cells[index].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cells[index], index);
            row.Children.Add(cells[index]);
        }
        if (actions is not null)
        {
            actions.HorizontalAlignment = HorizontalAlignment.Center;
            actions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actions, columnCount - 1);
            row.Children.Add(actions);
        }
        if (open is not null) return row;
        return new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row };
    }

    private const string ClientRowColumns = "2*,80,*,110,64";

    private bool _projectListMode;
    private bool _clientListMode;

    private static Button CreateOverflowActions(params Button[] actions)
        => CreateOverflowActions(PlacementMode.BottomEdgeAlignedRight, actions);

    private static Button CreateOverflowActions(PlacementMode placement, params Button[] actions)
    {
        var menu = new StackPanel { Spacing = 2, MinWidth = 184 };
        foreach (var action in actions)
        {
            action.Classes.Add("menu-action");
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.HorizontalContentAlignment = HorizontalAlignment.Left;
            menu.Children.Add(action);
        }

        var flyout = new Flyout
        {
            Placement = placement,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#141F30")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2D4D75")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(10),
                Padding = new Avalonia.Thickness(6),
                Child = menu
            }
        };
        flyout.FlyoutPresenterClasses.Add("action-menu-flyout");
        var overflow = new Button { Content = "⋯", Classes = { "icon-action" } };
        ToolTip.SetTip(overflow, "Actions");
        overflow.Click += (_, _) => flyout.ShowAt(overflow);
        return overflow;
    }

    private async void RefreshProjects_Click(object? sender, RoutedEventArgs e) => await LoadProjectsAsync();

    private async void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        _projectOriginClient = null;
        ShowWorkspace("Projets");
        PageTitle.Text = project.Title();
        PageSubtitle.Text = project.Reference;
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async void DashboardProjects_Click(object? sender, RoutedEventArgs e)
    {
        _projectOriginClient = null;
        ShowWorkspace("Projets");
        PageTitle.Text = "Projets";
        PageSubtitle.Text = PageSubtitles["Projets"];
        await LoadProjectsAsync();
    }

    private async void CreateProject_Click(object? sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        var clients = await Task.Run(() => new WorkspaceClientReader().Load(root));
        var dialog = new CreateProjectWindow(clients);
        if (await dialog.ShowDialog<bool>(this) && dialog.Client is { } client)
        {
            try
            {
                var projectName = dialog.ProjectName;
                BeginBusy();
                try { await Task.Run(() => new WorkspaceProjectCreator().Create(client.ClientPath, client.ClientCode, DateTime.Today.Year, projectName)); }
                finally { EndBusy(); }
                PageTitle.Text = "Projets";
                Notify("Projet créé.");
                ShowWorkspace("Projets");
                await LoadProjectsAsync();
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
            {
                Notify(exception.Message, error: true);
            }
        }
    }

    private async void CreateProjectForClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var dialog = new CreateProjectWindow([client], client);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Client is not { } selected) return;
        try
        {
            var projectName = dialog.ProjectName;
            await Task.Run(() => new WorkspaceProjectCreator().Create(selected.ClientPath, selected.ClientCode, DateTime.Today.Year, projectName));
            Notify("Projet créé.");
            await LoadClientDetailAsync(client with { ProjectCount = client.ProjectCount + 1 });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private IReadOnlyList<ProjectSummary> _allProjects = [];
    private string _projectStatusFilter = string.Empty;

    private async Task LoadProjectsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        ProjectList.Children.Clear();
        ProjectList.Spacing = 0;
        _projectListMode = true;
        ProjectsPanelTitle.Text = "Tous les projets";
        ProjectsPanelActions.IsVisible = true;
        ProjectFilterBar.IsVisible = true;

        try
        {
            _allProjects = await Task.Run(() => new WorkspaceProjectReader().Load(workspaceRoot));
            RenderProjects();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ProjectList.Children.Add(CreateEmptyState("IconAlert", "Impossible de lire les projets", exception.Message));
        }
    }

    private static bool MatchesProjectStatus(ProjectSummary project, string filter) => filter switch
    {
        "" => true,
        "TODO" => project.Profile is null,
        var status => project.Profile is not null && project.StatusLabel == status
    };

    private void RenderProjects()
    {
        if (!_projectListMode) return;
        ProjectList.Children.Clear();
        var text = ProjectFilterBox.Text?.Trim() ?? string.Empty;
        var projects = _allProjects
            .Where(project => MatchesProjectStatus(project, _projectStatusFilter))
            .Where(project => text.Length == 0 ||
                              $"{project.Reference} {project.ProjectName} {project.ClientFolderName}".Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        ProjectsPanelSubtitle.Text = projects.Length == _allProjects.Count
            ? (_allProjects.Count switch { 0 => "Aucun projet", 1 => "1 projet", var count => $"{count} projets" })
            : $"{projects.Length} sur {_allProjects.Count} projets";

        if (_allProjects.Count == 0)
        {
            ProjectFilterBar.IsVisible = false;
            ProjectList.Children.Add(CreateEmptyState("IconProjects", "Aucun projet pour l’instant", "Créez un projet pour suivre un devis, une fabrication et ses fichiers.", "Nouveau projet", CreateProject_Click));
            return;
        }
        if (projects.Length == 0)
        {
            ProjectList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Aucun projet ne correspond à ce filtre."));
            return;
        }

        ProjectList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "PROJET", "CLIENT", "STATUT", "ACTIONS"));
        foreach (var project in projects)
        {
            var open = new Button { Tag = project, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            open.Click += ProjectEntry_Click;
            var folder = new Button { Content = "↗  Ouvrir le dossier", Tag = project.ProjectPath }; folder.Click += OpenFolder_Click;
            var edit = new Button { Content = "✎  Modifier la fiche", Tag = project }; edit.Click += EditProject_Click;
            var rename = new Button { Content = "↻  Renommer", Tag = project }; rename.Click += RenameProject_Click;
            var move = new Button { Content = "⇄  Déplacer", Tag = project }; move.Click += MoveProject_Click;
            var trash = new Button { Content = "⌫  Corbeille", Tag = project }; trash.Click += TrashProject_Click;
            ProjectList.Children.Add(CreateActionRow(open, CreateOverflowActions(folder, edit, rename, move, trash),
                CreateCompactTableCell($"{project.Reference} · {project.Title()}", prominent: true),
                CreateCompactTableCell(project.ClientTitle()),
                CreateStatusBadge(project.StatusLabel)));
        }
    }

    private void ProjectFilterText_Changed(object? sender, TextChangedEventArgs e)
    {
        if (ProjectList is not null) RenderProjects();
    }

    private void ProjectFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string status }) return;
        _projectStatusFilter = status;
        SelectChip(ProjectStatusFilters, status);
        RenderProjects();
    }

    private static void SelectChip(Panel chips, string tag)
    {
        foreach (var chip in chips.Children.OfType<Button>())
        {
            if (chip.Tag as string == tag) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
        }
    }

    private async void ProjectEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Projects.ProjectSummary project }) return;
        // A project opened from a client sheet remembers that client, so "Retour" goes back to it.
        _projectOriginClient = ClientsWorkspacePanel.IsVisible ? _currentClientDetail : null;
        ShowWorkspace("Projets");
        PageTitle.Text = project.Title();
        PageSubtitle.Text = project.Reference;
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async Task LoadProjectDirectoryAsync(MS3DPRINT.Manager.Core.Projects.ProjectSummary project, string directory)
    {
        ProjectList.Children.Clear();
        ProjectList.Spacing = 8;
        _projectListMode = false;
        ProjectsPanelTitle.Text = directory == project.ProjectPath ? "Fiche projet" : Path.GetFileName(directory);
        ProjectsPanelSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
        ProjectsPanelActions.IsVisible = false;
        ProjectFilterBar.IsVisible = false;
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        var origin = _projectOriginClient;
        var back = new Button { Content = directory != project.ProjectPath ? "← Dossier parent" : origin is null ? "← Retour aux projets" : "← Retour au client", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            if (directory == project.ProjectPath && origin is not null)
            {
                _projectOriginClient = null;
                ShowWorkspace("Clients");
                PageTitle.Text = "Clients";
                PageSubtitle.Text = origin.Title();
                await LoadClientDetailAsync(origin);
            }
            else if (directory == project.ProjectPath)
            {
                PageTitle.Text = "Projets";
                PageSubtitle.Text = PageSubtitles["Projets"];
                await LoadProjectsAsync();
            }
            else await LoadProjectDirectoryAsync(project, new ProjectWorkspaceBrowser().Parent(project.ProjectPath, directory));
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var createFolder = new Button { Content = "+ Nouveau dossier", Tag = (Project: project, Directory: directory), Classes = { "primary-action" } };
        createFolder.Click += CreateProjectFolder_Click;
        var import = new Button { Content = "Importer un fichier", Tag = (Project: project, Directory: directory), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += ImportProjectFile_Click;
        actions.Children.Add(createFolder);
        actions.Children.Add(import);
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(back);
        toolbar.Children.Add(actions);
        ProjectList.Children.Add(toolbar);
        if (directory == project.ProjectPath)
            AddProjectOverview(project);
        try
        {
            var entries = await Task.Run(() => new ProjectWorkspaceBrowser().List(project.ProjectPath, directory));
            if (entries.Count == 0)
            {
                ProjectList.Children.Add(CreateEmptyState("IconFile", "Ce dossier est vide", "Importez un fichier ou créez un sous-dossier avec les boutons ci-dessus."));
                return;
            }
            ProjectList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "NOM", "TYPE", "TAILLE", "ACTIONS"));
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    var folder = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                    folder.Click += async (_, _) => await LoadProjectDirectoryAsync(project, entry.FullPath);
                    ProjectList.Children.Add(CreateActionRow(folder, null,
                        CreateNameCell("IconProjects", entry.Name, accentIcon: true),
                        CreateCompactTableCell("Dossier"), CreateCompactTableCell("—")));
                }
                else
                {
                    var file = new Button { Tag = entry.FullPath, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                    file.Click += FileEntry_Click;
                    var openWith = new Button { Content = "↗  Ouvrir avec…", Tag = entry.FullPath };
                    openWith.Click += OpenWithFile_Click;
                    var rename = new Button { Content = "↻  Renommer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    rename.Click += RenameProjectFile_Click;
                    var move = new Button { Content = "⇄  Déplacer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    move.Click += MoveProjectEntry_Click;
                    var trash = new Button { Content = "⌫  Corbeille", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    trash.Click += TrashProjectFile_Click;
                    ProjectList.Children.Add(CreateActionRow(file, CreateOverflowActions(openWith, rename, move, trash),
                        CreateNameCell("IconFile", entry.Name, accentIcon: false),
                        CreateCompactTableCell(entry.TypeLabel), CreateCompactTableCell(entry.SizeLabel)));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { ProjectList.Children.Add(new TextBlock { Text = "Impossible de lire les fichiers du projet.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void CreateProjectFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        var dialog = new TextPromptWindow("Nouveau dossier", "Nom du sous-dossier à créer.", string.Empty);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var folderName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).CreateFolder(context.Item2, folderName));
            Notify("Dossier créé.");
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    /// <summary>Project sub-folders (two levels) that can receive an imported file.</summary>
    private static IReadOnlyList<ImportDestination> ImportDestinations(string projectPath)
    {
        var folders = new List<ImportDestination>();
        foreach (var first in Directory.EnumerateDirectories(projectPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(first).StartsWith('.')) continue;
            folders.Add(new ImportDestination(Path.GetFileName(first), first));
            foreach (var second in Directory.EnumerateDirectories(first).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                folders.Add(new ImportDestination(Path.GetFileName(first) + " / " + Path.GetFileName(second), second));
        }
        return folders;
    }

    private async void ImportProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer un fichier", AllowMultiple = false });
        var source = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(source)) return;
        var originalName = Path.GetFileName(source);
        // From the project root the destination is a choice; inside a folder the file simply goes there.
        var atRoot = string.Equals(Path.GetFullPath(context.Item2).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(context.Item1.ProjectPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<ImportDestination>? destinations = null;
        string? suggestedFolder = null;
        if (atRoot)
        {
            destinations = ImportDestinations(context.Item1.ProjectPath);
            try { suggestedFolder = Path.Combine(context.Item1.ProjectPath, ProjectFileClassifier.Suggest(originalName, context.Item1.FolderName).RelativeDirectory); }
            catch (ArgumentException) { }
        }
        var naming = new ImportNameWindow(originalName, ProjectFileClassifier.SuggestImportName(originalName, context.Item1.Reference), context.Item1.Reference, destinations, suggestedFolder);
        if (!await naming.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var finalName = naming.FileName;
            var targetDirectory = naming.DestinationDirectory ?? context.Item2;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Import(source, targetDirectory, finalName));
            var where = naming.DestinationDirectory is null ? string.Empty : $" dans {Path.GetRelativePath(context.Item1.ProjectPath, targetDirectory)}";
            Notify((string.Equals(finalName, originalName, StringComparison.Ordinal) ? "Fichier importé" : $"Fichier importé sous le nom {finalName}") + where + ".");
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void FileEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        await OpenFileAsync(path);
    }

    private void OpenWithFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        try
        {
            if (DesktopPreviewLauncher.OpenWith is { } openWith) openWith(path);
            else _ = OpenFileAsync(path);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { Notify("Impossible d’ouvrir ce fichier avec une autre application : " + exception.Message, error: true); }
    }

    private async void RenameProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var isDirectory = Directory.Exists(context.Path);
        var dialog = new TextPromptWindow(isDirectory ? "Renommer le dossier" : "Renommer le fichier", isDirectory ? "Le dossier conservera son emplacement dans le projet." : "Le fichier conservera son emplacement dans le projet.", Path.GetFileName(context.Path));
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenamePath(context.Path, newName));
            Notify(isDirectory ? "Dossier renommé." : "Fichier renommé.");
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var name = Path.GetFileName(context.Path);
        var isDirectory = Directory.Exists(context.Path);
        if (!await new ConfirmTrashWindow(name, isDirectory ? "Le dossier et son contenu pourront être restaurés depuis la corbeille." : "Le fichier pourra être restauré depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Trash(context.Path, label: name));
            Notify(isDirectory ? "Dossier envoyé dans la corbeille." : "Fichier envoyé dans la corbeille.");
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveProjectEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier de destination", AllowMultiple = false });
        var destination = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var isDirectory = Directory.Exists(context.Path);
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MovePath(context.Path, destination));
            Notify(isDirectory ? "Dossier déplacé." : "Fichier déplacé.");
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task OpenFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            Notify("Ce fichier n’est plus disponible.", error: true);
            return;
        }
        try
        {
            if (IsImage(path))
            {
                await new ImagePreviewWindow(path).ShowDialog(this);
                return;
            }

            if (ThreeDFileSupport.IsPreviewable(path))
            {
                new ModelViewerWindow(path).Show(this);
                return;
            }

            if (DesktopPreviewLauncher.TryOpenDocument(path))
                return;

            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
                await launcher.LaunchUriAsync(new Uri(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException)
        {
            Notify("Impossible d’ouvrir ce fichier.", error: true);
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private void AddProjectOverview(ProjectSummary project)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(new TextBlock { Text = project.Title(), Classes = { "section-title" }, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock { Text = $"{project.Reference} · {project.ClientTitle()}", Classes = { "caption" } });
        var badge = CreateStatusBadge(project.Profile is null ? "À compléter" : project.StatusLabel);
        Grid.SetColumn(badge, 1);
        badge.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(titles);
        header.Children.Add(badge);

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(header);
        if (project.Profile?.DueDate is { } due)
            content.Children.Add(new TextBlock { Text = $"Échéance le {due:dd/MM/yyyy}", Classes = { "muted" } });
        if (!string.IsNullOrWhiteSpace(project.Profile?.Description))
            content.Children.Add(new TextBlock { Text = project.Profile.Description, TextWrapping = TextWrapping.Wrap });
        else if (project.Profile is null)
            content.Children.Add(new TextBlock { Text = "La fiche de ce projet est à compléter.", Classes = { "muted" } });
        var edit = new Button { Content = "✎  Modifier la fiche", Tag = project, HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += EditProject_Click;
        content.Children.Add(edit);
        ProjectList.Children.Add(new Border { Classes = { "card-raised" }, Margin = new Avalonia.Thickness(0, 0, 0, 10), Child = content });
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (project.Profile is null)
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var client = (await Task.Run(() => new WorkspaceClientReader().Load(root)))
                .FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
            if (client?.Profile is null) { Notify("Complétez d’abord la fiche de ce client historique.", error: true); return; }
            var completion = new TextPromptWindow("Compléter la fiche projet", "Le dossier et les fichiers existants seront conservés.", project.ProjectName);
            if (!await completion.ShowDialog<bool>(this)) return;
            try
            {
                var completedName = completion.Value;
                var completed = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, client.Profile, completedName));
                Notify("Fiche projet historique complétée.");
                if (_projectListMode) await LoadProjectsAsync();
                else await LoadProjectDirectoryAsync(project with { Profile = completed }, project.ProjectPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { Notify(exception.Message, error: true); }
            return;
        }
        var profile = project.Profile;
        var dialog = new EditProjectWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var (status, dueDate, description, notes) = (dialog.Status, dialog.DueDate, dialog.Description, dialog.Notes);
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateProject(root, profile, status, dueDate, description, notes));
            Notify("Fiche projet enregistrée.");
            if (_projectListMode) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(project with { Profile = updated }, project.ProjectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !Directory.Exists(path)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var project = (await Task.Run(() => new WorkspaceProjectReader().Load(root))).FirstOrDefault(item => path.StartsWith(item.ProjectPath, StringComparison.OrdinalIgnoreCase));
            if (project is not null)
            {
                PageTitle.Text = project.Title();
                await LoadProjectDirectoryAsync(project, path);
                return;
            }

            _folderWorkspaceRoot = Path.GetFullPath(path);
            _folderWorkspaceDirectory = _folderWorkspaceRoot;
            _folderWorkspaceReturnTitle = PageTitle.Text;
            _folderWorkspaceReturnSubtitle = PageSubtitle.Text;
            FolderWorkspacePanel.IsVisible = true;
            await LoadFolderWorkspaceAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Impossible d’ouvrir ce dossier : " + exception.Message, error: true); }
    }

    private async Task LoadFolderWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(_folderWorkspaceRoot) || string.IsNullOrWhiteSpace(_folderWorkspaceDirectory)) return;
        var root = _folderWorkspaceRoot;
        var directory = _folderWorkspaceDirectory;
        FolderWorkspaceTitle.Text = string.Equals(root, directory, StringComparison.OrdinalIgnoreCase) ? "Contenu du dossier" : Path.GetFileName(directory);
        FolderWorkspaceLocation.Text = directory;
        FolderWorkspaceList.Children.Clear();

        var back = new Button
        {
            Content = string.Equals(root, directory, StringComparison.OrdinalIgnoreCase) ? "← Retour" : "← Dossier parent",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        back.Click += async (_, _) =>
        {
            if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
            {
                FolderWorkspacePanel.IsVisible = false;
                PageTitle.Text = _folderWorkspaceReturnTitle ?? "Vue d’ensemble";
                PageSubtitle.Text = _folderWorkspaceReturnSubtitle ?? string.Empty;
                return;
            }

            _folderWorkspaceDirectory = new ProjectWorkspaceBrowser().Parent(root, directory);
            await LoadFolderWorkspaceAsync();
        };
        FolderWorkspaceList.Children.Add(back);
        try
        {
            var entries = await Task.Run(() => new ProjectWorkspaceBrowser().List(root, directory));
            if (entries.Count == 0)
            {
                FolderWorkspaceList.Children.Add(new TextBlock { Text = "Ce dossier est vide.", Classes = { "muted" }, Margin = new Avalonia.Thickness(8) });
                return;
            }

            foreach (var entry in entries)
            {
                var item = new Button
                {
                    Content = entry.IsDirectory ? $"▸  {entry.Name}\nDossier" : $"Fichier · {entry.Name}\n{entry.TypeLabel} · {entry.SizeLabel}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Classes = { "workspace-card" }
                };
                if (entry.IsDirectory)
                    item.Click += async (_, _) => { _folderWorkspaceDirectory = entry.FullPath; await LoadFolderWorkspaceAsync(); };
                else
                    item.Click += async (_, _) => await OpenFileAsync(entry.FullPath);
                FolderWorkspaceList.Children.Add(item);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { FolderWorkspaceList.Children.Add(new TextBlock { Text = "Impossible de lire ce dossier : " + exception.Message, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(8) }); }
    }

    private async void TrashProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (!await new ConfirmTrashWindow(project.Reference, "Tous les fichiers et sous-dossiers du projet sont concernés.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashProject(project));
            PageTitle.Text = "Projets";
            Notify("Projet envoyé dans la corbeille.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RenameProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        var dialog = new TextPromptWindow("Renommer le projet", "La référence du projet restera inchangée.", project.ProjectName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameProject(project, newName));
            PageTitle.Text = "Projets";
            Notify("Projet renommé.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        var clients = await Task.Run(() => new WorkspaceClientReader().Load(root)
            .Where(client => !string.Equals(client.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase)).ToArray());
        var dialog = new ClientPickerWindow(clients);
        if (!await dialog.ShowDialog<bool>(this) || dialog.SelectedClient is not { } destination) return;
        try
        {
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveProject(project, destination));
            PageTitle.Text = "Projets";
            Notify("Projet déplacé.");
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshClients_Click(object? sender, RoutedEventArgs e) => await LoadClientsAsync();

    private async void CreateClient_Click(object? sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        var usedCodes = await Task.Run(() => new WorkspaceClientCreator().UsedCodes(root));
        var dialog = new CreateClientWindow(usedCodes: usedCodes);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var (individual, firstName, lastName, clientName, siret, address) = (dialog.IsIndividual, dialog.FirstName, dialog.LastName, dialog.ClientName, dialog.Siret, dialog.Address);
            var (email, mobile) = (dialog.Email, dialog.Mobile);
            BeginBusy();
            ClientProfile created;
            try
            {
                created = await Task.Run(() => individual
                    ? new WorkspaceClientCreator().CreateIndividual(root, firstName, lastName, null, address, email, mobile)
                    : new WorkspaceClientCreator().CreateProfessional(root, clientName, null, siret, address, email, mobile));
            }
            finally { EndBusy(); }
            PageTitle.Text = "Clients";
            Notify($"Client créé (code {created.ClientCode}).");
            ShowWorkspace("Clients");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private IReadOnlyList<ClientSummary> _allClients = [];
    private string _clientKindFilter = string.Empty;

    private async Task LoadClientsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        ClientList.Children.Clear();
        ClientList.Spacing = 0;
        _clientListMode = true;
        _currentClientDetail = null;
        ClientsPanelTitle.Text = "Tous les clients";
        ClientsPanelActions.IsVisible = true;
        ClientFilterBar.IsVisible = true;
        try
        {
            _allClients = await Task.Run(() => new WorkspaceClientReader().Load(workspaceRoot));
            RenderClients();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClientList.Children.Add(CreateEmptyState("IconAlert", "Impossible de lire les clients", exception.Message));
        }
    }

    private void RenderClients()
    {
        if (!_clientListMode) return;
        ClientList.Children.Clear();
        var text = ClientFilterBox.Text?.Trim() ?? string.Empty;
        var clients = _allClients
            .Where(client => _clientKindFilter.Length == 0 || client.ProfileLabel == _clientKindFilter)
            .Where(client => text.Length == 0 || $"{client.DisplayName} {client.ClientCode}".Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        ClientsPanelSubtitle.Text = clients.Length == _allClients.Count
            ? (_allClients.Count switch { 0 => "Aucun client", 1 => "1 client", var count => $"{count} clients" })
            : $"{clients.Length} sur {_allClients.Count} clients";

        if (_allClients.Count == 0)
        {
            ClientFilterBar.IsVisible = false;
            ClientList.Children.Add(CreateEmptyState("IconClients", "Aucun client pour l’instant", "Ajoutez un client, particulier ou professionnel, pour y rattacher ses projets.", "Nouveau client", CreateClient_Click));
            return;
        }
        if (clients.Length == 0)
        {
            ClientList.Children.Add(CreateEmptyState("IconSearch", "Aucun résultat", "Aucun client ne correspond à ce filtre."));
            return;
        }

        ClientList.Children.Add(CreateCompactTableHeader(ClientRowColumns, "CLIENT", "CODE", "FICHE", "PROJETS", "ACTIONS"));
        foreach (var client in clients)
        {
            var open = new Button { Tag = client, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            open.Click += ClientEntry_Click;
            var folder = new Button { Content = "↗  Ouvrir le dossier", Tag = client.ClientPath }; folder.Click += OpenFolder_Click;
            var edit = new Button { Content = "✎  Modifier la fiche", Tag = client }; edit.Click += EditClient_Click;
            var rename = new Button { Content = "↻  Renommer", Tag = client }; rename.Click += RenameClient_Click;
            var move = new Button { Content = "⇄  Déplacer", Tag = client }; move.Click += MoveClient_Click;
            var trash = new Button { Content = "⌫  Corbeille", Tag = client }; trash.Click += TrashClient_Click;
            var kindVariant = client.IsProfileMissing ? "warning" : client.Kind == ClientKind.Individual ? "neutral" : "info";
            ClientList.Children.Add(CreateActionRow(ClientRowColumns, open, CreateOverflowActions(folder, edit, rename, move, trash),
                CreateCompactTableCell(client.Title(), prominent: true),
                CreateCompactTableCell(string.IsNullOrWhiteSpace(client.ClientCode) ? "—" : client.ClientCode, accent: true),
                new Border { Classes = { "badge", kindVariant }, Child = new TextBlock { Text = client.ProfileLabel } },
                CreateCompactTableCell(client.ProjectCount switch { 0 => "Aucun projet", 1 => "1 projet", var count => $"{count} projets" })));
        }
    }

    private void ClientFilterText_Changed(object? sender, TextChangedEventArgs e)
    {
        if (ClientList is not null) RenderClients();
    }

    private void ClientFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        _clientKindFilter = kind;
        SelectChip(ClientStatusFilters, kind);
        RenderClients();
    }

    private async void ClientEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MS3DPRINT.Manager.Core.Clients.ClientSummary client })
            await LoadClientDetailAsync(client);
    }

    private async Task LoadClientDetailAsync(ClientSummary client)
    {
        ClientList.Children.Clear();
        ClientList.Spacing = 8;
        _clientListMode = false;
        _currentClientDetail = client;
        ClientsPanelTitle.Text = "Fiche client";
        ClientsPanelSubtitle.Text = client.Title();
        ClientsPanelActions.IsVisible = false;
        ClientFilterBar.IsVisible = false;
        var back = new Button { Content = "← Retour aux clients", HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            PageTitle.Text = "Clients";
            PageSubtitle.Text = PageSubtitles["Clients"];
            await LoadClientsAsync();
        };
        ClientList.Children.Add(back);

        var card = new StackPanel { Spacing = 8 };
        card.Children.Add(new TextBlock { Text = client.Title(), FontSize = 18, FontWeight = FontWeight.SemiBold });
        card.Children.Add(new TextBlock { Text = $"{client.ProfileLabel} · {client.ClientCode} · {client.ProjectCount} projet(s)", Classes = { "muted" } });
        if (client.Profile?.PrimaryContact is { } contact)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(contact.Mobile)) lines.Add("Portable : " + contact.Mobile);
            if (!string.IsNullOrWhiteSpace(contact.Phone)) lines.Add("Téléphone : " + contact.Phone);
            if (!string.IsNullOrWhiteSpace(contact.Email)) lines.Add("E-mail : " + contact.Email);
            if (lines.Count > 0) card.Children.Add(new TextBlock { Text = string.Join("  ·  ", lines), TextWrapping = TextWrapping.Wrap });
        }
        if (!string.IsNullOrWhiteSpace(client.Profile?.Address)) card.Children.Add(new TextBlock { Text = client.Profile.Address, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Notes)) card.Children.Add(new TextBlock { Text = client.Profile.Notes, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = "✎  Modifier la fiche", Tag = client };
        edit.Click += EditClient_Click;
        var newProject = new Button { Content = "+  Nouveau projet", Tag = client, Classes = { "primary-action" } };
        newProject.Click += CreateProjectForClient_Click;
        card.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { newProject, edit } });
        ClientList.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(16), Child = card });
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root)
                .Where(project => string.Equals(project.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (projects.Length > 0) ClientList.Children.Add(new TextBlock { Text = "Projets", FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            foreach (var project in projects)
            {
                var projectButton = new Button
                {
                    Content = $"{project.Reference} · {project.Title()}\n{project.StatusLabel}",
                    Tag = project,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Classes = { "workspace-card" }
                };
                projectButton.Click += ProjectEntry_Click;
                ClientList.Children.Add(projectButton);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Notify("Impossible de lire les projets de ce client.", error: true);
        }
    }

    private async void EditClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (client.Profile is null)
        {
            var completion = new CreateClientWindow(client);
            if (!await completion.ShowDialog<bool>(this)) return;
            try
            {
                var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
                var (individual, lastName, clientName, code) = (completion.IsIndividual, completion.LastName, completion.ClientName, completion.Code);
                var (firstName, siret, address) = (completion.FirstName, completion.Siret, completion.Address);
                var (completionEmail, completionMobile) = (completion.Email, completion.Mobile);
                var completedProfile = await Task.Run(() => new WorkspaceClientCreator().CompleteExisting(root, client.ClientPath,
                    individual ? ClientKind.Individual : ClientKind.Professional,
                    individual ? lastName : clientName, code, firstName, lastName, siret, address, completionEmail, completionMobile));
                Notify("Fiche client historique complétée.");
                if (_clientListMode) await LoadClientsAsync();
                else await LoadClientDetailAsync(client with { Profile = completedProfile });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { Notify(exception.Message, error: true); }
            return;
        }
        var profile = client.Profile;
        var dialog = new EditClientWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var (address, notes, contactFirst, contactLast, contactRole) = (dialog.Address, dialog.Notes, dialog.ContactFirstName, dialog.ContactLastName, dialog.ContactRole);
            var (contactPhone, contactEmail, companyName, siret, firstName, lastName) = (dialog.ContactPhone, dialog.ContactEmail, dialog.CompanyName, dialog.Siret, dialog.IdentityFirstName, dialog.IdentityLastName);
            var contactMobile = dialog.ContactMobile;
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateClient(root, profile, address, notes,
                contactFirst, contactLast, contactRole, contactPhone, contactEmail,
                companyName, siret, firstName, lastName, contactMobile));
            Notify("Fiche client enregistrée.");
            if (_clientListMode) await LoadClientsAsync();
            else await LoadClientDetailAsync(client with { Profile = updated });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Notify(exception.Message, error: true);
        }
    }

    private async void TrashClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (!await new ConfirmTrashWindow(client.Title(), "Ses projets et tous ses fichiers seront concernés.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashClient(client));
            PageTitle.Text = "Clients";
            Notify("Client envoyé dans la corbeille.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RenameClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var dialog = new TextPromptWindow("Renommer le client", "Le code client et les références existantes restent inchangés.", client.FolderName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameClient(client, newName));
            PageTitle.Text = "Clients";
            Notify("Client renommé.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier de destination", AllowMultiple = false });
        var destination = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveClient(client, destination));
            PageTitle.Text = "Clients";
            Notify("Client déplacé.");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshCollections_Click(object? sender, RoutedEventArgs e) => await LoadCollectionsAsync();

    private void OpenThreeDViewer_Click(object? sender, RoutedEventArgs e)
        => new ModelViewerWindow().Show(this);

    private async void Changelog_Click(object? sender, RoutedEventArgs e)
        => await new ChangelogWindow().ShowDialog(this);

    private async void CreateCollection_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateCollectionWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var (newCategory, newName) = (dialog.Category, dialog.NameValue);
            var item = await Task.Run(() => new WorkspaceCollectionCreator().Create(root, newCategory, newName));
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, newCategory, item.Name, null, null));
            Notify("Collection créée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadCollectionsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        CollectionList.Children.Clear();
        try
        {
            var collections = await Task.Run(() => new WorkspaceInventoryReader().LoadAllCollections(workspaceRoot));
            if (collections.Count == 0) { CollectionList.Children.Add(CreateEmptyState("IconCollections", "Aucune collection", "Regroupez vos modèles 3D, vos produits et vos fournisseurs.", "Nouvelle collection", CreateCollection_Click)); return; }
            if (_collectionCategoryFilter.Length > 0)
                collections = collections.Where(item => CollectionCategory(workspaceRoot, item) == _collectionCategoryFilter).ToArray();
            if (collections.Count == 0) { CollectionList.Children.Add(CreateEmptyState("IconCollections", "Rien dans cette catégorie", "Choisissez « Tout » ou créez un nouvel élément.")); return; }
            CollectionList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "COLLECTION", "TYPE", "MODIFIÉE LE", "ACTIONS"));
            foreach (var item in collections)
            {
                var category = CollectionCategory(workspaceRoot, item);
                var open = new Button { Tag = item.Path, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                open.Click += OpenFolder_Click;
                var openMenu = new Button { Content = "↗  Ouvrir", Tag = item.Path }; openMenu.Click += OpenFolder_Click;
                var edit = new Button { Content = "✎  Modifier", Tag = item }; edit.Click += EditCollection_Click;
                var rename = new Button { Content = "↻  Renommer", Tag = item }; rename.Click += RenameCollection_Click;
                var move = new Button { Content = "⇄  Déplacer", Tag = item }; move.Click += MoveCollection_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = item }; trash.Click += TrashCollection_Click;
                var title = new StackPanel { Spacing = 1 };
                title.Children.Add(CreateCompactTableCell(item.Name, prominent: true));
                if (!string.IsNullOrWhiteSpace(item.Profile?.Description))
                    title.Children.Add(new TextBlock { Text = item.Profile.Description, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
                var type = new Border { Classes = { "badge", category == "06_FOURNISSEURS" ? "warning" : category == "03_PRODUITS_MS3DPRINT" ? "success" : "info" }, Child = new TextBlock { Text = CollectionCategoryLabel(category) } };
                CollectionList.Children.Add(CreateActionRow(open, CreateOverflowActions(openMenu, edit, rename, move, trash), title, type,
                    CreateCompactTableCell($"{item.LastWriteTime:dd/MM/yyyy}")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { CollectionList.Children.Add(new TextBlock { Text = "Impossible de lire les collections.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void EditCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var dialog = new EditCollectionWindow(item);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var category = Path.GetRelativePath(root, item.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var (name, description, notes, contact) = (dialog.NameValue, dialog.Description, dialog.Notes, dialog.Contact);
            var (address, phone, email, website) = (dialog.Address, dialog.Phone, dialog.Email, dialog.Website);
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, category, name, description, notes,
                contact, address, phone, email, website));
            Notify("Fiche collection enregistrée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private static string CollectionCategoryLabel(string category) => category switch
    {
        "02_MODELES_3D" => "Modèle 3D",
        "03_PRODUITS_MS3DPRINT" => "Produit",
        "06_FOURNISSEURS" => "Fournisseur",
        _ => "Collection"
    };

    private async void CollectionFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;
        _collectionCategoryFilter = category;
        foreach (var chip in CollectionFilters.Children.OfType<Button>())
        {
            if (chip.Tag as string == category) chip.Classes.Add("selected"); else chip.Classes.Remove("selected");
        }
        await LoadCollectionsAsync();
    }

    private static string CollectionCategory(string root, MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item) =>
        Path.GetRelativePath(root, item.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

    private async void RenameCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var dialog = new TextPromptWindow("Renommer la collection", "Le dossier et sa fiche seront mis à jour ensemble.", item.Name);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameCollection(item, CollectionCategory(root, item), newName));
            Notify("Collection renommée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier de destination", AllowMultiple = false });
        var destination = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MoveCollection(item, CollectionCategory(root, item), destination));
            Notify("Collection déplacée.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        if (!await new ConfirmTrashWindow(item.Name, "La collection et son contenu pourront être restaurés depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashCollection(item));
            Notify("Collection envoyée dans la corbeille.");
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshFilaments_Click(object? sender, RoutedEventArgs e) => await LoadFilamentsAsync();

    private async Task LoadEstimateFilamentsAsync()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var inventory = new WorkspaceInventoryReader();
            var filaments = inventory.LoadFilaments(root);
            var printers = inventory.LoadPrinters(root);
            var projects = new WorkspaceProjectReader().Load(root);
            EstimateProject.ItemsSource = projects;
            EstimateProject.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectSummary>((project, _) => new TextBlock { Text = project is null ? string.Empty : $"{project.Reference} · {project.ClientTitle()}" });
            EstimateProject.SelectedIndex = -1;
            EstimateFilament.ItemsSource = filaments;
            EstimateFilament.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>((f, _) => new TextBlock { Text = f is null ? string.Empty : $"{f?.Brand} · {f?.Name} ({f?.PricePerKg:0.00} €/kg)" });
            EstimateFilament.SelectedIndex = filaments.Count > 0 ? 0 : -1;
            EstimatePrinter.ItemsSource = printers.Where(printer => printer.IsActive).ToArray();
            EstimatePrinter.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PrinterProfile>((printer, _) => new TextBlock { Text = printer is null ? string.Empty : $"{printer.DisplayName} · {PrinterCostCalculator.HourlyRate(printer):0.00} €/h" });
            EstimatePrinter.SelectedIndex = printers.Any(printer => printer.IsActive) ? 0 : -1;
            EstimateDetail.Text = filaments.Count == 0 ? "Ajoutez un filament dans la page Filaments pour démarrer." : "Renseignez vos paramètres puis calculez.";
            await LoadSavedEstimatesAsync(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            EstimateFilament.ItemsSource = null;
            EstimateDetail.Text = "Les filaments ne peuvent pas être lus : " + exception.Message;
        }
    }

    private async void RefreshEstimates_Click(object? sender, RoutedEventArgs e) => await LoadEstimateFilamentsAsync();

    private void EstimatePrinter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EstimatePrinter.SelectedItem is PrinterProfile printer)
            EstimateMachineRate.Text = PrinterCostCalculator.HourlyRate(printer).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        RecalculateElectricity();
    }

    private void EstimateElectricityInputs_Changed(object? sender, TextChangedEventArgs e) => RecalculateElectricity();

    /// <summary>Keeps the electricity cost in sync with machine time, printer power and kWh price.</summary>
    private void RecalculateElectricity()
    {
        // Raised while the XAML is still loading, before the named controls exist.
        if (EstimateElectricity is null || EstimateHours is null || EstimateKwhPrice is null || EstimatePrinter is null || EstimateElectricityLabel is null) return;
        var printer = EstimatePrinter.SelectedItem as PrinterProfile;
        var automatic = printer?.PowerWatts is > 0;
        EstimateElectricity.IsReadOnly = automatic;
        EstimateElectricityLabel.Text = automatic ? "Électricité estimée (calculée automatiquement)" : "Électricité estimée (saisie manuelle : puissance de l’imprimante non renseignée)";
        if (!automatic || !TryEstimateValue(EstimateHours.Text, out var hours) || !TryEstimateValue(EstimateKwhPrice.Text, out var pricePerKwh) || hours < 0 || pricePerKwh < 0) return;
        EstimateElectricity.Text = PrinterCostCalculator.ElectricityCost(printer!, hours, pricePerKwh).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    }

    private int _estimateScopeIndex;

    /// <summary>0 = impression, 1 = modélisation seule, 2 = impression + modélisation.</summary>
    private int EstimateScopeIndex
    {
        get => _estimateScopeIndex;
        set
        {
            _estimateScopeIndex = value;
            foreach (var choice in EstimateScope.Children.OfType<Button>())
            {
                if (choice.Tag as string == value.ToString()) choice.Classes.Add("selected"); else choice.Classes.Remove("selected");
            }
            EstimatePrintInputs.IsVisible = value != 1;
        }
    }

    private void EstimateScope_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var index)) return;
        EstimateScopeIndex = index;
        if (index == 1)
            EstimateDetail.Text = "Renseignez le coût de modélisation puis calculez.";
    }

    private async void RefreshPrinters_Click(object? sender, RoutedEventArgs e) => await LoadPrintersAsync();

    private async void CreatePrinter_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreatePrinterWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var now = DateTimeOffset.UtcNow;
            new PrinterStore(root).Create(new PrinterProfile(Guid.NewGuid(), dialog.Brand, dialog.Model, dialog.PrinterName, dialog.HourlyRate, dialog.BuildVolume, dialog.PrinterIsActive, now, now,
                dialog.PurchasePrice, dialog.LifetimeHours, dialog.PowerWatts));
            Notify("Imprimante ajoutée.");
            await LoadPrintersAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void EditPrinter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PrinterProfile original }) return;
        var dialog = new CreatePrinterWindow(original);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            new PrinterStore(root).Update(original with { Brand = dialog.Brand, Model = dialog.Model, Name = dialog.PrinterName, HourlyRate = dialog.HourlyRate, BuildVolume = dialog.BuildVolume,
                IsActive = dialog.PrinterIsActive, PurchasePrice = dialog.PurchasePrice, LifetimeHours = dialog.LifetimeHours, PowerWatts = dialog.PowerWatts }, original.UpdatedAt);
            await LoadPrintersAsync();
            Notify("Imprimante enregistrée.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashPrinter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PrinterProfile printer }) return;
        if (!await new ConfirmTrashWindow(printer.Name, "Cette imprimante pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new PrinterStore(root).Trash(printer.Id));
            await LoadPrintersAsync();
            Notify("Imprimante envoyée dans la corbeille.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadPrintersAsync()
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        PrinterList.Children.Clear();
        try
        {
            var printers = await Task.Run(() => new WorkspaceInventoryReader().LoadPrinters(root));
            if (printers.Count == 0)
            {
                PrinterList.Children.Add(CreateEmptyState("IconPrinters", "Aucune imprimante enregistrée", "Ajoutez vos machines : leur coût horaire alimente vos estimations.", "Ajouter une imprimante", CreatePrinter_Click));
                return;
            }
            PrinterList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "IMPRIMANTE", "COÛT HORAIRE", "PUISSANCE", "ACTIONS"));
            foreach (var printer in printers)
            {
                var edit = new Button { Content = "✎  Modifier", Tag = printer }; edit.Click += EditPrinter_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = printer }; trash.Click += TrashPrinter_Click;
                PrinterList.Children.Add(CreateActionRow(null, CreateOverflowActions(edit, trash),
                    CreateCompactTableCell(printer.DisplayName, prominent: true),
                    CreateCompactTableCell($"{PrinterCostCalculator.HourlyRate(printer):0.00} €/h"),
                    CreateCompactTableCell(printer.PowerWatts is { } watts ? $"{watts} W" : "—")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { PrinterList.Children.Add(new TextBlock { Text = "Impossible de lire les imprimantes : " + exception.Message, Margin = new Avalonia.Thickness(8) }); }
    }


    private async Task LoadSavedEstimatesAsync(string root)
    {
        SavedEstimateList.Children.Clear();
        try
        {
            var estimates = await Task.Run(() => new EstimateStore(root).LoadAll());
            if (estimates.Count == 0)
            {
                SavedEstimateList.Children.Add(new TextBlock { Text = "Aucune estimation enregistrée.", Classes = { "muted" } });
                return;
            }
            foreach (var estimate in estimates.Take(5))
            {
                var text = new StackPanel { Spacing = 1 };
                text.Children.Add(new TextBlock { Text = estimate.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = $"{estimate.Result.FinalPrice:0.00} €", Classes = { "caption" } });
                var button = new Button { Content = text, Tag = estimate, Classes = { "workspace-card" }, MinHeight = 52, Padding = new Avalonia.Thickness(12, 8) };
                button.Click += LoadSavedEstimate_Click;
                var trash = new Button { Content = "⌫", Tag = estimate, Classes = { "icon-action" } };
                ToolTip.SetTip(trash, "Envoyer à la corbeille");
                trash.Click += TrashEstimate_Click;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                Grid.SetColumn(trash, 1);
                row.Children.Add(button);
                row.Children.Add(trash);
                SavedEstimateList.Children.Add(row);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { SavedEstimateList.Children.Add(new TextBlock { Text = "Impossible de lire les estimations : " + exception.Message, Classes = { "muted" } }); }
    }

    private async void TrashEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        if (!await new ConfirmTrashWindow(estimate.Name, "Cette estimation pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new EstimateStore(root).Trash(estimate.Id));
            if (_loadedEstimate?.Id == estimate.Id) ResetEstimate_Click(sender, e);
            await LoadSavedEstimatesAsync(root);
            EstimateDetail.Text = "Estimation envoyée dans la corbeille.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; }
    }

    private void LoadSavedEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EstimateProfile estimate }) return;
        var inputs = estimate.Inputs;
        _loadedEstimate = estimate;
        EstimateName.Text = estimate.Name;
        EstimateScopeIndex = string.Equals(estimate.FilamentName, "Modélisation seule", StringComparison.Ordinal) ? 1 : 0;
        EstimateProject.SelectedItem = (EstimateProject.ItemsSource as IEnumerable<ProjectSummary>)?.FirstOrDefault(project => project.Reference == estimate.ProjectReference && project.ClientFolderName == estimate.ClientName);
        EstimateFilament.SelectedItem = (EstimateFilament.ItemsSource as IEnumerable<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>)?.FirstOrDefault(filament => $"{filament.Brand} {filament.Name}" == estimate.FilamentName);
        EstimatePrinter.SelectedItem = (EstimatePrinter.ItemsSource as IEnumerable<PrinterProfile>)?.FirstOrDefault(printer => printer.Name == estimate.PrinterName || printer.DisplayName == estimate.PrinterName);
        EstimateWeight.Text = estimate.WeightGrams.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateHours.Text = estimate.MachineHours.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMachineRate.Text = estimate.MachineHourlyRate.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateKwhPrice.Text = "0,25";
        EstimateElectricity.Text = inputs.ElectricityCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateDesign.Text = inputs.DesignCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateLabor.Text = inputs.LaborCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimatePostProcessing.Text = inputs.PostProcessingCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateConsumables.Text = inputs.ConsumablesCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimatePackaging.Text = inputs.PackagingShippingCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMisc.Text = inputs.MiscCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateMargin.Text = inputs.MarginPercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateFailure.Text = inputs.FailureRatePercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        EstimateDiscount.Text = inputs.DiscountPercent.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        _latestEstimateInputs = inputs;
        _latestEstimateResult = estimate.Result;
        EstimateFinal.Text = estimate.Result.FinalPrice.ToString("0.00") + " €";
        EstimateDetail.Text = "Estimation chargée. Ajustez les valeurs, recalculez puis enregistrez la mise à jour.";
    }

    private void ResetEstimate_Click(object? sender, RoutedEventArgs e)
    {
        EstimateName.Text = "Nouvelle estimation";
        EstimateScopeIndex = 0;
        EstimateWeight.Text = "100";
        EstimateHours.Text = "2";
        EstimateMachineRate.Text = "2";
        EstimateKwhPrice.Text = "0,25";
        EstimateElectricity.Text = EstimateDesign.Text = EstimateLabor.Text = EstimatePostProcessing.Text = EstimateConsumables.Text = EstimatePackaging.Text = EstimateMisc.Text = EstimateDiscount.Text = "0";
        EstimateMargin.Text = "30";
        EstimateFailure.Text = "5";
        _latestEstimateInputs = null;
        _latestEstimateResult = null;
        _loadedEstimate = null;
        EstimateFinal.Text = "—";
        EstimateDetail.Text = "Nouvelle estimation prête.";
    }

    private async void SaveEstimate_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestEstimateInputs is null || _latestEstimateResult is null)
        { EstimateDetail.Text = "Calculez l’estimation avant de l’enregistrer."; return; }
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var printer = EstimatePrinter.SelectedItem as PrinterProfile;
            TryEstimateValue(EstimateWeight.Text, out var savedWeight);
            TryEstimateValue(EstimateHours.Text, out var savedHours);
            TryEstimateValue(EstimateMachineRate.Text, out var savedRate);
            var project = EstimateProject.SelectedItem as ProjectSummary;
            var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
            var profile = new EstimateProfile(_loadedEstimate?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(EstimateName.Text) ? "Nouvelle estimation" : EstimateName.Text.Trim(), filament is null ? "Modélisation seule" : $"{filament.Brand} {filament.Name}", printer?.DisplayName ?? "Sans impression", _latestEstimateInputs, _latestEstimateResult, _loadedEstimate?.CreatedAt ?? DateTimeOffset.UtcNow, savedWeight, savedHours, savedRate, project?.Reference, project?.ClientFolderName);
            var store = new EstimateStore(root);
            if (_loadedEstimate is null) store.Create(profile);
            else store.Update(profile);
            _loadedEstimate = profile;
            EstimateDetail.Text = "Estimation enregistrée.";
            await LoadSavedEstimatesAsync(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { EstimateDetail.Text = exception.Message; }
    }

    private void CalculateEstimate_Click(object? sender, RoutedEventArgs e)
    {
        var modellingOnly = EstimateScopeIndex == 1;
        var filament = EstimateFilament.SelectedItem as MS3DPRINT.Manager.Core.Filaments.FilamentProfile;
        if (!modellingOnly && filament is null)
        {
            EstimateDetail.Text = "Ajoutez ou sélectionnez un filament.";
            return;
        }

        if (!TryEstimateValue(EstimateWeight.Text, out var grams) || !TryEstimateValue(EstimateHours.Text, out var hours) ||
            !TryEstimateValue(EstimateMachineRate.Text, out var rate) || !TryEstimateValue(EstimateKwhPrice.Text, out var pricePerKwh) || !TryEstimateValue(EstimateElectricity.Text, out var electricity) ||
            !TryEstimateValue(EstimateDesign.Text, out var design) || !TryEstimateValue(EstimateLabor.Text, out var labor) ||
            !TryEstimateValue(EstimatePostProcessing.Text, out var postProcessing) || !TryEstimateValue(EstimateConsumables.Text, out var consumables) ||
            !TryEstimateValue(EstimatePackaging.Text, out var packaging) || !TryEstimateValue(EstimateMisc.Text, out var misc) ||
            !TryEstimateValue(EstimateMargin.Text, out var margin) || !TryEstimateValue(EstimateFailure.Text, out var failure) || !TryEstimateValue(EstimateDiscount.Text, out var discount) ||
            grams < 0 || hours < 0 || rate < 0 || pricePerKwh < 0 || electricity < 0 || design < 0 || labor < 0 || postProcessing < 0 || consumables < 0 || packaging < 0 || misc < 0)
        {
            EstimateDetail.Text = "Saisissez des montants positifs, avec une virgule ou un point.";
            return;
        }

        try
        {
            var printer = EstimatePrinter.SelectedItem as PrinterProfile;
            var machineCost = modellingOnly ? 0m : hours * rate;
            var electricityCost = modellingOnly ? 0m : printer?.PowerWatts is > 0 ? PrinterCostCalculator.ElectricityCost(printer, hours, pricePerKwh) : electricity;
            EstimateElectricity.Text = electricityCost.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            var inputs = new EstimateInputs(
                MaterialCost: modellingOnly ? 0m : filament!.PricePerKg * grams / 1000m, MachineCost: machineCost, ElectricityCost: electricityCost,
                DesignCost: design, LaborCost: labor, PostProcessingCost: postProcessing, ConsumablesCost: consumables,
                PackagingShippingCost: packaging, MiscCost: misc, MarginPercent: margin, FailureRatePercent: failure, DiscountPercent: discount);
            var result = EstimateCalculator.Calculate(inputs);
            _latestEstimateInputs = inputs;
            _latestEstimateResult = result;
            EstimateFinal.Text = result.FinalPrice.ToString("0.00") + " €";
            EstimateDetail.Text = $"Coût brut : {result.RawCost:0.00} € · Avec aléas : {result.ExpectedCost:0.00} € · Avant remise : {result.SalePrice:0.00} €.";
        }
        catch (ArgumentOutOfRangeException)
        {
            EstimateDetail.Text = "Le taux d’échec doit être inférieur à 100 %, et la remise entre 0 et 100 %.";
        }
    }

    private static bool TryEstimateValue(string? value, out decimal result) =>
        decimal.TryParse(value?.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out result);

    private async void CreateFilament_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateFilamentWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            new WorkspaceFilamentEditor().Create(root, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Filament ajouté.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void EditFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile original }) return;
        var dialog = new CreateFilamentWindow(original);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            new WorkspaceFilamentEditor().Update(root, original, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Fiche filament enregistrée.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadFilamentsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        FilamentList.Children.Clear();
        try
        {
            var filaments = await Task.Run(() => new WorkspaceInventoryReader().LoadFilaments(workspaceRoot));
            if (filaments.Count == 0) { FilamentList.Children.Add(CreateEmptyState("IconFilaments", "Aucun filament enregistré", "Ajoutez vos bobines pour calculer le coût matière de vos estimations.", "Ajouter un filament", CreateFilament_Click)); return; }
            FilamentList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "FILAMENT", "MATIÈRE", "PRIX", "ACTIONS"));
            foreach (var filament in filaments)
            {
                var edit = new Button { Content = "✎  Modifier", Tag = filament }; edit.Click += EditFilament_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = filament }; trash.Click += TrashFilament_Click;
                var color = string.IsNullOrWhiteSpace(filament.Color) ? string.Empty : $" · {filament.Color}";
                var material = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                material.Children.Add(CreateCompactTableCell($"{filament.Material}{color}"));
                if (filament.IsAbrasive) material.Children.Add(new Border { Classes = { "badge", "warning" }, Child = new TextBlock { Text = "Abrasif" } });
                FilamentList.Children.Add(CreateActionRow(null, CreateOverflowActions(edit, trash),
                    CreateCompactTableCell($"{filament.Brand} · {filament.Name}", prominent: true), material,
                    CreateCompactTableCell($"{filament.PricePerKg:0.00} €/kg")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { FilamentList.Children.Add(new TextBlock { Text = "Impossible de lire les filaments.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void TrashFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile filament }) return;
        if (!await new ConfirmTrashWindow(filament.Brand + " " + filament.Name, "La fiche pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Filaments.FilamentStore(root).Trash(filament.Id));
            Notify("Filament envoyé dans la corbeille.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshTrash_Click(object? sender, RoutedEventArgs e) => await LoadTrashAsync();

    private async Task LoadTrashAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        TrashList.Children.Clear();
        try
        {
            var entries = await Task.Run(() => new WorkspaceTrashReader().Load(workspaceRoot));
            if (entries.Count == 0) { TrashList.Children.Add(CreateEmptyState("IconTrash", "La corbeille est vide", "Les éléments supprimés apparaissent ici et peuvent être restaurés.")); return; }
            TrashList.Children.Add(CreateCompactTableHeader("*,Auto", "ÉLÉMENT", "ACTION"));
            foreach (var entry in entries)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, MinHeight = 44 };
                row.Children.Add(CreateCompactTableCell(entry.Label, prominent: true));
                var restore = new Button { Content = "Restaurer", Tag = entry.Id };
                restore.Click += RestoreTrash_Click;
                Grid.SetColumn(restore, 1);
                row.Children.Add(restore);
                TrashList.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#263A56")), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Padding = new Avalonia.Thickness(14, 6), Child = row });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { TrashList.Children.Add(new TextBlock { Text = "Impossible de lire la corbeille.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void RestoreTrash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try { await Task.Run(() => new WorkspaceTrashReader().Restore(root, id)); await LoadTrashAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RefreshSettings_Click(object? sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async void VerifyWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try
        {
            var result = await Task.Run(() => new MS3DPRINT.Manager.Core.Storage.FolderTreeService().EnsureMainStructure(root));
            Notify(result.CreatedFolders.Count == 0
                ? "Arborescence vérifiée : tous les dossiers existent."
                : $"Arborescence vérifiée : {result.CreatedFolders.Count} dossier(s) ajouté(s).");
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadSettingsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        StorageList.Children.Clear();
        StorageList.Spacing = 14;
        var providers = await Task.Run(() => new WorkspaceStorageReader().Load(workspaceRoot));

        Border Card(string title, string description, params Control[] content)
        {
            var stack = new StackPanel { Spacing = 12 };
            var heading = new StackPanel { Spacing = 2 };
            heading.Children.Add(new TextBlock { Text = title, Classes = { "card-title" } });
            heading.Children.Add(new TextBlock { Text = description, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(heading);
            foreach (var control in content) stack.Children.Add(control);
            return new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(20), Child = stack };
        }

        var open = new Button { Content = "Ouvrir l’espace de travail", Tag = workspaceRoot };
        open.Click += OpenFolder_Click;
        var verify = new Button { Content = "Vérifier l’arborescence" };
        verify.Click += VerifyWorkspace_Click;
        StorageList.Children.Add(Card("Espace de travail", workspaceRoot,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, verify } }));

        var providerRows = new StackPanel { Spacing = 0 };
        foreach (var provider in providers)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 40 };
            row.Children.Add(new TextBlock { Text = provider.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            var badge = new Border { Classes = { "badge", provider.IsAvailable ? "success" : "neutral" }, Child = new TextBlock { Text = provider.IsAvailable ? "Disponible" : "Non configuré" } };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            providerRows.Children.Add(new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row });
        }
        StorageList.Children.Add(Card("Stockage", "Emplacements où vos dossiers peuvent être synchronisés.", providerRows));

        var changelog = new Button { Content = "Journal des modifications", HorizontalAlignment = HorizontalAlignment.Left };
        changelog.Click += Changelog_Click;
        StorageList.Children.Add(Card("À propos", "MS3DPRINT Manager : gestion de vos clients, projets, collections, filaments, imprimantes et estimations.", changelog));
    }
}
