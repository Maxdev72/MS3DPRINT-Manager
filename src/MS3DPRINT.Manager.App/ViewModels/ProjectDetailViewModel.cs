using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ProjectDetailViewModel : ObservableObject
{
    private readonly ProjectProfileStore _store;
    private readonly ProjectProfile _profile;
    private ProjectStatus _status;
    private DateTime? _dueDate;
    private string? _description;
    private string? _notes;

    public ProjectDetailViewModel(ProjectProfile profile, ProjectProfileStore store)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _status = profile.Status;
        _dueDate = profile.DueDate?.ToDateTime(TimeOnly.MinValue);
        _description = profile.Description;
        _notes = profile.Notes;
    }

    public string Reference => _profile.Reference;
    public string ProjectName => _profile.ProjectName;
    public string ClientCode => _profile.ClientCode;

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

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
