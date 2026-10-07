using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.App.ViewModels;

public sealed class FilamentsViewModel : ObservableObject
{
    private IReadOnlyList<FilamentProfile> _all = [];
    private IReadOnlyList<FilamentProfile> _visible = [];
    private string _search = "";
    private string? _brand;
    private string? _material;
    private bool? _abrasive;
    private FilamentProfile? _selected;
    public const string AllBrands = "Toutes les marques";
    public const string AllMaterials = "Tous les types";
    public string SearchText { get => _search; set { if (SetProperty(ref _search, value)) Filter(); } }
    public string? BrandFilter { get => _brand; set { if (SetProperty(ref _brand, value)) Filter(); } }
    public string? MaterialFilter { get => _material; set { if (SetProperty(ref _material, value)) Filter(); } }
    public bool? AbrasiveFilter { get => _abrasive; set { if (SetProperty(ref _abrasive, value)) Filter(); } }
    public FilamentProfile? SelectedFilament { get => _selected; set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelection)); } }
    public bool HasSelection => SelectedFilament is not null;
    public IReadOnlyList<FilamentProfile> VisibleFilaments { get => _visible; private set => SetProperty(ref _visible, value); }
    public IReadOnlyList<string> Brands => new[] { AllBrands }.Concat(_all.Select(p => p.Brand).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s)).ToArray();
    public IReadOnlyList<string> Materials => new[] { AllMaterials }.Concat(_all.Select(p => p.Material).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(s => s)).ToArray();
    public void ApplyProfiles(IReadOnlyList<FilamentProfile> profiles)
    {
        var selectedId = SelectedFilament?.Id;
        _all = profiles;
        OnPropertyChanged(nameof(Brands));
        OnPropertyChanged(nameof(Materials));
        Filter();
        SelectedFilament = VisibleFilaments.FirstOrDefault(p => p.Id == selectedId);
    }
    private void Filter()
    {
        VisibleFilaments = _all.Where(p =>
            (string.IsNullOrEmpty(_brand) || _brand == AllBrands || string.Equals(p.Brand, _brand, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrEmpty(_material) || _material == AllMaterials || string.Equals(p.Material, _material, StringComparison.CurrentCultureIgnoreCase)) &&
            (!_abrasive.HasValue || p.IsAbrasive == _abrasive) &&
            (p.Brand + " " + p.Name + " " + p.Material).Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Brand, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (SelectedFilament is not null && !VisibleFilaments.Contains(SelectedFilament)) SelectedFilament = null;
    }
}
