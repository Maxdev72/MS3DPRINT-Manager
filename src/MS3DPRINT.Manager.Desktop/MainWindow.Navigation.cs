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

/// <summary>Workspace navigation: showing a page, breadcrumb, menu clicks and keyboard shortcuts.</summary>
public partial class MainWindow
{
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
        if (ViewerWorkspacePanel.IsVisible) { _modelViewer?.Release(); ViewerWorkspacePanel.IsVisible = false; ViewerNavButton.Classes.Remove("selected"); _hiddenByViewer = []; RefreshCentralWidth(); }
        ClearBreadcrumb();
        FadeIn(destination switch
        {
            "Projets" => ProjectsWorkspacePanel, "Clients" => ClientsWorkspacePanel, "Collections" => CollectionsWorkspacePanel,
            "Filaments" => FilamentsWorkspacePanel, "Estimations" => EstimatesWorkspacePanel, "Imprimantes" => PrintersWorkspacePanel,
            "Corbeille" => TrashWorkspacePanel, "Paramètres" => SettingsWorkspacePanel, _ => null
        });
        _selectedNavigationButton = NavigationButtons().FirstOrDefault(button => button.Tag as string == destination);
    }

    private static void FadeIn(Control? panel)
    {
        if (panel is null) return;
        panel.Opacity = 0;
        Dispatcher.UIThread.Post(() => panel.Opacity = 1, DispatcherPriority.Background);
    }

    private void ClearBreadcrumb()
    {
        Breadcrumb.Children.Clear();
        Breadcrumb.IsVisible = false;
    }

    /// <summary>Clickable path above the page title; the last segment is the current place.</summary>
    private void SetBreadcrumb(params (string Label, Func<Task>? Go)[] segments)
    {
        Breadcrumb.Children.Clear();
        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
                Breadcrumb.Children.Add(new TextBlock { Text = "›", Classes = { "faint" }, VerticalAlignment = VerticalAlignment.Center });
            var (label, go) = segments[index];
            if (go is null || index == segments.Length - 1)
            {
                Breadcrumb.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 });
                continue;
            }
            var crumb = new Button { Content = label, Classes = { "crumb" } };
            crumb.Click += async (_, _) => await go();
            Breadcrumb.Children.Add(crumb);
        }
        Breadcrumb.IsVisible = segments.Length > 1;
    }

    private async Task GoToProjectsAsync()
    {
        ShowWorkspace("Projets");
        PageTitle.Text = "Projets";
        PageSubtitle.Text = PageSubtitles["Projets"];
        await LoadProjectsAsync();
    }

    private async Task GoToClientsAsync()
    {
        ShowWorkspace("Clients");
        PageTitle.Text = "Clients";
        PageSubtitle.Text = PageSubtitles["Clients"];
        await LoadClientsAsync();
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
}
