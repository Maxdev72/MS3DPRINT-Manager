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
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Inside a project: folders and files, import, rename, move, trash and opening a file.</summary>
public partial class MainWindow
{
    private async Task LoadProjectDirectoryAsync(MS3DPRINT.Manager.Core.Projects.ProjectSummary project, string directory)
    {
        CancelFileRead();
        RememberProject(project, directory);
        ProjectList.Children.Clear();
        ProjectList.Spacing = 8;
        _projectListMode = false;
        ProjectsPanelTitle.Text = directory == project.ProjectPath ? "Fiche projet" : Path.GetFileName(directory);
        ProjectsPanelSubtitle.Text = $"{project.Reference} · {project.ClientTitle()}";
        ProjectsPanelActions.IsVisible = false;
        ProjectsPanelHeader.IsVisible = false;
        ProjectFilterBar.IsVisible = false;
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        var origin = _projectOriginClient;
        var back = new Button { Content = Icons.Label("IconArrowLeft", directory != project.ProjectPath ? "Dossier parent" : origin is null ? "Retour aux projets" : "Retour au client"), Classes = { "ghost" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        back.Click += async (_, _) =>
        {
            if (directory == project.ProjectPath && origin is not null)
            {
                _projectOriginClient = null;
                ShowWorkspaceForDetail("Clients");
                PageTitle.Text = "Clients";
                PageSubtitle.Text = origin.Title();
                await LoadClientDetailAsync(origin);
            }
            else if (directory == project.ProjectPath)
            {
                PageTitle.Text = "Projets";
                PageSubtitle.Text = PageSubtitles["Projets"];
                await LoadProjectsAsync();
            }
            else await LoadProjectDirectoryAsync(project, new ProjectWorkspaceBrowser().Parent(project.ProjectPath, directory));
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var createFolder = new Button { Content = Icons.Label("IconPlus", "Nouveau dossier"), Tag = (Project: project, Directory: directory), Classes = { "primary-action" } };
        createFolder.Click += CreateProjectFolder_Click;
        var import = new Button { Content = Icons.Label("IconUpload", "Importer des fichiers"), Tag = (Project: project, Directory: directory), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += ImportProjectFile_Click;
        actions.Children.Add(createFolder);
        actions.Children.Add(import);
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(back);
        toolbar.Children.Add(actions);
        ProjectList.Children.Add(toolbar);
        if (directory == project.ProjectPath)
            AddProjectOverview(project);
        ProjectList.Children.Add(new TextBlock { Text = "Fichiers du projet", FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 8, 0, 4) });

        var trail = new List<(string Label, Func<Task>? Go)>();
        if (origin is null) trail.Add(("Projets", GoToProjectsAsync));
        else
        {
            trail.Add(("Clients", GoToClientsAsync));
            trail.Add((origin.Title(), async () => { ShowWorkspaceForDetail("Clients"); PageTitle.Text = "Clients"; await LoadClientDetailAsync(origin); }));
        }
        trail.Add((project.Title(), () => LoadProjectDirectoryAsync(project, project.ProjectPath)));
        var relative = Path.GetRelativePath(project.ProjectPath, directory);
        if (relative != ".")
        {
            var current = project.ProjectPath;
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                var target = current;
                trail.Add((part, () => LoadProjectDirectoryAsync(project, target)));
            }
        }
        SetBreadcrumb(trail.ToArray());
        var reading = LoadFileBrowserAsync(ProjectList, project.ProjectPath, directory, project, () => LoadProjectDirectoryAsync(project, directory));
        // History is ready once the view is installed; its input lock must not cover disk I/O.
        if (!_replayingNavigation) await reading;
    }

    private async void CreateProjectFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        var dialog = new TextPromptWindow("Nouveau dossier", "Nom du sous-dossier à créer.", string.Empty);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var folderName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).CreateFolder(context.Item2, folderName));
            Notify("Dossier créé.");
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    /// <summary>Project sub-folders (two levels) that can receive an imported file.</summary>
    private static IReadOnlyList<ImportDestination> ImportDestinations(string projectPath)
    {
        var folders = new List<ImportDestination>();
        foreach (var first in Directory.EnumerateDirectories(projectPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(first).StartsWith('.')) continue;
            folders.Add(new ImportDestination(Path.GetFileName(first), first));
            foreach (var second in Directory.EnumerateDirectories(first).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                folders.Add(new ImportDestination(Path.GetFileName(first) + " / " + Path.GetFileName(second), second));
        }
        return folders;
    }

    private async void ImportProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        if (_importBatchActive) return;
        var root = WorkspaceRoot;
        var sources = await PickImportSourcesAsync();
        await ImportProjectFilesAsync(root, context.Item1, context.Item2, sources);
    }

