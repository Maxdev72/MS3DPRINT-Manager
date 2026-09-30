using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ClientsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-vm-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void VisibleClients_MatchesCodeContactAndSelectedKind()
    {
        var paths = new WorkspaceMetadataPaths(_root);
        var store = new ClientProfileStore(paths);
        store.Create(new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null, null, null,
            new PrimaryContact("Marie", "Durand", null, null, "marie@example.test"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        store.Create(new ClientProfile(Guid.NewGuid(), ClientKind.Individual, "DUPONT_JULIEN", "DUPONT", null, "Julien", "Dupont", null, null,
            new PrimaryContact("Julien", "Dupont", null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "MPO"));
        Directory.CreateDirectory(Path.Combine(_root, "01_CLIENTS", "DUPONT_JULIEN"));
        var viewModel = new ClientsViewModel(new ClientCatalog(store), _root);

        viewModel.Refresh();
        viewModel.SearchText = "marie";
        viewModel.SelectedKind = ClientKind.Professional;

        Assert.Equal("MPO", Assert.Single(viewModel.VisibleClients).ClientCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
