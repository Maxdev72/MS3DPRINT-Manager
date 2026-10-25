using System.Text;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class CorrectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-corrections-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 10, 25);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static RevenueEntry Revenue(decimal amount = 100m) =>
        new(Guid.NewGuid(), new DateOnly(2026, 3, 10), "Impression", amount, RevenueNature.Services, 0, amount, "Virement", null, "Client A", null, "F-1", null, DateTimeOffset.UtcNow);

    private static PurchaseEntry Purchase(decimal amount = 40m) =>
        new(Guid.NewGuid(), new DateOnly(2026, 3, 10), "Bambu Lab", "Bobines", amount, "Carte bancaire", "A-1", null, null, DateTimeOffset.UtcNow);

    [Fact]
    public void A_corrected_revenue_shows_the_new_values_keeps_the_original_and_lists_what_changed()
    {
        var journal = new RevenueJournal(_root);
        var entry = Revenue();
        journal.Append(entry, Today);
        journal.Correct(entry, entry with { Amount = 120m, ServicesAmount = 120m, Label = "Impression corrigée" }, "Erreur de saisie", new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero), Today);
        var line = Assert.Single(journal.Load(2026).Lines);
        Assert.True(line.IsCorrected); Assert.False(line.IsCancelled);
        Assert.Equal(120m, line.Entry.Amount); Assert.Equal(100m, line.FirstRecorded.Amount);
        var history = Assert.Single(RevenueHistory.Describe(line));
        Assert.Contains("Erreur de saisie", history); Assert.Contains("montant : 100,00 € → 120,00 €", history); Assert.Contains("libellé : Impression → Impression corrigée", history);
        Assert.Equal(120m, RevenueTotals.Compute(journal.Load(2026).Lines).Total);
    }

    [Fact]
    public void Several_corrections_chain_and_the_last_one_wins()
    {
        var journal = new RevenueJournal(_root);
        var entry = Revenue();
        journal.Append(entry, Today);
        var second = entry with { Amount = 110m, ServicesAmount = 110m };
        journal.Correct(entry, second, "Premier ajustement", DateTimeOffset.UtcNow, Today);
        journal.Correct(second, second with { Amount = 130m, ServicesAmount = 130m }, "Second ajustement", DateTimeOffset.UtcNow, Today);
        var line = Assert.Single(journal.Load(2026).Lines);
        Assert.Equal(130m, line.Entry.Amount); Assert.Equal(100m, line.FirstRecorded.Amount); Assert.Equal(2, RevenueHistory.Describe(line).Count);
    }

    [Fact]
    public void Corrections_are_refused_without_reason_for_another_year_for_invalid_values_or_when_cancelled()
    {
        var journal = new RevenueJournal(_root);
        var entry = Revenue();
        journal.Append(entry, Today);
        Assert.Throws<ArgumentException>(() => journal.Correct(entry, entry with { Label = "x" }, " ", DateTimeOffset.UtcNow, Today));
        Assert.Throws<ArgumentException>(() => journal.Correct(entry, entry with { ReceivedOn = new DateOnly(2025, 12, 31) }, "Année", DateTimeOffset.UtcNow, Today));
        Assert.Throws<ArgumentException>(() => journal.Correct(entry, entry with { Id = Guid.NewGuid() }, "Autre", DateTimeOffset.UtcNow, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Correct(entry, entry with { Amount = 0, ServicesAmount = 0 }, "Zéro", DateTimeOffset.UtcNow, Today));
        var ghost = Revenue(); Assert.Throws<InvalidOperationException>(() => journal.Correct(ghost, ghost with { Label = "Autre" }, "Absente", DateTimeOffset.UtcNow, Today));
        journal.Cancel(entry, "Annulée", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => journal.Correct(entry, entry with { Label = "y" }, "Trop tard", DateTimeOffset.UtcNow, Today));
        Assert.False(journal.Load(2026).Lines.Single().IsCorrected);
    }

    [Fact]
    public void A_corrected_line_can_still_be_cancelled_and_exports_show_the_status()
    {
        var journal = new RevenueJournal(_root);
        var entry = Revenue();
        journal.Append(entry, Today);
        var other = Revenue(50m) with { InvoiceNumber = "F-2" };
        journal.Append(other, Today);
        journal.Correct(entry, entry with { Label = "Corrigée" }, "Libellé erroné", DateTimeOffset.UtcNow, Today);
        var csv = RevenueExporter.ToCsv(journal.Load(2026));
        Assert.Contains(";Corrigée;Libellé erroné", csv);
        Assert.Contains(";Valide;", csv);
        Assert.Contains("Corrigée :", Encoding.Latin1.GetString(RevenueExporter.ToPdf(journal.Load(2026), new RevenueHeader())));
        journal.Cancel(journal.Load(2026).Lines.First(l => l.IsCorrected).Entry, "Finalement non", DateTimeOffset.UtcNow);
        Assert.Equal(50m, RevenueTotals.Compute(journal.Load(2026).Lines).Total);
    }

    [Fact]
    public void Purchases_follow_the_same_correction_rules_and_history()
    {
        var journal = new PurchaseJournal(_root);
        var entry = Purchase();
        journal.Append(entry, Today);
        Assert.Throws<ArgumentException>(() => journal.Correct(entry, entry with { Supplier = "X" }, "", DateTimeOffset.UtcNow, Today));
        Assert.Throws<ArgumentException>(() => journal.Correct(entry, entry with { PurchasedOn = new DateOnly(2025, 1, 1) }, "Année", DateTimeOffset.UtcNow, Today));
        journal.Correct(entry, entry with { Amount = 45.5m, Supplier = "Bambu Lab France" }, "Facture reçue", DateTimeOffset.UtcNow, Today);
        var line = Assert.Single(journal.Load(2026).Lines);
        Assert.Equal(45.5m, line.Entry.Amount); Assert.Equal(40m, line.FirstRecorded.Amount);
        var history = Assert.Single(PurchaseHistory.Describe(line));
        Assert.Contains("fournisseur : Bambu Lab → Bambu Lab France", history); Assert.Contains("Facture reçue", history);
        Assert.Contains(";Corrigé;Facture reçue", PurchaseExporter.ToCsv(journal.Load(2026)));
        journal.Cancel(line.Entry, "Doublon", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => journal.Correct(line.Entry, line.Entry with { Label = "z" }, "Tard", DateTimeOffset.UtcNow, Today));
    }

    [Fact]
    public void Purchase_annual_summary_lists_each_month_and_excludes_cancelled_purchases()
    {
        var journal = new PurchaseJournal(_root);
        var kept = Purchase(40m); var gone = Purchase(999m) with { Reference = "A-2" };
        journal.Append(kept, Today); journal.Append(gone, Today); journal.Cancel(gone, "Erreur", DateTimeOffset.UtcNow);
        var text = PurchaseExporter.AnnualSummary(2026, PurchaseTotals.Compute(journal.Load(2026).Lines), new RevenueHeader("MS3DPRINT", "123"));
        Assert.Contains("SIRET 123", text); Assert.Contains("Mars;40,00 €", text); Assert.Contains("Total 2026;40,00 €", text); Assert.Contains("1 achat(s) annulé(s)", text);
    }

    [Fact]
    public void Journals_written_before_corrections_existed_still_load()
    {
        var path = Path.Combine(_root, ".ms3dprint-manager", "revenues"); Directory.CreateDirectory(path);
        var entry = Revenue();
        File.WriteAllText(Path.Combine(path, "2026.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { Type = "entry", Entry = entry, Cancellation = (object?)null }) + "\n");
        var line = Assert.Single(new RevenueJournal(_root).Load(2026).Lines);
        Assert.Equal(entry.Id, line.Entry.Id); Assert.False(line.IsCorrected);
    }
}
