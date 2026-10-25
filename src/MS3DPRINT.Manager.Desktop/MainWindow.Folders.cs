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

/// <summary>Generic workspace folders opened from collections and other pages.</summary>
public partial class MainWindow
{
    /// <summary>Reloads the directory an action was made in: a project folder, or a generic workspace folder.</summary>
    private async Task ReloadDirectoryAsync(ProjectDirectoryContext context)
    {
        if (context.Project is { } project) { await LoadProjectDirectoryAsync(project, context.Directory); return; }
        _folderWorkspaceDirectory = context.Directory;
        await LoadFolderWorkspaceAsync();
    }

    private ProjectDirectoryContext FolderContext(string path) => new(null, _folderWorkspaceDirectory ?? _folderWorkspaceRoot ?? string.Empty, path);

    private async void CreateWorkspaceFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_folderWorkspaceDirectory)) return;
        var directory = _folderWorkspaceDirectory;
        var dialog = new TextPromptWindow("Nouveau dossier", "Nom du sous-dossier à créer.", string.Empty);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var folderName = dialog.Value;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Workspace.ManagedFileService(root).CreateFolder(directory, folderName));
            Notify("Dossier créé.");
            await LoadFolderWorkspaceAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void ImportWorkspaceFile_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_folderWorkspaceDirectory)) return;
        if (_importBatchActive) return;
        var directory = _folderWorkspaceDirectory;
        var root = WorkspaceRoot;
        var sources = await PickImportSourcesAsync();
        await ImportWorkspaceFilesAsync(root, directory, sources);
    }

    private Button[] EntryActions(ProjectDirectoryContext context, bool isFile)
    {
        var rename = new Button { Content = Icons.Label("IconRename", "Renommer"), Tag = context };
        rename.Click += RenameProjectFile_Click;
        var move = new Button { Content = Icons.Label("IconMove", "Déplacer"), Tag = context };
        move.Click += MoveProjectEntry_Click;
        var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = context, Classes = { "menu-destructive" } };
        trash.Click += TrashProjectFile_Click;
        if (!isFile) return [rename, move, trash];
        var openWith = new Button { Content = Icons.Label("IconOpen", "Ouvrir avec…"), Tag = context.Path };
        openWith.Click += OpenWithFile_Click;
        return [openWith, rename, move, trash];
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !Directory.Exists(path)) return;
        DismissSearchResults();
        try
        {
            var root = WorkspaceRoot;
            var project = (await Task.Run(() => new WorkspaceProjectReader().Load(root))).FirstOrDefault(item => path.StartsWith(item.ProjectPath, StringComparison.OrdinalIgnoreCase));
            if (project is not null)
            {
                PageTitle.Text = project.Title();
                await LoadProjectDirectoryAsync(project, path);
                return;
            }

            if (!FolderWorkspacePanel.IsVisible) _folderHistoryParent = CurrentNavigationStop;
            _folderWorkspaceRoot = Path.GetFullPath(path);
            _folderWorkspaceDirectory = _folderWorkspaceRoot;
            _folderWorkspaceReturnTitle = PageTitle.Text;
            _folderWorkspaceReturnSubtitle = PageSubtitle.Text;
            // The folder view replaces the page underneath instead of being drawn over it.
            if (!FolderWorkspacePanel.IsVisible)
            {
                _hiddenByFolderWorkspace = WorkspacePanels().Where(panel => panel.IsVisible).ToList();
                foreach (var panel in _hiddenByFolderWorkspace) panel.IsVisible = false;
            }
            FolderWorkspacePanel.IsVisible = true;
            await LoadFolderWorkspaceAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Impossible d’ouvrir ce dossier : " + exception.Message, error: true); }
    }

    private List<Control> _hiddenByFolderWorkspace = [];

    /// <summary>Asks, in the application, for a folder of the workspace (the system folder window opens Explorer and lets the user leave it).</summary>
    private async Task<string?> PickWorkspaceFolderAsync(string start, string heading, string? excluded = null)
    {
        var dialog = new FolderPickerWindow(WorkspaceRoot, start, heading, "Choisissez le dossier de destination dans l’espace de travail.", excluded);
        return await dialog.ShowDialog<bool>(this) ? dialog.SelectedFolder : null;
    }

    private IEnumerable<Control> WorkspacePanels() =>
    [
        DashboardStats, DashboardContent, ProjectsWorkspacePanel, ClientsWorkspacePanel, CollectionsWorkspacePanel,
        FilamentsWorkspacePanel, EstimatesWorkspacePanel, PrintersWorkspacePanel, RevenuesWorkspacePanel, PurchasesWorkspacePanel, ModelSearchWorkspacePanel, TrashWorkspacePanel, SettingsWorkspacePanel
    ];

    private async Task LoadFolderWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(_folderWorkspaceRoot) || string.IsNullOrWhiteSpace(_folderWorkspaceDirectory)) return;
        CancelFileRead();
        RememberFolder();
        var root = _folderWorkspaceRoot;
        var directory = _folderWorkspaceDirectory;
        FolderWorkspaceTitle.Text = string.Equals(root, directory, StringComparison.OrdinalIgnoreCase) ? "Contenu du dossier" : Path.GetFileName(directory);
        FolderWorkspaceLocation.Text = directory;
        FolderWorkspaceList.Children.Clear();

        var back = new Button
        {
            Content = Icons.Label("IconArrowLeft", string.Equals(root, directory, StringComparison.OrdinalIgnoreCase) ? "Retour" : "Dossier parent"),
            Classes = { "ghost" },
            HorizontalAlignment = HorizontalAlignment.Left
        };
        back.Click += async (_, _) =>
        {
            if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
            {
                CancelFileRead();
                FolderWorkspacePanel.IsVisible = false;
                foreach (var panel in _hiddenByFolderWorkspace) panel.IsVisible = true;
                _hiddenByFolderWorkspace = [];
                PageTitle.Text = _folderWorkspaceReturnTitle ?? "Vue d’ensemble";
                PageSubtitle.Text = _folderWorkspaceReturnSubtitle ?? string.Empty;
                if (_folderHistoryParent is { } parent) RememberNavigation(parent);
                return;
            }

            _folderWorkspaceDirectory = new ProjectWorkspaceBrowser().Parent(root, directory);
            await LoadFolderWorkspaceAsync();
        };
        var newFolder = new Button { Content = Icons.Label("IconProjects", "Nouveau dossier") };
        newFolder.Click += CreateWorkspaceFolder_Click;
        var importFile = new Button { Content = Icons.Label("IconUpload", "Importer des fichiers") };
        importFile.Click += ImportWorkspaceFile_Click;
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        Grid.SetColumn(newFolder, 1); Grid.SetColumn(importFile, 2);
        toolbar.Children.Add(back); toolbar.Children.Add(newFolder); toolbar.Children.Add(importFile);
        FolderWorkspaceList.Children.Add(toolbar);
        FolderWorkspaceList.Spacing = 12;
        var reading = LoadFileBrowserAsync(FolderWorkspaceList, root, directory, null, LoadFolderWorkspaceAsync);
        if (!_replayingNavigation) await reading;
    }
}
