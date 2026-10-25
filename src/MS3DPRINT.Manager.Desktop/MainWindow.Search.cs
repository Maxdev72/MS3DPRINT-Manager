using Avalonia.Controls;
using Avalonia;
using Avalonia.VisualTree;
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

/// <summary>Global search box (Ctrl+K) and its results.</summary>
public partial class MainWindow
{
    private int _searchIndex = -1;

    private void Workspace_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!SearchResultsPanel.IsVisible || e.Source is not Control source) return;
        if (source != SearchResultsPanel && source != GlobalSearchContainer &&
            !source.GetVisualAncestors().Any(parent => parent == SearchResultsPanel || parent == GlobalSearchContainer))
            DismissSearchResults();
    }

    private void PositionSearchResults()
    {
        if (GlobalSearchContainer.TranslatePoint(new Point(0, GlobalSearchContainer.Bounds.Height), CentralWorkspace) is { } bottom)
            SearchResultsPanel.Margin = new Thickness(0, bottom.Y + 8, 0, 0);
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        var items = SearchResults.Children.OfType<Button>().ToArray();
        if (e.Key is not (Key.Down or Key.Up or Key.Enter) || items.Length == 0 || !SearchResultsPanel.IsVisible) return;
        if (e.Key == Key.Enter)
        {
            items[Math.Clamp(_searchIndex, 0, items.Length - 1)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else
        {
            _searchIndex = e.Key == Key.Down ? Math.Min(items.Length - 1, _searchIndex + 1) : Math.Max(0, _searchIndex - 1);
            for (var index = 0; index < items.Length; index++)
            {
                if (index == _searchIndex) items[index].Classes.Add("selected"); else items[index].Classes.Remove("selected");
            }
            items[_searchIndex].BringIntoView();
        }
        e.Handled = true;
    }

    private static Control CreateSearchRow(MS3DPRINT.Manager.Core.Search.GlobalSearchResult result)
    {
        var (iconKey, kind) = result.Kind switch
        {
            MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Project => ("IconProjects", "Projet"),
            MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.Client => ("IconClients", "Client"),
            _ => ("IconFile", "Fichier")
        };
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon", "icon-accent" } };
        if (Avalonia.Application.Current?.TryFindResource(iconKey, out var geometry) == true && geometry is Geometry data) icon.Data = data;
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var title = result.Project?.Title() ?? result.Client?.Title() ?? result.Title;
        var detail = result.Project is { } project ? $"{project.Reference} · {project.ClientTitle()}" : result.Detail;
        texts.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = detail, FontSize = 13, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var badge = new Border { Classes = { "badge" }, Child = new TextBlock { Text = kind } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(texts, 1); Grid.SetColumn(badge, 2);
        row.Children.Add(icon); row.Children.Add(texts); row.Children.Add(badge);
        ToolTip.SetTip(row, title + "\n" + detail);
        return row;
    }

    private void DismissSearchResults()
    {
        _searchCancellation?.Cancel();
        SearchResultsPanel.IsVisible = false;
        SearchResults.Children.Clear();
        _searchIndex = -1;
    }

    private void ClearGlobalSearch_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        DismissSearchResults();
        SearchBox.Focus();
    }

    private void ShowSearchState(string heading, string detail)
    {
        PositionSearchResults();
        SearchResultsHeading.Text = heading;
        SearchResultsKeyboardHint.Text = "Échap Fermer";
        SearchResults.Children.Clear();
        SearchResults.Children.Add(new TextBlock { Text = detail, Margin = new Avalonia.Thickness(12), TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, LineHeight = 22 });
        ShowSearchOverlay();
    }

    private void ShowSearchOverlay()
    {
        var opening = !SearchResultsPanel.IsVisible;
        SearchResultsPanel.IsVisible = true;
        if (opening) BrandMotion.OpenOverlay(SearchResultsPanel, MotionReduced);
    }

    private async void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (SearchBox is null || SearchResultsPanel is null) return;
        _searchIndex = -1;
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        ClearGlobalSearchButton.IsVisible = !string.IsNullOrEmpty(SearchBox.Text);
        GlobalSearchKeyHint.IsVisible = !ClearGlobalSearchButton.IsVisible;

        if (query.Length < 2)
        {
            SearchResultsPanel.IsVisible = false;
            SearchResults.Children.Clear();
            return;
        }

        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var workspaceRoot = WorkspaceRoot;
        ShowSearchState("Recherche en cours…", "Recherche dans votre espace de travail.");
        try
        {
            await Task.Delay(180, cancellationToken);
            var results = await Task.Run(
                () => new WorkspaceSearchReader().Search(workspaceRoot, query, cancellationToken),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !string.Equals(workspaceRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                return;

            SearchResults.Children.Clear();
            if (results.Count == 0)
            {
                ShowSearchState($"Aucun résultat pour « {query} »", "Essayez un nom, une référence ou un code client.");
                return;
            }
            SearchResultsHeading.Text = results.Count > 8 ? "8 premiers résultats" : $"{results.Count} résultat{(results.Count > 1 ? "s" : "")}";
            SearchResultsKeyboardHint.Text = "↑ ↓ Parcourir · Entrée Ouvrir · Échap Fermer";
            foreach (var result in results.Take(8))
            {
                var item = new Button { Content = CreateSearchRow(result), Classes = { "search-item" }, Tag = result };
                item.Click += SearchResult_Click;
                SearchResults.Children.Add(item);
            }
            _searchIndex = 0;
            SearchResults.Children.OfType<Button>().First().Classes.Add("selected");
            ShowSearchOverlay();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!cancellationToken.IsCancellationRequested && string.Equals(workspaceRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                ShowSearchState("Recherche indisponible", "Impossible de lire les résultats. Vérifiez l’accès au dossier puis réessayez.");
        }
    }

    private async void SearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Search.GlobalSearchResult result }) return;
        DismissSearchResults();
        SearchBox.Text = string.Empty;
        if (result.Project is { } project)
        {
            _projectOriginClient = null;
            ShowWorkspaceForDetail("Projets");
            PageTitle.Text = project.Title();
            PageSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
            await LoadProjectDirectoryAsync(project, project.ProjectPath);
        }
        else if (result.Client is not null)
        {
            ShowWorkspaceForDetail("Clients");
            PageTitle.Text = "Clients";
            PageSubtitle.Text = result.Title;
            await LoadClientDetailAsync(result.Client);
        }
        else if (result.Kind == MS3DPRINT.Manager.Core.Search.GlobalSearchResultKind.File)
        {
            await OpenFileAsync(result.Path);
        }
    }
}
