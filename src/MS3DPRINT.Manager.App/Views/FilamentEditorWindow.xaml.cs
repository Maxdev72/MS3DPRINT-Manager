using System.Globalization;
using System.Windows;
using MS3DPRINT.Manager.Core.Filaments;

namespace MS3DPRINT.Manager.App.Views;

public partial class FilamentEditorWindow : Window
{
    private readonly FilamentProfile? _original;
    private readonly Action<FilamentProfile>? _save;
    public FilamentProfile? Result { get; private set; }

    public FilamentEditorWindow(FilamentProfile? original = null, Action<FilamentProfile>? save = null)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        MaxWidth = SystemParameters.WorkArea.Width * 0.9;
        MinHeight = Math.Min(MinHeight, MaxHeight);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        _original = original;
        _save = save;
        Title = original is null ? "Ajouter un filament" : "Modifier le filament";
        BrandBox.Text = original?.Brand ?? "";
        NameBox.Text = original?.Name ?? "";
        MaterialBox.ItemsSource = new[] { "PLA", "PETG", "ABS", "ASA", "TPU", "PA", "PC", "PVA" };
        MaterialBox.Text = original?.Material ?? "PLA";
        PriceBox.Text = (original?.PricePerKg ?? 0).ToString("0.00##########################", CultureInfo.GetCultureInfo("fr-FR"));
        AbrasiveBox.ItemsSource = new[] { "Non", "Oui" };
        AbrasiveBox.SelectedIndex = original?.IsAbrasive == true ? 1 : 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (string.IsNullOrWhiteSpace(BrandBox.Text)) { ErrorText.Text = "La marque est requise."; return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ErrorText.Text = "Le nom est requis."; return; }
        if (string.IsNullOrWhiteSpace(MaterialBox.Text)) { ErrorText.Text = "La matière / le type est requis."; return; }
        if (!FilamentPrice.TryParse(PriceBox.Text, out var price)) { ErrorText.Text = "Saisissez un prix valide supérieur ou égal à zéro (exemple : 24,90)."; return; }
        var now = DateTimeOffset.UtcNow;
        var result = new FilamentProfile(_original?.Id ?? Guid.NewGuid(), BrandBox.Text.Trim(), NameBox.Text.Trim(),
            MaterialBox.Text.Trim(), price, AbrasiveBox.SelectedIndex == 1, _original?.CreatedAt ?? now, now);
        try
        {
            _save?.Invoke(result);
            Result = result;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ErrorText.Text = "Enregistrement impossible : " + exception.Message;
        }
    }
}
