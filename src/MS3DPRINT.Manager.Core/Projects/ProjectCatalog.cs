using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectCatalog
{
    private static readonly Regex Pattern = new("^(?<reference>[A-Z0-9]+-[0-9]{4}-[0-9]{3})(?:_|$)", RegexOptions.CultureInvariant);
    private readonly ProjectProfileStore _profiles;

    public ProjectCatalog(ProjectProfileStore profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public IReadOnlyList<ProjectSummary> Load(string workspaceRoot)
    {
        var clientsRoot = Path.Combine(Path.GetFullPath(workspaceRoot), "01_CLIENTS");
        WorkspacePathSafety.EnsureNoLinks(clientsRoot);
        if (!Directory.Exists(clientsRoot)) return [];
        var profiles = _profiles.LoadReadable().ToDictionary(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase);
        var clients = new ClientCatalog(new ClientProfileStore(new WorkspaceMetadataPaths(workspaceRoot))).Load(workspaceRoot);
        var discovered = clients
            .SelectMany(client => Directory.EnumerateDirectories(client.ClientPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSafe)
                .Select(projectPath => CreateSummary(client.ClientPath, projectPath, profiles)))
            .Where(project => project is not null).Cast<ProjectSummary>();
        var indexed = new List<ProjectSummary>();
        foreach (var profile in profiles.Values.Where(profile => profile.RelativePath is not null))
        {
            try
            {
                var path = WorkspaceEntityPaths.Resolve(workspaceRoot, profile.RelativePath!, "01_CLIENTS");
                if (IsSafe(path) && Directory.Exists(path)) indexed.Add(new(Path.GetFileName(Path.GetDirectoryName(path)!), Path.GetDirectoryName(path)!, path, profile.Reference, profile.FolderName, profile));
            }
            catch (ArgumentException) { }
        }
        return discovered.Concat(indexed)
            .GroupBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())
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
    private static bool IsSafe(string path)
    { try { WorkspacePathSafety.EnsureNoLinks(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
}
