using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        await PreloadAsync();
    }

    /// <summary>Reads the dashboard data once, so a splash screen can wait for it before showing the window.</summary>
    public async Task PreloadAsync()
    {
        if (_preloaded) return;
        _preloaded = true;
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;

        try
        {
            var dashboard = await Task.Run(() => new WorkspaceDashboardReader().Load(workspaceRoot));
            ProjectCountText.Text = dashboard.ProjectCount.ToString();
            QuoteCountText.Text = dashboard.QuoteCount.ToString();
            InProgressCountText.Text = dashboard.InProgressCount.ToString();
            ClientCountText.Text = $"{dashboard.ClientCount} client(s)";
            WorkspaceStatusText.Text = $"{dashboard.CompletedCount} projet(s) terminé(s).";
            ApplyRecentProject(dashboard.RecentProjects.ElementAtOrDefault(0), RecentProjectOneButton, RecentProjectOneTitle, RecentProjectOneDetail, RecentProjectOneStatus);
            ApplyRecentProject(dashboard.RecentProjects.ElementAtOrDefault(1), RecentProjectTwoButton, RecentProjectTwoTitle, RecentProjectTwoDetail, RecentProjectTwoStatus);
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
        foreach (var navigationButton in NavigationItems.Children.OfType<Button>().Where(button => button.Tag is string))
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
        _selectedNavigationButton = NavigationItems.Children.OfType<Button>().FirstOrDefault(button => button.Tag as string == destination);
    }

    private async void NavigationButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string destination)
            return;

        ShowWorkspace(destination);
        PageTitle.Text = destination;
        PageSubtitle.Text = $"Espace {destination.ToLowerInvariant()} de l’atelier.";
        _projectOriginClient = null;
        var projectsVisible = destination == "Projets";
        var clientsVisible = destination == "Clients";
        var collectionsVisible = destination == "Collections";
        var filamentsVisible = destination == "Filaments";
        var estimatesVisible = destination == "Estimations";
        var printersVisible = destination == "Imprimantes";
        var trashVisible = destination == "Corbeille";
        var settingsVisible = destination == "Paramètres";

        if (projectsVisible)
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

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchResultsPanel.IsVisible)
        {
            SearchResultsPanel.IsVisible = false;
            SearchResults.Children.Clear();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Control || e.Key != Key.K)
            return;

        SearchBox.Focus();
        e.Handled = true;
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
            PageTitle.Text = project.ProjectName;
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

    private static void ApplyRecentProject(
        MS3DPRINT.Manager.Core.Projects.ProjectSummary? project,
        Button button,
        TextBlock title,
        TextBlock detail,
        TextBlock status)
    {
        button.Tag = project;
        button.IsEnabled = project is not null;
        if (project is null)
        {
            title.Text = "Aucun projet récent";
            detail.Text = "Créez un projet pour le voir ici.";
            status.Text = string.Empty;
            return;
        }

        title.Text = $"{project.Reference} · {project.ProjectName}";
        detail.Text = project.ClientFolderName;
        status.Text = project.StatusLabel;
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
        PageTitle.Text = project.ProjectName;
        PageSubtitle.Text = project.Reference;
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async void DashboardProjects_Click(object? sender, RoutedEventArgs e)
    {
        _projectOriginClient = null;
        ShowWorkspace("Projets");
        PageTitle.Text = "Projets";
        PageSubtitle.Text = "Tous les projets de l’atelier.";
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
                new WorkspaceProjectCreator().Create(client.ClientPath, client.ClientCode, DateTime.Today.Year, dialog.ProjectName);
                PageTitle.Text = "Projets";
                PageSubtitle.Text = "Projet créé.";
                ShowWorkspace("Projets");
                await LoadProjectsAsync();
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
            {
                PageSubtitle.Text = exception.Message;
            }
        }
    }

    private async Task LoadProjectsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        ProjectList.Children.Clear();
        ProjectList.Spacing = 0;
        ProjectsPanelTitle.Text = "Tous les projets";
        ProjectsPanelSubtitle.Text = "Catalogue lu depuis votre espace de travail.";
        ProjectsPanelActions.IsVisible = true;

        try
        {
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(workspaceRoot));
            if (projects.Count == 0)
            {
                ProjectList.Children.Add(new TextBlock { Text = "Aucun projet trouvé.", Margin = new Avalonia.Thickness(8) });
                return;
            }

            ProjectList.Children.Add(CreateCompactTableHeader("2*,*,Auto", "PROJET", "CLIENT", "STATUT"));

            foreach (var project in projects)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), ColumnSpacing = 16 };
                var title = CreateCompactTableCell($"{project.Reference} · {project.ProjectName}", prominent: true);
                var client = CreateCompactTableCell(project.ClientFolderName);
                var status = CreateCompactTableCell(project.StatusLabel, accent: true);
                Grid.SetColumn(client, 1); Grid.SetColumn(status, 2);
                row.Children.Add(title); row.Children.Add(client); row.Children.Add(status);
                var button = new Button
                {
                    Tag = project,
                    Content = row,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Classes = { "table-row" }
                };
                button.Click += ProjectEntry_Click;
                ProjectList.Children.Add(button);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ProjectList.Children.Add(new TextBlock { Text = "Impossible de lire les projets.", Margin = new Avalonia.Thickness(8) });
        }
    }

    private async void ProjectEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Projects.ProjectSummary project }) return;
        // A project opened from a client sheet remembers that client, so "Retour" goes back to it.
        _projectOriginClient = ClientsWorkspacePanel.IsVisible ? _currentClientDetail : null;
        ShowWorkspace("Projets");
        PageTitle.Text = project.ProjectName;
        PageSubtitle.Text = project.Reference;
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async Task LoadProjectDirectoryAsync(MS3DPRINT.Manager.Core.Projects.ProjectSummary project, string directory)
    {
        ProjectList.Children.Clear();
        ProjectList.Spacing = 8;
        ProjectsPanelTitle.Text = directory == project.ProjectPath ? "Fiche projet" : Path.GetFileName(directory);
        ProjectsPanelSubtitle.Text = $"{project.Reference} · {project.ClientFolderName}";
        ProjectsPanelActions.IsVisible = false;
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
                PageSubtitle.Text = origin.DisplayName;
                await LoadClientDetailAsync(origin);
            }
            else if (directory == project.ProjectPath)
            {
                PageTitle.Text = "Projets";
                PageSubtitle.Text = "Tous les projets de l’atelier.";
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
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    var folder = new Button
                    {
                        Content = $"▸  {entry.Name}\nDossier du projet",
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Classes = { "workspace-card" }
                    };
                    folder.Click += async (_, _) => await LoadProjectDirectoryAsync(project, entry.FullPath);
                    ProjectList.Children.Add(folder);
                }
                else
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                    var file = new Button
                    {
                        Content = $"Fichier · {entry.Name}",
                        Tag = entry.FullPath,
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Classes = { "table-row" }
                    };
                    file.Click += FileEntry_Click;
                    var rename = new Button { Content = "↻  Renommer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    rename.Click += RenameProjectFile_Click;
                    var move = new Button { Content = "⇄  Déplacer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    move.Click += MoveProjectEntry_Click;
                    var trash = new Button { Content = "⌫  Corbeille", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    trash.Click += TrashProjectFile_Click;
                    var overflow = CreateOverflowActions(rename, move, trash);
                    Grid.SetColumn(overflow, 1);
                    row.Children.Add(file); row.Children.Add(overflow);
                    ProjectList.Children.Add(row);
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
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).CreateFolder(context.Item2, dialog.Value));
            PageSubtitle.Text = "Dossier créé.";
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void ImportProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer un fichier", AllowMultiple = false });
        var source = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Import(source, context.Item2));
            PageSubtitle.Text = "Fichier importé.";
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void FileEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        await OpenFileAsync(path);
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
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenamePath(context.Path, dialog.Value));
            PageSubtitle.Text = isDirectory ? "Dossier renommé." : "Fichier renommé.";
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = isDirectory ? "Dossier envoyé dans la corbeille." : "Fichier envoyé dans la corbeille.";
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = isDirectory ? "Dossier déplacé." : "Fichier déplacé.";
            await LoadProjectDirectoryAsync(context.Project, context.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async Task OpenFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            PageSubtitle.Text = "Ce fichier n’est plus disponible.";
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
            PageSubtitle.Text = "Impossible d’ouvrir ce fichier.";
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private void AddProjectOverview(ProjectSummary project)
    {
        var details = project.Profile is { } profile
            ? $"{project.ClientFolderName} · {project.StatusLabel}" + (profile.DueDate is { } due ? $" · Échéance {due:dd/MM/yyyy}" : string.Empty)
            : $"{project.ClientFolderName} · Fiche à compléter";
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = project.Reference + " · " + project.ProjectName, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = details, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(project.Profile?.Description))
            content.Children.Add(new TextBlock { Text = project.Profile.Description, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = "✎  Modifier la fiche", Tag = project };
        edit.Click += EditProject_Click;
        var open = new Button { Content = "↗  Ouvrir le dossier", Tag = project.ProjectPath };
        open.Click += OpenFolder_Click;
        var rename = new Button { Content = "↻  Renommer", Tag = project };
        rename.Click += RenameProject_Click;
        var move = new Button { Content = "⇄  Déplacer", Tag = project };
        move.Click += MoveProject_Click;
        var trash = new Button { Content = "⌫  Corbeille", Tag = project };
        trash.Click += TrashProject_Click;
        content.Children.Add(CreateOverflowActions(PlacementMode.BottomEdgeAlignedLeft, open, edit, rename, move, trash));
        ProjectList.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(16), Child = content
        });
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        if (project.Profile is null)
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var client = (await Task.Run(() => new WorkspaceClientReader().Load(root)))
                .FirstOrDefault(candidate => string.Equals(candidate.ClientPath, project.ClientPath, StringComparison.OrdinalIgnoreCase));
            if (client?.Profile is null) { PageSubtitle.Text = "Complétez d’abord la fiche de ce client historique."; return; }
            var completion = new TextPromptWindow("Compléter la fiche projet", "Le dossier et les fichiers existants seront conservés.", project.ProjectName);
            if (!await completion.ShowDialog<bool>(this)) return;
            try
            {
                var completed = await Task.Run(() => new WorkspaceProjectCreator().CompleteExisting(root, project, client.Profile, completion.Value));
                PageSubtitle.Text = "Fiche projet historique complétée.";
                await LoadProjectDirectoryAsync(project with { Profile = completed }, project.ProjectPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { PageSubtitle.Text = exception.Message; }
            return;
        }
        var profile = project.Profile;
        var dialog = new EditProjectWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateProject(root, profile, dialog.Status, dialog.DueDate, dialog.Description, dialog.Notes));
            PageSubtitle.Text = "Fiche projet enregistrée.";
            await LoadProjectDirectoryAsync(project with { Profile = updated }, project.ProjectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            PageSubtitle.Text = exception.Message;
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
                PageTitle.Text = project.ProjectName;
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
        { PageSubtitle.Text = "Impossible d’ouvrir ce dossier : " + exception.Message; }
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
            PageSubtitle.Text = "Projet envoyé dans la corbeille.";
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void RenameProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectSummary project }) return;
        var dialog = new TextPromptWindow("Renommer le projet", "La référence du projet restera inchangée.", project.ProjectName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameProject(project, dialog.Value));
            PageTitle.Text = "Projets";
            PageSubtitle.Text = "Projet renommé.";
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Projet déplacé.";
            await LoadProjectsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void RefreshClients_Click(object? sender, RoutedEventArgs e) => await LoadClientsAsync();

    private async void CreateClient_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateClientWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try
        {
            if (dialog.IsIndividual)
                new WorkspaceClientCreator().CreateIndividual(root, dialog.FirstName, dialog.LastName, dialog.Code, dialog.Address);
            else
                new WorkspaceClientCreator().CreateProfessional(root, dialog.ClientName, dialog.Code, dialog.Siret, dialog.Address);
            PageTitle.Text = "Clients";
            PageSubtitle.Text = "Client créé.";
            ShowWorkspace("Clients");
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            PageSubtitle.Text = exception.Message;
        }
    }

    private async Task LoadClientsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        ClientList.Children.Clear();
        ClientList.Spacing = 0;
        _currentClientDetail = null;
        ClientsPanelTitle.Text = "Tous les clients";
        ClientsPanelSubtitle.Text = "Catalogue lu depuis votre espace de travail.";
        ClientsPanelActions.IsVisible = true;
        try
        {
            var clients = await Task.Run(() => new WorkspaceClientReader().Load(workspaceRoot));
            if (clients.Count == 0)
            {
                ClientList.Children.Add(new TextBlock { Text = "Aucun client trouvé.", Margin = new Avalonia.Thickness(8) });
                return;
            }

            ClientList.Children.Add(CreateCompactTableHeader("2*,*,Auto", "CLIENT", "FICHE", "PROJETS"));

            foreach (var client in clients)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), ColumnSpacing = 16 };
                var name = CreateCompactTableCell(client.DisplayName, prominent: true);
                var profile = CreateCompactTableCell(client.ProfileLabel);
                var projects = CreateCompactTableCell($"{client.ProjectCount} projet(s)", accent: true);
                Grid.SetColumn(profile, 1); Grid.SetColumn(projects, 2);
                row.Children.Add(name); row.Children.Add(profile); row.Children.Add(projects);
                var item = new Button
                {
                    Content = row,
                    Tag = client,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Classes = { "table-row" }
                };
                item.Click += ClientEntry_Click;
                ClientList.Children.Add(item);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClientList.Children.Add(new TextBlock { Text = "Impossible de lire les clients.", Margin = new Avalonia.Thickness(8) });
        }
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
        _currentClientDetail = client;
        ClientsPanelTitle.Text = "Fiche client";
        ClientsPanelSubtitle.Text = client.DisplayName;
        ClientsPanelActions.IsVisible = false;
        var back = new Button { Content = "← Retour aux clients", HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            PageTitle.Text = "Clients";
            PageSubtitle.Text = "Espace clients de l’atelier.";
            await LoadClientsAsync();
        };
        ClientList.Children.Add(back);

        var card = new StackPanel { Spacing = 8 };
        card.Children.Add(new TextBlock { Text = client.DisplayName, FontSize = 18, FontWeight = FontWeight.SemiBold });
        card.Children.Add(new TextBlock { Text = $"{client.ProfileLabel} · {client.ClientCode} · {client.ProjectCount} projet(s)", Classes = { "muted" } });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Address)) card.Children.Add(new TextBlock { Text = client.Profile.Address, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Notes)) card.Children.Add(new TextBlock { Text = client.Profile.Notes, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { Content = "✎  Modifier la fiche", Tag = client };
        edit.Click += EditClient_Click;
        var open = new Button { Content = "↗  Ouvrir le dossier", Tag = client.ClientPath };
        open.Click += OpenFolder_Click;
        var rename = new Button { Content = "↻  Renommer", Tag = client };
        rename.Click += RenameClient_Click;
        var move = new Button { Content = "⇄  Déplacer", Tag = client };
        move.Click += MoveClient_Click;
        var trash = new Button { Content = "⌫  Corbeille", Tag = client };
        trash.Click += TrashClient_Click;
        card.Children.Add(CreateOverflowActions(PlacementMode.BottomEdgeAlignedLeft, open, edit, rename, move, trash));
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
                    Content = $"{project.Reference} · {project.ProjectName}\n{project.StatusLabel}",
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
            PageSubtitle.Text = "Impossible de lire les projets de ce client.";
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
                var completedProfile = await Task.Run(() => new WorkspaceClientCreator().CompleteExisting(root, client.ClientPath,
                    completion.IsIndividual ? ClientKind.Individual : ClientKind.Professional,
                    completion.IsIndividual ? completion.LastName : completion.ClientName, completion.Code, completion.FirstName, completion.LastName, completion.Siret, completion.Address));
                PageSubtitle.Text = "Fiche client historique complétée.";
                await LoadClientDetailAsync(client with { Profile = completedProfile });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { PageSubtitle.Text = exception.Message; }
            return;
        }
        var profile = client.Profile;
        var dialog = new EditClientWindow(profile);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var updated = await Task.Run(() => new WorkspaceProfileEditor().UpdateClient(root, profile, dialog.Address, dialog.Notes,
                dialog.ContactFirstName, dialog.ContactLastName, dialog.ContactRole, dialog.ContactPhone, dialog.ContactEmail,
                dialog.CompanyName, dialog.Siret, dialog.IdentityFirstName, dialog.IdentityLastName));
            PageSubtitle.Text = "Fiche client enregistrée.";
            await LoadClientDetailAsync(client with { Profile = updated });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            PageSubtitle.Text = exception.Message;
        }
    }

    private async void TrashClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        if (!await new ConfirmTrashWindow(client.DisplayName, "Ses projets et tous ses fichiers seront concernés.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashClient(client));
            PageTitle.Text = "Clients";
            PageSubtitle.Text = "Client envoyé dans la corbeille.";
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void RenameClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientSummary client }) return;
        var dialog = new TextPromptWindow("Renommer le client", "Le code client et les références existantes restent inchangés.", client.FolderName);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameClient(client, dialog.Value));
            PageTitle.Text = "Clients";
            PageSubtitle.Text = "Client renommé.";
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Client déplacé.";
            await LoadClientsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            var item = await Task.Run(() => new WorkspaceCollectionCreator().Create(root, dialog.Category, dialog.NameValue));
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, dialog.Category, item.Name, null, null));
            PageSubtitle.Text = "Collection créée.";
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async Task LoadCollectionsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        CollectionList.Children.Clear();
        try
        {
            var collections = await Task.Run(() => new WorkspaceInventoryReader().LoadAllCollections(workspaceRoot));
            if (collections.Count == 0) { CollectionList.Children.Add(new TextBlock { Text = "Aucune collection trouvée.", Margin = new Avalonia.Thickness(8) }); return; }
            if (_collectionCategoryFilter.Length > 0)
                collections = collections.Where(item => CollectionCategory(workspaceRoot, item) == _collectionCategoryFilter).ToArray();
            if (collections.Count == 0) { CollectionList.Children.Add(new TextBlock { Text = "Aucun élément dans cette catégorie.", Margin = new Avalonia.Thickness(8) }); return; }
            CollectionList.Children.Add(CreateCompactTableHeader("2*,*,2*,Auto", "COLLECTION", "TYPE", "INFORMATIONS", "ACTIONS"));
            foreach (var item in collections)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,2*,Auto"), ColumnSpacing = 16, MinHeight = 44 };
                var name = CreateCompactTableCell(item.Name, prominent: true);
                var type = CreateCompactTableCell(CollectionCategoryLabel(CollectionCategory(workspaceRoot, item)), accent: true);
                var detail = CreateCompactTableCell((item.Profile?.Description ?? "—") + $" · {item.LastWriteTime:dd/MM/yyyy}");
                Grid.SetColumn(type, 1); Grid.SetColumn(detail, 2);
                var open = new Button { Content = "↗  Ouvrir", Tag = item.Path }; open.Click += OpenFolder_Click;
                var edit = new Button { Content = "✎  Modifier", Tag = item }; edit.Click += EditCollection_Click;
                var rename = new Button { Content = "↻  Renommer", Tag = item }; rename.Click += RenameCollection_Click;
                var move = new Button { Content = "⇄  Déplacer", Tag = item }; move.Click += MoveCollection_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = item }; trash.Click += TrashCollection_Click;
                var actions = CreateOverflowActions(open, edit, rename, move, trash);
                Grid.SetColumn(actions, 3);
                row.Children.Add(name); row.Children.Add(type); row.Children.Add(detail); row.Children.Add(actions);
                CollectionList.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#263A56")), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Padding = new Avalonia.Thickness(14, 6), Child = row });
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
            await Task.Run(() => new WorkspaceCollectionEditor().Save(root, item, category, dialog.NameValue, dialog.Description, dialog.Notes,
                dialog.Contact, dialog.Address, dialog.Phone, dialog.Email, dialog.Website));
            PageSubtitle.Text = "Fiche collection enregistrée.";
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenameCollection(item, CollectionCategory(root, item), dialog.Value));
            PageSubtitle.Text = "Collection renommée.";
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Collection déplacée.";
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void TrashCollection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Collections.CollectionItemSummary item }) return;
        if (!await new ConfirmTrashWindow(item.Name, "La collection et son contenu pourront être restaurés depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).TrashCollection(item));
            PageSubtitle.Text = "Collection envoyée dans la corbeille.";
            await LoadCollectionsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            EstimateProject.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectSummary>((project, _) => new TextBlock { Text = project is null ? "Projet indisponible" : $"{project.Reference} · {project.ClientFolderName}" });
            EstimateProject.SelectedIndex = -1;
            EstimateFilament.ItemsSource = filaments;
            EstimateFilament.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MS3DPRINT.Manager.Core.Filaments.FilamentProfile>((f, _) => new TextBlock { Text = f is null ? "Filament indisponible" : $"{f?.Brand} · {f?.Name} ({f?.PricePerKg:0.00} €/kg)" });
            EstimateFilament.SelectedIndex = filaments.Count > 0 ? 0 : -1;
            EstimatePrinter.ItemsSource = printers.Where(printer => printer.IsActive).ToArray();
            EstimatePrinter.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PrinterProfile>((printer, _) => new TextBlock { Text = printer is null ? "Imprimante indisponible" : $"{printer.DisplayName} · {PrinterCostCalculator.HourlyRate(printer):0.00} €/h" });
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
            PageSubtitle.Text = "Imprimante ajoutée.";
            await LoadPrintersAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Imprimante enregistrée.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Imprimante envoyée dans la corbeille.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { PageSubtitle.Text = exception.Message; }
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
                PrinterList.Children.Add(new TextBlock { Text = "Aucune imprimante enregistrée.", Margin = new Avalonia.Thickness(8) });
                return;
            }
            PrinterList.Children.Add(CreateCompactTableHeader("2*,*,*,Auto", "IMPRIMANTE", "COÛT HORAIRE", "PUISSANCE", "ACTIONS"));
            foreach (var printer in printers)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,Auto"), ColumnSpacing = 16, MinHeight = 44 };
                var name = CreateCompactTableCell(printer.DisplayName, prominent: true);
                var rate = CreateCompactTableCell($"{PrinterCostCalculator.HourlyRate(printer):0.00} €/h", accent: true);
                var power = CreateCompactTableCell(printer.PowerWatts is { } watts ? $"{watts} W" : "—");
                Grid.SetColumn(rate, 1); Grid.SetColumn(power, 2);
                var edit = new Button { Content = "✎  Modifier", Tag = printer };
                edit.Click += EditPrinter_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = printer };
                trash.Click += TrashPrinter_Click;
                var actions = CreateOverflowActions(edit, trash);
                Grid.SetColumn(actions, 3);
                row.Children.Add(name); row.Children.Add(rate); row.Children.Add(power); row.Children.Add(actions);
                PrinterList.Children.Add(new Border { Background = Avalonia.Media.Brushes.Transparent, BorderBrush = new SolidColorBrush(Color.Parse("#263A56")), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Padding = new Avalonia.Thickness(14, 6), Child = row });
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
            foreach (var estimate in estimates.Take(4))
            {
                var button = new Button { Content = $"{estimate.Name} · {estimate.Result.FinalPrice:0.00} €", Tag = estimate, HorizontalContentAlignment = HorizontalAlignment.Left };
                button.Click += LoadSavedEstimate_Click;
                var trash = new Button { Content = "Corbeille", Tag = estimate };
                trash.Click += TrashEstimate_Click;
                SavedEstimateList.Children.Add(button);
                SavedEstimateList.Children.Add(trash);
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
            PageSubtitle.Text = "Filament ajouté.";
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
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
            PageSubtitle.Text = "Fiche filament enregistrée.";
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async Task LoadFilamentsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        FilamentList.Children.Clear();
        try
        {
            var filaments = await Task.Run(() => new WorkspaceInventoryReader().LoadFilaments(workspaceRoot));
            if (filaments.Count == 0) { FilamentList.Children.Add(new TextBlock { Text = "Aucun filament enregistré.", Margin = new Avalonia.Thickness(8) }); return; }
            FilamentList.Children.Add(CreateCompactTableHeader("2*,*,Auto", "FILAMENT", "MATIÈRE / PRIX", "ACTIONS"));
            foreach (var filament in filaments)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), ColumnSpacing = 16, MinHeight = 44 };
                var name = CreateCompactTableCell($"{filament.Brand} · {filament.Name}", prominent: true);
                var color = string.IsNullOrWhiteSpace(filament.Color) ? string.Empty : $" · {filament.Color}";
                var material = CreateCompactTableCell($"{filament.Material}{color} · {filament.PricePerKg:0.00} €/kg{(filament.IsAbrasive ? " · Abrasif" : string.Empty)}", accent: filament.IsAbrasive);
                Grid.SetColumn(material, 1);
                var edit = new Button { Content = "✎  Modifier", Tag = filament };
                edit.Click += EditFilament_Click;
                var trash = new Button { Content = "⌫  Corbeille", Tag = filament };
                trash.Click += TrashFilament_Click;
                var actions = CreateOverflowActions(edit, trash);
                Grid.SetColumn(actions, 2);
                row.Children.Add(name); row.Children.Add(material); row.Children.Add(actions);
                FilamentList.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#263A56")), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Padding = new Avalonia.Thickness(14, 6), Child = row });
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
            PageSubtitle.Text = "Filament envoyé dans la corbeille.";
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async void RefreshTrash_Click(object? sender, RoutedEventArgs e) => await LoadTrashAsync();

    private async Task LoadTrashAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        TrashList.Children.Clear();
        try
        {
            var entries = await Task.Run(() => new WorkspaceTrashReader().Load(workspaceRoot));
            if (entries.Count == 0) { TrashList.Children.Add(new TextBlock { Text = "La corbeille est vide.", Margin = new Avalonia.Thickness(8) }); return; }
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
        { PageSubtitle.Text = exception.Message; }
    }

    private async void RefreshSettings_Click(object? sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async void VerifyWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try
        {
            var result = await Task.Run(() => new MS3DPRINT.Manager.Core.Storage.FolderTreeService().EnsureMainStructure(root));
            PageSubtitle.Text = result.CreatedFolders.Count == 0
                ? "Arborescence vérifiée : tous les dossiers existent."
                : $"Arborescence vérifiée : {result.CreatedFolders.Count} dossier(s) ajouté(s).";
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { PageSubtitle.Text = exception.Message; }
    }

    private async Task LoadSettingsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        StorageList.Children.Clear();
        var providers = await Task.Run(() => new WorkspaceStorageReader().Load(workspaceRoot));
        var open = new Button { Content = "Ouvrir l’espace de travail", Tag = workspaceRoot, HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += OpenFolder_Click;
        StorageList.Children.Add(open);
        foreach (var provider in providers)
            StorageList.Children.Add(new TextBlock { Text = $"{provider.DisplayName} · {(provider.IsAvailable ? "Disponible" : "Non configuré")}", Margin = new Avalonia.Thickness(8) });
    }
}
