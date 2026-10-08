using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly WorkspaceMetadataPaths _paths;

    public ProjectProfileStore(WorkspaceMetadataPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public IReadOnlyList<ProjectProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ProjectsDirectory);
        if (!Directory.Exists(_paths.ProjectsDirectory)) return [];
        return Directory.EnumerateFiles(_paths.ProjectsDirectory, "*.json")
            .Select(Read)
            .OrderBy(profile => profile.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ProjectProfile> LoadReadable()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ProjectsDirectory);
        if (!Directory.Exists(_paths.ProjectsDirectory)) return [];
        return Directory.EnumerateFiles(_paths.ProjectsDirectory, "*.json")
            .Select(TryRead)
            .Where(profile => profile is not null)
            .Cast<ProjectProfile>()
            .OrderBy(profile => profile.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Create(ProjectProfile profile)
    {
        Validate(profile);
        using var mutation = ProfileMutationLock.Acquire(Path(profile.Id));
        EnsureSafeDirectories();
        if (File.Exists(Path(profile.Id)) || LoadAll().Any(existing =>
                string.Equals(existing.FolderName, profile.FolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing.Reference, profile.Reference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Une fiche projet existe déjà pour ce dossier.");
        }

        new ProjectReferenceReservations(_paths.RootPath).Reserve(profile.Reference);
        var active = Path(profile.Id);
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { Write(temporary, profile); File.Move(temporary, active, overwrite: false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Update(ProjectProfile profile) => UpdateCore(profile, null);

    public ProjectProfile Update(ProjectProfile profile, DateTimeOffset expectedUpdatedAt) => UpdateCore(profile, expectedUpdatedAt);

    private ProjectProfile UpdateCore(ProjectProfile profile, DateTimeOffset? expectedUpdatedAt)
    {
        Validate(profile);
        var active = Path(profile.Id);
        using var mutation = ProfileMutationLock.Acquire(active);
        WorkspacePathSafety.EnsureNoLinks(active);
        if (!File.Exists(active)) throw new InvalidOperationException("La fiche projet est introuvable.");
        var previous = Read(active);
        ProfileMutationLock.CheckVersion(previous.UpdatedAt, expectedUpdatedAt);
        profile = profile with { UpdatedAt = ProfileMutationLock.NextVersion(previous.UpdatedAt, profile.UpdatedAt) };
        EnsureSafeDirectories();
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Write(temporary, profile);
            WorkspacePathSafety.EnsureNoLinks(active);
            File.Copy(active, System.IO.Path.Combine(_paths.HistoryDirectory, profile.Id + "-" + DateTimeOffset.UtcNow.Ticks + ".json"), false);
            WorkspacePathSafety.EnsureNoLinks(active);
            File.Replace(temporary, active, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return profile;
    }

    private ProjectProfile Read(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<ProjectProfile>(stream) ?? throw new JsonException("La fiche projet est vide.");
    }

    private ProjectProfile? TryRead(string path)
    {
        try { return Read(path); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Write(string path, ProjectProfile profile)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
        stream.Flush(true);
    }

    private void EnsureSafeDirectories()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ClientsDirectory);
        WorkspacePathSafety.EnsureNoLinks(_paths.ProjectsDirectory);
        WorkspacePathSafety.EnsureNoLinks(_paths.HistoryDirectory);
        _paths.EnsureMetadataDirectories();
    }

    private string Path(Guid id) => System.IO.Path.Combine(_paths.ProjectsDirectory, id + ".json");

    private static void Validate(ProjectProfile profile)
    {
        if (profile.Id == Guid.Empty || profile.ClientId == Guid.Empty) throw new ArgumentException("Les identifiants du projet et du client sont requis.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Reference) || string.IsNullOrWhiteSpace(profile.FolderName) || string.IsNullOrWhiteSpace(profile.ProjectName)) throw new ArgumentException("Les informations projet sont incomplètes.", nameof(profile));
    }
}
