using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Collections;
using MS3DPRINT.Manager.Core.Naming;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Workspace;

public sealed class EntityManagementService
{
    private readonly string _root;
    private readonly ManagedFileService _files;
    private readonly ClientProfileStore _clients;
    private readonly ProjectProfileStore _projects;
    private readonly CollectionProfileStore _collections;
    public EntityManagementService(string root)
    {
        _root = Path.GetFullPath(root); _files = new(_root);
        var paths = new WorkspaceMetadataPaths(_root);
        _clients = new(paths); _projects = new(paths); _collections = new(_root);
    }
    public string RenameClient(ClientSummary client, string folderName)
    {
        if (client.Profile is null) throw new ArgumentException("Complétez la fiche client avant de renommer son dossier.");
        ValidateName(folderName);
        return Relocate(client.ClientPath, Path.Combine(Path.GetDirectoryName(client.ClientPath)!, folderName), () => _files.Rename(client.ClientPath, folderName));
    }

    public string MoveClient(ClientSummary client, string destinationDirectory)
    {
        if (client.Profile is null) throw new ArgumentException("Complétez la fiche client avant de déplacer son dossier.");
        ValidateClientDestination(destinationDirectory);
        return Relocate(client.ClientPath, Path.Combine(destinationDirectory, client.FolderName), () => _files.Move(client.ClientPath, destinationDirectory));
    }
    public string RenameProject(ProjectSummary project, string newName)
    {
        if (project.Profile is null) throw new ArgumentException("Complétez la fiche projet avant de renommer son dossier.");
        var normalized = NameNormalizer.Normalize(newName);
        if (normalized.Length == 0) throw new ArgumentException("Le nom du projet doit contenir une lettre ou un chiffre.");
        var folder = project.Reference + "_" + normalized;
        return Relocate(project.ProjectPath, Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, folder), () => _files.Rename(project.ProjectPath, folder), projectName: newName.Trim());
    }
    public string MoveProject(ProjectSummary project, ClientSummary destinationClient)
    {
        if (project.Profile is null) throw new ArgumentException("Complétez la fiche projet avant de déplacer son dossier.");
        if (destinationClient.Profile is null) throw new ArgumentException("Complétez la fiche du client destinataire.");
        RequireCategory(destinationClient.ClientPath, "01_CLIENTS");
        return Relocate(project.ProjectPath, Path.Combine(destinationClient.ClientPath, project.FolderName), () => _files.Move(project.ProjectPath, destinationClient.ClientPath), destinationClient: destinationClient.Profile);
    }
    public string RenameCollection(CollectionItemSummary item, string category, string newName)
    {
        RequireCategory(item.Path, category);
        ValidateName(newName);
        Prevalidate(item.Path, Path.Combine(Path.GetDirectoryName(item.Path)!, newName));
        AdoptCollection(item, category);
        return Relocate(item.Path, Path.Combine(Path.GetDirectoryName(item.Path)!, newName), () => _files.Rename(item.Path, newName), collectionName: newName.Trim());
    }
    public string MoveCollection(CollectionItemSummary item, string category, string destinationDirectory)
    {
        RequireCategory(item.Path, category); RequireCategory(destinationDirectory, category, allowRoot: true);
        Prevalidate(item.Path, Path.Combine(destinationDirectory, Path.GetFileName(item.Path)));
        if (_collections.LoadAll().Any(existing => !Same(Path.Combine(_root, existing.RelativePath), item.Path) && WorkspaceEntityPaths.IsInside(Path.Combine(_root, existing.RelativePath), destinationDirectory)))
            throw new ArgumentException("Choisissez un dossier de classement, pas une autre fiche.");
        AdoptCollection(item, category);
        return Relocate(item.Path, Path.Combine(destinationDirectory, Path.GetFileName(item.Path)), () => _files.Move(item.Path, destinationDirectory));
    }
    public TrashEntry TrashClient(ClientSummary client) => TrashPath(client.ClientPath, "Client : " + client.DisplayName);
    public TrashEntry TrashProject(ProjectSummary project) => TrashPath(project.ProjectPath, "Projet : " + project.Reference);
    public TrashEntry TrashCollection(CollectionItemSummary item) => TrashPath(item.Path, item.Name);

    public TrashEntry TrashPath(string path, string? label = null)
    {
        RecoverPendingOperations();
        var companions = RelatedProfiles(path).Select(change => change.Path).ToArray();
        return _files.Trash(path, companions, label ?? Path.GetFileName(path));
    }

    // File browsers route entity roots through the same rules as the fiche actions.
    public string RenamePath(string path, string name)
    {
        var client = new ClientCatalog(_clients).Load(_root).FirstOrDefault(item => Same(item.ClientPath, path));
        if (client is not null) return RenameClient(client, name);
        var project = new ProjectCatalog(_projects).Load(_root).FirstOrDefault(item => Same(item.ProjectPath, path));
        if (project is not null) return RenameProject(project, name);
        var collection = _collections.FindByPath(path);
        if (collection is not null) return RenameCollection(new(collection.Name, path, DateTimeOffset.UtcNow, collection), collection.Category, name);
        var legacyCollection = FindLegacyCollection(path);
        if (legacyCollection is not null) return RenameCollection(legacyCollection.Value.Item, legacyCollection.Value.Category, name);
        ValidateName(name);
        return Relocate(path, Path.Combine(Path.GetDirectoryName(path)!, name), () => _files.Rename(path, name));
    }
    public string MovePath(string path, string destination)
    {
        var client = new ClientCatalog(_clients).Load(_root).FirstOrDefault(item => Same(item.ClientPath, path));
        if (client is not null) return MoveClient(client, destination);
        var project = new ProjectCatalog(_projects).Load(_root).FirstOrDefault(item => Same(item.ProjectPath, path));
        if (project is not null)
        {
            var target = new ClientCatalog(_clients).Load(_root).FirstOrDefault(item => Same(item.ClientPath, destination))
                ?? throw new ArgumentException("Un projet doit être déplacé vers le dossier d’un client existant.");
            return MoveProject(project, target);
        }
        var collection = _collections.FindByPath(path);
        if (collection is not null) return MoveCollection(new(collection.Name, path, DateTimeOffset.UtcNow, collection), collection.Category, destination);
        var legacyCollection = FindLegacyCollection(path);
        if (legacyCollection is not null) return MoveCollection(legacyCollection.Value.Item, legacyCollection.Value.Category, destination);
        if (_clients.LoadAll().Any(profile => WorkspaceEntityPaths.IsInside(path,
            WorkspaceEntityPaths.Resolve(_root, profile.RelativePath ?? Path.Combine("01_CLIENTS", profile.FolderName), "01_CLIENTS"))))
            ValidateClientDestination(destination);
        return Relocate(path, Path.Combine(destination, Path.GetFileName(path)), () => _files.Move(path, destination));
    }

    private string Relocate(string source, string target, Func<string> move, string? projectName = null, ClientProfile? destinationClient = null, string? collectionName = null)
    {
        RecoverPendingOperations();
        source = Path.GetFullPath(source); target = Path.GetFullPath(target);
        Prevalidate(source, target);
        var changes = RelatedProfiles(source, target, projectName, destinationClient, collectionName);
        if (changes.Count == 0) return move();
        var operationDirectory = Path.Combine(_root, ".ms3dprint-manager", "operations", Guid.NewGuid().ToString("N"));
        WorkspacePathSafety.EnsureNoLinks(operationDirectory);
        Directory.CreateDirectory(operationDirectory);
        var journal = new RelocationJournal(Path.GetRelativePath(_root, source), Path.GetRelativePath(_root, target), changes.Select(c => Path.GetRelativePath(_root, c.Path)).ToArray());
        var snapshots = changes.Select(change =>
        {
            var path = ResolveProfilePath(Path.GetRelativePath(_root, change.Path));
            PreflightProfileDestination(path);
            var bytes = File.ReadAllBytes(path);
            ValidateSnapshot(path, bytes);
            return bytes;
        }).ToArray();
        for (var index = 0; index < changes.Count; index++) WriteDurable(Path.Combine(operationDirectory, index + ".json"), snapshots[index]);
        var journalPath = Path.Combine(operationDirectory, "journal.json");
        WriteJournal(journalPath, journal);
        var moved = false;
        string result;
        try
        {
            result = move();
            moved = true;
            foreach (var change in changes) change.Save();
            WriteJournal(journalPath, journal with { Committed = true });
        }
        catch
        {
            if (moved) RecoverOperation(operationDirectory, journal);
            else
            {
                WriteJournal(journalPath, journal with { Committed = true });
                TryCleanup(operationDirectory, journal);
            }
            throw;
        }
        TryCleanup(operationDirectory, journal);
        return result;
    }

    private List<ProfileChange> RelatedProfiles(string source, string? target = null, string? projectName = null, ClientProfile? destinationClient = null, string? collectionName = null)
    {
        var changes = new List<ProfileChange>();
        var now = DateTimeOffset.UtcNow;
        string NewPath(string oldPath) => target is null ? oldPath : Path.GetFullPath(Path.Combine(target, Path.GetRelativePath(source, oldPath)));
        foreach (var profile in _clients.LoadAll())
        {
            var path = WorkspaceEntityPaths.Resolve(_root, profile.RelativePath ?? Path.Combine("01_CLIENTS", profile.FolderName), "01_CLIENTS");
            if (!WorkspaceEntityPaths.IsInside(source, path)) continue;
            var moved = NewPath(path);
            WorkspaceEntityPaths.Resolve(_root, Path.GetRelativePath(_root, moved), "01_CLIENTS");
            var updated = profile with { FolderName = Path.GetFileName(moved), RelativePath = Path.GetRelativePath(_root, moved), UpdatedAt = now };
            changes.Add(new(Path.Combine(_root, ".ms3dprint-manager", "clients", profile.Id + ".json"), () => _clients.Update(updated)));
        }
        // A read-tolerant catalogue must not silently omit a locked/corrupt companion during a mutation.
        _ = _projects.LoadAll();
        var projects = new ProjectCatalog(_projects).Load(_root);
        foreach (var project in projects.Where(p => p.Profile is not null && WorkspaceEntityPaths.IsInside(source, p.ProjectPath)))
        {
            var moved = NewPath(project.ProjectPath);
            WorkspaceEntityPaths.Resolve(_root, Path.GetRelativePath(_root, moved), "01_CLIENTS");
            var profile = project.Profile!;
            var updated = profile with { FolderName = Path.GetFileName(moved), RelativePath = Path.GetRelativePath(_root, moved), UpdatedAt = now,
                ProjectName = Same(source, project.ProjectPath) && projectName is not null ? projectName : profile.ProjectName,
                ClientId = destinationClient?.Id ?? profile.ClientId, ClientCode = destinationClient?.ClientCode ?? profile.ClientCode };
            changes.Add(new(Path.Combine(_root, ".ms3dprint-manager", "projects", profile.Id + ".json"), () => _projects.Update(updated)));
        }
        foreach (var profile in _collections.LoadAll())
        {
            var path = WorkspaceEntityPaths.Resolve(_root, profile.RelativePath, profile.Category);
            if (!WorkspaceEntityPaths.IsInside(source, path)) continue;
            WorkspaceEntityPaths.Resolve(_root, Path.GetRelativePath(_root, NewPath(path)), profile.Category);
            var updated = profile with { RelativePath = Path.GetRelativePath(_root, NewPath(path)), UpdatedAt = now,
                Name = Same(source, path) && collectionName is not null ? collectionName : profile.Name };
            changes.Add(new(_collections.ProfilePath(profile.Id), () => _collections.Update(updated)));
        }
        return changes;
    }

    public void RecoverPendingOperations()
    {
        var directory = Path.Combine(_root, ".ms3dprint-manager", "operations");
        WorkspacePathSafety.EnsureNoLinks(directory);
        if (!Directory.Exists(directory)) return;
        foreach (var operation in Directory.EnumerateDirectories(directory))
        {
            var journalPath = Path.Combine(operation, "journal.json");
            if (!File.Exists(journalPath)) continue;
            WorkspacePathSafety.EnsureNoLinks(journalPath);
            RelocationJournal journal;
            try { journal = JsonSerializer.Deserialize<RelocationJournal>(File.ReadAllBytes(journalPath)) ?? throw new IOException("Journal d’opération illisible."); }
            catch (JsonException exception) { throw new IOException("Journal d’opération illisible ; les sauvegardes sont conservées.", exception); }
            RecoverOperation(operation, journal);
        }
    }
    private void RecoverOperation(string directory, RelocationJournal journal)
    {
        WorkspacePathSafety.EnsureNoLinks(directory);
        var source = ResolveJournalPath(journal.Source);
        var target = ResolveJournalPath(journal.Target);
        if (journal.Profiles is null || journal.Profiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Profiles.Length)
            throw new IOException("Liste de sauvegardes invalide.");
        var profiles = journal.Profiles.Select(ResolveProfilePath).ToArray();
        if (journal.Committed) { TryCleanup(directory, journal); return; }
        // Validate every backup and destination before moving a single document or replacing any JSON.
        var snapshots = profiles.Select((path, index) =>
        {
            var backup = Path.Combine(directory, index + ".json");
            WorkspacePathSafety.EnsureNoLinks(backup);
            var bytes = File.ReadAllBytes(backup);
            ValidateSnapshot(path, bytes);
            PreflightProfileDestination(path);
            return bytes;
        }).ToArray();
        if (!Same(Path.GetDirectoryName(source)!, Path.GetDirectoryName(target)!) && !string.Equals(Path.GetFileName(source), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Journal de déplacement incohérent : aucune donnée n’a été modifiée.");
        if (Same(source, target) && (File.Exists(target) || Directory.Exists(target)))
            _files.Rename(target, Path.GetFileName(source));
        if (!File.Exists(source) && !Directory.Exists(source) && (File.Exists(target) || Directory.Exists(target)))
        {
            if (Same(Path.GetDirectoryName(source)!, Path.GetDirectoryName(target)!)) _files.Rename(target, Path.GetFileName(source));
            else if (string.Equals(Path.GetFileName(source), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase)) _files.Move(target, Path.GetDirectoryName(source)!);
            else throw new IOException("Journal de déplacement incohérent : aucune donnée n’a été modifiée.");
        }
        else if (!Same(source, target) && (Directory.Exists(target) || File.Exists(target)))
            throw new IOException("Une opération interrompue nécessite une vérification : les deux emplacements existent. Aucune donnée n’a été écrasée.");
        if (!File.Exists(source) && !Directory.Exists(source)) throw new IOException("Le dossier d’une opération interrompue est introuvable. Les sauvegardes sont conservées.");
        for (var index = 0; index < journal.Profiles.Length; index++)
        {
            var path = profiles[index];
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { WriteDurable(temporary, snapshots[index]); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // A finalized rollback may safely resume cleanup even if some backups are already removed.
        WriteJournal(Path.Combine(directory, "journal.json"), journal with { Committed = true });
        TryCleanup(directory, journal);
    }
    private static void TryCleanup(string directory, RelocationJournal journal)
    {
        try
        {
            WorkspacePathSafety.EnsureNoLinks(directory);
            for (var index = 0; index < journal.Profiles.Length; index++) File.Delete(Path.Combine(directory, index + ".json"));
            if (Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) is not "journal.json" and not "journal.json.tmp")) return;
            File.Delete(Path.Combine(directory, "journal.json.tmp"));
            File.Delete(Path.Combine(directory, "journal.json"));
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException) { } // Cleanup cannot turn a committed mutation into a reported failure.
        catch (UnauthorizedAccessException) { }
    }
    private static void WriteDurable(string path, byte[] bytes)
    { WorkspacePathSafety.EnsureNoLinks(path); using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(flushToDisk: true); }
    private static void WriteJournal(string path, RelocationJournal journal)
    {
        var temporary = path + ".tmp";
        try { WriteDurable(temporary, JsonSerializer.SerializeToUtf8Bytes(journal)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void RequireCategory(string path, string category, bool allowRoot = false)
    {
        var categoryRoot = Path.Combine(_root, category);
        if (!WorkspaceEntityPaths.IsInside(categoryRoot, path) || (!allowRoot && Same(categoryRoot, path))) throw new ArgumentException("La destination doit rester dans la catégorie de cet élément.");
    }
    private void ValidateClientDestination(string destination)
    {
        RequireCategory(destination, "01_CLIENTS", allowRoot: true);
        if (new ClientCatalog(_clients).Load(_root).Any(existing =>
            (existing.Profile is not null || existing.ProjectCount > 0 || Directory.Exists(Path.Combine(existing.ClientPath, "00_CLIENT")))
            && WorkspaceEntityPaths.IsInside(existing.ClientPath, destination)))
            throw new ArgumentException("Choisissez un dossier de classement, pas le dossier d’un client.");
    }
    private (CollectionItemSummary Item, string Category)? FindLegacyCollection(string path)
    {
        var category = Path.GetRelativePath(_root, path).Split(Path.DirectorySeparatorChar)[0];
        if (!new[] { "02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "06_FOURNISSEURS" }.Contains(category) || !Directory.Exists(path)) return null;
        var item = new CollectionCatalog().Load(_root, category).FirstOrDefault(candidate => Same(candidate.Path, path));
        return item is null ? null : (item, category);
    }
    private void Prevalidate(string source, string target)
    {
        var original = ResolveJournalPath(Path.GetRelativePath(_root, source));
        var destination = ResolveJournalPath(Path.GetRelativePath(_root, target));
        ValidateName(Path.GetFileName(destination));
        if (!File.Exists(original) && !Directory.Exists(original)) throw new FileNotFoundException("Élément source introuvable.", original);
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if (!Same(original, destination) && (File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException("Un élément existe déjà à la destination. Aucun écrasement n’est autorisé.");
        if (!Same(original, destination) && WorkspaceEntityPaths.IsInside(original, destination))
            throw new IOException("Un dossier ne peut pas être déplacé dans lui-même.");
        CheckTree(original);
        WorkspacePathSafety.EnsureNoLinks(parent);
    }
    private static void CheckTree(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        if (Directory.Exists(path)) foreach (var child in Directory.EnumerateFileSystemEntries(path)) CheckTree(child);
    }
    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Any(c => c < 32) || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw new ArgumentException("Nom de dossier invalide.");
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("Nom réservé par Windows.");
    }
    private string ResolveProfilePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
            throw new IOException("Chemin de sauvegarde de fiche invalide.");
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        var parent = Path.GetDirectoryName(path)!;
        var metadata = Path.Combine(_root, ".ms3dprint-manager");
        if (!new[] { "clients", "projects", "collections" }.Any(category => Same(parent, Path.Combine(metadata, category)))
            || !Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id) || id == Guid.Empty
            || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Chemin de sauvegarde de fiche invalide.");
        WorkspacePathSafety.EnsureNoLinks(path);
        return path;
    }
    private static void PreflightProfileDestination(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
        if (!Directory.Exists(Path.GetDirectoryName(path)!)) throw new IOException("Le dossier des fiches à restaurer est introuvable.");
        if (Directory.Exists(path)) throw new IOException("Un dossier occupe le chemin de la fiche à restaurer.");
        if (!File.Exists(path)) return;
        try { using var access = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); }
        catch (UnauthorizedAccessException exception) { throw new IOException("La fiche à restaurer est inaccessible.", exception); }
    }
    private void ValidateSnapshot(string path, byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Id", out var id)
                || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var value) || value != Guid.Parse(Path.GetFileNameWithoutExtension(path)))
                throw new IOException("La sauvegarde ne correspond pas à la fiche à restaurer.");
            var category = Path.GetFileName(Path.GetDirectoryName(path));
            if (category == "clients")
            {
                var profile = JsonSerializer.Deserialize<ClientProfile>(bytes) ?? throw new IOException("Sauvegarde client vide.");
                if (profile.PrimaryContact is null || !Enum.IsDefined(profile.Kind) || string.IsNullOrWhiteSpace(profile.FolderName)
                    || string.IsNullOrWhiteSpace(profile.ClientCode)
                    || (profile.Kind == ClientKind.Professional && string.IsNullOrWhiteSpace(profile.CompanyName))
                    || (profile.Kind == ClientKind.Individual && string.IsNullOrWhiteSpace(profile.LastName)))
                    throw new IOException("La sauvegarde client est incomplète.");
                if (profile.RelativePath is not null) WorkspaceEntityPaths.Resolve(_root, profile.RelativePath, "01_CLIENTS");
            }
            else if (category == "projects")
            {
                var profile = JsonSerializer.Deserialize<ProjectProfile>(bytes) ?? throw new IOException("Sauvegarde projet vide.");
                if (profile.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(profile.ClientCode) || string.IsNullOrWhiteSpace(profile.Reference)
                    || string.IsNullOrWhiteSpace(profile.FolderName) || string.IsNullOrWhiteSpace(profile.ProjectName) || !Enum.IsDefined(profile.Status))
                    throw new IOException("La sauvegarde projet est incomplète.");
                if (profile.RelativePath is not null) WorkspaceEntityPaths.Resolve(_root, profile.RelativePath, "01_CLIENTS");
            }
            else
            {
                var profile = JsonSerializer.Deserialize<CollectionProfile>(bytes) ?? throw new IOException("Sauvegarde collection vide.");
                if (!new[] { "02_MODELES_3D", "03_PRODUITS_MS3DPRINT", "06_FOURNISSEURS" }.Contains(profile.Category)
                    || string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.RelativePath))
                    throw new IOException("La sauvegarde collection est incomplète.");
                WorkspaceEntityPaths.Resolve(_root, profile.RelativePath, profile.Category);
            }
        }
        catch (JsonException exception) { throw new IOException("Sauvegarde JSON illisible ; les documents et sauvegardes sont conservés.", exception); }
        catch (ArgumentException exception) { throw new IOException("La sauvegarde de fiche contient un chemin invalide.", exception); }
    }
    private void AdoptCollection(CollectionItemSummary item, string category)
    {
        if (_collections.FindByPath(item.Path) is not null) return;
        var now = DateTimeOffset.UtcNow;
        _collections.Create(new(Guid.NewGuid(), category, Path.GetRelativePath(_root, item.Path), item.Name, null, null, null, null, null, null, null, now, now));
    }
    private string ResolveJournalPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..")) throw new IOException("Chemin de journal invalide.");
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        if (!WorkspaceEntityPaths.IsInside(_root, path) || Same(_root, path)) throw new IOException("Chemin de journal hors de l’espace géré.");
        if (WorkspaceEntityPaths.IsInside(Path.Combine(_root, ".ms3dprint-manager"), path)
            || (Same(Path.GetDirectoryName(path)!, _root) && FolderTemplates.Main.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Le journal ne peut pas déplacer les dossiers protégés.");
        WorkspacePathSafety.EnsureNoLinks(path);
        return path;
    }
    private static bool Same(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private sealed record ProfileChange(string Path, Action Save);
    private sealed record RelocationJournal(string Source, string Target, string[] Profiles, bool Committed = false);
}
