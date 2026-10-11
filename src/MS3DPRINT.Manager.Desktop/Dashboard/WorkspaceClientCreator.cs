using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Creates a professional client through the existing atomic services.</summary>
public sealed class WorkspaceClientCreator
{
    public ClientProfile CompleteExisting(string workspaceRoot, string clientPath, ClientKind kind, string name, string code, string? firstName, string? lastName)
    {
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot);
        var path = Path.GetFullPath(clientPath);
        if (!WorkspaceEntityPaths.IsInside(Path.Combine(root, "01_CLIENTS"), path) || !Directory.Exists(path))
            throw new ArgumentException("Le dossier client historique est introuvable.", nameof(clientPath));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Le code client est obligatoire.", nameof(code));
        var now = DateTimeOffset.UtcNow;
        var profile = kind == ClientKind.Professional
            ? new ClientProfile(Guid.NewGuid(), kind, Path.GetFileName(path), code.Trim().ToUpperInvariant(), name.Trim(), null, null, null, null,
                new PrimaryContact(null, null, null, null, null), now, now, RelativePath: Path.GetRelativePath(root, path))
            : new ClientProfile(Guid.NewGuid(), kind, Path.GetFileName(path), code.Trim().ToUpperInvariant(), null, firstName?.Trim(), lastName?.Trim(), null, null,
                new PrimaryContact(firstName?.Trim(), lastName?.Trim(), null, null, null), now, now, RelativePath: Path.GetRelativePath(root, path));
        new ClientProfileStore(paths).Create(profile);
        return profile;
    }

    public ClientProfile CreateProfessional(string workspaceRoot, string name, string code)
    {
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var now = DateTimeOffset.UtcNow;
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, name, code, name, null, null, null, null,
            new PrimaryContact(null, null, null, null, null), now, now, RelativePath: Path.Combine("01_CLIENTS", name));
        return new ClientCreationService(paths.RootPath, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(paths.RootPath, "data"))).Create(profile);
    }

    public ClientProfile CreateIndividual(string workspaceRoot, string firstName, string lastName, string code)
    {
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var now = DateTimeOffset.UtcNow;
        var folderName = (lastName.Trim() + " " + firstName.Trim()).Trim();
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Individual, folderName, code, null, firstName.Trim(), lastName.Trim(), null, null,
            new PrimaryContact(firstName.Trim(), lastName.Trim(), null, null, null), now, now, RelativePath: Path.Combine("01_CLIENTS", folderName));
        return new ClientCreationService(paths.RootPath, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(paths.RootPath, "data"))).Create(profile);
    }
}
