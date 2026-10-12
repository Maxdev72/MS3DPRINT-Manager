using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Desktop;

public sealed class CreatePrinterWindow : Window
{
    public static IReadOnlyList<string> KnownBrands { get; } =
    [
        "AnkerMake", "Anycubic", "Artillery", "Bambu Lab", "BCN3D", "BigRep",
        "Creality", "Elegoo", "Flashforge", "FLSUN", "Formlabs", "Markforged",
        "Phrozen", "Prusa Research", "Qidi Tech", "Raise3D", "Rat Rig", "Snapmaker",
        "Sovol", "UltiMaker", "Uniformation", "Voron", "Zortrax"
    ];

    private readonly ComboBox _brand = new()
    {
        PlaceholderText = "Choisir ou saisir une marque",
        IsEditable = true,
        ItemsSource = KnownBrands
    };
    private readonly TextBox _model = new() { PlaceholderText = "Modèle (ex. P1S)" };
    private readonly TextBox _purchasePrice = new() { PlaceholderText = "Prix d’achat (€)" };
    private readonly TextBox _lifetimeHours = new() { PlaceholderText = "Durée de vie estimée (h, ex. 8000)" };
    private readonly TextBox _powerWatts = new() { PlaceholderText = "Puissance (W, optionnelle)" };
    private readonly TextBox _volume = new() { PlaceholderText = "Volume (ex. 256 × 256 × 256 mm)" };
    private readonly CheckBox _active = new() { Content = "Imprimante active", IsChecked = true };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
    public PrinterProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? _brand.SelectedItem as string ?? string.Empty;
    public string Model => _model.Text?.Trim() ?? string.Empty;
    public string PrinterName => $"{Brand} {Model}".Trim();
    public string BuildVolume => _volume.Text?.Trim() ?? string.Empty;
    public decimal PurchasePrice { get; private set; }
    public int LifetimeHours { get; private set; }
    public int? PowerWatts { get; private set; }
    public decimal HourlyRate { get; private set; }
    public bool PrinterIsActive => _active.IsChecked == true;

    public CreatePrinterWindow(PrinterProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter une imprimante" : "Modifier l’imprimante"; Width = 440; Height = 500;
        _brand.Text = original?.Brand ?? string.Empty;
        _brand.SelectedItem = KnownBrands.Contains(original?.Brand ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Brand : null;
        _model.Text = original?.Model ?? string.Empty;
        _purchasePrice.Text = original?.PurchasePrice.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "0";
        _lifetimeHours.Text = (original?.LifetimeHours is > 0 ? original.LifetimeHours : 8000).ToString();
        _powerWatts.Text = original?.PowerWatts?.ToString() ?? string.Empty;
        _volume.Text = original?.BuildVolume ?? string.Empty; _active.IsChecked = original?.IsActive ?? true;
        var save = new Button { Content = original is null ? "Ajouter" : "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = "Nouvelle imprimante", FontSize = 20 }, _brand, _model, _purchasePrice, _lifetimeHours, _powerWatts, _volume, _active, _error, save } };
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Model) || !FilamentPrice.TryParse(_purchasePrice.Text, out var purchasePrice) ||
            !int.TryParse(_lifetimeHours.Text, out var lifetimeHours) || lifetimeHours <= 0 ||
            (!string.IsNullOrWhiteSpace(_powerWatts.Text) && (!int.TryParse(_powerWatts.Text, out var watts) || watts < 0)))
        { _error.Text = "Renseignez la marque, le modèle, le prix d’achat et une durée de vie valide."; _error.IsVisible = true; return; }
        PurchasePrice = purchasePrice;
        LifetimeHours = lifetimeHours;
        PowerWatts = string.IsNullOrWhiteSpace(_powerWatts.Text) ? null : int.Parse(_powerWatts.Text);
        HourlyRate = PrinterCostCalculator.HourlyRate(new PrinterProfile(Guid.Empty, Brand, Model, PrinterName, 0m, BuildVolume, PrinterIsActive, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, PurchasePrice, LifetimeHours, PowerWatts));
        Close(true);
    }
}

