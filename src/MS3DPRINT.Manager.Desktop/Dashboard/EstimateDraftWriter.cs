using System.Collections.Concurrent;

namespace MS3DPRINT.Manager.Desktop.Dashboard;

/// <summary>Serializes writes and removals. Versions are session-local, never quote revisions.</summary>
public sealed class EstimateDraftWriter
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<(string Root, Guid Id), long> _saved = new();
    private readonly Action<string, EstimateDraft> _save;
    private readonly Action<string, Guid> _remove;

    public EstimateDraftWriter(Action<string, EstimateDraft>? save = null, Action<string, Guid>? remove = null)
    {
        _save = save ?? ((root, draft) => new EstimateDraftStore(root).Save(draft));
        _remove = remove ?? ((root, id) => new EstimateDraftStore(root).Remove(id));
    }

    private static (string, Guid) Key(string root, Guid id) => (OperatingSystem.IsWindows() ? Path.GetFullPath(root).ToUpperInvariant() : Path.GetFullPath(root), id);
    public bool IsSaved(string root, Guid id, long revision) => _saved.TryGetValue(Key(root, id), out var saved) && saved >= revision;

    // Autosave invokes this on a worker. Explicit transitions retain a synchronous durability barrier.
    public void Save(string root, EstimateDraft draft, long revision)
    {
        lock (_gate)
        {
            if (IsSaved(root, draft.Id, revision)) return;
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Le dossier de cet atelier n’est pas accessible.");
            _save(root, draft);
            _saved[Key(root, draft.Id)] = revision;
        }
    }

    public void Remove(string root, Guid id)
    {
        lock (_gate)
        {
            _remove(root, id);
            // A delayed autosave of a consumed/discarded draft must never recreate it.
            _saved[Key(root, id)] = long.MaxValue;
        }
    }
}
