using System.Text;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Purchases;

/// <summary>One purchase recorded in the purchase register (date, supplier, label, amount, payment method, supporting document).</summary>
public sealed record PurchaseEntry(Guid Id, DateOnly PurchasedOn, string Supplier, string Label, decimal Amount, string PaymentMethod,
    string Reference, string? DocumentPath, string? ProjectRef, DateTimeOffset RecordedAt);

public sealed record PurchaseCancellation(Guid Id, Guid EntryId, string Reason, DateTimeOffset CancelledAt);

public sealed record PurchaseLine(PurchaseEntry Entry, PurchaseCancellation? Cancellation)
{
    public bool IsCancelled => Cancellation is not null;
}

public sealed record PurchaseYear(int Year, IReadOnlyList<PurchaseLine> Lines, IReadOnlyList<string> Errors);

public sealed record PurchaseSummary(decimal Total, IReadOnlyList<decimal> Months, int CancelledCount);

public static class PurchaseTotals
{
    public static PurchaseSummary Compute(IEnumerable<PurchaseLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var months = new decimal[12];
        var cancelled = 0;
        foreach (var line in lines)
        {
            if (line.IsCancelled) { cancelled++; continue; }
            months[line.Entry.PurchasedOn.Month - 1] += line.Entry.Amount;
        }
        return new(months.Sum(), months, cancelled);
    }
}

/// <summary>Append-only purchase register, one JSON-lines file per calendar year. Lines are never rewritten or removed.</summary>
public sealed class PurchaseJournal
{
    private readonly string _directory;

    public PurchaseJournal(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "purchases");
    }

    public IReadOnlyList<int> Years()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.jsonl")
            .Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out var year) ? year : 0)
            .Where(year => year > 0).OrderByDescending(year => year).ToArray();
    }

    public void Append(PurchaseEntry entry, DateOnly? today = null)
    {
        Validate(entry, today ?? DateOnly.FromDateTime(DateTime.Today));
        var path = PathFor(entry.PurchasedOn.Year);
        using var mutation = ProfileMutationLock.Acquire(path);
        WriteUnlocked(path, new Record("entry", entry, null));
    }

    public void Cancel(PurchaseEntry entry, string reason, DateTimeOffset cancelledAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Le motif d’annulation est obligatoire.", nameof(reason));
        var year = entry.PurchasedOn.Year;
        var path = PathFor(year);
        using var mutation = ProfileMutationLock.Acquire(path);
        var line = Load(year).Lines.FirstOrDefault(l => l.Entry.Id == entry.Id)
            ?? throw new InvalidOperationException("Cet achat est introuvable dans le registre.");
        if (line.IsCancelled) throw new InvalidOperationException("Cet achat est déjà annulé.");
        WriteUnlocked(path, new Record("cancellation", null, new PurchaseCancellation(Guid.NewGuid(), entry.Id, reason.Trim(), cancelledAt)));
    }

    public PurchaseYear Load(int year)
    {
        var path = PathFor(year);
        WorkspacePathSafety.EnsureNoLinks(path);
        var entries = new List<PurchaseEntry>();
        var cancellations = new Dictionary<Guid, PurchaseCancellation>();
        var errors = new List<string>();
        if (File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            for (var number = 1; reader.ReadLine() is { } text; number++)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<Record>(text);
                    if (record?.Entry is { } entry && record.Type == "entry") entries.Add(entry);
                    else if (record?.Cancellation is { } cancellation && record.Type == "cancellation") cancellations.TryAdd(cancellation.EntryId, cancellation);
                    else errors.Add($"Ligne {number} : type inconnu.");
                }
                catch (JsonException exception) { errors.Add($"Ligne {number} : {exception.Message}"); }
            }
        }
        var lines = entries.OrderBy(e => e.PurchasedOn).ThenBy(e => e.RecordedAt)
            .Select(e => new PurchaseLine(e, cancellations.GetValueOrDefault(e.Id))).ToArray();
        return new(year, lines, errors);
    }

    public bool IsReferenceKnown(string supplier, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        return Years().Any(year => Load(year).Lines.Any(l => !l.IsCancelled
            && string.Equals(l.Entry.Reference.Trim(), reference.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(l.Entry.Supplier.Trim(), supplier.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    }

    private string PathFor(int year) => Path.Combine(_directory, year.ToString("0000") + ".jsonl");

    private static void Validate(PurchaseEntry entry, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(entry), "Le montant doit être positif.");
        if (entry.PurchasedOn > today) throw new ArgumentOutOfRangeException(nameof(entry), "La date d’achat ne peut pas être dans le futur.");
        if (string.IsNullOrWhiteSpace(entry.Supplier)) throw new ArgumentException("Le fournisseur est obligatoire.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Label)) throw new ArgumentException("Le libellé est obligatoire.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Reference)) throw new ArgumentException("La référence de la pièce justificative est obligatoire.", nameof(entry));
    }

    private void WriteUnlocked(string path, Record record)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        Directory.CreateDirectory(_directory);
        var needsBreak = false;
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            probe.Seek(-1, SeekOrigin.End);
            needsBreak = probe.ReadByte() != '\n';
        }
        var text = (needsBreak ? "\n" : "") + JsonSerializer.Serialize(record) + "\n";
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(new UTF8Encoding(false).GetBytes(text));
        stream.Flush(flushToDisk: true);
    }

    private sealed record Record(string Type, PurchaseEntry? Entry, PurchaseCancellation? Cancellation);
}
