using MS3DPRINT.Manager.App.ViewModels;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Core.Tests.Filaments;

public sealed class FilamentsViewModelTests
{
    [Fact]
    public void FiltersCombineBrandMaterialAbrasiveAndSearch()
    {
        var model = new FilamentsViewModel();
        var now = DateTimeOffset.UtcNow;
        var matching = new FilamentProfile(Guid.NewGuid(), "Prusa", "Carbon", "PETG", 40, true, now, now);
        model.ApplyProfiles([matching, matching with { Id = Guid.NewGuid(), Brand = "Bambu" }, matching with { Id = Guid.NewGuid(), IsAbrasive = false }]);
        model.BrandFilter = "Prusa";
        model.MaterialFilter = "PETG";
        model.AbrasiveFilter = true;
        model.SearchText = "carb";
        Assert.Equal(matching, Assert.Single(model.VisibleFilaments));
        model.SelectedFilament = matching;
        model.SearchText = "absent";
        Assert.Empty(model.VisibleFilaments);
        Assert.Null(model.SelectedFilament);
    }
}
