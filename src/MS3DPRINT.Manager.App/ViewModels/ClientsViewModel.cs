using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ClientsViewModel : ObservableObject
{
    private readonly ClientCatalog _catalog;
    private readonly string _workspaceRoot;
    private IReadOnlyList<ClientSummary> _allClients = [];
    private IReadOnlyList<ClientSummary> _visibleClients = [];
    private string _searchText = string.Empty;
    private ClientKind? _selectedKind;

    public ClientsViewModel(ClientCatalog catalog, string workspaceRoot)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            ApplyFilter();
        }
    }

    public ClientKind? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (!SetProperty(ref _selectedKind, value)) return;
            ApplyFilter();
        }
    }

    public IReadOnlyList<ClientSummary> VisibleClients
    {
        get => _visibleClients;
        private set => SetProperty(ref _visibleClients, value);
    }

    public void Refresh()
    {
        _allClients = _catalog.Load(_workspaceRoot);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleClients = _allClients
            .Where(client => SelectedKind is null || client.Kind == SelectedKind)
            .Where(client => query.Length == 0 || Matches(client, query))
            .ToArray();
    }

    private static bool Matches(ClientSummary client, string query)
    {
        var profile = client.Profile;
        return new[]
            {
                client.DisplayName, client.ClientCode, profile?.CompanyName, profile?.FirstName, profile?.LastName,
                profile?.PrimaryContact.FirstName, profile?.PrimaryContact.LastName, profile?.PrimaryContact.Phone, profile?.PrimaryContact.Email
            }
            .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }
}
