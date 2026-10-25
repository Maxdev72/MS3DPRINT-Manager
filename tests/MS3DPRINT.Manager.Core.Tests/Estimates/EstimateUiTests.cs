using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateTestApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        foreach (var file in new[] { "Theme.axaml", "Controls.axaml", "Brand.axaml", "Workspace.axaml" })
            Styles.Add(new StyleInclude(new Uri("avares://MS3DPRINT.Manager.Desktop/")) { Source = new Uri("avares://MS3DPRINT.Manager.Desktop/Styles/" + file) });
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<EstimateTestApplication>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false });
}

[Collection("WorkspaceEnvironment")]
public sealed partial class EstimateUiTests : IDisposable
{
    [Fact]
    public Task Backup_settings_create_archive_and_restore_requires_preview_confirmation() => Run(async () =>
    {
        var exports = Path.Combine(Path.GetTempPath(), "ms3d-backup-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exports);
        var owner = await Open(); owner.Show();
        try
        {
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Sauvegarde", "CS");
            var client = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root));
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Support sauvegardé", ProjectPriority.High);
            var project = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root));
            var inputs = new EstimateInputs(MaterialCost: 10, MarginPercent: 20, EngineVersion: 2, V2: new());
            var quote = new EstimateProfile(Guid.NewGuid(), "Devis sauvegardé", "PLA", "Atelier", inputs, EstimateCalculator.Calculate(inputs), DateTimeOffset.Now, ProjectReference: project.Reference, ClientName: client.FolderName);
            new EstimateStore(_root).Create(quote);
            Invoke(owner, "ShowWorkspace", "Paramètres");
            await (Task)Invoke(owner, "LoadSettingsAsync")!;
            Text(owner, "SettingsSearchBox", "sauvegarde");
            var visible = owner.FindControl<StackPanel>("StorageList")!.Children.Where(c => c.IsVisible).ToArray();
            Assert.Single(visible);
            Assert.Contains(visible[0].GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Sauvegarde et restauration");
            var archive = Path.Combine(exports, "backup.zip");
            Assert.True(await (Task<bool>)Invoke(owner, "SaveWorkspaceBackupAsync", _root, archive, false)!);
            Assert.True(File.Exists(archive));
            Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager/last-backup.json")));
            Text(owner, "SettingsSearchBox", "sauvegarde");
            Assert.Contains(owner.FindControl<StackPanel>("StorageList")!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Dernière sauvegarde réussie") == true);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "backup-settings-" + width); }
            var destination = Path.Combine(exports, "restored");
            var cancelled = (Task<bool>)Invoke(owner, "RestoreWorkspaceBackupAsync", _root, archive, destination)!;
            await WaitForDialog<DialogWindow>(owner);
            Assert.False(Directory.Exists(destination));
            DialogAction(owner, "Annuler"); Assert.False(await cancelled);
            Assert.False(Directory.Exists(destination));
            var confirmed = (Task<bool>)Invoke(owner, "RestoreWorkspaceBackupAsync", _root, archive, destination)!;
            await WaitForDialog<DialogWindow>(owner);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "backup-preview-" + width); }
            DialogAction(owner, "Restaurer la copie"); Assert.True(await confirmed);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(destination, ".ms3dprint-manager/filaments")));
            var restoredClient = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(destination));
            var restoredProject = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(destination));
            Assert.Equal(client.Profile!.Id, restoredClient.Profile!.Id);
            Assert.Equal(project.Profile!.Id, restoredProject.Profile!.Id);
            Assert.Equal(ProjectPriority.High, restoredProject.Profile.Priority);
            Assert.StartsWith(destination, restoredProject.ProjectPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(quote, Assert.Single(new EstimateStore(destination).LoadAll()));
            Assert.Equal("CS", new MS3DPRINT.Manager.Core.Storage.ClientCodeRegistry(Path.Combine(destination, "data")).GetCode(client.FolderName));
            Assert.Equal(_root, Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE"));
        }
        finally { owner.Close(); Directory.Delete(exports, true); }
    });

    private static readonly string CaptureRun = "run-" + Guid.NewGuid().ToString("N");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-estimate-ui-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldWorkspace = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE");
    private readonly string? _oldWindowFile = Environment.GetEnvironmentVariable("MS3DPRINT_WINDOW_FILE");
    public EstimateUiTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root);
        Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", Path.Combine(_root, "window.json"));
        var now = DateTimeOffset.UtcNow;
        new FilamentStore(_root).Create(new(Guid.NewGuid(), "Test", "PLA", "PLA", 20, false, now, now));
        new PrinterStore(_root).Create(new(Guid.NewGuid(), "Test", "A", "Machine A", 2, "", true, now, now, PowerWatts: 200));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _oldWorkspace);
        Environment.SetEnvironmentVariable("MS3DPRINT_WINDOW_FILE", _oldWindowFile);
        Directory.Delete(_root, true);
    }
    private static object? Invoke(MainWindow window, string method, params object?[] args)
    {
        Dispatcher.UIThread.RunJobs();
        return typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
    }
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private static void Text(MainWindow window, string name, string value) { window.FindControl<TextBox>(name)!.Text = value; Dispatcher.UIThread.RunJobs(); }
    private static void Click(MainWindow window, string name) { Dispatcher.UIThread.RunJobs(); window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static async Task<T> WaitForDialog<T>(MainWindow owner) where T : DialogWindow
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (typeof(MainWindow).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner) is T dialog) return dialog;
            await Task.Delay(10);
        }
        throw new TimeoutException("Confirmation attendue : " + typeof(T).Name);
    }
    private static void DialogAction(MainWindow owner, string label)
        => owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Run(Func<Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EstimateTestApplication));
        await session.Dispatch(async () => { await test(); return true; }, CancellationToken.None);
    }
    [Fact]
    public Task Submitted_field_error_is_visible_accessible_and_clears_when_edited() => Run(() =>
    {
        var input = new TextBox(); var field = FormKit.Field("Poids", input, "g", "Poids fourni par le slicer.");
        var other = new TextBox(); var otherField = FormKit.Field("Durée", other, "h");
        var error = FormKit.ErrorText(); var window = new Window { Width = 500, Height = 500, Content = new StackPanel { Children = { field, otherField, error } } }; window.Show();
        try
        {
            var originalHelp = Avalonia.Automation.AutomationProperties.GetHelpText(input);
            FormKit.ShowError(error, input, "Le poids doit être positif."); Dispatcher.UIThread.RunJobs();
            Assert.Same(input, window.FocusManager!.GetFocusedElement());
            Assert.Contains("Le poids doit être positif.", Avalonia.Automation.AutomationProperties.GetHelpText(input));
            Assert.Contains(field.Children.OfType<TextBlock>(), t => t.IsVisible && t.Text == "Le poids doit être positif.");
            Assert.Contains("form-invalid", input.Classes);
            input.Text = "42";
            Assert.False(error.IsVisible); Assert.Equal(originalHelp, Avalonia.Automation.AutomationProperties.GetHelpText(input));
            Assert.DoesNotContain("form-invalid", input.Classes);
            Assert.DoesNotContain(field.Children.OfType<TextBlock>(), t => t.IsVisible && t.Text == "Le poids doit être positif.");
            input.Text = "43"; Assert.False(error.IsVisible);
            FormKit.ShowError(error, input, "Poids invalide.");
            FormKit.ShowError(error, other, "Durée invalide."); Dispatcher.UIThread.RunJobs();
            Assert.Same(other, window.FocusManager!.GetFocusedElement());
            Assert.Equal(originalHelp, Avalonia.Automation.AutomationProperties.GetHelpText(input));
            Assert.Contains("Durée invalide", Avalonia.Automation.AutomationProperties.GetItemStatus(other));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Client_validation_returns_to_the_invalid_field_with_inline_feedback() => Run(async () =>
    {
        var owner = await Open(); owner.Show(); var dialog = new CreateClientWindow(); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            await Task.Delay(260); Dispatcher.UIThread.RunJobs(); DialogAction(owner, "Créer le client"); Dispatcher.UIThread.RunJobs();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var invalid = Assert.Single(body.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("form-invalid")));
            Assert.Equal("Nom de l’entreprise", Avalonia.Automation.AutomationProperties.GetName(invalid));
            Assert.Same(invalid, owner.FocusManager!.GetFocusedElement());
            CaptureDraftUi(owner, "accessible-client-error");
            invalid.Text = "Client de test"; Dispatcher.UIThread.RunJobs(); Assert.DoesNotContain("form-invalid", invalid.Classes);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Document_toolbar_fits_narrow_windows_and_exposes_zoom_actions() => Run(async () =>
    {
        var path = Path.Combine(_root, "document-test.png");
        using (var image = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(1000, 600), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888)) image.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        var preview = new MS3DPRINT.Manager.Windows.App.DocumentPreviewWindow(path);
        T PreviewField<T>(string name) => (T)preview.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        preview.Show(); await Task.Delay(120); Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (var name in new[] { "_previous", "_next", "_print" }) PreviewField<Button>(name).IsVisible = true;
            PreviewField<TextBlock>("_pageText").IsVisible = true;
            PreviewField<TextBlock>("_pageText").Text = "1000 / 9999";
            foreach (var width in new[] { 520d, 960d })
            {
                preview.Width = width; await Task.Delay(180); Dispatcher.UIThread.RunJobs(); preview.UpdateLayout();
                foreach (var name in new[] { "_previous", "_next", "_zoomOut", "_zoomIn", "_print" })
                {
                    var button = PreviewField<Button>(name); var point = button.TranslatePoint(default, preview)!.Value;
                    Assert.InRange(point.X + button.Bounds.Width, 1, preview.ClientSize.Width - 10);
                }
                CaptureDraftUi(preview, "document-toolbar-" + width);
            }
            Assert.Equal("Agrandir le document", Avalonia.Automation.AutomationProperties.GetName(PreviewField<Button>("_zoomIn")));
            Assert.Equal("Réduire le document", Avalonia.Automation.AutomationProperties.GetName(PreviewField<Button>("_zoomOut")));
            var before = PreviewField<TextBlock>("_zoomText").Text;
            PreviewField<Button>("_zoomIn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotEqual(before, PreviewField<TextBlock>("_zoomText").Text);
        }
        finally
        {
            var previous = PreviewField<Image>("_image").Source as Avalonia.Media.Imaging.Bitmap;
            preview.Close();
            Assert.Null(PreviewField<Image>("_image").Source);
            if (previous is not null) { using var output = new MemoryStream(); Assert.ThrowsAny<Exception>(() => previous.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default)); }
        }
    });

    [Fact]
    public Task Form_fields_expose_their_units_hints_and_polite_errors() => Run(() =>
    {
        var input = new TextBox();
        var field = FormKit.Field("Heures annuelles", input, "h/an", "Uniquement les heures de production.");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(input)!;
        Assert.Equal("Heures annuelles", peer.GetName());
        Assert.Contains("h/an", peer.GetHelpText());
        Assert.Contains("Uniquement les heures de production.", peer.GetHelpText());
        Assert.Same(field.Children[0], Avalonia.Automation.AutomationProperties.GetLabeledBy(input));
        var error = FormKit.ErrorText();
        Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, Avalonia.Automation.AutomationProperties.GetLiveSetting(error));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Dashboard_deadlines_keep_the_reference_and_badge_below_long_project_names() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var projects = Enumerable.Range(1, 2).Select(i =>
            {
                var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "TEST", "TEST-2026-00" + i, "Projet", "Support de fixation pour la machine de production — variante " + i,
                    MS3DPRINT.Manager.Core.Projects.ProjectStatus.InProgress, DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today), null, null, DateTimeOffset.UtcNow);
                return new ProjectSummary("Client de démonstration", _root, _root, profile.Reference, "Projet", profile);
            }).ToArray();
            Invoke(owner, "BuildAttention", new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceDashboard(1, 2, 0, 2, 0, projects, projects, 0, 0));
            var list = owner.FindControl<StackPanel>("AttentionList")!;
            owner.UpdateLayout();
            var first = (Button)list.Children[0];
            var title = first.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == projects[0].Profile!.ProjectName);
            Assert.Equal(2, title.MaxLines); Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            var reference = first.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == projects[0].Reference);
            var badge = first.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("badge"));
            Assert.True(badge.TranslatePoint(default, first)!.Value.Y >= reference.TranslatePoint(default, first)!.Value.Y + reference.Bounds.Height);
            foreach (var width in new[] { 1100d, 1600d }) { owner.Width = width; await Task.Delay(200); CaptureDraftUi(owner, "readable-deadlines-" + width); }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Dashboard_priorities_are_visible_and_named_in_both_project_lists() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            var projects = new[] { ProjectPriority.High, ProjectPriority.Medium, ProjectPriority.Low, ProjectPriority.None }.Select((priority, i) =>
            {
                var profile = new ProjectProfile(Guid.NewGuid(), Guid.NewGuid(), "TEST", "TEST-2026-00" + i, "Projet", "Support de fixation pour la machine de production — variante " + i,
                    ProjectStatus.InProgress, DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today), null, null, DateTimeOffset.UtcNow) { Priority = priority };
                return new ProjectSummary("Client de démonstration", _root, _root, profile.Reference, "Projet", profile);
            }).ToArray();
            var dashboard = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceDashboard(1, 4, 0, 4, 0, projects, projects, 0, 0) { WorkQueue = projects };
            Invoke(owner, "BuildWorkQueue", dashboard); Invoke(owner, "BuildAttention", dashboard);
            foreach (var name in new[] { "RecentProjectsList", "AttentionList" })
            {
                var rows = owner.FindControl<StackPanel>(name)!.Children.OfType<Button>().ToArray();
                Assert.Equal(4, rows.Length);
                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    Assert.Contains(projects[i].Reference, Avalonia.Automation.AutomationProperties.GetName(row));
                    var flags = row.GetLogicalDescendants().OfType<Control>().Where(c => c.Classes.Contains("dashboard-priority")).ToArray();
                    if (projects[i].Priority == ProjectPriority.None) Assert.Empty(flags);
                    else
                    {
                        var flag = Assert.Single(flags);
                        Assert.Equal(ProjectPriorityVisual.Text(projects[i].Priority), Avalonia.Automation.AutomationProperties.GetName(flag));
                        Assert.Contains(ProjectPriorityVisual.Text(projects[i].Priority), Avalonia.Automation.AutomationProperties.GetName(row));
                    }
                }
            }
            foreach (var width in new[] { 1100d, 1600d }) { owner.Width = width; await Task.Delay(200); CaptureDraftUi(owner, "dashboard-priorities-" + width); }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Empty_collection_category_offers_a_working_return_to_all_collections() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceCollectionCreator().Create(_root, "02_MODELES_3D", "Support de démonstration");
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Collections");
            typeof(MainWindow).GetField("_collectionCategoryFilter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, "06_FOURNISSEURS");
            await (Task)Invoke(owner, "LoadCollectionsAsync")!;
            var list = owner.FindControl<StackPanel>("CollectionList")!;
            var reset = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Voir toutes les collections"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !list.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Support de démonstration"); i++) await Task.Delay(10);
            Assert.Equal("", Field<string>(owner, "_collectionCategoryFilter"));
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Support de démonstration");
            Assert.Contains(owner.FindControl<StackPanel>("CollectionFilters")!.Children.OfType<Button>(), b => b.Tag as string == "" && b.Classes.Contains("selected"));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Folder_read_error_has_copyable_details_and_retry_recovers_when_the_folder_returns() => Run(async () =>
    {
        var folder = Path.Combine(_root, "dossier-indisponible");
        var owner = await Open(); owner.Show();
        try
        {
            typeof(MainWindow).GetField("_folderWorkspaceRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, _root);
            typeof(MainWindow).GetField("_folderWorkspaceDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, folder);
            owner.FindControl<Border>("FolderWorkspacePanel")!.IsVisible = true;
            await (Task)Invoke(owner, "LoadFolderWorkspaceAsync")!;
            var list = owner.FindControl<StackPanel>("FolderWorkspaceList")!;
            var retry = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Réessayer"));
            var detail = Assert.Single(list.GetLogicalDescendants().OfType<SelectableTextBlock>());
            Assert.False(detail.IsEffectivelyVisible);
            var toggle = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Afficher le détail technique"));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(detail.IsEffectivelyVisible); Assert.False(string.IsNullOrWhiteSpace(detail.Text));
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "folder-read-error-" + width); }
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "document.txt"), "test");
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !list.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "document.txt"); i++) await Task.Delay(10);
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "document.txt");
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == "Réessayer");
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Trash_reports_unreadable_entries_and_keeps_healthy_entries_restorable(bool healthy) => Run(async () =>
    {
        var damaged = Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager", "trash", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(damaged, "manifest.json"), "{");
        var original = Path.Combine(_root, "a-recuperer.txt");
        if (healthy) { File.WriteAllText(original, "document conservé"); new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root).Trash(original); }
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Corbeille"); await (Task)Invoke(owner, "LoadTrashAsync")!;
            var list = owner.FindControl<StackPanel>("TrashList")!;
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Certaines entrées sont illisibles");
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "La corbeille est vide");
            Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Réessayer"));
            if (healthy)
            {
                CaptureDraftUi(owner, "trash-readable-neighbor");
                Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Restaurer")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && !File.Exists(original); i++) await Task.Delay(10);
                Assert.Equal("document conservé", File.ReadAllText(original));
            }
            Assert.True(File.Exists(Path.Combine(damaged, "manifest.json")));
            await (Task)Invoke(owner, "LoadTrashAsync")!;
            CaptureDraftUi(owner, "trash-unreadable-" + healthy);
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Inventory_reports_unreadable_profiles_and_retry_recovers_without_losing_healthy_rows(bool printer) => Run(async () =>
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(_root, ".ms3dprint-manager", printer ? "printers" : "filaments");
        var damaged = Path.Combine(directory, id + ".json"); File.WriteAllText(damaged, "{");
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", printer ? "Imprimantes" : "Filaments");
            await (Task)Invoke(owner, printer ? "LoadPrintersAsync" : "LoadFilamentsAsync")!;
            var list = owner.FindControl<StackPanel>(printer ? "PrinterList" : "FilamentList")!;
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Certaines fiches sont illisibles");
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == (printer ? "Test A" : "Test · PLA"));
            Assert.Equal("{", File.ReadAllText(damaged));
            var retry = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Réessayer"));
            if (printer)
            {
                var original = new PrinterStore(_root).LoadReadable().Single();
                File.WriteAllText(damaged, System.Text.Json.JsonSerializer.Serialize(original with { Id = id, Name = "Référence réparée", Model = "Référence réparée" }));
            }
            else
            {
                var original = new FilamentStore(_root).LoadReadable().Single();
                File.WriteAllText(damaged, System.Text.Json.JsonSerializer.Serialize(original with { Id = id, Name = "Référence réparée" }));
            }
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !list.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Référence réparée") == true); i++) await Task.Delay(10);
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Référence réparée") == true);
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Certaines fiches sont illisibles");
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Inventory_warning_survives_paging_and_never_calls_an_unreadable_catalogue_empty() => Run(async () =>
    {
        var store = new FilamentStore(_root); var original = store.LoadReadable().Single();
        for (var i = 0; i < 55; i++) store.Create(original with { Id = Guid.NewGuid(), Name = "Matière " + i });
        var damaged = Path.Combine(_root, ".ms3dprint-manager", "filaments", Guid.NewGuid() + ".json"); File.WriteAllText(damaged, "{");
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Filaments"); await (Task)Invoke(owner, "LoadFilamentsAsync")!;
            var list = owner.FindControl<StackPanel>("FilamentList")!;
            list.GetLogicalDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Certaines fiches sont illisibles");
            Assert.Equal(9, list.Children.Count); // Six remaining rows, header and two navigation bars.
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "inventory-read-warning-" + width); }
            foreach (var path in Directory.GetFiles(Path.GetDirectoryName(damaged)!, "*.json")) File.WriteAllText(path, "{");
            await (Task)Invoke(owner, "LoadFilamentsAsync")!;
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Certaines fiches sont illisibles");
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Aucun filament enregistré");
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Failed_catalog_read_keeps_its_error_when_filters_change_and_retry_restores_the_catalog(bool projects) => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier cache", "CACHE");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Pièce cache");
        var owner = await Open(); owner.Show();
        var metadata = Path.Combine(_root, ".ms3dprint-manager", projects ? "projects" : "clients");
        var saved = Path.Combine(_root, "fiches-preservees");
        var target = Directory.CreateDirectory(Path.Combine(_root, "cible-de-test")).FullName;
        var moved = false; var linked = false;
        try
        {
            var page = projects ? "Projets" : "Clients";
            var load = projects ? "LoadProjectsAsync" : "LoadClientsAsync";
            Invoke(owner, "ShowWorkspace", page); await (Task)Invoke(owner, load)!;
            var list = owner.FindControl<StackPanel>(projects ? "ProjectList" : "ClientList")!;
            var expected = projects ? "Pièce cache" : "Atelier cache";
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == expected);
            Assert.False(Path.GetRelativePath(_root, metadata).StartsWith("..")); Assert.False(Path.GetRelativePath(_root, saved).StartsWith(".."));
            Directory.Move(metadata, saved); moved = true;
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{metadata}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            { process.WaitForExit(); linked = process.ExitCode == 0; Assert.True(linked); }
            await (Task)Invoke(owner, load)!;
            Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Réessayer"));
            Assert.False(owner.FindControl<Control>(projects ? "ProjectFilterBar" : "ClientFilterBar")!.IsEnabled);
            owner.FindControl<TextBox>(projects ? "ProjectFilterBox" : "ClientFilterBox")!.Text = "cache";
            Invoke(owner, projects ? "RenderProjects" : "RenderClients");
            var retry = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Réessayer"));
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == expected);
            Directory.Delete(metadata); linked = false; Directory.Move(saved, metadata); moved = false;
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !list.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == expected); i++) await Task.Delay(10);
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == expected);
            Assert.True(owner.FindControl<Control>(projects ? "ProjectFilterBar" : "ClientFilterBar")!.IsEnabled);
            Assert.Equal("cache", owner.FindControl<TextBox>(projects ? "ProjectFilterBox" : "ClientFilterBox")!.Text);
        }
        finally
        {
            if (linked) Directory.Delete(metadata);
            if (moved) Directory.Move(saved, metadata);
            owner.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Restore_shows_pending_work_blocks_duplicate_requests_and_releases_its_state(bool conflict) => Run(async () =>
    {
        var path = Path.Combine(_root, "document-a-restaurer.txt"); File.WriteAllText(path, "document original");
        var service = new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root); var entry = service.Trash(path);
        if (conflict) File.WriteAllText(path, "nouveau document");
        var gate = service.GetType().GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Corbeille"); await (Task)Invoke(owner, "LoadTrashAsync")!;
            var button = owner.FindControl<StackPanel>("TrashList")!.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Restaurer");
            System.Threading.Monitor.Enter(gate);
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Invoke(owner, "RestoreTrash_Click", new Button { Tag = entry.Id }, new RoutedEventArgs());
                Assert.False(button.IsEnabled); Assert.Equal("Restauration…", button.Content);
                Assert.True(owner.FindControl<ProgressBar>("BusyBar")!.IsVisible);
                var status = owner.FindControl<Border>("OperationsPanel"); Assert.NotNull(status); Assert.True(status.IsVisible);
                Assert.Single(owner.FindControl<StackPanel>("OperationsList")!.Children);
                owner.Width = 960; CaptureDraftUi(owner, "restore-pending-" + conflict);
                if (!conflict)
                {
                    Invoke(owner, "ShowWorkspace", "Clients");
                    Field<TextBlock>(owner, "_trashPageSummary").Text = "Contexte quitté";
                }
            }
            finally { System.Threading.Monitor.Exit(gate); }
            for (var i = 0; i < 200 && Field<HashSet<string>>(owner, "_pendingOperations").Count > 0; i++) await Task.Delay(10);
            await Task.Delay(80);
            Assert.Empty(Field<HashSet<string>>(owner, "_pendingOperations"));
            Assert.False(owner.FindControl<Border>("OperationsPanel")!.IsVisible);
            Assert.False(owner.FindControl<ProgressBar>("BusyBar")!.IsVisible);
            Assert.Equal(conflict ? "nouveau document" : "document original", File.ReadAllText(path));
            if (conflict)
            {
                await (Task)Invoke(owner, "LoadTrashAsync")!;
                Assert.True(owner.FindControl<StackPanel>("TrashList")!.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Restaurer").IsEnabled);
                Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceTrashReader().Load(_root));
            }
            else
            {
                Assert.True(owner.FindControl<Border>("ClientsWorkspacePanel")!.IsVisible);
                Assert.Equal("Contexte quitté", Field<TextBlock>(owner, "_trashPageSummary").Text);
                Assert.Empty(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceTrashReader().Load(_root));
            }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Client_project_import_and_quote_flow_preserves_links_files_and_saved_prices() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "CreateClient_Click", null, new RoutedEventArgs());
            var clientDialog = await WaitForDialog<CreateClientWindow>(owner);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ((CheckBox)typeof(CreateClientWindow).GetField("_individual", flags)!.GetValue(clientDialog)!).IsChecked = true;
            ((TextBox)typeof(CreateClientWindow).GetField("_firstName", flags)!.GetValue(clientDialog)!).Text = "Camille";
            ((TextBox)typeof(CreateClientWindow).GetField("_lastName", flags)!.GetValue(clientDialog)!).Text = "Atelier";
            DialogAction(owner, "Créer le client");
            var clients = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader();
            for (var i = 0; i < 100 && clients.Load(_root).Count == 0; i++) await Task.Delay(10);
            var client = Assert.Single(clients.Load(_root));
            Invoke(owner, "CreateProjectForClient_Click", new Button { Tag = client }, new RoutedEventArgs());
            var projectDialog = await WaitForDialog<CreateProjectWindow>(owner);
            ((TextBox)typeof(CreateProjectWindow).GetField("_name", flags)!.GetValue(projectDialog)!).Text = "Support de démonstration";
            DialogAction(owner, "Créer le projet");
            var projects = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader();
            for (var i = 0; i < 100 && projects.Load(_root).Count == 0; i++) await Task.Delay(10);
            var project = Assert.Single(projects.Load(_root));
            await (Task)Invoke(owner, "LoadProjectDirectoryAsync", project, project.ProjectPath)!;
            var directory = Directory.CreateDirectory(Path.Combine(project.ProjectPath, "pieces")).FullName;
            var source = Path.Combine(_root, "support.stl"); File.WriteAllText(source, "solid support\nendsolid support");
            var import = (Task)Invoke(owner, "ImportProjectFilesAsync", _root, project, directory, new[] { source })!;
            await WaitForDialog<ImportNameWindow>(owner); DialogAction(owner, "Garder le nom d’origine");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source"); await import;
            Assert.Equal(File.ReadAllText(source), File.ReadAllText(Path.Combine(directory, "support.stl")));
            await (Task)Invoke(owner, "StartProjectEstimateAsync", project)!;
            Text(owner, "EstimateQuantity", "2"); Text(owner, "EstimateWeight", "10"); Text(owner, "EstimateHours", "1");
            Assert.True((bool)Invoke(owner, "CalculateEstimate")!);
            Click(owner, "EstimateSaveButton"); Click(owner, "EstimateSaveButton");
            var estimates = new EstimateStore(_root);
            for (var i = 0; i < 100 && estimates.LoadReadable().Count == 0; i++) await Task.Delay(10);
            var quote = Assert.Single(estimates.LoadReadable());
            Assert.Equal(project.Reference, quote.ProjectReference); Assert.Equal(project.ClientFolderName, quote.ClientName);
            Assert.Equal(2, quote.Inputs.Quantity); Assert.True(quote.Result.FinalPrice > 0);
            Invoke(owner, "LoadSavedEstimate_Click", new Button { Tag = quote }, new RoutedEventArgs());
            Assert.Equal(quote.Result.FinalPrice, Field<EstimateResult>(owner, "_latestEstimateResult").FinalPrice);
            Assert.Equal(project.Reference, Assert.IsType<ProjectSummary>(owner.FindControl<ComboBox>("EstimateProject")!.SelectedItem).Reference);
            Assert.True(File.Exists(source)); Assert.True(File.Exists(Path.Combine(directory, "support.stl")));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Filament_material_choices_match_the_26_materials_in_the_current_site_guide() => Run(() =>
    {
        var dialog = new CreateFilamentWindow();
        try
        {
            var material = (ComboBox)typeof(CreateFilamentWindow).GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Assert.Equal(new[] { "PLA", "PLA-CF", "PLA-GF", "PETG", "PETG-CF", "PETG-ESD", "ABS", "ASA", "PC", "PC-FR", "PC-ABS", "PC-PBT", "PET-CF", "ABS-GF", "ASA-CF", "PET-GF", "PA6-GF", "PA6-CF", "PA12-CF", "PA612-CF", "CoPA", "PA612-ESD", "PPS-CF", "PPS-GF", "TPU 90A", "TPU 95A" }, material.Items.Cast<string>());
            Assert.True(material.IsEditable); Assert.Equal("PLA", dialog.Material);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("PLA Silk")]
    [InlineData("PA6 (Nylon)")]
    [InlineData("Matière personnalisée")]
    public Task Editing_a_filament_preserves_materials_outside_the_site_catalogue(string name) => Run(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var original = new FilamentProfile(Guid.NewGuid(), "Marque", "Bobine", name, 48.59m, false, now, now);
        var dialog = new CreateFilamentWindow(original);
        try
        {
            var material = (ComboBox)typeof(CreateFilamentWindow).GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Assert.Equal(name, material.Text); Assert.Equal(name, dialog.Material);
            material.Text = "Autre formulation";
            Assert.Equal("Autre formulation", dialog.Material);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Filament_material_selection_and_custom_text_work_and_blank_material_is_rejected() => Run(async () =>
    {
        var now = DateTimeOffset.UtcNow;
        var owner = await Open(); owner.Show();
        var dialog = new CreateFilamentWindow(new FilamentProfile(Guid.NewGuid(), "Marque", "Bobine", "PLA Silk", 48.59m, false, now, now));
        var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            var material = (ComboBox)typeof(CreateFilamentWindow).GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            material.SelectedItem = "PA612-ESD"; Assert.Equal("PA612-ESD", dialog.Material);
            material.Text = ""; DialogAction(owner, "Enregistrer"); Dispatcher.UIThread.RunJobs();
            Assert.False(shown.IsCompleted); Assert.Contains("form-invalid", material.Classes);
            material.Text = "Formulation personnalisée"; Assert.Equal("Formulation personnalisée", dialog.Material);
            DialogAction(owner, "Enregistrer"); Assert.True(await shown); Assert.Equal(48.59m, dialog.Price);
        }
        finally { dialog.Close(false); owner.Close(); }
    });

    [Fact]
    public Task Filament_catalog_search_combines_words_families_and_keeps_prices_and_variant_labels_visible() => Run(async () =>
    {
        var now = DateTimeOffset.UtcNow; var store = new FilamentStore(_root);
        store.Create(new(Guid.NewGuid(), "Atelier", "Carbone", "PETG-CF", 48.59m, true, now, now, "Rouge"));
        store.Create(new(Guid.NewGuid(), "Atelier", "Nylon verre", "PA6-GF", 65m, true, now, now, "Noir"));
        store.Create(new(Guid.NewGuid(), "Atelier", "Antistatique", "PA612-ESD", 90m, false, now, now, "Bleu"));
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Filaments"); await (Task)Invoke(owner, "LoadFilamentsAsync")!;
            var search = owner.FindControl<TextBox>("FilamentSearch"); Assert.NotNull(search);
            var family = owner.FindControl<ComboBox>("FilamentFamily"); Assert.NotNull(family);
            var list = owner.FindControl<StackPanel>("FilamentList")!;
            search.Text = "carbone rouge"; Dispatcher.UIThread.RunJobs();
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Atelier · Carbone");
            Assert.DoesNotContain(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Atelier · Nylon verre");
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "CF");
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "48,59 €/kg");
            family.SelectedItem = "Polyamides (PA)"; search.Text = "antistatique bleu"; Dispatcher.UIThread.RunJobs();
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Atelier · Antistatique");
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "ESD");
            search.Text = "resultat introuvable"; Dispatcher.UIThread.RunJobs();
            var reset = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Content as string == "Effacer les filtres"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("", search.Text); Assert.Equal("Toutes les familles", family.SelectedItem);
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "GF");
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "filament-search-" + width); }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Estimate_filament_search_preserves_the_current_choice_until_explicit_confirmation() => Run(async () =>
    {
        var now = DateTimeOffset.UtcNow;
        var target = new FilamentProfile(Guid.NewGuid(), "Atelier", "Technique rouge", "PETG-CF", 48.59m, true, now, now, "Rouge"); new FilamentStore(_root).Create(target);
        var owner = await Open(); owner.Show();
        try
        {
            var current = owner.FindControl<ComboBox>("EstimateFilament")!.SelectedItem;
            var open = owner.FindControl<Button>("EstimateSearchFilament"); Assert.NotNull(open); open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = await WaitForDialog<DialogWindow>(owner);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            var search = body.GetLogicalDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Rechercher un filament");
            search.Text = "technique rouge"; Dispatcher.UIThread.RunJobs();
            var results = body.GetLogicalDescendants().OfType<ListBox>().Single(); Assert.Single(results.Items);
            results.SelectedIndex = 0; Assert.Same(current, owner.FindControl<ComboBox>("EstimateFilament")!.SelectedItem);
            picker.Close(false); Assert.Same(current, owner.FindControl<ComboBox>("EstimateFilament")!.SelectedItem);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitForDialog<DialogWindow>(owner);
            body = owner.FindControl<Border>("SidePanelBody")!;
            results = body.GetLogicalDescendants().OfType<ListBox>().Single(); results.SelectedItem = results.Items.Cast<FilamentProfile>().Single(f => f.Id == target.Id);
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(260); CaptureDraftUi(owner, "estimate-filament-picker-" + width); }
            DialogAction(owner, "Choisir ce filament"); await Task.Delay(30);
            Assert.Equal(target.Id, Assert.IsType<FilamentProfile>(owner.FindControl<ComboBox>("EstimateFilament")!.SelectedItem).Id);
            Assert.Equal("48,59", Field<TextBox>(owner, "_estimateFilamentPrice").Text);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Estimate_workspace_templates_tolerate_empty_items() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            var template = owner.FindControl<ComboBox>("EstimateFilament")!.ItemTemplate!;
            var empty = Assert.IsType<TextBlock>(template.Build(null));
            Assert.True(string.IsNullOrEmpty(empty.Text));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Inventory_and_client_lists_keep_all_items_accessible_through_pages() => Run(async () =>
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 55; i++)
        {
            new FilamentStore(_root).Create(new(Guid.NewGuid(), "Test", "PLA " + i, "PLA", 20, false, now, now));
            new PrinterStore(_root).Create(new(Guid.NewGuid(), "Test", "Machine " + i, "Machine " + i, 2, "", true, now, now));
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceCollectionCreator().Create(_root, "02_MODELES_3D", "Modèle " + i);
        }
        var owner = await Open();
        try
        {
            foreach (var surface in new[] { ("Filaments", "LoadFilamentsAsync", "FilamentList", 6), ("Imprimantes", "LoadPrintersAsync", "PrinterList", 6), ("Collections", "LoadCollectionsAsync", "CollectionList", 5) })
            {
                Invoke(owner, "ShowWorkspace", surface.Item1);
                await (Task)Invoke(owner, surface.Item2)!;
                var list = owner.FindControl<StackPanel>(surface.Item3)!;
                Assert.Equal(53, list.Children.Count); // Two navigation bars, header, 50 rows.
                list.GetLogicalDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(surface.Item4 + 3, list.Children.Count);
                Assert.All(list.GetLogicalDescendants().OfType<Button>().Where(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante"), b => Assert.False(b.IsEnabled));
            }
            Invoke(owner, "ShowWorkspace", "Clients");
            typeof(MainWindow).GetField("_clientListMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, true);
            var clients = Enumerable.Range(0, 75).Select(i => new MS3DPRINT.Manager.Core.Clients.ClientSummary(_root, "Client " + i, "Client " + i, "C" + i, null, null, 0)).ToArray();
            typeof(MainWindow).GetField("_allClients", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, clients);
            Invoke(owner, "RenderClients");
            var clientList = owner.FindControl<StackPanel>("ClientList")!;
            Assert.Equal(53, clientList.Children.Count);
            Text(owner, "ClientFilterBox", "C74");
            Assert.Single(clientList.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("table-row")));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Failed_background_save_keeps_the_form_and_blocks_reset_until_retry() => Run(async () =>
    {
        var owner = await Open(); var fail = true;
        var writer = new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftWriter((root, draft) =>
        {
            if (fail) throw new IOException("Échec de test");
            new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(root).Save(draft);
        });
        typeof(MainWindow).GetField("_estimateDraftWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, writer);
        try
        {
            Text(owner, "EstimateName", "Saisie protégée");
            await (Task)Invoke(owner, "AutoSaveEstimateDraftAsync")!;
            Assert.Contains("non sauvegardé", Field<TextBlock>(owner, "_estimateDraftStatus").Text);
            Assert.False((bool)Invoke(owner, "ResetEstimate_Click", owner, new RoutedEventArgs())!);
            Assert.Equal("Saisie protégée", owner.FindControl<TextBox>("EstimateName")!.Text);
            fail = false;
            Assert.True((bool)Invoke(owner, "FlushEstimateDraft")!);
            Assert.Equal("Saisie protégée", Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable()).Fields["Name"]);
        }
        finally { fail = false; owner.Close(); }
    });

    [Fact]
    public Task Closing_during_autosave_waits_for_the_latest_text_without_resurrecting_work() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftWriter((root, draft) =>
        {
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(root).Save(draft);
        });
        typeof(MainWindow).GetField("_estimateDraftWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, writer);
        Task? pending = null;
        try
        {
            Text(owner, "EstimateName", "Première version");
            pending = (Task)Invoke(owner, "AutoSaveEstimateDraftAsync")!;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Text(owner, "EstimateName", "Dernier texte avant fermeture");
            var unblocking = Task.Run(async () => { await Task.Delay(100); release.Set(); });
            owner.Close(); await unblocking; await pending;
            Assert.False(owner.IsVisible);
            Assert.Equal("Dernier texte avant fermeture", Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable()).Fields["Name"]);
            Assert.False(Field<DispatcherTimer>(owner, "_estimateDraftTimer").IsEnabled);
        }
        finally { release.Set(); if (pending is not null) await pending; owner.Close(); }
    });

    [Fact]
    public Task Delayed_autosave_keeps_the_UI_responsive_and_saves_the_latest_revision() => Run(async () =>
    {
        var owner = await Open();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftWriter((root, draft) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(root).Save(draft);
        });
        typeof(MainWindow).GetField("_estimateDraftWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, writer);
        Task? pending = null;
        try
        {
            Text(owner, "EstimateName", "Première saisie");
            pending = (Task)Invoke(owner, "AutoSaveEstimateDraftAsync")!;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            Text(owner, "EstimateName", "Dernière saisie pendant l’écriture");
            Assert.Equal("Dernière saisie pendant l’écriture", owner.FindControl<TextBox>("EstimateName")!.Text);
            release.Set(); await pending;
            await (Task)Invoke(owner, "AutoSaveEstimateDraftAsync")!;
            var saved = Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable());
            Assert.Equal("Dernière saisie pendant l’écriture", saved.Fields["Name"]);
            Assert.Contains("sauvegardé", Field<TextBlock>(owner, "_estimateDraftStatus").Text);
        }
        finally { release.Set(); if (pending is not null) await pending; owner.Close(); }
    });

    [Fact]
    public Task Large_project_lists_build_only_one_page_and_search_the_entire_catalogue() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            typeof(MainWindow).GetField("_projectListMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, true);
            var samples = new List<object>();
            foreach (var count in new[] { 100, 1000, 5000 })
            {
                var projects = Enumerable.Range(0, count).Select(i => new ProjectSummary("Client", _root, Path.Combine(_root, "Projet" + i), "TEST-" + i.ToString("D5"), "Projet " + i, null)).ToArray();
                typeof(MainWindow).GetField("_allProjects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, projects);
                double? fullConstructionMs = null;
                int? fullControls = null;
                if (Environment.GetEnvironmentVariable("MS3DPRINT_PERF_BASELINE") == "1")
                {
                    // Reconstruct the pre-paging row strategy without altering production sources.
                    var full = new StackPanel();
                    var factory = typeof(MainWindow).GetMethod("CreateProjectRow", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var baseline = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var project in projects) full.Children.Add((Control)factory.Invoke(owner, [project])!);
                    baseline.Stop(); fullConstructionMs = baseline.Elapsed.TotalMilliseconds;
                    fullControls = full.GetLogicalDescendants().Count(); full.Children.Clear();
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Invoke(owner, "RenderProjects"); watch.Stop();
                var list = owner.FindControl<StackPanel>("ProjectList")!;
                var controls = list.GetLogicalDescendants().Count();
                var rows = list.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag is ProjectSummary && b.Classes.Contains("table-row")).ToArray();
                Assert.Equal(50, rows.Length);
                Assert.InRange(controls, 1, 3000);
                samples.Add(new { count, milliseconds = watch.Elapsed.TotalMilliseconds, controls, fullConstructionMs, fullControls });
            }
            var projectList = owner.FindControl<StackPanel>("ProjectList")!;
            owner.Show(); Invoke(owner, "ShowWorkspace", "Projets");
            var next = projectList.GetLogicalDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Page suivante");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.Equal("Page suivante", Avalonia.Automation.AutomationProperties.GetName((Control)owner.FocusManager!.GetFocusedElement()!));
            Invoke(owner, "RenderProjects"); // A quick status update must not jump back to page one.
            Assert.DoesNotContain(projectList.GetLogicalDescendants().OfType<Button>(), b => b.Tag is ProjectSummary p && p.Reference == "TEST-00000");
            Text(owner, "ProjectFilterBox", "TEST-04999");
            var only = Assert.Single(projectList.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag is ProjectSummary && b.Classes.Contains("table-row")));
            Assert.Equal("TEST-04999", ((ProjectSummary)only.Tag!).Reference);
            Text(owner, "ProjectFilterBox", "");
            owner.Show(); Invoke(owner, "ShowWorkspace", "Projets");
            owner.FindControl<TextBlock>("PageTitle")!.Text = "Projets";
            foreach (var width in new[] { 1100d, 1600d }) { owner.Width = width; await Task.Delay(250); CaptureDraftUi(owner, "paged-projects-" + width); }
            var folder = new DirectoryInfo(AppContext.BaseDirectory); while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent;
            if (folder is not null) File.WriteAllText(Path.Combine(folder.FullName, "outputs", "paged-project-measurements.json"), System.Text.Json.JsonSerializer.Serialize(samples));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Unchanged_draft_is_not_written_again_on_every_transition() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            Text(owner, "EstimateName", "Sans réécriture");
            Assert.True((bool)Invoke(owner, "FlushEstimateDraft")!);
            var path = Directory.GetFiles(Path.Combine(_root, ".ms3dprint-manager", "estimate-drafts"), "*.json").Single();
            var marker = DateTime.UtcNow.AddDays(-1); File.SetLastWriteTimeUtc(path, marker);
            Assert.True((bool)Invoke(owner, "FlushEstimateDraft")!);
            Assert.Equal(marker, File.GetLastWriteTimeUtc(path));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task History_finds_old_quotes_by_client_and_displays_more_than_five() => Run(async () =>
    {
        var store = new EstimateStore(_root);
        for (var i = 0; i < 27; i++) store.Create(new(Guid.NewGuid(), "Devis " + i, "PLA", "Machine", new(), new(1, 2, 3, 4), DateTimeOffset.UtcNow.AddDays(-i), ClientName: i == 26 ? "Société Étoile" : "Client"));
        var owner = await Open(); owner.Show();
        try
        {
            await (Task)Invoke(owner, "LoadSavedEstimatesAsync", _root)!;
            var list = owner.FindControl<StackPanel>("SavedEstimateList")!;
            Assert.Equal(20, list.GetLogicalDescendants().OfType<Button>().Count(b => b.Classes.Contains("estimate-history-row")));
            var more = list.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Afficher plus de devis");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(27, list.GetLogicalDescendants().OfType<Button>().Count(b => b.Classes.Contains("estimate-history-row")));
            Field<TextBox>(owner, "_estimateHistorySearch").Text = "etoile";
            Dispatcher.UIThread.RunJobs();
            var row = Assert.Single(list.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("estimate-history-row")));
            Assert.Equal("Devis 26", ((EstimateProfile)row.Tag!).Name);
            Invoke(owner, "ShowWorkspace", "Estimations");
            foreach (var expander in owner.FindControl<Grid>("EstimateLayout")!.GetVisualDescendants().OfType<Expander>().Where(e => e.Classes.Contains("estimate-history"))) expander.IsExpanded = true;
            foreach (var width in new[] { 1100d, 1600d }) { owner.Width = width; await Task.Delay(150); CaptureDraftUi(owner, "searchable-history-" + width); }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Conflicting_quote_save_preserves_the_form_and_its_draft() => Run(async () =>
    {
        var store = new EstimateStore(_root);
        var quote = new EstimateProfile(Guid.NewGuid(), "Original", "Test PLA", "Machine A", new(MaterialCost: 1), new(1, 1, 12, 12), DateTimeOffset.UtcNow);
        store.Create(quote);
        var owner = await Open();
        try
        {
            Invoke(owner, "LoadSavedEstimate_Click", new Button { Tag = quote }, new RoutedEventArgs());
            Text(owner, "EstimateName", "Mes modifications");
            store.Update(quote with { Name = "Version externe" });
            Click(owner, "EstimateSaveButton");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Version externe", Assert.Single(store.LoadAll()).Name);
            Assert.Equal("Mes modifications", owner.FindControl<TextBox>("EstimateName")!.Text);
            Assert.Equal(quote.Id, Field<EstimateProfile>(owner, "_loadedEstimate").Id);
            Assert.Contains("modifiée depuis", owner.FindControl<TextBlock>("EstimateDetail")!.Text);
            Invoke(owner, "FlushEstimateDraft");
            Assert.Contains(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable(), draft => draft.Fields.GetValueOrDefault("Name") == "Mes modifications");
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Client_suggestions_use_the_navy_theme_and_readable_company_details() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        var dialog = new CreateClientWindow(); var shown = dialog.ShowDialog<bool>(owner);
        try
        {
            var list = (ListBox)typeof(CreateClientWindow).GetField("_companyResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            list.ItemsSource = new[] { new MS3DPRINT.Manager.Desktop.Clients.CompanySuggestion("BATI DRONE SERVICES", "94123504600013", "LIEU DIT L AUNAY ROND 72390 LAVARE"), new("BATI DRONE SOLUTIONS", "99479579700012", "419 DOMAINE DE LA VIGNE 59910 BONDUES") };
            list.IsVisible = true;
            await Task.Delay(280); // Wait for the side panel's entry transform before hit testing.
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(Color.Parse("#11243B"), ((ISolidColorBrush)list.Background!).Color);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "BATI DRONE SERVICES" && t.FontSize >= 14);
            var first = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            owner.MouseMove(new Point(10, 10));
            owner.MouseMove(first.TranslatePoint(new Point(20, 20), owner)!.Value);
            Dispatcher.UIThread.RunJobs();
            var presenter = first.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            Assert.Equal(Color.Parse("#183650"), ((ISolidColorBrush)presenter.Background!).Color);
            foreach (var width in new[] { 1100d, 1600d }) { owner.Width = width; await Task.Delay(150); CaptureDraftUi(owner, "company-suggestions-" + width); }
            list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Equal("BATI DRONE SERVICES", dialog.ClientName);
            Assert.Equal("94123504600013", dialog.Siret);
            Assert.False(list.IsVisible);
        }
        finally { dialog.Close(false); await shown; owner.Close(); }
    });

    [Fact]
    public Task Audit_fixes_keep_modal_focus_and_restore_the_opener() => Run(async () =>
    {
        var owner = await Open(); owner.Show(); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
        try
        {
            var opener = owner.FindControl<TextBox>("SearchBox")!; opener.Focus();
            var dialog = new TextPromptWindow("Audit", "Formulaire de test", "Nom");
            var shown = dialog.ShowDialog<bool>(owner); Dispatcher.UIThread.RunJobs();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            body.GetVisualDescendants().OfType<TextBox>().Single().Focus();
            for (var i = 0; i < 25; i++)
            {
                owner.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                owner.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains((Control)owner.FocusManager!.GetFocusedElement()!, body.GetVisualDescendants());
            }
            for (var i = 0; i < 8; i++)
            {
                owner.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                owner.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains((Control)owner.FocusManager!.GetFocusedElement()!, body.GetVisualDescendants());
            }
            owner.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            Assert.NotSame(opener, owner.FocusManager!.GetFocusedElement());
            dialog.Close(false); await shown; Dispatcher.UIThread.RunJobs();
            Assert.Same(opener, owner.FocusManager!.GetFocusedElement());
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Audit_fixes_keep_incomplete_quotes_out_of_history_and_report_the_files() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            var store = new EstimateStore(_root);
            store.Create(new(Guid.NewGuid(), "Sain", "PLA", "Machine", new(), new(1, 1, 2, 2), DateTimeOffset.UtcNow));
            var invalid = Guid.NewGuid() + ".json";
            File.WriteAllText(Path.Combine(_root, ".ms3dprint-manager", "estimates", invalid),
                System.Text.Json.JsonSerializer.Serialize(new { Id = Guid.NewGuid(), Name = "Incomplet", CreatedAt = DateTimeOffset.UtcNow.AddDays(1) }));
            await (Task)Invoke(owner, "LoadSavedEstimatesAsync", _root)!;
            var list = owner.FindControl<StackPanel>("SavedEstimateList")!;
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Sain");
            Assert.Contains(list.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("illisible") == true);
            Assert.True(File.Exists(Path.Combine(_root, ".ms3dprint-manager", "estimates", invalid)));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Audit_fixes_ignore_an_old_project_load_after_the_workspace_changes() => Run(async () =>
    {
        var owner = await Open();
        var rootA = Path.Combine(_root, "a"); var rootB = Path.Combine(_root, "b");
        var client = Path.Combine(rootA, "01_CLIENTS", "AUDIT_Client audit");
        Directory.CreateDirectory(client); Directory.CreateDirectory(rootB);
        for (var i = 1; i <= 500; i++) Directory.CreateDirectory(Path.Combine(client, $"AUDIT-2026-{i:000}_Projet A"));
        try
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", rootA);
            var old = (Task)Invoke(owner, "LoadProjectsAsync")!;
            var oldInventory = (Task)Invoke(owner, "LoadEstimateFilamentsAsync")!;
            var oldProject = new ProjectSummary("AUDIT_Client audit", client, Path.Combine(client, "AUDIT-2026-001_Projet A"), "AUDIT-2026-001", "AUDIT-2026-001_Projet A", null);
            var oldPreparation = (Task)Invoke(owner, "StartProjectEstimateAsync", oldProject)!;
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", rootB);
            await (Task)Invoke(owner, "LoadProjectsAsync")!;
            await (Task)Invoke(owner, "LoadEstimateFilamentsAsync")!;
            await old;
            await oldInventory;
            await oldPreparation;
            Assert.Empty(Field<IReadOnlyList<ProjectSummary>>(owner, "_allProjects"));
            Assert.Equal(rootB, Field<string>(owner, "_estimateFormRoot"));
            Assert.DoesNotContain("Projet A", owner.FindControl<TextBox>("EstimateName")!.Text ?? "");
            Assert.DoesNotContain(owner.FindControl<StackPanel>("ProjectList")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Projet A") == true);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root); owner.Close(); }
    });

    [Fact]
    public Task Audit_fixes_do_not_append_an_old_client_detail_to_the_new_client_list() => Run(async () =>
    {
        var owner = await Open();
        var rootA = Path.Combine(_root, "a"); var rootB = Path.Combine(_root, "b");
        var client = Path.Combine(rootA, "01_CLIENTS", "AUDIT_Client audit");
        Directory.CreateDirectory(client); Directory.CreateDirectory(rootB);
        for (var i = 1; i <= 500; i++) Directory.CreateDirectory(Path.Combine(client, $"AUDIT-2026-{i:000}_Projet A"));
        try
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", rootA);
            var summary = new MS3DPRINT.Manager.Core.Clients.ClientSummary(client, "AUDIT_Client audit", "Client audit", "AUDIT", null, null, 500);
            var old = (Task)Invoke(owner, "LoadClientDetailAsync", summary)!;
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", rootB);
            await (Task)Invoke(owner, "LoadClientsAsync")!;
            await old;
            Assert.DoesNotContain(owner.FindControl<StackPanel>("ClientList")!.GetLogicalDescendants().OfType<Button>(), b => b.Tag is ProjectSummary);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root); owner.Close(); }
    });

    [Fact]
    public Task Audit_fixes_offer_retry_without_recreating_an_unavailable_workspace() => Run(async () =>
    {
        var owner = await Open(); owner.Show(); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
        var missing = Path.Combine(_root, "indisponible");
        try
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", missing);
            var navigation = (Task)Invoke(owner, "NavigateToWorkspaceAsync", "Projets")!;
            await Task.Delay(50); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Field<DialogWindow?>(owner, "_panel"));
            Assert.False(Directory.Exists(missing));
            foreach (var width in new[] { 960, 1440 })
            {
                owner.Width = width; owner.Height = 760;
                await Task.Delay(100); Dispatcher.UIThread.RunJobs();
                CaptureDraftUi(owner, "audit-unavailable-" + width);
            }
            Directory.CreateDirectory(missing);
            DialogAction(owner, "Réessayer");
            await navigation;
            Assert.Null(Field<DialogWindow?>(owner, "_panel"));
            Assert.Equal(missing, Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE"));
            var chooser = new WorkspaceUnavailableWindow(missing, () => Task.FromResult<string?>(_root));
            var choice = chooser.ShowDialog<bool>(owner);
            owner.Width = 960; await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            CaptureDraftUi(owner, "audit-unavailable-with-choice");
            DialogAction(owner, "Choisir un autre dossier");
            Assert.True(await choice);
            Assert.Equal(_root, chooser.SelectedRoot);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root); owner.Close(); }
    });

    private static async Task<MainWindow> Open()
    {
        var window = new MainWindow();
        await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
        return window;
    }
    private static void CaptureDraftUi(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(root.FullName, "MS3DPRINT.Manager.sln"))) root = root.Parent!;
        var output = Path.Combine(root.FullName, "outputs", "qa", CaptureRun, "drafts-duplication"); Directory.CreateDirectory(output);
        using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(output, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [Fact]
    public Task Autosaved_draft_restores_invalid_unfinished_input_without_creating_a_saved_quote() => Run(async () =>
    {
        var first = await Open();
        Text(first, "EstimateName", "Commande interrompue");
        Text(first, "EstimateQuantity", "incomplet");
        Field<TextBox>(first, "_estimateDurationMinutes").Text = "75";
        first.Close();
        var restored = await Open();
        try
        {
            Assert.Equal("Commande interrompue", restored.FindControl<TextBox>("EstimateName")!.Text);
            Assert.Equal("incomplet", restored.FindControl<TextBox>("EstimateQuantity")!.Text);
            Assert.Equal("75", Field<TextBox>(restored, "_estimateDurationMinutes").Text);
            Assert.Empty(new EstimateStore(_root).LoadAll());
        }
        finally { restored.Close(); }
    });

    [Fact]
    public Task Duplicating_a_saved_quote_keeps_its_prices_and_creates_a_distinct_saved_record() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            var original = new EstimateProfile(Guid.NewGuid(), "Support original", "Test PLA", "Machine A", new(MaterialCost: 1), new(1, 1, 123.45m, 123.45m), DateTimeOffset.UtcNow, 100, 2, 2);
            new EstimateStore(_root).Create(original);
            Invoke(owner, "DuplicateEstimate_Click", new Button { Tag = original }, new RoutedEventArgs());
            Assert.Equal(123.45m, Field<EstimateResult>(owner, "_latestEstimateResult").FinalPrice);
            Invoke(owner, "FlushEstimateDraft"); owner.Close(); owner = await Open();
            Click(owner, "EstimateSaveButton");
            var saved = new EstimateStore(_root).LoadAll();
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, quote => quote.Id == original.Id && quote.Name == original.Name);
            Assert.Contains(saved, quote => quote.Id != original.Id && quote.Name.Contains("copie", StringComparison.OrdinalIgnoreCase) && quote.Result.FinalPrice == 123.45m);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Failed_draft_write_blocks_replacing_the_form_and_preserves_latest_text() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            Text(owner, "EstimateName", "Travail à conserver"); Invoke(owner, "FlushEstimateDraft");
            var path = Directory.GetFiles(Path.Combine(_root, ".ms3dprint-manager", "estimate-drafts"), "*.json").Single();
            var other = new EstimateProfile(Guid.NewGuid(), "Autre devis", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, 2, 2), DateTimeOffset.UtcNow);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Text(owner, "EstimateQuantity", "12");
                Invoke(owner, "LoadSavedEstimate_Click", new Button { Tag = other }, new RoutedEventArgs());
                Assert.Equal("Travail à conserver", owner.FindControl<TextBox>("EstimateName")!.Text);
                Assert.Equal("12", owner.FindControl<TextBox>("EstimateQuantity")!.Text);
            }
            Invoke(owner, "FlushEstimateDraft");
            Assert.Equal("12", new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable().Single().Fields["Quantity"]);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Copied_v2_quote_can_be_adjusted_with_a_material_snapshot_missing_from_inventory() => Run(async () =>
    {
        var owner = await Open();
        try
        {
            var input = new EstimateInputs(EngineVersion: 2, V2: new(Production: new(WeightGrams: 25, HoursPerRun: 1, MaterialPricePerKg: 33)));
            var source = new EstimateProfile(Guid.NewGuid(), "Matière ancienne", "Disparu PLA", "Machine ancienne", input, EstimateCalculator.Calculate(input), DateTimeOffset.UtcNow, 25, 1, 0);
            new EstimateStore(_root).Create(source);
            Invoke(owner, "DuplicateEstimate_Click", new Button { Tag = source }, new RoutedEventArgs());
            Text(owner, "EstimateDiscount", "10");
            Assert.True((bool)Invoke(owner, "CalculateEstimate")!);
            Click(owner, "EstimateSaveButton");
            var copy = new EstimateStore(_root).LoadAll().Single(q => q.Id != source.Id);
            Assert.Equal(33, copy.Inputs.V2!.Production!.MaterialPricePerKg);
            Assert.Equal(source.FilamentName, copy.FilamentName);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Project_duplication_creates_a_new_reference_and_optionally_copies_files() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Original");
        var reader = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader();
        var source = reader.Load(_root).Single();
        File.WriteAllText(Path.Combine(source.ProjectPath, "piece.stl"), "modele");
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Projets");
            Invoke(owner, "DuplicateProject_Click", new Button { Tag = source }, new RoutedEventArgs());
            await WaitForDialog<DialogWindow>(owner);
            foreach (var width in new[] { 1440, 960 })
            {
                owner.Width = width; owner.Height = width == 960 ? 760 : 960;
                await Task.Delay(250); CaptureDraftUi(owner, "project-copy-" + width);
            }
            owner.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
            DialogAction(owner, "Dupliquer le projet");
            for (var i = 0; i < 200 && Field<bool>(owner, "_duplicatingProject"); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.False(Field<bool>(owner, "_duplicatingProject"));
            var copy = reader.Load(_root).Single(p => p.Reference != source.Reference);
            Assert.NotEqual(source.Profile!.Id, copy.Profile!.Id);
            Assert.Equal(MS3DPRINT.Manager.Core.Projects.ProjectStatus.Quote, copy.Profile.Status);
            Assert.Equal("modele", File.ReadAllText(Path.Combine(copy.ProjectPath, "piece.stl")));
            Assert.Equal("modele", File.ReadAllText(Path.Combine(source.ProjectPath, "piece.stl")));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Drafts_stay_in_their_workspace_and_saving_consumes_only_the_current_draft() => Run(async () =>
    {
        var other = Path.Combine(Path.GetTempPath(), "ms3dprint-draft-other-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(other);
        var owner = await Open();
        try
        {
            Text(owner, "EstimateName", "Brouillon atelier A"); Text(owner, "EstimateQuantity", "non terminé");
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", other);
            await (Task)Invoke(owner, "LoadEstimateFilamentsAsync")!;
            Text(owner, "EstimateName", "Brouillon atelier B");
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root);
            await (Task)Invoke(owner, "LoadEstimateFilamentsAsync")!;
            Assert.Equal("Brouillon atelier A", owner.FindControl<TextBox>("EstimateName")!.Text);
            Assert.Equal("non terminé", owner.FindControl<TextBox>("EstimateQuantity")!.Text);
            var store = new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root);
            Assert.Single(store.LoadReadable());
            var profile = new EstimateProfile(Guid.NewGuid(), "Autre devis", "Test PLA", "Machine", new(MaterialCost: 1), new(1, 1, 10, 10), DateTimeOffset.UtcNow);
            new EstimateStore(_root).Create(profile);
            Invoke(owner, "DuplicateEstimate_Click", new Button { Tag = profile }, new RoutedEventArgs());
            Invoke(owner, "FlushEstimateDraft"); Assert.Equal(2, store.LoadReadable().Count);
            Click(owner, "EstimateSaveButton"); Assert.Single(store.LoadReadable());
            Assert.Equal("Brouillon atelier A", store.LoadReadable().Single().Fields["Name"]);
            Assert.Single(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(other).LoadReadable());
        }
        finally { owner.Close(); Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root); Directory.Delete(other, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Draft_restores_supplier_or_machine_inputs_even_when_not_yet_numeric(bool outsourced) => Run(async () =>
    {
        var first = await Open();
        Text(first, "EstimateName", "Configuration à terminer");
        Field<CheckBox>(first, "_estimateOutsource").IsChecked = outsourced;
        if (outsourced)
        {
            Field<TextBox>(first, "_estimateSupplier").Text = "Fournisseur Rhône";
            Field<TextBox>(first, "_estimateSupplierCost").Text = "en attente";
        }
        else
        {
            Field<TextBox>(first, "_estimatePower").Text = "";
            Invoke(first, "RecalculateElectricity"); Text(first, "EstimateElectricity", "0,77");
            Invoke(first, "AddEstimateMachine", new EstimateMachineAllocation("Machine spéciale", 2, .987654321m));
            var panel = (StackPanel)Field<StackPanel>(first, "_estimateAllocations").Children.Single();
            panel.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Text == "2").Text = "heures incomplètes";
        }
        first.Close();
        var restored = await Open();
        try
        {
            Assert.Equal(outsourced, Field<CheckBox>(restored, "_estimateOutsource").IsChecked);
            if (outsourced)
            {
                Assert.Equal("Fournisseur Rhône", Field<TextBox>(restored, "_estimateSupplier").Text);
                Assert.Equal("en attente", Field<TextBox>(restored, "_estimateSupplierCost").Text);
            }
            else
            {
                var texts = Field<StackPanel>(restored, "_estimateAllocations").GetLogicalDescendants().OfType<TextBox>().Select(t => t.Text).ToArray();
                Assert.Contains("Machine spéciale", texts); Assert.Contains("heures incomplètes", texts); Assert.Contains("0,987654321", texts);
                Assert.False(restored.FindControl<TextBox>("EstimateElectricity")!.IsReadOnly);
                Assert.Equal("0,77", restored.FindControl<TextBox>("EstimateElectricity")!.Text);
            }
        }
        finally { restored.Close(); }
    });

    [Fact]
    public Task Discarding_the_current_draft_requires_confirmation() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Text(owner, "EstimateName", "À conserver"); Invoke(owner, "FlushEstimateDraft");
            Invoke(owner, "ShowWorkspace", "Estimations");
            owner.FindControl<TextBlock>("PageTitle")!.Text = "Estimations";
            new EstimateStore(_root).Create(new(Guid.NewGuid(), "Support de fixation · série de 40 pièces", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, 28.11m, 28.11m), DateTimeOffset.UtcNow));
            await (Task)Invoke(owner, "LoadSavedEstimatesAsync", _root)!;
            owner.FindControl<StackPanel>("SavedEstimateList")!.GetLogicalAncestors().OfType<Expander>().First().IsExpanded = true;
            foreach (var width in new[] { 1440, 960 })
            {
                owner.Width = width; owner.Height = width == 960 ? 760 : 960;
                await Task.Delay(250); CaptureDraftUi(owner, "draft-history-" + width);
            }
            Invoke(owner, "NewEstimate_Click", null, new RoutedEventArgs());
            var dialog = await WaitForDialog<ConfirmRemovalWindow>(owner); dialog.Close(false);
            await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.Equal("À conserver", owner.FindControl<TextBox>("EstimateName")!.Text);
            Invoke(owner, "NewEstimate_Click", null, new RoutedEventArgs());
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Confirmer le retrait");
            await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.Empty(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable());
            Assert.Equal("Nouvelle estimation", owner.FindControl<TextBox>("EstimateName")!.Text);
            Text(owner, "EstimateName", "À supprimer depuis l’historique"); Invoke(owner, "FlushEstimateDraft");
            await (Task)Invoke(owner, "LoadSavedEstimatesAsync", _root)!;
            var remove = owner.FindControl<StackPanel>("SavedEstimateList")!.GetVisualDescendants().OfType<Button>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Supprimer le brouillon", StringComparison.Ordinal) == true);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner); DialogAction(owner, "Confirmer le retrait");
            await Task.Delay(50); Dispatcher.UIThread.RunJobs();
            Assert.Empty(new MS3DPRINT.Manager.Desktop.Dashboard.EstimateDraftStore(_root).LoadReadable());
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Reopening_a_legacy_estimate_and_navigating_does_not_reprice_it() => Run(async () =>
    {
        var window = await Open();
        try
        {
            var old = new EstimateProfile(Guid.NewGuid(), "Nouvelle estimation", "Test PLA", "Machine A", new(MaterialCost: 1), new(1, 1, 123.45m, 123.45m), DateTimeOffset.UtcNow, 100, 2, 2);
            new EstimateStore(_root).Create(old);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = old }, new RoutedEventArgs());
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Assert.Equal(123.45m, Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice);
            Click(window, "EstimateSaveButton");
            Assert.Equal(123.45m, Assert.Single(new EstimateStore(_root).LoadAll()).Result.FinalPrice);
            Assert.Equal(old.Name, Assert.Single(new EstimateStore(_root).LoadAll()).Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Refresh_keeps_manual_rate_and_material_snapshot_of_a_new_calculated_quote() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateMachineRate", "7,123456"); Field<TextBox>(window, "_estimateFilamentPrice").Text = "33,456789";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var price = Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice;
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Assert.Equal("7,123456", window.FindControl<TextBox>("EstimateMachineRate")!.Text);
            Assert.Equal("33,456789", Field<TextBox>(window, "_estimateFilamentPrice").Text);
            Assert.Equal(price, Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Electricity_stays_per_piece_and_repeated_calculations_do_not_multiply_it_again() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateQuantity", "40"); Text(window, "EstimateHours", "1");
            Assert.Equal("200", Field<TextBox>(window, "_estimatePower").Text);
            Assert.Equal("0,25", window.FindControl<TextBox>("EstimateKwhPrice")!.Text);
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2m, Field<EstimateResult>(window, "_latestEstimateResult").V2!.ElectricityCost);
            Assert.Equal("0,05", window.FindControl<TextBox>("EstimateElectricity")!.Text);
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2m, Field<EstimateResult>(window, "_latestEstimateResult").V2!.ElectricityCost);
            Text(window, "EstimateDiscount", "10");
            await Task.Delay(350);
            Assert.Equal(10, Field<EstimateInputs>(window, "_latestEstimateInputs").DiscountPercent);
            Text(window, "EstimateQuantity", "0");
            Click(window, "EstimateSaveButton");
            Assert.Empty(new EstimateStore(_root).LoadAll());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Save_reload_and_commercial_adjustment_preserve_machine_and_cost_precision() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateMachineRate", "0,123456789");
            Field<TextBox>(window, "_estimateCommercial").Text = "50,005";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Click(window, "EstimateSaveButton");
            var saved = Assert.Single(new EstimateStore(_root).LoadAll());
            Assert.Equal(50.01m, saved.Result.FinalPrice);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = saved }, new RoutedEventArgs());
            Assert.Equal("0,123456789", window.FindControl<TextBox>("EstimateMachineRate")!.Text);
            Text(window, "EstimateDiscount", "10");
            Click(window, "EstimateSaveButton");
            var updated = Assert.Single(new EstimateStore(_root).LoadAll());
            Assert.Equal(.123456789m, updated.Inputs.V2!.Production!.MachineHourlyRate);
            Assert.Equal(45m, updated.Result.FinalPrice);
            Assert.Equal(saved.Id, updated.Id);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Loading_unknown_power_restores_manual_electricity_and_keeps_saved_result() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Field<TextBox>(window, "_estimatePower").Text = "200";
            Invoke(window, "RecalculateElectricity");
            Assert.True(window.FindControl<TextBox>("EstimateElectricity")!.IsReadOnly);
            var input = new EstimateInputs(EngineVersion: 2, V2: new(Production: new(WeightGrams: 100, HoursPerRun: 2, MaterialPricePerKg: 20, ManualElectricityCost: .75m)));
            var saved = new EstimateProfile(Guid.NewGuid(), "Sans puissance", "Test PLA", "Machine retirée", input, EstimateCalculator.Calculate(input), DateTimeOffset.UtcNow);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = saved }, new RoutedEventArgs());
            Assert.False(window.FindControl<TextBox>("EstimateElectricity")!.IsReadOnly);
            Assert.Equal("0,75", window.FindControl<TextBox>("EstimateElectricity")!.Text);
            Assert.Equal(saved.Result.FinalPrice, Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Estimate_single_page_renders_at_desktop_and_minimum_sizes() => Run(async () =>
    {
        var window = await Open();
        try
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "estimate-v2"); Directory.CreateDirectory(output);
            window.Width = 1360; window.Height = 1000; window.Show();
            await Task.Delay(250);
            Invoke(window, "ShowWorkspace", "Estimations");
            window.FindControl<TextBlock>("PageTitle")!.Text = "Estimations";
            window.FindControl<TextBlock>("PageSubtitle")!.Text = "Coûts de production et rentabilité des devis.";
            Text(window, "EstimateQuantity", "40");
            Text(window, "EstimateFailure", "5"); Text(window, "EstimateDesign", "50");
            window.FindControl<ComboBox>("EstimatePrintDataMode")!.SelectedIndex = 1;
            Field<TextBox>(window, "_estimateCapacity").Text = "10";
            Invoke(window, "SetEstimateCharges", new EstimateCharges(SalesSocialPercent: 21.2m));
            Text(window, "EstimateMargin", "20");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            foreach (var step in new[] { 1, 3 })
            {
                window.FindControl<Expander>("EstimatePriceDetails")!.IsExpanded = step == 3;
                await Task.Delay(220);
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, $"step-{step}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            window.Width = 960; window.Height = 760; window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var compact = window.CaptureRenderedFrame(); Assert.NotNull(compact); compact.Save(Path.Combine(output, "compact.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Assert.True(window.FindControl<StackPanel>("EstimateStep1")!.Bounds.Width > 200);
            window.FindControl<ScrollViewer>("EstimateFormScroll")!.Offset = default; await Task.Delay(220); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var input = window.CaptureRenderedFrame()) { Assert.NotNull(input); input.Save(Path.Combine(output, "saisie-compact.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            window.Width = 1360; window.Height = 1000;
            foreach (var state in new[] { (Step: 1, Host: "EstimateV2ProductionHost", File: "production-open.png"), (Step: 2, Host: "EstimateV2CostsHost", File: "charges-open.png") })
            {
                window.FindControl<ScrollViewer>("EstimateFormScroll")!.Offset = default;
                if (state.Step == 2) Field<Expander>(window, "_estimateAdjustments").IsExpanded = true;
                window.UpdateLayout();
                var section = state.Step == 1 ? Field<Expander>(window, "_estimateProductionOptions")
                    : window.FindControl<StackPanel>(state.Host)!.GetVisualDescendants().OfType<Expander>().Last();
                section.IsExpanded = true;
                window.UpdateLayout();
                section.BringIntoView(new Rect(0, 0, section.Bounds.Width, 64));
                var scroll = section.GetVisualAncestors().OfType<ScrollViewer>().First();
                if (scroll.Content is Visual content && section.TranslatePoint(default, content) is { } position) scroll.Offset = new Vector(0, position.Y);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var expanded = window.CaptureRenderedFrame(); Assert.NotNull(expanded);
                expanded.Save(Path.Combine(output, state.File), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Disclosure_uses_the_app_palette_and_opens_from_the_keyboard() => Run(async () =>
    {
        var window = await Open();
        try
        {
            window.Show(); Invoke(window, "ShowWorkspace", "Estimations"); window.FindControl<ScrollViewer>("EstimateFormScroll")!.Offset = default;
            window.UpdateLayout();
            var section = Field<Expander>(window, "_estimateProductionOptions");
            var header = section.GetVisualDescendants().OfType<ToggleButton>().First();
            var background = header.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ToggleButtonBackground");
            Assert.Equal(((ISolidColorBrush)window.FindResource("SurfaceBrush")!).Color, ((ISolidColorBrush)background.Background!).Color);
            Assert.False(section.IsExpanded);
            header.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(section.IsExpanded);
            Assert.True(((Control)section.Content!).IsEffectivelyVisible);
            var presenter = header.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)presenter.Background!).Color);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(section.IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Irrelevant_production_and_disabled_charge_fields_are_inactive_without_losing_their_values() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Assert.False(Field<TextBox>(window, "_estimateCapacity").IsEnabled);
            window.FindControl<ComboBox>("EstimatePrintDataMode")!.SelectedIndex = 1;
            Assert.True(Field<TextBox>(window, "_estimateCapacity").IsEnabled);
            Assert.False(Field<TextBox>(window, "_estimateExtraGrams").IsEnabled);
            Field<CheckBox>(window, "_estimateWeightIncludesSupports").IsChecked = false;
            Assert.True(Field<TextBox>(window, "_estimateExtraGrams").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Workshop_defaults_apply_only_to_new_quotes_and_are_not_overwritten_by_saved_quotes() => Run(async () =>
    {
        var settings = new WorkshopEstimateSettings(new(12.3m, 21.2m, 25.6m, ContributionPercent: .3m), "fr-micro-2026", ElectricityPricePerKwh: .3m, MarginPercent: 20, FailureRatePercent: 8, PieceFee: 2);
        new WorkshopEstimateSettingsStore(_root).Save(settings);
        var window = await Open();
        try
        {
            Assert.Equal("0,3", window.FindControl<TextBox>("EstimateKwhPrice")!.Text);
            Assert.Equal("20", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(12.3m, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Charges!.SalesSocialPercent);
            var oldInputs = new EstimateInputs(EngineVersion: 2, V2: new(Charges: new(SalesSocialPercent: 7)), MarginPercent: 10);
            var old = new EstimateProfile(Guid.NewGuid(), "Conservé", "Test PLA", "Machine A", oldInputs, EstimateCalculator.Calculate(oldInputs), DateTimeOffset.UtcNow);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = old }, new RoutedEventArgs());
            Assert.Equal(7m, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Charges!.SalesSocialPercent);
            Assert.Equal("10", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Invoke(window, "ResetEstimate_Click", null, new RoutedEventArgs());
            Assert.Equal("20", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Assert.Equal(settings, new WorkshopEstimateSettingsStore(_root).Load());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Workshop_profile_prefills_standard_rates_and_keeps_disabled_contributions() => Run(async () =>
    {
        await Task.CompletedTask;
        var dialog = new WorkshopEstimateWindow(new(new()));
        try
        {
            T Read<T>(string name) => (T)typeof(WorkshopEstimateWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            var preset = Read<ComboBox>("_preset");
            preset.SelectedItem = ((IEnumerable<EstimateChargePreset>)preset.ItemsSource!).First(p => p.Id == "fr-micro-2026");
            Assert.Equal("12,3", Read<TextBox>("_sales").Text); Assert.Equal("21,2", Read<TextBox>("_bic").Text); Assert.Equal("25,6", Read<TextBox>("_bnc").Text);
            var training = Read<ComboBox>("_training"); training.SelectedItem = ((IEnumerable<EstimateTrainingPreset>)training.ItemsSource!).First(p => p.Id == "artisan");
            Assert.Equal("0,3", Read<TextBox>("_cfp").Text);
            Read<CheckBox>("_contributions").IsChecked = false;
            Assert.False(Read<TextBox>("_cfp").IsEnabled); Assert.Equal("0,3", Read<TextBox>("_cfp").Text);
            typeof(WorkshopEstimateWindow).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.NotNull(dialog.Result); Assert.Equal("fr-micro-2026", dialog.Result.PresetId); Assert.False(dialog.Result.Charges.ContributionsEnabled);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public Task Quote_and_workshop_help_render_with_only_order_fields_visible() => Run(async () =>
    {
        var window = await Open();
        try
        {
            window.Show(); Invoke(window, "ShowWorkspace", "Estimations"); Field<Expander>(window, "_estimateOptionalCosts").IsExpanded = true; window.UpdateLayout();
            foreach (var name in new[] { "EstimateMargin", "EstimateFailure" })
            {
                var field = window.FindControl<TextBox>(name)!;
                Assert.False(field.GetVisualAncestors().Contains(window) && field.IsEffectivelyVisible);
            }
            var kwh = window.FindControl<TextBox>("EstimateKwhPrice")!;
            // A closed Expander has not attached its content to the visual tree yet.
            Assert.False(kwh.GetVisualAncestors().Contains(window) && kwh.IsEffectivelyVisible);
            Assert.NotNull(Avalonia.Automation.AutomationProperties.GetHelpText(window.FindControl<TextBox>("EstimateHours")!));
            var category = Field<ComboBox>(window, "_estimateChargeCategory"); category.SelectedIndex = 1;
            Field<Expander>(window, "_estimateAdjustments").IsExpanded = true; window.UpdateLayout();
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(100, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Charges!.ServicesBicSharePercent);
            Assert.False(Field<TextBox>(window, "_estimateSalesShare").IsEffectivelyVisible);
            category.SelectedIndex = 3;
            Field<TextBox>(window, "_estimateSalesShare").Text = "75"; Field<TextBox>(window, "_estimateBicShare").Text = "25";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(75, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Charges!.SalesSharePercent);
            var folder = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "atelier"); Directory.CreateDirectory(output);
            var settings = new WorkshopEstimateSettings(EstimateChargeCatalog.Load().Apply("fr-micro-2026", new()), "fr-micro-2026");
            foreach (var dialog in new DialogWindow[] { new WorkshopEstimateWindow(settings), new EstimateHelpWindow() })
            {
                try
                {
                    dialog.Show(); dialog.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var bitmap = dialog.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                    bitmap.Save(Path.Combine(output, dialog is WorkshopEstimateWindow ? "workshop.png" : "help.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    if (dialog is WorkshopEstimateWindow)
                    {
                        dialog.Width = 440; dialog.Height = 760;
                        var prices = dialog.GetVisualDescendants().OfType<Expander>().ElementAt(1);
                        prices.IsExpanded = true;
                        dialog.UpdateLayout();
                        var scroll = prices.GetVisualAncestors().OfType<ScrollViewer>().First();
                        if (scroll.Content is Visual content && prices.TranslatePoint(default, content) is { } position) scroll.Offset = new Vector(0, position.Y);
                        Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var narrow = dialog.CaptureRenderedFrame(); Assert.NotNull(narrow);
                        narrow.Save(Path.Combine(output, "workshop-narrow-prices.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    }
                }
                finally { dialog.Close(); }
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Switching_workspaces_does_not_reuse_another_workshops_defaults() => Run(async () =>
    {
        new WorkshopEstimateSettingsStore(_root).Save(new(new(SalesSocialPercent: 12.3m), MarginPercent: 20));
        var otherRoot = Path.Combine(_root, "other"); Directory.CreateDirectory(otherRoot);
        var window = await Open();
        try
        {
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", otherRoot);
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Assert.Equal("30", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Assert.Equal(0, ((EstimateCharges)Invoke(window, "ReadEstimateCharges")!).SalesSocialPercent);
            new WorkshopEstimateSettingsStore(otherRoot).Save(new(new(SalesSocialPercent: 7), MarginPercent: 25));
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root);
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", otherRoot);
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Invoke(window, "ResetEstimate_Click", null, new RoutedEventArgs());
            Assert.Equal("25", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Assert.Equal(7, ((EstimateCharges)Invoke(window, "ReadEstimateCharges")!).SalesSocialPercent);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Saving_new_workshop_settings_before_returning_to_quotes_does_not_keep_the_previous_workshops_quote() => Run(async () =>
    {
        new WorkshopEstimateSettingsStore(_root).Save(new(new(SalesSocialPercent: 12.3m), MarginPercent: 20));
        var otherRoot = Path.Combine(_root, "other"); Directory.CreateDirectory(otherRoot);
        new WorkshopEstimateSettingsStore(otherRoot).Save(new(new(SalesSocialPercent: 7), MarginPercent: 25));
        var window = await Open();
        try
        {
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Text(window, "EstimateName", "Commande de l’atelier A");
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", otherRoot);
            // Saving atelier B updates its settings cache before navigating back to the quote.
            Invoke(window, "LoadWorkshopDefaults", otherRoot);
            await (Task)Invoke(window, "LoadEstimateFilamentsAsync")!;
            Assert.Equal("25", window.FindControl<TextBox>("EstimateMargin")!.Text);
            Assert.Equal(7, ((EstimateCharges)Invoke(window, "ReadEstimateCharges")!).SalesSocialPercent);
            Assert.NotEqual("Commande de l’atelier A", window.FindControl<TextBox>("EstimateName")!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Navigation_motion_follows_the_latest_page_and_reduced_motion_keeps_it_immediately_usable() => Run(async () =>
    {
        var window = await Open();
        try
        {
            window.Width = 1360; window.Height = 900; window.Show();
            Invoke(window, "ShowWorkspace", "Projets");
            Invoke(window, "ShowWorkspace", "Clients");
            Invoke(window, "ShowWorkspace", "Estimations");
            await Task.Delay(280); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var indicator = window.FindControl<Border>("NavigationIndicator");
            Assert.NotNull(indicator);
            var selected = window.FindControl<StackPanel>("NavigationItems")!.Children.OfType<Button>().Single(b => b.Tag as string == "Estimations");
            Assert.Equal(1, indicator.Opacity);
            Assert.InRange(Math.Abs(indicator.TranslatePoint(default, window)!.Value.Y - selected.TranslatePoint(default, window)!.Value.Y), 0, 1);
            window.Classes.Add("reduce-motion");
            Invoke(window, "ShowWorkspace", "Clients");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var page = window.FindControl<Border>("ClientsWorkspacePanel")!;
            Assert.True(page.IsVisible); Assert.Equal(1, page.Opacity);
            Assert.True(page.Transitions is null || page.Transitions.Count == 0);
            Assert.Contains("selected", window.FindControl<StackPanel>("NavigationItems")!.Children.OfType<Button>().Single(b => b.Tag as string == "Clients").Classes);
            var previousMotion = DesktopApplication.ReduceMotion;
            try
            {
                DesktopApplication.ReduceMotion = true;
                var dialog = new BrandResourcesWindow();
                Assert.Contains("reduce-motion", dialog.Classes);
            }
            finally { DesktopApplication.ReduceMotion = previousMotion; }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Shared_overlays_animate_once_on_open_and_respect_reduced_motion() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Imprimantes");
            await (Task)Invoke(owner, "LoadPrintersAsync")!;
            var actions = owner.FindControl<StackPanel>("PrinterList")!.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is Flyout);
            var flyout = (Flyout)actions.Tag!;
            flyout.ShowAt(actions); Dispatcher.UIThread.RunJobs();
            var surface = Assert.IsType<Border>(flyout.Content);
            Assert.NotNull(surface.Transitions);
            Assert.InRange(surface.Opacity, .92, 1);
            await Task.Delay(200); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, surface.Opacity);
            flyout.Hide();
            Invoke(owner, "ShowSearchState", "Recherche", "Exemple");
            var search = owner.FindControl<Border>("SearchResultsPanel")!;
            var opening = search.Transitions;
            Assert.NotNull(opening);
            Invoke(owner, "ShowSearchState", "Aucun résultat", "Autre recherche");
            Assert.Same(opening, search.Transitions); // typing must not replay an entrance
            Invoke(owner, "DismissSearchResults");
            var animatedDialog = new CreateClientWindow(); var animatedDone = animatedDialog.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            Assert.NotNull(owner.FindControl<Border>("SidePanelScrim")!.Transitions);
            animatedDialog.Close(false); Assert.False(await animatedDone);
            owner.Classes.Add("reduce-motion");
            flyout.ShowAt(actions); Dispatcher.UIThread.RunJobs();
            Assert.Null(surface.Transitions); Assert.Equal(1, surface.Opacity);
            flyout.Hide();
            Invoke(owner, "ShowSearchState", "Recherche", "Exemple");
            Assert.Null(search.Transitions); Assert.Equal(1, search.Opacity);
            Invoke(owner, "DismissSearchResults");
            var dialog = new CreateClientWindow(); var done = dialog.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var scrim = owner.FindControl<Border>("SidePanelScrim");
            Assert.NotNull(scrim); Assert.Null(scrim.Transitions); Assert.Equal(1, scrim.Opacity);
            Assert.Null(owner.FindControl<Border>("SidePanel")!.Transitions);
            dialog.Close(false); Assert.False(await done);
            Assert.False(owner.FindControl<Grid>("SidePanelOverlay")!.IsVisible);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Settings_categories_keep_related_controls_together_and_switch_without_reloading() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Invoke(window, "ShowWorkspace", "Paramètres");
            await (Task)Invoke(window, "LoadSettingsAsync")!;
            var categories = window.FindControl<WrapPanel>("SettingsCategories");
            Assert.NotNull(categories);
            var cards = window.FindControl<StackPanel>("StorageList")!.Children;
            Assert.Equal(4, cards.Count(c => c.IsVisible)); // company information + activity thresholds + mobile app + quote pricing
            Assert.All(cards.Where(c => c.IsVisible), c => Assert.Equal("atelier", Assert.IsType<Border>(c).Tag));
            var storage = categories.Children.OfType<Button>().Single(b => b.Tag as string == "storage");
            storage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, cards.Count(c => c.IsVisible));
            Assert.All(cards.Where(c => c.IsVisible), c => Assert.Equal("storage", Assert.IsType<Border>(c).Tag));
            window.Width = 1360; window.Height = 620; window.Show(); window.UpdateLayout();
            var scroll = window.FindControl<ScrollViewer>("SettingsScroll");
            Assert.NotNull(scroll);
            scroll.Offset = new Vector(0, 200);
            categories.Children.OfType<Button>().Single(b => b.Tag as string == "atelier").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Settings_search_finds_controls_across_categories_and_clears_back_to_the_selected_section() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Paramètres");
            await (Task)Invoke(owner, "LoadSettingsAsync")!;
            Invoke(owner, "SettingsCategory_Click", new Button { Tag = "storage" }, new RoutedEventArgs());
            var cards = owner.FindControl<StackPanel>("StorageList")!.Children.ToArray();
            Text(owner, "SettingsSearchBox", "electricite");
            var visible = cards.OfType<Border>().Where(c => c.IsVisible).ToArray();
            Assert.Single(visible); Assert.Equal("atelier", visible[0].Tag);
            Assert.Contains("recherche", owner.FindControl<TextBlock>("SettingsSectionTitle")!.Text!, StringComparison.OrdinalIgnoreCase);
            Text(owner, "SettingsSearchBox", "arborescence");
            Assert.Contains(cards.OfType<Border>(), c => c.IsVisible && c.Tag as string == "organisation");
            Text(owner, "SettingsSearchBox", "introuvable-xyz");
            Assert.DoesNotContain(cards, c => c.IsVisible);
            Assert.True(owner.FindControl<Control>("SettingsEmptyState")!.IsVisible);
            Click(owner, "ClearSettingsSearchButton");
            Dispatcher.UIThread.RunJobs();
            Assert.False(owner.FindControl<Control>("SettingsEmptyState")!.IsVisible);
            Assert.Equal(3, cards.Count(c => c.IsVisible));
            Assert.All(cards.Where(c => c.IsVisible), c => Assert.Equal("storage", ((Border)c).Tag));
            Assert.Equal(cards, owner.FindControl<StackPanel>("StorageList")!.Children.ToArray());
            Text(owner, "SettingsSearchBox", "stockage");
            Invoke(owner, "EstimateWorkshopSettings_Click", null, new RoutedEventArgs());
            Assert.Equal("", owner.FindControl<TextBox>("SettingsSearchBox")!.Text);
            for (var i = 0; i < 100 && owner.FindControl<StackPanel>("StorageList")!.Children.Count == 0; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(4, owner.FindControl<StackPanel>("StorageList")!.Children.Count(c => c.IsVisible));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Settings_search_scrolls_to_the_matching_setting_and_keeps_typing_focus() => Run(async () =>
    {
        var owner = await Open(); owner.Width = 960; owner.Height = 760; owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Paramètres"); await (Task)Invoke(owner, "LoadSettingsAsync")!;
            var search = owner.FindControl<TextBox>("SettingsSearchBox")!; search.Focus();
            Text(owner, "SettingsSearchBox", "prestations BNC");
            await Task.Delay(50); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var scroll = owner.FindControl<ScrollViewer>("SettingsScroll")!;
            var target = owner.FindControl<StackPanel>("StorageList")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Cotisations · prestations BNC");
            Assert.True(scroll.Offset.Y > 0);
            var position = target.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(position.Y, 0, scroll.Bounds.Height - target.Bounds.Height + 1);
            Assert.Contains("settings-search-current", target.Classes);
            Assert.Same(search, owner.FocusManager!.GetFocusedElement());
            Text(owner, "SettingsSearchBox", "cotisations");
            await Task.Delay(30); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var first = owner.FindControl<StackPanel>("StorageList")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("settings-search-current"));
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            var next = owner.FindControl<StackPanel>("StorageList")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("settings-search-current"));
            Assert.NotSame(first, next);
            Text(owner, "SettingsSearchBox", "prestations BNC"); Text(owner, "SettingsSearchBox", "");
            await Task.Delay(30); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroll.Offset.Y);
            Assert.DoesNotContain(target.Classes, c => c.StartsWith("settings-search-", StringComparison.Ordinal));
            var folder = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "settings-search"); Directory.CreateDirectory(output);
            foreach (var width in new[] { 960, 1440 })
            {
                owner.Width = width; owner.Height = width == 960 ? 760 : 960;
                Text(owner, "SettingsSearchBox", ""); Text(owner, "SettingsSearchBox", "prestations BNC");
                await Task.Delay(80); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = owner.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(output, $"match-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Workspace_pages_render_with_sample_data_and_compact_filters_remain_usable() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateIndividual(_root, "Camille", "Martin", "MARTIN");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        foreach (var name in new[] { "Supports de fixation · série de 40", "Boîtier de commande sur mesure", "Prototype de charnière" })
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, name);
        var projectStore = new MS3DPRINT.Manager.Core.Projects.ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(_root));
        foreach (var project in new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root))
        {
            var profile = project.Profile!;
            projectStore.Update(profile with
            {
                Status = profile.ProjectName.StartsWith("Supports") ? MS3DPRINT.Manager.Core.Projects.ProjectStatus.InProgress : profile.Status,
                DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(profile.ProjectName.StartsWith("Prototype") ? -1 : 3))
            });
        }
        var window = await Open();
        try
        {
            window.Width = 1440; window.Height = 960; window.Show();
            var folder = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "interface"); Directory.CreateDirectory(output);
            async Task Capture(string page, string method, string file, params object?[] args)
            {
                Invoke(window, "ShowWorkspace", page);
                window.FindControl<TextBlock>("PageTitle")!.Text = page == "Accueil" ? "Votre atelier" : page;
                window.FindControl<TextBlock>("PageSubtitle")!.Text = "Espace de démonstration · données de test";
                if (method == "PreloadAsync") await window.PreloadAsync(force: true);
                else await (Task)Invoke(window, method, args)!;
                await Task.Delay(240);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(output, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            await Capture("Accueil", "PreloadAsync", "home-initial.png");
            foreach (var (page, method, file) in new[] { ("Projets", "LoadProjectsAsync", "projects.png"), ("Clients", "LoadClientsAsync", "clients.png"), ("Filaments", "LoadFilamentsAsync", "filaments.png"), ("Imprimantes", "LoadPrintersAsync", "printers.png"), ("Collections", "LoadCollectionsAsync", "collections.png"), ("Paramètres", "LoadSettingsAsync", "settings.png"), ("Corbeille", "LoadTrashAsync", "trash.png") })
                await Capture(page, method, file);
            foreach (var category in new[] { "organisation", "storage", "application" })
            {
                Invoke(window, "SettingsCategory_Click", new Button { Tag = category }, new RoutedEventArgs());
                await Capture("Paramètres", "LoadSettingsAsync", "settings-" + category + ".png");
            }
            var demoProject = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
            demoProject = demoProject with { Profile = demoProject.Profile! with { Description = "Pièce de démonstration : vérifier les cotes avant de lancer la fabrication.", Notes = "Préparer un prototype pour validation client.", DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(4)) } };
            new EstimateStore(_root).Create(new EstimateProfile(Guid.NewGuid(), "Prototype · exemple de devis", "PLA", "Machine", new(MaterialCost: 10), new(10, 10, 42, 42), DateTimeOffset.UtcNow, ProjectReference: demoProject.Reference));
            await Capture("Projets", "LoadProjectDirectoryAsync", "project-detail.png", demoProject, demoProject.ProjectPath);
            Invoke(window, "ShowModelViewer", new object?[] { null });
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var viewer = window.CaptureRenderedFrame()) { Assert.NotNull(viewer); viewer.Save(Path.Combine(output, "viewer.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            Invoke(window, "CloseModelViewer");
            foreach (var (dialog, file) in new (DialogWindow, string)[] { (new CreateClientWindow(), "client-panel.png"), (new CreatePrinterWindow(), "printer-panel.png"), (new CreateProjectWindow(new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root)), "project-panel.png") })
            {
                var done = dialog.ShowDialog<bool>(window);
                await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var panelBitmap = window.CaptureRenderedFrame(); Assert.NotNull(panelBitmap);
                panelBitmap.Save(Path.Combine(output, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                dialog.Close(false); await done;
            }
            var motionFolder = Path.Combine(output, "motion"); Directory.CreateDirectory(motionFolder);
            var frameNumber = 0;
            async Task MotionFrames(int count, int delay)
            {
                for (var frame = 0; frame < count; frame++)
                {
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var captured = window.CaptureRenderedFrame(); Assert.NotNull(captured);
                    captured.Save(Path.Combine(motionFolder, $"{frameNumber++:000}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    await Task.Delay(delay);
                }
            }
            await Capture("Accueil", "PreloadAsync", "home.png");
            await MotionFrames(1, 350);
            foreach (var page in new[] { "Projets", "Clients" })
            {
                Invoke(window, "ShowWorkspace", page); window.FindControl<TextBlock>("PageTitle")!.Text = page;
                await MotionFrames(8, 40); await MotionFrames(1, 350);
            }
            var motionDialog = new CreateClientWindow(); var motionDone = motionDialog.ShowDialog<bool>(window);
            await MotionFrames(9, 40); await MotionFrames(1, 350);
            motionDialog.Close(false); await motionDone;
            window.Width = 960; window.Height = 760;
            await Capture("Accueil", "PreloadAsync", "home-compact.png");
            await Capture("Projets", "LoadProjectsAsync", "projects-compact.png");
            var filters = window.FindControl<WrapPanel>("ProjectStatusFilters")!;
            var create = window.FindControl<StackPanel>("ProjectsPanelActions")!.Children.OfType<Button>().Last();
            Assert.True(filters.Bounds.Width > 500);
            Assert.True(create.Bounds.Width >= 100);
            Assert.True(filters.TranslatePoint(default, window)!.Value.Y >= create.TranslatePoint(default, window)!.Value.Y + create.Bounds.Height);
            await Capture("Projets", "LoadProjectDirectoryAsync", "project-detail-compact.png", demoProject, demoProject.ProjectPath);
            foreach (var category in new[] { "atelier", "organisation", "storage", "application" })
            {
                Invoke(window, "SettingsCategory_Click", new Button { Tag = category }, new RoutedEventArgs());
                await Capture("Paramètres", "LoadSettingsAsync", "settings-" + category + "-compact.png");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Price_summary_updates_after_commercial_changes_and_clears_when_inputs_are_invalid() => Run(async () =>
    {
        var window = await Open();
        try
        {
            window.Show(); window.UpdateLayout();
            var placeholder = window.FindControl<TextBox>("SearchBox")!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
            Assert.Equal(1d, placeholder.Opacity);
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var summary = window.FindControl<StackPanel>("EstimatePriceSummary");
            Assert.NotNull(summary);
            var floor = window.FindControl<TextBlock>("EstimateFloorValue")!;
            Assert.Contains("€", floor.Text!);
            Field<TextBox>(window, "_estimateCommercial").Text = "1";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Contains("warning", window.FindControl<Border>("EstimateProfitStatus")!.Classes);
            Assert.Contains("plancher", window.FindControl<TextBlock>("EstimateProfitStatusText")!.Text!);
            Text(window, "EstimateQuantity", "0");
            Assert.False(summary.IsVisible);
            Assert.False((bool)Invoke(window, "CalculateEstimate")!);
            Assert.False(summary.IsVisible);
            Assert.False(window.FindControl<Border>("EstimateProfitStatus")!.IsVisible);
            Assert.Equal("—", window.FindControl<TextBlock>("EstimateFinal")!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Optional_costs_stay_in_the_price_when_collapsed_and_modelling_remains_discoverable() => Run(async () =>
    {
        var window = await Open();
        try
        {
            var costs = Field<Expander>(window, "_estimateOptionalCosts");
            Assert.False(costs.IsExpanded);
            Text(window, "EstimatePackaging", "25");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var price = Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice;
            costs.IsExpanded = true;
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(price, Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice);
            Assert.Contains("1 poste", Field<TextBlock>(window, "_estimateOptionalCostSummary").Text!);
            Invoke(window, "EstimateScope_Click", new Button { Tag = "1" }, new RoutedEventArgs());
            Assert.True(costs.IsExpanded);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Project_sheet_starts_an_estimate_linked_to_its_project() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Support personnalisé");
        foreach (var title in new[] { "Consommable machine", "Plaque EPW175S", "Cache Pot Collection Automne", "Pions d’Échecs", "Calle d’arrêt machine", "Boîtier de commande avec assemblage et contrôle des dimensions pour le client" })
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, title);
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
        var window = await Open();
        try
        {
            await (Task)Invoke(window, "StartProjectEstimateAsync", project)!;
            var selected = Assert.IsType<MS3DPRINT.Manager.Core.Projects.ProjectSummary>(window.FindControl<ComboBox>("EstimateProject")!.SelectedItem);
            Assert.Equal(project.Reference, selected.Reference);
            Assert.Contains(project.Title(), window.FindControl<TextBox>("EstimateName")!.Text!);
            Assert.True(window.FindControl<Border>("EstimatesWorkspacePanel")!.IsVisible);
            var name = window.FindControl<TextBox>("EstimateName")!;
            var picker = window.FindControl<ComboBox>("EstimateProject")!;
            window.Width = 1440; window.Height = 960; window.Show(); window.UpdateLayout();
            Assert.DoesNotContain(name.GetVisualAncestors(), ancestor => ancestor is Expander);
            var scope = window.FindControl<Grid>("EstimateScope")!;
            Assert.True(name.TranslatePoint(default, window)!.Value.Y < scope.TranslatePoint(default, window)!.Value.Y);
            Assert.True(picker.TranslatePoint(default, window)!.Value.Y < scope.TranslatePoint(default, window)!.Value.Y);
            var option = Assert.IsAssignableFrom<Control>(picker.ItemTemplate!.Build(selected));
            Assert.True(double.IsPositiveInfinity(option.MaxWidth));
            var labels = option.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text).ToArray();
            Assert.Contains(selected.Title(), labels);
            Assert.Contains($"{selected.Reference} · {selected.ClientTitle()}", labels);
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "quote-identity"); Directory.CreateDirectory(output);
            foreach (var width in new[] { 1800, 1440, 960 })
            {
                window.Width = width; window.Height = width == 960 ? 760 : 960;
                await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using (var bitmap = window.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, $"identity-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                picker.IsDropDownOpen = true;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var popupBorder = Assert.IsType<Border>(picker.GetVisualDescendants().OfType<Popup>().Single().Child);
                Assert.Equal(((ISolidColorBrush)Application.Current!.FindResource("SurfaceBrush")!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(popupBorder.Background).Color);
                var container = Assert.IsType<ComboBoxItem>(picker.ContainerFromIndex(0));
                var itemText = container.GetVisualDescendants().OfType<TextBlock>().First();
                Assert.InRange(itemText.TranslatePoint(default, container)!.Value.X, 8, 20);
                using (var bitmap = window.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, $"projects-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                picker.IsDropDownOpen = false;
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Project_history_keeps_readable_estimates_when_an_unrelated_file_is_corrupt() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Prototype");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).First();
        var quote = new EstimateProfile(Guid.NewGuid(), "Prototype validé", "PLA", "Machine", new(MaterialCost: 1), new(1, 1, 12, 12), DateTimeOffset.UtcNow, ProjectReference: project.Reference);
        new EstimateStore(_root).Create(quote);
        File.WriteAllText(Path.Combine(_root, ".ms3dprint-manager", "estimates", Guid.NewGuid() + ".json"), "{ json incomplet");
        var window = await Open();
        try
        {
            var sheet = Assert.IsType<Border>(Invoke(window, "BuildProjectOverview", project));
            var sections = Assert.IsType<StackPanel>(sheet.Child).Children.OfType<Expander>();
            var history = Assert.Single(sections);
            var open = Assert.Single(Assert.IsType<StackPanel>(history.Content).Children.OfType<Button>());
            Assert.Contains("Prototype validé", Assert.IsType<TextBlock>(open.Content).Text!);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Empty_project_filter_can_be_cleared_without_losing_the_project_list() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).First();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Prototype");
        var window = await Open();
        try
        {
            await (Task)Invoke(window, "LoadProjectsAsync")!;
            Text(window, "ProjectFilterBox", "introuvable");
            Invoke(window, "ClearProjectFilters_Click", null, new RoutedEventArgs());
            Assert.Equal("", window.FindControl<TextBox>("ProjectFilterBox")!.Text);
            Assert.True(window.FindControl<StackPanel>("ProjectList")!.Children.Count >= 2);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Quick_estimate_calculates_live_and_saves_directly_on_one_page() => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations"); window.UpdateLayout();
            Invoke(window, "ResetEstimate_Click", null, new RoutedEventArgs());
            Assert.True(window.FindControl<StackPanel>("EstimateStep1")!.IsVisible);
            Assert.Null(window.FindControl<Grid>("EstimateStepper"));
            Assert.True(window.FindControl<Button>("EstimateSaveButton")!.IsVisible);
            Assert.Contains(window.FindControl<Border>("EstimatePriceRail")!, Field<TextBox>(window, "_estimateCommercial").GetVisualAncestors());
            Text(window, "EstimateWeight", "125");
            Text(window, "EstimateQuantity", "3");
            await Task.Delay(350); Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Field<EstimateInputs>(window, "_latestEstimateInputs").Quantity);
            Assert.NotEqual("—", window.FindControl<TextBlock>("EstimateFinal")!.Text);
            Click(window, "EstimateSaveButton");
            var saved = Assert.Single(new EstimateStore(_root).LoadAll());
            Assert.Equal(3, saved.Inputs.Quantity);
            Assert.True(window.FindControl<StackPanel>("EstimateStep1")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Plate_input_exposes_capacity_and_confirms_total_production_without_extra_steps() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateQuantity", "40");
            window.FindControl<ComboBox>("EstimatePrintDataMode")!.SelectedIndex = 1;
            Assert.True(Field<Control>(window, "_estimateCapacityField").IsVisible);
            Assert.Contains(window.FindControl<StackPanel>("EstimatePrintInputs")!, Field<Control>(window, "_estimateCapacityField").GetVisualAncestors());
            Field<TextBox>(window, "_estimateCapacity").Text = "10";
            Text(window, "EstimateWeight", "250"); Text(window, "EstimateHours", "9");
            await Task.Delay(350); Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, Field<EstimateResult>(window, "_latestEstimateResult").V2!.Runs);
            var summary = Field<TextBlock>(window, "_estimateProductionSummary").Text!;
            Assert.Contains("40 pièces", summary); Assert.Contains("4 plateaux", summary); Assert.Contains("36", summary);
            Click(window, "EstimateSaveButton");
            Assert.Equal(10, Assert.Single(new EstimateStore(_root).LoadAll()).Inputs.V2!.Production!.PiecesPerPlate);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("2", true)]
    [InlineData("0", false)]
    public Task Control_enter_saves_directly_and_rejects_invalid_quantity(string quantity, bool valid) => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateQuantity", quantity);
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control };
            window.FindControl<Border>("EstimatesWorkspacePanel")!.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.Equal(valid ? 1 : 0, new EstimateStore(_root).LoadAll().Count);
            if (!valid) Assert.True(Field<TextBlock>(window, "_estimateInputMessage").IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Repeated_save_does_not_create_a_quote_from_reset_defaults_and_new_input_rearms_it() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateQuantity", "3");
            Click(window, "EstimateSaveButton");
            Click(window, "EstimateSaveButton");
            await Task.Delay(300); Dispatcher.UIThread.RunJobs();
            Click(window, "EstimateSaveButton");
            Assert.Single(new EstimateStore(_root).LoadAll());
            Text(window, "EstimateName", "Deuxième commande");
            Click(window, "EstimateSaveButton");
            Assert.Equal(2, new EstimateStore(_root).LoadAll().Count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Duration_in_hours_and_minutes_is_exact_and_invalid_minutes_block_save() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Field<TextBox>(window, "_estimateDurationHours").Text = "2";
            Field<TextBox>(window, "_estimateDurationMinutes").Text = "30";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2.5m, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Production!.HoursPerRun);
            Field<TextBox>(window, "_estimateDurationMinutes").Text = "60";
            Click(window, "EstimateSaveButton");
            Assert.Empty(new EstimateStore(_root).LoadAll());
            Assert.True(Field<TextBlock>(window, "_estimateDurationError").IsVisible);
            Text(window, "EstimateHours", "2,123456789");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2.123456789m, Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Production!.HoursPerRun);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Modelling_only_does_not_require_a_hidden_print_quantity_or_reuse_it_in_the_name() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Text(window, "EstimateQuantity", "0");
            Invoke(window, "EstimateScope_Click", new Button { Tag = "1" }, new RoutedEventArgs());
            Text(window, "EstimateDesign", "50");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(1, Field<EstimateInputs>(window, "_latestEstimateInputs").Quantity);
            Click(window, "EstimateSaveButton");
            Assert.Equal("Modélisation", Assert.Single(new EstimateStore(_root).LoadAll()).Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task New_estimates_hide_engine_selection_and_legacy_conversion_requires_an_explicit_action() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Assert.False(window.FindControl<StackPanel>("EstimateV2VersionHost")!.IsVisible);
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2, Field<EstimateInputs>(window, "_latestEstimateInputs").EngineVersion);
            var legacy = new EstimateProfile(Guid.NewGuid(), "Archive", "Test PLA", "A", new(MaterialCost: 1), new(1, 1, 123.45m, 123.45m), DateTimeOffset.UtcNow, 100, 2, 2);
            new EstimateStore(_root).Create(legacy);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = legacy }, new RoutedEventArgs());
            Assert.True(window.FindControl<StackPanel>("EstimateV2VersionHost")!.IsVisible);
            Field<Button>(window, "_estimateConvertV2").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(2, Field<EstimateInputs>(window, "_latestEstimateInputs").EngineVersion);
            Assert.Equal(123.45m, Assert.Single(new EstimateStore(_root).LoadAll()).Result.FinalPrice);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Deletion_requires_explicit_confirmation_and_escape_cancels(bool trash) => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            DialogWindow NewDialog() => trash ? new ConfirmTrashWindow("Devis client", "Le devis sera déplacé.") : new ConfirmRemovalWindow("Préparation", "Le poste sera retiré du devis.");
            var dialog = NewDialog();
            var pending = dialog.ShowDialog<bool>(window);
            Dispatcher.UIThread.RunJobs();
            typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [new KeyEventArgs { Key = Key.Enter, Source = new TextBlock() }]);
            Assert.False(pending.IsCompleted);
            typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [new KeyEventArgs { Key = Key.Escape }]);
            Assert.False(await pending);
            dialog = NewDialog();
            pending = dialog.ShowDialog<bool>(window);
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("danger")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await pending);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Global_search_handles_pasted_text_empty_results_and_dismissal_without_stale_results() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        foreach (var title in new[] { "Supports de fixation · série de 40 pièces", "Boîtier de commande personnalisé avec assemblage et contrôle des dimensions", "Prototype de charnière" })
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, title);
        var window = await Open(); window.Width = 1440; window.Height = 960; window.Show();
        try
        {
            var search = window.FindControl<TextBox>("SearchBox")!;
            var results = window.FindControl<StackPanel>("SearchResults")!;
            search.Text = "Horizon";
            await Task.Delay(450); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.NotEmpty(results.Children.OfType<Button>());
            Assert.True(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "workspace-search"); Directory.CreateDirectory(output);
            foreach (var width in new[] { 1440, 960 })
            {
                window.Width = width; window.Height = width == 960 ? 760 : 960;
                await Task.Delay(100); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(output, $"results-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            search.Text = "introuvablezzzzz";
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Assert.Empty(results.Children.OfType<Button>());
            Assert.True(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            Assert.Contains("Aucun résultat", window.FindControl<TextBlock>("SearchResultsHeading")!.Text!);
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var bitmap = window.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "no-results.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            search.Text = "Horizon";
            Invoke(window, "Window_KeyDown", window, new KeyEventArgs { Key = Key.Escape });
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            search.Text = "Horizon ";
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Click(window, "ClearGlobalSearchButton");
            Assert.True(string.IsNullOrEmpty(search.Text));
            Assert.False(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            search.Text = "Horizon";
            Invoke(window, "ShowWorkspace", "Clients");
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            search.Text = "Horizon ";
            Invoke(window, "ShowModelViewer", new object?[] { null });
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Global_search_opens_the_client_sheet_directly_with_enter() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var window = await Open(); window.Show();
        try
        {
            window.FindControl<TextBox>("SearchBox")!.Text = "Horizon";
            await Task.Delay(450); Dispatcher.UIThread.RunJobs();
            Invoke(window, "SearchBox_KeyDown", window, new KeyEventArgs { Key = Key.Enter });
            await Task.Delay(150); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Atelier Horizon", Field<MS3DPRINT.Manager.Core.Clients.ClientSummary>(window, "_currentClientDetail").DisplayName);
            Assert.False(window.FindControl<Border>("SearchResultsPanel")!.IsVisible);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.False(window.FindControl<Border>("ClientsWorkspacePanel")!.IsVisible);
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Project_priorities_save_filter_combine_and_sort_in_both_directions() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        foreach (var title in new[] { "Priorité haute", "Priorité moyenne", "Priorité basse", "Sans priorité" })
            new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, title);
        var owner = await Open(); owner.Width = 1440; owner.Height = 960; owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Projets");
            await (Task)Invoke(owner, "LoadProjectsAsync")!;
            foreach (var (title, priority) in new[] { ("Priorité haute", ProjectPriority.High), ("Priorité moyenne", ProjectPriority.Medium), ("Priorité basse", ProjectPriority.Low) })
            {
                var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single(item => item.ProjectName == title);
                await (Task)Invoke(owner, "SetProjectPriorityAsync", project, priority)!;
            }
            ProjectSummary[] Rows() => owner.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("table-row")).Select(button => (ProjectSummary)button.Tag!).ToArray();
            var filter = Field<ComboBox>(owner, "_projectPriorityFilterBox");
            filter.SelectedIndex = 1; Assert.Equal(ProjectPriority.High, Assert.Single(Rows()).Priority);
            Invoke(owner, "ProjectFilter_Click", new Button { Tag = "EN_COURS" }, new RoutedEventArgs());
            Assert.Empty(Rows());
            Invoke(owner, "ClearProjectFilters_Click", owner, new RoutedEventArgs()); Assert.Equal(4, Rows().Length);
            var sort = Field<Button>(owner, "_projectPrioritySortButton");
            sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { ProjectPriority.High, ProjectPriority.Medium, ProjectPriority.Low, ProjectPriority.None }, Rows().Select(project => project.Priority));
            sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { ProjectPriority.Low, ProjectPriority.Medium, ProjectPriority.High, ProjectPriority.None }, Rows().Select(project => project.Priority));
            filter.SelectedIndex = 4; Assert.Equal(ProjectPriority.None, Assert.Single(Rows()).Priority);
            Invoke(owner, "ClearProjectFilters_Click", owner, new RoutedEventArgs());
            sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "project-priority"); Directory.CreateDirectory(output);
            owner.FindControl<StackPanel>("ToastHost")!.Children.Clear();
            foreach (var width in new[] { 1440, 960 })
            {
                owner.Width = width; owner.Height = width == 960 ? 760 : 960;
                await Task.Delay(300); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = owner.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(output, $"projects-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            var high = Rows().Single(project => project.Priority == ProjectPriority.High);
            var dialog = new EditProjectWindow(high.Profile!);
            Assert.Equal(ProjectPriority.High, dialog.Priority);
            var chooser = (ProjectPriorityPicker)typeof(EditProjectWindow).GetField("_priority", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            ((Button)((StackPanel)chooser.Children[0]).Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(ProjectPriority.Medium, dialog.Priority);
            var pending = dialog.ShowDialog<bool>(owner);
            await Task.Delay(300); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var bitmap = owner.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "edit-priority.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            dialog.Close(false); Assert.False(await pending);
            Assert.Equal(ProjectPriority.High, new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single(project => project.Reference == high.Reference).Priority);
            await (Task)Invoke(owner, "SetProjectPriorityAsync", high, ProjectPriority.None)!;
            Assert.Equal(ProjectPriority.None, new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single(project => project.Reference == high.Reference).Priority);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Imported_source_is_kept_by_default_and_deleted_only_after_explicit_acceptance() => Run(async () =>
    {
        var source = Path.Combine(_root, "original.txt"); File.WriteAllText(source, "copie vérifiée");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var imported = new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(_root).Import(source, directory, "fichier-projet.txt");
        var owner = await Open(); owner.Width = 1400; owner.Height = 900; owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ConfirmImportedSourceRemovalAsync", _root, source, imported)!;
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
            var dialog = Assert.IsType<ConfirmImportSourceWindow>(Field<DialogWindow>(owner, "_panel"));
            Assert.True(File.Exists(source));
            typeof(DialogWindow).GetMethod("HandleKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [new KeyEventArgs { Key = Key.Enter, Source = new TextBlock() }]);
            Assert.False(pending.IsCompleted);
            var body = owner.FindControl<Border>("SidePanelBody")!;
            body.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Conserver la source").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.True(File.Exists(source));
            pending = (Task)Invoke(owner, "ConfirmImportedSourceRemovalAsync", _root, source, imported)!;
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "import-source"); Directory.CreateDirectory(output);
            using (var bitmap = owner.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "confirmation.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            body.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("danger")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.False(File.Exists(source));
            Assert.Equal("copie vérifiée", File.ReadAllText(imported));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Project_import_batch_keeps_per_file_naming_confirmation_and_can_stop(bool stopSecond) => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Projet import");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single();
        var directory = Directory.CreateDirectory(Path.Combine(project.ProjectPath, "imports")).FullName;
        var sources = new[] { Path.Combine(_root, "premier.txt"), Path.Combine(_root, "second.txt") };
        File.WriteAllText(sources[0], "premier"); File.WriteAllText(sources[1], "second");
        var owner = await Open(); owner.Width = 1400; owner.Height = 900; owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportProjectFilesAsync", _root, project, directory, sources)!;
            await WaitForDialog<ImportNameWindow>(owner);
            await Task.Delay(300); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "import-batch"); Directory.CreateDirectory(output);
            using (var bitmap = owner.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "naming.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            DialogAction(owner, "Garder le nom d’origine");
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            var second = await WaitForDialog<ImportNameWindow>(owner);
            Assert.True(File.Exists(sources[0]));
            if (stopSecond) second.Close(false);
            else
            {
                DialogAction(owner, "Importer ce fichier");
                await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Supprimer la source");
            }
            await pending;
            Assert.Equal("premier", File.ReadAllText(Path.Combine(directory, "premier.txt")));
            Assert.Equal(stopSecond, File.Exists(sources[1]));
            Assert.Equal(stopSecond ? 1 : 2, Directory.GetFiles(directory).Length);
            if (!stopSecond) Assert.Equal("second", File.ReadAllText(Directory.GetFiles(directory).Single(path => Path.GetFileName(path) != "premier.txt")));
            Assert.False(Field<bool>(owner, "_importBatchActive"));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Workspace_import_batch_preserves_collisions_and_continues_with_other_files() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "premier.txt"), Path.Combine(_root, "second.txt") };
        File.WriteAllText(sources[0], "nouveau"); File.WriteAllText(sources[1], "second");
        File.WriteAllText(Path.Combine(directory, "premier.txt"), "existant");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source");
            await pending;
            Assert.Equal("existant", File.ReadAllText(Path.Combine(directory, "premier.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(directory, "second.txt")));
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.False(Field<bool>(owner, "_importBatchActive"));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Import_progress_can_stop_after_the_current_copy_and_keeps_a_persistent_summary() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "premier.txt"), Path.Combine(_root, "second.txt") };
        foreach (var source in sources) File.WriteAllText(source, "contenu");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<ConfirmImportSourceWindow>(owner);
            var panel = owner.FindControl<Border>("ImportBatchPanel"); Assert.NotNull(panel); Assert.True(panel.IsVisible);
            Assert.Contains("1 sur 2", owner.FindControl<TextBlock>("ImportBatchHeading")!.Text);
            Assert.Contains("premier.txt", owner.FindControl<TextBlock>("ImportBatchFile")!.Text);
            await Task.Delay(260); owner.UpdateLayout();
            var body = owner.FindControl<Border>("SidePanelBody")!;
            foreach (var button in body.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var x = button.TranslatePoint(default, body)!.Value.X;
                Assert.InRange(x, 0, body.Bounds.Width - button.Bounds.Width);
            }
            CaptureDraftUi(owner, "import-source-with-stop");
            DialogAction(owner, "Conserver et arrêter l’import"); await pending;
            Assert.True(File.Exists(Path.Combine(directory, "premier.txt"))); Assert.False(File.Exists(Path.Combine(directory, "second.txt")));
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.Contains("1 importé", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
            Assert.Contains("1 non traité", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
            Assert.Equal(1d, owner.FindControl<ProgressBar>("ImportBatchProgress")!.Value);
            Assert.True(panel.IsVisible); Assert.False(Field<bool>(owner, "_importBatchActive"));
            foreach (var width in new[] { 960d, 1600d }) { owner.Width = width; await Task.Delay(180); CaptureDraftUi(owner, "import-summary-" + width); }
            Click(owner, "ImportBatchDismiss"); Assert.False(panel.IsVisible);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Import_summary_retains_failure_details_without_overwriting_existing_files() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "collision.txt"), Path.Combine(_root, "valide.txt") };
        foreach (var source in sources) File.WriteAllText(source, "contenu");
        File.WriteAllText(Path.Combine(directory, "collision.txt"), "existant");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source"); await pending;
            var summary = owner.FindControl<TextBlock>("ImportBatchSummary"); Assert.NotNull(summary);
            Assert.Contains("1 importé", summary.Text); Assert.Contains("1 échec", summary.Text); Assert.Contains("0 non traité", summary.Text);
            Assert.Contains("collision.txt", owner.FindControl<SelectableTextBlock>("ImportBatchErrors")!.Text);
            Assert.True(owner.FindControl<Expander>("ImportBatchDetails")!.IsVisible);
            Assert.Equal(2d, owner.FindControl<ProgressBar>("ImportBatchProgress")!.Value);
            Assert.Equal("existant", File.ReadAllText(Path.Combine(directory, "collision.txt")));
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            owner.Width = 960; owner.FindControl<Expander>("ImportBatchDetails")!.IsExpanded = true;
            await Task.Delay(180); CaptureDraftUi(owner, "import-errors-960");
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Stop_during_copy_preserves_that_file_and_a_new_batch_resets_the_stop_request() => Run(async () =>
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var sources = new[] { Path.Combine(_root, "premier.txt"), Path.Combine(_root, "second.txt") };
        foreach (var source in sources) File.WriteAllText(source, "contenu");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, sources)!;
            // Request stop on the UI thread before the copy's continuation can open its confirmation.
            owner.FindControl<Button>("ImportBatchStop")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Field<bool>(owner, "_importStopRequested"));
            Assert.False(owner.FindControl<Button>("ImportBatchStop")!.IsEnabled);
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source"); await pending;
            Assert.True(File.Exists(Path.Combine(directory, "premier.txt"))); Assert.False(File.Exists(Path.Combine(directory, "second.txt")));
            var next = (Task)Invoke(owner, "ImportWorkspaceFilesAsync", _root, directory, new[] { sources[1] })!;
            Assert.False(Field<bool>(owner, "_importStopRequested"));
            Assert.True(owner.FindControl<Button>("ImportBatchStop")!.IsEnabled);
            await WaitForDialog<ConfirmImportSourceWindow>(owner); DialogAction(owner, "Conserver la source"); await next;
            Assert.Equal("Import terminé", owner.FindControl<TextBlock>("ImportBatchHeading")!.Text);
            Assert.Contains("1 importé · 0 échec · 0 non traité", owner.FindControl<TextBlock>("ImportBatchSummary")!.Text);
            Assert.True(File.Exists(Path.Combine(directory, "second.txt"))); Assert.All(sources, source => Assert.True(File.Exists(source)));
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Project_import_batch_stops_without_changing_the_newly_opened_page() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier Horizon", "HORIZON");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Projet import");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single();
        var directory = Directory.CreateDirectory(Path.Combine(project.ProjectPath, "imports")).FullName;
        var source = Path.Combine(_root, "source.txt"); File.WriteAllText(source, "source");
        var owner = await Open(); owner.Show();
        try
        {
            var pending = (Task)Invoke(owner, "ImportProjectFilesAsync", _root, project, directory, new[] { source })!;
            await WaitForDialog<ImportNameWindow>(owner);
            DialogAction(owner, "Garder le nom d’origine");
            typeof(MainWindow).GetMethod("ShowWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, ["Clients"]);
            await pending;
            Assert.True(owner.FindControl<Border>("ClientsWorkspacePanel")!.IsVisible);
            Assert.True(File.Exists(source));
            Assert.Empty(Directory.GetFiles(directory));
            Assert.False(Field<bool>(owner, "_importBatchActive"));
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Estimate_entries_are_removed_only_after_confirmation(bool machine) => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            if (machine) Invoke(window, "AddEstimateMachine", new EstimateMachineAllocation("Machine de test", 2, 3));
            else Invoke(window, "AddEstimateCost", new EstimateCostItem("Préparation spécifique", 25, CostFrequency.PerOrder));
            var list = Field<StackPanel>(window, machine ? "_estimateAllocations" : "_estimateExtraCosts");
            var panel = Assert.IsType<StackPanel>(list.Children.Last());
            var remove = panel.Children.OfType<Button>().Single(button => button.Content is string text && text.StartsWith("Retirer"));
            var initial = list.Children.Count;
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<ConfirmRemovalWindow>(Field<DialogWindow>(window, "_panel"));
            Assert.Equal(initial, list.Children.Count);
            Field<DialogWindow>(window, "_panel").Close(false);
            await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.Equal(initial, list.Children.Count);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Border>("SidePanelBody")!.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("danger")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.Equal(initial - 1, list.Children.Count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Saved_history_sits_below_the_estimator_and_still_reopens_a_quote() => Run(async () =>
    {
        var saved = new EstimateProfile(Guid.NewGuid(), "Supports de fixation", "Test PLA", "Machine A", new(MaterialCost: 1), new(1, 1, 123.45m, 123.45m), DateTimeOffset.UtcNow);
        var historyStore = new EstimateStore(_root);
        historyStore.Create(saved);
        foreach (var title in new[] { "Impression 3D · 40 pièces", "Impression 3D · 2 pièces sans livraison", "Boîtier de commande personnalisé avec assemblage et contrôle des dimensions", "Pince" })
            historyStore.Create(saved with { Id = Guid.NewGuid(), Name = title, CreatedAt = saved.CreatedAt.AddDays(-1) });
        var window = await Open(); window.Width = 1400; window.Height = 960; window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            var layout = window.FindControl<Grid>("EstimateLayout")!;
            var history = layout.GetVisualDescendants().OfType<Expander>().Single(section => section.Header is StackPanel header && header.Children.OfType<TextBlock>().Any(text => text.Text == "Historique des estimations"));
            Assert.False(history.IsExpanded);
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "estimate-history"); Directory.CreateDirectory(output);
            foreach (var width in new[] { 1400, 960 })
            {
                window.Width = width; window.Height = width == 960 ? 760 : 960;
                history.IsExpanded = true;
                await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var rail = window.FindControl<Border>("EstimatePriceRail")!;
                Assert.True(window.FindControl<ScrollViewer>("EstimateFormScroll")!.Bounds.Height >= 160);
                Assert.True(history.TranslatePoint(default, window)!.Value.Y >= rail.TranslatePoint(default, window)!.Value.Y + rail.Bounds.Height);
                Assert.True(history.Bounds.Width > rail.Bounds.Width * 2);
                using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(output, $"history-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            var list = window.FindControl<StackPanel>("SavedEstimateList")!;
            var reopen = list.GetVisualDescendants().OfType<Button>().Single(button => button.Tag is EstimateProfile quote && quote.Id == saved.Id && !button.Classes.Contains("icon-action"));
            Invoke(window, "LoadSavedEstimate_Click", reopen, new RoutedEventArgs());
            Assert.Equal(saved.Name, window.FindControl<TextBox>("EstimateName")!.Text);
            Assert.Equal(saved.Result, Field<EstimateResult>(window, "_latestEstimateResult"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Workshop_page_summaries_follow_the_loaded_inventory_and_empty_trash() => Run(async () =>
    {
        var window = await Open();
        try
        {
            await (Task)Invoke(window, "LoadPrintersAsync")!;
            await (Task)Invoke(window, "LoadFilamentsAsync")!;
            await (Task)Invoke(window, "LoadTrashAsync")!;
            Assert.Equal("1 imprimante · 1 active", Field<TextBlock>(window, "_printerPageSummary").Text);
            Assert.Equal("1 référence · tarifs au kilogramme", Field<TextBlock>(window, "_filamentPageSummary").Text);
            Assert.Equal("Aucun élément supprimé", Field<TextBlock>(window, "_trashPageSummary").Text);
            var printer = new PrinterStore(_root).LoadAll().Single();
            new PrinterStore(_root).Trash(printer.Id);
            await (Task)Invoke(window, "LoadPrintersAsync")!;
            await (Task)Invoke(window, "LoadTrashAsync")!;
            Assert.Equal("0 imprimante · 0 active", Field<TextBlock>(window, "_printerPageSummary").Text);
            Assert.Equal("1 élément récupérable", Field<TextBlock>(window, "_trashPageSummary").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Subcontracted_manufacturing_replaces_internal_costs_and_survives_save_reload() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateWeight", "999"); Text(window, "EstimateHours", "invalid");
            window.FindControl<ComboBox>("EstimateFilament")!.SelectedItem = null;
            Field<CheckBox>(window, "_estimateOutsource").IsChecked = true;
            Field<TextBox>(window, "_estimateSupplier").Text = "Atelier partenaire";
            Field<TextBox>(window, "_estimateSupplierCost").Text = "100";
            Field<TextBox>(window, "_estimateSupplierShipping").Text = "10";
            Text(window, "EstimateQuantity", "40"); Text(window, "EstimateDesign", "20");
            Text(window, "EstimateMinimum", "0"); Text(window, "EstimateMargin", "20"); Text(window, "EstimateFailure", "15");
            Invoke(window, "SetEstimateCharges", new EstimateCharges(CommercialPercent: 20));
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var result = Field<EstimateResult>(window, "_latestEstimateResult");
            Assert.Equal(130, result.RawCost); Assert.Equal(130, result.ExpectedCost);
            Assert.Equal(162.50m, result.V2!.FloorPrice); Assert.Equal(216.67m, result.FinalPrice);
            Assert.Equal(0, result.V2.MaterialCost); Assert.Equal(0, result.V2.MachineCost); Assert.Equal(0, result.V2.FailureProvision);
            window.Width = 1360; window.Height = 1000; window.Show();
            window.FindControl<TextBlock>("PageTitle")!.Text = "Estimations";
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "subcontracting"); Directory.CreateDirectory(output);
            using (var bitmap = window.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "supplier-quote.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            Click(window, "EstimateSaveButton");
            var saved = Assert.Single(new EstimateStore(_root).LoadAll());
            Assert.False(EstimateScopeRules.IsModellingOnly(saved));
            Assert.Equal(0, saved.MachineHours);
            var archived = saved with { Result = saved.Result with { FinalPrice = 999m } };
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = archived }, new RoutedEventArgs());
            await Task.Delay(250); Dispatcher.UIThread.RunJobs();
            Assert.Equal(999m, Field<EstimateResult>(window, "_latestEstimateResult").FinalPrice);
            Assert.True(Field<CheckBox>(window, "_estimateOutsource").IsChecked);
            Assert.Equal("100", Field<TextBox>(window, "_estimateSupplierCost").Text);
            Assert.Equal("40", Field<TextBox>(window, "_estimateSupplierQuantity").Text);
            Field<TextBox>(window, "_estimateCommercial").Text = "100";
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.True(Field<EstimateResult>(window, "_latestEstimateResult").V2!.BelowBreakEven);
            Field<CheckBox>(window, "_estimateOutsource").IsChecked = false;
            window.FindControl<ComboBox>("EstimateFilament")!.SelectedIndex = 0;
            Text(window, "EstimateWeight", "100"); Text(window, "EstimateHours", "1");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Null(Field<EstimateInputs>(window, "_latestEstimateInputs").V2!.Subcontracting);
            Assert.Equal(0, Field<EstimateResult>(window, "_latestEstimateResult").V2!.SubcontractingCost);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Redesigned_estimate_shows_correct_cost_breakdown_and_marks_pending_prices() => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateDesign", "50"); Text(window, "EstimateFailure", "5");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var result = Field<EstimateResult>(window, "_latestEstimateResult");
            Assert.Equal(EstimateCalculatorV2.Money(result.ExpectedCost).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " €",
                window.FindControl<TextBlock>("EstimateCostTotal")!.Text);
            Assert.True(window.FindControl<Control>("EstimateCostBreakdown")!.IsVisible);
            var price = window.FindControl<TextBlock>("EstimateFinal")!.Text;
            Text(window, "EstimateQuantity", "40");
            Assert.Equal(price, window.FindControl<TextBlock>("EstimateFinal")!.Text);
            Assert.True(window.FindControl<TextBlock>("EstimateCalculationState")!.IsVisible);
            Text(window, "EstimateQuantity", "0");
            Assert.False((bool)Invoke(window, "CalculateEstimate")!);
            Assert.False(window.FindControl<Control>("EstimateCostBreakdown")!.IsVisible);
            Assert.Equal("—", window.FindControl<TextBlock>("EstimateFinal")!.Text);
            window.Classes.Add("reduce-motion");
            Text(window, "EstimateQuantity", "1");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Invoke(window, "BuildEstimateRecap");
            Assert.True(window.FindControl<Control>("EstimateStep1")!.IsVisible);
            Assert.Null(window.FindControl<Grid>("EstimateCostStrip")!.Transitions);
            window.Classes.Remove("reduce-motion");
            window.Width = 960; window.UpdateLayout();
            Assert.Null(window.FindControl<Grid>("EstimateStepper"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Estimate_receipt_uses_the_discounted_manual_price_and_zero_cost_chart_is_safe() => Run(async () =>
    {
        var window = await Open(); window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateQuantity", "40"); Text(window, "EstimateWeight", "0"); Text(window, "EstimateHours", "0");
            Text(window, "EstimateMinimum", "0");
            Field<TextBox>(window, "_estimateCommercial").Text = "100";
            Text(window, "EstimateDiscount", "10");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Invoke(window, "BuildEstimateRecap"); window.UpdateLayout();
            var result = Field<EstimateResult>(window, "_latestEstimateResult");
            Assert.Equal(90, result.FinalPrice);
            var text = window.FindControl<StackPanel>("EstimateRecap")!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Equal("90,00 €", window.FindControl<TextBlock>("EstimateFinal")!.Text);
            Assert.Contains("2,25 € / pièce", window.FindControl<TextBlock>("EstimateUnitValue")!.Text);
            // Recap is collapsed by default; inspect its logical children after expanding.
            var details = window.FindControl<StackPanel>("EstimateRecap")!;
            var amounts = details.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("− 10,00 €", amounts);
            Assert.Equal("0,00 €", window.FindControl<TextBlock>("EstimateCostTotal")!.Text);
            Assert.All(window.FindControl<Grid>("EstimateCostStrip")!.ColumnDefinitions, c => Assert.Equal(0, c.Width.Value));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Complementary_costs_are_entered_as_totals_and_do_not_scale_with_quantity() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateQuantity", "40"); Text(window, "EstimateWeight", "0"); Text(window, "EstimateHours", "0");
            Text(window, "EstimateMinimum", "0"); Text(window, "EstimateFailure", "5");
            Text(window, "EstimateDesign", "50"); Text(window, "EstimateLabor", "20"); Text(window, "EstimatePostProcessing", "80");
            Text(window, "EstimateConsumables", "10"); Text(window, "EstimatePackaging", "10"); Text(window, "EstimateMisc", "5");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var result = Field<EstimateResult>(window, "_latestEstimateResult");
            Assert.Equal(175, result.RawCost);
            Assert.Equal(145m + 30m / .95m, result.ExpectedCost);
            window.Width = 1360; window.Height = 1000; window.Show();
            window.FindControl<TextBlock>("PageTitle")!.Text = "Estimations";
            var section = Field<Expander>(window, "_estimateOptionalCosts"); section.IsExpanded = true;
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            section.BringIntoView(new Rect(0, 0, section.Bounds.Width, 400));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "simple-costs"); Directory.CreateDirectory(output);
            using (var bitmap = window.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "order-totals.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            Text(window, "EstimateQuantity", "80");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            Assert.Equal(175, Field<EstimateResult>(window, "_latestEstimateResult").RawCost);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Existing_v2_unit_costs_are_shown_as_order_totals_without_changing_the_quote() => Run(async () =>
    {
        var window = await Open();
        try
        {
            var inputs = new EstimateInputs(Quantity: 40, DesignCost: 50, LaborCost: 2, PostProcessingCost: 3, ConsumablesCost: 1,
                PackagingShippingCost: 10, FailureRatePercent: 5, EngineVersion: 2, V2: new());
            var result = EstimateCalculator.Calculate(inputs);
            var quote = new EstimateProfile(Guid.NewGuid(), "Ancien V2", "Test PLA", "A", inputs, result, DateTimeOffset.UtcNow);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = quote }, new RoutedEventArgs());
            Assert.Equal("80", window.FindControl<TextBox>("EstimateLabor")!.Text);
            Assert.Equal("120", window.FindControl<TextBox>("EstimatePostProcessing")!.Text);
            Assert.Equal("40", window.FindControl<TextBox>("EstimateConsumables")!.Text);
            Assert.Equal(result, Field<EstimateResult>(window, "_latestEstimateResult"));
            Text(window, "EstimateDiscount", "1");
            Assert.True((bool)Invoke(window, "CalculateEstimate")!);
            var updated = Field<EstimateInputs>(window, "_latestEstimateInputs");
            Assert.Equal(result.RawCost, EstimateCalculator.Calculate(updated).RawCost);
            Assert.Equal(result.ExpectedCost, EstimateCalculator.Calculate(updated).ExpectedCost);
            var store = new EstimateStore(_root);
            store.Create(quote with { Inputs = updated, Result = EstimateCalculator.Calculate(updated) });
            Assert.Equal(result.ExpectedCost, EstimateCalculator.Calculate(store.LoadAll().Single().Inputs).ExpectedCost);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Numeric_input_keeps_the_text_and_insertion_point_together_on_the_left() => Run(async () =>
    {
        var window = await Open(); window.Width = 1360; window.Height = 900; window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var quantity = window.FindControl<TextBox>("EstimateQuantity")!;
            quantity.Focus(); quantity.SelectAll(); window.KeyTextInput("40");
            window.UpdateLayout();
            var presenter = quantity.GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.InRange(presenter.TextLayout.HitTestTextPosition(0).X, 0, 2);
            Assert.Equal(2, quantity.CaretIndex);
            window.KeyTextInput("2"); Assert.Equal("402", quantity.Text);
            window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            window.KeyTextInput("1"); Assert.Equal("1402", quantity.Text);
            quantity.SelectAll(); window.KeyTextInput("40");
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "numeric-input"); Directory.CreateDirectory(output);
            using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(output, "quantity-focused.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Browser_history_restores_collection_folders_and_viewer_and_resets_for_another_workspace() => Run(async () =>
    {
        var directory = Path.Combine(_root, "Collection test"); Directory.CreateDirectory(directory);
        var child = Path.Combine(directory, "Sous-dossier"); Directory.CreateDirectory(child);
        var window = await Open();
        try
        {
            Invoke(window, "ShowWorkspace", "Collections");
            Invoke(window, "OpenFolder_Click", new Button { Tag = directory }, new RoutedEventArgs());
            for (var attempt = 0; attempt < 100 && !window.FindControl<Control>("FolderWorkspacePanel")!.IsVisible; attempt++) await Task.Delay(10);
            Assert.True(window.FindControl<Control>("FolderWorkspacePanel")!.IsVisible);
            typeof(MainWindow).GetField("_folderWorkspaceDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, child);
            await (Task)Invoke(window, "LoadFolderWorkspaceAsync")!;
            Invoke(window, "ShowModelViewer", new object?[] { null });
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.False(window.FindControl<Control>("ViewerWorkspacePanel")!.IsVisible);
            Assert.Equal(child, Field<string>(window, "_folderWorkspaceDirectory"));
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.Equal(directory, Field<string>(window, "_folderWorkspaceDirectory"));
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.True(window.FindControl<Control>("CollectionsWorkspacePanel")!.IsVisible);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", 1)!);
            Assert.True(window.FindControl<Control>("FolderWorkspacePanel")!.IsVisible);
            Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", Path.Combine(_root, "Autre espace"));
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", 1)!);
        }
        finally { Environment.SetEnvironmentVariable("MS3DPRINT_WORKSPACE", _root); window.Close(); }
    });

    [Fact]
    public Task Browser_mouse_buttons_and_alt_arrows_preserve_the_estimate_draft() => Run(async () =>
    {
        var window = await Open(); window.Show();
        async Task Settle()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                if (!Field<bool>(window, "_historyNavigationBusy")) return;
                await Task.Delay(10);
            }
            Assert.Fail("La navigation ne se termine pas.");
        }
        try
        {
            Invoke(window, "ShowWorkspace", "Estimations");
            Text(window, "EstimateName", "Brouillon conservé");
            Text(window, "EstimateWeight", "123,45");
            Invoke(window, "ShowWorkspace", "Imprimantes");
            window.MouseDown(new Point(600, 100), MouseButton.XButton1);
            window.MouseUp(new Point(600, 100), MouseButton.XButton1);
            await Settle();
            Assert.True(window.FindControl<Control>("EstimatesWorkspacePanel")!.IsVisible);
            Assert.Equal("Brouillon conservé", window.FindControl<TextBox>("EstimateName")!.Text);
            Assert.Equal("123,45", window.FindControl<TextBox>("EstimateWeight")!.Text);
            window.MouseDown(new Point(600, 100), MouseButton.XButton2);
            window.MouseUp(new Point(600, 100), MouseButton.XButton2);
            await Settle();
            Assert.True(window.FindControl<Control>("PrintersWorkspacePanel")!.IsVisible);
            window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null); window.KeyRelease(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null);
            await Settle();
            Assert.True(window.FindControl<Control>("EstimatesWorkspacePanel")!.IsVisible);
            window.KeyPress(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight, null); window.KeyRelease(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight, null);
            await Settle();
            Assert.True(window.FindControl<Control>("PrintersWorkspacePanel")!.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Browser_history_goes_back_forward_and_discards_the_forward_branch() => Run(async () =>
    {
        var window = await Open();
        try
        {
            Invoke(window, "ShowWorkspace", "Clients");
            Invoke(window, "ShowWorkspace", "Imprimantes");
            var returning = (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!;
            Invoke(window, "NavigationButton_Click", new Button { Tag = "Filaments" }, new RoutedEventArgs());
            Assert.True(await returning);
            Assert.True(window.FindControl<Control>("ClientsWorkspacePanel")!.IsVisible);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", 1)!);
            Assert.True(window.FindControl<Control>("PrintersWorkspacePanel")!.IsVisible);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Invoke(window, "ShowWorkspace", "Filaments");
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", 1)!);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.True(window.FindControl<Control>("DashboardContent")!.IsVisible);
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Browser_history_restores_project_folders_and_blocks_navigation_during_a_form() => Run(async () =>
    {
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientCreator().CreateProfessional(_root, "Atelier", "ATELIER");
        var client = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceClientReader().Load(_root).Single();
        new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectCreator().CreateTracked(_root, client, 2026, "Prototype");
        var project = new MS3DPRINT.Manager.Desktop.Dashboard.WorkspaceProjectReader().Load(_root).Single();
        var directory = Path.Combine(project.ProjectPath, "Sous-dossier"); Directory.CreateDirectory(directory);
        var window = await Open(); window.Show();
        try
        {
            Invoke(window, "ShowWorkspace", "Clients");
            await (Task)Invoke(window, "LoadClientDetailAsync", client)!;
            Invoke(window, "ShowWorkspaceForDetail", "Projets");
            await (Task)Invoke(window, "LoadProjectDirectoryAsync", project, project.ProjectPath)!;
            await (Task)Invoke(window, "LoadProjectDirectoryAsync", project, directory)!;
            var projectStore = new MS3DPRINT.Manager.Core.Projects.ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(_root));
            projectStore.Update(project.Profile! with { Notes = "Modifié depuis la première visite" });
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.Equal("Fiche projet", window.FindControl<TextBlock>("ProjectsPanelTitle")!.Text);
            Assert.Contains(window.FindControl<StackPanel>("ProjectList")!.GetVisualDescendants().OfType<Button>(), b => b.Tag is MS3DPRINT.Manager.Core.Projects.ProjectSummary { Profile.Notes: "Modifié depuis la première visite" });
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.True(window.FindControl<Control>("ClientsWorkspacePanel")!.IsVisible);
            Assert.False(Field<bool>(window, "_clientListMode"));
            Assert.True(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", 1)!);
            var dialog = new CreatePrinterWindow(); var done = dialog.ShowDialog<bool>(window);
            Assert.False(await (Task<bool>)Invoke(window, "NavigateHistoryAsync", -1)!);
            Assert.Same(dialog, Field<DialogWindow>(window, "_panel"));
            dialog.Close(false); await done;
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Printer_action_menu_closes_when_edit_opens_and_rate_stays_exact() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        try
        {
            Invoke(owner, "ShowWorkspace", "Imprimantes");
            await (Task)Invoke(owner, "LoadPrintersAsync")!;
            var actions = owner.FindControl<StackPanel>("PrinterList")!.GetVisualDescendants().OfType<Button>().Single(b => b.Tag is Flyout);
            actions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var flyout = (Flyout)actions.Tag!;
            Assert.True(flyout.IsOpen);
            var menu = (StackPanel)((Border)flyout.Content!).Child!;
            var edit = Assert.IsType<Button>(menu.Children[0]);
            var trash = menu.Children.OfType<Button>().Single(button => button.Classes.Contains("menu-destructive"));
            Assert.Single(menu.Children.OfType<Border>());
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "action-menus"); Directory.CreateDirectory(output);
            foreach (var width in new[] { 1440, 960 })
            {
                flyout.Hide(); owner.Width = width; owner.Height = width == 960 ? 760 : 960;
                await Task.Delay(250); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout();
                flyout.ShowAt(actions);
                foreach (var hovered in new[] { edit, trash })
                {
                    ((IPseudoClasses)hovered.Classes).Set(":pointerover", true);
                    Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var presenter = hovered.GetVisualDescendants().OfType<ContentPresenter>().First();
                    var brush = (ISolidColorBrush)Application.Current!.FindResource(hovered == trash ? "DangerSoftBrush" : "HoverBrush")!;
                    Assert.Equal(brush.Color, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
                    using (var bitmap = owner.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, $"{(hovered == trash ? "trash" : "edit")}-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
                    ((IPseudoClasses)hovered.Classes).Set(":pointerover", false);
                }
            }
            ((Button)menu.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(flyout.IsOpen);
            Field<DialogWindow>(owner, "_panel").Close(false);

            var now = DateTimeOffset.UtcNow;
            var rate = 2500m / 3000m;
            var dialog = new CreatePrinterWindow(new(Guid.NewGuid(), "Bambu Lab", "H2D", "H2D", rate, "", true, now, now, 2500, 3000));
            var input = (TextBox)typeof(CreatePrinterWindow).GetField("_manualRate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Assert.Equal("0,833333", input.Text);
            var done = dialog.ShowDialog<bool>(owner);
            typeof(CreatePrinterWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [null, new RoutedEventArgs()]);
            Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(rate, dialog.HourlyRate);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task Printer_annual_amortization_saves_without_a_historical_lifetime() => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        var now = DateTimeOffset.UtcNow;
        var original = new PrinterProfile(Guid.NewGuid(), "Bambu Lab", "H2D", "Bambu Lab H2D", .83m, "", true, now, now, 2500m, 0, 500, 4m, 1000m);
        var dialog = new CreatePrinterWindow(original);
        T Read<T>(string name) => (T)typeof(CreatePrinterWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var done = dialog.ShowDialog<bool>(owner);
        try
        {
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(folder.FullName, "MS3DPRINT.Manager.sln"))) folder = folder.Parent!;
            var output = Path.Combine(folder.FullName, "outputs", "qa", CaptureRun, "printer-v2"); Directory.CreateDirectory(output);
            using (var bitmap = owner.CaptureRenderedFrame()) { Assert.NotNull(bitmap); bitmap.Save(Path.Combine(output, "amortissement-annuel.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); }
            typeof(CreatePrinterWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [null, new RoutedEventArgs()]);
            Assert.False(Read<TextBlock>("_error").IsVisible, Read<TextBlock>("_error").Text);
            Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(.625m, dialog.HourlyRate);
            Assert.Equal(0, dialog.LifetimeHours);
            var saved = original with { HourlyRate = dialog.HourlyRate, LifetimeHours = dialog.LifetimeHours, AmortizationYears = dialog.AmortizationYears, ProductiveHoursPerYear = dialog.ProductiveHoursPerYear };
            new PrinterStore(_root).Create(saved);
            Assert.Equal(.625m, PrinterCostCalculator.HourlyRate(new PrinterStore(_root).LoadAll().Single(p => p.Id == saved.Id)));
        }
        finally { if (!done.IsCompleted) dialog.Close(false); owner.Close(); }
    });

    [Theory]
    [InlineData(2500, 8000, true, .3125)]
    [InlineData(0, 0, true, .83)]
    public Task Printer_old_profiles_keep_their_hourly_cost(decimal price, int lifetime, bool manual, decimal expected) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        var now = DateTimeOffset.UtcNow;
        var dialog = new CreatePrinterWindow(new(Guid.NewGuid(), "Bambu Lab", "H2D", "Bambu Lab H2D", .83m, "ancien volume", true, now, now, price, lifetime));
        var done = dialog.ShowDialog<bool>(owner);
        try
        {
            Assert.Equal(manual, dialog.UseManualHourlyRate);
            typeof(CreatePrinterWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [null, new RoutedEventArgs()]);
            Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(expected, dialog.HourlyRate);
            Assert.Equal(lifetime, dialog.LifetimeHours);
            Assert.Equal("ancien volume", dialog.BuildVolume);
            Assert.Equal(0, dialog.ProductiveHoursPerYear);
            var saved = dialog.Original! with { HourlyRate = dialog.HourlyRate, UseManualHourlyRate = dialog.UseManualHourlyRate, AmortizationYears = dialog.AmortizationYears, ProductiveHoursPerYear = dialog.ProductiveHoursPerYear };
            var other = saved with { PurchasePrice = 0, HourlyRate = 3, UseManualHourlyRate = true };
            Assert.Equal(PrinterCostCalculator.FleetHourlyRate([dialog.Original!, other]), PrinterCostCalculator.FleetHourlyRate([saved, other]));
        }
        finally { if (!done.IsCompleted) dialog.Close(false); owner.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public Task Printer_annual_inputs_are_required_only_in_automatic_mode(bool manual, bool valid) => Run(async () =>
    {
        var owner = await Open(); owner.Show();
        var now = DateTimeOffset.UtcNow;
        var dialog = new CreatePrinterWindow(new(Guid.NewGuid(), "Bambu Lab", "H2D", "Bambu Lab H2D", .83m, "", true, now, now, 2500m, 0));
        T Read<T>(string name) => (T)typeof(CreatePrinterWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var done = dialog.ShowDialog<bool>(owner);
        try
        {
            Read<TextBox>("_years").Text = "0";
            Read<TextBox>("_annualHours").Text = "incorrect";
            Read<CheckBox>("_useManual").IsChecked = manual;
            typeof(CreatePrinterWindow).GetMethod("Save_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [null, new RoutedEventArgs()]);
            if (valid) { Assert.False(Read<TextBlock>("_error").IsVisible, Read<TextBlock>("_error").Text); Assert.True(await done.WaitAsync(TimeSpan.FromSeconds(2))); Assert.Equal(.83m, dialog.HourlyRate); }
            else Assert.True(Read<TextBlock>("_error").IsVisible);
        }
        finally { if (!done.IsCompleted) dialog.Close(false); owner.Close(); }
    });

    [Fact]
    public Task Legacy_cost_fields_are_labelled_as_order_totals() => Run(async () =>
    {
        var window = await Open();
        try
        {
            var input = new EstimateInputs(Quantity: 40, PostProcessingCost: 80);
            var legacy = new EstimateProfile(Guid.NewGuid(), "V1", "Test PLA", "A", input, EstimateCalculator.Calculate(input), DateTimeOffset.UtcNow);
            Invoke(window, "LoadSavedEstimate_Click", new Button { Tag = legacy }, new RoutedEventArgs());
            var box = window.FindControl<TextBox>("EstimatePostProcessing")!;
            var label = ((StackPanel)box.Parent!.Parent!).Children.OfType<TextBlock>().First();
            Assert.Contains("total", label.Text!);
        }
        finally { window.Close(); }
    });
}
