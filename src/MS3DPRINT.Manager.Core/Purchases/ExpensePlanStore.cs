using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Purchases;

public sealed class ExpensePlanStore(string workspaceRoot)
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".ms3dprint-manager", "expense-plans");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public IReadOnlyList<string> ReadErrors { get; private set; } = [];

    public IReadOnlyList<ExpensePlan> LoadReadable()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        var plans = new List<ExpensePlan>(); var errors = new List<string>();
        if (Directory.Exists(_directory)) foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try { plans.Add(Read(path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            { errors.Add(Path.GetFileName(path) + " : " + e.Message); }
        }
        ReadErrors = errors; return plans.OrderBy(p => p.Organization).ThenBy(p => p.Label).ToArray();
    }

    public void Create(ExpensePlan p)
    {
        ExpensePlans.Validate(p); CheckSchedule(p);
        using var guard = ProfileMutationLock.Acquire(FilePath(p.Id));
        if (File.Exists(FilePath(p.Id))) throw new InvalidOperationException("Une fiche porte déjà cet identifiant.");
        Write(p, false);
    }

    public ExpensePlan Update(ExpensePlan p, DateTimeOffset expected)
    {
        ExpensePlans.Validate(p);
        using var guard = ProfileMutationLock.Acquire(FilePath(p.Id));
        var previous = Read(FilePath(p.Id)); ProfileMutationLock.CheckVersion(previous.UpdatedAt, expected);
        CheckSchedule(p);
        // Confirmed terms are immutable; cancelling the confirmation is an explicit separate action.
        foreach (var old in previous.Payments.Where(m => p.Payments.Any(n => n.Installment.Id == m.Installment.Id)))
            if (!p.Payments.Contains(old)) throw new InvalidOperationException("Annulez la confirmation avant de modifier un paiement.");
        p = p with { CreatedAt = previous.CreatedAt, UpdatedAt = ProfileMutationLock.NextVersion(previous.UpdatedAt, DateTimeOffset.UtcNow) };
        Write(p, true); return p;
    }

    private static void CheckSchedule(ExpensePlan p)
    {
        if (p.EndOn is not null && (p.Installments.Count == 0 || p.Installments[0].DueOn != p.FirstDueOn || p.Installments[^1].DueOn > p.EndOn)) throw new InvalidOperationException("Les échéances ne correspondent pas aux dates de la fiche.");
        var horizon = p.Payments.Count > 0 ? p.Payments.Max(m => m.Installment.DueOn) : DateOnly.FromDateTime(DateTime.Today);
        var rows = ExpensePlans.Schedule(p, horizon).ToDictionary(r => r.Id);
        foreach (var m in p.Payments)
            if (!rows.TryGetValue(m.Installment.Id, out var row) || row != m.Installment) throw new InvalidOperationException("Une échéance payée ne peut pas être modifiée ou retirée. Annulez d’abord sa confirmation.");
    }

    private string FilePath(Guid id) => Path.Combine(_directory, id + ".json");
    private ExpensePlan Read(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        var p = JsonSerializer.Deserialize<ExpensePlan>(File.ReadAllText(path)) ?? throw new JsonException("Fiche vide.");
        ExpensePlans.Validate(p); CheckSchedule(p);
        if (Path.GetFileNameWithoutExtension(path) != p.Id.ToString()) throw new JsonException("Identifiant de fiche incohérent.");
        return p;
    }
    private void Write(ExpensePlan p, bool replace)
    {
        var path = FilePath(p.Id); WorkspacePathSafety.EnsureNoLinks(path); Directory.CreateDirectory(_directory);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, p, Json); stream.Flush(true); }
            WorkspacePathSafety.EnsureNoLinks(path);
            if (replace) Storage.AtomicFile.Replace(tmp, path); else File.Move(tmp, path);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
