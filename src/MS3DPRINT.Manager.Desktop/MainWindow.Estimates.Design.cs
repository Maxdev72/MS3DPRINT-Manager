using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private decimal[]? _estimateLastCostAmounts;

    private void InitializeEstimateDesign()
    {
        EstimateFinal.FontSize = 42;
        UpdateEstimateDesignContext();
    }

    private void UpdateEstimateDesignContext()
    {
        EstimateEditorTitle.Text = "Préparer votre devis";
        EstimateFormHint.Text = IsEstimateOutsourced
            ? "Reportez l’achat fournisseur : votre prix de revente se calcule ici."
            : "Reportez le poids et la durée du slicer : le prix s’actualise automatiquement.";
    }

    private void MarkEstimatePricePending()
    {
        if (EstimateScopeIndex != 1 && (!int.TryParse(EstimateQuantity.Text, out var quantity) || quantity < 1))
        {
            EstimateFinal.Text = "—";
            UpdateEstimatePriceSummary();
            return;
        }
        // Keep the last valid amounts readable during the short debounce; they are explicitly marked as stale.
        EstimateCalculationState.IsVisible = true;
        EstimateProfitStatus.IsVisible = false;
        EstimateCostBreakdown.Opacity = .6;
    }

    private void UpdateEstimateCostDesign(EstimateInputs? input, EstimateResult? result)
    {
        EstimateCalculationState.IsVisible = false;
        EstimateCostBreakdown.Opacity = 1;
        EstimateCostBreakdown.IsVisible = result?.V2 is not null;
        if (MotionReduced) EstimateCostStrip.Transitions = null;
        if (result?.V2 is not { } costs || input is null) return;
        EstimateCostTotal.Text = EstimateMoney(result.ExpectedCost);
        decimal[] amounts = [costs.MaterialCost, costs.MachineCost, costs.ElectricityCost, costs.LaborCost, costs.OtherCost, costs.FailureProvision];
        string[] labels = ["Matière", "Machine", "Électricité", "Travail", "Autres frais", "Échecs prévus"];
        string[] brushes = ["CyanBrush", "AccentBrush", "WarningBrush", "BrandBlueBrush", "FaintTextBrush", "DangerBrush"];
        if (input.V2?.Subcontracting is not null)
        {
            amounts = [costs.SubcontractingCost, costs.LaborCost, costs.OtherCost];
            labels = ["Fournisseur et transport", "Votre travail", "Autres frais"];
            brushes = ["SuccessBrush", "BrandBlueBrush", "FaintTextBrush"];
        }
        var changed = _estimateLastCostAmounts is null || !_estimateLastCostAmounts.SequenceEqual(amounts);
        _estimateLastCostAmounts = amounts;
        EstimateCostStrip.Children.Clear(); EstimateCostStrip.ColumnDefinitions.Clear(); EstimateCostLegend.Children.Clear();
        EstimateCostStrip.Background = (IBrush?)Application.Current?.FindResource("BorderBrush");
        var segments = new List<Border>();
        for (var index = 0; index < amounts.Length; index++)
        {
            var amount = amounts[index];
            var brush = (IBrush?)Application.Current?.FindResource(brushes[index]);
            // Normalize before converting decimal to double. Zero-cost orders never divide by zero.
            var share = result.ExpectedCost > 0 ? (double)(amount / result.ExpectedCost) : 0;
            EstimateCostStrip.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(share, GridUnitType.Star)));
            var segment = new Border { Background = brush };
            Grid.SetColumn(segment, index); EstimateCostStrip.Children.Add(segment); segments.Add(segment);
            ToolTip.SetTip(segment, labels[index] + " · " + EstimateMoney(amount));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(2), Background = brush, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = labels[index], FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
            var value = new TextBlock { Text = EstimateMoney(amount), FontSize = 13, MaxWidth = 160, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(label, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(value);
            var selected = index;
            row.PointerEntered += (_, _) => { for (var i = 0; i < segments.Count; i++) segments[i].Opacity = i == selected ? 1 : .3; };
            row.PointerExited += (_, _) => { foreach (var bar in segments) bar.Opacity = 1; };
            EstimateCostLegend.Children.Add(row);
        }
        if (changed)
        {
            EstimateCostStrip.Transitions = null;
            EstimateCostStrip.RenderTransformOrigin = new RelativePoint(0, .5, RelativeUnit.Relative);
            EstimateCostStrip.RenderTransform = TransformOperations.Parse("scaleX(1)");
            if (!MotionReduced)
            {
                EstimateCostStrip.RenderTransform = TransformOperations.Parse("scaleX(0.85)");
                EstimateCostStrip.Transitions = BrandMotion.Movement(240);
                EstimateCostStrip.RenderTransform = TransformOperations.Parse("scaleX(1)");
            }
        }
    }

}
