using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectCatalog
{
    private static readonly Regex Pattern = new("^(?<reference>[A-Z0-9]+-[0-9]{4}-[0-9]{3})(?:_|$)", RegexOptions.CultureInvariant);
    private readonly ProjectProfileStore _profiles;

    public ProjectCatalog(ProjectProfileStore profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public IReadOnlyList<ProjectSummary> Load(string workspaceRoot)
    {
        var clientsRoot = Path.Combine(Path.GetFullPath(workspaceRoot), "01_CLIENTS");
        if (!Directory.Exists(clientsRoot)) return [];
        var profiles = _profiles.LoadReadable().ToDictionary(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(clientsRoot, "*", SearchOption.TopDirectoryOnly)
            .SelectMany(clientPath => Directory.EnumerateDirectories(clientPath, "*", SearchOption.TopDirectoryOnly)
                .Select(projectPath => CreateSummary(clientPath, projectPath, profiles)))
            .Where(project => project is not null)
            .Cast<ProjectSummary>()
            .OrderByDescending(project => project.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProjectSummary? CreateSummary(string clientPath, string projectPath, IReadOnlyDictionary<string, ProjectProfile> profiles)
    {
        var folderName = Path.GetFileName(projectPath);
        var match = Pattern.Match(folderName);
        if (!match.Success) return null;
        profiles.TryGetValue(folderName, out var profile);
        return new ProjectSummary(Path.GetFileName(clientPath), clientPath, projectPath, match.Groups["reference"].Value, folderName, profile);
    }
}
