using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Journal_pointing_can_switch_registers_during_a_slow_write_without_unsaved_checkmarks() => Run(async () =>
    {
        var purchases = new PurchaseJournal(_root); purchases.Append(UiPurchase("SLOW", 20m) with { Label = "Achat lent" });
        var revenues = new RevenueJournal(_root); revenues.Append(UiRevenue("FAST", 30m) with { Label = "Recette rapide" });
        var owner = await Open(); owner.Show();
        using var release = new ManualResetEventSlim();
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? holder = null;
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            holder = Task.Run(() =>
            {
                using var mutation = MS3DPRINT.Manager.Core.Workspace.ProfileMutationLock.Acquire(Path.Combine(_root, ".ms3dprint-manager", "purchases", DateTime.Today.Year + ".jsonl"));
                acquired.SetResult(); release.Wait(TimeSpan.FromSeconds(10));
            });
            await acquired.Task;
            var purchaseList = owner.FindControl<StackPanel>("PurchaseList")!;
            var check = purchaseList.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Pointage de : Achat lent");
            check.IsChecked = true;
            Assert.False(check.IsChecked);
            Assert.False(purchaseList.IsEnabled);
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            var revenueList = owner.FindControl<StackPanel>("RevenueList")!;
            revenueList.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Pointage de : Recette rapide").IsChecked = true;
            for (var i = 0; i < 200 && (revenues.Load(DateTime.Today.Year).Lines[0].PointedAt is null || !revenueList.IsEnabled); i++) await Task.Delay(10);
            Assert.NotNull(revenues.Load(DateTime.Today.Year).Lines[0].PointedAt);
            Assert.True(revenueList.IsEnabled);
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            await (Task)Invoke(owner, "LoadPurchasesAsync")!;
            release.Set(); await holder;
            for (var i = 0; i < 200 && (purchases.Load(DateTime.Today.Year).Lines[0].PointedAt is null || !purchaseList.IsEnabled); i++) await Task.Delay(10);
            Assert.NotNull(purchases.Load(DateTime.Today.Year).Lines[0].PointedAt);
            Assert.True(purchaseList.IsEnabled);
            for (var i = 0; i < 200 && !purchaseList.GetVisualDescendants().OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Pointage de : Achat lent" && c.IsChecked == true); i++) await Task.Delay(10);
            Assert.True(purchaseList.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Pointage de : Achat lent").IsChecked);
        }
        finally { release.Set(); if (holder is not null) await holder; owner.Close(); }
    });

    [Fact]
    public Task Journal_pointing_selection_spans_pages_and_batch_reversal_requires_confirmation() => Run(async () =>
    {
        var journal = new PurchaseJournal(_root);
        for (var i = 0; i < 51; i++) journal.Append(UiPurchase("PAGE-" + i, 1m) with { Label = "Achat " + i });
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Achats")!;
            var list = owner.FindControl<StackPanel>("PurchaseList")!;
            T Named<T>(string name) where T : Control => list.GetVisualDescendants().OfType<T>().First(c => AutomationProperties.GetName(c) == name);
            void Click(string name) => Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task Settled(int count)
            {
                for (var i = 0; i < 200 && (journal.Load(DateTime.Today.Year).Lines.Count(l => l.PointedAt is not null) != count || !list.IsEnabled); i++) await Task.Delay(10);
                await Task.Delay(80); Dispatcher.UIThread.RunJobs();
                Assert.Equal(count, journal.Load(DateTime.Today.Year).Lines.Count(l => l.PointedAt is not null));
            }
            Named<CheckBox>("Sélectionner toutes les lignes filtrées pour le pointage").IsChecked = true;
            Click("Page suivante"); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var visible = list.GetVisualDescendants().OfType<CheckBox>().Where(c => AutomationProperties.GetName(c)?.StartsWith("Sélectionner pour le pointage : ") == true).ToArray();
            Assert.Single(visible); Assert.True(visible[0].IsChecked);
            Click("Pointer la sélection"); await Settled(51);
            Assert.Equal(1, PagedRows.GetPage(list));
            Named<CheckBox>("Sélectionner toutes les lignes filtrées pour le pointage").IsChecked = true;
            Click("Dépointer la sélection"); await WaitForDialog<ConfirmRemovalWindow>(owner);
            DialogAction(owner, "Annuler"); await Settled(51);
            Named<CheckBox>("Sélectionner toutes les lignes filtrées pour le pointage").IsChecked = true;
            Click("Dépointer la sélection"); await WaitForDialog<ConfirmRemovalWindow>(owner);
            DialogAction(owner, "Confirmer le retrait"); await Settled(0);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Journal_manual_pointing_supports_batches_filters_totals_and_confirmed_reversal(bool revenue) => Run(async () =>
    {
        Func<int> pointed;
        if (revenue)
        {
            var journal = new RevenueJournal(_root);
            journal.Append(UiRevenue("P-A", 20m) with { Label = "Ligne A" });
            journal.Append(UiRevenue("P-B", 30m) with { Label = "Ligne B" });
            var cancelled = UiRevenue("P-C", 40m); journal.Append(cancelled); journal.Cancel(cancelled, "Erreur", DateTimeOffset.Now);
            pointed = () => journal.Load(DateTime.Today.Year).Lines.Count(l => l.PointedAt is not null);
        }
        else
        {
            var journal = new PurchaseJournal(_root);
            journal.Append(UiPurchase("P-A", 20m) with { Label = "Ligne A" });
            journal.Append(UiPurchase("P-B", 30m) with { Label = "Ligne B" });
            var cancelled = UiPurchase("P-C", 40m); journal.Append(cancelled); journal.Cancel(cancelled, "Erreur", DateTimeOffset.Now);
            pointed = () => journal.Load(DateTime.Today.Year).Lines.Count(l => l.PointedAt is not null);
        }
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", revenue ? "Recettes" : "Achats")!;
            var list = owner.FindControl<StackPanel>(revenue ? "RevenueList" : "PurchaseList")!;
            T Named<T>(string name) where T : Control => list.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            void Click(string name) => Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task Settled(int count)
            {
                for (var i = 0; i < 200 && (pointed() != count || !list.IsEnabled); i++) await Task.Delay(10);
                await Task.Delay(80); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                Assert.Equal(count, pointed()); Assert.True(list.IsEnabled);
            }
            Named<CheckBox>("Sélectionner pour le pointage : Ligne A").IsChecked = true;
            Named<CheckBox>("Sélectionner pour le pointage : Ligne B").IsChecked = true;
            Click("Pointer la sélection"); await Settled(2);
            await (Task)Invoke(owner, revenue ? "LoadRevenuesAsync" : "LoadPurchasesAsync")!;
            Assert.Equal(2, pointed());
            Click("Pointage : À pointer"); await Settled(2);
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c)?.StartsWith("Pointage de : ") == true);
            Click("Pointage : Pointés"); await Settled(2);
            Named<CheckBox>("Pointage de : Ligne A").IsChecked = false;
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Annuler"); await Settled(2);
            Assert.True(Named<CheckBox>("Pointage de : Ligne A").IsChecked);
            Named<CheckBox>("Pointage de : Ligne A").IsChecked = false;
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Confirmer le retrait"); await Settled(1);
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Pointage de : Ligne A");
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("30,00") == true && t.Text.Contains("20,00"));
            Click("Pointage : Tous"); await Settled(1);
            foreach (var width in new[] { 960d, 1440d }) { owner.Width = width; await Task.Delay(150); CaptureDraftUi(owner, "journal-pointing-" + revenue + "-" + width); }
        }
        finally { owner.Close(); }
    });
}
