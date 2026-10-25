using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private readonly CheckBox _estimateOutsource = new() { Content = "Fabrication confiée à un fournisseur" };
    private readonly TextBox _estimateSupplier = new() { PlaceholderText = "Nom du fournisseur (facultatif)" };
    private readonly TextBox _estimateSupplierCost = Number(""), _estimateSupplierShipping = Number("0"), _estimateSupplierQuantity = Number("1");
    private readonly StackPanel _estimateSupplierInputs = new() { Spacing = 14, IsVisible = false };
    private bool IsEstimateOutsourced => IsEstimateV2 && EstimateScopeIndex != 1 && _estimateOutsource.IsChecked == true;

    private void InitializeEstimateSubcontracting()
    {
        var position = EstimateStep1.Children.IndexOf(EstimateScope);
        EstimateStep1.Children.Insert(position + 1, _estimateOutsource);
        EstimateStep1.Children.Insert(position + 2, _estimateSupplierInputs);
        _estimateSupplierQuantity.Bind(TextBox.TextProperty, new Binding(nameof(TextBox.Text)) { Source = EstimateQuantity, Mode = BindingMode.TwoWay });
        _estimateSupplierInputs.Children.Add(EstimateHint("Reportez le devis fournisseur pour toute la commande. Il remplace vos coûts d’impression internes, sans ajouter leurs aléas. Si le transport est déjà inclus dans l’achat, laissez-le à 0."));
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 12 };
        identity.Children.Add(EstimateField("Fournisseur (facultatif)", _estimateSupplier));
        var quantity = EstimateField("Quantité", _estimateSupplierQuantity, "pièces"); Grid.SetColumn(quantity, 1); identity.Children.Add(quantity);
        var costs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        costs.Children.Add(EstimateField("Coût d’achat total", _estimateSupplierCost, "€"));
        var shipping = EstimateField("Transport fournisseur", _estimateSupplierShipping, "€"); Grid.SetColumn(shipping, 1); costs.Children.Add(shipping);
        _estimateSupplierInputs.Children.Add(identity); _estimateSupplierInputs.Children.Add(costs);
        _estimateSupplierInputs.Children.Add(EstimateHint("Votre préparation, vos finitions et la livraison au client se renseignent dans « Interventions et frais ». Ne comptez chaque dépense qu’une fois."));
        foreach (var field in new[] { _estimateSupplierCost, _estimateSupplierShipping, _estimateSupplierQuantity }) field.Classes.Add("estimate-number");
        _estimateOutsource.IsCheckedChanged += (_, _) => { UpdateEstimateQuickContext(); EstimateChanged(); };
        foreach (var field in new[] { _estimateSupplier, _estimateSupplierCost, _estimateSupplierShipping })
            field.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) EstimateChanged(); };
        UpdateEstimateQuickContext();
    }

    private void RestoreEstimateSubcontracting(EstimateSubcontracting? subcontracting)
    {
        _estimateSupplier.Text = subcontracting?.Supplier ?? "";
        _estimateSupplierCost.Text = subcontracting is null ? "" : Exact(subcontracting.PurchaseCost);
        _estimateSupplierShipping.Text = Exact(subcontracting?.ShippingCost ?? 0);
        _estimateOutsource.IsChecked = subcontracting is not null;
    }

    private EstimateSubcontracting? ReadEstimateSubcontracting() => !IsEstimateOutsourced ? null
        : new(EstimateNumber(_estimateSupplierCost), EstimateNumber(_estimateSupplierShipping), _estimateSupplier.Text?.Trim());
}