    private async Task ImportProjectFilesAsync(string root, ProjectSummary project, string directory, IReadOnlyList<string> sources)
    {
        if (_importBatchActive || sources.Count == 0) return;
        StartImportBatch(sources.Count);
        var navigationKey = CurrentNavigationStop?.Key;
        var importedCount = 0; var failures = 0; var stopped = false;
        try
        {
            var atRoot = string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(project.ProjectPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            var destinations = atRoot ? await Task.Run(() => ImportDestinations(project.ProjectPath)) : null;
            for (var index = 0; index < sources.Count; index++)
            {
                if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                var source = sources[index];
                UpdateImportProgress(index + 1, sources.Count, source, "Choix du nom et du dossier");
                try
                {
                    var originalName = Path.GetFileName(source);
                    string? suggestedFolder = null;
                    if (atRoot)
                    {
                        try { suggestedFolder = Path.Combine(project.ProjectPath, ProjectFileClassifier.Suggest(originalName, project.FolderName, new ProjectTemplateStore(new WorkspaceMetadataPaths(root)).Load()).RelativeDirectory); }
                        catch (ArgumentException) { }
                    }
                    var naming = new ImportNameWindow(originalName, ProjectFileClassifier.SuggestImportName(originalName, project.Reference), project.Reference, destinations, suggestedFolder, index + 1, sources.Count);
                    if (!await naming.ShowDialog<bool>(this)) { stopped = true; break; }
                    if (_importStopRequested || !MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                    var targetDirectory = naming.DestinationDirectory ?? directory;
                    var finalName = naming.FileName;
                    UpdateImportProgress(index + 1, sources.Count, source, "Copie en cours");
                    var imported = await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Import(source, targetDirectory, finalName));
                    importedCount++;
                    ImportBatchProgress.Value = importedCount + failures;
                    if (!MatchesNavigationContext(root, navigationKey)) { stopped = true; break; }
                    UpdateImportProgress(index + 1, sources.Count, source, "Copié · choix pour la source");
                    await ConfirmImportedSourceRemovalAsync(root, source, imported);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { failures++; ImportBatchProgress.Value = importedCount + failures; RecordImportError(Path.GetFileName(source) + " : " + exception.Message); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { stopped = true; RecordImportError(exception.Message); }
        finally { stopped |= _importStopRequested; CompleteImportBatch(importedCount, failures, sources.Count, stopped); }
        if (MatchesNavigationContext(root, navigationKey)) await LoadProjectDirectoryAsync(project, directory);
    }

    private async void FileEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        await OpenFileAsync(path);
    }

    private async void OpenWithFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        if (ExecutableFileGuard.IsExecutable(path))
        {
            Notify("Ce fichier est un programme ou un script : le logiciel ne le lance pas. Ouvrez son dossier dans l’Explorateur si vous êtes sûr de sa provenance.", error: true);
            return;
        }
        try
        {
            var root = WorkspaceRoot;
            if (DesktopPreviewLauncher.OpenWith is { } openWith) { openWith(path); await RememberOpenedFileAsync(root, path); }
            else await OpenFileAsync(path);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { Notify("Impossible d’ouvrir ce fichier avec une autre application : " + exception.Message, error: true); }
    }

    private async void RenameProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        await RenameProjectEntryAsync(context);
    }

    private async Task RenameProjectEntryAsync(ProjectDirectoryContext context)
    {
        var isDirectory = Directory.Exists(context.Path);
        var dialog = new TextPromptWindow(isDirectory ? "Renommer le dossier" : "Renommer le fichier", isDirectory ? "Le dossier conservera son emplacement dans le projet." : "Le fichier conservera son emplacement dans le projet.", Path.GetFileName(context.Path));
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var newName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).RenamePath(context.Path, newName));
            Notify(isDirectory ? "Dossier renommé." : "Fichier renommé.");
            await ReloadDirectoryAsync(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var name = Path.GetFileName(context.Path);
        var isDirectory = Directory.Exists(context.Path);
        if (!await new ConfirmTrashWindow(name, isDirectory ? "Le dossier et son contenu pourront être restaurés depuis la corbeille." : "Le fichier pourra être restauré depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Trash(context.Path, label: name));
            Notify(isDirectory ? "Dossier envoyé dans la corbeille." : "Fichier envoyé dans la corbeille.");
            await ReloadDirectoryAsync(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void MoveProjectEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var isFolder = Directory.Exists(context.Path);
        var destination = await PickWorkspaceFolderAsync(Path.GetDirectoryName(context.Path) ?? WorkspaceRoot,
            "Déplacer « " + Path.GetFileName(context.Path) + " »", isFolder ? context.Path : null);
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var isDirectory = Directory.Exists(context.Path);
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MovePath(context.Path, destination));
            Notify(isDirectory ? "Dossier déplacé." : "Fichier déplacé.");
            await ReloadDirectoryAsync(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task OpenFileAsync(string path)
    {
        var root = WorkspaceRoot;
        if (!File.Exists(path))
        {
            Notify("Ce fichier n’est plus disponible.", error: true);
            return;
        }
        try
        {
            if (IsImage(path))
            {
                var preview = new ImagePreviewWindow(path);
                await RememberOpenedFileAsync(root, path);
                await preview.ShowDialog(this);
                return;
            }

            if (ThreeDFileSupport.IsPreviewable(path))
            {
                ShowModelViewer(path);
                return;
            }

            if (DesktopPreviewLauncher.TryOpenDocument(path))
            { await RememberOpenedFileAsync(root, path); return; }

            if (ExecutableFileGuard.IsExecutable(path))
            {
                Notify("Ce fichier est un programme ou un script : le logiciel ne le lance pas. Ouvrez son dossier dans l’Explorateur si vous êtes sûr de sa provenance.", error: true);
                return;
            }

            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            var opened = false;
            try { opened = launcher is not null && await launcher.LaunchUriAsync(new Uri(path)); }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            if (opened) { await RememberOpenedFileAsync(root, path); return; }

            // Windows has no application for this type of file: a slicer for a 3D model, else the "Open with" window
            if (SlicerLauncher.TryOpen(path) is { } slicer) { await RememberOpenedFileAsync(root, path); Notify($"Ouvert dans {slicer}."); return; }
            if (DesktopPreviewLauncher.OpenWith is { } openWith) { openWith(path); await RememberOpenedFileAsync(root, path); }
            else Notify("Aucune application n’est associée à ce type de fichier.", error: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            Notify("Impossible d’ouvrir ce fichier : " + exception.Message, error: true);
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
}
