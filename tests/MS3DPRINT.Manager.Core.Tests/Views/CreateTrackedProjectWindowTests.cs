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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HomonymousClientFolders_SelectIdentityOrLegacyParentPath(bool legacy)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-project-homonyms-" + Guid.NewGuid().ToString("N"));
            try
            {
                var paths = new WorkspaceMetadataPaths(root);
                var store = new ClientProfileStore(paths);
                var now = DateTimeOffset.UtcNow;
                var firstPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "GROUP", "A")).FullName;
                var secondPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "A")).FullName;
                store.Create(new(Guid.NewGuid(), ClientKind.Professional, "A", "AAA", "First", null, null, null, null, new(null, null, null, null, null), now, now,
                    RelativePath: Path.GetRelativePath(root, firstPath)));
                var second = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "B", "BBB", "Second", null, null, null, null, new(null, null, null, null, null), now, now,
                    RelativePath: Path.GetRelativePath(root, secondPath));
                store.Create(second);
                store.Update(second with { FolderName = "A" });
                var catalog = new ClientCatalog(store);
                var expected = catalog.Load(root).Single(client => client.ClientPath == secondPath);
                var projectPath = Directory.CreateDirectory(Path.Combine(secondPath, "BBB-2026-001_TEST")).FullName;
                var project = new ProjectSummary("A", secondPath, projectPath, "BBB-2026-001", "BBB-2026-001_TEST", null);
                var window = new CreateTrackedProjectWindow(root, new FolderTreeService(), catalog, new ProjectProfileStore(paths),
                    existingProject: legacy ? project : null, preselectedClient: legacy ? null : expected);
                try
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Show();
                    var box = Assert.IsType<ComboBox>(window.FindName("ClientBox"));
                    Assert.Equal(expected.Profile!.Id, Assert.IsType<ClientSummary>(box.SelectedItem).Profile!.Id);
                    if (legacy) Assert.Single(box.Items);
                }
                finally { window.Close(); }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

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

    [Fact]
    public void Wizard_ProvidesClientProjectAndConfirmationNavigation()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-project-wizard-" + Guid.NewGuid().ToString("N"));
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

                    Assert.NotNull(window.FindName("ClientStepPanel"));
                    Assert.NotNull(window.FindName("ProjectStepPanel"));
                    Assert.NotNull(window.FindName("ConfirmationStepPanel"));
                    Assert.NotNull(window.FindName("NextButton"));
                    Assert.NotNull(window.FindName("PreviousButton"));
                    Assert.NotNull(window.FindName("NewClientButton"));
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
