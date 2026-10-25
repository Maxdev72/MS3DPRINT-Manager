using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static void FileKey(MainWindow owner, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        owner.KeyPress(key, modifiers, default, null); owner.KeyRelease(key, modifiers, default, null); Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task File_keyboard_selects_all_filtered_pages_and_escape_clears_without_touching_text_editing() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Clavier")).FullName;
            Directory.CreateDirectory(Path.Combine(folder, "Dossier"));
            for (var i = 0; i < 72; i++) File.WriteAllText(Path.Combine(folder, $"piece-{i:D2}.pdf"), "test");
            File.WriteAllText(Path.Combine(folder, "piece.txt"), "test");
            await OpenFileTestFolder(owner, folder);
            var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            browser.SearchInput.Text = "piece"; browser.FamilyInput.SelectedItem = FileFamily.Pdf;
            var first = browser.Rows.GetVisualDescendants().OfType<CheckBox>().First(); first.BringIntoView(); first.Focus();
            FileKey(owner, Key.A, RawInputModifiers.Control);
            Assert.All(browser.Rows.GetVisualDescendants().OfType<CheckBox>(), c => Assert.True(c.IsChecked));
            FileKey(owner, Key.F2);
            Assert.Null(typeof(MainWindow).GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner));
            browser.Rows.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Page suivante").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(22, browser.Rows.GetVisualDescendants().OfType<CheckBox>().Count());
            Assert.All(browser.Rows.GetVisualDescendants().OfType<CheckBox>(), c => Assert.True(c.IsChecked));
            first = browser.Rows.GetVisualDescendants().OfType<CheckBox>().First(); first.BringIntoView(); first.Focus();
            FileKey(owner, Key.Escape);
            Assert.All(browser.Rows.GetVisualDescendants().OfType<CheckBox>(), c => Assert.False(c.IsChecked));
            first.IsChecked = true;
            browser.SearchInput.BringIntoView(); browser.SearchInput.Focus();
            FileKey(owner, Key.A, RawInputModifiers.Control);
            Assert.Equal("piece", browser.SearchInput.SelectedText);
            Assert.Single(browser.Rows.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsChecked == true));
            FileKey(owner, Key.F2);
            Assert.Null(typeof(MainWindow).GetField("_panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner));
            FileKey(owner, Key.Delete);
            Assert.Equal("", browser.SearchInput.Text);
            Assert.Equal(73, Directory.GetFiles(folder).Length);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task File_keyboard_rename_and_delete_reuse_dialogs_and_cancel_preserves_the_file() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Clavier")).FullName;
            var path = Path.Combine(folder, "piece.txt"); File.WriteAllText(path, "original");
            await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            CheckBox Selected()
            {
                var check = host.GetVisualDescendants().OfType<CheckBox>().Single(); check.IsChecked = true;
                check.BringIntoView(); check.Focus(); return check;
            }
            Selected(); FileKey(owner, Key.F2); await WaitForDialog<TextPromptWindow>(owner);
            DialogAction(owner, "Annuler"); await Task.Delay(30); Assert.True(File.Exists(path));
            Selected(); FileKey(owner, Key.F2); await WaitForDialog<TextPromptWindow>(owner);
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single().Text = "renommee.txt";
            DialogAction(owner, "Enregistrer");
            var renamed = Path.Combine(folder, "renommee.txt");
            for (var i = 0; i < 200 && !host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "renommee.txt"); i++) await Task.Delay(10);
            Assert.Equal("original", File.ReadAllText(renamed)); Assert.False(File.Exists(path));
            var opener = Selected(); FileKey(owner, Key.Delete); await WaitForDialog<ConfirmTrashWindow>(owner);
            Assert.True(File.Exists(renamed)); DialogAction(owner, "Annuler"); await Task.Delay(30);
            Assert.True(opener.IsFocused);
            FileKey(owner, Key.Delete); await WaitForDialog<ConfirmTrashWindow>(owner);
            DialogAction(owner, "Envoyer à la corbeille");
            for (var i = 0; i < 200 && File.Exists(renamed); i++) await Task.Delay(10);
            Assert.False(File.Exists(renamed));
        }
        finally { owner.Close(); }
    });
}
