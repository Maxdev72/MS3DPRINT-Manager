namespace MS3DPRINT.Manager.Core.Workspace;

public static class WorkspaceEntityPaths
{
    public static string Resolve(string root, string relative, string category)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ArgumentException("Le chemin de la fiche doit être relatif à l’espace géré.");
        var categoryRoot = Path.GetFullPath(Path.Combine(root, category));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsInside(categoryRoot, path) || string.Equals(categoryRoot, path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le chemin de la fiche doit rester dans sa catégorie.");
        return path;
    }

    public static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
