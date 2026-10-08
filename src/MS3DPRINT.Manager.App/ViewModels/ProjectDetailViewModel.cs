using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ProjectDetailViewModel : ObservableObject
{
    private readonly ProjectProfileStore _store;
    private ProjectProfile _profile;
    private readonly ProjectFileBrowser _files;
    private readonly string _projectPath;
    private ProjectStatus _status;
    private DateTime? _dueDate;
    private string? _description;
    private string? _notes;
    private ProjectDraft _savedDraft;

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
        _savedDraft = CaptureDraft();
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

    public bool HasUnsavedChanges => CaptureDraft() != _savedDraft;

    public void Save()
    {
        var updated = _profile with
        {
            Status = Status,
            DueDate = DueDate is { } date ? DateOnly.FromDateTime(date) : null,
            Description = TrimOrNull(Description),
            Notes = TrimOrNull(Notes),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _profile = _store.Update(updated, _profile.UpdatedAt);
        _savedDraft = CaptureDraft();
    }

    private ProjectDraft CaptureDraft() => new(Status, DueDate, Description, Notes);

    private sealed record ProjectDraft(ProjectStatus Status, DateTime? DueDate, string? Description, string? Notes);

    public ProjectFileListing ReadFiles()
    {
        var current = string.IsNullOrWhiteSpace(CurrentDirectory) ? ProjectPath : CurrentDirectory;
        if (string.IsNullOrWhiteSpace(current)) return new ProjectFileListing(string.Empty, []);

        try
        {
            return new ProjectFileListing(current, _files.List(ProjectPath, current));
        }
        catch (DirectoryNotFoundException) when (!string.Equals(ProjectPath, current, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && Directory.Exists(ProjectPath))
        {
            return new ProjectFileListing(ProjectPath, _files.List(ProjectPath, ProjectPath));
        }
    }

    public ProjectFileListing ReadFilesForDirectory(ProjectFileEntry entry)
    {
        if (!entry.IsDirectory) throw new ArgumentException("L’entrée sélectionnée n’est pas un dossier.", nameof(entry));
        return new ProjectFileListing(entry.FullPath, _files.List(ProjectPath, entry.FullPath));
    }

    public ProjectFileListing ReadParentFiles()
    {
        if (!CanGoUp) return new ProjectFileListing(CurrentDirectory, FileEntries);
        var parent = _files.GetParentDirectory(ProjectPath, CurrentDirectory);
        return new ProjectFileListing(parent, _files.List(ProjectPath, parent));
    }

    public void ApplyFileListing(ProjectFileListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        CurrentDirectory = listing.DirectoryPath;
        FileEntries = listing.Entries;
        OnPropertyChanged(nameof(CurrentDirectory));
        OnPropertyChanged(nameof(FileEntries));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public void LoadFiles() => ApplyFileListing(ReadFiles());

    public void OpenDirectory(ProjectFileEntry entry)
    {
        if (!entry.IsDirectory) throw new ArgumentException("L’entrée sélectionnée n’est pas un dossier.", nameof(entry));
        ApplyFileListing(ReadFilesForDirectory(entry));
    }

    public void GoUp()
    {
        if (!CanGoUp) return;
        ApplyFileListing(ReadParentFiles());
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ProjectFileListing(string DirectoryPath, IReadOnlyList<ProjectFileEntry> Entries);
