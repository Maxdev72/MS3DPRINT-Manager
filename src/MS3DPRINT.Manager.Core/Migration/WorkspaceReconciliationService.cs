using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.ObjectModel;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Migration;

public sealed class WorkspaceReconciliationService
{
    public WorkspaceSnapshot Capture(string root)
    {
        var inventory = MigrationInventory.Capture(root);
        var issues = inventory.Issues.Concat(MigrationMetadataAudit.Audit(root, inventory)).Concat(AuditCollections(root))
            .Concat(AuditProfileVersions(root, inventory)).ToList();
        var labels = CaptureLabels(root, inventory, issues);
        return new(Fingerprint(inventory.Files, inventory.Directories), Array.AsReadOnly(inventory.Files),
            Array.AsReadOnly(inventory.Directories), issues.AsReadOnly(), labels);
    }

    public WorkspaceReconciliationPlan Compare(WorkspaceSnapshot pc, WorkspaceSnapshot server)
    {
        Validate(pc); Validate(server);
        var left = pc.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var right = server.Files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var files = left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
        {
            left.TryGetValue(path, out var p); right.TryGetValue(path, out var s);
            var kind = Kind(p, s);
            return new WorkspaceDifference(path, kind, p, s, pc.Labels?.GetValueOrDefault(path) ?? server.Labels?.GetValueOrDefault(path) ?? path);
        }).ToArray();
        var directories = pc.Directories.Union(server.Directories, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        ValidateTree(files.Select(f => f.Path), directories);
        var plan = new WorkspaceReconciliationPlan(pc.Fingerprint, server.Fingerprint, "", Array.AsReadOnly(files),
            Array.AsReadOnly(directories), Array.AsReadOnly(pc.Issues.Concat(server.Issues.Select(i =>
                i.Code == "ProjectProfileMissing" && server.Directories.Contains(i.Path, StringComparer.Ordinal) ? HistoricalFolder(i) : i)).Distinct().ToArray()));
        return plan with { PlanHash = PlanHash(plan) };
    }

    // Only reviewed server folders may remain documents without inventing business metadata.
    public WorkspaceSnapshot CaptureForReconciliation(string root, WorkspaceReconciliationPlan plan)
    {
        ValidatePlan(plan);
        var folders = plan.Issues.Where(i => i.Code == "PreservedProjectFolder" && !i.Blocking)
            .Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
        var snapshot = Capture(root);
        return snapshot with { Issues = snapshot.Issues.Select(i => i.Code == "ProjectProfileMissing" && folders.Contains(i.Path)
            ? HistoricalFolder(i) : i).ToArray() };
    }
    private static MigrationIssue HistoricalFolder(MigrationIssue issue) => new("PreservedProjectFolder", issue.Path,
        "Dossier historique conservé avec tous ses fichiers, sans créer de fiche projet ni de tâches.", false);

