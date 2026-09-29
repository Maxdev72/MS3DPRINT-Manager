using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ProjectFileTransferServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-transfer-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _projectPath;
    private readonly string _sourcePath;
    private readonly ProjectFileDestination _destination = new(ProjectFileCategory.Documents, "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf");

    public ProjectFileTransferServiceTests()
    {
        _projectPath = Path.Combine(_root, "MPO-2026-001_OUTILLAGE");
        _sourcePath = Path.Combine(_root, "Téléchargements", "DEV2026-05.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourcePath)!);
    }

    [Fact]
    public void Move_MovesFileAndRemovesTheSource()
    {
        File.WriteAllText(_sourcePath, "indy document");
        Directory.CreateDirectory(Path.Combine(_projectPath, "01_DEVIS_FACTURES"));

        var result = new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination);

        Assert.False(File.Exists(_sourcePath));
        Assert.Equal("indy document", File.ReadAllText(result));
        Assert.Equal(Path.Combine(_projectPath, "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf"), result);
    }

    [Fact]
    public void Move_RejectsExistingDestinationAndKeepsSource()
    {
        File.WriteAllText(_sourcePath, "source");
        var target = Path.Combine(_projectPath, "01_DEVIS_FACTURES", _destination.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "existing");

        Assert.Throws<FolderConflictException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));

        Assert.True(File.Exists(_sourcePath));
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public void Move_RejectsDestinationsOutsideTheProjectAndKeepsSource()
    {
        File.WriteAllText(_sourcePath, "source");
        var outside = new ProjectFileDestination(ProjectFileCategory.Documents, "..\\outside", _destination.FileName);

        Assert.Throws<ArgumentException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, outside));

        Assert.True(File.Exists(_sourcePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
