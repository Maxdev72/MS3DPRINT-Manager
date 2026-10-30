using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

public sealed class QuantityPricingWindow : DialogWindow
{
    private readonly List<(int Quantity,TextBox Discount,TextBlock Price,TextBlock Detail)> _rows=[];
    private readonly TextBlock _error=FormKit.ErrorText();
    private readonly TextBlock _reference=new() {TextWrapping=TextWrapping.Wrap,FontWeight=FontWeight.SemiBold};
    private readonly EstimateInputs _input;
    private IReadOnlyList<QuantityPriceResult>? _results;
    private static readonly CultureInfo Culture=CultureInfo.GetCultureInfo("fr-FR");
    private static string Money(decimal value)=>value.ToString("N2",Culture)+" €";

    public QuantityPricingWindow(EstimateInputs input,IReadOnlyList<QuantityPriceTier> tiers,Func<IReadOnlyList<QuantityPriceTier>,string?> save)
    {
        _input=input;Title="Tarifs par quantité";Width=600;Height=820;
        var body=new List<Control>{_reference,new TextBlock {Text="Remises appliquées une seule fois au tarif de référence, sans cumuler la remise du devis. Les prix du devis en cours restent inchangés.",TextWrapping=TextWrapping.Wrap,Classes={"muted"}}};
        var gridRows=new StackPanel();body.Add(gridRows);
        foreach(var quantity in new[]{1,5,10,20,50})
        {
            var discount=new TextBox {Text=(tiers.FirstOrDefault(t=>t.Quantity==quantity)?.DiscountPercent??QuantityPricing.Defaults().Single(t=>t.Quantity==quantity).DiscountPercent).ToString("0.##",Culture),Width=85,HorizontalAlignment=HorizontalAlignment.Right};
            AutomationProperties.SetName(discount,$"Remise pour {quantity} pièce{(quantity==1?"":"s")}");
            discount.Width=68;
            var label=new TextBlock {Text=$"{quantity} pièce{(quantity==1?"":"s")}",FontSize=14,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center};
            var percent=new TextBlock {Text="%",VerticalAlignment=VerticalAlignment.Center};
            var heading=new Grid {ColumnDefinitions=new("70,*,Auto,Auto"),ColumnSpacing=8};heading.Children.Add(label);Grid.SetColumn(discount,2);heading.Children.Add(discount);Grid.SetColumn(percent,3);heading.Children.Add(percent);
            var price=new TextBlock {FontSize=16,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
            var detail=new TextBlock {TextWrapping=TextWrapping.Wrap,Classes={"caption"}};
            var prices=new StackPanel {Spacing=4,VerticalAlignment=VerticalAlignment.Center,Children={price,detail}};Grid.SetColumn(prices,1);heading.Children.Add(prices);
            gridRows.Children.Add(new Border {BorderBrush=(IBrush?)Application.Current?.FindResource("SubtleBorderBrush"),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(0,10),Child=heading});
            _rows.Add((quantity,discount,price,detail));discount.TextChanged+=(_,_)=>Refresh();
        }
        body.Add(new TextBlock {Text="Coûts et marges projetés : frais par commande conservés, frais par pièce adaptés. Avec une capacité renseignée, les impressions sont comptées par plateaux complets ; sinon poids et durée sont extrapolés. Vérifiez le slicer ou le devis fournisseur avant de vous engager.",TextWrapping=TextWrapping.Wrap,Classes={"caption"}});
        body.Add(_error);
        var copy=new Button {Content="Copier la grille",HorizontalAlignment=HorizontalAlignment.Left};
        copy.Click+=async(_,_)=>
        {
            if(!Refresh(revealError:true))return;
            try
            {
                var clipboard=TopLevel.GetTopLevel(copy)?.Clipboard;
                if(clipboard is null){ShowError("Le presse-papiers est indisponible.");return;}
                var text="Quantité\tPrix unitaire\tRemise\tTotal"+Environment.NewLine+string.Join(Environment.NewLine,_results!.Select(r=>$"{r.Tier.Quantity}\t{Money(r.UnitPrice)}\t{r.Tier.DiscountPercent.ToString("0.##",Culture)} %\t{Money(r.TotalPrice)}"));
                await clipboard.SetTextAsync(text);copy.Content="Grille copiée";
            }
            catch(Exception e) when(e is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException){ShowError("Copie impossible : "+e.Message);}
        };
        body.Add(copy);
        var confirm=new Button {Content="Conserver les remises"};
        confirm.Click+=(_,_)=>{if(!Refresh(revealError:true))return;var error=save(_results!.Select(r=>r.Tier).ToArray());if(error is null)Close(true);else ShowError(error);};
        Layout(Title,"Modifiez les pourcentages ; les prix et la marge se mettent à jour.",body,confirm);Refresh();
    }

    private void ShowError(string message,bool reveal=true)
    {
        _error.Text=message;_error.IsVisible=true;
        if(reveal)Dispatcher.UIThread.Post(()=>_error.BringIntoView(),DispatcherPriority.Background);
    }
    private bool Refresh(bool revealError=false)
    {
        _results=null;_error.IsVisible=false;
        foreach(var row in _rows){row.Price.Text="—";row.Detail.Text="";}
        try
        {
            var tiers=_rows.Select(row=>decimal.TryParse(row.Discount.Text?.Replace(',', '.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var discount)
                ? new QuantityPriceTier(row.Quantity,discount):throw new ArgumentException("Saisissez une remise de 0 à 100 %.")).ToArray();
            _results=QuantityPricing.Calculate(_input,tiers);
            _reference.Text="Référence avant remise : "+Money(_results[0].ReferenceUnitPrice)+" / pièce";
            for(var i=0;i<_rows.Count;i++)
            {
                var r=_results[i];var row=_rows[i];row.Price.Text=Money(r.UnitPrice)+" / pièce · total "+Money(r.TotalPrice);
                row.Detail.Text="Marge estimée : "+r.MarginPercent.ToString("0.00",Culture)+" % · résultat "+Money(r.Profit)+(r.BelowFloor?" · Sous le prix plancher":"");
                row.Detail.Foreground=(IBrush?)Application.Current?.FindResource(r.BelowFloor?"WarningBrush":"MutedTextBrush");
            }
            return true;
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or OverflowException){ShowError(e.Message,revealError);return false;}
    }
}
