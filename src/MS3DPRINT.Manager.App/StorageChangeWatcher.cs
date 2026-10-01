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
        _debounce = new Timer(_ => { lock (_gate) { if (!_disposed) changed(); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        void Schedule(object? sender, FileSystemEventArgs args)
        {
            var relative = Path.GetRelativePath(root, args.FullPath);
            if (relative.StartsWith(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase)
                && !(relative.StartsWith(Path.Combine(".ms3dprint-manager", "clients") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith(Path.Combine(".ms3dprint-manager", "projects") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return;
            if (relative.StartsWith(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase) && !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
            lock (_gate) { if (!_disposed) _debounce.Change(interval, Timeout.InfiniteTimeSpan); }
        }
        _watcher.Changed += Schedule; _watcher.Created += Schedule; _watcher.Deleted += Schedule; _watcher.Renamed += Schedule;
        _watcher.Error += (_, args) => error(args.GetException());
        _watcher.EnableRaisingEvents = true;
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watcher.Dispose(); _debounce.Dispose(); }
    }
}
