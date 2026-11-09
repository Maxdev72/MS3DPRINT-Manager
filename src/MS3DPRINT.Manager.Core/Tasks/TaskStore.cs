using System.Text.Json;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tasks;

/// <summary>One JSON file per task in <c>.ms3dprint-manager/tasks</c>. A removed task stays as a dated tombstone so sync never brings it back.</summary>
public sealed class TaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;

    public TaskStore(WorkspaceMetadataPaths paths) => _directory = Path.Combine(paths.MetadataDirectory, "tasks");

    /// <summary>Every task, tombstones included; an unreadable file is skipped rather than blocking the page.</summary>
    public IReadOnlyList<WorkTask> LoadAll()
    {
        WorkspacePathSafety.EnsureNoLinks(_directory);
        if (!Directory.Exists(_directory)) return [];
        return Directory.EnumerateFiles(_directory, "*.json").Select(TryRead).OfType<WorkTask>().ToArray();
    }

    public IReadOnlyList<WorkTask> LoadActive() => LoadAll().Where(t => !t.IsDeleted).ToArray();

    public WorkTask? Find(Guid id) => File.Exists(FilePath(id)) ? TryRead(FilePath(id)) : null;

    public WorkTask Create(WorkTask task)
    {
        WorkTask.Validate(task);
        using var mutation = ProfileMutationLock.Acquire(FilePath(task.Id));
        if (File.Exists(FilePath(task.Id))) throw new InvalidOperationException("Cette tâche existe déjà.");
        Write(task);
        return task;
    }

    /// <summary>A change made here: refused if the task changed since it was read, then dated now.</summary>
    public WorkTask Update(WorkTask task)
    {
        WorkTask.Validate(task);
        using var mutation = ProfileMutationLock.Acquire(FilePath(task.Id));
        var previous = Find(task.Id) ?? throw new InvalidOperationException("La tâche est introuvable. Actualisez la liste.");
        ProfileMutationLock.CheckVersion(previous.UpdatedAt, task.UpdatedAt);
        var saved = task with { UpdatedAt = ProfileMutationLock.NextVersion(previous.UpdatedAt, previous.UpdatedAt) };
        Write(saved);
        return saved;
    }

    public WorkTask Remove(WorkTask task) => Update(task with { DeletedAt = DateTimeOffset.UtcNow });

    /// <summary>Sync: keeps the copy with the latest <c>UpdatedAt</c>, its date unchanged. Returns true when this copy was written.</summary>
    public bool Merge(WorkTask incoming)
    {
        WorkTask.Validate(incoming);
        using var mutation = ProfileMutationLock.Acquire(FilePath(incoming.Id));
        if (Find(incoming.Id) is { } current && current.UpdatedAt >= incoming.UpdatedAt) return false;
        Write(incoming);
        return true;
    }

    private void Write(WorkTask task)
    {
        Directory.CreateDirectory(_directory);
        WorkspacePathSafety.EnsureNoLinks(_directory);
        var path = FilePath(task.Id);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, task, JsonOptions); stream.Flush(true); }
            if (File.Exists(path)) Storage.AtomicFile.Replace(temporary, path);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static WorkTask? TryRead(string path)
    {
        try
        {
            WorkspacePathSafety.EnsureNoLinks(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var task = JsonSerializer.Deserialize<WorkTask>(stream);
            if (task is null) return null;
            WorkTask.Validate(task);
            return task;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private string FilePath(Guid id) => Path.Combine(_directory, id + ".json");
}
