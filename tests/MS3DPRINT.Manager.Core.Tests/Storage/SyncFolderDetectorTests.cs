using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class SyncFolderDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-sync-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "user");
    private string AppData => Path.Combine(_root, "appdata");
    private string LocalAppData => Path.Combine(_root, "local");

    public SyncFolderDetectorTests() => Directory.CreateDirectory(Profile);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private SyncFolderDetector Detector(params string[] drives) => new(Profile, AppData, LocalAppData, drives);

    private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

    [Fact]
    public void An_empty_profile_has_nothing()
        => Assert.Empty(Detector().Detect());

    [Fact]
    public void The_usual_folders_are_found()
    {
        var nextcloud = Folder("user", "Nextcloud");
        var dropbox = Folder("user", "Dropbox");
        var drive = Folder("user", "My Drive");

        var found = Detector().Detect();

        Assert.Equal(
        [
            new SyncFolder(StorageProviderKind.Nextcloud, nextcloud),
            new SyncFolder(StorageProviderKind.GoogleDrive, drive),
            new SyncFolder(StorageProviderKind.Dropbox, dropbox)
        ], found);
    }

    [Fact]
    public void Google_Drive_is_found_under_its_french_name_and_on_a_drive_letter()
    {
        var french = Folder("user", "Mon Drive");
        var mounted = Folder("G", "My Drive");

        var found = Detector(Path.Combine(_root, "G")).Detect();

        Assert.Equal([french, mounted], found.Where(folder => folder.Kind == StorageProviderKind.GoogleDrive).Select(folder => folder.Path));
    }

    [Fact]
    public void Nextcloud_folders_are_read_from_the_client_settings()
    {
        var custom = Folder("Elsewhere", "Cloud");
        Directory.CreateDirectory(Path.Combine(AppData, "Nextcloud"));
        File.WriteAllText(Path.Combine(AppData, "Nextcloud", "nextcloud.cfg"),
            "[General]\nclientVersion=3.0\n\n[Accounts]\n0\\Folders\\1\\localPath=" + custom.Replace('\\', '/') + "/\n0\\Folders\\1\\targetPath=/\n0\\Folders\\2\\localPath=" + Path.Combine(_root, "Gone").Replace('\\', '/') + "/\n");

        var found = Detector().Detect();

        Assert.Equal([new SyncFolder(StorageProviderKind.Nextcloud, custom)], found);
    }

    [Fact]
    public void Dropbox_folders_are_read_from_its_info_file()
    {
        var personal = Folder("Data", "Dropbox (Perso)");
        var business = Folder("Data", "Dropbox (Pro)");
        Directory.CreateDirectory(Path.Combine(LocalAppData, "Dropbox"));
        File.WriteAllText(Path.Combine(LocalAppData, "Dropbox", "info.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["personal"] = new { path = personal }, ["business"] = new { path = business } }));

        var found = Detector().Detect();

        Assert.Equal([personal, business], found.Select(folder => folder.Path));
        Assert.All(found, folder => Assert.Equal(StorageProviderKind.Dropbox, folder.Kind));
    }

    [Fact]
    public void A_damaged_dropbox_file_is_ignored()
    {
        Directory.CreateDirectory(Path.Combine(LocalAppData, "Dropbox"));
        File.WriteAllText(Path.Combine(LocalAppData, "Dropbox", "info.json"), "{ nope");
        var dropbox = Folder("user", "Dropbox");

        Assert.Equal([new SyncFolder(StorageProviderKind.Dropbox, dropbox)], Detector().Detect());
    }

    [Fact]
    public void A_folder_known_twice_is_listed_once()
    {
        var nextcloud = Folder("user", "Nextcloud");
        Directory.CreateDirectory(Path.Combine(AppData, "Nextcloud"));
        File.WriteAllText(Path.Combine(AppData, "Nextcloud", "nextcloud.cfg"), "0\\Folders\\1\\localPath=" + nextcloud.ToUpperInvariant().Replace('\\', '/') + "/\n");

        Assert.Single(Detector().Detect());
    }

    [Fact]
    public void The_workspace_sits_in_a_folder_named_after_the_software()
        => Assert.Equal(Path.Combine(@"C:\Cloud", "MS3DPRINT"), SyncFolderDetector.WorkspaceIn(new SyncFolder(StorageProviderKind.Nextcloud, @"C:\Cloud")));
}
