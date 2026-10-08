using System.Text.Json;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Workspace;

public sealed class CaseRenameJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-case-journal-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovery_InterruptedIntermediateRenameRestoresOriginalForFilesAndDirectories(bool directory)
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "02_MODELES_3D")).FullName;
        var source = Path.Combine(parent, directory ? "MODEL" : "MODEL.txt");
        if (directory) { Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "document.txt"), "document"); }
        else File.WriteAllText(source, "document");
        var intermediate = Path.Combine(parent, ".rename-" + Guid.NewGuid().ToString("N"));
        WriteRenameJournal(source, Path.Combine(parent, Path.GetFileName(source).ToLowerInvariant()), intermediate);
        if (directory) Directory.Move(source, intermediate); else File.Move(source, intermediate);
        new EntityManagementService(_root).RecoverPendingOperations();
        Assert.Equal("document", File.ReadAllText(directory ? Path.Combine(source, "document.txt") : source));
        Assert.Contains(Directory.EnumerateFileSystemEntries(parent), entry => Path.GetFileName(entry) == Path.GetFileName(source));
        Assert.False(File.Exists(intermediate) || Directory.Exists(intermediate));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, ".ms3dprint-manager", "renames"), "*.json"));
    }

    [Fact]
    public void Recovery_EntityRenameRestoresIntermediateBeforeProfileRollback()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "ACME")).FullName;
        File.WriteAllText(Path.Combine(source, "document.txt"), "document");
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        new ClientProfileStore(new(_root)).Create(new(id, ClientKind.Professional, "ACME", "ACM", "ACME", null, null, null, null, new(null, null, null, null, null), now, now));
        var profile = Path.Combine(_root, ".ms3dprint-manager", "clients", id + ".json"); var before = File.ReadAllBytes(profile);
        var operation = Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager", "operations", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllBytes(Path.Combine(operation, "0.json"), before);
        var target = Path.Combine(Path.GetDirectoryName(source)!, "acme");
        File.WriteAllText(Path.Combine(operation, "journal.json"), JsonSerializer.Serialize(new { Source = Path.GetRelativePath(_root, source), Target = Path.GetRelativePath(_root, target), Profiles = new[] { Path.GetRelativePath(_root, profile) }, Committed = false }));
        var intermediate = Path.Combine(Path.GetDirectoryName(source)!, ".rename-" + Guid.NewGuid().ToString("N"));
        WriteRenameJournal(source, target, intermediate); Directory.Move(source, intermediate);
        new EntityManagementService(_root).RecoverPendingOperations();
        Assert.Equal("document", File.ReadAllText(Path.Combine(source, "document.txt")));
        Assert.Equal(before, File.ReadAllBytes(profile)); Assert.False(Directory.Exists(operation));
    }

    private void WriteRenameJournal(string source, string target, string intermediate)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, ".ms3dprint-manager", "renames")).FullName;
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), JsonSerializer.Serialize(new { Source = Path.GetRelativePath(_root, source), Target = Path.GetRelativePath(_root, target), Intermediate = Path.GetRelativePath(_root, intermediate), Committed = false }));
    }

    [Fact]
    public void Recovery_AfterSecondMoveRestoresOriginalCasing()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "documents")).FullName;
        var source = Path.Combine(parent, "ORIGINAL.txt");
        var target = Path.Combine(parent, "original.txt");
        var intermediate = Path.Combine(parent, ".rename-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(target, "preserved");
        WriteRenameJournal(source, target, intermediate);
        new EntityManagementService(_root).RecoverPendingOperations();
        Assert.Equal("preserved", File.ReadAllText(source));
        Assert.Equal("ORIGINAL.txt", Path.GetFileName(Assert.Single(Directory.GetFiles(parent))));
    }

    [Fact]
    public void Recovery_InvalidIntermediateDoesNotMoveOrDeleteDocuments()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "documents")).FullName;
        var source = Path.Combine(parent, "ORIGINAL.txt");
        var unrelated = Path.Combine(parent, "unrelated.txt");
        File.WriteAllText(unrelated, "preserved");
        WriteRenameJournal(source, Path.Combine(parent, "original.txt"), unrelated);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.Equal("preserved", File.ReadAllText(unrelated));
        Assert.False(File.Exists(source));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, ".ms3dprint-manager", "renames"), "*.json"));
    }

    [Fact]
    public void Recovery_IntermediateAndOriginCollisionPreservesBoth()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "documents")).FullName;
        var source = Path.Combine(parent, "ORIGINAL.txt");
        var intermediate = Path.Combine(parent, ".rename-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(source, "new-document");
        File.WriteAllText(intermediate, "original-document");
        WriteRenameJournal(source, Path.Combine(parent, "original.txt"), intermediate);
        Assert.ThrowsAny<IOException>(() => new EntityManagementService(_root).RecoverPendingOperations());
        Assert.Equal("new-document", File.ReadAllText(source));
        Assert.Equal("original-document", File.ReadAllText(intermediate));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
