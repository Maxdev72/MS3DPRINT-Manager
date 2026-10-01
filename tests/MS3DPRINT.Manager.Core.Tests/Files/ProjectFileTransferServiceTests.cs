using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Storage;
using System.Security.AccessControl;
using System.Security.Principal;

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
    public void Move_LockedSourceReportsOperationPathsAndNativeErrorAndKeepsSource()
    {
        File.WriteAllText(_sourcePath, "source");
        Directory.CreateDirectory(Path.Combine(_projectPath, "01_DEVIS_FACTURES"));
        using var locked = new FileStream(_sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Assert.ThrowsAny<IOException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));

        Assert.Equal("ProjectFileTransferException", error.GetType().Name);
        var diagnostic = Assert.IsType<ProjectFileTransferException>(error);
        Assert.Equal("File.Move", diagnostic.Operation);
        Assert.Equal(_sourcePath, diagnostic.SourcePath);
        Assert.Equal(32, diagnostic.Win32ErrorCode);
        Assert.Equal(diagnostic.InnerException!.HResult, diagnostic.HResult);
        Assert.Contains(_sourcePath, error.Message);
        Assert.Contains(Path.Combine(_projectPath, "01_DEVIS_FACTURES", _destination.FileName), error.Message);
        Assert.Contains("File.Move", error.Message);
        Assert.Contains("0x80070020", error.Message);
        Assert.True(File.Exists(_sourcePath));
        Assert.False(File.Exists(Path.Combine(_projectPath, "01_DEVIS_FACTURES", _destination.FileName)));
    }

    [Fact]
    public void Move_DestinationWriteDeniedReportsNativeErrorAndKeepsSource()
    {
        File.WriteAllText(_sourcePath, "source");
        var directory = new DirectoryInfo(Path.Combine(_projectPath, "01_DEVIS_FACTURES"));
        directory.Create();
        var original = directory.GetAccessControl();
        var denied = directory.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            var error = Assert.Throws<ProjectFileTransferException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));
            Assert.Equal("File.Move", error.Operation);
            Assert.Equal(5, error.Win32ErrorCode);
            Assert.Equal(Path.Combine(directory.FullName, _destination.FileName), error.DestinationPath);
            Assert.Equal("source", File.ReadAllText(_sourcePath));
            Assert.False(File.Exists(error.DestinationPath));
        }
        finally
        {
            directory.SetAccessControl(original);
        }
    }

    [Fact]
    public void Move_MissingSourceReportsSourceInspectionAndNativeCause()
    {
        Directory.CreateDirectory(Path.Combine(_projectPath, "01_DEVIS_FACTURES"));
        var error = Assert.Throws<ProjectFileTransferException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));
        Assert.Equal("File.GetAttributes (source)", error.Operation);
        Assert.Equal(_sourcePath, error.SourcePath);
        Assert.Equal(_sourcePath, error.FailedPath);
        Assert.Equal(2, error.Win32ErrorCode);
        Assert.IsType<FileNotFoundException>(error.InnerException);
        Assert.False(File.Exists(error.DestinationPath));
    }

    [Theory]
    [InlineData(false, "File.GetAttributes (project)")]
    [InlineData(true, "File.GetAttributes (destination directory)")]
    public void Move_MissingDirectoryReportsInspectionAndNativeCauseAndKeepsSource(bool createProject, string operation)
    {
        File.WriteAllText(_sourcePath, "source");
        if (createProject) Directory.CreateDirectory(_projectPath);
        var error = Assert.Throws<ProjectFileTransferException>(() => new ProjectFileTransferService().Move(_sourcePath, _projectPath, _destination));
        Assert.Equal(operation, error.Operation);
        Assert.Equal(createProject ? Path.Combine(_projectPath, "01_DEVIS_FACTURES") : _projectPath, error.FailedPath);
        Assert.Contains(createProject ? Path.Combine(_projectPath, "01_DEVIS_FACTURES") : _projectPath, error.Message);
        Assert.Equal(error.InnerException!.HResult, error.HResult);
        Assert.True(error.Win32ErrorCode is 2 or 3);
        Assert.Equal("source", File.ReadAllText(_sourcePath));
        Assert.False(File.Exists(error.DestinationPath));
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
