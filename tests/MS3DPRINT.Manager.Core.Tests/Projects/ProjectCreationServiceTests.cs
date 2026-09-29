using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public sealed class ProjectCreationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_RecomputesSequenceInsideCreationWhenPreviewsAreStale()
    {
        var clientPath = Path.Combine(_root, "CLIENT");
        Directory.CreateDirectory(clientPath);
        var stalePreviewOne = ProjectReferenceGenerator.Create("MPO", 2026, [], "Premier");
        var stalePreviewTwo = ProjectReferenceGenerator.Create("MPO", 2026, [], "Deuxieme");
        Assert.Equal(1, stalePreviewOne.Sequence);
        Assert.Equal(1, stalePreviewTwo.Sequence);
        var service = new ProjectCreationService(new FolderTreeService());

        var first = service.Create(clientPath, "MPO", 2026, "Premier");
        var second = service.Create(clientPath, "MPO", 2026, "Deuxieme");

        Assert.Equal("MPO-2026-001_PREMIER", first.FolderName);
        Assert.Equal("MPO-2026-002_DEUXIEME", second.FolderName);
        Assert.True(Directory.Exists(Path.Combine(clientPath, first.FolderName)));
        Assert.True(Directory.Exists(Path.Combine(clientPath, second.FolderName)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
