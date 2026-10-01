using MS3DPRINT.Manager.Core.Collections;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class CollectionViewModel : ObservableObject
{
    private readonly CollectionCatalog _catalog;
    private readonly string _workspaceRoot;
    private readonly string _collectionFolder;
    private IReadOnlyList<CollectionItemSummary> _allItems = [];
    private IReadOnlyList<CollectionItemSummary> _visibleItems = [];
    private string _searchText = string.Empty;

    public CollectionViewModel(CollectionCatalog catalog, string workspaceRoot, string collectionFolder, string title, string subtitle)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot)));
        _collectionFolder = collectionFolder ?? throw new ArgumentNullException(nameof(collectionFolder));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Subtitle = subtitle ?? throw new ArgumentNullException(nameof(subtitle));
    }

    public string Title { get; }
    public string Subtitle { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    public IReadOnlyList<CollectionItemSummary> VisibleItems
    {
        get => _visibleItems;
        private set => SetProperty(ref _visibleItems, value);
    }

    public IReadOnlyList<CollectionItemSummary> LoadCatalog() => _catalog.Load(_workspaceRoot, _collectionFolder);

    public void ApplyCatalog(IReadOnlyList<CollectionItemSummary> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _allItems = items;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleItems = _allItems
            .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
