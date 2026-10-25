using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>Append-only revenue book, one JSON-lines file per calendar year. Lines are never rewritten or removed.</summary>
public sealed class RevenueJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly string _directory;

    public RevenueJournal(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "revenues");
    }

    public IReadOnlyList<int> Years()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.jsonl")
            .Select(path => int.TryParse(Path.GetFileNameWithoutExtension(path), out var year) ? year : 0)
            .Where(year => year > 0).OrderByDescending(year => year).ToArray();
    }

    public void Append(RevenueEntry entry, DateOnly? today = null)
    {
        Validate(entry, today ?? DateOnly.FromDateTime(DateTime.Today));
        var path = PathFor(entry.ReceivedOn.Year);
        using var mutation = ProfileMutationLock.Acquire(path);
        WriteUnlocked(path, new Record("entry", entry, null));
    }

    public void Cancel(RevenueEntry entry, string reason, DateTimeOffset cancelledAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Le motif d’annulation est obligatoire.", nameof(reason));
        var year = entry.ReceivedOn.Year;
        var path = PathFor(year);
        using var mutation = ProfileMutationLock.Acquire(path);
        var line = Load(year).Lines.FirstOrDefault(l => l.Entry.Id == entry.Id)
            ?? throw new InvalidOperationException("Cette recette est introuvable dans le livre.");
        if (line.IsCancelled) throw new InvalidOperationException("Cette recette est déjà annulée.");
        WriteUnlocked(path, new Record("cancellation", null, new RevenueCancellation(Guid.NewGuid(), entry.Id, reason.Trim(), cancelledAt)));
    }

    /// <summary>
    /// Replaces the values of a recorded line while keeping the whole history. The collection stays in the same calendar year:
    /// to move a line to another year, cancel it and record it again.
    /// </summary>
    public void Correct(RevenueEntry current, RevenueEntry corrected, string reason, DateTimeOffset correctedAt, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(corrected);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Le motif de la correction est obligatoire.", nameof(reason));
        if (corrected.Id != current.Id) throw new ArgumentException("La correction doit porter sur la même recette.", nameof(corrected));
        if (corrected.ReceivedOn.Year != current.ReceivedOn.Year)
            throw new ArgumentException("La date doit rester dans la même année. Pour changer d’année, annulez la recette puis saisissez-la à nouveau.", nameof(corrected));
        Validate(corrected, today ?? DateOnly.FromDateTime(DateTime.Today));
        var year = current.ReceivedOn.Year;
        var path = PathFor(year);
        using var mutation = ProfileMutationLock.Acquire(path);
        var line = Load(year).Lines.FirstOrDefault(l => l.Entry.Id == current.Id)
            ?? throw new InvalidOperationException("Cette recette est introuvable dans le livre.");
        if (line.IsCancelled) throw new InvalidOperationException("Une recette annulée ne peut plus être corrigée.");
        WriteUnlocked(path, new Record("correction", null, null, new RevenueCorrection(Guid.NewGuid(), current.Id, reason.Trim(), correctedAt, corrected)));
    }

    public RevenueYear Load(int year)
    {
        var path = PathFor(year);
        WorkspacePathSafety.EnsureNoLinks(path);
        var entries = new List<RevenueEntry>();
        var cancellations = new Dictionary<Guid, RevenueCancellation>();
        var corrections = new Dictionary<Guid, List<RevenueCorrection>>();
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
                    var record = JsonSerializer.Deserialize<Record>(text, JsonOptions);
                    if (record?.Entry is { } entry && record.Type == "entry") entries.Add(entry);
                    else if (record?.Cancellation is { } cancellation && record.Type == "cancellation") cancellations.TryAdd(cancellation.EntryId, cancellation);
                    else if (record?.Correction is { } correction && record.Type == "correction")
                    {
                        if (!corrections.TryGetValue(correction.EntryId, out var history)) corrections[correction.EntryId] = history = [];
                        history.Add(correction);
                    }
                    else errors.Add($"Ligne {number} : type inconnu.");
                }
                catch (JsonException exception) { errors.Add($"Ligne {number} : {exception.Message}"); }
            }
        }
        var lines = entries.Select(original =>
            {
                var history = corrections.GetValueOrDefault(original.Id);
                return history is { Count: > 0 }
                    ? new RevenueLine(history[^1].Entry, cancellations.GetValueOrDefault(original.Id), history, original)
                    : new RevenueLine(original, cancellations.GetValueOrDefault(original.Id));
            }).OrderBy(l => l.Entry.ReceivedOn).ThenBy(l => l.Entry.RecordedAt).ToArray();
        return new(year, lines, errors);
    }

    public bool IsInvoiceKnown(string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) return false;
        return Years().Any(year => Load(year).Lines.Any(l => !l.IsCancelled
            && string.Equals(l.Entry.InvoiceNumber.Trim(), invoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private string PathFor(int year) => Path.Combine(_directory, year.ToString("0000") + ".jsonl");

    private static void Validate(RevenueEntry entry, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(entry), "Le montant doit être positif.");
        if (entry.ReceivedOn > today) throw new ArgumentOutOfRangeException(nameof(entry), "La date d’encaissement ne peut pas être dans le futur.");
        if (string.IsNullOrWhiteSpace(entry.Label)) throw new ArgumentException("Le libellé est obligatoire.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.InvoiceNumber)) throw new ArgumentException("La référence de la pièce justificative est obligatoire.", nameof(entry));
        if (entry.GoodsAmount < 0 || entry.ServicesAmount < 0 || entry.GoodsAmount + entry.ServicesAmount != entry.Amount)
            throw new ArgumentException("La répartition marchandises/prestations doit égaler le montant.", nameof(entry));
        if (entry.Nature == RevenueNature.Goods && entry.ServicesAmount != 0 || entry.Nature == RevenueNature.Services && entry.GoodsAmount != 0)
            throw new ArgumentException("La nature ne correspond pas à la répartition.", nameof(entry));
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
        var text = (needsBreak ? "\n" : "") + JsonSerializer.Serialize(record, JsonOptions) + "\n";
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(new UTF8Encoding(false).GetBytes(text));
        stream.Flush(flushToDisk: true);
    }

    private sealed record Record(string Type, RevenueEntry? Entry, RevenueCancellation? Cancellation, RevenueCorrection? Correction = null);
}
