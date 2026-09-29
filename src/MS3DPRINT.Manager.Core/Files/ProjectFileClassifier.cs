using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Files;

public static partial class ProjectFileClassifier
{
    public static ProjectFileDestination Suggest(string sourceFileName, string projectFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderName);

        var fileName = Path.GetFileName(sourceFileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length == 0) throw new ArgumentException("Le fichier source doit avoir un nom valide.", nameof(sourceFileName));

        var reference = projectFolderName.Split('_', 2, StringSplitOptions.None)[0];
        if (!ProjectReferencePattern().IsMatch(reference))
        {
            throw new ArgumentException("La référence projet est invalide.", nameof(projectFolderName));
        }

        var extension = Path.GetExtension(fileName);
        var category = CategoryFor(extension);
        return new ProjectFileDestination(category, DirectoryFor(category), stem + "__" + reference + extension);
    }

    private static ProjectFileCategory CategoryFor(string extension) => extension.ToUpperInvariant() switch
    {
        ".PDF" => ProjectFileCategory.Documents,
        ".STEP" or ".STP" => ProjectFileCategory.Step,
        ".STL" => ProjectFileCategory.Stl,
        ".3MF" => ProjectFileCategory.ThreeMf,
        _ => ProjectFileCategory.ClientFiles
    };

    private static string DirectoryFor(ProjectFileCategory category) => category switch
    {
        ProjectFileCategory.Documents => "01_DEVIS_FACTURES",
        ProjectFileCategory.Step => Path.Combine("03_CAO_3D", "02_STEP"),
        ProjectFileCategory.Stl => Path.Combine("03_CAO_3D", "03_STL"),
        ProjectFileCategory.ThreeMf => Path.Combine("04_IMPRESSION_3D", "02_3MF"),
        _ => "02_FICHIERS_CLIENT"
    };

    [GeneratedRegex("^[A-Z0-9]+-[0-9]{4}-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectReferencePattern();
}
