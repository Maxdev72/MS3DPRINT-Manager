using System.Security.Cryptography;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>Copies the supporting invoice into the workspace. The original is never moved or deleted and nothing is overwritten.</summary>
public static class RevenueInvoiceArchive
{
    /// <summary>
    /// Stores a copy in the client's Documents folder when the client folder is known, otherwise in the book's own pieces folder.
    /// Returns the workspace-relative path. An identical file already present is reused instead of duplicated.
    /// </summary>
    public static string Store(string workspaceRoot, string sourcePdf, string? clientFolder, int year, string area = "revenues")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdf);
        var root = Path.GetFullPath(workspaceRoot);
        if (!File.Exists(sourcePdf)) throw new FileNotFoundException("La facture à joindre est introuvable.", sourcePdf);
        var directory = clientFolder is not null && IsInside(root, clientFolder) && Directory.Exists(clientFolder)
            ? Path.Combine(Path.GetFullPath(clientFolder), "Documents")
            : Path.Combine(root, ".ms3dprint-manager", area, "pieces", year.ToString("0000"));
        WorkspacePathSafety.EnsureNoLinks(directory);
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(sourcePdf);
        var extension = Path.GetExtension(sourcePdf);
        for (var attempt = 1; attempt < 1000; attempt++)
        {
            var name = attempt == 1 ? stem + extension : $"{stem} ({attempt}){extension}";
            var target = Path.Combine(directory, name);
            if (File.Exists(target))
            {
                if (SameContent(sourcePdf, target)) return Path.GetRelativePath(root, target);
                continue;
            }
            try { File.Copy(sourcePdf, target, overwrite: false); }
            catch (IOException) when (File.Exists(target)) { continue; }
            return Path.GetRelativePath(root, target);
        }
        throw new IOException("Impossible de trouver un nom libre pour la facture.");
    }

    /// <summary>Resolves a stored relative path and refuses anything that would leave the workspace.</summary>
    public static string? Resolve(string workspaceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(workspaceRoot);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return IsInside(root, full) && File.Exists(full) ? full : null;
    }

    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool SameContent(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var a = File.OpenRead(first); using var b = File.OpenRead(second);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
