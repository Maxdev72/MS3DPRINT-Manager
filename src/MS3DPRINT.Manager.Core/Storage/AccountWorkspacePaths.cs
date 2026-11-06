using System.Security.Cryptography;
using System.Text;
using MS3DPRINT.Manager.Core.Remote;

namespace MS3DPRINT.Manager.Core.Storage;

/// <summary>
/// Private per-device folder of one account's workspace (copy, binding, recovery). The key is a SHA256 of the canonical
/// origin, the account and the workspace GUID, never the display name, so two accounts never share a copy.
/// </summary>
public sealed class AccountWorkspacePaths
{
    public AccountWorkspacePaths(string privateRoot, WorkspaceIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateRoot);
        ArgumentNullException.ThrowIfNull(identity);
        var server = identity.Server;
        if (server is null || !server.IsAbsoluteUri || server.Scheme != Uri.UriSchemeHttps || server.AbsolutePath != "/"
            || server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(identity.AccountId) || identity.WorkspaceId == Guid.Empty)
            throw new ArgumentException("Identité d’atelier invalide.", nameof(identity));
        var value = server.AbsoluteUri + "\0" + identity.AccountId + "\0" + identity.WorkspaceId.ToString("N");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        DirectoryPath = Path.Combine(Path.GetFullPath(privateRoot), "workspaces", key);
    }

    public string DirectoryPath { get; }
    public string CachePath => Path.Combine(DirectoryPath, "copy.json");
    public string BindingPath => Path.Combine(DirectoryPath, "binding.json");
    public string RecoveryDirectory => Path.Combine(DirectoryPath, "recovery");
}
