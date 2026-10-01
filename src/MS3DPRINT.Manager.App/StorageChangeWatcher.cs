namespace MS3DPRINT.Manager.App;

/// <summary>One-shot debounce driven exclusively by filesystem events.</summary>
public sealed class StorageChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly object _gate = new();
    private bool _disposed;
    public StorageChangeWatcher(string root, Action changed, Action<Exception> error, TimeSpan? delay = null)
    {
        var interval = delay ?? TimeSpan.FromMilliseconds(650);
        _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        try { _debounce = new Timer(_ => { lock (_gate) { if (!_disposed) changed(); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch { _watcher.Dispose(); throw; }
        bool Relevant(string path, WatcherChangeTypes change)
        {
            var relative = Path.GetRelativePath(root, path);
            var metadata = ".ms3dprint-manager";
            if (!relative.Equals(metadata, StringComparison.OrdinalIgnoreCase) && !relative.StartsWith(metadata + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var folder in new[] { "clients", "projects" })
            {
                var catalog = Path.Combine(metadata, folder);
                if (relative.Equals(catalog, StringComparison.OrdinalIgnoreCase)) return change is WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed;
                if (relative.StartsWith(catalog + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            }
            return relative.Equals(metadata, StringComparison.OrdinalIgnoreCase) && change is WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed;
        }
        void Schedule(object? sender, FileSystemEventArgs args)
        {
            if (!Relevant(args.FullPath, args.ChangeType) && !(args is RenamedEventArgs renamed && Relevant(renamed.OldFullPath, args.ChangeType))) return;
            lock (_gate) { if (!_disposed) _debounce.Change(interval, Timeout.InfiniteTimeSpan); }
        }
        _watcher.Changed += Schedule; _watcher.Created += Schedule; _watcher.Deleted += Schedule; _watcher.Renamed += Schedule;
        _watcher.Error += (_, args) => { lock (_gate) { if (!_disposed) error(args.GetException()); } };
        try { _watcher.EnableRaisingEvents = true; }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watcher.Dispose(); _debounce.Dispose(); }
    }
}
