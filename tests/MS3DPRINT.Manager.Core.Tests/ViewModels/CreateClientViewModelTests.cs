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
}
