using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateFilamentWindow : DialogWindow
{
    private static readonly string[] Materials = [
        "PLA", "PLA+", "PLA Silk", "PLA Matte", "PETG", "PETG-CF", "ABS", "ASA", "TPU 95A", "TPU 98A", "TPE",
        "PA6 (Nylon)", "PA12 (Nylon)", "PA-CF", "PC", "PC-ABS", "POM", "PP", "HIPS", "PVA", "BVOH", "PET", "CPE",
        "PEI (Ultem)", "PPS", "PPSU", "PEEK", "PEKK", "PVDF", "Bois", "Métal", "Céramique", "Conducteur", "Phosphorescent", "Recyclé", "Autre"
    ];
    private static readonly string[] Colors = [
        "Noir", "Blanc", "Gris", "Argent", "Rouge", "Orange", "Jaune", "Vert", "Bleu", "Violet", "Rose", "Marron", "Transparent", "Multicolore", "Autre"
    ];
    private readonly TextBox _brand = new() { PlaceholderText = "Marque" };
    private readonly TextBox _name = new() { PlaceholderText = "Nom du filament" };
    private readonly ComboBox _material = new() { ItemsSource = Materials, SelectedIndex = 0, IsEditable = true };
    private readonly ComboBox _color = new() { ItemsSource = Colors, IsEditable = true, PlaceholderText = "Couleur (optionnelle)" };
    private readonly TextBox _price = new() { PlaceholderText = "ex. 24,90" };
    private readonly CheckBox _abrasive = new() { Content = "Matière abrasive" };
    private readonly TextBlock _error = FormKit.ErrorText();
    public FilamentProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? string.Empty;
    public string FilamentName => _name.Text?.Trim() ?? string.Empty;
    public string Material => (_material.SelectedItem?.ToString() ?? _material.Text ?? "PLA").Trim();
    public string? Color => string.IsNullOrWhiteSpace(_color.Text) ? _color.SelectedItem?.ToString()?.Trim() : _color.Text.Trim();
    public decimal Price { get; private set; }
    public bool IsAbrasive => _abrasive.IsChecked == true;

    public CreateFilamentWindow(FilamentProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter un filament" : "Modifier le filament"; Width = 480; Height = 640; MinWidth = 420;
        _brand.Text = original?.Brand ?? string.Empty;
        _name.Text = original?.Name ?? string.Empty;
        _material.SelectedItem = original?.Material ?? "PLA";
        _color.Text = original?.Color ?? string.Empty;
        _color.SelectedItem = Colors.Contains(original?.Color ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Color : null;
        _price.Text = original?.PricePerKg.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? string.Empty;
        _abrasive.IsChecked = original?.IsAbrasive ?? false;
        var save = new Button { Content = original is null ? "Ajouter le filament" : "Enregistrer" };
        save.Click += Save_Click;
        Layout(original is null ? "Nouveau filament" : "Modifier le filament", "Le prix au kilo sert au coût matière de vos estimations.",
            [FormKit.Field("Marque", _brand), FormKit.Field("Nom du filament", _name), FormKit.Field("Matière", _material),
             FormKit.Field("Couleur (optionnelle)", _color), FormKit.Field("Prix au kilo", _price, "€/kg"), _abrasive, _error], save);
    }
    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(FilamentName) || !FilamentPrice.TryParse(_price.Text, out var price))
        { _error.Text = "Renseignez la marque, le nom et un prix valide."; _error.IsVisible = true; return; }
        Price = price; Close(true);
    }
}
