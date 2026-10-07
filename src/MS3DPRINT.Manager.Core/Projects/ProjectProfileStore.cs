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
        if (!Directory.Exists(_paths.ProjectsDirectory)) return [];
        return Directory.EnumerateFiles(_paths.ProjectsDirectory, "*.json")
            .Select(Read)
            .OrderBy(profile => profile.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ProjectProfile> LoadReadable()
    {
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
        _paths.EnsureMetadataDirectories();
        if (File.Exists(Path(profile.Id)) || LoadAll().Any(existing =>
                string.Equals(existing.FolderName, profile.FolderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existing.Reference, profile.Reference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Une fiche projet existe déjà pour ce dossier.");
        }

        Write(Path(profile.Id), profile);
    }

    public void Update(ProjectProfile profile)
    {
        Validate(profile);
        _paths.EnsureMetadataDirectories();
        var active = Path(profile.Id);
        if (!File.Exists(active)) throw new InvalidOperationException("La fiche projet est introuvable.");
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Write(temporary, profile);
            File.Copy(active, System.IO.Path.Combine(_paths.HistoryDirectory, profile.Id + "-" + DateTimeOffset.UtcNow.Ticks + ".json"), false);
            File.Replace(temporary, active, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private ProjectProfile Read(string path)
    {
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
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
        stream.Flush(true);
    }

    private string Path(Guid id) => System.IO.Path.Combine(_paths.ProjectsDirectory, id + ".json");

    private static void Validate(ProjectProfile profile)
    {
        if (profile.Id == Guid.Empty || profile.ClientId == Guid.Empty) throw new ArgumentException("Les identifiants du projet et du client sont requis.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Reference) || string.IsNullOrWhiteSpace(profile.FolderName) || string.IsNullOrWhiteSpace(profile.ProjectName)) throw new ArgumentException("Les informations projet sont incomplètes.", nameof(profile));
    }
}
