using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Estimates;

public sealed record EstimateInputs(decimal MaterialCost = 0, decimal MachineCost = 0, decimal ElectricityCost = 0,
    decimal DesignCost = 0, decimal LaborCost = 0, decimal PostProcessingCost = 0, decimal ConsumablesCost = 0,
    decimal PackagingShippingCost = 0, decimal MiscCost = 0, decimal MarginPercent = 0, decimal FailureRatePercent = 0, decimal DiscountPercent = 0,
    int Quantity = 1, decimal MinimumPerPiece = 0, bool PrintDataForWholeOrder = false,
    int EngineVersion = 1, EstimateV2Options? V2 = null);

public sealed record EstimateResult(decimal RawCost, decimal ExpectedCost, decimal SalePrice, decimal FinalPrice, decimal FlatFee = 0, EstimateProfitability? V2 = null);

public static class EstimateCalculator
{
    public static EstimateResult Calculate(EstimateInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.EngineVersion == 2) return EstimateCalculatorV2.Calculate(input);
        if (input.EngineVersion != 1) throw new ArgumentException("Version du moteur d’estimation non prise en charge.", nameof(input));
        if (new[] { input.MaterialCost, input.MachineCost, input.ElectricityCost, input.DesignCost, input.LaborCost, input.PostProcessingCost,
                input.ConsumablesCost, input.PackagingShippingCost, input.MiscCost }.Any(cost => cost < 0))
            throw new ArgumentOutOfRangeException(nameof(input), "Un coût ne peut pas être négatif.");
        if (input.FailureRatePercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(input.FailureRatePercent));
        if (input.MarginPercent < 0 || input.DiscountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException();
        if (input.Quantity < 1 || input.MinimumPerPiece < 0) throw new ArgumentOutOfRangeException(nameof(input), "La quantité doit être d’au moins 1 et le forfait minimum positif.");
        var raw = input.MaterialCost + input.MachineCost + input.ElectricityCost + input.DesignCost + input.LaborCost + input.PostProcessingCost + input.ConsumablesCost + input.PackagingShippingCost + input.MiscCost;
        var expected = Math.Round(raw / (1 - input.FailureRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var sale = Math.Round(expected * (1 + input.MarginPercent / 100m), 2, MidpointRounding.AwayFromZero);
        // flat fee: added per piece after the margin (it carries neither failure rate nor margin), before the discount
        var flatFee = Math.Round(input.MinimumPerPiece * input.Quantity, 2, MidpointRounding.AwayFromZero);
        var beforeDiscount = sale + flatFee;
        return new(raw, expected, beforeDiscount, Math.Round(beforeDiscount * (1 - input.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero), flatFee);
    }
}


public sealed record EstimateProfile(Guid Id, string Name, string FilamentName, string PrinterName, EstimateInputs Inputs, EstimateResult Result, DateTimeOffset CreatedAt, decimal WeightGrams = 0, decimal MachineHours = 0, decimal MachineHourlyRate = 0, string? ProjectReference = null, string? ClientName = null, long Revision = 0);

/// <summary>What an estimate says about its scope: a modelling-only estimate carries no filament, printer, weight or machine time.</summary>
public static class EstimateScopeRules
{
    public const string ModellingOnlyLabel = "Modélisation seule";
    public const string NoPrinterLabel = "Sans impression";
    public const string FleetLabel = "Parc d’imprimantes";

    public static bool IsModellingOnly(EstimateProfile estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return string.Equals(estimate.FilamentName, ModellingOnlyLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Filament, printer, weight, hours and hourly rate to store. The scope decides, never the leftover selection of a hidden form;
    /// a printing estimate whose filament or printer is no longer in the lists keeps the names it already had.
    /// </summary>
    public static (string Filament, string Printer, decimal Grams, decimal Hours, decimal Rate) ForSave(
        bool modellingOnly, string? filament, string? printer, decimal grams, decimal hours, decimal rate, EstimateProfile? previous)
    {
        if (modellingOnly) return (ModellingOnlyLabel, NoPrinterLabel, 0m, 0m, 0m);
        var keep = previous is not null && !IsModellingOnly(previous) ? previous : null;
        var filamentName = filament ?? keep?.FilamentName ?? ModellingOnlyLabel;
        var printerName = printer ?? (keep is not null ? keep.PrinterName : NoPrinterLabel);
        return (filamentName, printerName, grams, hours, rate);
    }
}

public sealed record EstimateReadResult(IReadOnlyList<EstimateProfile> Estimates, IReadOnlyList<string> UnreadableFiles);

public sealed class EstimateStore
{
    private readonly string _root;
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EstimateStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.GetFullPath(workspaceRoot);
        _directory = Path.Combine(_root, ".ms3dprint-manager", "estimates");
    }

    public void Create(EstimateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Le nom de l’estimation est requis.");
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, profile.Id + ".json");
        WorkspacePathSafety.EnsureNoLinks(path);
        using var mutation = ProfileMutationLock.Acquire(path);
        WriteAtomic(path, profile, overwrite: false);
    }

    public TrashEntry Trash(Guid id)
    {
        var path = Path.Combine(_directory, id + ".json");
        WorkspacePathSafety.EnsureNoLinks(path);
        using var mutation = ProfileMutationLock.Acquire(path);
        if (!File.Exists(path)) throw new InvalidOperationException("L’estimation est introuvable.");
        var estimate = Read(path);
        return new ManagedFileService(_root).Trash(path, label: "Estimation : " + estimate.Name);
    }

    public EstimateProfile Update(EstimateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Le nom de l’estimation est requis.");
        var path = Path.Combine(_directory, profile.Id + ".json");
        WorkspacePathSafety.EnsureNoLinks(path);
        using var mutation = ProfileMutationLock.Acquire(path);
        if (!File.Exists(path)) throw new InvalidOperationException("L’estimation est introuvable.");
        var current = Read(path);
        if (current.Revision != profile.Revision) throw new ProfileConflictException();
        var updated = profile with { Revision = checked(current.Revision + 1) };
        WriteAtomic(path, updated, overwrite: true);
        return updated;
    }

    private static void WriteAtomic(string path, EstimateProfile profile, bool overwrite)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Every estimate; fails on the first unreadable file so that callers which rewrite estimates never skip one silently.</summary>
    public IReadOnlyList<EstimateProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(Read).OrderByDescending(estimate => estimate.CreatedAt).ToArray();
    }

    /// <summary>The readable estimates; a corrupt file is left out instead of hiding all the others.</summary>
    public IReadOnlyList<EstimateProfile> LoadReadable() => LoadWithDiagnostics().Estimates;

    public EstimateReadResult LoadWithDiagnostics()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return new([], []);
        var readable = new List<EstimateProfile>();
        var unreadable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { readable.Add(Read(path)); }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            { unreadable.Add(Path.GetFileName(path)); }
        }
        return new(readable.OrderByDescending(estimate => estimate.CreatedAt).ToArray(), unreadable);
    }

    private static EstimateProfile Read(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        using var stream = File.OpenRead(path);
        var estimate = JsonSerializer.Deserialize<EstimateProfile>(stream) ?? throw new JsonException("Une estimation est vide.");
        if (estimate.Id == Guid.Empty || !Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var fileId) || fileId != estimate.Id
            || string.IsNullOrWhiteSpace(estimate.Name) || estimate.Inputs is null || estimate.Result is null || estimate.Revision < 0
            || estimate.Inputs.EngineVersion is not (1 or 2)
            || estimate.Inputs.V2?.Production?.Allocations?.Any(item => item is null) == true
            || estimate.Inputs.V2?.ExtraCosts?.Any(item => item is null) == true)
            throw new JsonException("La fiche d’estimation est incomplète ou son identifiant ne correspond pas au fichier.");
        return estimate; // Stored historic results are never recomputed when reading.
    }
}
