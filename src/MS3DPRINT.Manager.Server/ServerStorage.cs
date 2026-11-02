using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Server;

public sealed class ServerStorage
{
    public string StateRoot { get; }
    public string WorkspaceRoot { get; }

    public ServerStorage(IConfiguration configuration)
    {
        StateRoot = ReadRoot(configuration, "Server:StateRoot");
        WorkspaceRoot = ReadRoot(configuration, "Server:WorkspaceRoot");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (IsWithin(StateRoot, WorkspaceRoot, comparison) || IsWithin(WorkspaceRoot, StateRoot, comparison))
            throw new InvalidOperationException("Les dossiers d’état du serveur et d’atelier doivent être distincts et non imbriqués.");
        WorkspacePathSafety.EnsureNoLinks(StateRoot);
        WorkspacePathSafety.EnsureNoLinks(WorkspaceRoot);
        if (!Directory.Exists(WorkspaceRoot)) throw new InvalidOperationException("Le dossier d’atelier configuré doit déjà exister.");
        Directory.CreateDirectory(StateRoot);
    }

    private static string ReadRoot(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new InvalidOperationException($"La configuration {key} doit contenir un chemin absolu explicite.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static bool IsWithin(string path, string root, StringComparison comparison)
        => path.Equals(root, comparison) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison);
}
