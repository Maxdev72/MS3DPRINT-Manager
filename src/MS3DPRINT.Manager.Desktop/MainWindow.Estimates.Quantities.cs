using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<QuantityPriceTier>? _estimateQuantityTiers;
    private readonly Button _estimateQuantityButton=new() {Content="Tarifs par quantité",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};

    private void InitializeQuantityPricing()
    {
        EstimateV2CommercialHost.Children.Add(_estimateQuantityButton);
        _estimateQuantityButton.Click+=async(_,_)=>
        {
            if(!IsEstimateV2){Notify("Convertissez ce devis pour utiliser les tarifs par quantité.",error:true);return;}
            if(EstimateScopeIndex==1){Notify("Les tarifs par quantité concernent les pièces imprimées.");return;}
            if(!CalculateEstimate() || _latestEstimateInputs is not { } input)return;
            var root=WorkspaceRoot;var navigation=CurrentNavigationStop?.Key;var draft=_estimateDraftId;
            await new QuantityPricingWindow(input,_estimateQuantityTiers??QuantityPricing.Defaults(),tiers=>
            {
                if(!MatchesNavigationContext(root,navigation)||_estimateFormRoot!=root||_estimateDraftId!=draft)
                    return "Le devis ou l’atelier a changé. Rouvrez la grille depuis le devis concerné.";
                _estimateQuantityTiers=tiers.ToArray();EstimateChanged();return null;
            }).ShowDialog<bool>(this);
        };
    }
}
