using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Import_batch_edits_all_names_before_copying_and_confirms_sources_once() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "premier.txt"), Path.Combine(_root, "second.txt") };
        File.WriteAllText(sources[0], "premier"); File.WriteAllText(sources[1], "second");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<DialogWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var names = body.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(2, names.Length);
            Assert.Empty(Directory.GetFiles(directory));
            names[0].Text = "renomme.txt";
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "import-grouped-names-" + width); }
            DialogAction(owner, "Importer les 2 fichiers");
            await WaitForDialog<DialogWindow>(owner);
            Assert.Equal("premier", File.ReadAllText(Path.Combine(directory, "renomme.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(directory, "second.txt")));
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Conserver les sources").IsFocused);
            var checks = body.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Tag is string).ToArray();
            Assert.Equal(2, checks.Length);
            checks.Single(c => c.Tag as string == sources[0]).IsChecked = false;
            Assert.Null(body.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Tag is null).IsChecked);
            File.WriteAllText(sources[1], "source modifiée après copie");
            DialogAction(owner, "Supprimer les sources sélectionnées");
            await pending;
            Assert.True(File.Exists(sources[0])); Assert.True(File.Exists(sources[1]));
            Click(owner, "ImportBatchView"); await WaitForDialog<DialogWindow>(owner);
            Assert.Contains(body.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text?.Contains("source") == true);
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Import terminé");
            Assert.Single(body.GetVisualDescendants().OfType<Expander>());
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Import_batch_rejects_duplicate_targets_and_can_cancel_source_cleanup() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "un.txt"), Path.Combine(_root, "deux.txt") };
        foreach (var source in sources) File.WriteAllText(source, "contenu");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<ImportBatchWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var names = body.GetVisualDescendants().OfType<TextBox>().ToArray();
            names[1].Text = "UN.txt";
            DialogAction(owner, "Importer les 2 fichiers");
            Assert.Empty(Directory.GetFiles(directory)); Assert.False(pending.IsCompleted);
            Assert.Contains("form-invalid", names[1].Classes);
            names[1].Text = "deux.txt";
            DialogAction(owner, "Importer les 2 fichiers");
            await WaitForDialog<ConfirmImportSourcesWindow>(owner);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "import-grouped-sources-" + width); }
            var master = body.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Tag is null);
            master.Focus();
            owner.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Dispatcher.UIThread.RunJobs();
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.IsType<ConfirmImportSourcesWindow>(Field<DialogWindow>(owner, "_panel"));
            master.IsChecked = false;
            Assert.False(body.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("danger")).IsEnabled);
            DialogAction(owner, "Conserver les sources");
            await pending;
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Project_root_batch_keeps_individual_destination_choices() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Import", "CI");
        var client = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root));
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Destinations");
        var project = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root));
        var sources = new[] { Path.Combine(_root, "plan.pdf"), Path.Combine(_root, "piece.stl") };
        foreach (var source in sources) File.WriteAllText(source, Path.GetFileName(source));
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportProjectFilesAsync", _root, project, project.ProjectPath, sources)!;
            await WaitForDialog<ImportBatchWindow>(owner);
            var folders = owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Assert.Equal(2, folders.Length);
            Assert.Equal("01_DEVIS_FACTURES", folders[0].SelectedItem);
            Assert.Equal("03_CAO_3D / 03_STL", folders[1].SelectedItem);
            folders[0].SelectedItem = "02_FICHIERS_CLIENT";
            DialogAction(owner, "Garder tous les noms d’origine");
            DialogAction(owner, "Importer les 2 fichiers");
            await WaitForDialog<ConfirmImportSourcesWindow>(owner); DialogAction(owner, "Conserver les sources"); await pending;
            Assert.Equal("plan.pdf", File.ReadAllText(Path.Combine(project.ProjectPath, "02_FICHIERS_CLIENT", "plan.pdf")));
            Assert.Equal("piece.stl", File.ReadAllText(Path.Combine(project.ProjectPath, "03_CAO_3D", "03_STL", "piece.stl")));
            Assert.All(sources, source => Assert.True(File.Exists(source)));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Notification_history_is_bounded_and_clearing_requires_confirmation() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            for (var index = 0; index < 205; index++) Invoke(owner, "AddNotification", $"Résultat {index}", "Détails", false, null);
            Click(owner, "NotificationCenterButton"); await WaitForDialog<DialogWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal(200, body.GetVisualDescendants().OfType<Expander>().Count());
            Assert.DoesNotContain(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Résultat 4");
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Résultat 5");
            DialogAction(owner, "Effacer l’historique");
            var confirmation = Assert.Single(owner.OwnedWindows.OfType<ConfirmRemovalWindow>());
            confirmation.Close(false); await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.Equal(200, body.GetVisualDescendants().OfType<Expander>().Count());
            DialogAction(owner, "Effacer l’historique");
            confirmation = Assert.Single(owner.OwnedWindows.OfType<ConfirmRemovalWindow>());
            confirmation.Close(true); await Task.Delay(20); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(body.GetVisualDescendants().OfType<Expander>());
            DialogAction(owner, "Fermer");
            Assert.Contains("0 non lue", Avalonia.Automation.AutomationProperties.GetName(owner.FindControl<Button>("NotificationCenterButton")!));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Notification_center_keeps_dismissed_toasts_filters_errors_and_marks_read() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "Notify", "Copie terminée", false);
            Invoke(owner, "Notify", "Accès refusé", true);
            owner.FindControl<StackPanel>("ToastHost")!.Children.Clear();
            var button = owner.FindControl<Button>("NotificationCenterButton"); Assert.NotNull(button);
            Click(owner, "NotificationCenterButton"); await WaitForDialog<DialogWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Copie terminée");
            var filter = body.GetVisualDescendants().OfType<ComboBox>().Single(); filter.SelectedIndex = 1;
            owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Copie terminée");
            Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Accès refusé");
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "notifications-errors-" + width); }
            DialogAction(owner, "Tout marquer comme lu"); filter.SelectedIndex = 2;
            owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(body.GetVisualDescendants().OfType<Expander>(), _ => true);
            DialogAction(owner, "Fermer");
            Assert.Contains("0 non lue", Avalonia.Automation.AutomationProperties.GetName(button));
        }
        finally { owner.Close(); }
    });
}
