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
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Inside a project: folders and files, import, rename, move, trash and opening a file.</summary>
public partial class MainWindow
{
    private async Task LoadProjectDirectoryAsync(MS3DPRINT.Manager.Core.Projects.ProjectSummary project, string directory)
    {
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
                ShowWorkspace("Clients");
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
        var import = new Button { Content = Icons.Label("IconUpload", "Importer un fichier"), Tag = (Project: project, Directory: directory), HorizontalAlignment = HorizontalAlignment.Left };
        import.Click += ImportProjectFile_Click;
        actions.Children.Add(createFolder);
        actions.Children.Add(import);
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(back);
        toolbar.Children.Add(actions);
        ProjectList.Children.Add(toolbar);
        if (directory == project.ProjectPath)
            AddProjectOverview(project);

        var trail = new List<(string Label, Func<Task>? Go)>();
        if (origin is null) trail.Add(("Projets", GoToProjectsAsync));
        else
        {
            trail.Add(("Clients", GoToClientsAsync));
            trail.Add((origin.Title(), async () => { ShowWorkspace("Clients"); PageTitle.Text = "Clients"; await LoadClientDetailAsync(origin); }));
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
        try
        {
            var entries = await Task.Run(() => new ProjectWorkspaceBrowser().List(project.ProjectPath, directory));
            if (entries.Count == 0)
            {
                ProjectList.Children.Add(CreateEmptyState("IconFile", "Ce dossier est vide", "Importez un fichier ou créez un sous-dossier avec les boutons ci-dessus."));
                return;
            }
            ProjectList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "NOM", "TYPE", "TAILLE", "ACTIONS"));
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    var folder = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                    folder.Click += async (_, _) => await LoadProjectDirectoryAsync(project, entry.FullPath);
                    ProjectList.Children.Add(CreateActionRow(folder, CreateOverflowActions(EntryActions(new ProjectDirectoryContext(project, directory, entry.FullPath), isFile: false)),
                        CreateNameCell("IconProjects", entry.Name, accentIcon: true),
                        CreateCompactTableCell("Dossier"), CreateCompactTableCell("—")));
                }
                else
                {
                    var file = new Button { Tag = entry.FullPath, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                    file.Click += FileEntry_Click;
                    var openWith = new Button { Content = Icons.Label("IconOpen", "Ouvrir avec…"), Tag = entry.FullPath };
                    openWith.Click += OpenWithFile_Click;
                    var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    rename.Click += RenameProjectFile_Click;
                    var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    move.Click += MoveProjectEntry_Click;
                    var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = new ProjectDirectoryContext(project, directory, entry.FullPath) };
                    trash.Click += TrashProjectFile_Click;
                    ProjectList.Children.Add(CreateActionRow(file, CreateOverflowActions(openWith, rename, move, trash),
                        CreateNameCell("IconFile", entry.Name, accentIcon: false, modelPath: entry.FullPath),
                        CreateCompactTableCell(entry.TypeLabel), CreateCompactTableCell(entry.SizeLabel)));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { ProjectList.Children.Add(new TextBlock { Text = "Impossible de lire les fichiers du projet.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void CreateProjectFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<ProjectSummary, string> context }) return;
        var dialog = new TextPromptWindow("Nouveau dossier", "Nom du sous-dossier à créer.", string.Empty);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer un fichier", AllowMultiple = false });
        var source = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(source)) return;
        var originalName = Path.GetFileName(source);
        // From the project root the destination is a choice; inside a folder the file simply goes there.
        var atRoot = string.Equals(Path.GetFullPath(context.Item2).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(context.Item1.ProjectPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<ImportDestination>? destinations = null;
        string? suggestedFolder = null;
        if (atRoot)
        {
            destinations = await Task.Run(() => ImportDestinations(context.Item1.ProjectPath));
            try { suggestedFolder = Path.Combine(context.Item1.ProjectPath, ProjectFileClassifier.Suggest(originalName, context.Item1.FolderName).RelativeDirectory); }
            catch (ArgumentException) { }
        }
        var naming = new ImportNameWindow(originalName, ProjectFileClassifier.SuggestImportName(originalName, context.Item1.Reference), context.Item1.Reference, destinations, suggestedFolder);
        if (!await naming.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var finalName = naming.FileName;
            var targetDirectory = naming.DestinationDirectory ?? context.Item2;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).Import(source, targetDirectory, finalName));
            var where = naming.DestinationDirectory is null ? string.Empty : $" dans {Path.GetRelativePath(context.Item1.ProjectPath, targetDirectory)}";
            Notify((string.Equals(finalName, originalName, StringComparison.Ordinal) ? "Fichier importé" : $"Fichier importé sous le nom {finalName}") + where + ".");
            await LoadProjectDirectoryAsync(context.Item1, context.Item2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void FileEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        await OpenFileAsync(path);
    }

    private void OpenWithFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        try
        {
            if (DesktopPreviewLauncher.OpenWith is { } openWith) openWith(path);
            else _ = OpenFileAsync(path);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { Notify("Impossible d’ouvrir ce fichier avec une autre application : " + exception.Message, error: true); }
    }

    private async void RenameProjectFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectDirectoryContext context }) return;
        var isDirectory = Directory.Exists(context.Path);
        var dialog = new TextPromptWindow(isDirectory ? "Renommer le dossier" : "Renommer le fichier", isDirectory ? "Le dossier conservera son emplacement dans le projet." : "Le fichier conservera son emplacement dans le projet.", Path.GetFileName(context.Path));
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
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
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choisir le dossier de destination", AllowMultiple = false });
        var destination = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(destination)) return;
        try
        {
            var isDirectory = Directory.Exists(context.Path);
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.EntityManagementService(root).MovePath(context.Path, destination));
            Notify(isDirectory ? "Dossier déplacé." : "Fichier déplacé.");
            await ReloadDirectoryAsync(context);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task OpenFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            Notify("Ce fichier n’est plus disponible.", error: true);
            return;
        }
        try
        {
            if (IsImage(path))
            {
                await new ImagePreviewWindow(path).ShowDialog(this);
                return;
            }

            if (ThreeDFileSupport.IsPreviewable(path))
            {
                ShowModelViewer(path);
                return;
            }

            if (DesktopPreviewLauncher.TryOpenDocument(path))
                return;

            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
                await launcher.LaunchUriAsync(new Uri(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException)
        {
            Notify("Impossible d’ouvrir ce fichier.", error: true);
        }
    }

    private static bool IsImage(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
}
