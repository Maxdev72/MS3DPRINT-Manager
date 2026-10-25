using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Estimates;

/// <summary>Opt-in defaults for this workspace only. Never rewrites any saved quote or supplies a universal tax regime.</summary>
public sealed class WorkshopEstimateSettingsStore
{
    private readonly string _directory;
    private readonly string _path;
    public WorkshopEstimateSettingsStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _directory = Path.Combine(Path.GetFullPath(root), ".ms3dprint-manager");
        _path = Path.Combine(_directory, "estimate-workshop-settings.json");
    }
    public WorkshopEstimateSettings? Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return null;
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var preferences = JsonSerializer.Deserialize<WorkshopEstimateSettings>(stream) ?? throw new JsonException("Les préférences de cotisations sont vides.");
        Validate(preferences);
        return preferences;
    }
    public void Save(WorkshopEstimateSettings preferences)
    {
        Validate(preferences);
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(_directory);
        using var mutation = ProfileMutationLock.Acquire(_path);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate(WorkshopEstimateSettings preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(preferences.Charges);
        EstimateCalculatorV2.ChargeRatePercent(preferences.Charges);
        if (preferences.ElectricityPricePerKwh < 0) throw new ArgumentOutOfRangeException(nameof(preferences.ElectricityPricePerKwh));
        EstimateCalculator.Calculate(new(MarginPercent: preferences.MarginPercent, FailureRatePercent: preferences.FailureRatePercent,
            MinimumPerPiece: preferences.PieceFee, EngineVersion: 2, V2: preferences.Options));
    }
}
