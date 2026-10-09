using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow : Window
{
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

        _selectedNavigationButton?.Classes.Remove("selected");
        button.Classes.Add("selected");
        _selectedNavigationButton = button;

        PageTitle.Text = destination;
        PageSubtitle.Text = $"Espace {destination.ToLowerInvariant()} de l’atelier.";
        var projectsVisible = destination == "Projets";
        var clientsVisible = destination == "Clients";
        var collectionsVisible = destination == "Collections";
        var filamentsVisible = destination == "Filaments";
        DashboardStats.IsVisible = !projectsVisible && !clientsVisible && !collectionsVisible && !filamentsVisible;
        DashboardContent.IsVisible = !projectsVisible && !clientsVisible && !collectionsVisible && !filamentsVisible;
        ProjectsWorkspacePanel.IsVisible = projectsVisible;
        ClientsWorkspacePanel.IsVisible = clientsVisible;
        CollectionsWorkspacePanel.IsVisible = collectionsVisible;
        FilamentsWorkspacePanel.IsVisible = filamentsVisible;

        if (projectsVisible)
            await LoadProjectsAsync();
        else if (clientsVisible)
            await LoadClientsAsync();
        else if (collectionsVisible)
            await LoadCollectionsAsync();
        else if (filamentsVisible)
            await LoadFilamentsAsync();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
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
                SearchResults.Children.Add(new TextBlock
                {
                    Text = $"{result.Title}\n{result.Detail}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Padding = new Avalonia.Thickness(10, 8)
                });
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
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
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
        try
        {
            var entries = await Task.Run(() => new ProjectWorkspaceBrowser().List(project.ProjectPath, directory));
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    var folder = new Button { Content = $"Dossier · {entry.Name}", HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                    folder.Click += async (_, _) => await LoadProjectDirectoryAsync(project, entry.FullPath);
                    ProjectList.Children.Add(folder);
                }
                else ProjectList.Children.Add(new TextBlock { Text = $"Fichier · {entry.Name}", Margin = new Avalonia.Thickness(12, 6) });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { ProjectList.Children.Add(new TextBlock { Text = "Impossible de lire les fichiers du projet.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void RefreshClients_Click(object? sender, RoutedEventArgs e) => await LoadClientsAsync();

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
                ClientList.Children.Add(new Border
                {
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141F30")),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(14),
                    Child = new TextBlock
                    {
                        Text = $"{client.DisplayName}\n{client.ProfileLabel} · {client.ProjectCount} projet(s)",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ClientList.Children.Add(new TextBlock { Text = "Impossible de lire les clients.", Margin = new Avalonia.Thickness(8) });
        }
    }

    private async void RefreshCollections_Click(object? sender, RoutedEventArgs e) => await LoadCollectionsAsync();

    private async Task LoadCollectionsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        CollectionList.Children.Clear();
        try
        {
            var collections = await Task.Run(() => new WorkspaceInventoryReader().LoadAllCollections(workspaceRoot));
            if (collections.Count == 0) { CollectionList.Children.Add(new TextBlock { Text = "Aucune collection trouvée.", Margin = new Avalonia.Thickness(8) }); return; }
            foreach (var item in collections)
                CollectionList.Children.Add(new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(14), Child = new TextBlock { Text = $"{item.Name}\nModifié le {item.LastWriteTime:dd/MM/yyyy}", TextWrapping = Avalonia.Media.TextWrapping.Wrap } });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { CollectionList.Children.Add(new TextBlock { Text = "Impossible de lire les collections.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void RefreshFilaments_Click(object? sender, RoutedEventArgs e) => await LoadFilamentsAsync();

    private async Task LoadFilamentsAsync()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        FilamentList.Children.Clear();
        try
        {
            var filaments = await Task.Run(() => new WorkspaceInventoryReader().LoadFilaments(workspaceRoot));
            if (filaments.Count == 0) { FilamentList.Children.Add(new TextBlock { Text = "Aucun filament enregistré.", Margin = new Avalonia.Thickness(8) }); return; }
            foreach (var filament in filaments)
                FilamentList.Children.Add(new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141F30")), CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(14), Child = new TextBlock { Text = $"{filament.Brand} · {filament.Name}\n{filament.Material} · {filament.PricePerKg:0.00} €/kg{(filament.IsAbrasive ? " · Abrasif" : string.Empty)}", TextWrapping = Avalonia.Media.TextWrapping.Wrap } });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { FilamentList.Children.Add(new TextBlock { Text = "Impossible de lire les filaments.", Margin = new Avalonia.Thickness(8) }); }
    }
}
