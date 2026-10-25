using System.Globalization;
using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>The ceilings of the user's own regime; an empty field means the ceiling is not watched.</summary>
public sealed class ThresholdsWindow : DialogWindow
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly TextBox _microTotal = new() { PlaceholderText = "Non surveillé" };
    private readonly TextBox _microServices = new() { PlaceholderText = "Non surveillé" };
    private readonly TextBox _vatTotal = new() { PlaceholderText = "Non surveillé" };
    private readonly TextBox _vatServices = new() { PlaceholderText = "Non surveillé" };
    private readonly TextBlock _error = FormKit.ErrorText();

    public ActivityThresholds Result { get; private set; } = new();

    public ThresholdsWindow(ActivityThresholds current)
    {
        Title = "Seuils d’activité"; Width = 520; Height = 700; MinWidth = 440;
        string? Show(decimal? value) => value?.ToString("0.##", French);
        _microTotal.Text = Show(current.MicroTotalCeiling); _microServices.Text = Show(current.MicroServicesCeiling);
        _vatTotal.Text = Show(current.VatTotalThreshold); _vatServices.Text = Show(current.VatServicesThreshold);
        var save = new Button { Content = "Enregistrer" };
        save.Click += Save_Click;
        foreach (var field in new[] { _microTotal, _microServices, _vatTotal, _vatServices }) field.TextChanged += (_, _) => FormKit.ClearError(_error);
        Layout("Seuils d’activité",
            "Saisissez les plafonds de votre régime pour être averti quand votre chiffre d’affaires encaissé s’en approche (à partir de 80 %). Les montants changent selon l’année et l’activité : vérifiez-les sur impots.gouv.fr ou auprès de l’URSSAF. Laissez vide un plafond à ne pas surveiller.",
            [FormKit.Field("Plafond micro · chiffre d’affaires total", _microTotal, "€ / an"), FormKit.Field("Plafond micro · prestations de services", _microServices, "€ / an"),
             FormKit.Field("Franchise de TVA · chiffre d’affaires total", _vatTotal, "€ / an"), FormKit.Field("Franchise de TVA · prestations de services", _vatServices, "€ / an"), _error], save);
    }

    private bool TryRead(TextBox box, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(box.Text)) return true;
        if (!FilamentPrice.TryParse(box.Text, out var parsed) || parsed <= 0) { FormKit.ShowError(_error, box, "Saisissez un montant strictement positif, ou laissez vide."); return false; }
        value = parsed; return true;
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!TryRead(_microTotal, out var a) || !TryRead(_microServices, out var b) || !TryRead(_vatTotal, out var c) || !TryRead(_vatServices, out var d)) return;
        Result = new ActivityThresholds(a, b, c, d);
        Close(true);
    }
}
