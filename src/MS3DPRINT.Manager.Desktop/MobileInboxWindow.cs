using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Purchases noted on the phone and waiting in the exchange folder. The user confirms what is recorded in the register.</summary>
public sealed class MobileInboxWindow : DialogWindow
{
    private readonly List<(CheckBox Box, MobileInboxItem Item)> _rows = [];
    private readonly TextBlock _error = FormKit.ErrorText();

    public IReadOnlyList<MobileInboxItem> Selected { get; private set; } = [];

    public MobileInboxWindow(IReadOnlyList<MobileInboxItem> items, IReadOnlyList<string> problems)
    {
        Title = "Saisies du téléphone"; Width = 720; Height = 640; MinWidth = 520;
        var import = new Button { Content = "Importer la sélection" };
        import.Click += (_, _) =>
        {
            Selected = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item).ToArray();
            if (Selected.Count == 0) { FormKit.ShowError(_error, _rows.FirstOrDefault().Box ?? (Control)import, "Cochez au moins une saisie."); return; }
            Close(true);
        };
        var list = new StackPanel { Spacing = 0 };
        foreach (var item in items)
        {
            var p = item.Purchase;
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            _rows.Add((box, item));
            var text = new StackPanel { Spacing = 3, Children =
            {
                new TextBlock { Text = $"{p.PurchasedOn:dd/MM/yyyy} · {p.Supplier} · {RevenueExporter.Money(p.Amount)}", FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"{p.Label} · {p.PaymentMethod}" + (string.IsNullOrWhiteSpace(p.ProjectRef) ? "" : $" · projet {p.ProjectRef}") + (item.PhotoPath is null ? " · sans photo" : " · photo jointe"), Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, MinHeight = 60, Margin = new Avalonia.Thickness(4), Children = { box, text } };
            Grid.SetColumn(text, 1);
            list.Children.Add(new Border { BorderBrush = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = grid });
        }
        var body = new List<Control> { list, _error };
        if (problems.Count > 0)
            body.Add(new TextBlock { Text = "Fichiers non importables pour l’instant :\n" + string.Join("\n", problems), Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Layout("Saisies du téléphone", $"{items.Count} achat{(items.Count > 1 ? "s" : "")} noté{(items.Count > 1 ? "s" : "")} sur le téléphone. Vérifiez puis importez : chaque ligne entre dans le registre des achats avec sa photo comme justificatif.", body, import);
    }
}
