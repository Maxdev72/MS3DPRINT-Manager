using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Projects;

public static class ProjectReferenceFormat
{
    private static readonly Regex FolderPattern = new("^(?<reference>[A-Z0-9_]+-[0-9]{4}-[0-9]{3})(?:_|$)", RegexOptions.CultureInvariant);

    public static bool TryParseFolderName(string folderName, out string reference)
    {
        var match = FolderPattern.Match(folderName);
        reference = match.Success ? match.Groups["reference"].Value : string.Empty;
        return match.Success;
    }
}
