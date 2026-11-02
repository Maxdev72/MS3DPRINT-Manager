using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(205)]
    public Task Import_banner_opens_its_result_first_even_after_later_notifications(int laterMessages) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "StartImportBatch", 2);
            Invoke(owner, "CompleteImportBatch", 2, 0, 2, false);
            if (laterMessages == 0)
            {
                Click(owner, "NotificationCenterButton"); await WaitForDialog<NotificationCenterWindow>(owner);
                DialogAction(owner, "Effacer l’historique");
                Assert.Single(owner.OwnedWindows.OfType<ConfirmRemovalWindow>()).Close(true);
                await Task.Delay(20); Dispatcher.UIThread.RunJobs();
                DialogAction(owner, "Fermer");
            }
            for (var index = 0; index < laterMessages; index++) Invoke(owner, "AddNotification", $"Message ultérieur {index}", "Détails", false, null);
            Click(owner, "ImportBatchView"); await WaitForDialog<NotificationCenterWindow>(owner);
            var entries = owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Expander>().ToArray();
            Assert.True(entries[0].IsExpanded);
            Assert.Contains(entries[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Import terminé");
            Assert.Contains(entries[0].GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text?.Contains("2 importés") == true);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Toast_stays_while_keyboard_focus_is_inside_then_expires_after_leaving() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "Notify", "Projet enregistré.", false);
            var host = owner.FindControl<StackPanel>("ToastHost")!;
            var toast = Assert.Single(host.Children);
            toast.GetVisualDescendants().OfType<Button>().Single().Focus();
            await Task.Delay(5000); Dispatcher.UIThread.RunJobs();
            Assert.Contains(toast, host.Children);
            owner.FindControl<TextBox>("SearchBox")!.Focus(); Dispatcher.UIThread.RunJobs();
            await Task.Delay(5000); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(toast, host.Children);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Toasts_limit_overlap_and_open_the_full_message_without_losing_history() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var message = "Impossible de lire le fichier. " + new string('x', 600);
            Invoke(owner, "Notify", "Première opération terminée.", false);
            Invoke(owner, "Notify", "Deuxième opération terminée.", false);
            Invoke(owner, "Notify", message, true);
            Invoke(owner, "Notify", message, true);
            var host = owner.FindControl<StackPanel>("ToastHost")!;
            Assert.Equal(2, host.Children.Count);
            Assert.Equal(message, host.Children[0].Tag);
            var text = host.Children[0].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == message);
            Assert.InRange(text.MaxLines, 1, 3);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "refined-toasts-" + width); }
            host.Children[0].GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Détails").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<NotificationCenterWindow>(owner);
            var details = Assert.Single(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Expander>().Where(e => e.IsExpanded));
            Assert.Contains(details.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == message);
            DialogAction(owner, "Fermer");
            Assert.Contains(host.Children, control => control.Tag as string == "Deuxième opération terminée.");
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Completed_import_banner_is_compact_and_opens_its_saved_summary(bool error) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "StartImportBatch", 2);
            Assert.True(owner.FindControl<ProgressBar>("ImportBatchProgress")!.IsVisible);
            if (error) Invoke(owner, "RecordImportError", "piece.stl : accès refusé. Réessayez après avoir vérifié les droits du dossier.");
            Invoke(owner, "CompleteImportBatch", error ? 1 : 2, error ? 1 : 0, 2, false);
            Assert.False(owner.FindControl<ProgressBar>("ImportBatchProgress")!.IsVisible);
            Assert.False(owner.FindControl<TextBlock>("ImportBatchFile")!.IsVisible);
            var panel = owner.FindControl<Border>("ImportBatchPanel")!;
            foreach (var width in new[] { 960d, 1600d })
            {
                owner.Width = width; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.InRange(panel.Bounds.Height, 44, 100);
                await Task.Delay(160); CaptureDraftUi(owner, "refined-import-" + error + "-" + width);
            }
            Click(owner, "ImportBatchView"); await WaitForDialog<NotificationCenterWindow>(owner);
            Assert.Single(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Expander>().Where(e => e.IsExpanded));
            DialogAction(owner, "Fermer");
            Click(owner, "ImportBatchDismiss"); Assert.False(panel.IsVisible);
            Click(owner, "NotificationCenterButton"); await WaitForDialog<NotificationCenterWindow>(owner);
            Assert.Single(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Expander>());
        }
        finally { owner.Close(); }
    });
}
