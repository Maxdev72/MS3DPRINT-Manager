using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Clients;

namespace MS3DPRINT.Manager.Core.Tests.ViewModels;

public sealed class CreateClientViewModelTests
{
    [Fact]
    public void Individual_UsesFirstAndLastNameForTheFolderPreview()
    {
        var viewModel = new CreateClientViewModel
        {
            Kind = ClientKind.Individual,
            FirstName = "Élodie",
            LastName = "Du Pont"
        };

        Assert.Equal("ELODIE_DU_PONT", viewModel.NormalizedName);
    }

    [Fact]
    public void CreateProfile_CopiesAProfessionalPrimaryContact()
    {
        var viewModel = new CreateClientViewModel
        {
            ClientName = "MPO",
            ClientCode = "MPO",
            ContactFirstName = "Marie",
            ContactLastName = "Durand",
            ContactEmail = "marie@example.test",
            Address = "1 rue de l'Atelier",
            Notes = "Contact par e-mail"
        };

        var profile = viewModel.CreateProfile();

        Assert.Equal("MPO", profile.CompanyName);
        Assert.Equal("Marie", profile.PrimaryContact.FirstName);
        Assert.Equal("Contact par e-mail", profile.Notes);
    }
}
