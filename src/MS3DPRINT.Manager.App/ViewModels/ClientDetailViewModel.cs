using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ClientDetailViewModel : ObservableObject
{
    private readonly ClientProfileStore _store;
    private ClientProfile _profile;
    private readonly ProjectCatalog? _projectCatalog;
    private readonly string? _workspaceRoot;
    private string? _notes;
    private IReadOnlyList<ProjectSummary> _projects = [];

    public ClientDetailViewModel(ClientProfile profile, ClientProfileStore store)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        CompanyName = profile.CompanyName ?? string.Empty;
        Siret = profile.Siret ?? string.Empty;
        FirstName = profile.FirstName ?? string.Empty;
        LastName = profile.LastName ?? string.Empty;
        Address = profile.Address ?? string.Empty;
        ContactFirstName = profile.PrimaryContact.FirstName ?? string.Empty;
        ContactLastName = profile.PrimaryContact.LastName ?? string.Empty;
        ContactRole = profile.PrimaryContact.Role ?? string.Empty;
        ContactPhone = profile.PrimaryContact.Phone ?? string.Empty;
        ContactEmail = profile.PrimaryContact.Email ?? string.Empty;
        _notes = profile.Notes;
    }

    public ClientDetailViewModel(ClientProfile profile, ClientProfileStore store, ProjectCatalog projectCatalog, string workspaceRoot)
        : this(profile, store)
    {
        _projectCatalog = projectCatalog ?? throw new ArgumentNullException(nameof(projectCatalog));
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot)));
    }

    public string DisplayName => _profile.Kind == ClientKind.Professional
        ? CompanyName
        : string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string ClientCode => _profile.ClientCode;
    public bool IsProfessional => _profile.Kind == ClientKind.Professional;
    public bool IsIndividual => _profile.Kind == ClientKind.Individual;
    public string CompanyName { get; set; }
    public string Siret { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Address { get; set; }
    public string ContactFirstName { get; set; }
    public string ContactLastName { get; set; }
    public string ContactRole { get; set; }
    public string ContactPhone { get; set; }
    public string ContactEmail { get; set; }

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
        var updated = _profile with
        {
            CompanyName = IsProfessional ? NullIfEmpty(CompanyName) : null,
            Siret = IsProfessional ? NullIfEmpty(Siret) : null,
            FirstName = IsIndividual ? NullIfEmpty(FirstName) : null,
            LastName = IsIndividual ? NullIfEmpty(LastName) : null,
            Address = NullIfEmpty(Address),
            Notes = NullIfEmpty(Notes),
            PrimaryContact = new PrimaryContact(IsProfessional ? NullIfEmpty(ContactFirstName) : null, IsProfessional ? NullIfEmpty(ContactLastName) : null, IsProfessional ? NullIfEmpty(ContactRole) : null, NullIfEmpty(ContactPhone), NullIfEmpty(ContactEmail)),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _store.Update(updated);
        _profile = updated;
        OnPropertyChanged(nameof(DisplayName));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
