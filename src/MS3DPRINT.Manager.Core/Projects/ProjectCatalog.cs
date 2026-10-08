using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Projects;

public sealed class ProjectCatalog
{
    private readonly ProjectProfileStore _profiles;
    public ProjectCatalog(ProjectProfileStore profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public IReadOnlyList<ProjectSummary> Load(string workspaceRoot)
    {
        var clientsRoot = Path.Combine(Path.GetFullPath(workspaceRoot), "01_CLIENTS");
        WorkspacePathSafety.EnsureNoLinks(clientsRoot);
        if (!Directory.Exists(clientsRoot)) return [];
        var clients = new ClientCatalog(new ClientProfileStore(new WorkspaceMetadataPaths(workspaceRoot))).Load(workspaceRoot);
        var clientsById = clients.Where(client => client.Profile is not null).ToDictionary(client => client.Profile!.Id);
        var profilesByPath = new Dictionary<string, ProjectProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _profiles.LoadReadable())
        {
            if (!clientsById.TryGetValue(profile.ClientId, out var client)) continue;
            try
            {
                var path = profile.RelativePath is null
                    ? WorkspaceEntityPaths.Resolve(workspaceRoot, Path.GetRelativePath(workspaceRoot, Path.Combine(client.ClientPath, profile.FolderName)), "01_CLIENTS")
                    : WorkspaceEntityPaths.Resolve(workspaceRoot, profile.RelativePath, "01_CLIENTS");
                if (string.Equals(Path.GetDirectoryName(path), client.ClientPath, StringComparison.OrdinalIgnoreCase) && IsSafe(path))
                    profilesByPath[path] = profile;
            }
            catch (ArgumentException) { }
        }
        return clients.SelectMany(client => Directory.EnumerateDirectories(client.ClientPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSafe).Select(path => CreateSummary(client, path, profilesByPath)))
            .Where(project => project is not null).Cast<ProjectSummary>()
            .OrderByDescending(project => project.Reference, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ProjectSummary? CreateSummary(ClientSummary client, string path, IReadOnlyDictionary<string, ProjectProfile> profiles)
    {
        var folderName = Path.GetFileName(path);
        profiles.TryGetValue(path, out var profile);
        if (!ProjectReferenceFormat.TryParseFolderName(folderName, out var reference) && profile is null) return null;
        return new(client.FolderName, client.ClientPath, path, profile?.Reference ?? reference, folderName, profile);
    }
    private static bool IsSafe(string path)
    { try { WorkspacePathSafety.EnsureNoLinks(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
}
