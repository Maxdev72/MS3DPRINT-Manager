using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Files;

public static partial class ProjectFileClassifier
{
    public static ProjectFileDestination Suggest(string sourceFileName, string projectFolderName, ProjectTemplate? template = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolderName);

        var fileName = Path.GetFileName(sourceFileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length == 0) throw new ArgumentException("Le fichier source doit avoir un nom valide.", nameof(sourceFileName));

        if (!ProjectReferenceFormat.TryParseFolderName(projectFolderName, out var reference))
        {
            throw new ArgumentException("La référence projet est invalide.", nameof(projectFolderName));
        }

        var extension = Path.GetExtension(fileName);
        var category = CategoryFor(extension);
        return new ProjectFileDestination(category, (template ?? ProjectTemplate.Default).DirectoryFor(category), stem + "__" + reference + extension);
    }

    /// <summary>
    /// Name proposed when importing a file into a project: "&lt;name&gt;__&lt;project reference&gt;.&lt;ext&gt;".
    /// A name that already carries the reference is returned unchanged.
    /// </summary>
    public static string SuggestImportName(string sourceFileName, string projectReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectReference);
        var fileName = Path.GetFileName(sourceFileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length == 0) throw new ArgumentException("Le fichier source doit avoir un nom valide.", nameof(sourceFileName));
        var suffix = "__" + projectReference.Trim();
        return stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fileName : stem + suffix + Path.GetExtension(fileName);
    }

    private static ProjectFileCategory CategoryFor(string extension) => extension.ToUpperInvariant() switch
    {
        ".PDF" => ProjectFileCategory.Documents,
        ".STEP" or ".STP" => ProjectFileCategory.Step,
        ".STL" => ProjectFileCategory.Stl,
        ".3MF" => ProjectFileCategory.ThreeMf,
        _ => ProjectFileCategory.ClientFiles
    };
}
