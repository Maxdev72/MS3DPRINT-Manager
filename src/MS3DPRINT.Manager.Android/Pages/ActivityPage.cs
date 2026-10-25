using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Companion.Services;
using MS3DPRINT.Manager.Companion.Ui;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Companion.Pages;

/// <summary>Turnover of the year, month by month, and the ceilings of the user's regime.</summary>
public sealed class ActivityPage : UserControl
{
    private static readonly string[] MonthLetters = ["J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D"];
    private readonly AppServices _app = AppServices.Current;
    private readonly StackPanel _stack = new() { Spacing = 14, Margin = new Thickness(16, 12, 16, 28) };

    public ActivityPage()
    {
        Content = new ScrollViewer { Content = _stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _app.Changed += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        _stack.Children.Clear();
        var snapshot = _app.Snapshot;
        if (snapshot is null)
        {
            _stack.Children.Add(Ui.Ui.Title("Activité"));
            _stack.Children.Add(Ui.Ui.EmptyState("Aucun état de l’atelier reçu", _app.SnapshotProblem ?? "Ouvrez MS3DPRINT Manager sur le PC, puis actualisez."));
            return;
        }
        var activity = snapshot.Activity;
        _stack.Children.Add(Ui.Ui.Title($"Activité {activity.Year}"));
        var age = DateTimeOffset.Now - snapshot.GeneratedAt;
        if (age > TimeSpan.FromDays(1)) _stack.Children.Add(Ui.Ui.Banner($"Ces chiffres datent du {snapshot.GeneratedAt:dd/MM/yyyy à HH:mm}. Ouvrez MS3DPRINT Manager sur le PC pour les mettre à jour.", BannerKind.Warning));
        else _stack.Children.Add(Ui.Ui.Muted($"État du PC du {snapshot.GeneratedAt:dd/MM/yyyy à HH:mm}"));

        var total = new StackPanel { Spacing = 4, Children =
        {
            Ui.Ui.Text("Chiffre d’affaires encaissé", 14, Pal.Muted),
            Ui.Ui.Text(RevenueExporter.Money(activity.Total), 34, Pal.Text, bold: true),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0), Children =
            {
                Ui.Ui.Pill("Marchandises " + RevenueExporter.Money(activity.Goods), Pal.AccentSoft, Pal.Accent),
                Ui.Ui.Pill("Prestations " + RevenueExporter.Money(activity.Services), Pal.SuccessSoft, Pal.Success)
            } },
            Ui.Ui.Text("Achats payés : " + RevenueExporter.Money(activity.PurchasesTotal), 15, Pal.Muted)
        } };
        _stack.Children.Add(Ui.Ui.Card(total));

        _stack.Children.Add(Ui.Ui.Section("Mois par mois"));
        _stack.Children.Add(Ui.Ui.Card(Months(activity.Months)));

        _stack.Children.Add(Ui.Ui.Section("Seuils d’activité"));
        if (activity.Thresholds.Count == 0)
            _stack.Children.Add(Ui.Ui.Muted("Aucun plafond surveillé. Renseignez ceux de votre régime dans Paramètres > Atelier sur le PC."));
        foreach (var threshold in activity.Thresholds) _stack.Children.Add(Threshold(threshold));

        var waiting = activity.PendingFromPhone + _app.PendingCount;
        _stack.Children.Add(Ui.Ui.Section("À importer"));
        _stack.Children.Add(Ui.Ui.Card(new StackPanel { Spacing = 4, Children =
        {
            Ui.Ui.Text(waiting == 0 ? "Rien en attente" : $"{waiting} saisie{(waiting > 1 ? "s" : "")} en attente", 17, waiting == 0 ? Pal.Success : Pal.Warning, bold: true),
            Ui.Ui.Muted($"{activity.PendingFromPhone} reçue{(activity.PendingFromPhone > 1 ? "s" : "")} par le PC, à confirmer ; {_app.PendingCount} encore sur le téléphone.")
        } }));
    }

    private static Control Months(IReadOnlyList<decimal> months)
    {
        var max = months.Count == 0 ? 0 : months.Max();
        var grid = new Grid { ColumnSpacing = 4, Height = 150 };
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star)); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var currentMonth = DateTime.Today.Month - 1;
        for (var i = 0; i < 12; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var value = i < months.Count ? months[i] : 0m;
            var height = max <= 0 ? 0 : Math.Max(value > 0 ? 4 : 0, (double)(value / max) * 110);
            var bar = new Border { Height = height, Background = i == currentMonth ? Pal.Accent : Pal.Brand, CornerRadius = new CornerRadius(4, 4, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(bar, RevenueExporter.Money(value));
            Grid.SetColumn(bar, i); grid.Children.Add(bar);
            var letter = new TextBlock { Text = MonthLetters[i], FontSize = 12, Foreground = i == currentMonth ? Pal.Accent : Pal.Faint, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetColumn(letter, i); Grid.SetRow(letter, 1); grid.Children.Add(letter);
        }
        var peak = max > 0 ? Ui.Ui.Muted("Meilleur mois : " + RevenueExporter.Money(max), 13) : Ui.Ui.Muted("Aucune recette encaissée cette année.", 13);
        return new StackPanel { Spacing = 8, Children = { grid, peak } };
    }

    private static Control Threshold(SnapshotThreshold threshold)
    {
        var percent = threshold.Limit <= 0 ? 0 : (double)(threshold.Amount / threshold.Limit * 100m);
        var (brush, text) = threshold.Level switch
        {
            "Exceeded" => (Pal.Danger, "Plafond atteint ou dépassé"),
            "Near" => (Pal.Warning, $"Il reste {RevenueExporter.Money(Math.Max(0, threshold.Limit - threshold.Amount))}"),
            _ => (Pal.Success, $"Il reste {RevenueExporter.Money(Math.Max(0, threshold.Limit - threshold.Amount))}")
        };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Min(100, percent), Height = 10, Foreground = brush, Background = Pal.Neutral, CornerRadius = new CornerRadius(5) };
        return Ui.Ui.Card(new StackPanel { Spacing = 8, Children =
        {
            Ui.Ui.Text(threshold.Label, 15, Pal.Text, bold: true),
            bar,
            new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children =
            {
                Ui.Ui.Text($"{RevenueExporter.Money(threshold.Amount)} sur {RevenueExporter.Money(threshold.Limit)}", 14, Pal.Muted),
                new TextBlock { Text = $"{percent.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"))} %", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = brush, [Grid.ColumnProperty] = 1 }
            } },
            Ui.Ui.Text(text, 13, brush)
        } });
    }
}
