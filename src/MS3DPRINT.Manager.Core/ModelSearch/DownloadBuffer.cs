using System.Text.Json;
using System.Text.Json.Nodes;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.ModelSearch;

/// <summary>A 3D file the embedded browser finished downloading into the buffer.</summary>
public sealed record BufferedFile(string Path, long Length)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>The 3D files that came out of a downloaded ZIP (opened in the background), or why none did.</summary>
public sealed record BufferedBatch(string Source, IReadOnlyList<BufferedFile> Files, string? Problem);

/// <summary>
/// Staging folder for what the embedded browser downloads: the browser profile is pointed at it, so nothing lands in the user's
/// Downloads folder, and a watcher announces each finished 3D file so it can be sent to the slicer.
/// </summary>
public sealed class DownloadBuffer : IDisposable
{
    /// <summary>What slicers open; archives and anything else stay in the folder without being announced as a model.</summary>
    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase) { ".stl", ".3mf", ".obj", ".step", ".stp", ".amf" };

    private static readonly TimeSpan StableWait = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    private readonly HashSet<string> _announced = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _offered = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;

    public DownloadBuffer(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
    }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MS3DPRINT Manager", "tampon");

    public string Folder { get; }

    /// <summary>Raised, on a background thread, once a 3D file is complete and no longer written to.</summary>
    public event Action<BufferedFile>? FileReady;

    /// <summary>Raised, on a background thread, once a downloaded ZIP has been opened (its 3D files are in a folder of the buffer).</summary>
    public event Action<BufferedBatch>? BatchReady;

    /// <summary>The offer window closed: these remaining files become ordinary temporary buffer entries again.</summary>
    public void ReleaseOffered(IEnumerable<string> paths)
    {
        var released = paths.Select(Path.GetFullPath).ToArray();
        lock (_gate) foreach (var path in released) _offered.Remove(path);
    }

    private static bool IsArchive(string path) => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsModelFile(string path)
        => !string.IsNullOrWhiteSpace(path) && ModelExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Makes the browser profile download into <see cref="Folder"/> without asking where to save. It edits the profile's
    /// <c>Preferences</c> file, so it must run before the browser starts (the browser rewrites that file when it exits).
    /// </summary>
    public void PrepareProfile(string userDataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataFolder);
        Directory.CreateDirectory(Folder);
        var profile = Path.Combine(Path.GetFullPath(userDataFolder), "EBWebView", "Default");
        Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "Preferences");

        JsonObject root;
        try { root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing ? existing : new JsonObject(); }
        catch (Exception exception) when (exception is JsonException or IOException) { root = new JsonObject(); }

        var download = root["download"] as JsonObject ?? new JsonObject();
        download["default_directory"] = Folder;
        download["prompt_for_download"] = false;
        download["directory_upgrade"] = true;
        root["download"] = download;

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(), new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void StartWatching()
    {
        if (_watcher is not null) return;
        Directory.CreateDirectory(Folder);
        _watcher = new FileSystemWatcher(Folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = false };
        _watcher.Created += (_, e) => Consider(e.FullPath);
        _watcher.Renamed += (_, e) => Consider(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Deletes buffered files not touched for <paramref name="age"/>; the buffer is a stopover, not storage.</summary>
    public int PurgeOlderThan(TimeSpan age, IEnumerable<string>? keep = null)
    {
        if (age < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(age));
        lock (_gate)
        {
            var kept = (keep ?? []).Select(Path.GetFullPath).ToArray();
            return CleanupEntries(kept).Count(entry => DateTime.UtcNow - entry.Written >= age && RemoveCleanupEntry(entry, kept));
        }
    }

    /// <summary>Removes what is older than <paramref name="age"/>, then the oldest entries until the buffer weighs at most <paramref name="maxBytes"/>.</summary>
    public int Maintain(TimeSpan age, long maxBytes, IEnumerable<string>? keep = null)
    {
        if (age < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(age));
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var kept = (keep ?? []).ToArray();
        lock (_gate) return PurgeOlderThan(age, kept) + TrimToSize(maxBytes, kept);
    }

    /// <summary>
    /// Deletes the oldest files and opened-ZIP folders until the buffer weighs at most <paramref name="maxBytes"/>. What is still being
    /// downloaded (<c>.crdownload</c>, <c>.tmp</c>) and the <paramref name="keep"/> paths (files being offered to the user) are never removed.
    /// </summary>
    public int TrimToSize(long maxBytes, IEnumerable<string>? keep = null)
    {
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        lock (_gate)
        {
            var kept = (keep ?? []).Select(Path.GetFullPath).ToArray();
            var entries = CleanupEntries(kept);
            var total = entries.Sum(entry => entry.Size);
            var removed = 0;
            foreach (var entry in entries.OrderBy(entry => entry.Written))
            {
                if (total <= maxBytes) break;
                if (!RemoveCleanupEntry(entry, kept)) continue;
                total -= entry.Size; removed++;
            }
            return removed;
        }
    }

    private sealed record CleanupEntry(string Path, bool IsDirectory, long Size, DateTime Written, bool Protected);

    private List<CleanupEntry> CleanupEntries(string[] kept)
    {
        _offered.RemoveWhere(path => !File.Exists(path)); // Files moved out of the buffer no longer retain an archive folder.
        kept = [.. kept, .. _processing, .. _offered];
        var entries = new List<CleanupEntry>();
        try
        {
            WorkspacePathSafety.EnsureNoLinks(Folder);
            if (!Directory.Exists(Folder)) return entries;
            foreach (var path in Directory.EnumerateFileSystemEntries(Folder)) entries.Add(InspectCleanupEntry(path, kept));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return entries;
    }

    private static bool Below(string path, string parent) => path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static CleanupEntry InspectCleanupEntry(string path, string[] kept)
    {
        var protectedEntry = kept.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase) || Below(item, path) || Below(path, item));
        var directory = false; long size = 0; var written = DateTime.MaxValue;
        try
        {
            var attributes = File.GetAttributes(path);
            directory = attributes.HasFlag(FileAttributes.Directory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) return new(path, directory, 0, written, true);
            written = File.GetLastWriteTimeUtc(path);
            var pending = new Stack<string>(); pending.Push(path);
            while (pending.TryPop(out var current))
            {
                var flags = File.GetAttributes(current);
                if (flags.HasFlag(FileAttributes.ReparsePoint)) { protectedEntry = true; continue; } // Never follow a link, even for size counting.
                if (current.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || current.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) protectedEntry = true;
                if (flags.HasFlag(FileAttributes.Directory))
                    foreach (var child in Directory.EnumerateFileSystemEntries(current)) pending.Push(child);
                else
                {
                    size = checked(size + new FileInfo(current).Length);
                    // A model can retain its final extension while another process is still writing it.
                    using var probe = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.None);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException) { protectedEntry = true; }
        return new(path, directory, size, written, protectedEntry);
    }

    private bool RemoveCleanupEntry(CleanupEntry entry, string[] kept)
    {
        if (entry.Protected) return false;
        try
        {
            var absolute = Path.GetFullPath(entry.Path);
            if (!Below(absolute, Folder)) return false;
            WorkspacePathSafety.EnsureNoLinks(absolute);
            if (InspectCleanupEntry(absolute, kept).Protected) return false;
            if (entry.IsDirectory) Directory.Delete(absolute, true); else File.Delete(absolute);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Moves 3D files out of the buffer into <paramref name="folder"/> (the user keeps them), never replacing a file already there;
    /// returns the new paths. Anything that is not a model, or cannot be moved, stays where it is.
    /// </summary>
    public static IReadOnlyList<string> MoveModels(IEnumerable<string> paths, string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Le dossier de destination n’existe plus.");
        var moved = new List<string>();
        foreach (var path in paths)
        {
            if (!IsModelFile(path) || !File.Exists(path)) continue;
            var destination = DownloadArchive.UniquePath(folder, System.IO.Path.GetFileName(path));
            try { File.Move(path, destination); moved.Add(destination); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return moved;
    }

    private void Consider(string path)
    {
        if (!IsModelFile(path) && !IsArchive(path)) return;
        lock (_gate) { if (!_processing.Add(path)) return; }
        _ = Task.Run(async () =>
        {
            try { await AnnounceWhenStable(path); }
            finally { lock (_gate) _processing.Remove(path); }
        });
    }

    private async Task AnnounceWhenStable(string path)
    {
        var started = DateTime.UtcNow;
        long previous = -1;
        while (DateTime.UtcNow - started < GiveUpAfter)
        {
            try
            {
                if (!File.Exists(path)) return;
                var length = new FileInfo(path).Length;
                if (length > 0 && length == previous)
                {
                    // a browser still writing keeps the file open: opening it exclusively proves it is finished
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    lock (_gate)
                    {
                        if (!_announced.Add(path + "|" + length + "|" + File.GetLastWriteTimeUtc(path).Ticks)) return;
                        if (!IsArchive(path)) _offered.Add(path);
                    }
                    if (IsArchive(path)) OpenArchive(path);
                    else FileReady?.Invoke(new BufferedFile(path, length));
                    return;
                }
                previous = length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            await Task.Delay(StableWait);
        }
    }

    /// <summary>Takes the 3D files out of the ZIP into their own folder, deletes the ZIP and announces the result.</summary>
    private void OpenArchive(string zipPath)
    {
        var folder = Path.Combine(Folder, Path.GetFileNameWithoutExtension(zipPath) + "-" + Guid.NewGuid().ToString("N")[..6]);
        lock (_gate) _processing.Add(folder);
        try
        {
            var content = DownloadArchive.ExtractModels(zipPath, folder);
            lock (_gate) foreach (var file in content.Files) _offered.Add(file.Path);
            if (content.Files.Count == 0)
            {
                try { Directory.Delete(folder, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            try { File.Delete(zipPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            BatchReady?.Invoke(new BufferedBatch(Path.GetFileName(zipPath), content.Files, content.Problem));
        }
        finally { lock (_gate) _processing.Remove(folder); }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
