using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed partial class EstimateUiTests
{
    [Fact]
    public Task Expense_schedule_creates_bank_example_confirms_and_reverses_payment_without_purchase_entries() => Run(async () =>
    {
        var owner=await Open();owner.Show();
        try
        {
            Invoke(owner,"ShowWorkspace","Achats");await (Task)Invoke(owner,"LoadPurchasesAsync")!;
            owner.FindControl<Button>("PurchaseScheduleViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            owner.FindControl<Button>("CreateFinancingButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ExpensePlanWindow>(owner);
            var body=owner.FindControl<Border>("SidePanelBody")!;
            IdentityControl<TextBox>(body,"Organisme").Text="FLOA";
            IdentityControl<TextBox>(body,"Équipement ou libellé").Text="Tablette";
            IdentityControl<TextBox>(body,"Montant habituel").Text="35,08";
            IdentityControl<TextBox>(body,"Première échéance restante").Text="05/11/2026";
            IdentityControl<TextBox>(body,"Dernière échéance").Text="05/09/2028";
            IdentityControl<TextBox>(body,"Dernier montant (facultatif)").Text="34,99";
            DialogAction(owner,"Prévisualiser");
            owner.Width=960;await Task.Delay(280);CaptureDraftUi(owner,"expense-plan-form-960");
            DialogAction(owner,"Enregistrer la fiche");
            var store=new ExpensePlanStore(_root);
            for(var i=0;i<100 && store.LoadReadable().Count==0;i++)await Task.Delay(20);
            Assert.Single(store.LoadReadable());
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            var list=owner.FindControl<StackPanel>("PurchaseList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="806,75 €");
            foreach(var width in new[]{960d,1440d}){owner.Width=width;await Task.Delay(280);CaptureDraftUi(owner,"expense-plan-summary-"+width);}
            list.GetVisualDescendants().OfType<Expander>().Single(e=>e.Classes.Contains("expense-installments")).IsExpanded=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<Button>().First(b=>b.Content as string=="Confirmer le paiement").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ExpensePaymentWindow>(owner);DialogAction(owner,"Confirmer le paiement");
            for(var i=0;i<100 && store.LoadReadable().Single().Payments.Count==0;i++)await Task.Delay(20);
            Assert.Single(store.LoadReadable().Single().Payments);
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="771,67 €");
            list.GetVisualDescendants().OfType<Expander>().Single(e=>e.Classes.Contains("expense-installments")).IsExpanded=true;
            Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<Button>().First(b=>b.Content as string=="Annuler la confirmation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Confirmer le retrait");
            for(var i=0;i<100 && store.LoadReadable().Single().Payments.Count>0;i++)await Task.Delay(20);
            Assert.Empty(store.LoadReadable().Single().Payments);
            Assert.Empty(new PurchaseJournal(_root).Years());
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            void Archive() => list.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Archiver la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Archive();await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Annuler");Assert.False(store.LoadReadable().Single().IsArchived);
            Archive();await WaitForDialog<ConfirmRemovalWindow>(owner);DialogAction(owner,"Confirmer le retrait");
            for(var i=0;i<100 && !store.LoadReadable().Single().IsArchived;i++)await Task.Delay(20);
            Assert.True(store.LoadReadable().Single().IsArchived);
            await Task.Delay(100);owner.FindControl<CheckBox>("ExpenseArchivedBox")!.IsChecked=true;Dispatcher.UIThread.RunJobs();owner.UpdateLayout();
            list.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Restaurer la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(var i=0;i<100 && store.LoadReadable().Single().IsArchived;i++)await Task.Delay(20);
            Assert.False(store.LoadReadable().Single().IsArchived);
        }
        finally{CloseInvoiceTestPanel(owner);owner.Close();}
    });

    [Fact]
    public Task Expense_recurring_form_saves_forecast_and_edit_preserves_confirmed_payments() => Run(async () =>
    {
        ExpensePlan? saved=null;
        var dialog=new ExpensePlanWindow(ExpensePlanKind.Recurring,p=>{saved=p;return Task.FromResult<string?>(null);});dialog.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
            IdentityControl<TextBox>(dialog,"Organisme").Text="Assurance";IdentityControl<TextBox>(dialog,"Équipement ou libellé").Text="Atelier";
            IdentityControl<TextBox>(dialog,"Montant habituel").Text="25,90";
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Prévisualiser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.Contains("prévision jusqu’au")==true);
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Enregistrer la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(30);Assert.NotNull(saved);Assert.Null(saved.EndOn);
        }
        finally {dialog.Close(false);}
        var today=DateOnly.FromDateTime(DateTime.Today);
        var paid=ExpensePlans.ConfirmPayment(saved!,ExpensePlans.Schedule(saved!,today)[0].Id,today,today);
        var edit=new ExpensePlanWindow(ExpensePlanKind.Recurring,p=>{saved=p;return Task.FromResult<string?>(null);},paid);edit.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();edit.UpdateLayout();IdentityControl<TextBox>(edit,"Équipement ou libellé").Text="Atelier assuré";
            edit.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Prévisualiser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            edit.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Enregistrer la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(30);Assert.Single(saved!.Payments);Assert.Equal("Atelier assuré",saved.Label);
        }
        finally {edit.Close(false);}
    });

    [Fact]
    public Task Expense_bank_table_preview_rejects_invalid_rows_and_keeps_bank_components() => Run(async () =>
    {
        var saved=new List<ExpensePlan>();
        var dialog=new ExpensePlanWindow(ExpensePlanKind.Financing,p=>{saved.Add(p);return Task.FromResult<string?>(null);});
        dialog.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
            IdentityControl<TextBox>(dialog,"Organisme").Text="Banque";IdentityControl<TextBox>(dialog,"Équipement ou libellé").Text="Imprimante";
            IdentityControl<ComboBox>(dialog,"Mode de saisie").SelectedIndex=1;
            var table=IdentityControl<TextBox>(dialog,"Tableau bancaire");
            table.Text="Date\tMontant\tCapital\tIntérêts\n31/02/2026\t50\t40\t10";
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Enregistrer la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(saved);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),t=>t.IsVisible && t.Text?.Contains("Ligne 2")==true);
            table.Text="Date\tMontant\tCapital\tIntérêts\n05/11/2026\t50\t40\t10";
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Prévisualiser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="Enregistrer la fiche").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(50);
            Assert.Equal(40m,Assert.Single(saved).Installments.Single().Principal);
        }
        finally{dialog.Close(false);}
    });
}
