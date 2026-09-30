using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;

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
        var destination = Path.Combine(_workspaceRoot, "01_CLIENTS", profile.FolderName);
        _folders.CreateTree(destination, FolderTemplates.Client);
        _profiles.Create(profile);
        _codes.Add(profile.FolderName, profile.ClientCode);
        return profile;
    }
}
