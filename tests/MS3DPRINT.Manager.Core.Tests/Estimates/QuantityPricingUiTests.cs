using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Quantity_grid_defaults_edit_validate_and_survive_draft_reopening() => Run(async () =>
    {
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Estimations");
            Field<TextBox>(owner,"_estimateCommercial").Text="100";Text(owner,"EstimateQuantity","1");
            Assert.True((bool)Invoke(owner,"CalculateEstimate")!);
            Field<Button>(owner,"_estimateQuantityButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<QuantityPricingWindow>(owner);
            var panel=owner.FindControl<Border>("SidePanelBody")!;
            Assert.Equal("25",IdentityControl<TextBox>(panel,"Remise pour 50 pièces").Text);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.Contains("75,00 € / pièce")==true);
            IdentityControl<TextBox>(panel,"Remise pour 5 pièces").Text="101";
            DialogAction(owner,"Conserver les remises");
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>t.IsVisible&&t.Text?.Contains("0 à 100")==true);
            IdentityControl<TextBox>(panel,"Remise pour 5 pièces").Text="6,5";
            foreach(var width in new[]{960d,1440d}){owner.Width=width;await Task.Delay(220);CaptureDraftUi(owner,"quantity-prices-"+width);}
            DialogAction(owner,"Conserver les remises");await Task.Delay(70);
            Assert.True((bool)Invoke(owner,"CalculateEstimate")!);
            Assert.Equal(6.5m,Field<EstimateInputs>(owner,"_latestEstimateInputs").V2!.QuantityTiers![1].DiscountPercent);
            Assert.Equal("1",owner.FindControl<TextBox>("EstimateQuantity")!.Text);
            Assert.Equal("100",Field<TextBox>(owner,"_estimateCommercial").Text);
            Field<Button>(owner,"_estimateQuantityButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<QuantityPricingWindow>(owner);
            IdentityControl<TextBox>(owner.FindControl<Border>("SidePanelBody")!,"Remise pour 50 pièces").Text="0";
            DialogAction(owner,"Annuler");
            Assert.True((bool)Invoke(owner,"CalculateEstimate")!);
            Assert.Equal(25m,Field<EstimateInputs>(owner,"_latestEstimateInputs").V2!.QuantityTiers![4].DiscountPercent);
        }
        finally {CloseInvoiceTestPanel(owner);owner.Close();}
        var reopened=await Open();
        try
        {
            Assert.True((bool)Invoke(reopened,"CalculateEstimate")!);
            Assert.Equal(6.5m,Field<EstimateInputs>(reopened,"_latestEstimateInputs").V2!.QuantityTiers![1].DiscountPercent);
            Invoke(reopened,"SaveEstimate_Click",null,new RoutedEventArgs());
            for(var i=0;i<100&&new EstimateStore(_root).LoadAll().Count==0;i++)await Task.Delay(20);
            var quote=Assert.Single(new EstimateStore(_root).LoadAll());
            Assert.Equal(6.5m,quote.Inputs.V2!.QuantityTiers![1].DiscountPercent);
            Assert.True((bool)Invoke(reopened,"TryLoadEstimate",quote)!);
            Assert.True((bool)Invoke(reopened,"CalculateEstimate")!);
            Assert.Equal(6.5m,Field<EstimateInputs>(reopened,"_latestEstimateInputs").V2!.QuantityTiers![1].DiscountPercent);
        }
        finally{reopened.Close();}
    });
}
