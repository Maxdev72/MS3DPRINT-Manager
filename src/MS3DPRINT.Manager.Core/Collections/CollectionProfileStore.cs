using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Collections;

public sealed class CollectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly string _directory;

    public CollectionProfileStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _directory = Path.Combine(_root, ".ms3dprint-manager", "collections");
    }

    public IReadOnlyList<CollectionProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        return !Directory.Exists(_directory) ? [] : Directory.EnumerateFiles(_directory, "*.json")
            .Select(Read).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public CollectionProfile? Load(Guid id)
    {
        var path = ProfilePath(id);
        return File.Exists(path) ? Read(path) : null;
    }

    public CollectionProfile? FindByPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_root, path));
        return LoadAll().FirstOrDefault(p => string.Equals(Path.GetFullPath(Path.Combine(_root, p.RelativePath)), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public string ProfilePath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("L’identifiant de la fiche est requis.", nameof(id));
        return Path.Combine(_directory, id + ".json");
    }

    public CollectionProfile Create(CollectionProfile profile)
    {
        Validate(profile, requireFolder: true);
        using var mutation = ProfileMutationLock.Acquire(ProfilePath(profile.Id));
        if (File.Exists(ProfilePath(profile.Id)) || FindByPath(profile.RelativePath) is not null)
            throw new InvalidOperationException("Une fiche existe déjà pour ce dossier ou cet identifiant.");
        Write(profile, replace: false);
        return profile;
    }

    public CollectionProfile Update(CollectionProfile profile) => UpdateCore(profile, null);

    public CollectionProfile Update(CollectionProfile profile, DateTimeOffset expectedUpdatedAt) => UpdateCore(profile, expectedUpdatedAt);

    private CollectionProfile UpdateCore(CollectionProfile profile, DateTimeOffset? expectedUpdatedAt)
    {
        Validate(profile, requireFolder: true);
        using var mutation = ProfileMutationLock.Acquire(ProfilePath(profile.Id));
        var previous = Load(profile.Id) ?? throw new InvalidOperationException("La fiche à modifier est introuvable.");
        ProfileMutationLock.CheckVersion(previous.UpdatedAt, expectedUpdatedAt);
        if (previous.Category != profile.Category || previous.CreatedAt != profile.CreatedAt)
            throw new ArgumentException("La catégorie et la date de création de la fiche ne peuvent pas changer.", nameof(profile));
        if (LoadAll().Any(p => p.Id != profile.Id && string.Equals(Path.GetFullPath(Path.Combine(_root, p.RelativePath)), Path.GetFullPath(Path.Combine(_root, profile.RelativePath)), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Une autre fiche existe déjà pour ce dossier.");
        profile = profile with { UpdatedAt = ProfileMutationLock.NextVersion(previous.UpdatedAt, profile.UpdatedAt) };
        Write(profile, replace: true);
        return profile;
    }

    private CollectionProfile Read(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var profile = JsonSerializer.Deserialize<CollectionProfile>(stream) ?? throw new JsonException("La fiche est vide.");
        Validate(profile, requireFolder: false);
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), profile.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new JsonException("L’identifiant de la fiche ne correspond pas au fichier.");
        return profile;
    }

    private void Write(CollectionProfile profile, bool replace)
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        Directory.CreateDirectory(_directory);
        var active = ProfilePath(profile.Id);
        WorkspacePathSafety.EnsureNoLinks(active);
        var temporary = active + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (replace) File.Replace(temporary, active, destinationBackupFileName: null);
            else File.Move(temporary, active, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Validate(CollectionProfile profile, bool requireFolder)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty) throw new ArgumentException("L’identifiant de la fiche est requis.", nameof(profile));
        ValidateCategory(profile.Category);
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Any(char.IsControl))
            throw new ArgumentException("Le nom est requis et ne doit pas contenir de caractères de contrôle.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.RelativePath) || Path.IsPathRooted(profile.RelativePath))
            throw new ArgumentException("Le chemin de la fiche doit être relatif.", nameof(profile));
        var segments = profile.RelativePath.Replace('\\', '/').Split('/');
        if (segments.Length < 2 || segments[0] != profile.Category || segments.Any(s => s is "" or "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReserved(s)))
            throw new ArgumentException("Le chemin doit désigner un dossier de sa catégorie.", nameof(profile));
        var fullPath = Path.GetFullPath(Path.Combine(_root, profile.RelativePath));
        WorkspacePathSafety.EnsureNoLinks(fullPath);
        if (requireFolder && !Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Le dossier de la fiche est introuvable.");
    }

    internal static bool IsReserved(string name) => name.Equals(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".MS3DPRINT-STAGING-", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateCategory(string category)
    {
        if (category is not ("02_MODELES_3D" or "03_PRODUITS_MS3DPRINT" or "06_FOURNISSEURS"))
            throw new ArgumentException("La catégorie de la fiche est invalide.", nameof(category));
    }

}
