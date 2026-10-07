using System.Collections.Concurrent;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Templates;

namespace MS3DPRINT.Manager.Core.Workspace;

/// <summary>Non-overwriting workspace operations with a persistent, recoverable trash journal.</summary>
public sealed class ManagedFileService
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private readonly string _metadata;
    private readonly string _trash;
    private readonly object _gate;
    public IReadOnlyList<string> TrashReadErrors { get; private set; } = [];

    public ManagedFileService(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _metadata = Path.Combine(_root, ".ms3dprint-manager");
        _trash = Path.Combine(_metadata, "trash");
        _gate = Gates.GetOrAdd(_root, _ => new object());
        CheckLinks(_root);
    }

    public string Rename(string path, string newName)
    {
        lock (_gate)
        {
            ValidateName(newName);
            var source = Source(path);
            var destination = Managed(Path.Combine(Path.GetDirectoryName(source)!, newName));
            ProtectMetadata(destination);
            if (string.Equals(source, destination, StringComparison.Ordinal)) return source;
            if (Same(source, destination))
            {
                // Windows requires an intermediate name to update the directory entry casing.
                var intermediate = Path.Combine(Path.GetDirectoryName(source)!, ".rename-" + Guid.NewGuid().ToString("N"));
                MoveItem(source, intermediate);
                try { MoveItem(intermediate, destination); }
                catch { MoveItem(intermediate, source); throw; }
            }
            else { Vacant(destination); MoveItem(source, destination); }
            return destination;
        }
    }

    public string Move(string path, string destinationDirectory)
    {
        lock (_gate)
        {
            var source = Source(path);
            var parent = DestinationDirectory(destinationDirectory);
            var destination = Managed(Path.Combine(parent, Path.GetFileName(source)));
            if (Same(source, destination) || Below(parent, source)) throw new IOException("Impossible de déplacer un élément dans lui-même.");
            Vacant(destination);
            MoveItem(source, destination);
            return destination;
        }
    }

    public string CreateFolder(string parent, string name)
    {
        lock (_gate)
        {
            ValidateName(name);
            var destination = Managed(Path.Combine(DestinationDirectory(parent), name));
            ProtectMetadata(destination);
            Vacant(destination);
            // The gate serializes competing operations made through this service.
            Directory.CreateDirectory(destination);
            return destination;
        }
    }

    public string Import(string sourceFile, string destinationDirectory)
    {
        lock (_gate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
            var source = Path.GetFullPath(sourceFile);
            CheckLinks(source);
            if (!File.Exists(source)) throw new FileNotFoundException("Fichier source introuvable.", source);
            ValidateName(Path.GetFileName(source));
            var parent = DestinationDirectory(destinationDirectory);
            var destination = Managed(Path.Combine(parent, Path.GetFileName(source)));
            Vacant(destination);
            var temporary = Path.Combine(parent, ".import-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                File.Move(temporary, destination, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }
    }

    public TrashEntry Trash(string path, IReadOnlyList<string>? companionPaths = null, string? label = null)
    {
        lock (_gate)
        {
            var primary = Source(path, allowProfile: true);
            var paths = new List<string> { primary };
            foreach (var companion in companionPaths ?? []) paths.Add(Source(companion, allowProfile: true));
            for (var i = 0; i < paths.Count; i++)
                for (var j = i + 1; j < paths.Count; j++)
                    if (Same(paths[i], paths[j]) || Below(paths[i], paths[j]) || Below(paths[j], paths[i]))
                        throw new IOException("Les éléments de corbeille se chevauchent.");
            var entry = new TrashEntry(Guid.NewGuid(), label ?? Path.GetFileName(primary), DateTimeOffset.UtcNow);
            var directory = TransactionDirectory(entry.Id);
            Vacant(directory);
            Directory.CreateDirectory(directory);
            var manifest = new Manifest { Entry = entry, State = "Trashing", Items = paths.Select((p, i) => new Item { Original = Path.GetRelativePath(_root, p), Payload = i.ToString(), IsDirectory = Directory.Exists(p) }).ToList() };
            Save(directory, manifest);
            var moved = new List<(string Source, string Destination)>();
            try
            {
                foreach (var item in manifest.Items)
                {
                    var original = Original(item);
                    var payload = Payload(directory, item);
                    Vacant(payload);
                    MoveItem(original, payload);
                    moved.Add((original, payload));
                }
                manifest.State = "Trashed";
                Save(directory, manifest);
                return entry;
            }
            catch
            {
                foreach (var pair in moved.AsEnumerable().Reverse())
                    try { Vacant(pair.Source); MoveItem(pair.Destination, pair.Source); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (manifest.Items.All(item => !Exists(Payload(directory, item)))) Cleanup(directory);
                // Any payload which could not be rolled back stays discoverable in its journal.
                throw;
            }
        }
    }

    public IReadOnlyList<TrashEntry> ListTrash()
    {
        lock (_gate)
        {
            TrashReadErrors = [];
            CheckLinks(_trash);
            if (!Directory.Exists(_trash)) return [];
            var entries = new List<TrashEntry>();
            var errors = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(_trash))
            {
                try
                {
                    CheckLinks(directory);
                    if (!File.Exists(Path.Combine(directory, "manifest.json")))
                        throw new IOException("Manifeste absent ; les données de cette entrée ont été conservées.");
                    var manifest = Load(directory);
                    if (manifest.State != "Restored") entries.Add(manifest.Entry);
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add(Path.GetFileName(directory) + " : " + exception.Message);
                }
            }
            TrashReadErrors = errors.AsReadOnly();
            return entries.OrderByDescending(entry => entry.DeletedAt).ToList();
        }
    }

    public void Restore(Guid id)
    {
        lock (_gate)
        {
            var directory = TransactionDirectory(id);
            var manifest = Load(directory);
            if (manifest.State == "Restored") { Cleanup(directory); return; }
            var pending = new List<(string Original, string Payload)>();
            foreach (var item in manifest.Items)
            {
                var original = Original(item);
                var payload = Payload(directory, item);
                ValidateRestoreParents(original);
                CheckTree(payload);
                if (Exists(payload)) { Vacant(original); pending.Add((original, payload)); }
                else if (manifest.State == "Trashed" || !Exists(original)) throw new IOException("Élément de corbeille manquant.");
                else CheckTree(original); // A interrupted transaction may leave some entries at their origin.
            }
            manifest.State = "Restoring";
            Save(directory, manifest);
            var restored = new List<(string Original, string Payload)>();
            try
            {
                foreach (var pair in pending)
                {
                    CheckLinks(Path.GetDirectoryName(pair.Original)!);
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Original)!);
                    MoveItem(pair.Payload, pair.Original);
                    restored.Add(pair);
                }
                manifest.State = "Restored";
                Save(directory, manifest);
            }
            catch
            {
                foreach (var pair in restored.AsEnumerable().Reverse())
                    try { Vacant(pair.Payload); MoveItem(pair.Original, pair.Payload); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                // Restore can resume by examining the journal and actual locations.
                throw;
            }
            // Cleanup follows the committed restoration and must never trigger a data rollback.
            Cleanup(directory);
        }
    }

    private static void ValidateRestoreParents(string path)
    {
        for (var parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
        {
            CheckLinks(parent);
            if (File.Exists(parent)) throw new IOException("Un fichier occupe un dossier parent de restauration.");
        }
    }

    private string Source(string path, bool allowProfile = false)
    {
        var full = Managed(path);
        if (Same(full, _root) || (Same(Path.GetDirectoryName(full)!, _root) && FolderTemplates.Main.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase)))
            throw new IOException("Ce dossier structurel est protégé.");
        if (Same(full, _metadata) || Below(full, _metadata))
        {
            if (!allowProfile || !IsProfile(full)) throw new IOException("Les métadonnées sont protégées.");
        }
        if (!Exists(full)) throw new FileNotFoundException("Élément introuvable.", full);
        CheckTree(full);
        return full;
    }

    private bool IsProfile(string full)
    {
        var parts = Path.GetRelativePath(_metadata, full).Split(Path.DirectorySeparatorChar);
        return parts.Length == 2 && new[] { "clients", "projects", "collections", "filaments" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
            && string.Equals(Path.GetExtension(full), ".json", StringComparison.OrdinalIgnoreCase) && File.Exists(full);
    }

    private string DestinationDirectory(string path)
    {
        var full = Managed(path);
        if (Same(full, _metadata) || Below(full, _metadata)) throw new IOException("Les métadonnées sont protégées.");
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        CheckTree(full);
        return full;
    }

    private void ProtectMetadata(string path)
    {
        if (Same(path, _metadata) || Below(path, _metadata)) throw new IOException("Les métadonnées sont protégées.");
    }

    private string Managed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..")) throw new IOException("Les chemins parents sont interdits.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_root, path)));
        if (!Same(full, _root) && !Below(full, _root)) throw new IOException("Chemin hors de l'espace géré.");
        CheckLinks(full);
        return full;
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(c => c < 32) || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw new ArgumentException("Nom de fichier invalide.", nameof(name));
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
            throw new ArgumentException("Nom réservé par Windows.", nameof(name));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Below(string path, string parent) => path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static void Vacant(string path) { CheckLinks(path); if (Exists(path)) throw new IOException("Un élément existe déjà à cet emplacement."); }
    private static void MoveItem(string source, string destination) { if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination, false); }

    private static void CheckLinks(string path)
    {
        WorkspacePathSafety.EnsureNoLinks(path);
    }

    private static void CheckTree(string path)
    {
        CheckLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) CheckTree(child);
    }

    private string TransactionDirectory(Guid id) => Managed(Path.Combine(_trash, id.ToString("N")));
    private string Original(Item item) => Managed(item.Original);
    private string Payload(string directory, Item item)
    {
        if (!int.TryParse(item.Payload, out var index) || index < 0 || item.Payload != index.ToString()) throw new IOException("Manifeste de corbeille invalide.");
        return Managed(Path.Combine(directory, item.Payload));
    }

    private Manifest Load(string directory)
    {
        var path = Managed(Path.Combine(directory, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? throw new IOException("Manifeste de corbeille vide.");
        if (manifest.Entry == null || manifest.Entry.Id == Guid.Empty || manifest.Entry.Label is null
            || Path.GetFileName(directory) != manifest.Entry.Id.ToString("N") || manifest.Items is null || manifest.Items.Count == 0
            || !new[] { "Trashing", "Trashed", "Restoring", "Restored" }.Contains(manifest.State))
            throw new IOException("Manifeste de corbeille invalide.");
        foreach (var item in manifest.Items)
        {
            if (item is null) throw new IOException("Élément de manifeste de corbeille invalide.");
            var original = Original(item);
            if (Same(original, _root) || Same(original, _metadata) || Below(original, _trash)
                || (Same(Path.GetDirectoryName(original)!, _root) && FolderTemplates.Main.Contains(Path.GetFileName(original), StringComparer.OrdinalIgnoreCase)))
                throw new IOException("Origine de corbeille invalide.");
            _ = Payload(directory, item);
        }
        return manifest;
    }

    private static void Save(string directory, Manifest manifest)
    {
        CheckLinks(directory);
        var temporary = Path.Combine(directory, "manifest.tmp");
        CheckLinks(temporary);
        CheckLinks(Path.Combine(directory, "manifest.json"));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, manifest); stream.Flush(true); }
        File.Move(temporary, Path.Combine(directory, "manifest.json"), true);
    }

    private static void Cleanup(string directory)
    {
        // Only delete journal files and an empty transaction directory, never payload data.
        if (Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) is not "manifest.tmp" and not "manifest.json"))
            throw new IOException("Le dossier de corbeille contient encore des éléments ; son journal est conservé.");
        File.Delete(Path.Combine(directory, "manifest.tmp"));
        File.Delete(Path.Combine(directory, "manifest.json"));
        Directory.Delete(directory, false);
    }

    private sealed class Manifest
    {
        public TrashEntry Entry { get; set; } = null!;
        public string State { get; set; } = "";
        public List<Item> Items { get; set; } = [];
    }
    private sealed class Item
    {
        public string Original { get; set; } = "";
        public string Payload { get; set; } = "";
        public bool IsDirectory { get; set; }
    }
}
