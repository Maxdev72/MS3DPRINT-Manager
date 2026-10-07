using System.Windows;
using System.Windows.Controls;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[Collection("Responsive layout UI")]
public sealed class CreateTrackedProjectWindowTests
{
    [Fact]
    public void RetryAfterAnUnreadableClientProfile_ReloadsClientsWithoutReopeningTheDialog()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-project-dialog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new WorkspaceMetadataPaths(root);
                paths.EnsureMetadataDirectories();
                Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ATELIER"));
                var invalidProfilePath = Path.Combine(paths.ClientsDirectory, "invalid.json");
                File.WriteAllText(invalidProfilePath, "{");
                var store = new ClientProfileStore(paths);
                var window = new CreateTrackedProjectWindow(root, new FolderTreeService(), new ClientCatalog(store), new ProjectProfileStore(paths));
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    window.UpdateLayout();

                    File.Delete(invalidProfilePath);
                    store.Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ATELIER", "AT", "Atelier", null, null, null, null,
                        new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

                    var retry = Assert.IsType<Button>(window.FindName("RetryButton"));
                    retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.UpdateLayout();

                    var clients = Assert.IsType<ComboBox>(window.FindName("ClientBox"));
                    var error = Assert.IsType<TextBlock>(window.FindName("ErrorText"));
                    Assert.True(clients.IsEnabled);
                    Assert.Single(clients.Items);
                    Assert.Empty(error.Text);
                }
                finally { window.Close(); }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        });
    }

    [Fact]
    public void OpeningWithAnUnreadableClientProfile_KeepsTheDialogOpenWithoutHidingTheRestOfTheCatalog()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-project-dialog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new WorkspaceMetadataPaths(root);
                paths.EnsureMetadataDirectories();
                Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS"));
                File.WriteAllText(Path.Combine(paths.ClientsDirectory, "invalid.json"), "{");

                var window = new CreateTrackedProjectWindow(root, new FolderTreeService(), new ClientCatalog(new ClientProfileStore(paths)), new ProjectProfileStore(paths));
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    window.UpdateLayout();

                    var error = Assert.IsType<TextBlock>(window.FindName("ErrorText"));
                    var clientBox = Assert.IsType<ComboBox>(window.FindName("ClientBox"));
                    var create = Assert.IsType<Button>(window.FindName("CreateButton"));

                    Assert.Empty(error.Text);
                    Assert.False(clientBox.IsEnabled);
                    Assert.False(create.IsEnabled);
                }
                finally { window.Close(); }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        });
    }

    [Fact]
    public void NewProject_RequiresExplicitClientSelection()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-project-dialog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new WorkspaceMetadataPaths(root);
                var store = new ClientProfileStore(paths);
                store.Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ATELIER", "AT", "Atelier", null, null, null, null,
                    new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ATELIER"));
                var window = new CreateTrackedProjectWindow(root, new FolderTreeService(), new ClientCatalog(store), new ProjectProfileStore(paths));
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    window.UpdateLayout();

                    var clients = Assert.IsType<ComboBox>(window.FindName("ClientBox"));
                    var create = Assert.IsType<Button>(window.FindName("CreateButton"));
                    Assert.Single(clients.Items);
                    Assert.Null(clients.SelectedItem);
                    Assert.False(create.IsEnabled);
                }
                finally { window.Close(); }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }
}
