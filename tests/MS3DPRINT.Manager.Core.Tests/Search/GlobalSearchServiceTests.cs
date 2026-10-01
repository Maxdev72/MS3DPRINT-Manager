using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Search;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Search;

public sealed class GlobalSearchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-search-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Search_FindsClientProjectAndFileByNameWithoutExposingMetadata()
    {
        var clientPath = Path.Combine(_root, "01_CLIENTS", "MPO");
        var projectPath = Path.Combine(clientPath, "MPO-2026-001_OUTILLAGE");
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(Path.Combine(projectPath, "outillage.step"), "test");
        var paths = new WorkspaceMetadataPaths(_root);
        new ClientProfileStore(paths).Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "Outillage MPO", null, null, null, null,
            new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        File.WriteAllText(Path.Combine(paths.MetadataDirectory, "outillage-secret.tmp"), "secret");

        var result = CreateService(paths).Search(_root, "outillage");

        Assert.Contains(result.Results, item => item.Kind == GlobalSearchResultKind.Client && item.Title == "Outillage MPO");
        Assert.Contains(result.Results, item => item.Kind == GlobalSearchResultKind.Project && item.Title.Contains("OUTILLAGE"));
        Assert.Contains(result.Results, item => item.Kind == GlobalSearchResultKind.File && item.Title == "outillage.step");
        Assert.DoesNotContain(result.Results, item => item.Path.Contains(".ms3dprint-manager", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_LimitsResultsAndRejectsBlankQuery()
    {
        var folder = Path.Combine(_root, "02_MODELES_3D");
        Directory.CreateDirectory(folder);
        for (var index = 0; index < 5; index++) File.WriteAllText(Path.Combine(folder, $"piece-{index}.stl"), "test");
        var service = CreateService(new WorkspaceMetadataPaths(_root));

        Assert.Throws<ArgumentException>(() => service.Search(_root, " "));
        var result = service.Search(_root, "piece", maxResults: 2);
        Assert.Equal(2, result.Results.Count);
        Assert.True(result.HasMore);
    }

    [Fact]
    public void Search_FindsClientByPrimaryContactDetails()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO"));
        var paths = new WorkspaceMetadataPaths(_root);
        new ClientProfileStore(paths).Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null,
            new PrimaryContact("Julie", "Martin", "Achats", "0600000000", "julie@example.fr"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var result = CreateService(paths).Search(_root, "julie@example.fr");

        Assert.Contains(result.Results, item => item.Kind == GlobalSearchResultKind.Client && item.Title == "MPO");
    }

    private static GlobalSearchService CreateService(WorkspaceMetadataPaths paths)
        => new(new ClientCatalog(new ClientProfileStore(paths)), new ProjectCatalog(new ProjectProfileStore(paths)));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
