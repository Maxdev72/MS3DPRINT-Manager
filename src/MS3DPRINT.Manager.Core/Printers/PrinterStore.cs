using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Printers;

public sealed record PrinterProfile(Guid Id, string Brand, string Model, string Name, decimal HourlyRate, string BuildVolume, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, decimal PurchasePrice = 0, int LifetimeHours = 0, int? PowerWatts = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Model) ? Name : $"{Brand} {Model}";
}

public static class PrinterCostCalculator
{
    public static decimal HourlyRate(PrinterProfile printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        return printer.PurchasePrice > 0 && printer.LifetimeHours > 0
            ? Math.Round(printer.PurchasePrice / printer.LifetimeHours, 2, MidpointRounding.AwayFromZero)
            : printer.HourlyRate;
    }

    public static decimal MachineCost(PrinterProfile printer, decimal durationHours)
    {
        if (durationHours < 0) throw new ArgumentOutOfRangeException(nameof(durationHours));
        return Math.Round(HourlyRate(printer) * durationHours, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ElectricityCost(PrinterProfile printer, decimal durationHours, decimal pricePerKwh)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (durationHours < 0) throw new ArgumentOutOfRangeException(nameof(durationHours));
        if (pricePerKwh < 0) throw new ArgumentOutOfRangeException(nameof(pricePerKwh));
        return printer.PowerWatts is > 0
            ? Math.Round(durationHours * printer.PowerWatts.Value / 1000m * pricePerKwh, 2, MidpointRounding.AwayFromZero)
            : 0m;
    }
}

public sealed class PrinterStore
{
    private readonly string _root;
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PrinterStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _directory = Path.Combine(_root, ".ms3dprint-manager", "printers");
    }

    public IReadOnlyList<PrinterProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(Read)
            .OrderByDescending(printer => printer.IsActive).ThenBy(printer => printer.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>The readable printers; a corrupt file is left out instead of hiding all the others.</summary>
    public IReadOnlyList<PrinterProfile> LoadReadable()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        var readable = new List<PrinterProfile>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { readable.Add(Read(path)); }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return readable.OrderByDescending(printer => printer.IsActive).ThenBy(printer => printer.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
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

    public TrashEntry Trash(Guid id)
    {
        var path = Path.Combine(_directory, id + ".json");
        if (!File.Exists(path)) throw new InvalidOperationException("La fiche imprimante est introuvable.");
        var profile = Read(path);
        return new ManagedFileService(_root).Trash(path, label: "Imprimante : " + profile.Name);
    }

    public PrinterProfile Update(PrinterProfile profile, DateTimeOffset expectedUpdatedAt)
    {
        Validate(profile);
        var path = Path.Combine(_directory, profile.Id + ".json");
        var current = Read(path);
        if (current.UpdatedAt != expectedUpdatedAt) throw new InvalidOperationException("La fiche imprimante a été modifiée par ailleurs.");
        profile = profile with { CreatedAt = current.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, profile, JsonOptions);
            File.Move(temporary, path, true);
            return profile;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
        if (profile.HourlyRate < 0 || profile.PurchasePrice < 0 || profile.LifetimeHours < 0 || profile.PowerWatts < 0)
            throw new ArgumentException("Les coûts et caractéristiques de l’imprimante doivent être positifs.");
    }
}
