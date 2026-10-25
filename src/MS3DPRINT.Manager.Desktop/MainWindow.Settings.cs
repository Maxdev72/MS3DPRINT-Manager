using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Settings page and changelog.</summary>
public partial class MainWindow
{
    private async void Changelog_Click(object? sender, RoutedEventArgs e)
        => await new ChangelogWindow().ShowDialog(this);

    private async void EditProjectTemplate_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        var store = new ProjectTemplateStore(new WorkspaceMetadataPaths(root));
        var dialog = new ProjectTemplateWindow(store.Load());
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            if (dialog.ResetRequested) { store.Reset(); Notify("Modèle standard rétabli pour les nouveaux projets."); }
            else if (dialog.Result is { } template) { store.Save(template); Notify("Arborescence des projets enregistrée."); }
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    /// <summary>Makes <paramref name="root"/> the workspace: creates it with the base folders, remembers it and reloads the data.</summary>
    private async Task<bool> ActivateWorkspaceAsync(string root)
    {
        try
        {
            var full = Path.GetFullPath(root);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(full);
                new MS3DPRINT.Manager.Core.Storage.FolderTreeService().EnsureMainStructure(full);
            });
            _workspaceLocation.Save(full);
            await PreloadAsync(force: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Notify(exception.Message, error: true);
            return false;
        }
    }

    private async void UseSyncFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<string, string> choice }) return;
        if (!await new UseWorkspaceWindow(choice.Item1, choice.Item2).ShowDialog<bool>(this)) return;
        if (await ActivateWorkspaceAsync(choice.Item2))
        {
            Notify("Espace de travail changé : " + choice.Item2);
            await LoadSettingsAsync();
        }
    }

    /// <summary>First launch: asks where to keep the data, offering the synchronised folders found on the computer.</summary>
    private async Task RunWorkspaceSetupAsync()
    {
        var detected = await Task.Run(() => MS3DPRINT.Manager.Core.Storage.SyncFolderDetector.ForThisComputer().Detect());
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), MS3DPRINT.Manager.Core.Storage.SyncFolderDetector.WorkspaceFolderName);
        var dialog = new WorkspaceSetupWindow(detected, local);
        var confirmed = await dialog.ShowDialog<bool>(this);
        var root = confirmed ? dialog.SelectedRoot : local;
        if (confirmed && root is null)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir l’espace de travail", AllowMultiple = false });
            root = folders.FirstOrDefault()?.TryGetLocalPath() is { Length: > 0 } chosen ? chosen : local;
        }
        await ActivateWorkspaceAsync(root ?? local);
    }

    private async void RefreshSettings_Click(object? sender, RoutedEventArgs e) => await LoadSettingsAsync();

    private async void ChangeWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir l’espace de travail", AllowMultiple = false });
        var chosen = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(chosen)) return;
        try
        {
            _workspaceLocation.Save(chosen);
            Notify("Espace de travail changé. Utilisez « Vérifier l’arborescence » pour y créer les dossiers manquants.");
            await PreloadAsync(force: true);
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private async void VerifyWorkspace_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        try
        {
            var result = await Task.Run(() => new MS3DPRINT.Manager.Core.Storage.FolderTreeService().EnsureMainStructure(root));
            Notify(result.CreatedFolders.Count == 0
                ? "Arborescence vérifiée : tous les dossiers existent."
                : $"Arborescence vérifiée : {result.CreatedFolders.Count} dossier(s) ajouté(s).");
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadSettingsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        var load = StartWorkspaceLoad("settings", workspaceRoot);
        StorageList.Children.Clear();
        _settingsIndex.Clear();
        StorageList.Spacing = 16;
        // cards stay readable on a wide window instead of stretching across it
        StorageList.MaxWidth = 920;
        StorageList.HorizontalAlignment = HorizontalAlignment.Stretch;
        var providers = await Task.Run(() => new WorkspaceStorageReader().Load(workspaceRoot));
        if (!IsWorkspaceLoadCurrent(load)) return;
        AddSettingsCard("atelier", BuildCompanySettingsCard(workspaceRoot));
        AddSettingsCard("atelier", BuildThresholdsSettingsCard(workspaceRoot));
        AddSettingsCard("atelier", BuildWorkshopSettingsCard(workspaceRoot));

        Border Card(string title, string description, Control? headerAction, params Control[] content)
        {
            var stack = new StackPanel { Spacing = 14 };
            var icon = title switch { "Performances" => "IconSettings", "Arborescence des projets" => "IconProjects", "Stockage" => "IconCollections", "Espace de travail" => "IconProjects", _ => "IconInfo" };
            stack.Children.Add(SettingsCardHeader(icon, title, description, headerAction));
            foreach (var control in content) stack.Children.Add(control);
            return new Border { Classes = { "card-raised" }, Padding = new Avalonia.Thickness(22, 20), Child = stack };
        }

        // ---- workspace: the folder is shown as a field one can select from, and the three actions follow
        var path = new SelectableTextBlock { Text = workspaceRoot, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 15, LineHeight = 23 };
        var pathField = new Border
        {
            Background = (IBrush?)Avalonia.Application.Current?.FindResource("SurfaceBrush"), CornerRadius = new Avalonia.CornerRadius(8),
            BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(1), Padding = new Avalonia.Thickness(12, 10),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Children = { Icons.Glyph("IconProjects", 18), path } }
        };
        Grid.SetColumn(path, 1);

        var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir l’espace de travail", 16), Tag = workspaceRoot };
        open.Click += OpenFolder_Click;
        var verify = new Button { Content = Icons.Label("IconCheck", "Vérifier l’arborescence", 16) };
        verify.Click += VerifyWorkspace_Click;
        var change = new Button { Content = Icons.Label("IconMove", "Changer d’espace de travail…", 16) };
        change.Click += ChangeWorkspace_Click;
        var fixedByEnvironment = MS3DPRINT.Manager.Core.Storage.WorkspaceLocation.IsFixedByEnvironment;
        change.IsEnabled = !fixedByEnvironment;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { open, verify, change }) { button.Margin = new Avalonia.Thickness(0, 0, 8, 8); actions.Children.Add(button); }
        var workspaceContent = new List<Control> { pathField, actions };
        if (fixedByEnvironment)
            workspaceContent.Add(new TextBlock { Text = "Cet espace de travail est imposé par la variable d’environnement MS3DPRINT_WORKSPACE : il ne peut pas être changé ici.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        AddSettingsCard("storage", Card("Espace de travail", "Le dossier qui contient vos clients, projets et collections.", null, workspaceContent.ToArray()));
        var backupStatus = await Task.Run(() => LoadBackupStatus(workspaceRoot));
        if (!IsWorkspaceLoadCurrent(load)) return;
        AddSettingsCard("storage", BuildBackupSettingsCard(workspaceRoot, backupStatus));

        // ---- project template: folders created in each new project
        var template = new ProjectTemplateStore(new WorkspaceMetadataPaths(workspaceRoot)).Load();
        var editTemplate = new Button { Content = Icons.Label("IconEdit", "Modifier l’arborescence…", 16), HorizontalAlignment = HorizontalAlignment.Left };
        editTemplate.Click += EditProjectTemplate_Click;
        var preview = new StackPanel { Spacing = 2 };
        foreach (var folder in template.Folders)
            preview.Children.Add(new TextBlock { Text = folder.Category is { } category ? $"{folder.Path}  ·  {ProjectTemplate.Label(category)}" : folder.Path, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
        AddSettingsCard("organisation", Card("Arborescence des projets", "Dossiers créés dans chaque nouveau projet et classement des fichiers importés. Les projets existants ne changent pas.", null, preview, editTemplate));

        // ---- performance: how much of the processor the heavy work may take
        var performanceStore = new PerformanceSettings();
        var modes = Enum.GetValues<MS3DPRINT.Estimator.PerformanceMode>();
        var currentMode = MS3DPRINT.Estimator.Parallelism.Mode;
        var modeBox = new ComboBox { ItemsSource = modes, SelectedItem = currentMode, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        modeBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MS3DPRINT.Estimator.PerformanceMode>((mode, _) => new TextBlock { Text = PerformanceSettings.Label(mode) });
        var modeText = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        void ShowMode(MS3DPRINT.Estimator.PerformanceMode mode) => modeText.Text = PerformanceSettings.Description(mode) +
            $" Ici : {MS3DPRINT.Estimator.Parallelism.ThreadsFor(mode, Environment.ProcessorCount)} thread(s) sur {Environment.ProcessorCount}.";
        ShowMode(currentMode);
        modeBox.SelectionChanged += (_, _) =>
        {
            if (modeBox.SelectedItem is not MS3DPRINT.Estimator.PerformanceMode chosen || chosen == MS3DPRINT.Estimator.Parallelism.Mode) return;
            MS3DPRINT.Estimator.Parallelism.Apply(chosen);
            ShowMode(chosen);
            try { performanceStore.Save(chosen); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Notify("Le réglage est appliqué mais n’a pas pu être enregistré : " + exception.Message, error: true); }
        };
        AddSettingsCard("application", Card("Performances", "Part du processeur que les calculs lourds peuvent utiliser : estimations, orientation automatique, aperçus et rendu logiciel.", null, modeBox, modeText));

        // ---- storage: the synchronised folders found on this computer; the clients of the services do the synchronising
        var providerRows = new StackPanel { Spacing = 0 };
        foreach (var provider in providers)
        {
            var isLocal = provider.Kind == MS3DPRINT.Manager.Core.Storage.StorageProviderKind.LocalFolder;
            var inUse = provider.IsAvailable && !string.IsNullOrWhiteSpace(provider.RootPath) && MS3DPRINT.Manager.Core.Storage.StorageProviderCatalog.IsInside(workspaceRoot, provider.RootPath);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), MinHeight = 52, ColumnSpacing = 12 };
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { new TextBlock { Text = provider.DisplayName, FontWeight = FontWeight.Medium } } };
            if (provider.IsAvailable && !string.IsNullOrWhiteSpace(provider.RootPath))
                name.Children.Add(new TextBlock { Text = provider.RootPath, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(name);
            var state = isLocal ? (inUse ? "Espace de travail" : "Dossier local") : inUse ? "Espace de travail" : provider.IsAvailable ? "Détecté" : "Non détecté";
            var badge = new Border { Classes = { "badge", inUse ? "info" : provider.IsAvailable ? "success" : "neutral" }, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = state } };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            if (!isLocal && provider.IsAvailable && !inUse && provider.RootPath is { } syncPath)
            {
                var use = new Button { Content = "Utiliser", Tag = (provider.DisplayName, MS3DPRINT.Manager.Core.Storage.SyncFolderDetector.WorkspaceIn(new MS3DPRINT.Manager.Core.Storage.SyncFolder(provider.Kind, syncPath))), VerticalAlignment = VerticalAlignment.Center };
                use.Click += UseSyncFolder_Click;
                Grid.SetColumn(use, 2);
                row.Children.Add(use);
            }
            providerRows.Children.Add(new Border { BorderBrush = (IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = row });
        }
        var refresh = new Button { Content = Icons.Label("IconRefresh", "Actualiser", 16), Classes = { "ghost" }, VerticalAlignment = VerticalAlignment.Top };
        refresh.Click += RefreshSettings_Click;
        AddSettingsCard("storage", Card("Stockage", "Nextcloud, Google Drive et Dropbox : le logiciel utilise le dossier synchronisé par leur application, il ne se connecte pas à ces services lui-même.", refresh, providerRows));

        // ---- about: the version tells which build is installed
        var informational = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? string.Empty;
        var plus = informational.IndexOf('+');
        var version = plus < 0 ? informational : informational[..plus];
        var revision = plus >= 0 ? informational[(plus + 1)..] : string.Empty;
        var versionText = version.Length == 0 ? string.Empty : "Version " + version + (revision.Length >= 7 ? " · révision " + revision[..7] : string.Empty);
        var changelog = new Button { Content = Icons.Label("IconFile", "Journal des modifications", 16), HorizontalAlignment = HorizontalAlignment.Left };
        changelog.Click += Changelog_Click;
        var about = new List<Control>();
        if (versionText.Length > 0) about.Add(new TextBlock { Text = versionText, Classes = { "caption" } });
        about.Add(changelog);
        var licenses = new Button { Content = Icons.Label("IconInfo", "Identité visuelle et licences", 16), HorizontalAlignment = HorizontalAlignment.Left };
        licenses.Click += async (_, _) => await new BrandResourcesWindow().ShowDialog<bool>(this);
        about.Add(licenses);
        AddSettingsCard("application", Card("MS3DPRINT Manager", "Gestion de vos clients, projets, collections, filaments, imprimantes et estimations.", null, about.ToArray()));
        UpdateSettingsCategory();
    }
}
