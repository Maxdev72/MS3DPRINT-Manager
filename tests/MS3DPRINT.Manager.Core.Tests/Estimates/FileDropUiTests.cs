using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    private static DataTransfer DroppedFiles(params string[] paths)
    {
        var data = new DataTransfer();
        foreach (var path in paths)
        {
            // Headless has no OS picker. Use Avalonia's actual file-backed storage item for routed drag events.
            var directory = Directory.Exists(path);
            var type = typeof(IStorageItem).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorage" + (directory ? "Folder" : "File"))!;
            var item = (IStorageItem)Activator.CreateInstance(type, directory ? (object)new DirectoryInfo(path) : new FileInfo(path))!;
            data.Add(DataTransferItem.Create(DataFormat.File, item));
        }
        return data;
    }

    private static DragEventArgs FileDrag(FileBrowserView browser, IDataTransfer data, bool drop = false)
    {
        var args = new DragEventArgs(drop ? DragDrop.DropEvent : DragDrop.DragOverEvent, data, browser, new Point(12, 12), KeyModifiers.None)
            { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };
        browser.RaiseEvent(args); return args;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task File_drop_imports_multiple_files_through_the_existing_confirmation_flow(bool projectFolder) => Run(async () =>
    {
        var source = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms3d-drop-" + Guid.NewGuid().ToString("N"))).FullName;
        var a = Path.Combine(source, "a.txt"); var b = Path.Combine(source, "b.txt"); File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var owner = await Open(); owner.Show();
        try
        {
            string folder; MS3DPRINT.Manager.Core.Projects.ProjectSummary? project = null;
            if (projectFolder)
            {
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Dépôt", "CD");
                var client = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root));
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Dépôt fichiers");
                project = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root));
                folder = Path.Combine(project.ProjectPath, "02_FICHIERS_CLIENT");
                Invoke(owner, "ShowWorkspace", "Projets"); await (Task)Invoke(owner, "LoadProjectDirectoryAsync", project, folder)!;
            }
            else { folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Dépôt")).FullName; await OpenFileTestFolder(owner, folder); }
            var browser = owner.FindControl<StackPanel>(projectFolder ? "ProjectList" : "FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            using var data = DroppedFiles(a, b, a);
            Assert.Equal(DragDropEffects.Copy, FileDrag(browser, data).DragEffects);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(120); CaptureDraftUi(owner, "files-drop-" + projectFolder + "-" + width); }
            FileDrag(browser, data, drop: true);
            foreach (var path in new[] { a, b })
            {
                if (projectFolder) { await WaitForDialog<ImportNameWindow>(owner); DialogAction(owner, "Garder le nom d’origine"); }
                await WaitForDialog<ConfirmImportSourceWindow>(owner);
                Assert.True(File.Exists(Path.Combine(folder, Path.GetFileName(path)))); Assert.True(File.Exists(path));
                Assert.Equal(DragDropEffects.None, FileDrag(browser, data).DragEffects);
                FileDrag(browser, data, drop: true); // A second drop must not start another batch behind the confirmation.
                DialogAction(owner, "Conserver la source");
            }
            for (var i = 0; i < 200 && owner.FindControl<TextBlock>("ImportBatchHeading")!.Text != "Import terminé"; i++) await Task.Delay(10);
            Assert.Equal("Import terminé", owner.FindControl<TextBlock>("ImportBatchHeading")!.Text);
            Assert.Equal("a", File.ReadAllText(Path.Combine(folder, "a.txt"))); Assert.Equal("b", File.ReadAllText(Path.Combine(folder, "b.txt")));
            Assert.True(File.Exists(a)); Assert.True(File.Exists(b));
            Assert.Contains("2 importés", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
        }
        finally { owner.Close(); Directory.Delete(source, true); }
    });

    [Fact]
    public Task File_drop_refuses_folders_text_busy_and_stale_views() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Cible")).FullName;
            var source = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Source")).FullName;
            var path = Path.Combine(source, "a.txt"); File.WriteAllText(path, "a");
            await OpenFileTestFolder(owner, folder);
            var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            using var directories = DroppedFiles(source); Assert.Equal(DragDropEffects.None, FileDrag(browser, directories).DragEffects);
            using var mixed = DroppedFiles(source, path); Assert.Equal(DragDropEffects.None, FileDrag(browser, mixed).DragEffects);
            using var text = new DataTransfer(); text.Add(DataTransferItem.Create(DataFormat.Text, "https://example.com/model.stl"));
            Assert.Equal(DragDropEffects.None, FileDrag(browser, text).DragEffects);
            using var files = DroppedFiles(path);
            var moveOnly = new DragEventArgs(DragDrop.DragOverEvent, files, browser, new Point(12, 12), KeyModifiers.None) { DragEffects = DragDropEffects.Move };
            browser.RaiseEvent(moveOnly); Assert.Equal(DragDropEffects.None, moveOnly.DragEffects);
            var form = new CreatePrinterWindow(); var formDone = form.ShowDialog<bool>(owner); Dispatcher.UIThread.RunJobs();
            Assert.Equal(DragDropEffects.None, FileDrag(browser, files).DragEffects); FileDrag(browser, files, drop: true);
            form.Close(false); await formDone;
            Invoke(owner, "ShowWorkspace", "Accueil");
            Assert.Equal(DragDropEffects.None, FileDrag(browser, files).DragEffects); FileDrag(browser, files, drop: true);
            Assert.Empty(Directory.GetFiles(folder)); Assert.True(File.Exists(path));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task File_drop_preserves_collisions_and_deletes_a_source_only_after_explicit_consent(bool collision) => Run(async () =>
    {
        var source = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ms3d-drop-source-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(source, "a.txt"); File.WriteAllText(path, "original");
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Cible")).FullName;
            var imported = Path.Combine(folder, "a.txt"); if (collision) File.WriteAllText(imported, "existing");
            await OpenFileTestFolder(owner, folder);
            var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            using var data = DroppedFiles(path); FileDrag(browser, data, drop: true);
            if (!collision)
            {
                await WaitForDialog<ConfirmImportSourceWindow>(owner);
                Assert.Equal("original", File.ReadAllText(path)); Assert.Equal("original", File.ReadAllText(imported));
                DialogAction(owner, "Supprimer la source");
            }
            for (var i = 0; i < 200 && owner.FindControl<TextBlock>("ImportBatchHeading")!.Text != "Import terminé"; i++) await Task.Delay(10);
            Assert.Equal("Import terminé", owner.FindControl<TextBlock>("ImportBatchHeading")!.Text);
            Assert.Equal(collision, File.Exists(path));
            Assert.Equal(collision ? "existing" : "original", File.ReadAllText(imported));
            Assert.Contains(collision ? "1 échec" : "0 échec", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
        }
        finally { owner.Close(); Directory.Delete(source, true); }
    });
}
