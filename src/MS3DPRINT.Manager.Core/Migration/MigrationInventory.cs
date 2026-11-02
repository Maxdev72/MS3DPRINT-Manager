using System.Security.Cryptography;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Templates;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Migration;

internal sealed record MigrationInventory(MigrationFile[] Files, string[] Directories, MigrationIssue[] Issues)
{
    internal static MigrationInventory Capture(string root)
    {
        WorkspacePathSafety.EnsureNoLinks(root);
        var files = new List<MigrationFile>();
        var directories = new List<string>();
        var issues = new List<MigrationIssue>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(FolderTemplates.Main.Append(".ms3dprint-manager"), StringComparer.Ordinal);
        void Register(string relative)
        {
            WorkspaceBackupService.ValidateRelative(relative);
            if (!names.Add(relative)) throw new InvalidDataException("Des chemins entrent en conflit sans distinction de casse.");
            if (names.Count > 200_000) throw new InvalidDataException("L’inventaire contient trop d’entrées.");
        }
        void Visit(string path)
        {
            WorkspacePathSafety.EnsureNoLinks(path);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative == ".ms3dprint-manager/last-backup.json")
            {
                issues.Add(new("BackupMarkerExcluded", relative, "L’indicateur de dernière sauvegarde n’est pas transféré.", false));
                return;
            }
            Register(relative);
            if (Directory.Exists(path))
            {
                directories.Add(relative);
                foreach (var child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal)) Visit(child);
            }
            else
            {
                var info = new FileInfo(path);
                var length = info.Length;
                var modified = info.LastWriteTimeUtc;
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length != length) throw new IOException("Un fichier a changé pendant l’inventaire.");
                var hash = Convert.ToHexString(SHA256.HashData(input));
                info.Refresh();
                if (info.Length != length || info.LastWriteTimeUtc != modified) throw new IOException("Un fichier a changé pendant l’inventaire.");
                files.Add(new(relative, length, hash, new DateTimeOffset(modified, TimeSpan.Zero)));
            }
        }
        foreach (var path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (roots.Contains(name) && Directory.Exists(path)) Visit(path);
            else if (name == "data" && Directory.Exists(path))
            {
                WorkspacePathSafety.EnsureNoLinks(path);
                var registry = Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal).ToArray();
                if (registry.Any(p => File.Exists(p) && WorkspaceBackupService.IsRegistry("data/" + Path.GetFileName(p))))
                {
                    Register("data");
                    directories.Add("data");
                }
                foreach (var entry in registry)
                {
                    var relative = "data/" + Path.GetFileName(entry);
                    if (File.Exists(entry) && WorkspaceBackupService.IsRegistry(relative)) Visit(entry);
                    else issues.Add(new("ExcludedEntry", relative, "Cette entrée n’est pas incluse dans la sauvegarde ; son traitement doit être décidé avant une migration réelle."));
                }
            }
            else issues.Add(new("ExcludedEntry", name, "Cette entrée est hors du périmètre de sauvegarde ; elle reste sur la source et exige une décision avant migration."));
        }
        return new(files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(), directories.Order(StringComparer.Ordinal).ToArray(), issues.ToArray());
    }
}
