using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop;

public static class FilamentChoiceVisual
{
    public static Control Create(FilamentProfile? filament)
    {
        if (filament is null) return new TextBlock();
        var title = new TextBlock { Text = filament.Brand + " · " + filament.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var price = new TextBlock { Text = $"{filament.PricePerKg:0.00} €/kg", VerticalAlignment = VerticalAlignment.Top };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(price, 1); heading.Children.Add(title); heading.Children.Add(price);
        var details = filament.Material + (string.IsNullOrWhiteSpace(filament.Color) ? "" : " · " + filament.Color) + (filament.IsAbrasive ? " · Abrasif" : "");
        return new StackPanel { Spacing = 3, Children = { heading, new TextBlock { Text = details, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap } } };
    }
}

public sealed class FilamentPickerWindow : DialogWindow
{
    private readonly IReadOnlyList<FilamentProfile> _filaments;
    private readonly TextBox _search = new() { PlaceholderText = "Marque, nom, matière, couleur…" };
    private readonly ComboBox _family = new() { ItemsSource = FilamentCatalogSearch.Families, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _results = new() { MinHeight = 140, MaxHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "lookup-suggestions" } };
    private readonly TextBlock _count = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _choose = new() { Content = "Choisir ce filament" };
    public FilamentProfile? SelectedFilament => _results.SelectedItem as FilamentProfile;

    public FilamentPickerWindow(IReadOnlyList<FilamentProfile> filaments, FilamentProfile? selected)
    {
        _filaments = filaments; Title = "Choisir un filament"; Width = 600; Height = 760; MinWidth = 440;
        _results.ItemTemplate = new FuncDataTemplate<FilamentProfile>((f, _) => FilamentChoiceVisual.Create(f));
        Avalonia.Automation.AutomationProperties.SetName(_results, "Filaments disponibles");
        _search.TextChanged += (_, _) => Filter(); _family.SelectionChanged += (_, _) => Filter();
        _results.SelectionChanged += (_, _) => _choose.IsEnabled = SelectedFilament is not null;
        _choose.Click += (_, _) => { if (SelectedFilament is not null) Close(true); };
        Layout("Choisir un filament", "Retrouvez une bobine et vérifiez son prix avant de la sélectionner.",
            [FormKit.Field("Rechercher un filament", _search), FormKit.Field("Famille", _family), _count, _results], _choose);
        Filter(); _results.SelectedItem = filaments.FirstOrDefault(f => f.Id == selected?.Id);
        _choose.IsEnabled = SelectedFilament is not null;
        Opened += (_, _) => _search.Focus();
    }

    private void Filter()
    {
        var previous = SelectedFilament?.Id;
        var filtered = FilamentCatalogSearch.Filter(_filaments, _search.Text, _family.SelectedItem as string);
        _results.ItemsSource = filtered;
        _results.SelectedItem = filtered.FirstOrDefault(f => f.Id == previous);
        _count.Text = filtered.Count == 0 ? "Aucun résultat. Modifiez la recherche ou choisissez une autre famille." : $"{filtered.Count} sur {_filaments.Count} filament{(_filaments.Count > 1 ? "s" : "")}";
        _choose.IsEnabled = SelectedFilament is not null;
    }
}
