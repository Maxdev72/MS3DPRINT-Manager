using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ProjectsViewModel : ObservableObject
{
    private readonly ProjectCatalog _catalog;
    private readonly string _workspaceRoot;
    private IReadOnlyList<ProjectSummary> _allProjects = [];
    private IReadOnlyList<ProjectSummary> _visibleProjects = [];
    private string _searchText = string.Empty;
    private ProjectStatus? _selectedStatus;
    private string? _selectedClient;
    private int? _selectedYear;

    public ProjectsViewModel(ProjectCatalog catalog, string workspaceRoot)
    {
        _catalog = catalog;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public ProjectStatus? SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetProperty(ref _selectedStatus, value)) ApplyFilter(); }
    }

    public string? SelectedClient
    {
        get => _selectedClient;
        set { if (SetProperty(ref _selectedClient, value)) ApplyFilter(); }
    }

    public int? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) ApplyFilter(); }
    }

    public IReadOnlyList<string> AvailableClients { get; private set; } = [];
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    public IReadOnlyList<ProjectSummary> VisibleProjects
    {
        get => _visibleProjects;
        private set => SetProperty(ref _visibleProjects, value);
    }

    public void Refresh()
    {
        ApplyCatalog(LoadCatalog());
    }

    public IReadOnlyList<ProjectSummary> LoadCatalog() => _catalog.Load(_workspaceRoot);

    public void ApplyCatalog(IReadOnlyList<ProjectSummary> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        _allProjects = projects;
        AvailableClients = _allProjects.Select(project => project.ClientFolderName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(client => client, StringComparer.OrdinalIgnoreCase).ToArray();
        AvailableYears = _allProjects.Select(project => TryGetYear(project.Reference)).Where(year => year.HasValue).Select(year => year!.Value).Distinct().OrderByDescending(year => year).ToArray();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleProjects = _allProjects
            .Where(project => SelectedStatus is null || project.Status == SelectedStatus)
            .Where(project => string.IsNullOrWhiteSpace(SelectedClient) || string.Equals(project.ClientFolderName, SelectedClient, StringComparison.OrdinalIgnoreCase))
            .Where(project => SelectedYear is null || TryGetYear(project.Reference) == SelectedYear)
            .Where(project => query.Length == 0 || project.Reference.Contains(query, StringComparison.OrdinalIgnoreCase) || project.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) || project.ClientFolderName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static int? TryGetYear(string reference)
    {
        var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && int.TryParse(parts[1], out var year) ? year : null;
    }
}
