using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static RevenueEntry UiRevenue(string invoice, decimal amount) =>
        new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), "Impression de supports", amount, RevenueNature.Services, 0, amount, "Virement", null, "Atelier Test", null, invoice, null, DateTimeOffset.UtcNow);

    [Fact]
    public Task Revenue_book_page_lists_entries_and_cancellation_needs_a_reason_and_keeps_the_line() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            var panel = owner.FindControl<Border>("RevenuesWorkspacePanel")!;
            Assert.True(panel.IsVisible);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Aucune recette en") == true);

            var journal = new RevenueJournal(_root);
            var kept = UiRevenue("F-100", 80m); var wrong = UiRevenue("F-101", 20m);
            journal.Append(kept); journal.Append(wrong);
            await (Task)Invoke(owner, "LoadRevenuesAsync")!;
            var list = owner.FindControl<StackPanel>("RevenueList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "F-100");
            Assert.Contains("2 recettes", Field<TextBlock>(owner, "_revenuePageSummary").Text!);

            var cancel = new Button { Tag = wrong };
            Invoke(owner, "CancelRevenue_Click", cancel, new RoutedEventArgs());
            await WaitForDialog<RevenueCancelWindow>(owner);
            DialogAction(owner, "Annuler la recette");
            Dispatcher.UIThread.RunJobs();
            Assert.False(journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled);
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single().Text = "Doublon";
            DialogAction(owner, "Annuler la recette");
            for (var i = 0; i < 200 && !journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id).IsCancelled; i++) await Task.Delay(10);

            var toasts = string.Join(" | ", owner.FindControl<StackPanel>("ToastHost")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            var line = journal.Load(DateTime.Today.Year).Lines.Single(l => l.Entry.Id == wrong.Id);
            Assert.True(line.IsCancelled, toasts);
            Assert.True(line.IsCancelled); Assert.Equal("Doublon", line.Cancellation!.Reason);
            for (var i = 0; i < 200 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("annulée : Doublon") == true); i++) await Task.Delay(10);
            var struck = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("annulée : Doublon") == true);
            Assert.Equal(TextDecorations.Strikethrough, struck.TextDecorations);
            Assert.Contains("1 recette ", Field<TextBlock>(owner, "_revenuePageSummary").Text!);
        }
        finally { owner.Close(); }
    });
}
