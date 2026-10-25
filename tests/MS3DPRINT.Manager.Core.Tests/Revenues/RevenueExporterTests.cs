using System.Text;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Revenues;

public sealed class RevenueExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-revenue-export-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static RevenueEntry Entry(string label, decimal amount, string invoice = "F-1", int day = 10) =>
        new(Guid.NewGuid(), new DateOnly(2026, 3, day), label, amount, RevenueNature.Services, 0, amount, "Virement", null, "Atelier Dupont", "P-1", invoice, null, DateTimeOffset.UtcNow);

    private RevenueYear Year(params RevenueLine[] lines) => new(2026, lines, []);

    [Fact]
    public void Csv_has_french_decimals_status_and_cancellation_reason()
    {
        var valid = new RevenueLine(Entry("Pièces \"spéciales\"; lot", 1234.5m), null);
        var cancelledEntry = Entry("Erreur", 10m, "F-2", 11);
        var cancelled = new RevenueLine(cancelledEntry, new RevenueCancellation(Guid.NewGuid(), cancelledEntry.Id, "Doublon", DateTimeOffset.UtcNow));
        var lines = RevenueExporter.ToCsv(Year(valid, cancelled)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"Pièces \"\"spéciales\"\"; lot\"", lines[1]);
        Assert.Contains(";1234,50;", lines[1]);
        Assert.Contains(";Valide;", lines[1]);
        Assert.EndsWith(";Annulée;Doublon", lines[2]);
    }

    [Fact]
    public void Csv_neutralises_formula_injection()
    {
        var csv = RevenueExporter.ToCsv(Year(new RevenueLine(Entry("=HYPERLINK(\"http://x\")", 5m), null)));
        Assert.Contains(";\"'=HYPERLINK", csv);
        Assert.DoesNotContain(";\"=HYPERLINK", csv);
    }

    [Fact]
    public void Declaration_summary_lists_each_month_and_the_year_total()
    {
        var summary = RevenueTotals.Compute([new RevenueLine(Entry("a", 100m), null), new RevenueLine(Entry("b", 50.5m, "F-2"), null)]);
        var text = RevenueExporter.DeclarationSummary(2026, summary, new RevenueHeader("MS3DPRINT", "123 456 789 00012"));
        Assert.Contains("SIRET 123 456 789 00012", text);
        Assert.Contains("Mars;0,00 €;150,50 €;150,50 €", text);
        Assert.Contains("Total 2026;0,00 €;150,50 €;150,50 €", text);
    }

    [Fact]
    public void Pdf_is_a_well_formed_multi_page_document_with_the_requested_data()
    {
        var lines = Enumerable.Range(1, 80).Select(i => new RevenueLine(Entry("Pièce n° " + i + " (é)", i, "F-" + i, 1 + i % 28), null)).ToArray();
        var bytes = RevenueExporter.ToPdf(Year(lines), new RevenueHeader("MS3DPRINT", "123"));
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("Livre des recettes 2026", text);
        Assert.Contains("SIRET 123", text);
        Assert.Contains("TOTAL 2026", text);
        var pages = System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page /Parent").Count;
        Assert.True(pages >= 2);
        Assert.Contains($"/Count {pages}", text);
        var startxref = long.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[(int)startxref..]);
    }

    [Fact]
    public void Pdf_of_an_empty_year_still_has_one_page_and_zero_totals()
    {
        var text = Encoding.Latin1.GetString(RevenueExporter.ToPdf(Year(), new RevenueHeader()));
        Assert.Contains("/Count 1", text);
        Assert.Contains("Atelier non renseigné", text);
    }

    [Fact]
    public void Header_store_round_trips_and_defaults_to_empty()
    {
        var store = new RevenueHeaderStore(_root);
        Assert.Equal(new RevenueHeader(), store.Load());
        store.Save(new RevenueHeader("MS3DPRINT", "123 456 789 00012", "M. Semaille"));
        Assert.Equal("123 456 789 00012", new RevenueHeaderStore(_root).Load().Siret);
    }

    [Fact]
    public void Company_contact_details_round_trip_and_are_printed_under_the_identity_line()
    {
        var store = new RevenueHeaderStore(_root);
        store.Save(new RevenueHeader("MS3DPRINT", "123", "M. Semaille", "5 rue de la Crémaillère, 72370 Le Breil-sur-Mérize", "contact@exemple.fr", "06 00 00 00 00"));
        var loaded = new RevenueHeaderStore(_root).Load();
        Assert.Equal("contact@exemple.fr", loaded.Email);
        Assert.Equal("5 rue de la Crémaillère, 72370 Le Breil-sur-Mérize — contact@exemple.fr — 06 00 00 00 00", loaded.ContactLine());
        Assert.Null(new RevenueHeader("A").ContactLine());
        var pdf = Encoding.Latin1.GetString(RevenueExporter.ToPdf(Year(), loaded));
        Assert.Contains("contact@exemple.fr", pdf);
    }

    [Fact]
    public void An_old_header_file_without_contact_fields_still_loads()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager"));
        File.WriteAllText(Path.Combine(_root, ".ms3dprint-manager", "revenue-header.json"), "{\"BusinessName\":\"Ancien\",\"Siret\":\"1\",\"OwnerName\":\"X\"}");
        var loaded = new RevenueHeaderStore(_root).Load();
        Assert.Equal("Ancien", loaded.BusinessName); Assert.Equal("", loaded.Address);
    }
}