    public string ReviewHash(WorkspaceReconciliationPlan plan, IReadOnlyList<WorkspaceReconciliationChoice> choices)
    {
        ValidatePlan(plan);
        var selected = ValidateChoices(plan, choices);
        return Hash(new { plan.PlanHash, Choices = selected.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new { Path = c.Key, Choice = c.Value }).ToArray() });
    }

    public WorkspaceReconciliationCandidate BuildCandidate(string pcRoot, string serverRoot, string newRoot,
        WorkspaceReconciliationPlan plan, IReadOnlyList<WorkspaceReconciliationChoice> choices)
    {
        _ = ReviewHash(plan, choices);
        var selected = ValidateChoices(plan, choices);
        pcRoot = Path.GetFullPath(pcRoot); serverRoot = Path.GetFullPath(serverRoot); newRoot = Path.GetFullPath(newRoot);
        if (Overlaps(pcRoot, newRoot) || Overlaps(serverRoot, newRoot) || Overlaps(pcRoot, serverRoot))
            throw new InvalidDataException("Les sources et la candidate doivent être distinctes sans chevauchement.");
        using var mutation = ProfileMutationLock.Acquire(newRoot);
        WorkspacePathSafety.EnsureNoLinks(newRoot);
        if (Directory.Exists(newRoot) || File.Exists(newRoot)) throw new IOException("La candidate exige un nouveau dossier.");
        if (!Directory.Exists(Path.GetDirectoryName(newRoot))) throw new DirectoryNotFoundException();
        var pc = Capture(pcRoot); var server = Capture(serverRoot);
        if (Compare(pc, server).PlanHash != plan.PlanHash) throw new InvalidDataException("Les sources ou le plan ont changé.");
        if (plan.Issues.Any(i => i.Blocking)) throw new InvalidDataException("Le plan comporte des anomalies bloquantes.");
        Directory.CreateDirectory(newRoot);
        foreach (var directory in plan.Directories) Directory.CreateDirectory(Path.Combine(newRoot, directory));
        var versions = new List<WorkspaceVersionChange>();
        var expected = new Dictionary<string, (long Bytes, string Hash)>(StringComparer.Ordinal);
        foreach (var difference in plan.Files)
        {
            var fromPc = difference.Kind == WorkspaceDifferenceKind.PcOnly || difference.Kind == WorkspaceDifferenceKind.Conflict && selected[difference.Path] == WorkspaceConflictChoice.Pc;
            var file = (fromPc ? difference.Pc : difference.Server)!;
            var source = Path.Combine(fromPc ? pcRoot : serverRoot, file.Path);
            var destination = Path.Combine(newRoot, file.Path);
            WorkspacePathSafety.EnsureNoLinks(source); WorkspacePathSafety.EnsureNoLinks(destination);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var hash = WorkspaceBackupService.CopyAndHash(input, output, file.Bytes);
                output.Flush(true);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Un fichier source a changé pendant la copie.");
            }
            if (fromPc && IsVersionedProfile(file.Path))
            {
                RenewVersion(destination, serverRoot, difference, versions);
                expected.Add(file.Path, (new FileInfo(destination).Length, versions[^1].AfterSha256));
            }
            else expected.Add(file.Path, (file.Bytes, file.Sha256));
        }
        if (Compare(Capture(pcRoot), Capture(serverRoot)).PlanHash != plan.PlanHash)
            throw new InvalidDataException("Une source a changé pendant la préparation.");
        var snapshot = CaptureForReconciliation(newRoot, plan);
        if (snapshot.Files.Count != expected.Count || !snapshot.Directories.SequenceEqual(plan.Directories)
            || snapshot.Files.Any(f => !expected.TryGetValue(f.Path, out var match) || f.Bytes != match.Bytes || !f.Sha256.Equals(match.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("L’empreinte de la candidate diffère du contenu choisi.");
        return new(!snapshot.Issues.Any(i => i.Blocking), snapshot, versions.AsReadOnly(), snapshot.Issues);
    }

    private static void RenewVersion(string path, string serverRoot, WorkspaceDifference difference, List<WorkspaceVersionChange> versions)
    {
        var bytes = File.ReadAllBytes(path);
        var json = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException();
        var previous = json["UpdatedAt"]?.GetValue<DateTimeOffset>() ?? throw new InvalidDataException();
        if (previous == default) throw new InvalidDataException("La version de la fiche est invalide.");
        var baseline = previous;
        if (difference.Server is not null)
        {
            var server = JsonNode.Parse(File.ReadAllBytes(Path.Combine(serverRoot, difference.Path))) as JsonObject ?? throw new InvalidDataException();
            var version = server["UpdatedAt"]?.GetValue<DateTimeOffset>() ?? throw new InvalidDataException();
            if (version > baseline) baseline = version;
        }
        var updated = ProfileMutationLock.NextVersion(baseline, DateTimeOffset.UtcNow);
        json["UpdatedAt"] = updated;
        var after = JsonSerializer.SerializeToUtf8Bytes(json, new JsonSerializerOptions { WriteIndented = true });
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(after); stream.Flush(true); }
        versions.Add(new(difference.Path, previous, updated, HashBytes(bytes), HashBytes(after)));
    }

    private static bool IsVersionedProfile(string path) => path.Split('/') is [".ms3dprint-manager", "clients" or "projects", var name] && name.EndsWith(".json", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> CaptureLabels(string root, MigrationInventory inventory, List<MigrationIssue> issues)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = inventory.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        void Add(string category, Guid id, string name)
        {
            var path = ".ms3dprint-manager/" + category + "/" + id + ".json";
            if (files.Contains(path) && !name.Any(char.IsControl) && !issues.Any(i => i.Blocking && (i.Path == path || i.Path == ".ms3dprint-manager"))) labels[path] = name;
        }
        void Load(string category, Action action)
        {
            try { action(); }
            catch (Exception e) when (e is JsonException or ArgumentException or IOException or InvalidOperationException or FormatException)
            { issues.Add(new("InvalidProfileLabels", ".ms3dprint-manager/" + category, "Les fiches ne peuvent pas être relues pour afficher leur nom.")); }
        }
        var paths = new WorkspaceMetadataPaths(root);
        Load("clients", () =>
        {
            foreach (var profile in new ClientProfileStore(paths).LoadAll())
            {
                var name = profile.Kind == ClientKind.Professional ? profile.CompanyName : string.Join(" ", new[] { profile.FirstName, profile.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
                Add("clients", profile.Id, "Client « " + (string.IsNullOrWhiteSpace(name) ? profile.ClientCode : name) + " »");
            }
        });
        Load("projects", () => { foreach (var profile in new ProjectProfileStore(paths).LoadAll()) Add("projects", profile.Id, "Projet « " + profile.Reference + " – " + profile.ProjectName + " »"); });
        Load("collections", () => { foreach (var profile in new CollectionProfileStore(root).LoadAll()) Add("collections", profile.Id, "Collection « " + profile.Name + " »"); });
        return new ReadOnlyDictionary<string, string>(labels);
    }

    private static IReadOnlyList<MigrationIssue> AuditProfileVersions(string root, MigrationInventory inventory)
    {
        var issues = new List<MigrationIssue>();
        foreach (var file in inventory.Files.Where(f => IsVersionedProfile(f.Path) && f.Bytes <= 16 * 1024 * 1024))
        {
            try
            {
                using var input = File.OpenRead(Path.Combine(root, file.Path));
                using var json = JsonDocument.Parse(input);
                var value = json.RootElement;
                if (!value.TryGetProperty("CreatedAt", out var created) || !created.TryGetDateTimeOffset(out var createdAt) || createdAt == default
                    || !value.TryGetProperty("UpdatedAt", out var updated) || !updated.TryGetDateTimeOffset(out var updatedAt) || updatedAt == default
                    || !value.TryGetProperty("FolderName", out var folder) || folder.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(folder.GetString()))
                    issues.Add(new("InvalidProfileVersion", file.Path, "La fiche possède des dates techniques ou un dossier invalides."));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            { issues.Add(new("InvalidProfileVersion", file.Path, "Les dates techniques de la fiche sont illisibles.")); }
        }
        return issues;
    }

    internal static IReadOnlyList<MigrationIssue> AuditCollections(string root)
    {
        var issues = new List<MigrationIssue>();
        try
        {
            var profiles = new CollectionProfileStore(root).LoadAll();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in profiles)
            {
                var path = ".ms3dprint-manager/collections/" + profile.Id + ".json";
                try { WorkspaceBackupService.ValidateRelative(profile.RelativePath); }
                catch (InvalidDataException)
                {
                    // Raw PC metadata is captured before portable preparation; reject activation, not its backup.
                    issues.Add(new("InvalidCollectionPath", path, "Le chemin de cette fiche doit être vérifié dans la préparation portable avant toute activation."));
                    continue;
                }
                if (profile.CreatedAt == default || profile.UpdatedAt == default || !paths.Add(profile.RelativePath))
                    issues.Add(new("InvalidCollection", path, "La fiche de collection possède une version ou un dossier invalide ou dupliqué."));
                if (!Directory.Exists(Path.Combine(root, profile.RelativePath)))
                    issues.Add(new("MissingCollectionFolder", path, "Le dossier de cette fiche de collection est absent. Une décision de reprise est nécessaire."));
            }
        }
        catch (Exception e) when (e is JsonException or ArgumentException or IOException or InvalidOperationException)
        { issues.Add(new("InvalidCollections", ".ms3dprint-manager/collections", "Les fiches de collections ne peuvent pas être validées.")); }
        return issues;
    }

    private static WorkspaceDifferenceKind Kind(MigrationFile? pc, MigrationFile? server) => pc is null ? WorkspaceDifferenceKind.ServerOnly : server is null ? WorkspaceDifferenceKind.PcOnly
        : pc.Bytes == server.Bytes && pc.Sha256.Equals(server.Sha256, StringComparison.OrdinalIgnoreCase) ? WorkspaceDifferenceKind.Same : WorkspaceDifferenceKind.Conflict;
    private static bool Overlaps(string a, string b) => Inside(a, b) || Inside(b, a);
    private static bool Inside(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void Validate(WorkspaceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Files is null || snapshot.Directories is null || snapshot.Issues is null) throw new InvalidDataException();
        ValidateIssues(snapshot.Issues);
        ValidateTree(snapshot.Files.Select(f => f?.Path ?? throw new InvalidDataException()), snapshot.Directories);
        foreach (var file in snapshot.Files) ValidateFile(file);
        if (snapshot.Fingerprint != Fingerprint(snapshot.Files, snapshot.Directories)) throw new InvalidDataException("L’empreinte de l’instantané est invalide.");
    }
    private static void ValidateFile(MigrationFile file)
    {
        if (file.Bytes < 0 || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException();
    }
    private static void ValidateIssues(IReadOnlyList<MigrationIssue> issues)
    {
        if (issues.Count > 200_000 || issues.Any(i => i is null || string.IsNullOrWhiteSpace(i.Code) || i.Path is null || i.Message is null)) throw new InvalidDataException();
    }
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void ValidateTree(IEnumerable<string> files, IEnumerable<string> directories)
    {
        var dirs = directories.ToArray(); var paths = files.Concat(dirs).ToArray();
        if (paths.Length > 200_000) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactDirs = dirs.ToHashSet(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            WorkspaceBackupService.ValidateRelative(path);
            if (!names.Add(path)) throw new InvalidDataException("Collision de chemins, de casse ou fichier/dossier.");
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
                if (!exactDirs.Contains(path[..slash])) throw new InvalidDataException("Un dossier parent manque ou sa casse diffère.");
        }
    }
    private static void ValidatePlan(WorkspaceReconciliationPlan plan)
    {
        if (plan is null || plan.Files is null || plan.Directories is null || plan.Issues is null) throw new InvalidDataException();
        ValidateIssues(plan.Issues);
        if (!ValidHash(plan.PcFingerprint) || !ValidHash(plan.ServerFingerprint)) throw new InvalidDataException();
        ValidateTree(plan.Files.Select(f => f?.Path ?? throw new InvalidDataException()), plan.Directories);
        foreach (var item in plan.Files)
        {
            if (!Enum.IsDefined(item.Kind) || item.Pc is null && item.Server is null || item.Pc is not null && item.Pc.Path != item.Path || item.Server is not null && item.Server.Path != item.Path) throw new InvalidDataException();
            foreach (var file in new[] { item.Pc, item.Server }.OfType<MigrationFile>()) ValidateFile(file);
            if (item.Kind != Kind(item.Pc, item.Server)) throw new InvalidDataException();
        }
        if (plan.PlanHash != PlanHash(plan)) throw new InvalidDataException("Le plan a été altéré.");
    }
    private static Dictionary<string, WorkspaceConflictChoice> ValidateChoices(WorkspaceReconciliationPlan plan, IReadOnlyList<WorkspaceReconciliationChoice> choices)
    {
        if (choices is null) throw new InvalidDataException();
        var conflicts = plan.Files.Where(f => f.Kind == WorkspaceDifferenceKind.Conflict).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, WorkspaceConflictChoice>(StringComparer.Ordinal);
        foreach (var choice in choices)
            if (choice is null || choice.Path is null || !Enum.IsDefined(choice.Choice) || !conflicts.Contains(choice.Path) || !result.TryAdd(choice.Path, choice.Choice)) throw new InvalidDataException("Choix absent, inconnu ou dupliqué.");
        if (result.Count != conflicts.Count) throw new InvalidDataException("Chaque conflit exige un choix explicite.");
        return result;
    }
    private static string Fingerprint(IEnumerable<MigrationFile> files, IEnumerable<string> directories) => Hash(new
    {
        Files = files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => new { f.Path, f.Bytes, Sha256 = f.Sha256.ToUpperInvariant() }).ToArray(),
        Directories = directories.Order(StringComparer.Ordinal).ToArray()
    });
    private static string PlanHash(WorkspaceReconciliationPlan plan) => Hash(new
    {
        plan.PcFingerprint, plan.ServerFingerprint,
        Files = plan.Files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => new { f.Path, f.Kind,
            Pc = f.Pc is null ? null : new { f.Pc.Bytes, Sha256 = f.Pc.Sha256.ToUpperInvariant() },
            Server = f.Server is null ? null : new { f.Server.Bytes, Sha256 = f.Server.Sha256.ToUpperInvariant() } }).ToArray(),
        Directories = plan.Directories.Order(StringComparer.Ordinal).ToArray(), Issues = plan.Issues.OrderBy(i => i.Path, StringComparer.Ordinal).ThenBy(i => i.Code, StringComparer.Ordinal).ToArray()
    });
    private static string Hash<T>(T value) => HashBytes(JsonSerializer.SerializeToUtf8Bytes(value));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
