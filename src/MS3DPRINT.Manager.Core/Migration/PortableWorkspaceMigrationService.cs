using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Migration;

public sealed record MigrationTransformation(string Path, IReadOnlyList<string> Fields, string BeforeSha256, string AfterSha256);
public sealed record PortableMigrationReport(int Version, bool Succeeded, string? SourceArchiveSha256,
    string? PortableArchiveSha256, IReadOnlyList<MigrationTransformation> Transformations, IReadOnlyList<MigrationIssue> Issues);

public sealed class PortableWorkspaceMigrationService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public PortableMigrationReport Prepare(string archivePath, string preparationDirectory, string expectedArchiveSha256)
        => Prepare(archivePath, preparationDirectory, expectedArchiveSha256, new HashSet<Guid>());

    public PortableMigrationReport Prepare(string archivePath, string preparationDirectory, string expectedArchiveSha256,
        IReadOnlySet<Guid> inactiveMissingCollections)
    {
        ArgumentNullException.ThrowIfNull(inactiveMissingCollections);
        preparationDirectory = Path.GetFullPath(preparationDirectory);
        using var mutation = ProfileMutationLock.Acquire(preparationDirectory);
        WorkspacePathSafety.EnsureNoLinks(preparationDirectory);
        if (Directory.Exists(preparationDirectory) || File.Exists(preparationDirectory))
            throw new IOException("La préparation exige un nouveau dossier.");
        if (!Directory.Exists(Path.GetDirectoryName(preparationDirectory))) throw new DirectoryNotFoundException();
        Directory.CreateDirectory(preparationDirectory);
        var changes = new List<MigrationTransformation>();
        var issues = new List<MigrationIssue>();
        string? sourceHash = null, portableHash = null;
        try
        {
            if (expectedArchiveSha256.Length != 64 || !expectedArchiveSha256.All(Uri.IsHexDigit)) throw new InvalidDataException();
            WorkspacePathSafety.EnsureNoLinks(archivePath);
            var raw = Path.Combine(preparationDirectory, "source.zip");
            using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(raw, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                sourceHash = WorkspaceBackupService.CopyAndHash(input, output, input.Length);
                output.Flush(true);
            }
            if (!sourceHash.Equals(expectedArchiveSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            var backup = new WorkspaceBackupService();
            if (!backup.Inspect(raw).IncludesDocuments) throw new InvalidDataException();
            var workspace = Path.Combine(preparationDirectory, "workspace");
            backup.Restore(raw, workspace, sourceHash);
            AnnexMissingCollections(workspace, preparationDirectory, inactiveMissingCollections);
            ConvertMetadata(workspace, changes, preparationDirectory);
            var inventory = MigrationInventory.Capture(workspace);
            issues.AddRange(inventory.Issues);
            issues.AddRange(MigrationMetadataAudit.Audit(workspace, inventory));
            issues.AddRange(WorkspaceReconciliationService.AuditCollections(workspace));
            if (!issues.Any(i => i.Blocking))
            {
                var portable = Path.Combine(preparationDirectory, "portable.zip");
                portableHash = backup.Create(workspace, portable, true).ArchiveSha256;
                var verification = new WorkspaceMigrationService().Stage(portable, Path.Combine(preparationDirectory, "verification"), portableHash);
                issues.AddRange(verification.Issues);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException)
        {
            issues.Add(new("PortablePreparationFailed", e is MetadataFailure metadata ? metadata.Path : "",
                "La préparation portable a échoué (" + (e.InnerException ?? e).GetType().Name + "). Vérifiez le schéma et les chemins du fichier indiqué. Aucune activation effectuée."));
        }
        Save(Path.Combine(preparationDirectory, "transformations.json"), changes);
        var report = new PortableMigrationReport(1, !issues.Any(i => i.Blocking), sourceHash, portableHash, changes, issues);
        Save(Path.Combine(preparationDirectory, "portable-report.json"), report);
        return report;
    }

    private static void AnnexMissingCollections(string workspace, string preparation, IReadOnlySet<Guid> selected)
    {
        var entries = new List<(Guid Id, string Path, byte[] Bytes, string Sha256)>();
        foreach (var id in selected.Order())
        {
            if (id == Guid.Empty) throw new InvalidDataException();
            var relative = ".ms3dprint-manager/collections/" + id + ".json";
            var file = Path.Combine(workspace, relative);
            WorkspacePathSafety.EnsureNoLinks(file);
            if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException();
            var bytes = File.ReadAllBytes(file);
            var json = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException();
            var category = ProfileCategory(json, "collections", id.ToString());
            var path = Normalize(json["RelativePath"]?.GetValue<string>() ?? throw new InvalidDataException(), category);
            var target = Path.Combine(workspace, path);
            WorkspacePathSafety.EnsureNoLinks(target);
            if (Directory.Exists(target) || File.Exists(target)) throw new InvalidDataException("Seules les collections réellement sans dossier peuvent être mises en annexe.");
            entries.Add((id, relative, bytes, Hash(bytes)));
        }
        if (entries.Count == 0) return;
        var annex = Path.Combine(preparation, "inactive-collections");
        Directory.CreateDirectory(annex);
        foreach (var entry in entries)
        {
            var output = Path.Combine(annex, entry.Id + ".json");
            Write(output, entry.Bytes);
            if (Hash(File.ReadAllBytes(output)) != entry.Sha256) throw new InvalidDataException();
        }
        Save(Path.Combine(annex, "manifest.json"), entries.Select(e => new { e.Id, e.Path, e.Sha256, Bytes = e.Bytes.Length }).ToArray());
        foreach (var entry in entries) File.Delete(Path.Combine(workspace, entry.Path));
    }

    private static void ConvertMetadata(string workspace, List<MigrationTransformation> changes, string preparation)
    {
        var metadata = Path.Combine(workspace, ".ms3dprint-manager");
        if (!Directory.Exists(metadata)) return;
        var trashRoot = Path.Combine(metadata, "trash");
        if (Directory.Exists(trashRoot))
            foreach (var directory in Directory.EnumerateDirectories(trashRoot))
                if (!File.Exists(Path.Combine(directory, "manifest.json")))
                    throw new MetadataFailure(Path.GetRelativePath(workspace, directory).Replace('\\', '/'), new InvalidDataException());
        ConvertJournals(workspace, changes, preparation);
        foreach (var file in Directory.EnumerateFiles(metadata, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(workspace, file).Replace('\\', '/');
            try
            {
                var parts = relative.Split('/');
                var profile = parts.Length == 3 && parts[1] is "clients" or "projects" or "collections" or "history";
                var trash = parts.Length == 4 && parts[1] == "trash" && parts[3] == "manifest.json";
                if (!profile && !trash) continue;
                if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException();
                var bytes = File.ReadAllBytes(file);
                var root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException();
                var fields = new List<string>();
                if (profile) ConvertProfile(root, relative, fields);
                else ConvertTrash(root, parts[2], Path.GetDirectoryName(file)!, workspace, changes, preparation, fields);
                PersistConversion(root, bytes, file, relative, fields, changes, preparation);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or FormatException)
            {
                throw new MetadataFailure(relative, e);
            }
        }
    }

    private static void ConvertJournals(string workspace, List<MigrationTransformation> changes, string preparation)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        foreach (var kind in new[] { "purchases", "revenues" })
        {
            var directory = Path.Combine(workspace, ".ms3dprint-manager", kind);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
            {
                var relative = Path.GetRelativePath(workspace, file).Replace('\\', '/');
                try
                {
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var year) || year is < 1 or > 9999
                        || new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException();
                    var before = File.ReadAllBytes(file);
                    var lines = Encoding.UTF8.GetString(before).Split('\n');
                    var fields = new List<string>();
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i])) continue;
                        var root = JsonNode.Parse(lines[i]) as JsonObject ?? throw new InvalidDataException();
                        var type = root["Type"]?.GetValue<string>();
                        var entry = (type == "entry" ? root["Entry"] : type == "correction" ? root["Correction"]?["Entry"] : null) as JsonObject;
                        var field = kind == "purchases" ? "DocumentPath" : "InvoicePdfPath";
                        if (entry?[field] is not { } path || string.IsNullOrEmpty(path.GetValue<string>())) continue;
                        var id = entry["Id"]?.GetValue<Guid>() ?? Guid.Empty;
                        if (id == Guid.Empty || entry["RecordedAt"]?.GetValue<DateTimeOffset>() is not { } recorded || recorded == default)
                            throw new InvalidDataException();
                        if (type == "correction" && (root["Correction"]?["EntryId"]?.GetValue<Guid>() != id
                            || root["Correction"]?["Id"]?.GetValue<Guid>() is not { } correctionId || correctionId == Guid.Empty)) throw new InvalidDataException();
                        if (kind == "purchases") _ = entry.Deserialize<PurchaseEntry>(options) ?? throw new InvalidDataException();
                        else _ = entry.Deserialize<RevenueEntry>(options) ?? throw new InvalidDataException();
                        var original = path.GetValue<string>();
                        var normalized = Normalize(original, null);
                        if (normalized.StartsWith(".ms3dprint-manager/", StringComparison.Ordinal)
                            && !(kind == "purchases" && normalized.StartsWith(".ms3dprint-manager/purchases/pieces/", StringComparison.Ordinal)))
                            throw new InvalidDataException();
                        if (original == normalized) continue;
                        entry[field] = normalized;
                        fields.Add("/Lines/" + (i + 1) + (type == "entry" ? "/Entry/" : "/Correction/Entry/") + field);
                        lines[i] = root.ToJsonString();
                    }
                    if (fields.Count > 0)
                    {
                        var after = Encoding.UTF8.GetBytes(string.Join('\n', lines));
                        changes.Add(new(relative, fields, Hash(before), Hash(after)));
                        Save(Path.Combine(preparation, "transformations.json"), changes);
                        Write(file, after);
                    }
                    var errors = kind == "purchases" ? new PurchaseJournal(workspace).Load(year).Errors : new RevenueJournal(workspace).Load(year).Errors;
                    if (errors.Count != 0) throw new InvalidDataException();
                }
                catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or FormatException)
                { throw new MetadataFailure(relative, e); }
            }
        }
    }

    private static void ConvertProfile(JsonObject root, string relative, List<string> fields)
    {
        var parts = relative.Split('/');
        if (parts.Length != 3 || parts[0] != ".ms3dprint-manager" || parts[1] is not ("clients" or "projects" or "collections" or "history")) return;
        if (parts[1] == "history" && !root.ContainsKey("RelativePath")) return;
        var category = ProfileCategory(root, parts[1], Path.GetFileNameWithoutExtension(relative));
        if (root["RelativePath"] is { } path)
        {
            var original = path.GetValue<string>();
            var normalized = Normalize(original, category);
            if (original != normalized) { root["RelativePath"] = normalized; fields.Add("/RelativePath"); }
        }
        else if (parts[1] == "collections") throw new InvalidDataException();
    }

    private static void PersistConversion(JsonObject root, byte[] bytes, string file, string relative, List<string> fields,
        List<MigrationTransformation> changes, string preparation)
    {
        if (fields.Count == 0) return;
        var converted = JsonSerializer.SerializeToUtf8Bytes(root, Json);
        var change = new MigrationTransformation(relative, fields, Hash(bytes), Hash(converted));
        // Persist intent before replacing a metadata file; failed runs remain private and cannot be activated.
        changes.Add(change);
        Save(Path.Combine(preparation, "transformations.json"), changes);
        Write(file, converted);
    }

    private static string ProfileCategory(JsonObject root, string kind, string filename)
    {
        if (!Guid.TryParse(root["Id"]?.GetValue<string>(), out var id) || id == Guid.Empty
            || root["CreatedAt"] is null || root["UpdatedAt"] is null
            || root["CreatedAt"]!.GetValue<DateTimeOffset>() == default || root["UpdatedAt"]!.GetValue<DateTimeOffset>() == default)
            throw new InvalidDataException();
        if (kind != "history" && !filename.Equals(id.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        if (kind == "history")
        {
            if (!filename.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(filename[(id.ToString().Length + 1)..], out var ticks) || ticks <= 0) throw new InvalidDataException();
            kind = root.ContainsKey("ClientId") ? "projects" : root.ContainsKey("Kind") ? "clients" : "unknown";
        }
        if (kind == "clients")
        {
            var p = root.Deserialize<ClientProfile>() ?? throw new InvalidDataException();
            if (!Enum.IsDefined(p.Kind) || string.IsNullOrWhiteSpace(p.ClientCode) || string.IsNullOrWhiteSpace(p.FolderName) || p.PrimaryContact is null)
                throw new InvalidDataException();
            return "01_CLIENTS";
        }
        if (kind == "projects")
        {
            var p = root.Deserialize<ProjectProfile>() ?? throw new InvalidDataException();
            if (p.ClientId == Guid.Empty || !Enum.IsDefined(p.Status) || !Enum.IsDefined(p.Priority)
                || string.IsNullOrWhiteSpace(p.Reference) || string.IsNullOrWhiteSpace(p.ProjectName) || string.IsNullOrWhiteSpace(p.FolderName)) throw new InvalidDataException();
            return "01_CLIENTS";
        }
        if (kind == "collections")
        {
            var p = root.Deserialize<CollectionProfile>() ?? throw new InvalidDataException();
            CollectionProfileStore.ValidateCategory(p.Category);
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Any(char.IsControl)) throw new InvalidDataException();
            return p.Category;
        }
        throw new InvalidDataException();
    }

    private static void ConvertTrash(JsonObject root, string directory, string fullDirectory, string workspace,
        List<MigrationTransformation> changes, string preparation, List<string> fields)
    {
        var entry = root["Entry"]?.Deserialize<TrashEntry>();
        if (entry is null || entry.Id == Guid.Empty || entry.Id.ToString("N") != directory || entry.Label is null || entry.DeletedAt == default
            || root["State"]?.GetValue<string>() != "Trashed" || root["Items"] is not JsonArray { Count: > 0 } items) throw new InvalidDataException();
        var originals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloads = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not JsonObject item || item["IsDirectory"] is null) throw new InvalidDataException();
            var isDirectory = item["IsDirectory"]!.GetValue<bool>();
            var payload = item["Payload"]?.GetValue<string>();
            if (!int.TryParse(payload, out var index) || index < 0 || payload != index.ToString() || !payloads.Add(payload)) throw new InvalidDataException();
            var payloadPath = Path.Combine(fullDirectory, payload);
            if (isDirectory ? !Directory.Exists(payloadPath) : !File.Exists(payloadPath)) throw new InvalidDataException();
            var original = item["Original"]?.GetValue<string>() ?? throw new InvalidDataException();
            var normalized = Normalize(original, null);
            if (!originals.Add(normalized) || normalized.StartsWith(".ms3dprint-manager/trash", StringComparison.OrdinalIgnoreCase)
                || !normalized.Contains('/')) throw new InvalidDataException();
            if (original != normalized) { item["Original"] = normalized; fields.Add("/Items/" + i + "/Original"); }
            if (normalized.StartsWith(".ms3dprint-manager/", StringComparison.OrdinalIgnoreCase))
            {
                if (isDirectory || !normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || new FileInfo(payloadPath).Length > 16 * 1024 * 1024)
                    throw new InvalidDataException();
                var bytes = File.ReadAllBytes(payloadPath);
                var payloadRoot = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException();
                var payloadFields = new List<string>();
                ConvertProfile(payloadRoot, normalized, payloadFields);
                // Numeric trash filenames are not scanned as JSON by the general inventory audit.
                var referenceIssues = new List<MigrationIssue>();
                using var document = JsonDocument.Parse(payloadRoot.ToJsonString());
                MigrationMetadataAudit.Walk(document.RootElement, normalized, referenceIssues);
                if (referenceIssues.Any(i => i.Blocking)) throw new InvalidDataException();
                PersistConversion(payloadRoot, bytes, payloadPath, Path.GetRelativePath(workspace, payloadPath).Replace('\\', '/'), payloadFields, changes, preparation);
            }
        }
        if (Directory.EnumerateFileSystemEntries(fullDirectory).Any(path =>
            Path.GetFileName(path) != "manifest.json" && !payloads.Contains(Path.GetFileName(path)))) throw new InvalidDataException();
    }

    private static string Normalize(string path, string? category)
    {
        var normalized = path.Replace('\\', '/');
        WorkspaceBackupService.ValidateRelative(normalized);
        if (category is not null && (!normalized.StartsWith(category + "/", StringComparison.Ordinal) || normalized.Split('/').Length < 2)) throw new InvalidDataException();
        return normalized;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class MetadataFailure(string path, Exception inner) : IOException("Métadonnées non portables.", inner)
    {
        internal string Path { get; } = path;
    }
    private static void Save<T>(string path, T value) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    private static void Write(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}
