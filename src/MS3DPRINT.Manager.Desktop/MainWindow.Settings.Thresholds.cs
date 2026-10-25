using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly TextBlock _revenueThresholdNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, IsVisible = false };

    /// <summary>Ceilings of the user's regime, watched against the collected turnover of the book.</summary>
    private Border BuildThresholdsSettingsCard(string root)
    {
        ActivityThresholds thresholds;
        try { thresholds = new ActivityThresholdStore(root).Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { thresholds = new(); Notify("Les seuils d’activité ne peuvent pas être lus : " + e.Message, error: true); }
        var edit = new Button { Content = Icons.Label("IconEdit", "Modifier", 16), Classes = { "primary-action" }, HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += EditThresholds_Click;
        static string Value(decimal? amount) => amount is { } v ? RevenueExporter.Money(v) + " / an" : "Non surveillé";
        var values = new StackPanel { Spacing = 0, Children =
        {
            SettingsValueRow("Plafond micro · chiffre d’affaires total", Value(thresholds.MicroTotalCeiling)),
            SettingsValueRow("Plafond micro · prestations de services", Value(thresholds.MicroServicesCeiling)),
            SettingsValueRow("Franchise de TVA · chiffre d’affaires total", Value(thresholds.VatTotalThreshold)),
            SettingsValueRow("Franchise de TVA · prestations de services", Value(thresholds.VatServicesThreshold))
        } };
        var header = SettingsCardHeader("IconActivity", "Seuils d’activité", "Plafonds de votre régime, surveillés sur la page Recettes à partir de 80 % du chiffre d’affaires encaissé. Aucun montant n’est imposé par le logiciel.", edit);
        return new Border { Classes = { "card-raised" }, Child = new StackPanel { Spacing = 16, Children = { header, values } } };
    }

    private async void EditThresholds_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        try
        {
            var store = new ActivityThresholdStore(root);
            var dialog = new ThresholdsWindow(await Task.Run(() => store.Load()));
            if (!await dialog.ShowDialog<bool>(this)) return;
            var result = dialog.Result;
            await Task.Run(() => store.Save(result));
            Notify("Seuils d’activité enregistrés.");
            await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Notify(exception.Message, error: true); }
    }

    /// <summary>One line per watched ceiling under the page summary; amber from 80 %, red once reached.</summary>
    private void ShowRevenueThresholds(ActivityThresholds thresholds, RevenueSummary summary, int year)
    {
        var statuses = ActivityThresholdEvaluator.Evaluate(thresholds, summary);
        _revenueThresholdNote.IsVisible = true;
        if (statuses.Count == 0)
        {
            _revenueThresholdNote.Text = "Aucun plafond surveillé : renseignez ceux de votre régime dans Paramètres > Atelier.";
            _revenueThresholdNote.Classes.Set("muted", true);
            _revenueThresholdNote.Foreground = null;
            return;
        }
        _revenueThresholdNote.Classes.Set("muted", false);
        _revenueThresholdNote.Text = string.Join("\n", statuses.Select(s =>
            $"{s.Label} : {RevenueExporter.Money(s.Amount)} sur {RevenueExporter.Money(s.Limit)} ({s.Percent:0.#} %)"
            + (s.Level == ThresholdLevel.Exceeded ? " — plafond atteint ou dépassé en " + year : s.Level == ThresholdLevel.Near ? $" — il reste {RevenueExporter.Money(s.Remaining)}" : "")));
        var worst = statuses.Max(s => s.Level);
        var brushKey = worst == ThresholdLevel.Exceeded ? "DangerBrush" : worst == ThresholdLevel.Near ? "WarningBrush" : null;
        _revenueThresholdNote.Foreground = brushKey is null ? null : (IBrush?)Avalonia.Application.Current?.FindResource(brushKey);
    }

    /// <summary>Quote, collected money and linked purchases of a project, loaded in the background when the sheet is shown.</summary>
    private Control BuildProjectBalanceSection(ProjectSummary project)
    {
        var root = WorkspaceRoot;
        // One compact line of figures: the project's files stay the main content of the sheet.
        var heading = new TextBlock { Text = "Bilan du projet", FontSize = 16, FontWeight = FontWeight.SemiBold, Classes = { "brand-heading" }, VerticalAlignment = VerticalAlignment.Center };
        var section = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 28, LineSpacing = 6, IsVisible = false, Children = { heading } };
        ToolTip.SetTip(section, "Calcul d’après les recettes et achats rattachés à la référence du projet. Les cotisations, l’électricité et l’usure des machines ne sont pas déduites.");
        static Control Stat(string label, string value) => new StackPanel { Spacing = 0, Children =
        {
            new TextBlock { Text = label, FontSize = 12, Classes = { "muted" } },
            new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeight.SemiBold }
        } };
        section.AttachedToVisualTree += async (_, _) =>
        {
            try
            {
                var balance = await Task.Run(() =>
                {
                    var revenues = new RevenueJournal(root); var purchases = new PurchaseJournal(root);
                    return ProjectBalanceCalculator.Compute(project.Reference, new EstimateStore(root).LoadReadable(),
                        revenues.Years().SelectMany(y => revenues.Load(y).Lines), purchases.Years().SelectMany(y => purchases.Load(y).Lines));
                });
                if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase) || !balance.HasData) return;
                while (section.Children.Count > 1) section.Children.RemoveAt(1);
                section.Children.Add(Stat("Estimé (dernier devis)", balance.Quoted is { } q ? RevenueExporter.Money(q) : "Pas de devis"));
                section.Children.Add(Stat($"Encaissé ({balance.RevenueLines})", RevenueExporter.Money(balance.Collected)));
                if (balance.RemainingToCollect is { } left) section.Children.Add(Stat("Reste à encaisser", RevenueExporter.Money(left)));
                if (balance.PurchaseLines > 0) section.Children.Add(Stat($"Achats liés ({balance.PurchaseLines})", RevenueExporter.Money(balance.Purchases)));
                if (balance.RevenueLines > 0 || balance.PurchaseLines > 0) section.Children.Add(Stat("Encaissé − achats", RevenueExporter.Money(balance.NetOfPurchases)));
                section.IsVisible = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            { section.IsVisible = false; }
        };
        return section;
    }
}
