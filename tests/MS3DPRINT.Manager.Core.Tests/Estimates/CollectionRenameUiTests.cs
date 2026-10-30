using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Moved_subfolder_history_uses_a_browse_root_that_contains_its_new_location() => Run(async () =>
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Collection A")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "STL")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Collection B")).FullName;
        File.WriteAllText(Path.Combine(source, "piece.stl"), "conservé");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenFileTestFolder(owner, root);
            var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            browser.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == source)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 200 && Field<string>(owner, "_folderWorkspaceDirectory") != source; i++) await Task.Delay(10);
            Invoke(owner, "ShowWorkspace", "Collections");
            var moved = new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(_root).MovePath(source, destination);
            Invoke(owner, "RelocateFolderWorkspace", _root, source, moved);
            Assert.True(await (Task<bool>)Invoke(owner, "NavigateHistoryAsync", -1)!);
            Assert.Equal(moved, Field<string>(owner, "_folderWorkspaceRoot"));
            Assert.Equal(moved, Field<string>(owner, "_folderWorkspaceDirectory"));
            await (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
            browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
            Assert.True(browser.SearchInput.IsEnabled);
            Assert.Equal("conservé", File.ReadAllText(Path.Combine(moved, "piece.stl")));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Collection_rename_updates_open_folder_and_history_before_import(int historyMode) => Run(async () =>
    {
        var oldRoot = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "CACHE_POT_POMME_DE_PIN")).FullName;
        Directory.CreateDirectory(Path.Combine(oldRoot, "STL"));
        File.WriteAllText(Path.Combine(oldRoot, "conserve.txt"), "conservé");
        var item = new CollectionCatalog().Load(_root, "02_MODELES_3D").Single();
        var newRoot = Path.Combine(_root, "02_MODELES_3D", "Cache Pot Pomme de pin");
        var owner = await Open(); owner.Show();
        try
        {
            await OpenFileTestFolder(owner, oldRoot);
            if (historyMode > 0)
            {
                var browser = owner.FindControl<StackPanel>("FolderWorkspaceList")!.Children.OfType<FileBrowserView>().Single();
                browser.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == Path.Combine(oldRoot, "STL"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 200 && Field<string>(owner, "_folderWorkspaceDirectory") != Path.Combine(oldRoot, "STL"); i++) await Task.Delay(10);
                if (historyMode == 2) Invoke(owner, "ShowModelViewer", new object?[] { null });
                Invoke(owner, "ShowWorkspace", "Collections");
            }
            Invoke(owner, "RenameCollection_Click", new Button { Tag = item }, new RoutedEventArgs());
            await WaitForDialog<TextPromptWindow>(owner);
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<TextBox>().Single().Text = "Cache Pot Pomme de pin";
            DialogAction(owner, "Enregistrer");
            for (var i = 0; i < 200 && !Directory.Exists(newRoot); i++) await Task.Delay(10);
            await Task.Delay(100);
            Assert.True(Directory.Exists(newRoot));
            if (historyMode > 0) Assert.True(await (Task<bool>)Invoke(owner, "NavigateHistoryAsync", -1)!);
            if (historyMode == 2)
            {
                Assert.True(owner.FindControl<Control>("ViewerWorkspacePanel")!.IsVisible);
                Invoke(owner, "CloseModelViewer");
            }
            Assert.Equal(newRoot, Field<string>(owner, "_folderWorkspaceRoot"));
            var directory = historyMode > 0 ? Path.Combine(newRoot, "STL") : newRoot;
            Assert.Equal(directory, Field<string>(owner, "_folderWorkspaceDirectory"));
            await (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
            Assert.Equal(directory, owner.FindControl<TextBlock>("FolderWorkspaceLocation")!.Text);
            var source = Path.Combine(_root, "rendu.webp"); File.WriteAllText(source, "image importée");
            var import = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, new[] { source })!;
            await WaitForDialog<ImportNameWindow>(owner); DialogAction(owner, "Importer avec ce nom");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            await import;
            Assert.Equal("image importée", File.ReadAllText(Path.Combine(directory, "rendu__CACHE_POT_POMME_DE_PIN.webp")));
            Assert.Equal("conservé", File.ReadAllText(Path.Combine(newRoot, "conserve.txt")));
            Assert.True(File.Exists(source));
            Assert.False(Directory.Exists(oldRoot));
        }
        finally { owner.Close(); }
    });
}
