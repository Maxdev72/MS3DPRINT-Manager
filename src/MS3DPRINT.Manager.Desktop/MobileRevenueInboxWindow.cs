using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Collections noted on the phone and waiting in the exchange folder; the user confirms what enters the revenue book.</summary>
public sealed class MobileRevenueInboxWindow : DialogWindow
{
    private readonly List<(CheckBox Box, MobileRevenueItem Item)> _rows = [];
    private readonly TextBlock _error = FormKit.ErrorText();

    public IReadOnlyList<MobileRevenueItem> Selected { get; private set; } = [];

    public MobileRevenueInboxWindow(IReadOnlyList<MobileRevenueItem> items, IReadOnlyList<string> problems)
    {
        Title = "Recettes du téléphone"; Width = 720; Height = 640; MinWidth = 520;
        var import = new Button { Content = "Importer la sélection" };
        import.Click += (_, _) =>
        {
            Selected = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item).ToArray();
            if (Selected.Count == 0) { FormKit.ShowError(_error, _rows.FirstOrDefault().Box ?? (Control)import, "Cochez au moins une recette."); return; }
            Close(true);
        };
        var list = new StackPanel { Spacing = 0 };
        foreach (var item in items)
        {
            var r = item.Revenue;
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            _rows.Add((box, item));
            var nature = RevenueExporter.NatureName(r.Nature);
            var text = new StackPanel { Spacing = 3, Children =
            {
                new TextBlock { Text = $"{r.ReceivedOn:dd/MM/yyyy} · {r.Label} · {RevenueExporter.Money(r.Amount)}", FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"{nature} · {r.PaymentMethod}" + (string.IsNullOrWhiteSpace(r.ClientName) ? "" : $" · {r.ClientName}") + (string.IsNullOrWhiteSpace(r.InvoiceNumber) ? "" : $" · pièce {r.InvoiceNumber}")
                    + (string.IsNullOrWhiteSpace(r.ProjectRef) ? "" : $" · projet {r.ProjectRef}") + (item.PhotoPath is null ? "" : " · photo jointe"), Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, MinHeight = 60, Margin = new Avalonia.Thickness(4), Children = { box, text } };
            Grid.SetColumn(text, 1);
            list.Children.Add(new Border { BorderBrush = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = grid });
        }
        var body = new List<Control> { list, _error };
        if (problems.Count > 0)
            body.Add(new TextBlock { Text = "Fichiers non importables pour l’instant :\n" + string.Join("\n", problems), Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Layout("Recettes du téléphone", $"{items.Count} recette{(items.Count > 1 ? "s" : "")} notée{(items.Count > 1 ? "s" : "")} sur le téléphone. Vérifiez la date d’encaissement et le montant, puis importez : chaque ligne entre dans le livre des recettes.", body, import);
    }
}
