using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Mobile;

/// <summary>A read-only picture of the workshop that the computer publishes for the phone (projects, clients, activity). The phone never modifies it.</summary>
public sealed record PcSnapshot(DateTimeOffset GeneratedAt, string WorkshopName, IReadOnlyList<SnapshotClient> Clients, IReadOnlyList<SnapshotProject> Projects,
    SnapshotActivity Activity, IReadOnlyList<string> Suppliers, int Version = PcSnapshot.CurrentVersion)
{
    public const int CurrentVersion = 1;
    public const string FileName = "etat-pc.json";
}

public sealed record SnapshotClient(string Name, string? Code);

/// <param name="Status">"Quote", "InProgress" or "Completed".</param>
/// <param name="Priority">"None", "Low", "Medium" or "High".</param>
public sealed record SnapshotProject(string Reference, string Title, string ClientName, string Status, DateOnly? DueDate, string Priority, bool Archived,
    decimal? Quoted, decimal Collected, decimal Purchases);

public sealed record SnapshotThreshold(string Label, decimal Amount, decimal Limit, string Level);

/// <param name="Months">Twelve amounts, January first, cancelled lines excluded.</param>
public sealed record SnapshotActivity(int Year, decimal Total, decimal Goods, decimal Services, IReadOnlyList<decimal> Months, decimal PurchasesTotal,
    IReadOnlyList<SnapshotThreshold> Thresholds, int PendingFromPhone);

public static class PcSnapshotFile
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static byte[] Serialize(PcSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
    }

    public static PcSnapshot Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaxBytes) throw new JsonException("État du PC vide ou trop volumineux.");
        var snapshot = JsonSerializer.Deserialize<PcSnapshot>(json) ?? throw new JsonException("État du PC vide.");
        if (snapshot.Version > PcSnapshot.CurrentVersion) throw new JsonException("L’état du PC vient d’une version plus récente du logiciel : mettez l’application à jour.");
        return snapshot;
    }

    /// <summary>Atomic write into the exchange folder; the phone never sees a half-written file.</summary>
    public static void Write(string workspaceRoot, PcSnapshot snapshot)
    {
        var folder = MobileInbox.FolderFor(workspaceRoot);
        WorkspacePathSafety.EnsureNoLinks(folder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, PcSnapshot.FileName);
        using var mutation = ProfileMutationLock.Acquire(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, Serialize(snapshot));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static PcSnapshot? Read(string workspaceRoot)
    {
        var path = Path.Combine(MobileInbox.FolderFor(workspaceRoot), PcSnapshot.FileName);
        WorkspacePathSafety.EnsureNoLinks(path);
        return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null;
    }
}
