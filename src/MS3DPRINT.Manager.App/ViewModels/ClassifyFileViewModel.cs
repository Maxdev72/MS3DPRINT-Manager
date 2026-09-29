using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class ClassifyFileViewModel : ObservableObject
{
    private static readonly Regex ProjectFolderPattern = new("^[A-Z0-9]+-[0-9]{4}-[0-9]{3}(?:_|$)", RegexOptions.CultureInvariant);
    private static readonly string[] AllowedRelativeDirectories =
    [
        "01_DEVIS_FACTURES",
        "02_FICHIERS_CLIENT",
        Path.Combine("03_CAO_3D", "01_MASTER"),
        Path.Combine("03_CAO_3D", "02_STEP"),
        Path.Combine("03_CAO_3D", "03_STL"),
        Path.Combine("04_IMPRESSION_3D", "01_STL_PRODUCTION"),
        Path.Combine("04_IMPRESSION_3D", "02_3MF"),
        Path.Combine("04_IMPRESSION_3D", "03_PARAMETRES")
    ];

    private readonly Dictionary<string, string> _projectPaths = new(StringComparer.Ordinal);
    private string _sourcePath = string.Empty;
    private string? _selectedProject;
    private string? _selectedRelativeDirectory;
    private string _finalFileName = string.Empty;
    private ProjectFileCategory _category = ProjectFileCategory.ClientFiles;

    public ClassifyFileViewModel(string storageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        LoadProjects(Path.Combine(Path.GetFullPath(storageRoot), "01_CLIENTS"));
    }

    public ObservableCollection<string> Projects { get; } = new();
    public ObservableCollection<string> DestinationDirectories { get; } = new();

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (!SetProperty(ref _sourcePath, value ?? string.Empty)) return;
            UpdateSuggestion();
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public string? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value)) return;
            RefreshDestinationDirectories();
            UpdateSuggestion();
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public string? SelectedRelativeDirectory
    {
        get => _selectedRelativeDirectory;
        set
        {
            if (!SetProperty(ref _selectedRelativeDirectory, value)) return;
            OnPropertyChanged(nameof(DestinationPath));
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public string FinalFileName
    {
        get => _finalFileName;
        private set => SetProperty(ref _finalFileName, value);
    }

    public string ProjectPath => SelectedProject is not null && _projectPaths.TryGetValue(SelectedProject, out var path)
        ? path
        : throw new InvalidOperationException("Sélectionnez un projet.");

    public string DestinationPath => string.IsNullOrWhiteSpace(SelectedRelativeDirectory) || string.IsNullOrWhiteSpace(FinalFileName) || SelectedProject is null
        ? string.Empty
        : Path.Combine(ProjectPath, SelectedRelativeDirectory, FinalFileName);

    public bool IsReady => File.Exists(SourcePath) && !string.IsNullOrWhiteSpace(SelectedProject) &&
                           !string.IsNullOrWhiteSpace(SelectedRelativeDirectory) && FinalFileName.Length > 0;

    public ProjectFileDestination CreateDestination()
    {
        if (string.IsNullOrWhiteSpace(SelectedRelativeDirectory) || FinalFileName.Length == 0)
            throw new InvalidOperationException("Sélectionnez un fichier et un projet.");
        return new ProjectFileDestination(_category, SelectedRelativeDirectory, FinalFileName);
    }

    private void LoadProjects(string clientsRoot)
    {
        if (!Directory.Exists(clientsRoot)) return;
        foreach (var clientPath in Directory.EnumerateDirectories(clientsRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            var clientName = Path.GetFileName(clientPath);
            foreach (var projectPath in Directory.EnumerateDirectories(clientPath, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                         .Where(path => ProjectFolderPattern.IsMatch(Path.GetFileName(path)))
                         .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                var label = clientName + " — " + Path.GetFileName(projectPath);
                _projectPaths.Add(label, projectPath);
                Projects.Add(label);
            }
        }
    }

    private void RefreshDestinationDirectories()
    {
        DestinationDirectories.Clear();
        if (SelectedProject is null) return;
        foreach (var relativeDirectory in AllowedRelativeDirectories.Where(relative => Directory.Exists(Path.Combine(ProjectPath, relative))))
        {
            DestinationDirectories.Add(relativeDirectory);
        }
    }

    private void UpdateSuggestion()
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(SourcePath))
        {
            FinalFileName = string.Empty;
            return;
        }

        var projectFolderName = Path.GetFileName(ProjectPath);
        var suggestion = ProjectFileClassifier.Suggest(Path.GetFileName(SourcePath), projectFolderName);
        _category = suggestion.Category;
        FinalFileName = suggestion.FileName;
        SelectedRelativeDirectory = DestinationDirectories.Contains(suggestion.RelativeDirectory)
            ? suggestion.RelativeDirectory
            : DestinationDirectories.FirstOrDefault();
    }
}
