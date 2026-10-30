using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using MS3DPRINT.Manager.Core.Filaments;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Invoices filed in the workspace that have no revenue line yet. Nothing is recorded by default: the user ticks the invoices that were
/// actually paid and confirms the collection date, the nature and the payment method of each.
/// </summary>
public sealed class RevenueBatchWindow : DialogWindow
{
    private sealed class Row
    {
        public required InvoiceCandidate Invoice { get; init; }
        public required CheckBox Selected { get; init; }
        public required TextBox Date { get; init; }
        public required ComboBox Nature { get; init; }
        public required ComboBox Payment { get; init; }
        public TextBox Goods { get; } = new() { PlaceholderText = "Montant en euros" };
        public TextBlock Services { get; } = new() { FontSize = 16, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        public TextBlock Error { get; } = FormKit.ErrorText();
    }

    private static readonly (string Label, RevenueNature Nature)[] Natures = [("Prestation", RevenueNature.Services), ("Marchandises", RevenueNature.Goods), ("Mixte", RevenueNature.Mixed)];
    private readonly List<Row> _rows = [];
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly TextBlock _count = new() { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 320 };
    private readonly DateOnly _today;

    public IReadOnlyList<RevenueEntry> Result { get; private set; } = [];

    public RevenueBatchWindow(IReadOnlyList<InvoiceCandidate> invoices, int unreadable, DateOnly today)
    {
        _today = today;
        Title = "Factures sans recette"; Width = 640; Height = 760; MinWidth = 440;
        var save = new Button { Content = "Enregistrer la sélection" };
        save.Click += Save_Click;
        var all = new Button { Content = "Tout cocher", Classes = { "ghost" } };
        var none = new Button { Content = "Tout décocher", Classes = { "ghost" } };
        all.Click += (_, _) => { foreach (var row in _rows) row.Selected.IsChecked = true; };
        none.Click += (_, _) => { foreach (var row in _rows) row.Selected.IsChecked = false; };

        var list = new StackPanel { Spacing = 0 };
        foreach (var invoice in invoices)
        {
            var data = invoice.Data;
            var mixed = data.Nature == RevenueNature.Mixed;
            var row = new Row
            {
                Invoice = invoice,
                Selected = new CheckBox { VerticalAlignment = VerticalAlignment.Center },
                Date = new TextBox { Text = (data.IssuedOn ?? today).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), MinWidth = 120 },
                Nature = new ComboBox { ItemsSource = Natures.Select(n => n.Label).ToArray(), MinWidth = 140, PlaceholderText = "À choisir" },
                Payment = new ComboBox { ItemsSource = RevenueEntryWindow.PaymentMethods, SelectedIndex = 0, MinWidth = 150 }
            };
            if (data.Nature is { } nature) row.Nature.SelectedIndex = Array.FindIndex(Natures, n => n.Nature == nature);
            _rows.Add(row);
            var who = data.ClientName ?? invoice.FolderClient ?? "Client inconnu";
            var title = mixed ? $"{data.InvoiceNumber} · {who}\nFacture mixte : indiquez la part marchandises ci-dessous."
                : $"{data.InvoiceNumber} · {who}\nÉmise le {(data.IssuedOn?.ToString("dd/MM/yyyy") ?? "date inconnue")}";
            list.Children.Add(Line(row, title, RevenueExporter.Money(data.TotalTtc ?? 0)));
        }
        _count.Text = $"{invoices.Count} facture{(invoices.Count > 1 ? "s" : "")} sans recette" + (unreadable > 0 ? $" · {unreadable} fichier{(unreadable > 1 ? "s" : "")} non lisible{(unreadable > 1 ? "s" : "")} (à saisir à la main)" : "") + ".";
        Layout("Factures sans recette",
            "Ces factures sont rangées dans votre espace de travail mais ne figurent pas dans le livre. Cochez celles qui ont été payées, vérifiez la date à laquelle l’argent est arrivé, puis enregistrez.",
            [new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8, Children = { all, none, _count } }, list, _error], save);
    }

    private Control Line(Row row, string title, string amount)
    {
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        Avalonia.Automation.AutomationProperties.SetName(row.Selected, "Sélectionner la facture " + row.Invoice.Data.InvoiceNumber);
        heading.Children.Add(row.Selected);
        var identity = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
        Grid.SetColumn(identity, 1); heading.Children.Add(identity);
        var preview = new Button { Content = Icons.Label("IconOpen", "Voir la facture"), Classes = { "ghost" } };
        var previewError = FormKit.ErrorText();
        Avalonia.Automation.AutomationProperties.SetName(preview, "Voir la facture " + row.Invoice.Data.InvoiceNumber);
        ToolTip.SetTip(preview, row.Invoice.FilePath);
        preview.Click += (_, _) => InvoicePreviewUi.Open(row.Invoice, preview, previewError);
        var document = new StackPanel { Spacing = 6, Children =
        {
            new TextBlock { Text = amount, FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right }, preview
        } };
        Grid.SetColumn(document, 2); heading.Children.Add(document);
        var fields = new WrapPanel { ItemSpacing = 12, LineSpacing = 10 };
        void Field(string label, Control control, double width)
        {
            var field = FormKit.Field(label, control); field.Width = width; fields.Children.Add(field);
        }
        Field("Date d’encaissement", row.Date, 140);
        Field("Nature", row.Nature, 150);
        Field("Règlement", row.Payment, 160);
        var goods = FormKit.Field("Part marchandises", row.Goods, "€", "Le reste est compté en prestations."); goods.Width = 220;
        var services = FormKit.Field("Part prestations calculée", row.Services); services.Width = 220;
        var split = new WrapPanel { ItemSpacing = 16, LineSpacing = 10, Children = { goods, services } };
        void UpdateSplit()
        {
            split.IsVisible = row.Nature.SelectedIndex >= 0 && Natures[row.Nature.SelectedIndex].Nature == RevenueNature.Mixed;
            row.Services.Text = TryReadMixedGoods(row, out var part) ? RevenueExporter.Money(row.Invoice.Data.TotalTtc!.Value - part) : "À calculer";
        }
        row.Goods.TextChanged += (_, _) => UpdateSplit();
        row.Nature.SelectionChanged += (_, _) => { FormKit.ClearError(row.Error); UpdateSplit(); };
        row.Selected.IsCheckedChanged += (_, _) => { if (row.Selected.IsChecked != true) FormKit.ClearError(row.Error); };
        UpdateSplit();
        return new Border
        {
            BorderBrush = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Padding = new Avalonia.Thickness(0, 14),
            Child = new StackPanel { Spacing = 12, Children = { heading, previewError, fields, split, row.Error } }
        };
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entries = new List<RevenueEntry>();
        foreach (var row in _rows) FormKit.ClearError(row.Error);
        foreach (var row in _rows.Where(r => r.Selected.IsChecked == true))
        {
            var data = row.Invoice.Data;
            var label = data.InvoiceNumber!;
            if (!DateOnly.TryParseExact(row.Date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { FormKit.ShowError(row.Error, row.Date, $"{label} : saisissez la date au format jj/mm/aaaa."); return; }
            if (date > _today) { FormKit.ShowError(row.Error, row.Date, $"{label} : la date d’encaissement ne peut pas être dans le futur."); return; }
            if (row.Nature.SelectedIndex < 0) { FormKit.ShowError(row.Error, row.Nature, $"{label} : choisissez la nature (prestation, marchandises ou mixte)."); return; }
            var nature = Natures[row.Nature.SelectedIndex].Nature;
            var amount = data.TotalTtc!.Value;
            decimal goods = nature == RevenueNature.Goods ? amount : 0;
            if (nature == RevenueNature.Mixed && !TryReadMixedGoods(row, out goods))
            {
                FormKit.ShowError(row.Error, row.Goods, $"{label} : la part marchandises doit être positive et inférieure au montant total, après arrondi au centime.");
                return;
            }
            var services = amount - goods;
            entries.Add(new RevenueEntry(Guid.NewGuid(), date, "Facture " + label, amount, nature, goods, services,
                row.Payment.SelectedItem as string ?? "Autre", null, data.ClientName ?? row.Invoice.FolderClient, null, label, row.Invoice.RelativePath, DateTimeOffset.UtcNow));
        }
        if (entries.Count == 0) { FormKit.ShowError(_error, _rows.FirstOrDefault()?.Selected ?? (Control)_count, "Cochez au moins une facture."); return; }
        Result = entries;
        Close(true);
    }

    private static bool TryReadMixedGoods(Row row, out decimal goods)
    {
        if (!FilamentPrice.TryParse(row.Goods.Text, out goods)) return false;
        goods = Math.Round(goods, 2, MidpointRounding.AwayFromZero);
        return goods > 0 && goods < row.Invoice.Data.TotalTtc!.Value;
    }
}
