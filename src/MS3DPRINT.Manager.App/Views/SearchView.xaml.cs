using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MS3DPRINT.Manager.Core.Search;

namespace MS3DPRINT.Manager.App.Views;

public partial class SearchView : UserControl
{
    private readonly GlobalSearchService _search;
    private readonly string _workspaceRoot;
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;

    public SearchView(GlobalSearchService search, string workspaceRoot)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
        InitializeComponent();
        Loaded += (_, _) => QueryBox.Focus();
        Unloaded += (_, _) => _searchCancellation?.Cancel();
    }

    public event Action<GlobalSearchResult>? ResultSelected;

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();
    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            MessageText.Text = "Saisissez un nom, une référence ou un code client.";
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var version = ++_searchVersion;
        MessageText.Text = "Recherche en cours…";
        try
        {
            var response = await Task.Run(() => _search.Search(_workspaceRoot, query, cancellationToken: cancellation.Token));
            if (version != _searchVersion || cancellation.IsCancellationRequested) return;
            ResultsList.ItemsSource = response.Results;
            MessageText.Text = response.Results.Count == 0
                ? "Aucun résultat. Essayez un autre terme."
                : response.HasMore
                    ? $"{response.Results.Count} premiers résultats affichés. Affinez votre recherche pour en voir d’autres."
                    : $"{response.Results.Count} résultat(s). Sélectionnez une ligne pour ouvrir l’élément.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (version == _searchVersion) MessageText.Text = UiErrorMessages.For(exception);
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not GlobalSearchResult result) return;
        ResultsList.SelectedItem = null;
        ResultSelected?.Invoke(result);
    }
}
