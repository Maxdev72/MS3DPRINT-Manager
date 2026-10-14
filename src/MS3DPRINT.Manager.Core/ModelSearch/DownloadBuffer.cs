using System.Text.Json;
using System.Text.Json.Nodes;

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
    public int PurgeOlderThan(TimeSpan age)
    {
        if (!Directory.Exists(Folder)) return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < age) continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        // the folders where ZIPs were opened
        foreach (var directory in Directory.EnumerateDirectories(Folder))
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < age) continue;
                Directory.Delete(directory, true);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }

    private void Consider(string path)
    {
        if (!IsModelFile(path) && !IsArchive(path)) return;
        _ = Task.Run(() => AnnounceWhenStable(path));
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
                    lock (_gate) { if (!_announced.Add(path + "|" + length + "|" + File.GetLastWriteTimeUtc(path).Ticks)) return; }
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
        var content = DownloadArchive.ExtractModels(zipPath, folder);
        if (content.Files.Count == 0)
        {
            try { Directory.Delete(folder, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        try { File.Delete(zipPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        BatchReady?.Invoke(new BufferedBatch(Path.GetFileName(zipPath), content.Files, content.Problem));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
