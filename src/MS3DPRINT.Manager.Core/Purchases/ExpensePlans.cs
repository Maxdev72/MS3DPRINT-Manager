using System.Security.Cryptography;
using System.Text;

namespace MS3DPRINT.Manager.Core.Purchases;

public enum ExpensePlanKind { Financing, Recurring }
public sealed record ExpenseInstallment(Guid Id, DateOnly DueOn, decimal Amount, decimal? Principal = null,
    decimal? Interest = null, decimal? Insurance = null, decimal? RemainingCapital = null);
public sealed record ExpensePayment(ExpenseInstallment Installment, DateOnly PaidOn);
public sealed record ExpensePlan(Guid Id, ExpensePlanKind Kind, string Organization, string Label, decimal RegularAmount,
    DateOnly FirstDueOn, int IntervalMonths, DateOnly? EndOn, decimal? LastAmount,
    IReadOnlyList<ExpenseInstallment> Installments, IReadOnlyList<ExpensePayment> Payments,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsArchived = false);
public sealed record ExpensePlanSummary(int RemainingCount, decimal RemainingAmount, DateOnly? NextDue,
    DateOnly? LastDue, decimal? LastAmount, bool IsForecast);

public static class ExpensePlans
{
    public static ExpensePlan Create(Guid id, ExpensePlanKind kind, string organization, string label, decimal amount,
        DateOnly first, int frequency, DateOnly? end, decimal? last)
    {
        if (kind == ExpensePlanKind.Financing && end is null) throw new ArgumentException("La dernière échéance du financement est requise.");
        var now = DateTimeOffset.UtcNow;
        var p = new ExpensePlan(id, kind, organization.Trim(), label.Trim(), amount, first, frequency, end, last, [], [], now, now);
        Validate(p);
        if (end is { } until)
        {
            var rows = Generate(p, until).ToArray();
            if (kind == ExpensePlanKind.Financing && rows[^1].DueOn != until) throw new ArgumentException("La dernière date doit correspondre à la fréquence choisie. Sinon, utilisez le tableau bancaire.");
            if (last is { } final) rows[^1] = rows[^1] with { Amount = final };
            p = p with { Installments = rows };
        }
        return p;
    }

    public static ExpensePlan FromTable(Guid id, string organization, string label, IReadOnlyList<ExpenseInstallment> rows)
    {
        if (rows.Count == 0) throw new ArgumentException("Le tableau est vide.");
        var now = DateTimeOffset.UtcNow;
        var usual = rows.GroupBy(r => r.Amount).OrderByDescending(g => g.Count()).First().Key;
        var frequency = new[] { 1, 3, 12 }.FirstOrDefault(n => rows.Select((r, i) =>
            (r.DueOn.Year - rows[0].DueOn.Year) * 12 + r.DueOn.Month - rows[0].DueOn.Month == i * n
            && rows[0].DueOn.AddMonths(i * n) == r.DueOn).All(matches => matches), 1);
        var p = new ExpensePlan(id, ExpensePlanKind.Financing, organization.Trim(), label.Trim(), usual,
            rows[0].DueOn, frequency, rows[^1].DueOn, rows[^1].Amount, rows, [], now, now);
        Validate(p); return p;
    }

    public static IReadOnlyList<ExpenseInstallment> Schedule(ExpensePlan p, DateOnly today)
    {
        if (p.EndOn is not null) return p.Installments;
        var start = today > p.FirstDueOn ? today : p.FirstDueOn;
        var through = start.Year >= 9999 ? DateOnly.MaxValue : start.AddMonths(12);
        return Generate(p, through).ToArray();
    }

    private static IEnumerable<ExpenseInstallment> Generate(ExpensePlan p, DateOnly through)
    {
        for (var i = 0; i < 1200; i++)
        {
            if ((long)p.FirstDueOn.Year * 12 + p.FirstDueOn.Month - 1 + (long)i * p.IntervalMonths >= 120000) yield break;
            var due = p.FirstDueOn.AddMonths(i * p.IntervalMonths);
            if (due > through) yield break;
            yield return new(TermId(p.Id, i, due), due, p.RegularAmount);
            if (due == through) yield break;
        }
        throw new ArgumentException("L’échéancier est limité à 1 200 lignes. Renseignez la prochaine échéance restante comme point de départ.");
    }

