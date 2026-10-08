using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Clients;

public sealed class ClientCatalog
{
    private readonly ClientProfileStore _profiles;
    private readonly ClientCodeRegistry? _legacyCodes;

    public ClientCatalog(ClientProfileStore profiles, ClientCodeRegistry? legacyCodes = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _legacyCodes = legacyCodes;
    }

    public IReadOnlyList<ClientSummary> Load(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var clientsRoot = Path.Combine(Path.GetFullPath(workspaceRoot), "01_CLIENTS");
        WorkspacePathSafety.EnsureNoLinks(clientsRoot);
        if (!Directory.Exists(clientsRoot)) return [];

        var profiles = new Dictionary<string, ClientProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _profiles.LoadReadable())
        {
            try { profiles[WorkspaceEntityPaths.Resolve(workspaceRoot, profile.RelativePath ?? Path.Combine("01_CLIENTS", profile.FolderName), "01_CLIENTS")] = profile; }
            catch (ArgumentException) { }
        }
        var direct = Directory.EnumerateDirectories(clientsRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !profiles.Keys.Any(indexed => !string.Equals(indexed, path, StringComparison.OrdinalIgnoreCase) && WorkspaceEntityPaths.IsInside(path, indexed)));
        return direct.Concat(profiles.Keys.Where(Directory.Exists)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsSafe)
            .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
            .Where(path => !Path.GetFileName(path).StartsWith(".MS3DPRINT-STAGING-", StringComparison.OrdinalIgnoreCase))
            .Select(path => CreateSummary(path, profiles))
            .Where(summary => summary.FolderName is not "00_CLIENT" and not "99_ARCHIVES")
            .OrderBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ClientSummary CreateSummary(string clientPath, IReadOnlyDictionary<string, ClientProfile> profiles)
    {
        var folderName = Path.GetFileName(clientPath);
        profiles.TryGetValue(clientPath, out var profile);
        var clientCode = profile?.ClientCode ?? ReadLegacyCode(folderName);
        var displayName = profile is null ? folderName : GetDisplayName(profile);
        var projectCount = Directory.EnumerateDirectories(clientPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Count(name => ProjectReferenceFormat.TryParseFolderName(name, out _));
        return new ClientSummary(clientPath, folderName, displayName, clientCode, profile?.Kind, profile, projectCount);
    }

    private string ReadLegacyCode(string folderName)
    {
        try { return _legacyCodes?.GetCode(folderName) ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }
    private static bool IsSafe(string path)
    { try { WorkspacePathSafety.EnsureNoLinks(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }

    private static string GetDisplayName(ClientProfile profile) => profile.Kind == ClientKind.Professional
        ? profile.CompanyName!
        : string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
