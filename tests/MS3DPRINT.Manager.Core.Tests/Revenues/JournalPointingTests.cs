using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class JournalPointingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-pointing-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2026, 1, 15);
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);
    private static PurchaseEntry Purchase() => new(Guid.NewGuid(), Date, "Fournisseur", "Matière", 25m, "Virement", "F-1", null, null, Now);
    private static RevenueEntry Revenue() => new(Guid.NewGuid(), Date, "Prestation", 100m, RevenueNature.Services, 0, 100m, "Virement", null, "Client", null, "F-2", null, Now);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pointing_is_persisted_in_batches_and_reversible_without_changing_entries(bool revenue)
    {
        if (revenue)
        {
            var journal = new RevenueJournal(_root); var a = Revenue(); var b = Revenue();
            journal.Append(a); journal.Append(b);
            journal.SetPointed([a, b], true, Now);
            Assert.All(new RevenueJournal(_root).Load(2026).Lines, l => Assert.Equal(Now, l.PointedAt));
            journal.SetPointed([a], false, Now.AddMinutes(1));
            var lines = journal.Load(2026).Lines;
            Assert.Null(lines.Single(l => l.Entry.Id == a.Id).PointedAt);
            Assert.Equal(Now, lines.Single(l => l.Entry.Id == b.Id).PointedAt);
            Assert.Equal(200m, RevenueTotals.Compute(lines).Total);
            Assert.Equal(a, lines.Single(l => l.Entry.Id == a.Id).Entry);
        }
        else
        {
            var journal = new PurchaseJournal(_root); var a = Purchase(); var b = Purchase();
            journal.Append(a); journal.Append(b);
            journal.SetPointed([a, b], true, Now);
            Assert.All(new PurchaseJournal(_root).Load(2026).Lines, l => Assert.Equal(Now, l.PointedAt));
            journal.SetPointed([a], false, Now.AddMinutes(1));
            var lines = journal.Load(2026).Lines;
            Assert.Null(lines.Single(l => l.Entry.Id == a.Id).PointedAt);
            Assert.Equal(Now, lines.Single(l => l.Entry.Id == b.Id).PointedAt);
            Assert.Equal(50m, PurchaseTotals.Compute(lines).Total);
            Assert.Equal(a, lines.Single(l => l.Entry.Id == a.Id).Entry);
        }
        var path = Path.Combine(_root, ".ms3dprint-manager", revenue ? "revenues" : "purchases", "2026.jsonl");
        Assert.Equal(4, File.ReadAllLines(path).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Correction_clears_pointing_and_stale_or_cancelled_batches_are_rejected(bool revenue)
    {
        if (revenue)
        {
            var journal = new RevenueJournal(_root); var a = Revenue(); var b = Revenue();
            journal.Append(a); journal.Append(b); journal.SetPointed([a], true, Now);
            var corrected = a with { Label = "Corrigé" };
            journal.Correct(a, corrected, "Erreur", Now.AddMinutes(1));
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([b, a], true, Now));
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            journal.SetPointed([corrected], true, Now);
            journal.Cancel(corrected, "Annulation", Now);
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([b, corrected], true, Now));
        }
        else
        {
            var journal = new PurchaseJournal(_root); var a = Purchase(); var b = Purchase();
            journal.Append(a); journal.Append(b); journal.SetPointed([a], true, Now);
            var corrected = a with { Label = "Corrigé" };
            journal.Correct(a, corrected, "Erreur", Now.AddMinutes(1));
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([b, a], true, Now));
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            journal.SetPointed([corrected], true, Now);
            journal.Cancel(corrected, "Annulation", Now);
            Assert.All(journal.Load(2026).Lines, l => Assert.Null(l.PointedAt));
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([b, corrected], true, Now));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_batches_and_unreadable_journals_are_never_partially_pointed(bool revenue)
    {
        Action point;
        if (revenue)
        {
            var journal = new RevenueJournal(_root); var a = Revenue(); journal.Append(a);
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([a, Revenue()], true, Now));
            Assert.Throws<ArgumentException>(() => journal.SetPointed([a, a with { Id = Guid.NewGuid(), ReceivedOn = Date.AddYears(-1) }], true, Now));
            Assert.Null(Assert.Single(journal.Load(2026).Lines).PointedAt);
            point = () => journal.SetPointed([a], true, Now);
        }
        else
        {
            var journal = new PurchaseJournal(_root); var a = Purchase(); journal.Append(a);
            Assert.Throws<InvalidOperationException>(() => journal.SetPointed([a, Purchase()], true, Now));
            Assert.Throws<ArgumentException>(() => journal.SetPointed([a, a with { Id = Guid.NewGuid(), PurchasedOn = Date.AddYears(-1) }], true, Now));
            Assert.Null(Assert.Single(journal.Load(2026).Lines).PointedAt);
            point = () => journal.SetPointed([a], true, Now);
        }
        var path = Path.Combine(_root, ".ms3dprint-manager", revenue ? "revenues" : "purchases", "2026.jsonl");
        File.AppendAllText(path, "{invalid}\n");
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(point);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
