using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Printers;

public sealed record PrinterProfile(Guid Id, string Brand, string Model, string Name, decimal HourlyRate, string BuildVolume, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed class PrinterStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PrinterStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "printers");
    }

    public IReadOnlyList<PrinterProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(Read)
            .OrderByDescending(printer => printer.IsActive).ThenBy(printer => printer.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void Create(PrinterProfile profile)
    {
        Validate(profile);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, profile.Id + ".json");
        WorkspacePathSafety.EnsureNoLinks(path);
        if (File.Exists(path)) throw new InvalidOperationException("Une imprimante porte déjà cet identifiant.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
    }

    private static PrinterProfile Read(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<PrinterProfile>(stream) ?? throw new JsonException("La fiche imprimante est vide.");
        Validate(profile);
        return profile;
    }

    private static void Validate(PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty) throw new ArgumentException("L’identifiant de l’imprimante est requis.");
        if (string.IsNullOrWhiteSpace(profile.Brand) || string.IsNullOrWhiteSpace(profile.Model) || string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("La marque, le modèle et le nom sont requis.");
        if (profile.HourlyRate < 0) throw new ArgumentException("Le coût horaire doit être positif.");
    }
}
