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
        if (!Directory.Exists(_paths.ClientsDirectory)) return [];

        return Directory.EnumerateFiles(_paths.ClientsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(ReadProfile)
            .OrderBy(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Create(ClientProfile profile)
    {
        Validate(profile);
        _paths.EnsureMetadataDirectories();
        if (File.Exists(ProfilePath(profile.Id)) || LoadAll().Any(existing => string.Equals(existing.FolderName, profile.FolderName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Une fiche client existe déjà pour ce dossier.");

        WriteNew(ProfilePath(profile.Id), profile);
    }

    public void Update(ClientProfile profile)
    {
        Validate(profile);
        var activePath = ProfilePath(profile.Id);
        if (!File.Exists(activePath)) throw new InvalidOperationException("La fiche client à modifier est introuvable.");

        _paths.EnsureMetadataDirectories();
        var temporaryPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteNew(temporaryPath, profile);
            File.Copy(activePath, Path.Combine(_paths.HistoryDirectory, profile.Id + "-" + DateTimeOffset.UtcNow.Ticks + ".json"), overwrite: false);
            File.Replace(temporaryPath, activePath, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private ClientProfile ReadProfile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<ClientProfile>(stream) ?? throw new JsonException("La fiche client est vide.");
    }

    private void WriteNew(string path, ClientProfile profile)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
        stream.Flush(flushToDisk: true);
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
