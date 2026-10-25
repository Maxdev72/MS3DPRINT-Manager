using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>
/// Ceilings the user wants to watch. No legal amount is built in: they change every year and depend on the activity,
/// so the user enters the ones of their own regime. An empty value is not watched.
/// </summary>
public sealed record ActivityThresholds(decimal? MicroTotalCeiling = null, decimal? MicroServicesCeiling = null, decimal? VatTotalThreshold = null, decimal? VatServicesThreshold = null)
{
    public bool IsEmpty => MicroTotalCeiling is null && MicroServicesCeiling is null && VatTotalThreshold is null && VatServicesThreshold is null;
}

public enum ThresholdLevel { Ok, Near, Exceeded }

public sealed record ThresholdStatus(string Label, decimal Amount, decimal Limit, ThresholdLevel Level)
{
    public decimal Percent => Limit <= 0 ? 0 : Math.Round(Amount / Limit * 100m, 1, MidpointRounding.AwayFromZero);
    public decimal Remaining => Math.Max(0, Limit - Amount);
}

public static class ActivityThresholdEvaluator
{
    public const decimal NearPercent = 80m;

    /// <summary>Compares the year's collected turnover (cancelled lines excluded) with each ceiling that was filled in.</summary>
    public static IReadOnlyList<ThresholdStatus> Evaluate(ActivityThresholds thresholds, RevenueSummary summary)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(summary);
        var result = new List<ThresholdStatus>();
        void Add(string label, decimal amount, decimal? limit)
        {
            if (limit is not > 0) return;
            var level = amount >= limit ? ThresholdLevel.Exceeded : amount >= limit * NearPercent / 100m ? ThresholdLevel.Near : ThresholdLevel.Ok;
            result.Add(new ThresholdStatus(label, amount, limit.Value, level));
        }
        Add("Plafond du régime micro · chiffre d’affaires total", summary.Total, thresholds.MicroTotalCeiling);
        Add("Plafond du régime micro · prestations de services", summary.Services, thresholds.MicroServicesCeiling);
        Add("Franchise de TVA · chiffre d’affaires total", summary.Total, thresholds.VatTotalThreshold);
        Add("Franchise de TVA · prestations de services", summary.Services, thresholds.VatServicesThreshold);
        return result;
    }
}

public sealed class ActivityThresholdStore
{
    private readonly string _path;
    public ActivityThresholdStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "activity-thresholds.json");
    }

    public ActivityThresholds Load()
    {
        WorkspacePathSafety.EnsureNoLinks(_path);
        if (!File.Exists(_path)) return new();
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<ActivityThresholds>(stream) ?? new();
    }

    public void Save(ActivityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        foreach (var value in new[] { thresholds.MicroTotalCeiling, thresholds.MicroServicesCeiling, thresholds.VatTotalThreshold, thresholds.VatServicesThreshold })
            if (value is <= 0) throw new ArgumentOutOfRangeException(nameof(thresholds), "Un plafond doit être strictement positif, ou laissé vide.");
        WorkspacePathSafety.EnsureNoLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var mutation = ProfileMutationLock.Acquire(_path);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, thresholds, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(flushToDisk: true); }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
