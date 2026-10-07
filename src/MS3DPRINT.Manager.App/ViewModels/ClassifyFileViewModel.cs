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
    private readonly string _clientsRoot;
    private readonly string? _initialProjectPath;
    private string _sourcePath = string.Empty;
    private string _sourceIssue = string.Empty;
    private string? _selectedProject;
    private string? _selectedRelativeDirectory;
    private string _finalFileName = string.Empty;
    private ProjectFileCategory _category = ProjectFileCategory.ClientFiles;

    public ClassifyFileViewModel(string storageRoot, string? initialProjectPath = null, bool loadProjects = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        _clientsRoot = Path.Combine(Path.GetFullPath(storageRoot), "01_CLIENTS");
        _initialProjectPath = initialProjectPath;
        if (loadProjects) ApplyProjectChoices(ReadProjectChoices());
    }

    public ObservableCollection<string> Projects { get; } = new();
    public ObservableCollection<string> DestinationDirectories { get; } = new();

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (!SetProperty(ref _sourcePath, value ?? string.Empty)) return;
            UpdateSourceIssue();
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

    public string SourceIssue
    {
        get => _sourceIssue;
        private set => SetProperty(ref _sourceIssue, value);
    }

    public string ProjectPath => SelectedProject is not null && _projectPaths.TryGetValue(SelectedProject, out var path)
        ? path
        : throw new InvalidOperationException("Sélectionnez un projet.");

    public string DestinationPath => string.IsNullOrWhiteSpace(SelectedRelativeDirectory) || string.IsNullOrWhiteSpace(FinalFileName) || SelectedProject is null
        ? string.Empty
        : Path.Combine(ProjectPath, SelectedRelativeDirectory, FinalFileName);

    public bool IsReady => SourceIssue.Length == 0 && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(SelectedProject) &&
                           !string.IsNullOrWhiteSpace(SelectedRelativeDirectory) && FinalFileName.Length > 0;

    public ProjectFileDestination CreateDestination()
    {
        if (string.IsNullOrWhiteSpace(SelectedRelativeDirectory) || FinalFileName.Length == 0)
            throw new InvalidOperationException("Sélectionnez un fichier et un projet.");
        return new ProjectFileDestination(_category, SelectedRelativeDirectory, FinalFileName);
    }

    public IReadOnlyList<ClassifyProjectChoice> ReadProjectChoices()
    {
        if (!Directory.Exists(_clientsRoot)) return [];
        return Directory.EnumerateDirectories(_clientsRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(clientPath =>
            {
                var clientName = Path.GetFileName(clientPath);
                return Directory.EnumerateDirectories(clientPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
                    .Where(path => ProjectFolderPattern.IsMatch(Path.GetFileName(path)))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                    .Select(projectPath => new ClassifyProjectChoice(clientName + " — " + Path.GetFileName(projectPath), projectPath));
            })
            .ToArray();
    }

    public void ApplyProjectChoices(IReadOnlyList<ClassifyProjectChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _projectPaths.Clear();
        Projects.Clear();
        DestinationDirectories.Clear();
        SelectedProject = null;
        foreach (var choice in choices)
        {
            _projectPaths.Add(choice.Label, choice.Path);
            Projects.Add(choice.Label);
        }
        if (!string.IsNullOrWhiteSpace(_initialProjectPath)) SelectProject(_initialProjectPath);
    }

    private void SelectProject(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var match = _projectPaths.FirstOrDefault(pair => string.Equals(Path.GetFullPath(pair.Value), fullPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(match.Key)) SelectedProject = match.Key;
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

    private void UpdateSourceIssue()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            SourceIssue = string.Empty;
            return;
        }

        try
        {
            var attributes = File.GetAttributes(SourcePath);
            SourceIssue = (attributes & FileAttributes.Directory) != 0
                ? "Le chemin sélectionné est un dossier. Sélectionnez un fichier."
                : string.Empty;
        }
        catch (FileNotFoundException)
        {
            SourceIssue = "Le fichier source est introuvable. Sélectionnez-le à nouveau.";
        }
        catch (DirectoryNotFoundException)
        {
            SourceIssue = "Le fichier source est introuvable. Sélectionnez-le à nouveau.";
        }
        catch (UnauthorizedAccessException)
        {
            SourceIssue = "Accès refusé au fichier source. Vérifiez vos droits ou fermez l’application qui l’utilise.";
        }
        catch (IOException)
        {
            SourceIssue = "Le fichier source ne peut pas être utilisé. Sélectionnez-le à nouveau.";
        }
        catch (ArgumentException)
        {
            SourceIssue = "Le chemin du fichier source est invalide. Sélectionnez-le à nouveau.";
        }
    }
}

public sealed record ClassifyProjectChoice(string Label, string Path);
