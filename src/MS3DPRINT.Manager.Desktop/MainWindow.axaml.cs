using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MS3DPRINT.Manager.Desktop.Dashboard;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow : Window
{
    private const string DefaultWorkspaceRoot = @"C:\MS3DPRINT\Nextcloud\MS3DPRINT";
    private CancellationTokenSource? _searchCancellation;

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
        if (sender is not Button { Tag: string destination })
            return;

        PageTitle.Text = destination;
        PageSubtitle.Text = $"Espace {destination.ToLowerInvariant()} de l’atelier.";
        var projectsVisible = destination == "Projets";
        DashboardStats.IsVisible = !projectsVisible;
        DashboardContent.IsVisible = !projectsVisible;
        ProjectsWorkspacePanel.IsVisible = projectsVisible;

        if (projectsVisible)
            await LoadProjectsAsync();
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
                ProjectList.Children.Add(new Border
                {
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141F30")),
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(14),
                    Child = new TextBlock
                    {
                        Text = $"{project.Reference} · {project.ProjectName}\n{project.ClientFolderName} · {project.StatusLabel}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ProjectList.Children.Add(new TextBlock { Text = "Impossible de lire les projets.", Margin = new Avalonia.Thickness(8) });
        }
    }
}
