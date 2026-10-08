using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Filaments;

public sealed class FilamentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly string _directory;

    public FilamentStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _directory = Path.Combine(_root, ".ms3dprint-manager", "filaments");
    }

    public IReadOnlyList<FilamentProfile> LoadAll()
    {
        CheckLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(Read)
            .OrderBy(p => p.Brand, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void Create(FilamentProfile profile)
    {
        Validate(profile);
        using var mutation = ProfileMutationLock.Acquire(ProfilePath(profile.Id));
        WriteAtomic(profile, replace: false);
    }

    public void Update(FilamentProfile profile) => UpdateCore(profile, null);

    public FilamentProfile Update(FilamentProfile profile, DateTimeOffset expectedUpdatedAt) => UpdateCore(profile, expectedUpdatedAt);

    private FilamentProfile UpdateCore(FilamentProfile profile, DateTimeOffset? expectedUpdatedAt)
    {
        Validate(profile);
        using var mutation = ProfileMutationLock.Acquire(ProfilePath(profile.Id));
        var existing = ReadRequired(profile.Id);
        ProfileMutationLock.CheckVersion(existing.UpdatedAt, expectedUpdatedAt);
        profile = profile with { CreatedAt = existing.CreatedAt, UpdatedAt = ProfileMutationLock.NextVersion(existing.UpdatedAt, DateTimeOffset.UtcNow) };
        WriteAtomic(profile, replace: true);
        return profile;
    }

    public FilamentProfile Duplicate(Guid id)
    {
        var existing = ReadRequired(id);
        var now = DateTimeOffset.UtcNow;
        var duplicate = existing with { Id = Guid.NewGuid(), Name = existing.Name + " (copie)", CreatedAt = now, UpdatedAt = now };
        Create(duplicate);
        return duplicate;
    }

    public TrashEntry Trash(Guid id)
    {
        var profile = ReadRequired(id);
        return new ManagedFileService(_root).Trash(ProfilePath(id), label: "Filament : " + profile.Brand + " " + profile.Name);
    }

    private FilamentProfile ReadRequired(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("L’identifiant du filament est requis.", nameof(id));
        var path = ProfilePath(id);
        CheckLinks(path);
        if (!File.Exists(path)) throw new InvalidOperationException("La fiche filament est introuvable.");
        return Read(path);
    }

    private FilamentProfile Read(string path)
    {
        CheckLinks(path);
        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<FilamentProfile>(stream) ?? throw new JsonException("La fiche filament est vide.");
        Validate(profile);
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), profile.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new JsonException("L’identifiant de la fiche filament ne correspond pas à son fichier.");
        return profile;
    }

    private void WriteAtomic(FilamentProfile profile, bool replace)
    {
        var destination = ProfilePath(profile.Id);
        CheckLinks(destination);
        if (!replace && File.Exists(destination)) throw new InvalidOperationException("Une fiche filament porte déjà cet identifiant.");
        Directory.CreateDirectory(_directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptions);
                stream.Flush(true);
            }
            CheckLinks(destination);
            if (replace) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string ProfilePath(Guid id) => Path.Combine(_directory, id + ".json");

    private static void Validate(FilamentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty) throw new ArgumentException("L’identifiant du filament est requis.");
        if (string.IsNullOrWhiteSpace(profile.Brand)) throw new ArgumentException("La marque est requise.");
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Le nom est requis.");
        if (string.IsNullOrWhiteSpace(profile.Material)) throw new ArgumentException("La matière / le type est requis.");
        if (profile.PricePerKg < 0) throw new ArgumentException("Le prix doit être supérieur ou égal à zéro.");
    }

    private static void CheckLinks(string path) => WorkspacePathSafety.EnsureNoLinks(path);
}
