using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class ClientDetailViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS3DPRINT-client-detail-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_IndividualDropsProfessionalDataButKeepsPhoneEmail()
    {
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Individual, "MARTIN", "MARTIN", null, "Alice", "Martin", null, null,
            new PrimaryContact("Duplicate", "Identity", "Director", "0100000000", "alice@example.test"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "12345678900012");
        store.Create(profile);
        var model = new ClientDetailViewModel(profile, store);
        model.Save();
        var saved = store.LoadAll().Single();
        Assert.Null(saved.Siret);
        Assert.Null(saved.PrimaryContact.FirstName);
        Assert.Null(saved.PrimaryContact.LastName);
        Assert.Null(saved.PrimaryContact.Role);
        Assert.Equal("0100000000", saved.PrimaryContact.Phone);
        Assert.Equal("alice@example.test", saved.PrimaryContact.Email);
    }

    [Fact]
    public void Save_ProfessionalSiretCanBeEdited()
    {
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new CreateClientViewModel { ClientName = "Atelier", Siret = "12345678900012" }.CreateProfile();
        store.Create(profile);
        var model = new ClientDetailViewModel(profile, store);
        var property = typeof(ClientDetailViewModel).GetProperty("Siret");
        Assert.NotNull(property);
        Assert.Equal("12345678900012", property.GetValue(model));
        property.SetValue(model, " 98765432100012 ");
        model.Save();
        Assert.Equal("98765432100012", store.LoadAll().Single().Siret);
    }

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

    [Fact]
    public void Save_UpdatesIdentityContactAndAddressWithoutChangingFolderOrCode()
    {
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Professional, "MPO", "MPO", "MPO", null, null, "Ancienne adresse", null,
            new PrimaryContact("Marie", "Durand", "Achats", "0100000000", "ancienne@example.fr"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        store.Create(profile);
        var viewModel = new ClientDetailViewModel(profile, store)
        {
            CompanyName = "MPO Industrie",
            Address = "12 rue des Ateliers",
            ContactFirstName = "Julie",
            ContactLastName = "Martin",
            ContactRole = "Direction",
            ContactPhone = "0200000000",
            ContactEmail = "julie@example.fr"
        };

        viewModel.Save();

        var saved = store.LoadAll().Single();
        Assert.Equal("MPO Industrie", saved.CompanyName);
        Assert.Equal("12 rue des Ateliers", saved.Address);
        Assert.Equal(new PrimaryContact("Julie", "Martin", "Direction", "0200000000", "julie@example.fr"), saved.PrimaryContact);
        Assert.Equal("MPO", saved.FolderName);
        Assert.Equal("MPO", saved.ClientCode);
        Assert.Equal("MPO Industrie", viewModel.DisplayName);
    }

    [Fact]
    public void Save_RequiresIndividualLastNameAndLeavesExistingProfileUntouched()
    {
        var store = new ClientProfileStore(new WorkspaceMetadataPaths(_root));
        var profile = new ClientProfile(Guid.NewGuid(), ClientKind.Individual, "DUPONT", "DUPONT", null, "Anne", "Dupont", null, null,
            new PrimaryContact(null, null, null, null, null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        store.Create(profile);
        var viewModel = new ClientDetailViewModel(profile, store) { LastName = "  " };

        Assert.Throws<ArgumentException>(() => viewModel.Save());
        Assert.Equal("Dupont", store.LoadAll().Single().LastName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
