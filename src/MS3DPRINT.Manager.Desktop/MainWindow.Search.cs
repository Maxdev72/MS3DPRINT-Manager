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

/// <summary>Global search box (Ctrl+K) and its results.</summary>
public partial class MainWindow
{
    private int _searchIndex = -1;

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
        texts.Children.Add(new TextBlock { Text = result.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = result.Detail, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
        var badge = new Border { Classes = { "badge" }, Child = new TextBlock { Text = kind } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(texts, 1); Grid.SetColumn(badge, 2);
        row.Children.Add(icon); row.Children.Add(texts); row.Children.Add(badge);
        return row;
    }

    private async void SearchBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab) return;
        _searchIndex = -1;
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
        var workspaceRoot = WorkspaceRoot;
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
                var item = new Button { Content = CreateSearchRow(result), Classes = { "search-item" }, Tag = result };
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
            PageSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
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
}
