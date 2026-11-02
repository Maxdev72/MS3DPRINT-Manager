using System.IO.Compression;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Migration;

public sealed class WorkspaceMigrationService
{
    public MigrationReport Export(string sourceRoot, string packageDirectory)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        packageDirectory = Path.GetFullPath(packageDirectory);
        if (WorkspaceEntityPaths.IsInside(sourceRoot, packageDirectory))
            throw new IOException("Le dossier d’export doit être en dehors de l’atelier source.");
        using var mutation = ProfileMutationLock.Acquire(packageDirectory);
        Reserve(packageDirectory);
        var issues = new List<MigrationIssue>();
        MigrationInventory? inventory = null;
        string? hash = null;
        try
        {
            inventory = MigrationInventory.Capture(sourceRoot);
            issues.AddRange(inventory.Issues);
            issues.AddRange(MigrationMetadataAudit.Audit(sourceRoot, inventory));
            if (!issues.Any(i => i.Blocking))
            {
                var archive = Path.Combine(packageDirectory, "workspace.zip");
                var backup = new WorkspaceBackupService();
                hash = backup.Create(sourceRoot, archive, includeDocuments: true).ArchiveSha256;
                backup.Inspect(archive);
                var after = MigrationInventory.Capture(sourceRoot);
                if (!inventory.Files.SequenceEqual(after.Files) || !inventory.Directories.SequenceEqual(after.Directories)
                    || !inventory.Issues.SequenceEqual(after.Issues))
                    issues.Add(new("SourceChanged", "", "La source a changé pendant l’export. Fermez les écritures et recommencez."));
                VerifyManifest(archive, inventory);
            }
        }
        catch (Exception e) when (IsValidationFailure(e)) { issues.Add(Failure(e)); }
        return Save(packageDirectory, "export", hash, inventory, issues);
    }

    public MigrationReport Stage(string archivePath, string preparationDirectory, string expectedArchiveSha256)
    {
        preparationDirectory = Path.GetFullPath(preparationDirectory);
        using var mutation = ProfileMutationLock.Acquire(preparationDirectory);
        Reserve(preparationDirectory);
        var issues = new List<MigrationIssue>();
        MigrationInventory? inventory = null;
        string? hash = null;
        try
        {
            if (expectedArchiveSha256.Length != 64 || !expectedArchiveSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Une empreinte SHA256 est requise.");
            var copy = Path.Combine(preparationDirectory, "workspace.zip");
            WorkspacePathSafety.EnsureNoLinks(archivePath);
            using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                hash = WorkspaceBackupService.CopyAndHash(input, output, input.Length);
                output.Flush(flushToDisk: true);
            }
            if (!string.Equals(hash, expectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L’empreinte de l’archive ne correspond pas à celle attendue.");
            var backup = new WorkspaceBackupService();
            if (!backup.Inspect(copy).IncludesDocuments)
                throw new InvalidDataException("La migration exige une sauvegarde complète avec documents.");
            var workspace = Path.Combine(preparationDirectory, "workspace");
            backup.Restore(copy, workspace, hash);
            inventory = MigrationInventory.Capture(workspace);
            issues.AddRange(inventory.Issues);
            VerifyManifest(copy, inventory);
            issues.AddRange(MigrationMetadataAudit.Audit(workspace, inventory));
        }
        catch (Exception e) when (IsValidationFailure(e)) { issues.Add(Failure(e)); }
        return Save(preparationDirectory, "stage", hash, inventory, issues);
    }

    private static void Reserve(string directory)
    {
        WorkspacePathSafety.EnsureNoLinks(directory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("La préparation exige un nouveau dossier ; aucun dossier existant ne sera remplacé.");
        if (!Directory.Exists(Path.GetDirectoryName(directory)))
            throw new DirectoryNotFoundException("Le dossier parent doit déjà exister.");
        Directory.CreateDirectory(directory);
    }

    private static void VerifyManifest(string archive, MigrationInventory inventory)
    {
        using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        using var manifestStream = zip.GetEntry("ms3dprint-backup.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var expectedFiles = manifest.RootElement.GetProperty("Files").EnumerateArray()
            .Select(f => (Path: f.GetProperty("Path").GetString()!, Bytes: f.GetProperty("Bytes").GetInt64(), Hash: f.GetProperty("Sha256").GetString()!))
            .OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var actualFiles = inventory.Files.Select(f => (f.Path, f.Bytes, Hash: f.Sha256)).ToArray();
        var expectedDirectories = manifest.RootElement.GetProperty("Directories").EnumerateArray()
            .Select(d => d.GetString()!).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles) || !expectedDirectories.SequenceEqual(inventory.Directories))
            throw new InvalidDataException("L’inventaire et les empreintes de la copie ne correspondent pas au manifeste de l’archive.");
    }

    private static MigrationReport Save(string directory, string operation, string? hash, MigrationInventory? inventory, List<MigrationIssue> issues)
    {
        var report = new MigrationReport(1, operation, !issues.Any(i => i.Blocking), DateTimeOffset.UtcNow,
            hash, inventory?.Files ?? [], inventory?.Directories ?? [], issues);
        var temporary = Path.Combine(directory, "report.json.tmp");
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
            output.Flush(flushToDisk: true);
        }
        File.Move(temporary, Path.Combine(directory, "report.json"));
        return report;
    }

    private static bool IsValidationFailure(Exception e) => e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
    private static MigrationIssue Failure(Exception e) => new("ValidationFailed", "",
        "Les contrôles n’ont pas abouti (" + e.GetType().Name + "). Aucune activation n’a été effectuée. Vérifiez la source et recommencez dans un nouveau dossier.");
}
