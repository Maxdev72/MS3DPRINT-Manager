using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Estimates;

public sealed record EstimateInputs(decimal MaterialCost = 0, decimal MachineCost = 0, decimal ElectricityCost = 0,
    decimal DesignCost = 0, decimal LaborCost = 0, decimal PostProcessingCost = 0, decimal ConsumablesCost = 0,
    decimal PackagingShippingCost = 0, decimal MiscCost = 0, decimal MarginPercent = 0, decimal FailureRatePercent = 0, decimal DiscountPercent = 0);

public sealed record EstimateResult(decimal RawCost, decimal ExpectedCost, decimal SalePrice, decimal FinalPrice);

public static class EstimateCalculator
{
    public static EstimateResult Calculate(EstimateInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.FailureRatePercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(input.FailureRatePercent));
        if (input.MarginPercent < 0 || input.DiscountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException();
        var raw = input.MaterialCost + input.MachineCost + input.ElectricityCost + input.DesignCost + input.LaborCost + input.PostProcessingCost + input.ConsumablesCost + input.PackagingShippingCost + input.MiscCost;
        var expected = Math.Round(raw / (1 - input.FailureRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var sale = Math.Round(expected * (1 + input.MarginPercent / 100m), 2, MidpointRounding.AwayFromZero);
        return new(raw, expected, sale, Math.Round(sale * (1 - input.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero));
    }
}


public sealed record EstimateProfile(Guid Id, string Name, string FilamentName, string PrinterName, EstimateInputs Inputs, EstimateResult Result, DateTimeOffset CreatedAt, decimal WeightGrams = 0, decimal MachineHours = 0, decimal MachineHourlyRate = 0, string? ProjectReference = null, string? ClientName = null);

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
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, profile, JsonOptions);
    }

    public TrashEntry Trash(Guid id)
    {
        var path = Path.Combine(_directory, id + ".json");
        if (!File.Exists(path)) throw new InvalidOperationException("L’estimation est introuvable.");
        var estimate = LoadAll().FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("L’estimation est introuvable.");
        return new ManagedFileService(_root).Trash(path, label: "Estimation : " + estimate.Name);
    }

    public void Update(EstimateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Le nom de l’estimation est requis.");
        var path = Path.Combine(_directory, profile.Id + ".json");
        WorkspacePathSafety.EnsureNoLinks(path);
        if (!File.Exists(path)) throw new InvalidOperationException("L’estimation est introuvable.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, profile, JsonOptions);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public IReadOnlyList<EstimateProfile> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(path =>
        {
            WorkspacePathSafety.EnsureNoLinks(path);
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<EstimateProfile>(stream) ?? throw new JsonException("Une estimation est vide.");
        }).OrderByDescending(estimate => estimate.CreatedAt).ToArray();
    }
}
