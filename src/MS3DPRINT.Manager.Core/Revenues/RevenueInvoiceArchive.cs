using System.Security.Cryptography;
using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>Copies the supporting invoice into the workspace. The original is never moved or deleted and nothing is overwritten.</summary>
public static class RevenueInvoiceArchive
{
    /// <summary>Files a purchase receipt in the existing supplier tree, without changing its original.</summary>
    public static string StorePurchase(string workspaceRoot, string source, string supplier, int year)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplier);
        if (!File.Exists(source)) throw new FileNotFoundException("Le justificatif à classer est introuvable.", source);
        if (year is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        var name = NameNormalizer.Normalize(supplier);
        if (name.Length == 0) throw new ArgumentException("Le fournisseur doit avoir un nom utilisable pour son dossier.", nameof(supplier));
        // Windows device names cannot be used as directory names.
        if (name is "CON" or "PRN" or "AUX" or "NUL" || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9'))
            name = "FOURNISSEUR_" + name;
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("L’espace de travail est introuvable.");
        if (IsInside(root, source)) WorkspacePathSafety.EnsureNoLinks(source);
        var category = Path.Combine(root, "06_FOURNISSEURS");
        WorkspacePathSafety.EnsureNoLinks(category);
        using var mutation = ProfileMutationLock.Acquire(category);
        var matches = Directory.Exists(category) ? Directory.EnumerateDirectories(category)
            .Where(path => NameNormalizer.Normalize(Path.GetFileName(path)) == name).ToArray() : [];
        if (matches.Length > 1) throw new InvalidOperationException("Plusieurs dossiers correspondent au fournisseur « " + supplier + " ». Renommez-les pour éviter toute ambiguïté.");
        var folder = matches.SingleOrDefault() ?? Path.Combine(category, name);
        WorkspacePathSafety.EnsureNoLinks(folder);
        if (!Directory.Exists(folder))
            foreach (var part in FolderTemplates.Supplier) Directory.CreateDirectory(Path.Combine(folder, part));
        return StoreInDirectory(root, source, Path.Combine(folder, "04_FACTURES", year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)));
    }

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
        return StoreInDirectory(root, sourcePdf, directory);
    }

    private static string StoreInDirectory(string root, string sourcePdf, string directory)
    {
        WorkspacePathSafety.EnsureNoLinks(directory);
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(sourcePdf);
        var extension = Path.GetExtension(sourcePdf);
        for (var attempt = 1; attempt < 1000; attempt++)
        {
            var name = attempt == 1 ? stem + extension : $"{stem} ({attempt}){extension}";
            var target = Path.Combine(directory, name);
            WorkspacePathSafety.EnsureNoLinks(target);
            if (File.Exists(target))
            {
                if (SameContent(sourcePdf, target)) return Path.GetRelativePath(root, target);
                continue;
            }
            // Copy content, not Cloud Files/reparse metadata or read-only attributes from the source.
            // Publish the completed file atomically so Nextcloud never sees a half-written invoice.
            var temporary = Path.Combine(directory, ".ms3dprint-invoice-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var input = new FileStream(sourcePdf, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                try { File.Move(temporary, target, overwrite: false); }
                catch (IOException) when (File.Exists(target)) { continue; }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