    public static ExpensePlanSummary Summarize(ExpensePlan p, DateOnly today)
    {
        var all = Schedule(p, today);
        var paid = p.Payments.Select(m => m.Installment.Id).ToHashSet();
        var remaining = all.Where(r => !paid.Contains(r.Id)).ToArray();
        return new(remaining.Length, remaining.Sum(r => r.Amount), remaining.FirstOrDefault()?.DueOn,
            all.LastOrDefault()?.DueOn, all.LastOrDefault()?.Amount, p.EndOn is null);
    }

    public static ExpensePlan ConfirmPayment(ExpensePlan p, Guid term, DateOnly paidOn, DateOnly today)
    {
        if (paidOn > today) throw new ArgumentException("La date de paiement ne peut pas être future.");
        if (p.Payments.Any(m => m.Installment.Id == term)) throw new InvalidOperationException("Ce paiement est déjà confirmé.");
        var row = Schedule(p, today).SingleOrDefault(r => r.Id == term) ?? throw new InvalidOperationException("L’échéance est introuvable. Actualisez la fiche.");
        return p with { Payments = [.. p.Payments, new ExpensePayment(row, paidOn)] };
    }

    public static ExpensePlan ReversePayment(ExpensePlan p, Guid term)
        => p with { Payments = p.Payments.Where(m => m.Installment.Id != term).ToArray() };

    public static Guid TermId(Guid plan, int index, DateOnly date)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{plan:N}|{index}|{date:yyyy-MM-dd}"))).AsSpan(0, 16));

    public static void Validate(ExpensePlan p)
    {
        if (p.Id == Guid.Empty || !Enum.IsDefined(p.Kind) || string.IsNullOrWhiteSpace(p.Organization) || string.IsNullOrWhiteSpace(p.Label)) throw new ArgumentException("L’organisme et le libellé sont requis.");
        if (p.Organization.Length > 250 || p.Label.Length > 500) throw new ArgumentException("L’organisme ou le libellé est trop long.");
        if (!Money(p.RegularAmount) || p.LastAmount is { } a && !Money(a)) throw new ArgumentException("Les montants doivent être positifs, avec deux décimales au maximum.");
        if (p.IntervalMonths is not (1 or 3 or 12) || p.EndOn < p.FirstDueOn || p.Kind == ExpensePlanKind.Financing && p.EndOn is null) throw new ArgumentException("Vérifiez la fréquence et les dates de l’échéancier.");
        if (p.Installments is null || p.Payments is null || p.Installments.Count > 1200 || p.Payments.Count > 1200) throw new ArgumentException("L’échéancier est invalide ou trop volumineux.");
        var ids = new HashSet<Guid>(); DateOnly? previous = null;
        foreach (var r in p.Installments)
        {
            if (r is null || r.Id == Guid.Empty || !ids.Add(r.Id) || !Money(r.Amount) || r.DueOn < previous) throw new ArgumentException("Une échéance est invalide ou les dates ne sont pas classées.");
            foreach (var part in new[] { r.Principal, r.Interest, r.Insurance, r.RemainingCapital }) if (part is { } value && (value < 0 || value != decimal.Round(value, 2))) throw new ArgumentException("Les composantes bancaires sont invalides.");
            previous = r.DueOn;
        }
        var payments = new HashSet<Guid>();
        foreach (var m in p.Payments)
            if (m?.Installment is null || !payments.Add(m.Installment.Id) || !Money(m.Installment.Amount)) throw new ArgumentException("Une confirmation de paiement est invalide.");
    }
    private static bool Money(decimal n) => n > 0 && n <= 1000000000m && n == decimal.Round(n, 2);
}
