using System.IO.Compression;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Storage;

public sealed class WorkspaceBackupTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "ms3d-backup-tests-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_temp, "workspace");
    private string Archive => Path.Combine(_temp, "backup.zip");
    public WorkspaceBackupTests()
    {
        Directory.CreateDirectory(Root);
        Write(".ms3dprint-manager/clients/client.json", "client");
        Write(".ms3dprint-manager/estimate-workshop-settings.json", "settings");
        Write("01_CLIENTS/CLIENT/PROJET/piece.stl", "piece");
        Directory.CreateDirectory(Path.Combine(Root, "01_CLIENTS/CLIENT/PROJET/vide"));
        Write("98_DEVELOPPEMENT/source.cs", "excluded");
    }
    private void Write(string relative, string text)
    { var path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Backup_restores_metadata_and_tree_with_optional_documents(bool documents)
    {
        var service = new WorkspaceBackupService();
        var result = service.Create(Root, Archive, documents);
        var preview = service.Inspect(Archive);
        Assert.Equal(documents, preview.IncludesDocuments);
        Assert.Equal(result.FileCount, preview.FileCount);
        Assert.True(preview.TotalBytes > 0);
        var restored = Path.Combine(_temp, "restored");
        service.Restore(Archive, restored, preview.ArchiveSha256);
        Assert.Equal("client", File.ReadAllText(Path.Combine(restored, ".ms3dprint-manager/clients/client.json")));
        Assert.True(Directory.Exists(Path.Combine(restored, "01_CLIENTS/CLIENT/PROJET/vide")));
        Assert.Equal(documents, File.Exists(Path.Combine(restored, "01_CLIENTS/CLIENT/PROJET/piece.stl")));
        Assert.False(Directory.Exists(Path.Combine(restored, "98_DEVELOPPEMENT")));
        Assert.Equal("settings", File.ReadAllText(Path.Combine(restored, ".ms3dprint-manager/estimate-workshop-settings.json")));
    }

    [Fact]
    public void Existing_archive_and_restore_destination_are_never_overwritten()
    {
        var service = new WorkspaceBackupService();
        var preview = service.Create(Root, Archive, true);
        var original = File.ReadAllBytes(Archive);
        Assert.Throws<IOException>(() => service.Create(Root, Archive, false));
        Assert.Equal(original, File.ReadAllBytes(Archive));
        Assert.Throws<IOException>(() => service.Restore(Archive, Root, preview.ArchiveSha256));
        Assert.Equal("piece", File.ReadAllText(Path.Combine(Root, "01_CLIENTS/CLIENT/PROJET/piece.stl")));
    }

    [Fact]
    public void Archive_cannot_be_created_inside_workspace()
    {
        Assert.Throws<IOException>(() => new WorkspaceBackupService().Create(Root, Path.Combine(Root, "backup.zip"), true));
        Assert.False(File.Exists(Path.Combine(Root, "backup.zip")));
    }

    [Theory]
    [InlineData("workspace/../escape.txt")]
    [InlineData("workspace/01_CLIENTS/CON.txt")]
    [InlineData("workspace/01_CLIENTS/file:stream")]
    [InlineData("workspace/01_CLIENTS/file. ")]
    [InlineData("workspace/01_CLIENTS/file.txt")]
    public void Unlisted_or_unsafe_entries_are_rejected(string entryName)
    {
        var service = new WorkspaceBackupService(); service.Create(Root, Archive, true);
        using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write("injected");
        Assert.Throws<InvalidDataException>(() => service.Inspect(Archive));
    }

    [Fact]
    public void Damaged_content_is_rejected_and_preview_cannot_be_reused_for_another_archive()
    {
        var service = new WorkspaceBackupService(); var preview = service.Create(Root, Archive, true);
        using (var zip = ZipFile.Open(Archive, ZipArchiveMode.Update))
        {
            zip.GetEntry("workspace/.ms3dprint-manager/clients/client.json")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("workspace/.ms3dprint-manager/clients/client.json").Open()); writer.Write("broken");
        }
        Assert.Throws<InvalidDataException>(() => service.Inspect(Archive));
        var destination = Path.Combine(_temp, "restored");
        Assert.Throws<InvalidDataException>(() => service.Restore(Archive, destination, preview.ArchiveSha256));
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(_temp, ".ms3d-restore-*"));
    }

    [Fact]
    public void Business_registry_is_included_but_workstation_settings_and_trash_are_excluded_without_documents()
    {
        Write("data/client-codes.json", "codes");
        Write("data/client-codes.previous-123.json", "old codes");
        Write("data/workspace.json", "workstation");
        Write("data/performance.json", "workstation");
        Write(".ms3dprint-manager/trash/item/0.stl", "deleted document");
        Write(".ms3dprint-manager/trash/item/journal.json", "deleted record");
        var service = new WorkspaceBackupService(); var preview = service.Create(Root, Archive, false);
        var destination = Path.Combine(_temp, "restored"); service.Restore(Archive, destination, preview.ArchiveSha256);
        Assert.Equal("codes", File.ReadAllText(Path.Combine(destination, "data/client-codes.json")));
        Assert.True(File.Exists(Path.Combine(destination, "data/client-codes.previous-123.json")));
        Assert.False(File.Exists(Path.Combine(destination, "data/workspace.json")));
        Assert.False(File.Exists(Path.Combine(destination, "data/performance.json")));
        Assert.False(Directory.Exists(Path.Combine(destination, ".ms3dprint-manager/trash/item")));
    }

    [Fact]
    public void Full_backup_preserves_recoverable_trash_and_refuses_pending_mutation_journals()
    {
        Write(".ms3dprint-manager/trash/item/payload.stl", "deleted document");
        var service = new WorkspaceBackupService(); var preview = service.Create(Root, Archive, true);
        var destination = Path.Combine(_temp, "restored"); service.Restore(Archive, destination, preview.ArchiveSha256);
        Assert.Equal("deleted document", File.ReadAllText(Path.Combine(destination, ".ms3dprint-manager/trash/item/payload.stl")));
        Write(".ms3dprint-manager/operations/pending/journal.json", "pending");
        Assert.Throws<IOException>(() => service.Create(Root, Path.Combine(_temp, "pending.zip"), true));
    }

    [Fact]
    public void A_valid_replacement_archive_invalidates_the_previous_preview()
    {
        var service = new WorkspaceBackupService(); var preview = service.Create(Root, Archive, true);
        Write(".ms3dprint-manager/clients/client.json", "changed");
        var replacement = Path.Combine(_temp, "replacement.zip"); service.Create(Root, replacement, true);
        File.Copy(replacement, Archive, overwrite: true);
        Assert.Throws<InvalidDataException>(() => service.Restore(Archive, Path.Combine(_temp, "restored"), preview.ArchiveSha256));
        Assert.False(Directory.Exists(Path.Combine(_temp, "restored")));
    }

    [Fact]
    public void An_unreadable_file_does_not_publish_a_partial_archive()
    {
        using var locked = new FileStream(Path.Combine(Root, "01_CLIENTS/CLIENT/PROJET/piece.stl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => new WorkspaceBackupService().Create(Root, Archive, true));
        Assert.False(File.Exists(Archive));
        Assert.Empty(Directory.GetFiles(_temp, "backup.zip.*.tmp"));
    }

    [Fact]
    public void Missing_workspace_fails_without_publishing_an_archive()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new WorkspaceBackupService().Create(Path.Combine(_temp, "missing"), Archive, true));
        Assert.False(File.Exists(Archive));
    }

    public void Dispose() => Directory.Delete(_temp, true);
}
