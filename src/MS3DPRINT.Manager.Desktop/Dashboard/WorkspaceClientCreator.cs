using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Creates a professional client through the existing atomic services.</summary>
public sealed class WorkspaceClientCreator
{
    public ClientProfile CreateProfessional(string workspaceRoot, string name, string code)
    {
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, name, code, name, null, null, null, null,
            new PrimaryContact(null, null, null, null, null), now, now, RelativePath: Path.Combine("01_CLIENTS", name));
        return new ClientCreationService(paths.RootPath, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(paths.RootPath, "data"))).Create(profile);
    }
}
