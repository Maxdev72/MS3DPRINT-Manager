using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Clients;

public sealed class ClientCreationService
{
    private readonly string _workspaceRoot;
    private readonly FolderTreeService _folders;
    private readonly ClientProfileStore _profiles;
    private readonly ClientCodeRegistry _codes;

    public ClientCreationService(string workspaceRoot, FolderTreeService folders, ClientProfileStore profiles, ClientCodeRegistry codes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
    }

    public ClientProfile Create(ClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.FolderName) || profile.FolderName is "." or ".." ||
            profile.FolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Le nom du client doit être un nom de dossier simple.", nameof(profile));
        var destination = Path.Combine(_workspaceRoot, "01_CLIENTS", profile.FolderName);
        WorkspacePathSafety.EnsureNoLinks(destination);
        var created = false;
        var profileCreated = false;
        try
        {
            _codes.Add(profile.FolderName, profile.ClientCode, () =>
            {
                _folders.CreateTree(destination, FolderTemplates.Client);
                created = true;
                _profiles.Create(profile);
                profileCreated = true;
            });
        }
        catch (Exception failure)
        {
            if (created)
                CreationRollback.Preserve(_workspaceRoot, destination,
                    profileCreated ? [Path.Combine(_workspaceRoot, ".ms3dprint-manager", "clients", profile.Id + ".json")] : [], failure);
            throw;
        }
        return profile;
    }
}
