using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Revenue_page_warns_when_the_watched_ceiling_is_getting_close() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            var note = Field<TextBlock>(owner, "_revenueThresholdNote");
            Assert.Contains("Aucun plafond surveillé", note.Text!);
            new ActivityThresholdStore(_root).Save(new ActivityThresholds(MicroTotalCeiling: 100m));
            new RevenueJournal(_root).Append(UiRevenue("F-900", 90m));
            await (Task)Invoke(owner, "LoadRevenuesAsync")!;
            Assert.Contains("90 %", note.Text!); Assert.Contains("il reste 10,00 €", note.Text!);
            new RevenueJournal(_root).Append(UiRevenue("F-901", 20m));
            await (Task)Invoke(owner, "LoadRevenuesAsync")!;
            Assert.Contains("plafond atteint ou dépassé", note.Text!);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Project_sheet_shows_quoted_collected_and_linked_purchases() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Support");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
        new EstimateStore(_root).Create(new EstimateProfile(Guid.NewGuid(), "Devis", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, 150m, 150m), DateTimeOffset.UtcNow, ProjectReference: project.Reference));
        new RevenueJournal(_root).Append(UiRevenue("F-950", 60m) with { ProjectRef = project.Reference });
        new PurchaseJournal(_root).Append(UiPurchase("A-950", 12.5m) with { ProjectRef = project.Reference });
        var window = await Open(); window.Show();
        try
        {
            var sheet = Assert.IsType<Border>(Invoke(window, "BuildProjectOverview", project));
            window.Content = sheet;
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 800 && !sheet.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Reste à encaisser"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var texts = sheet.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Bilan du projet", texts); Assert.Contains("150,00 €", texts); Assert.Contains("60,00 €", texts);
            Assert.Contains("90,00 €", texts); Assert.Contains("12,50 €", texts); Assert.Contains("47,50 €", texts);
            Assert.Contains("Encaissé − achats", texts);
            Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<Expander>(), e => e.IsExpanded);
        }
        finally { window.Close(); }
    });
}
