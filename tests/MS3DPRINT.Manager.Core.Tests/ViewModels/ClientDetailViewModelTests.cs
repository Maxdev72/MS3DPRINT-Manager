using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ClientDetailViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-detail-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_UpdatesOnlyTheSelectedClientProfile()
    {
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null,
            new PrimaryContact("Marie", "Durand", null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        store.Create(profile);
        var viewModel = new ClientDetailViewModel(profile, store) { Notes = "Client prioritaire" };

        viewModel.Save();

        Assert.Equal("Client prioritaire", store.LoadAll().Single().Notes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
