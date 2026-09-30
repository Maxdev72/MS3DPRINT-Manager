using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Clients;

public sealed class ClientCatalog
{
    private static readonly Regex ProjectFolderPattern = new("^[A-Z0-9]+-[0-9]{4}-[0-9]{3}(?:_|$)", RegexOptions.CultureInvariant);
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
        if (!Directory.Exists(clientsRoot)) return [];

        var profiles = _profiles.LoadAll().ToDictionary(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(clientsRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
            .Select(path => CreateSummary(path, profiles))
            .Where(summary => summary.FolderName is not "00_CLIENT" and not "99_ARCHIVES")
            .OrderBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ClientSummary CreateSummary(string clientPath, IReadOnlyDictionary<string, ClientProfile> profiles)
    {
        var folderName = Path.GetFileName(clientPath);
        profiles.TryGetValue(folderName, out var profile);
        var clientCode = profile?.ClientCode ?? ReadLegacyCode(folderName);
        var displayName = profile is null ? folderName : GetDisplayName(profile);
        var projectCount = Directory.EnumerateDirectories(clientPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Count(name => ProjectFolderPattern.IsMatch(name));
        return new ClientSummary(clientPath, folderName, displayName, clientCode, profile?.Kind, profile, projectCount);
    }

    private string ReadLegacyCode(string folderName)
    {
        try { return _legacyCodes?.GetCode(folderName) ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    private static string GetDisplayName(ClientProfile profile) => profile.Kind == ClientKind.Professional
        ? profile.CompanyName!
        : string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
