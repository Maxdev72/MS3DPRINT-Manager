using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ClassifyFileWindow : Window
{
    private static readonly string[] AllowedDirectories =
    ["01_DEVIS_FACTURES", "02_FICHIERS_CLIENT", Path.Combine("03_CAO_3D", "01_MASTER"), Path.Combine("03_CAO_3D", "02_STEP"),
     Path.Combine("03_CAO_3D", "03_STL"), Path.Combine("04_IMPRESSION_3D", "01_STL_PRODUCTION"), Path.Combine("04_IMPRESSION_3D", "02_3MF"), Path.Combine("04_IMPRESSION_3D", "03_PARAMETRES")];

    private readonly string _workspaceRoot;
    private readonly TextBlock _source;
    private readonly ComboBox _projects;
    private readonly ComboBox _directories;
    private readonly TextBox _fileName;
    private readonly TextBlock _error;
    private readonly List<ProjectSummary> _projectItems = [];

    public ClassifyFileWindow(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        Title = "Classer un fichier"; Width = 620; Height = 510; MinWidth = 460;
        _source = new TextBlock { Text = "Aucun fichier choisi.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _projects = new ComboBox { PlaceholderText = "Choisir un projet" };
        _directories = new ComboBox { PlaceholderText = "Choisir un dossier" };
        _fileName = new TextBox { PlaceholderText = "Nom de fichier" };
        _error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _projects.SelectionChanged += ProjectChanged;
        var browse = new Button { Content = "Choisir le fichier" }; browse.Click += Browse_Click;
        var move = new Button { Content = "Classer le fichier", HorizontalAlignment = HorizontalAlignment.Right }; move.Click += Move_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = "Classer un fichier", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
            new TextBlock { Text = "Le fichier est déplacé vers un dossier métier du projet sélectionné.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            browse, _source, new TextBlock { Text = "Projet" }, _projects, new TextBlock { Text = "Dossier de destination" }, _directories,
            new TextBlock { Text = "Nom final" }, _fileName, _error, move
        }};
        Opened += async (_, _) => await LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        try
        {
            var projects = await Task.Run(() => new ProjectCatalog(new ProjectProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(_workspaceRoot))).Load(_workspaceRoot)
                .OrderBy(p => p.ClientFolderName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.FolderName, StringComparer.CurrentCultureIgnoreCase).ToArray());
            _projectItems.AddRange(projects);
            _projects.ItemsSource = _projectItems.Select(p => p.ClientFolderName + " — " + p.FolderName).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { _error.Text = exception.Message; }
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choisir le fichier à classer", AllowMultiple = false });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        _source.Tag = path; _source.Text = path; UpdateSuggestion();
    }

    private void ProjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        var project = SelectedProject;
        _directories.ItemsSource = project is null ? [] : AllowedDirectories.Where(relative => Directory.Exists(Path.Combine(project.ProjectPath, relative))).ToArray();
        UpdateSuggestion();
    }

    private ProjectSummary? SelectedProject => _projects.SelectedIndex >= 0 && _projects.SelectedIndex < _projectItems.Count ? _projectItems[_projects.SelectedIndex] : null;

    private void UpdateSuggestion()
    {
        if (_source.Tag is not string source || SelectedProject is not { } project) return;
        try
        {
            var suggestion = ProjectFileClassifier.Suggest(Path.GetFileName(source), project.FolderName);
            _fileName.Text = suggestion.FileName;
            if ((_directories.ItemsSource as IEnumerable<string>)?.Contains(suggestion.RelativeDirectory) == true) _directories.SelectedItem = suggestion.RelativeDirectory;
        }
        catch (ArgumentException exception) { _error.Text = exception.Message; }
    }

    private async void Move_Click(object? sender, RoutedEventArgs e)
    {
        _error.Text = string.Empty;
        if (_source.Tag is not string source || SelectedProject is not { } project || _directories.SelectedItem is not string directory || string.IsNullOrWhiteSpace(_fileName.Text))
        { _error.Text = "Choisissez un fichier, un projet et un dossier de destination."; return; }
        var destination = Path.Combine(project.ProjectPath, directory, _fileName.Text.Trim());
        if (!await new ConfirmMoveWindow(source, destination).ShowDialog<bool>(this)) return;
        try
        {
            var suggestion = ProjectFileClassifier.Suggest(_fileName.Text.Trim(), project.FolderName);
            await Task.Run(() => new ProjectFileTransferService().Move(source, project.ProjectPath, new ProjectFileDestination(suggestion.Category, directory, _fileName.Text.Trim())));
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { _error.Text = exception.Message; }
    }
}
