using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Invoice_import_reports_an_unreadable_book_without_an_unhandled_error_or_a_new_record() => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        var owner = await Open(); owner.Show();
        Exception? unexpected = null;
        DispatcherUnhandledExceptionEventHandler onError = (_, args) => { unexpected = args.Exception; args.Handled = true; };
        Dispatcher.UIThread.UnhandledException += onError;
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            Invoke(owner, "ImportInvoices_Click", null, new RoutedEventArgs());
            await WaitForDialog<MS3DPRINT.Manager.Desktop.RevenueBatchWindow>(owner);
            var journal = new RevenueJournal(_root); journal.Append(UiRevenue("OTHER-1", 10m));
            var path = Path.Combine(_root, ".ms3dprint-manager", "revenues", "2026.jsonl");
            File.AppendAllText(path, "{ligne illisible}"); var before = File.ReadAllBytes(path);
            DialogAction(owner, "Tout cocher"); DialogAction(owner, "Enregistrer la sélection");
            var host = owner.FindControl<StackPanel>("ToastHost")!;
            bool HasMessage() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Vérification des doublons impossible") == true);
            for (var attempt = 0; attempt < 250 && unexpected is null && !HasMessage(); attempt++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Null(unexpected); Assert.True(HasMessage());
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(journal.Load(2026).Lines);
        }
        finally { Dispatcher.UIThread.UnhandledException -= onError; owner.Close(); }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task Invoice_import_skips_collections_recorded_after_the_batch_was_opened_and_reports_their_numbers(int existingCount) => Run(async () =>
    {
        WriteWorkspaceInvoice("Martin", "FACT202610-51", "85,31", "prestation de service");
        WriteWorkspaceInvoice("Martin", "FACT202610-52", "40,00", "prestation de service");
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Recettes")!;
            Invoke(owner, "ImportInvoices_Click", null, new RoutedEventArgs());
            await WaitForDialog<MS3DPRINT.Manager.Desktop.RevenueBatchWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var journal = new RevenueJournal(_root);
            var existing = new List<RevenueEntry>();
            for (var index = 0; index < existingCount; index++)
            {
                var entry = UiRevenue("FACT202610-" + (51 + index), index == 0 ? 85.31m : 40m);
                existing.Add(entry); journal.Append(entry);
            }
            DialogAction(owner, "Tout cocher"); DialogAction(owner, "Enregistrer la sélection");
            var expected = existingCount == 1 ? "1 facture déjà enregistrée ignorée" : "2 factures déjà enregistrées ignorées";
            var host = owner.FindControl<StackPanel>("ToastHost")!;
            for (var attempt = 0; attempt < 250 && !host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(expected) == true); attempt++)
                await Task.Delay(10);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(expected) == true && t.Text.Contains("FACT202610-51"));
            var lines = journal.Load(2026).Lines; Assert.Equal(2, lines.Count);
            Assert.All(lines.GroupBy(l => l.Entry.InvoiceNumber), group => Assert.Single(group));
            foreach (var entry in existing) Assert.Equal(entry, lines.Single(l => l.Entry.Id == entry.Id).Entry);
        }
        finally { owner.Close(); }
    });
}
