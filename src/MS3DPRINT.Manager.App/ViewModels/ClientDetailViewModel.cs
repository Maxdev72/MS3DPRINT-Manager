using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ClientDetailViewModel : ObservableObject
{
    private readonly ClientProfileStore _store;
    private readonly ClientProfile _profile;
    private readonly ProjectCatalog? _projectCatalog;
    private readonly string? _workspaceRoot;
    private string? _notes;
    private IReadOnlyList<ProjectSummary> _projects = [];

    public ClientDetailViewModel(ClientProfile profile, ClientProfileStore store)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _notes = profile.Notes;
    }

    public ClientDetailViewModel(ClientProfile profile, ClientProfileStore store, ProjectCatalog projectCatalog, string workspaceRoot)
        : this(profile, store)
    {
        _projectCatalog = projectCatalog ?? throw new ArgumentNullException(nameof(projectCatalog));
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot)));
    }

    public string DisplayName => _profile.Kind == ClientKind.Professional
        ? _profile.CompanyName!
        : string.Join(" ", new[] { _profile.FirstName, _profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string ClientCode => _profile.ClientCode;
    public string? Address => _profile.Address;
    public PrimaryContact PrimaryContact => _profile.PrimaryContact;

    public IReadOnlyList<ProjectSummary> Projects
    {
        get => _projects;
        private set => SetProperty(ref _projects, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public void Save()
    {
        _store.Update(_profile with { Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(), UpdatedAt = DateTimeOffset.UtcNow });
    }

    public IReadOnlyList<ProjectSummary> LoadProjects()
    {
        if (_projectCatalog is null || _workspaceRoot is null) return [];

        return _projectCatalog.Load(_workspaceRoot)
            .Where(project => string.Equals(project.ClientFolderName, _profile.FolderName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(project => project.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ApplyProjects(IReadOnlyList<ProjectSummary> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        Projects = projects;
    }
}
