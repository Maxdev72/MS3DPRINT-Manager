using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task File_selection_accepts_ctrl_shift_clicks_and_regular_click_still_opens() => Run(async () =>
    {
        var owner = await Open(); owner.Show(); var original = DesktopPreviewLauncher.OpenDocument;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Raccourcis")).FullName;
            for (var i = 0; i < 6; i++) File.WriteAllText(Path.Combine(folder, $"piece-{i}.pdf"), "test");
            await OpenFileTestFolder(owner, folder);
            var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            var opened = 0; DesktopPreviewLauncher.OpenDocument = _ => opened++;
            void Click(int index, RawInputModifiers modifiers)
            {
                owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var button = browser.Rows.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("table-row") && b.Tag as string == Path.Combine(folder, $"piece-{index}.pdf"));
                button.BringIntoView(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var point = button.TranslatePoint(new Avalonia.Point(80, button.Bounds.Height / 2), owner)!.Value;
                owner.MouseDown(point, MouseButton.Left, modifiers); owner.MouseUp(point, MouseButton.Left, modifiers); Dispatcher.UIThread.RunJobs();
            }
            int Selected() => browser.Rows.GetVisualDescendants().OfType<CheckBox>().Count(c => c.IsChecked == true);
            Click(0, RawInputModifiers.Control); Assert.Equal(1, Selected()); Assert.Equal(0, opened);
            Click(3, RawInputModifiers.Shift); Assert.Equal(4, Selected()); Assert.Equal(0, opened);
            Click(2, RawInputModifiers.Control); Assert.Equal(3, Selected());
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(140); CaptureDraftUi(owner, "files-selection-" + width); }
            Click(1, RawInputModifiers.None); Assert.Equal(1, opened); Assert.Equal(3, Selected());
        }
        finally { DesktopPreviewLauncher.OpenDocument = original; owner.Close(); }
    });

    [Fact]
    public Task File_batch_move_uses_one_destination_and_explains_partial_failures() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Déplacement")).FullName;
            var target = Directory.CreateDirectory(Path.Combine(folder, "Destination")).FullName;
            File.WriteAllText(Path.Combine(folder, "a.txt"), "a"); File.WriteAllText(Path.Combine(folder, "b.txt"), "b");
            File.WriteAllText(Path.Combine(target, "a.txt"), "existing");
            await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            var browser = host.Children.OfType<FileBrowserView>().Single();
            foreach (var check in browser.Rows.GetVisualDescendants().OfType<CheckBox>()) check.IsChecked = true;
            browser.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Déplacer la sélection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<FolderPickerWindow>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            owner.FindControl<Border>("SidePanelBody")!.GetLogicalDescendants().OfType<Button>().Single(b => b.Content is Control c && c.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Destination"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DialogAction(owner, "Déplacer ici");
            await WaitForDialog<FileBatchResultWindow>(owner);
            Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.True(File.Exists(Path.Combine(folder, "a.txt"))); Assert.False(File.Exists(Path.Combine(folder, "b.txt")));
            Assert.Equal("b", File.ReadAllText(Path.Combine(target, "b.txt")));
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(140); CaptureDraftUi(owner, "files-batch-result-" + width); }
            DialogAction(owner, "Fermer");
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task File_selection_survives_paging_but_filters_clear_it_and_trash_requires_confirmation() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Lot")).FullName;
            Directory.CreateDirectory(Path.Combine(folder, "Sous-dossier"));
            for (var i = 0; i < 55; i++) File.WriteAllText(Path.Combine(folder, $"piece-{i:D2}.txt"), "test");
            await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            var browser = Assert.Single(host.Children.OfType<FileBrowserView>());
            var first = Assert.Single(browser.Rows.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Tag as string == Path.Combine(folder, "piece-00.txt")));
            first.IsChecked = true;
            Button Action(string name) => browser.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == name);
            Assert.True(Action("Mettre la sélection à la corbeille").IsEffectivelyVisible);
            Action("Page suivante").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            browser.Rows.GetVisualDescendants().OfType<CheckBox>().First().IsChecked = true;
            Action("Page précédente").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(browser.Rows.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Tag as string == Path.Combine(folder, "piece-00.txt")).IsChecked);
            browser.SearchInput.Text = "piece-0";
            Assert.False(Action("Mettre la sélection à la corbeille").IsEffectivelyVisible);
            Assert.All(browser.Rows.GetVisualDescendants().OfType<CheckBox>(), c => Assert.False(c.IsChecked));
            foreach (var check in browser.Rows.GetVisualDescendants().OfType<CheckBox>().Take(2)) check.IsChecked = true;
            Action("Mettre la sélection à la corbeille").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmTrashWindow>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.True(owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Annuler").IsFocused);
            Assert.True(File.Exists(Path.Combine(folder, "piece-00.txt")));
            DialogAction(owner, "Annuler"); await Task.Delay(50);
            Assert.True(File.Exists(Path.Combine(folder, "piece-00.txt")));
            Action("Mettre la sélection à la corbeille").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmTrashWindow>(owner);
            DialogAction(owner, "Envoyer à la corbeille");
            for (var i = 0; i < 200 && File.Exists(Path.Combine(folder, "piece-00.txt")); i++) await Task.Delay(10);
            Assert.False(File.Exists(Path.Combine(folder, "piece-00.txt")));
            for (var i = 0; i < 200 && Directory.GetFiles(folder).Length != 53; i++) await Task.Delay(10);
            Assert.Equal(53, Directory.GetFiles(folder).Length);
            Assert.False(File.Exists(Path.Combine(folder, "piece-01.txt")));
        }
        finally { owner.Close(); }
    });
}
