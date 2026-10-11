using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow : Window
{
    private sealed record ProjectDirectoryContext(ProjectSummary Project, string Directory, string Path);
    private const string DefaultWorkspaceRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
    private CancellationTokenSource? _searchCancellation;
    private Button? _selectedNavigationButton;

    public MainWindow() => InitializeComponent();

    private async void Window_Opened(object? sender, EventArgs e)
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;

        try
        {
            var dashboard = await Task.Run(() => new WorkspaceDashboardReader().Load(workspaceRoot));
            ProjectCountText.Text = dashboard.ProjectCount.ToString();
            QuoteCountText.Text = dashboard.QuoteCount.ToString();
            InProgressCountText.Text = dashboard.InProgressCount.ToString();
            ClientCountText.Text = $"{dashboard.ClientCount} client(s)";
            WorkspaceStatusText.Text = $"{dashboard.CompletedCount} projet(s) terminé(s).";
            ApplyRecentProject(dashboard.RecentProjects.ElementAtOrDefault(0), RecentProjectOneTitle, RecentProjectOneDetail, RecentProjectOneStatus);
            ApplyRecentProject(dashboard.RecentProjects.ElementAtOrDefault(1), RecentProjectTwoTitle, RecentProjectTwoDetail, RecentProjectTwoStatus);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            WorkspaceStatusText.Text = "Impossible de lire l’espace de travail.";
        }
    }

    private async void NavigationButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string destination)
            return;

        foreach (var navigationButton in NavigationItems.Children.OfType<Button>()) navigationButton.Classes.Remove("selected");
        button.Classes.Add("selected");
        _selectedNavigationButton = button;

        PageTitle.Text = destination;
        PageSubtitle.Text = $"Espace {destination.ToLowerInvariant()} de l’atelier.";
        var projectsVisible = destination == "Projets";
        var clientsVisible = destination == "Clients";
        var collectionsVisible = destination == "Collections";
        var filamentsVisible = destination == "Filaments";
        var trashVisible = destination == "Corbeille";
        var settingsVisible = destination == "Paramètres";
        DashboardStats.IsVisible = !projectsVisible && !clientsVisible && !collectionsVisible && !filamentsVisible && !trashVisible && !settingsVisible;
        DashboardContent.IsVisible = !projectsVisible && !clientsVisible && !collectionsVisible && !filamentsVisible && !trashVisible && !settingsVisible;
        ProjectsWorkspacePanel.IsVisible = projectsVisible;
        ClientsWorkspacePanel.IsVisible = clientsVisible;
        CollectionsWorkspacePanel.IsVisible = collectionsVisible;
        FilamentsWorkspacePanel.IsVisible = filamentsVisible;
        TrashWorkspacePanel.IsVisible = trashVisible;
        SettingsWorkspacePanel.IsVisible = settingsVisible;

        if (projectsVisible)
            await LoadProjectsAsync();
        else if (clientsVisible)
            await LoadClientsAsync();
        else if (collectionsVisible)
            await LoadCollectionsAsync();
        else if (filamentsVisible)
            await LoadFilamentsAsync();
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
            ProjectsWorkspacePanel.IsVisible = true;
            DashboardStats.IsVisible = false;
            DashboardContent.IsVisible = false;
            PageTitle.Text = project.ProjectName;
            PageSubtitle.Text = project.Reference;
            await LoadProjectDirectoryAsync(project, project.ProjectPath);
        }
        else if (result.Client is not null)
        {
            ClientsWorkspacePanel.IsVisible = true;
            DashboardStats.IsVisible = false;
            DashboardContent.IsVisible = false;
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
        TextBlock title,
        TextBlock detail,
        TextBlock status)
    {
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

    private async void RefreshProjects_Click(object? sender, RoutedEventArgs e) => await LoadProjectsAsync();

    private async void DashboardProjects_Click(object? sender, RoutedEventArgs e)
    {
        DashboardStats.IsVisible = false;
        DashboardContent.IsVisible = false;
        ProjectsWorkspacePanel.IsVisible = true;
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
                DashboardStats.IsVisible = false;
                DashboardContent.IsVisible = false;
                ProjectsWorkspacePanel.IsVisible = true;
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

        try
        {
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(workspaceRoot));
            if (projects.Count == 0)
            {
                ProjectList.Children.Add(new TextBlock { Text = "Aucun projet trouvé.", Margin = new Avalonia.Thickness(8) });
                return;
            }

            foreach (var project in projects)
            {
                var button = new Button
                {
                    Tag = project,
                    Content = $"{project.Reference} · {project.ProjectName}\n{project.ClientFolderName} · {project.StatusLabel}",
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Classes = { "workspace-card" }
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
        PageTitle.Text = project.ProjectName;
        PageSubtitle.Text = project.Reference;
        await LoadProjectDirectoryAsync(project, project.ProjectPath);
    }

    private async Task LoadProjectDirectoryAsync(MS3DPRINT.Manager.Core.Projects.ProjectSummary project, string directory)
    {
        ProjectList.Children.Clear();
        var back = new Button { Content = directory == project.ProjectPath ? "← Retour aux projets" : "← Dossier parent", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            if (directory == project.ProjectPath) await LoadProjectsAsync();
            else await LoadProjectDirectoryAsync(project, new ProjectWorkspaceBrowser().Parent(project.ProjectPath, directory));
        };
        ProjectList.Children.Add(back);
        var createFolder = new Button { Content = "+ Nouveau dossier", Tag = (Project: project, Directory: directory), HorizontalAlignment = HorizontalAlignment.Left };
        createFolder.Click += CreateProjectFolder_Click;
        ProjectList.Children.Add(createFolder);
        var import = new Button { Content = "Importer un fichier", Tag = (Project: project, Directory: directory), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += ImportProjectFile_Click;
        ProjectList.Children.Add(import);
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
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    var file = new Button { Content = $"Fichier · {entry.Name}", Tag = entry.FullPath, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                    file.Click += FileEntry_Click;
                    var rename = new Button { Content = "Renommer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    rename.Click += RenameProjectFile_Click;
                    var move = new Button { Content = "Déplacer", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    move.Click += MoveProjectEntry_Click;
                    var trash = new Button { Content = "Corbeille", Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    trash.Click += TrashProjectFile_Click;
                    row.Children.Add(file); row.Children.Add(rename); row.Children.Add(move); row.Children.Add(trash);
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

            if (ThreeDFileSupport.IsPreviewable(path) && DesktopPreviewLauncher.TryOpenThreeD(path))
                return;

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
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var edit = new Button { Content = "Modifier la fiche", Tag = project };
        edit.Click += EditProject_Click;
        var open = new Button { Content = "Ouvrir le dossier", Tag = project.ProjectPath };
        open.Click += OpenFolder_Click;
        var rename = new Button { Content = "Renommer", Tag = project };
        rename.Click += RenameProject_Click;
        var move = new Button { Content = "Déplacer", Tag = project };
        move.Click += MoveProject_Click;
        var trash = new Button { Content = "Corbeille", Tag = project };
        trash.Click += TrashProject_Click;
        commands.Children.Add(edit);
        commands.Children.Add(open);
        commands.Children.Add(rename);
        commands.Children.Add(move);
        commands.Children.Add(trash);
        content.Children.Add(commands);
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
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null) await launcher.LaunchUriAsync(new Uri(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException)
        {
            PageSubtitle.Text = "Impossible d’ouvrir ce dossier.";
        }
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
                new WorkspaceClientCreator().CreateIndividual(root, dialog.FirstName, dialog.LastName, dialog.Code);
            else
                new WorkspaceClientCreator().CreateProfessional(root, dialog.ClientName, dialog.Code);
            PageTitle.Text = "Clients";
            PageSubtitle.Text = "Client créé.";
            DashboardStats.IsVisible = false;
            DashboardContent.IsVisible = false;
            ClientsWorkspacePanel.IsVisible = true;
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
        try
        {
            var clients = await Task.Run(() => new WorkspaceClientReader().Load(workspaceRoot));
            if (clients.Count == 0)
            {
                ClientList.Children.Add(new TextBlock { Text = "Aucun client trouvé.", Margin = new Avalonia.Thickness(8) });
                return;
            }

            foreach (var client in clients)
            {
                var item = new Button
                {
                    Content = $"{client.DisplayName}\n{client.ProfileLabel} · {client.ProjectCount} projet(s)",
                    Tag = client,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Classes = { "workspace-card" }
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
        var back = new Button { Content = "← Retour aux clients", HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += async (_, _) => await LoadClientsAsync();
        ClientList.Children.Add(back);

        var card = new StackPanel { Spacing = 8 };
        card.Children.Add(new TextBlock { Text = client.DisplayName, FontSize = 18, FontWeight = FontWeight.SemiBold });
        card.Children.Add(new TextBlock { Text = $"{client.ProfileLabel} · {client.ClientCode} · {client.ProjectCount} projet(s)", Classes = { "muted" } });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Address)) card.Children.Add(new TextBlock { Text = client.Profile.Address, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(client.Profile?.Notes)) card.Children.Add(new TextBlock { Text = client.Profile.Notes, TextWrapping = TextWrapping.Wrap });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var edit = new Button { Content = "Modifier la fiche", Tag = client };
        edit.Click += EditClient_Click;
        var open = new Button { Content = "Ouvrir le dossier", Tag = client.ClientPath };
        open.Click += OpenFolder_Click;
        var rename = new Button { Content = "Renommer", Tag = client };
        rename.Click += RenameClient_Click;
        var move = new Button { Content = "Déplacer", Tag = client };
        move.Click += MoveClient_Click;
        var trash = new Button { Content = "Corbeille", Tag = client };
        trash.Click += TrashClient_Click;
        commands.Children.Add(edit); commands.Children.Add(open); commands.Children.Add(rename); commands.Children.Add(move); commands.Children.Add(trash); card.Children.Add(commands);
        ClientList.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(16), Child = card });
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var projects = await Task.Run(() => new WorkspaceProjectReader().Load(root)
                .Where(project => string.Equals(project.ClientPath, client.ClientPath, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (projects.Length > 0) ClientList.Children.Add(new TextBlock { Text = "Projets", FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            foreach (var project in projects)
            {
                var projectButton = new Button { Content = $"{project.Reference} · {project.ProjectName}\n{project.StatusLabel}", Tag = project, HorizontalContentAlignment = HorizontalAlignment.Left };
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
                    completion.IsIndividual ? completion.LastName : completion.ClientName, completion.Code, completion.FirstName, completion.LastName));
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

    private async void ClassifyFile_Click(object? sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        if (await new ClassifyFileWindow(root).ShowDialog<bool>(this)) PageSubtitle.Text = "Fichier classé dans le projet.";
    }

    private void OpenThreeDViewer_Click(object? sender, RoutedEventArgs e)
    {
        if (DesktopPreviewLauncher.OpenThreeD is null) { PageSubtitle.Text = "Le visualiseur 3D n’est pas disponible sur cette plateforme."; return; }
        DesktopPreviewLauncher.OpenThreeD(null);
    }

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
            foreach (var item in collections)
            {
                var content = new StackPanel { Spacing = 6 };
                content.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold });
                content.Children.Add(new TextBlock { Text = (item.Profile?.Description ?? "Collection") + $" · Modifié le {item.LastWriteTime:dd/MM/yyyy}", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
                var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var open = new Button { Content = "Ouvrir", Tag = item.Path }; open.Click += OpenFolder_Click;
                var edit = new Button { Content = "Modifier la fiche", Tag = item }; edit.Click += EditCollection_Click;
                var rename = new Button { Content = "Renommer", Tag = item }; rename.Click += RenameCollection_Click;
                var move = new Button { Content = "Déplacer", Tag = item }; move.Click += MoveCollection_Click;
                var trash = new Button { Content = "Corbeille", Tag = item }; trash.Click += TrashCollection_Click;
                commands.Children.Add(open); commands.Children.Add(edit); commands.Children.Add(rename); commands.Children.Add(move); commands.Children.Add(trash); content.Children.Add(commands);
                CollectionList.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(10), Padding = new Avalonia.Thickness(14), Child = content });
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

    private async void CreateFilament_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateFilamentWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new WorkspaceFilamentEditor().Create(root, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive));
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
            await Task.Run(() => new WorkspaceFilamentEditor().Update(root, original, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive));
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
            foreach (var filament in filaments)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var button = new Button { Content = $"{filament.Brand} · {filament.Name}\n{filament.Material} · {filament.PricePerKg:0.00} €/kg{(filament.IsAbrasive ? " · Abrasif" : string.Empty)}", Tag = filament, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "workspace-card" } };
                button.Click += EditFilament_Click;
                var trash = new Button { Content = "Corbeille", Tag = filament };
                trash.Click += TrashFilament_Click;
                row.Children.Add(button); row.Children.Add(trash);
                FilamentList.Children.Add(row);
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
            foreach (var entry in entries)
            {
                var restore = new Button { Content = $"Restaurer · {entry.Label}", Tag = entry.Id, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                restore.Click += RestoreTrash_Click;
                TrashList.Children.Add(restore);
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
