using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MS3DPRINT.Manager.App;
using MS3DPRINT.Manager.App.Controls;
using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.App.Views;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Views;

[Collection("Responsive layout UI")]
public sealed class AccessibleDetailTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningLegacyEntityShowsFilesWithoutWritingProfiles(bool project)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-accessible-" + Guid.NewGuid().ToString("N"));
            var clientPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME")).FullName;
            var projectPath = Directory.CreateDirectory(Path.Combine(clientPath, "ACM-2026-001_TEST")).FullName;
            File.WriteAllText(Path.Combine(project ? projectPath : clientPath, "document.txt"), "document");
            var previousRoot = Environment.GetEnvironmentVariable("MS3DPRINT_STORAGE_ROOT");
            var previousData = Environment.GetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY");
            Environment.SetEnvironmentVariable("MS3DPRINT_STORAGE_ROOT", root);
            Environment.SetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY", Path.Combine(root, "app-data"));
            var window = new MainWindow();
            var forcedForm = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                foreach (Window dialog in window.OwnedWindows)
                    if (dialog is CreateClientWindow or CreateTrackedProjectWindow) { forcedForm = true; dialog.Close(); }
            };
            try
            {
                window.ShowInTaskbar = false; window.ShowActivated = false;
                window.Show(); window.Hide();
                timer.Start();
                if (project)
                    Invoke(window, "ShowProjectDetail", new ProjectSummary("ACME", clientPath, projectPath, "ACM-2026-001", "ACM-2026-001_TEST", null), null);
                else
                    Invoke(window, "ShowClientDetailCore", new ClientSummary(clientPath, "ACME", "ACME", "ACM", null, null, 1), null);
                Assert.False(forcedForm);
                var page = Assert.IsAssignableFrom<UserControl>(((ContentControl)window.FindName("PageHost")).Content);
                var files = Assert.IsType<WorkspaceFilesView>(((ContentControl)page.FindName("FilesHost")).Content);
                var refresh = files.RefreshAsync();
                PumpUntil(() => refresh.IsCompleted);
                Assert.True(refresh.IsCompletedSuccessfully, refresh.Exception?.ToString());
                Assert.False(Directory.Exists(Path.Combine(root, ".ms3dprint-manager")));
                Assert.Equal("document", File.ReadAllText(Path.Combine(project ? projectPath : clientPath, "document.txt")));
            }
            finally
            {
                timer.Stop(); window.Close();
                Environment.SetEnvironmentVariable("MS3DPRINT_STORAGE_ROOT", previousRoot);
                Environment.SetEnvironmentVariable("MS3DPRINT_DATA_DIRECTORY", previousData);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void GuidedProjectCompletionCompletesItsParentFirstAndReturnsTheTrackedProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-guided-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clientPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME")).FullName;
            var projectPath = Directory.CreateDirectory(Path.Combine(clientPath, "ACM-2026-001_TEST")).FullName;
            File.WriteAllText(Path.Combine(projectPath, "document.txt"), "document");
            var paths = new WorkspaceMetadataPaths(root);
            var clientStore = new ClientProfileStore(paths); var projectStore = new ProjectProfileStore(paths);
            var clientCatalog = new ClientCatalog(clientStore); var projectCatalog = new ProjectCatalog(projectStore);
            var calls = new List<string>();
            var original = new ProjectSummary("ACME", clientPath, projectPath, "ACM-2026-001", "ACM-2026-001_TEST", null);
            var result = new LegacyProjectCompletionCoordinator(root, clientCatalog, projectCatalog).Complete(original,
                client =>
                {
                    calls.Add("client"); Assert.Equal(clientPath, client.ClientPath);
                    var now = DateTimeOffset.UtcNow;
                    clientStore.Create(new(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACM", "ACME", null, null, null, null, new(null, null, null, null, null), now, now, RelativePath: Path.GetRelativePath(root, clientPath)));
                    return true;
                },
                (project, client) =>
                {
                    calls.Add("project"); Assert.NotNull(client.Profile); Assert.Equal(original.Reference, project.Reference);
                    var now = DateTimeOffset.UtcNow;
                    projectStore.Create(new(Guid.NewGuid(), client.Profile.Id, "ACM", project.Reference, project.FolderName, project.ProjectName, ProjectStatus.Quote, now, null, null, null, now, Path.GetRelativePath(root, project.ProjectPath)));
                    return true;
                });
            Assert.Equal(["client", "project"], calls);
            Assert.NotNull(result); Assert.NotNull(result.Profile); Assert.Equal(projectPath, result.ProjectPath); Assert.Equal(original.Reference, result.Reference);
            Assert.Equal("document", File.ReadAllText(Path.Combine(projectPath, "document.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void GuidedProjectCompletionCancellationKeepsFilesAndDoesNotCreateAProjectProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-guided-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clientPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME")).FullName;
            var projectPath = Directory.CreateDirectory(Path.Combine(clientPath, "ACM-2026-001_TEST")).FullName;
            File.WriteAllText(Path.Combine(projectPath, "document.txt"), "document");
            var paths = new WorkspaceMetadataPaths(root);
            var projectStore = new ProjectProfileStore(paths);
            var coordinator = new LegacyProjectCompletionCoordinator(root, new ClientCatalog(new ClientProfileStore(paths)), new ProjectCatalog(projectStore));
            var projectRequested = false;
            var result = coordinator.Complete(new("ACME", clientPath, projectPath, "ACM-2026-001", "ACM-2026-001_TEST", null), _ => false, (_, _) => { projectRequested = true; return true; });
            Assert.Null(result); Assert.False(projectRequested); Assert.Empty(projectStore.LoadAll());
            Assert.Equal("document", File.ReadAllText(Path.Combine(projectPath, "document.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Invoke(object instance, string name, params object?[] arguments)
        => instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, arguments);

    [Fact]
    public void TrackedDetailsStartWithFilesAndSwitchingTabsKeepsTheDirtyDraft()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-detail-tabs-" + Guid.NewGuid().ToString("N"));
            try
            {
                var now = DateTimeOffset.UtcNow;
                var paths = new WorkspaceMetadataPaths(root);
                var client = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "ACME", "ACM", "ACME", null, null, null, null, new(null, null, null, null, null), now, now);
                var clientModel = new ClientDetailViewModel(client, new ClientProfileStore(paths));
                var clientView = new ClientDetailView(clientModel);
                var clientTabs = Assert.IsType<TabControl>(clientView.FindName("DetailTabs"));
                Assert.Equal("Fichiers", ((TabItem)clientTabs.SelectedItem).Header);
                clientTabs.SelectedIndex = 1; clientModel.CompanyName = "DRAFT"; clientTabs.SelectedIndex = 0;
                Assert.True(clientView.HasUnsavedChanges);
                var projectPath = Directory.CreateDirectory(Path.Combine(root, "01_CLIENTS", "ACME", "ACM-2026-001_TEST")).FullName;
                var profile = new ProjectProfile(Guid.NewGuid(), client.Id, "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now);
                var model = new ProjectDetailViewModel(new ProjectSummary("ACME", Path.GetDirectoryName(projectPath)!, projectPath, profile.Reference, profile.FolderName, profile), new ProjectProfileStore(paths));
                var view = new ProjectDetailView(model);
                var tabs = Assert.IsType<TabControl>(view.FindName("DetailTabs"));
                Assert.Equal("Fichiers", ((TabItem)tabs.SelectedItem).Header);
                tabs.SelectedIndex = 1; model.Description = "DRAFT"; tabs.SelectedIndex = 0;
                Assert.True(view.HasUnsavedChanges);
                Assert.False(Directory.Exists(paths.MetadataDirectory));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingDetailedFileKeepsSelectionWithoutNavigating(bool project)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-detail-selection-" + Guid.NewGuid().ToString("N"));
            try
            {
                var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
                var entry = new ProjectFileEntry("child", child, true, null, DateTimeOffset.UtcNow);
                UserControl view;
                Func<string> current;
                if (project)
                {
                    var now = DateTimeOffset.UtcNow;
                    var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now);
                    var model = new ProjectDetailViewModel(new ProjectSummary("ACME", root, root, profile.Reference, profile.FolderName, profile), new ProjectProfileStore(new(root)));
                    model.ApplyFileListing(new(root, [entry])); view = new ProjectDetailView(model); current = () => model.CurrentDirectory;
                }
                else
                {
                    var model = new CollectionDetailViewModel(new CollectionItemSummary("MODEL", root, DateTimeOffset.UtcNow));
                    model.ApplyFileListing(new(root, [entry])); view = new CollectionDetailView(model); current = () => model.CurrentDirectory;
                }
                var list = Assert.IsType<ListView>(view.FindName("FilesList"));
                view.Measure(new Size(800, 600)); view.Arrange(new Rect(0, 0, 800, 600)); view.UpdateLayout();
                PumpUntil(() => list.Items.Count == 1);
                list.SelectedItem = entry;
                Assert.Same(entry, list.SelectedItem);
                Assert.Equal(root, current());
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var completed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var start = DateTime.UtcNow;
        timer.Tick += (_, _) => { if (condition()) { completed = true; frame.Continue = false; } else if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5)) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert.True(completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeDSelectionWaitsForExplicitOpenWhichLaunchesTheSelectedFile(bool project)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ms3d-detail-open3d-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var entry = new ProjectFileEntry("model.stl", Path.Combine(root, "model.stl"), false, 0, DateTimeOffset.UtcNow);
                var launched = new List<string?>();
                UserControl view; Action open;
                if (project)
                {
                    var now = DateTimeOffset.UtcNow;
                    var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "ACM", "ACM-2026-001", "ACM-2026-001_TEST", "TEST", ProjectStatus.Quote, now, null, null, null, now);
                    var model = new ProjectDetailViewModel(new ProjectSummary("ACME", root, root, profile.Reference, profile.FolderName, profile), new ProjectProfileStore(new(root)));
                    model.ApplyFileListing(new(root, [entry]));
                    var detail = new ProjectDetailView(model, (path, _) => launched.Add(path)); view = detail; open = () => detail.OpenEntry(entry);
                }
                else
                {
                    var model = new CollectionDetailViewModel(new("MODEL", root, DateTimeOffset.UtcNow)); model.ApplyFileListing(new(root, [entry]));
                    var detail = new CollectionDetailView(model, (path, _) => launched.Add(path)); view = detail; open = () => detail.OpenEntry(entry);
                }
                view.Measure(new Size(800, 600)); view.Arrange(new Rect(0, 0, 800, 600)); view.UpdateLayout();
                var list = Assert.IsType<ListView>(view.FindName("FilesList")); PumpUntil(() => list.Items.Count == 1);
                list.SelectedItem = entry;
                Assert.Empty(launched);
                Assert.True(((Button)view.FindName("Preview3DButton")).IsEnabled);
                open();
                Assert.Equal([entry.FullPath], launched);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw failure;
    }
}
