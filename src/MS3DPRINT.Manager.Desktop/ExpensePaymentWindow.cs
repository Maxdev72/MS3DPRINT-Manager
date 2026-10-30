using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ExpensePaymentWindow : DialogWindow
{
    public ExpensePaymentWindow(ExpensePlan plan, ExpenseInstallment term, Func<DateOnly, Task<string?>> save)
    {
        Title = "Confirmer un paiement"; Width = 500; Height = 460;
        var date = new TextBox { Text = DateTime.Today.ToString("dd/MM/yyyy") }; var error = FormKit.ErrorText();
        var confirm = new Button { Content = "Confirmer le paiement" };
        confirm.Click += async (_, _) =>
        {
            if (!ExpenseScheduleImport.Date(date.Text, out var paidOn) || paidOn > DateOnly.FromDateTime(DateTime.Today)) { FormKit.ShowError(error, date, "Saisissez la date réelle du paiement, sans date future."); return; }
            confirm.IsEnabled = false;
            try { error.Text = await save(paidOn); error.IsVisible = error.Text is not null; if (error.Text is null) Close(true); }
            finally { confirm.IsEnabled = true; }
        };
        Layout("Confirmer un paiement", plan.Organization + " · " + plan.Label,
            [new TextBlock { Text = $"Échéance du {term.DueOn:dd/MM/yyyy} · {RevenueExporter.Money(term.Amount)}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                FormKit.Field("Date réelle du paiement", date, hint: "JJ/MM/AAAA"), new TextBlock { Text = "Cette confirmation met à jour l’échéancier. Elle ne crée pas de ligne dans le registre des achats.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } }, error], confirm);
    }
}
