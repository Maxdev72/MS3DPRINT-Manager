using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class ManagedFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "managed-files-" + Guid.NewGuid().ToString("N"));
    public ManagedFileServiceTests() => Directory.CreateDirectory(_root);
    private string FileAt(string relative, string contents = "document")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void RenameAndMove_PreserveContentsAndCaseOnlyName()
    {
        var service = new ManagedFileService(_root);
        var source = FileAt("documents/test.txt");
        var renamed = service.Rename(source, "TEST.txt");
        Assert.Contains(Path.Combine(Path.GetDirectoryName(source)!, "TEST.txt"), Directory.GetFiles(Path.GetDirectoryName(source)!));
        var destination = service.CreateFolder(_root, "destination");
        var moved = service.Move(renamed, destination);
        Assert.Equal("document", File.ReadAllText(moved));
        Assert.False(File.Exists(renamed));
    }

    [Fact]
    public void Import_CopiesWithoutOverwriting()
    {
        var source = FileAt("source/test.txt");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var service = new ManagedFileService(_root);
        Assert.Equal("document", File.ReadAllText(service.Import(source, destination)));
        Assert.ThrowsAny<Exception>(() => service.Import(source, destination));
        Assert.Equal("document", File.ReadAllText(source));
        Assert.Single(Directory.GetFiles(destination));
    }

    [Fact]
    public void TrashAndRestore_PersistTogetherAndPreflightAllConflicts()
    {
        var service = new ManagedFileService(_root);
        var document = FileAt("01_CLIENTS/client/test.txt");
        var folder = Path.GetDirectoryName(document)!;
        var profile = FileAt(".ms3dprint-manager/clients/client.json", "profile");
        var entry = service.Trash(folder, [profile], "Client");
        Assert.False(Directory.Exists(folder));
        Assert.False(File.Exists(profile));
        service = new ManagedFileService(_root);
        Assert.Equal(entry, Assert.Single(service.ListTrash()));
        FileAt(".ms3dprint-manager/clients/client.json", "conflict");
        Assert.ThrowsAny<Exception>(() => service.Restore(entry.Id));
        Assert.False(Directory.Exists(folder));
        File.Delete(profile);
        service.Restore(entry.Id);
        Assert.Equal("document", File.ReadAllText(document));
        Assert.Equal("profile", File.ReadAllText(profile));
        Assert.Empty(service.ListTrash());
    }

    [Fact]
    public void Trash_PreflightsMissingCompanionAndAllowsFilamentJson()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("document.txt");
        Assert.ThrowsAny<Exception>(() => service.Trash(file, [Path.Combine(_root, "missing.json")]));
        Assert.True(File.Exists(file));
        var filament = FileAt(".ms3dprint-manager/filaments/f.json");
        var entry = service.Trash(filament);
        service.Restore(entry.Id);
        Assert.True(File.Exists(filament));
    }

    [Fact]
    public void Trash_LockedCompanionRollsBackDocuments()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("documents/test.txt");
        var profile = FileAt(".ms3dprint-manager/clients/a.json");
        using var locked = new FileStream(profile, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => service.Trash(Path.GetDirectoryName(file)!, [profile]));
        Assert.Equal("document", File.ReadAllText(file));
        Assert.Empty(service.ListTrash());
    }

    [Fact]
    public void Restore_ResumesInterruptedRestoreFromPersistentManifest()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("documents/test.txt");
        var profile = FileAt(".ms3dprint-manager/clients/a.json", "profile");
        var entry = service.Trash(Path.GetDirectoryName(file)!, [profile]);
        var transaction = Path.Combine(_root, ".ms3dprint-manager", "trash", entry.Id.ToString("N"));
        var manifestPath = Path.Combine(transaction, "manifest.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["State"] = "Restoring";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Directory.Move(Path.Combine(transaction, "0"), Path.GetDirectoryName(file)!);
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal("document", File.ReadAllText(file));
        Assert.Equal("profile", File.ReadAllText(profile));
        Assert.Empty(service.ListTrash());
    }

    [Fact]
    public void Restore_ParentFileConflictDoesNotChangeJournalOrRestoreAnyPayload()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("documents/test.txt");
        var profile = FileAt(".ms3dprint-manager/clients/a.json");
        var entry = service.Trash(Path.GetDirectoryName(file)!, [profile]);
        Directory.Delete(Path.GetDirectoryName(profile)!);
        FileAt(".ms3dprint-manager/clients", "blocking parent");
        var manifest = Path.Combine(_root, ".ms3dprint-manager", "trash", entry.Id.ToString("N"), "manifest.json");
        var before = File.ReadAllText(manifest);
        Assert.ThrowsAny<IOException>(() => service.Restore(entry.Id));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)!));
        Assert.Equal(before, File.ReadAllText(manifest));
    }

    [Fact]
    public void Restore_CleanupFailureDoesNotRollBackCommittedRestoration()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("documents/test.txt");
        var entry = service.Trash(Path.GetDirectoryName(file)!);
        var transaction = Path.Combine(_root, ".ms3dprint-manager", "trash", entry.Id.ToString("N"));
        File.WriteAllText(Path.Combine(transaction, "unexpected.txt"), "preserve me");
        Assert.ThrowsAny<IOException>(() => service.Restore(entry.Id));
        Assert.Equal("document", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(transaction, "manifest.json")));
        Assert.Equal("preserve me", File.ReadAllText(Path.Combine(transaction, "unexpected.txt")));
    }

    [Fact]
    public void Import_LockedSourceLeavesNoPartialDestination()
    {
        var source = FileAt("source/test.txt");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.ThrowsAny<IOException>(() => new ManagedFileService(_root).Import(source, destination));
        Assert.Empty(Directory.GetFiles(destination));
    }

    [Fact]
    public void Operations_RejectCollisionsOutsidePathsMetadataAndStructuralFolders()
    {
        var service = new ManagedFileService(_root);
        var file = FileAt("documents/test.txt");
        Directory.CreateDirectory(Path.Combine(_root, "documents", "collision"));
        Assert.ThrowsAny<Exception>(() => service.Rename(file, "collision"));
        Assert.ThrowsAny<Exception>(() => service.Move(file, Path.GetTempPath()));
        Assert.ThrowsAny<Exception>(() => service.CreateFolder(Path.Combine(_root, ".."), "outside"));
        var profile = FileAt(".ms3dprint-manager/clients/a.json");
        Assert.ThrowsAny<Exception>(() => service.Rename(profile, "b.json"));
        var structural = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS")).FullName;
        Assert.ThrowsAny<Exception>(() => service.Trash(structural));
        Assert.ThrowsAny<Exception>(() => service.Rename(_root, "renamed"));
        Assert.ThrowsAny<Exception>(() => service.Move(Path.GetDirectoryName(file)!, Path.GetDirectoryName(file)!));
        Assert.ThrowsAny<Exception>(() => service.CreateFolder(_root, "CON.txt"));
        Assert.ThrowsAny<Exception>(() => service.CreateFolder(_root, "../bad"));
    }

    [Fact]
    public void Operations_RejectReparsePointsAndLinkedSubtree()
    {
        var service = new ManagedFileService(_root);
        var source = FileAt("documents/file.txt");
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var link = Path.Combine(_root, "documents", "link");
        // Junctions need no developer-mode privilege on Windows.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false });
        process!.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.ThrowsAny<Exception>(() => service.Move(source, link));
        Assert.ThrowsAny<Exception>(() => service.Trash(Path.GetDirectoryName(source)!));
        Directory.Delete(link);
    }

    [Fact]
    public void WorkspacePathSafety_AllowsRepositoryCloudAncestorsWithoutWriting()
    {
        // Test binaries in the Nextcloud checkout have cloud-tagged ancestors on this host.
        // This is a read-only validation; all mutation tests use isolated temporary directories.
        WorkspacePathSafety.EnsureNoLinks(typeof(ManagedFileServiceTests).Assembly.Location);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null-items")]
    public void ListTrash_CorruptManifestDoesNotHideHealthyEntryOrDeletePayload(string corruptJson)
    {
        var service = new ManagedFileService(_root);
        var healthyFile = FileAt("healthy.txt");
        var corruptFile = FileAt("corrupt.txt", "keep corrupt payload");
        var healthy = service.Trash(healthyFile);
        var corrupt = service.Trash(corruptFile);
        var transaction = Path.Combine(_root, ".ms3dprint-manager", "trash", corrupt.Id.ToString("N"));
        if (corruptJson == "null-items")
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(transaction, "manifest.json")))!;
            json["Items"] = null;
            corruptJson = json.ToJsonString();
        }
        File.WriteAllText(Path.Combine(transaction, "manifest.json"), corruptJson);
        Assert.Equal(healthy, Assert.Single(service.ListTrash()));
        Assert.Single(service.TrashReadErrors);
        Assert.Contains(corrupt.Id.ToString("N"), service.TrashReadErrors[0]);
        service.Restore(healthy.Id);
        Assert.Equal("document", File.ReadAllText(healthyFile));
        Assert.Equal("keep corrupt payload", File.ReadAllText(Path.Combine(transaction, "0")));
        Assert.Empty(service.ListTrash());
        Assert.Single(service.TrashReadErrors);
    }

    [Fact]
    public void ListTrash_RejectsLinkedTransactionWithoutHidingHealthyEntry()
    {
        var service = new ManagedFileService(_root);
        var entry = service.Trash(FileAt("healthy.txt"));
        var targetFile = FileAt("link-target/untouched.txt", "preserve me");
        var id = Guid.NewGuid();
        var link = Path.Combine(_root, ".ms3dprint-manager", "trash", id.ToString("N"));
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Path.GetDirectoryName(targetFile)}\"") { CreateNoWindow = true, UseShellExecute = false });
        process!.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Equal(entry, Assert.Single(service.ListTrash()));
            Assert.Single(service.TrashReadErrors);
            Assert.Contains(id.ToString("N"), service.TrashReadErrors[0]);
            Assert.Equal("preserve me", File.ReadAllText(targetFile));
        }
        finally { Directory.Delete(link); }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
