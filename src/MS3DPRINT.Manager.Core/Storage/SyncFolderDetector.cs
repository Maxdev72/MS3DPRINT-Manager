using System.Text.Json;
using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Storage;

/// <summary>A folder kept in step by the desktop client of a storage service.</summary>
public sealed record SyncFolder(StorageProviderKind Kind, string Path);

/// <summary>
/// Finds the folders that the Nextcloud, Google Drive and Dropbox desktop clients keep synchronised on this computer. The software
/// does not talk to these services itself: it works in an ordinary folder, and their clients do the synchronising.
/// </summary>
public sealed partial class SyncFolderDetector
{
    /// <summary>Name of the workspace folder proposed inside a synchronised folder.</summary>
    public const string WorkspaceFolderName = "MS3DPRINT";

    private readonly string _userProfile;
    private readonly string? _appData;
    private readonly string? _localAppData;
    private readonly IReadOnlyList<string> _driveRoots;

    public SyncFolderDetector(string userProfile, string? appData = null, string? localAppData = null, IEnumerable<string>? driveRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
        _userProfile = userProfile;
        _appData = appData;
        _localAppData = localAppData;
        _driveRoots = (driveRoots ?? []).ToArray();
    }

    /// <summary>The detector of this computer.</summary>
    public static SyncFolderDetector ForThisComputer()
    {
        var drives = new List<string>();
        try
        {
            drives.AddRange(DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.RootDirectory.FullName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return new SyncFolderDetector(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), drives);
    }

    /// <summary>The synchronised folders that exist, Nextcloud first, then Google Drive and Dropbox; a folder is listed once.</summary>
    public IReadOnlyList<SyncFolder> Detect()
    {
        var found = new List<SyncFolder>();
        void Add(StorageProviderKind kind, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                if (!Directory.Exists(full)) return;
                if (found.Any(known => string.Equals(known.Path, full, StringComparison.OrdinalIgnoreCase))) return;
                found.Add(new SyncFolder(kind, full));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
        }

        foreach (var path in NextcloudFolders()) Add(StorageProviderKind.Nextcloud, path);
        foreach (var path in GoogleDriveFolders()) Add(StorageProviderKind.GoogleDrive, path);
        foreach (var path in DropboxFolders()) Add(StorageProviderKind.Dropbox, path);
        return found;
    }

    /// <summary>Where the workspace would sit inside a synchronised folder.</summary>
    public static string WorkspaceIn(SyncFolder folder) => System.IO.Path.Combine(folder.Path, WorkspaceFolderName);

    private IEnumerable<string> NextcloudFolders()
    {
        // the folders the client really synchronises, as it records them
        if (_appData is not null && ReadText(System.IO.Path.Combine(_appData, "Nextcloud", "nextcloud.cfg")) is { } config)
            foreach (Match match in LocalPathLine().Matches(config))
                yield return match.Groups[1].Value.Trim();
        yield return System.IO.Path.Combine(_userProfile, "Nextcloud");
        yield return System.IO.Path.Combine(_userProfile, "Documents", "Nextcloud");
    }

    private IEnumerable<string> GoogleDriveFolders()
    {
        // "My Drive" is called "Mon Drive" on a French system; Drive for desktop mounts it on a drive letter or in the user folder
        string[] names = ["My Drive", "Mon Drive"];
        foreach (var name in names)
            yield return System.IO.Path.Combine(_userProfile, name);
        yield return System.IO.Path.Combine(_userProfile, "Google Drive");
        foreach (var name in names)
            yield return System.IO.Path.Combine(_userProfile, "Google Drive", name);
        foreach (var root in _driveRoots)
            foreach (var name in names)
                yield return System.IO.Path.Combine(root, name);
    }

    private IEnumerable<string> DropboxFolders()
    {
        if (_localAppData is not null && ReadText(System.IO.Path.Combine(_localAppData, "Dropbox", "info.json")) is { } info)
        {
            string?[] paths;
            try
            {
                using var document = JsonDocument.Parse(info);
                paths = document.RootElement.ValueKind == JsonValueKind.Object
                    ? document.RootElement.EnumerateObject()
                        .Where(account => account.Value.ValueKind == JsonValueKind.Object && account.Value.TryGetProperty("path", out _))
                        .Select(account => account.Value.GetProperty("path").GetString()).ToArray()
                    : [];
            }
            catch (JsonException) { paths = []; }
            foreach (var path in paths) yield return path ?? string.Empty;
        }
        yield return System.IO.Path.Combine(_userProfile, "Dropbox");
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    [GeneratedRegex(@"^\d+\\Folders\\\d+\\localPath=(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex LocalPathLine();
}
