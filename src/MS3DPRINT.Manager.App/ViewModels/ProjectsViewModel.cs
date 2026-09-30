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

    public IReadOnlyList<ProjectSummary> VisibleProjects
    {
        get => _visibleProjects;
        private set => SetProperty(ref _visibleProjects, value);
    }

    public void Refresh()
    {
        _allProjects = _catalog.Load(_workspaceRoot);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisibleProjects = _allProjects
            .Where(project => SelectedStatus is null || project.Status == SelectedStatus)
            .Where(project => query.Length == 0 || project.Reference.Contains(query, StringComparison.OrdinalIgnoreCase) || project.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase) || project.ClientFolderName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
