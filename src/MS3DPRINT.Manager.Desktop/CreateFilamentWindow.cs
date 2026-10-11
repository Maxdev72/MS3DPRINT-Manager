using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreateFilamentWindow : Window
{
    private static readonly string[] Materials = [
        "PLA", "PLA+", "PLA Silk", "PLA Matte", "PETG", "PETG-CF", "ABS", "ASA", "TPU 95A", "TPU 98A", "TPE",
        "PA6 (Nylon)", "PA12 (Nylon)", "PA-CF", "PC", "PC-ABS", "POM", "PP", "HIPS", "PVA", "BVOH", "PET", "CPE",
        "PEI (Ultem)", "PPS", "PPSU", "PEEK", "PEKK", "PVDF", "Bois", "Métal", "Céramique", "Conducteur", "Phosphorescent", "Recyclé", "Autre"
    ];
    private readonly TextBox _brand = new() { PlaceholderText = "Marque" };
    private readonly TextBox _name = new() { PlaceholderText = "Nom du filament" };
    private readonly ComboBox _material = new() { ItemsSource = Materials, SelectedIndex = 0, IsEditable = true };
    private readonly TextBox _price = new() { PlaceholderText = "Prix au kg (ex. 24,90)" };
    private readonly CheckBox _abrasive = new() { Content = "Matière abrasive" };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
    public FilamentProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? string.Empty;
    public string FilamentName => _name.Text?.Trim() ?? string.Empty;
    public string Material => (_material.SelectedItem?.ToString() ?? _material.Text ?? "PLA").Trim();
    public decimal Price { get; private set; }
    public bool IsAbrasive => _abrasive.IsChecked == true;

    public CreateFilamentWindow(FilamentProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter un filament" : "Modifier le filament"; Width = 440; Height = 410;
        _brand.Text = original?.Brand ?? string.Empty;
        _name.Text = original?.Name ?? string.Empty;
        _material.SelectedItem = original?.Material ?? "PLA";
        _price.Text = original?.PricePerKg.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? string.Empty;
        _abrasive.IsChecked = original?.IsAbrasive ?? false;
        var save = new Button { Content = original is null ? "Ajouter" : "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nouveau filament", FontSize = 20 }, _brand, _name, _material, _price, _abrasive, _error, save } };
    }
    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(FilamentName) || !FilamentPrice.TryParse(_price.Text, out var price))
        { _error.Text = "Renseignez la marque, le nom et un prix valide."; _error.IsVisible = true; return; }
        Price = price; Close(true);
    }
}
