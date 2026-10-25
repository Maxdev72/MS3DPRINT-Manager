using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task File_browser_is_visible_during_reading_and_leaving_cancels_the_read() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Chargement")).FullName;
            for (var index = 0; index < 250; index++) File.WriteAllText(Path.Combine(folder, $"piece-{index:D4}.txt"), "test");
            var loading = OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            var browser = Assert.Single(host.Children.OfType<FileBrowserView>());
            Assert.Contains("Lecture", browser.StatusLabel.Text);
            Invoke(owner, "ShowWorkspace", "Accueil");
            var rowsAtDeparture = browser.Rows.Children.ToArray();
            await loading;
            Assert.Equal(rowsAtDeparture, browser.Rows.Children.ToArray());
            Assert.True(owner.FindControl<Control>("DashboardContent")!.IsVisible);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task File_browser_reports_an_unavailable_folder_and_can_retry_without_leaving() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var folder = Path.Combine(_root, "02_MODELES_3D", "Indisponible");
            await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            var browser = Assert.Single(host.Children.OfType<FileBrowserView>());
            Assert.Contains("interrompue", browser.StatusLabel.Text);
            Assert.False(browser.SearchInput.IsEnabled);
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "retrouve.txt"), "test");
            browser.Rows.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Réessayer")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var index = 0; index < 100 && !host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "retrouve.txt"); index++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "retrouve.txt");
            Assert.True(Assert.Single(host.Children.OfType<FileBrowserView>()).SearchInput.IsEnabled);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task File_browser_restores_filters_page_and_scroll_after_history_navigation(bool projectFolder) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            string folder; MS3DPRINT.Manager.Core.Projects.ProjectSummary? project = null;
            if (projectFolder)
            {
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Retour", "CR");
                var client = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root));
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Retour fichiers");
                project = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root));
                folder = Path.Combine(project.ProjectPath, "02_FICHIERS_CLIENT");
            }
            else folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Retour")).FullName;
            for (var index = 0; index < 125; index++) File.WriteAllText(Path.Combine(folder, $"piece-{index:D3}.pdf"), "test");
            if (project is not null) { Invoke(owner, "ShowWorkspace", "Projets"); await (Task)Invoke(owner, "LoadProjectDirectoryAsync", project, folder)!; }
            else await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>(projectFolder ? "ProjectList" : "FolderWorkspaceList")!;
            FileBrowserView Browser() => Assert.Single(host.Children.OfType<FileBrowserView>());
            var browser = Browser(); browser.SearchInput.Text = "piece"; browser.FamilyInput.SelectedItem = FileFamily.Pdf;
            browser.Rows.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var scroll = host.GetVisualAncestors().OfType<ScrollViewer>().First();
            scroll.Offset = new Avalonia.Vector(0, 380); var offset = scroll.Offset.Y;
            Assert.True(offset > 0);
            Invoke(owner, "ShowWorkspace", "Accueil");
            Assert.True(await (Task<bool>)Invoke(owner, "NavigateHistoryAsync", -1)!);
            Assert.Contains("Lecture", Browser().StatusLabel.Text);
            for (var index = 0; index < 200 && Browser().StatusLabel.Text?.Contains("Lecture") == true; index++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            browser = Browser();
            Assert.Equal("piece", browser.SearchInput.Text); Assert.Equal(FileFamily.Pdf, browser.FamilyInput.SelectedItem);
            Assert.Contains(browser.Rows.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "piece-050.pdf");
            Assert.Equal(offset, scroll.Offset.Y, precision: 0);
            // Refresh keeps context, but removes vanished entries and clamps a now missing page.
            foreach (var path in Directory.GetFiles(folder).Skip(20)) File.Delete(path);
            if (project is not null) await (Task)Invoke(owner, "LoadProjectDirectoryAsync", project, folder)!;
            else await (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
            Assert.Equal("piece", Browser().SearchInput.Text);
            Assert.Equal(20, Browser().Rows.GetVisualDescendants().OfType<Grid>().Count(r => r.Classes.Contains("workspace-row")));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task File_browser_filters_cached_entries_and_keeps_folders_and_paging(bool projectFolder) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            string folder; MS3DPRINT.Manager.Core.Projects.ProjectSummary? project = null;
            if (projectFolder)
            {
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Fichiers", "CF");
                var client = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root));
                new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Pièces techniques");
                project = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root));
                folder = Path.Combine(project.ProjectPath, "02_FICHIERS_CLIENT");
            }
            else folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Pièces techniques")).FullName;
            Directory.CreateDirectory(Path.Combine(folder, "Documents"));
            for (var index = 0; index < 70; index++) File.WriteAllText(Path.Combine(folder, $"Notice-{index:D2}.txt"), "synthetic");
            File.WriteAllText(Path.Combine(folder, "Pièce été.stl"), "solid piece\nendsolid piece");
            File.WriteAllText(Path.Combine(folder, "Plan.pdf"), "synthetic");
            if (project is not null) { Invoke(owner, "ShowWorkspace", "Projets"); await (Task)Invoke(owner, "LoadProjectDirectoryAsync", project, folder)!; }
            else await OpenFileTestFolder(owner, folder);
            var host = owner.FindControl<StackPanel>(projectFolder ? "ProjectList" : "FolderWorkspaceList")!;
            var browser = Assert.Single(host.GetVisualDescendants().OfType<FileBrowserView>());
            Assert.Equal(50, browser.Rows.GetVisualDescendants().OfType<Grid>().Count(row => row.Classes.Contains("workspace-row")));
            browser.FamilyInput.SelectedItem = FileFamily.Models; Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, browser.Rows.GetVisualDescendants().OfType<Grid>().Count(row => row.Classes.Contains("workspace-row")));
            browser.SearchInput.Focus(); browser.SearchInput.Text = "piece ete"; Dispatcher.UIThread.RunJobs();
            Assert.Single(browser.Rows.GetVisualDescendants().OfType<Grid>().Where(row => row.Classes.Contains("workspace-row")));
            Assert.True(browser.SearchInput.IsFocused);
            Assert.Contains("Pièce été.stl", string.Join(" ", browser.Rows.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
            browser.SearchInput.Text = "absent"; Dispatcher.UIThread.RunJobs();
            Assert.Contains("Aucun résultat", browser.StatusLabel.Text);
            browser.SearchInput.Text = ""; browser.FamilyInput.SelectedItem = FileFamily.All;
            var next = browser.Rows.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(23, browser.Rows.GetVisualDescendants().OfType<Grid>().Count(row => row.Classes.Contains("workspace-row")));
            browser.SearchInput.Text = "Plan"; Dispatcher.UIThread.RunJobs();
            Assert.Single(browser.Rows.GetVisualDescendants().OfType<Grid>().Where(row => row.Classes.Contains("workspace-row")));
            browser.SearchInput.Text = "";
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "files-browser-" + projectFolder + "-" + width); }
        }
        finally { owner.Close(); }
    });

    private static async Task OpenFileTestFolder(MainWindow owner, string folder)
    {
        Invoke(owner, "ShowWorkspace", "Collections");
        typeof(MainWindow).GetField("_folderWorkspaceRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(owner, folder);
        typeof(MainWindow).GetField("_folderWorkspaceDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(owner, folder);
        owner.FindControl<Control>("CollectionsWorkspacePanel")!.IsVisible = false;
        owner.FindControl<Control>("FolderWorkspacePanel")!.IsVisible = true;
        await (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
    }

    [Fact]
    public Task Recent_files_track_opening_and_preserve_history_when_the_file_disappears() => Run(async () =>
    {
        var owner = await Open(); owner.Show(); var original = DesktopPreviewLauncher.OpenDocument;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "Support client")).FullName;
            var path = Path.Combine(folder, "Plan support.pdf"); File.WriteAllText(path, "synthetic");
            var calls = 0; DesktopPreviewLauncher.OpenDocument = _ => calls++;
            await (Task)Invoke(owner, "OpenFileAsync", path)!;
            Assert.Equal(1, calls);
            Assert.Single(new RecentFileStore(_root).Load());
            Invoke(owner, "ShowWorkspace", "Accueil"); await (Task)Invoke(owner, "LoadRecentFilesAsync")!;
            var list = owner.FindControl<StackPanel>("RecentFilesList")!;
            var reopen = list.GetVisualDescendants().OfType<Button>().Single(b => b.Tag as string == path);
            reopen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var index = 0; index < 100 && calls < 2; index++) await Task.Delay(10);
            Assert.Equal(2, calls); Assert.Single(new RecentFileStore(_root).Load());
            File.Delete(path); await (Task)Invoke(owner, "LoadRecentFilesAsync")!;
            for (var index = 0; index < 200 && !list.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Indisponible") == true); index++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Indisponible") == true);
            Assert.Single(new RecentFileStore(_root).Load());
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(160); CaptureDraftUi(owner, "files-recent-" + width); }
        }
        finally { DesktopPreviewLauncher.OpenDocument = original; owner.Close(); }
    });

    [Fact]
    public Task File_browser_measurements_record_loading_layout_and_realized_rows() => Run(async () =>
    {
        var runLabel = Environment.GetEnvironmentVariable("MS3DPRINT_PROFILE_FILES");
        if (runLabel is not ("before" or "after" or "progressive")) return;
        var owner = await Open(); owner.Show();
        var results = new List<object>();
        try
        {
            foreach (var count in runLabel == "before" ? new[] { 100, 1000 } : new[] { 100, 1000, 10000 })
            {
                var folder = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D", "measure-" + count)).FullName;
                for (var index = 0; index < count; index++) File.WriteAllText(Path.Combine(folder, $"piece-{index:D5}.txt"), "synthetic");
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    Invoke(owner, "ShowWorkspace", "Collections");
                    typeof(MS3DPRINT.Manager.Desktop.MainWindow).GetField("_folderWorkspaceRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(owner, folder);
                    typeof(MS3DPRINT.Manager.Desktop.MainWindow).GetField("_folderWorkspaceDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(owner, folder);
                    owner.FindControl<Control>("CollectionsWorkspacePanel")!.IsVisible = false;
                    owner.FindControl<Control>("FolderWorkspacePanel")!.IsVisible = true;
                    GC.Collect(); var allocated = GC.GetTotalAllocatedBytes(precise: true); var watch = Stopwatch.StartNew();
                    var loading = (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
                    double? firstPageMs = null;
                    if (runLabel == "progressive")
                    {
                        while (!loading.IsCompleted)
                        {
                            owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                            if (owner.FindControl<StackPanel>("FolderWorkspaceList")!.GetVisualDescendants().OfType<Grid>().Any(r => r.Classes.Contains("workspace-row")))
                            { firstPageMs = watch.Elapsed.TotalMilliseconds; break; }
                            await Task.Delay(10);
                        }
                    }
                    await loading;
                    owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); watch.Stop();
                    firstPageMs ??= watch.Elapsed.TotalMilliseconds;
                    var rows = owner.FindControl<StackPanel>("FolderWorkspaceList")!.GetVisualDescendants().OfType<Grid>()
                        .Count(row => row.Classes.Contains("workspace-row"));
                    Assert.InRange(rows, 1, count);
                    var loadAllocations = GC.GetTotalAllocatedBytes(precise: true) - allocated;
                    double? filterMs = null;
                    if (owner.FindControl<StackPanel>("FolderWorkspaceList")!.GetVisualDescendants().OfType<FileBrowserView>().FirstOrDefault() is { } browser)
                    {
                        var filter = Stopwatch.StartNew(); browser.SearchInput.Text = "00007";
                        owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); filter.Stop(); filterMs = filter.Elapsed.TotalMilliseconds;
                        Assert.Single(browser.Rows.GetVisualDescendants().OfType<Grid>().Where(row => row.Classes.Contains("workspace-row")));
                        browser.SearchInput.Text = "";
                    }
                    results.Add(new { Count = count, Repeat = repeat, LoadAndLayoutMs = watch.Elapsed.TotalMilliseconds, FirstPageAndLayoutMs = firstPageMs, AllocatedBytes = loadAllocations, RealizedRows = rows, WorkingSetBytes = Process.GetCurrentProcess().WorkingSet64, FilterAndLayoutMs = filterMs });
                }
            }
        }
        finally
        {
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../outputs/performance")); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "files-" + runLabel + ".json"), JsonSerializer.Serialize(new { Date = DateTimeOffset.Now, Environment = new { Runtime = System.Environment.Version.ToString(), Os = System.Environment.OSVersion.ToString(), Processors = System.Environment.ProcessorCount, Framework = typeof(Control).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion }, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            owner.Close();
        }
    });
}
