using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private Expander _estimateOptionalCosts = null!;
    private readonly TextBlock _estimateOptionalCostSummary = EstimateHint("");
    private readonly TextBlock _estimateProductionSummary = EstimateHint("");

    private void InitializeEstimateProgressiveDisclosure()
    {
        var costFields = EstimateStep2.Children.OfType<Grid>().Take(3).ToArray();
        var costs = new StackPanel { Spacing = 16 };
        costs.Children.Add(EstimateHint("Saisissez le total pour toute la commande. Exemple : 80 € de finitions pour 40 pièces → saisir 80 €. Laissez 0 si rien à ajouter. La provision pour échecs est ajoutée séparément."));
        foreach (var grid in costFields) { EstimateStep2.Children.Remove(grid); costs.Children.Add(grid); }
        EstimateStep2.Children.RemoveAt(0);
        _estimateOptionalCosts = (Expander)EstimateSection("Interventions et frais", "Montants totaux pour toute la commande", costs);
        EstimateStep2.Children.Insert(0, _estimateOptionalCosts);
        EstimateStep2.Children.Insert(1, _estimateOptionalCostSummary);

        var technical = new StackPanel { Spacing = 14 };
        var rate = (Control)EstimateMachineRate.Parent!.Parent!.Parent!;
        var energy = (Control)EstimateElectricity.Parent!.Parent!;
        EstimatePrintInputs.Children.Remove(rate); EstimatePrintInputs.Children.Remove(energy);
        technical.Children.Add(rate); technical.Children.Add(energy);
        EstimatePrintInputs.Children.Add(EstimateSection("Détail machine et énergie", "Taux issu de l’imprimante et électricité calculée ou ajustable", technical));
        EstimatePrintInputs.Children.Insert(0, EstimateHint("Choisissez la matière et la machine, puis reportez le poids et la durée du slicer."));
        EstimatePrintInputs.Children.Add(_estimateProductionSummary);
        UpdateEstimateOptionalCostSummary();
    }

    private void UpdateEstimateOptionalCostSummary()
    {
        var count = new[] { EstimateDesign, EstimateLabor, EstimatePostProcessing, EstimateConsumables, EstimatePackaging, EstimateMisc }
            .Count(field => TryEstimateValue(field.Text, out var amount) && amount != 0);
        _estimateOptionalCostSummary.IsVisible = count > 0;
        _estimateOptionalCostSummary.Text = count == 0 ? "Aucun frais complémentaire renseigné. Ajoutez-en uniquement si nécessaire."
            : $"{count} poste{(count > 1 ? "s" : "")} renseigné{(count > 1 ? "s" : "")} · inclus dans le calcul, même section repliée.";
    }

    private static string EstimateMoney(decimal amount) => EstimateCalculatorV2.Money(amount).ToString("N2", EstimateCulture) + " €";

    private void UpdateEstimatePriceSummary(EstimateInputs? input = null, EstimateResult? result = null)
    {
        UpdateEstimateCostDesign(input, result);
        var p = result?.V2;
        _estimateProductionSummary.IsVisible = p is not null && input?.V2?.Production is not null;
        _estimateProductionSummary.Text = p is null || input is null ? "" : $"{input.Quantity} pièce{(input.Quantity > 1 ? "s" : "")} · {p.Runs} "
            + (input.PrintDataForWholeOrder ? (p.Runs > 1 ? "plateaux" : "plateau") : (p.Runs > 1 ? "impressions" : "impression"))
            + $" · {p.TotalWeightGrams:0.##} g · {p.TotalMachineHours:0.##} h au total, hors échecs";
        EstimatePriceSummary.IsVisible = EstimateProfitStatus.IsVisible = p is not null;
        EstimatePriceDetails.IsVisible = p is not null;
        EstimateUnitValue.IsVisible = result is not null && input is { Quantity: > 0 };
        if (result is not null && input is { Quantity: > 0 })
            EstimateUnitValue.Text = EstimateMoney(result.FinalPrice / input.Quantity) + " / pièce · " + input.Quantity + (input.Quantity == 1 ? " pièce" : " pièces");
        if (p is null) return;
        BrandMotion.Confirm(EstimateFinal, MotionReduced);
        EstimateFloorValue.Text = EstimateMoney(p.FloorPrice);
        EstimateRecommendedValue.Text = EstimateMoney(p.RecommendedPrice);
        EstimateProfitValue.Text = EstimateMoney(p.EstimatedResult);
        EstimateActualMarginValue.Text = p.ActualMarginPercent.ToString("0.00", EstimateCulture) + " %";
        EstimateProfitValue.Foreground = (IBrush?)Application.Current?.FindResource(p.EstimatedResult < 0 ? "WarningBrush" : "SuccessBrush");
        EstimateProfitStatus.Classes.Set("warning", p.BelowBreakEven);
        EstimateProfitStatusText.Text = p.BelowBreakEven ? "Sous le prix plancher : commande potentiellement déficitaire." : "Coûts et charges retenus couverts.";
    }

    private void InitializeEstimatePresentation()
    {
        TextBox[] numbers = [EstimateQuantity, EstimateWeight, EstimateHours, EstimateMachineRate, EstimateElectricity,
            EstimateDesign, EstimateLabor, EstimatePostProcessing, EstimateConsumables, EstimatePackaging, EstimateMisc, EstimateDiscount,
            _estimateCapacity, _estimateExtraGrams, _estimateSalesShare, _estimateBicShare, _estimateCommercial];
        foreach (var field in numbers) field.Classes.Add("estimate-number");
        // Keep the currency/unit suffix next to a readable label and separate each field from the next.
        foreach (var field in numbers)
        {
            if ((field.Parent is Grid grid ? grid.Parent : field.Parent) is StackPanel stack)
            {
                stack.Spacing = 6;
                if (stack.Children.FirstOrDefault() is TextBlock label)
                { label.Classes.Remove("muted"); label.Classes.Add("estimate-label"); }
            }
        }
        _estimateWeightIncludesSupports.Content = new TextBlock { Text = "Le poids du slicer inclut déjà les supports et purges", TextWrapping = TextWrapping.Wrap };
        EstimateFieldHelp.Attach(EstimateFloorValue, "FloorPrice");
        EstimateFieldHelp.Attach(EstimateRecommendedValue, "RecommendedPrice");
        EstimateFieldHelp.Attach(EstimateProfitValue, "Profit");
        SizeChanged += (_, _) => UpdateEstimateLayout();
        UpdateEstimateLayout();
        InitializeEstimateProgressiveDisclosure();
        InitializeEstimateQuickForm();
        InitializeEstimateDesign();
        InitializeEstimateSubcontracting();
        InitializeQuantityPricing();
    }

    private void UpdateEstimateLayout()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var compact = width < 1100;
        EstimateLayout.ColumnDefinitions[1].Width = new GridLength(compact ? 258 : width < 1250 ? 290 : 330);
        EstimateLayout.ColumnSpacing = compact ? 16 : 24;
        EstimateFormLayout.RowSpacing = compact ? 12 : 18;
        EstimateFormHint.IsVisible = !compact;
        if (EstimateFormLayout.Parent is Border form) form.Padding = new Thickness(compact ? 16 : 24);
        EstimatePriceRail.Padding = new Thickness(compact ? 16 : 24);
        EstimateFinal.FontSize = compact ? 32 : 42;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        if (_estimateHistoryScroll is not null) _estimateHistoryScroll.MaxHeight = height < 820 ? 112 : height < 960 ? 168 : 220;
    }

    private static Control EstimateResultRow(string label, string value, bool strong = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 18, Margin = new Thickness(0, 7) };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, LineHeight = 21, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = (IBrush?)Application.Current?.FindResource(strong ? "TextBrush" : "MutedTextBrush") });
        var amount = new TextBlock { Text = value, FontSize = strong ? 17 : 15, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
        Grid.SetColumn(amount, 1); row.Children.Add(amount);
        return row;
    }
}
