using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Templates;

/// <summary>A folder of the project template; <paramref name="Category"/> says which imported files land in it.</summary>
public sealed record ProjectTemplateFolder(string Path, ProjectFileCategory? Category = null);

/// <summary>Folders created in every new project, and where the imported files are sorted.</summary>
public sealed record ProjectTemplate(IReadOnlyList<ProjectTemplateFolder> Folders)
{
    public const int MaxFolders = 60;
    public const int MaxPathLength = 120;

    /// <summary>The standard template: the one built into the software.</summary>
    public static ProjectTemplate Default { get; } = new(FolderTemplates.Project
        .Select(path => new ProjectTemplateFolder(path, DefaultCategory(path)))
        .ToArray());

    public IReadOnlyList<string> Paths => Folders.Select(folder => folder.Path).ToArray();

    /// <summary>Folder of a category, relative to the project; the "client files" folder when the category has none.</summary>
    public string DirectoryFor(ProjectFileCategory category)
    {
        var folder = Folders.FirstOrDefault(candidate => candidate.Category == category)
                     ?? Folders.FirstOrDefault(candidate => candidate.Category == ProjectFileCategory.ClientFiles)
                     ?? new ProjectTemplateFolder(FolderTemplates.Project[2]);
        return folder.Path.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    /// <summary>The reason the template cannot be used, or null when it is valid.</summary>
    public string? Validate()
    {
        if (Folders is null || Folders.Count == 0) return "Le modèle doit contenir au moins un dossier.";
        if (Folders.Count > MaxFolders) return $"Le modèle ne peut pas dépasser {MaxFolders} dossiers.";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Path)) return "Un dossier du modèle n’a pas de nom.";
            if (folder.Path.Length > MaxPathLength) return $"« {folder.Path} » est trop long.";
            var parts = folder.Path.Split('/');
            if (System.IO.Path.IsPathRooted(folder.Path) || folder.Path.Contains('\\') ||
                parts.Any(part => part.Length == 0 || part is "." or ".." || part != part.Trim() || part.EndsWith('.') ||
                                  part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || IsReservedName(part)))
                return $"« {folder.Path} » n’est pas un nom de dossier valide.";
            if (!seen.Add(folder.Path)) return $"« {folder.Path} » apparaît deux fois.";
        }
        var duplicate = Folders.Where(folder => folder.Category is not null).GroupBy(folder => folder.Category).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) return $"Un seul dossier peut recevoir la catégorie « {Label(duplicate.Key!.Value)} ».";
        if (Folders.All(folder => folder.Category != ProjectFileCategory.ClientFiles))
            return "Un dossier doit recevoir les fichiers client : les fichiers qui ne rentrent dans aucune autre catégorie y sont classés.";
        return null;
    }

    public static string Label(ProjectFileCategory category) => category switch
    {
        ProjectFileCategory.Documents => "Documents (PDF)",
        ProjectFileCategory.Step => "Fichiers STEP",
        ProjectFileCategory.Stl => "Fichiers STL",
        ProjectFileCategory.ThreeMf => "Fichiers 3MF",
        _ => "Fichiers client"
    };

    private static ProjectFileCategory? DefaultCategory(string path) => path switch
    {
        "01_DEVIS_FACTURES" => ProjectFileCategory.Documents,
        "02_FICHIERS_CLIENT" => ProjectFileCategory.ClientFiles,
        "03_CAO_3D/02_STEP" => ProjectFileCategory.Step,
        "03_CAO_3D/03_STL" => ProjectFileCategory.Stl,
        "04_IMPRESSION_3D/02_3MF" => ProjectFileCategory.ThreeMf,
        _ => null
    };

    private static bool IsReservedName(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]));
    }
}
