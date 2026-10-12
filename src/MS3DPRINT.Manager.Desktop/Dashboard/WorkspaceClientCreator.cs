using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Creates a professional client through the existing atomic services.</summary>
public sealed class WorkspaceClientCreator
{
    public ClientProfile CompleteExisting(string workspaceRoot, string clientPath, ClientKind kind, string name, string code, string? firstName, string? lastName, string? siret = null, string? address = null)
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
                new PrimaryContact(null, null, null, null, null), now, now, NullIfBlank(siret), Path.GetRelativePath(root, path))
            : new ClientProfile(Guid.NewGuid(), kind, Path.GetFileName(path), code.Trim().ToUpperInvariant(), null, firstName?.Trim(), lastName?.Trim(), null, null,
                new PrimaryContact(firstName?.Trim(), lastName?.Trim(), null, null, null), now, now, RelativePath: Path.GetRelativePath(root, path));
        profile = profile with { Address = NullIfBlank(address) };
        new ClientProfileStore(paths).Create(profile);
        return profile;
    }

    /// <summary>First free code for a client: the initials of its name ("Alexis Leroy" gives AL), then AL2, AL3...</summary>
    public string GenerateCode(string workspaceRoot, string identity)
    {
        var baseCode = ClientCodeSuggester.Suggest(identity);
        if (baseCode.Length == 0) throw new ArgumentException("Le nom du client ne permet pas de générer un code.", nameof(identity));
        var used = new WorkspaceClientReader().Load(workspaceRoot)
            .Select(client => client.ClientCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseCode)) return baseCode;
        for (var number = 2; ; number++)
            if (!used.Contains(baseCode + number)) return baseCode + number;
    }

    public ClientProfile CreateProfessional(string workspaceRoot, string name, string? code = null, string? siret = null, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(code)) code = GenerateCode(workspaceRoot, name);
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var now = DateTimeOffset.UtcNow;
        var folderName = NameNormalizer.Normalize(name);
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, folderName, code.Trim().ToUpperInvariant(), name.Trim(), null, null, NullIfBlank(address), null,
            new PrimaryContact(null, null, null, null, null), now, now, NullIfBlank(siret), Path.Combine("01_CLIENTS", folderName));
        return new ClientCreationService(paths.RootPath, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(paths.RootPath, "data"))).Create(profile);
    }

    public ClientProfile CreateIndividual(string workspaceRoot, string firstName, string lastName, string? code = null, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(code)) code = GenerateCode(workspaceRoot, $"{firstName} {lastName}");
        var paths = new WorkspaceMetadataPaths(workspaceRoot);
        var now = DateTimeOffset.UtcNow;
        var folderName = NameNormalizer.Normalize($"{firstName} {lastName}");
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Individual, folderName, code.Trim().ToUpperInvariant(), null, firstName.Trim(), lastName.Trim(), NullIfBlank(address), null,
            new PrimaryContact(firstName.Trim(), lastName.Trim(), null, null, null), now, now, RelativePath: Path.Combine("01_CLIENTS", folderName));
        return new ClientCreationService(paths.RootPath, new FolderTreeService(), new ClientProfileStore(paths), new ClientCodeRegistry(Path.Combine(paths.RootPath, "data"))).Create(profile);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
