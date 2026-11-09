using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tasks;

/// <summary>A task list name; a removed list stays as a dated tombstone so sync never brings it back.</summary>
public sealed record TaskListEntry(string Name, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt = null)
{
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 60;
}

/// <summary>Lists kept even when empty, in <c>.ms3dprint-manager/task-lists.json</c>. Names compare without case.</summary>
public sealed class TaskListStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public TaskListStore(WorkspaceMetadataPaths paths) => _path = Path.Combine(paths.MetadataDirectory, "task-lists.json");

    public IReadOnlyList<TaskListEntry> LoadAll()
    {
        try
        {
            WorkspacePathSafety.EnsureNoLinks(_path);
            if (!File.Exists(_path)) return [];
            var entries = JsonSerializer.Deserialize<TaskListEntry[]>(File.ReadAllText(_path)) ?? [];
            return entries.Where(e => e is not null && TaskListEntry.IsValidName(e.Name)).ToArray();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    public IReadOnlyList<string> ActiveNames() => LoadAll().Where(e => e.DeletedAt is null).Select(e => e.Name).ToArray();

    public void Add(string name) => Change(name.Trim(), null);
    public void Remove(string name) => Change(name.Trim(), DateTimeOffset.UtcNow);

    /// <summary>Sync: each name keeps its latest dated state. Returns how many entries changed here.</summary>
    public int Merge(IEnumerable<TaskListEntry> incoming)
    {
        using var mutation = ProfileMutationLock.Acquire(_path);
        var entries = LoadAll().ToDictionary(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        var changed = 0;
        foreach (var entry in incoming.Where(e => e is not null && TaskListEntry.IsValidName(e.Name)))
        {
            if (entries.TryGetValue(entry.Name, out var current) && current.UpdatedAt >= entry.UpdatedAt) continue;
            entries[entry.Name] = entry with { Name = entry.Name.Trim() }; changed++;
        }
        if (changed > 0) Save(entries.Values);
        return changed;
    }

    private void Change(string name, DateTimeOffset? deleted)
    {
        if (!TaskListEntry.IsValidName(name)) throw new ArgumentException("Le nom de la liste est requis (60 caractères maximum).");
        using var mutation = ProfileMutationLock.Acquire(_path);
        var entries = LoadAll().ToDictionary(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
        var previous = entries.GetValueOrDefault(name)?.UpdatedAt ?? DateTimeOffset.MinValue;
        entries[name] = new TaskListEntry(name, ProfileMutationLock.NextVersion(previous, previous), deleted);
        Save(entries.Values);
    }

    private void Save(IEnumerable<TaskListEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), JsonOptions));
            if (File.Exists(_path)) Storage.AtomicFile.Replace(temporary, _path); else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
