using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Clients;

public sealed class ClientCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-catalog-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ClientCatalog _catalog;

    public ClientCatalogTests() => _catalog = new(new ClientProfileStore(new WorkspaceMetadataPaths(_root)));

    [Fact]
    public void Load_ShowsExistingFolderWithoutProfileAsNeedingCompletion()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "DUPONT"));

        var client = _catalog.Load(_root).Single();

        Assert.True(client.IsProfileMissing);
        Assert.Equal("DUPONT", client.DisplayName);
        Assert.Equal("DUPONT", client.FolderName);
    }

    [Fact]
    public void Load_ExcludesClientSystemFoldersAndCountsOnlyProjectReferences()
    {
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "00_CLIENT"));
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "99_ARCHIVES"));
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO", "MPO-2026-001_OUTILLAGE"));

        var client = _catalog.Load(_root).Single();

        Assert.Equal(1, client.ProjectCount);
    }

    [Fact]
    public void ClientSummary_UsesALabelInsteadOfShowingItsProfileBoolean()
    {
        var client = new ClientSummary(_root, "MPO", "MPO", "MPO", null, null, 1);

        Assert.Equal("Fiche à compléter", client.ProfileLabel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
