using System.Text.Json;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Clients;

public sealed class ClientProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly WorkspaceMetadataPaths _paths;

    public ClientProfileStore(WorkspaceMetadataPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public IReadOnlyList<ClientProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ClientsDirectory);
        if (!Directory.Exists(_paths.ClientsDirectory)) return [];

        return Directory.EnumerateFiles(_paths.ClientsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(ReadProfile)
            .OrderBy(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ClientProfile> LoadReadable()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ClientsDirectory);
        if (!Directory.Exists(_paths.ClientsDirectory)) return [];

        return Directory.EnumerateFiles(_paths.ClientsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(TryReadProfile)
            .Where(profile => profile is not null)
            .Cast<ClientProfile>()
            .OrderBy(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ClientProfile Load(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("L’identifiant du client est requis.", nameof(id));
        var path = ProfilePath(id);
        if (!File.Exists(path)) throw new InvalidOperationException("La fiche client est introuvable.");
        return ReadProfile(path);
    }

    public void Create(ClientProfile profile)
    {
        Validate(profile);
        using var mutation = ProfileMutationLock.Acquire(ProfilePath(profile.Id));
        EnsureSafeDirectories();
        if (File.Exists(ProfilePath(profile.Id)) || LoadAll().Any(existing => string.Equals(existing.FolderName, profile.FolderName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Une fiche client existe déjà pour ce dossier.");

        var active = ProfilePath(profile.Id);
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteNew(temporary, profile); File.Move(temporary, active, overwrite: false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Update(ClientProfile profile) => UpdateCore(profile, null);

    public ClientProfile Update(ClientProfile profile, DateTimeOffset expectedUpdatedAt) => UpdateCore(profile, expectedUpdatedAt);

    private ClientProfile UpdateCore(ClientProfile profile, DateTimeOffset? expectedUpdatedAt)
    {
        Validate(profile);
        var activePath = ProfilePath(profile.Id);
        using var mutation = ProfileMutationLock.Acquire(activePath);
        WorkspacePathSafety.EnsureNoLinks(activePath);
        if (!File.Exists(activePath)) throw new InvalidOperationException("La fiche client à modifier est introuvable.");
        var previous = ReadProfile(activePath);
        ProfileMutationLock.CheckVersion(previous.UpdatedAt, expectedUpdatedAt);
        profile = profile with { UpdatedAt = ProfileMutationLock.NextVersion(previous.UpdatedAt, profile.UpdatedAt) };

        EnsureSafeDirectories();
        var temporaryPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteNew(temporaryPath, profile);
            WorkspacePathSafety.EnsureNoLinks(activePath);
            File.Copy(activePath, Path.Combine(_paths.HistoryDirectory, profile.Id + "-" + DateTimeOffset.UtcNow.Ticks + ".json"), overwrite: false);
            WorkspacePathSafety.EnsureNoLinks(activePath);
            File.Replace(temporaryPath, activePath, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return profile;
    }

    private ClientProfile ReadProfile(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<ClientProfile>(stream) ?? throw new JsonException("La fiche client est vide.");
    }

    private ClientProfile? TryReadProfile(string path)
    {
        try { return ReadProfile(path); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteNew(string path, ClientProfile profile)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
        stream.Flush(flushToDisk: true);
    }

    private void EnsureSafeDirectories()
    {
        WorkspacePathSafety.EnsureNoLinks(_paths.ClientsDirectory);
        WorkspacePathSafety.EnsureNoLinks(_paths.ProjectsDirectory);
        WorkspacePathSafety.EnsureNoLinks(_paths.HistoryDirectory);
        _paths.EnsureMetadataDirectories();
    }

    private string ProfilePath(Guid id) => Path.Combine(_paths.ClientsDirectory, id + ".json");

    private static void Validate(ClientProfile profile)
    {
        if (profile.Id == Guid.Empty) throw new ArgumentException("L’identifiant du client est requis.", nameof(profile));
        if (NameNormalizer.Normalize(profile.FolderName).Length == 0) throw new ArgumentException("Le nom de dossier client est invalide.", nameof(profile));
        if (NameNormalizer.Normalize(profile.ClientCode).Length == 0) throw new ArgumentException("Le code client est invalide.", nameof(profile));
        if (profile.PrimaryContact is null) throw new ArgumentException("Le contact principal est requis.", nameof(profile));
        if (profile.Kind == ClientKind.Professional && string.IsNullOrWhiteSpace(profile.CompanyName)) throw new ArgumentException("La raison sociale est requise.", nameof(profile));
        if (profile.Kind == ClientKind.Individual && string.IsNullOrWhiteSpace(profile.LastName)) throw new ArgumentException("Le nom du particulier est requis.", nameof(profile));
    }
}
