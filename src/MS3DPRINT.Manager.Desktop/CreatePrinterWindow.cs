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
    private readonly TextBox _purchasePrice = new() { PlaceholderText = "Prix d’achat" };
    private readonly TextBox _lifetimeHours = new() { PlaceholderText = "ex. 8000" };
    private readonly TextBox _powerWatts = new() { PlaceholderText = "ex. 150" };
    private readonly TextBox _volume = new() { PlaceholderText = "Non utilisé pour l’instant", IsEnabled = false };
    private readonly TextBlock _error = new() { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };
    public PrinterProfile? Original { get; }
    public string Brand => _brand.Text?.Trim() ?? _brand.SelectedItem as string ?? string.Empty;
    public string Model => _model.Text?.Trim() ?? string.Empty;
    public string PrinterName => $"{Brand} {Model}".Trim();
    public string BuildVolume => Original?.BuildVolume ?? string.Empty;
    public decimal PurchasePrice { get; private set; }
    public int LifetimeHours { get; private set; }
    public int? PowerWatts { get; private set; }
    public decimal HourlyRate { get; private set; }
    public bool PrinterIsActive => Original?.IsActive ?? true;

    public CreatePrinterWindow(PrinterProfile? original = null)
    {
        Original = original;
        Title = original is null ? "Ajouter une imprimante" : "Modifier l’imprimante"; Width = 440; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _brand.Text = original?.Brand ?? string.Empty;
        _brand.SelectedItem = KnownBrands.Contains(original?.Brand ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? original?.Brand : null;
        _model.Text = original?.Model ?? string.Empty;
        _purchasePrice.Text = original?.PurchasePrice.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) ?? "0";
        _lifetimeHours.Text = (original?.LifetimeHours is > 0 ? original.LifetimeHours : 8000).ToString();
        _powerWatts.Text = original?.PowerWatts?.ToString() ?? string.Empty;
        _volume.Text = original?.BuildVolume ?? string.Empty;
        var save = new Button { Content = original is null ? "Ajouter" : "Enregistrer", HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += Save_Click;
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, Children = { new TextBlock { Text = original is null ? "Nouvelle imprimante" : "Modifier l’imprimante", FontSize = 20 }, CreateUnitField("Marque", _brand), CreateUnitField("Modèle", _model), CreateUnitField("Prix d’achat", _purchasePrice, "€"), CreateUnitField("Durée de vie estimée", _lifetimeHours, "h"), CreateUnitField("Puissance (optionnelle)", _powerWatts, "W"), CreateUnitField("Volume d’impression — bientôt disponible", _volume, "mm"), _error, save } };
    }

    internal static StackPanel CreateUnitField(string label, Control input, string? unit = null)
    {
        var text = new TextBlock { Text = label };
        text.Classes.Add("muted");
        Control row = input;
        if (unit is not null)
        {
            var suffix = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, MinWidth = 36 };
            suffix.Classes.Add("muted");
            Grid.SetColumn(suffix, 1);
            row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { input, suffix } };
        }
        return new StackPanel { Spacing = 4, Children = { text, row } };
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

