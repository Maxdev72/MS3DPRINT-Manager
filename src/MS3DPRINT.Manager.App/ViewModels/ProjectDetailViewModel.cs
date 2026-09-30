using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ProjectDetailViewModel : ObservableObject
{
    private readonly ProjectProfileStore _store;
    private readonly ProjectProfile _profile;
    private readonly ProjectFileBrowser _files;
    private readonly string _projectPath;
    private ProjectStatus _status;
    private DateTime? _dueDate;
    private string? _description;
    private string? _notes;

    public ProjectDetailViewModel(ProjectProfile profile, ProjectProfileStore store)
        : this(profile, store, string.Empty, new ProjectFileBrowser()) { }

    public ProjectDetailViewModel(ProjectSummary project, ProjectProfileStore store, ProjectFileBrowser? files = null)
        : this(project.Profile ?? throw new ArgumentException("Le projet doit avoir une fiche.", nameof(project)), store, project.ProjectPath, files ?? new ProjectFileBrowser()) { }

    private ProjectDetailViewModel(ProjectProfile profile, ProjectProfileStore store, string projectPath, ProjectFileBrowser files)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projectPath = projectPath;
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _status = profile.Status;
        _dueDate = profile.DueDate?.ToDateTime(TimeOnly.MinValue);
        _description = profile.Description;
        _notes = profile.Notes;
    }

    public string Reference => _profile.Reference;
    public string ProjectName => _profile.ProjectName;
    public string ClientCode => _profile.ClientCode;
    public string ProjectPath => _projectPath;
    public string CurrentDirectory { get; private set; } = string.Empty;
    public IReadOnlyList<ProjectFileEntry> FileEntries { get; private set; } = [];
    public bool CanGoUp => !string.IsNullOrWhiteSpace(ProjectPath) && !string.Equals(ProjectPath, CurrentDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public ProjectStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public void Save()
    {
        _store.Update(_profile with
        {
            Status = Status,
            DueDate = DueDate is { } date ? DateOnly.FromDateTime(date) : null,
            Description = TrimOrNull(Description),
            Notes = TrimOrNull(Notes),
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    public void LoadFiles()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath)) return;
        var current = string.IsNullOrWhiteSpace(CurrentDirectory) ? ProjectPath : CurrentDirectory;
        var entries = _files.List(ProjectPath, current);
        CurrentDirectory = current;
        FileEntries = entries;
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(FileEntries));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public void OpenDirectory(ProjectFileEntry entry)
    {
        if (!entry.IsDirectory) throw new ArgumentException("L’entrée sélectionnée n’est pas un dossier.", nameof(entry));
        CurrentDirectory = entry.FullPath;
        LoadFiles();
    }

    public void GoUp()
    {
        if (!CanGoUp) return;
        CurrentDirectory = _files.GetParentDirectory(ProjectPath, CurrentDirectory);
        LoadFiles();
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
