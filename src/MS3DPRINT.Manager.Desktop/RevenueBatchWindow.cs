using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
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
        public required bool Selectable { get; init; }
    }

    private static readonly (string Label, RevenueNature Nature)[] Natures = [("Prestation", RevenueNature.Services), ("Marchandises", RevenueNature.Goods)];
    private readonly List<Row> _rows = [];
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly TextBlock _count = new() { Classes = { "muted" } };
    private readonly DateOnly _today;

    public IReadOnlyList<RevenueEntry> Result { get; private set; } = [];

    public RevenueBatchWindow(IReadOnlyList<InvoiceCandidate> invoices, int unreadable, DateOnly today)
    {
        _today = today;
        Title = "Factures sans recette"; Width = 1040; Height = 720; MinWidth = 820;
        var save = new Button { Content = "Enregistrer la sélection" };
        save.Click += Save_Click;
        var all = new Button { Content = "Tout cocher", Classes = { "ghost" } };
        var none = new Button { Content = "Tout décocher", Classes = { "ghost" } };
        all.Click += (_, _) => { foreach (var row in _rows.Where(r => r.Selectable)) row.Selected.IsChecked = true; };
        none.Click += (_, _) => { foreach (var row in _rows) row.Selected.IsChecked = false; };

        var list = new StackPanel { Spacing = 0 };
        list.Children.Add(Line(null, "FACTURE", "MONTANT", "DATE D’ENCAISSEMENT", "NATURE", "RÈGLEMENT", header: true));
        foreach (var invoice in invoices)
        {
            var data = invoice.Data;
            var mixed = data.Nature == RevenueNature.Mixed;
            var row = new Row
            {
                Invoice = invoice, Selectable = !mixed,
                Selected = new CheckBox { IsEnabled = !mixed, VerticalAlignment = VerticalAlignment.Center },
                Date = new TextBox { Text = (data.IssuedOn ?? today).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), MinWidth = 120, IsEnabled = !mixed },
                Nature = new ComboBox { ItemsSource = Natures.Select(n => n.Label).ToArray(), IsEnabled = !mixed, MinWidth = 140, PlaceholderText = "À choisir" },
                Payment = new ComboBox { ItemsSource = RevenueEntryWindow.PaymentMethods, SelectedIndex = 0, IsEnabled = !mixed, MinWidth = 150 }
            };
            if (data.Nature is { } nature && nature != RevenueNature.Mixed) row.Nature.SelectedIndex = Array.FindIndex(Natures, n => n.Nature == nature);
            _rows.Add(row);
            var who = data.ClientName ?? invoice.FolderClient ?? "Client inconnu";
            var title = mixed ? $"{data.InvoiceNumber} · {who}\nFacture mixte (vente et prestation) : à saisir avec « Ajouter » pour indiquer la part marchandises."
                : $"{data.InvoiceNumber} · {who}\nÉmise le {(data.IssuedOn?.ToString("dd/MM/yyyy") ?? "date inconnue")}";
            list.Children.Add(Line(row, title, RevenueExporter.Money(data.TotalTtc ?? 0)));
        }
        _count.Text = $"{invoices.Count} facture{(invoices.Count > 1 ? "s" : "")} sans recette" + (unreadable > 0 ? $" · {unreadable} fichier{(unreadable > 1 ? "s" : "")} non lisible{(unreadable > 1 ? "s" : "")} (à saisir à la main)" : "") + ".";
        Layout("Factures sans recette",
            "Ces factures sont rangées dans votre espace de travail mais ne figurent pas dans le livre. Cochez celles qui ont été payées, vérifiez la date à laquelle l’argent est arrivé, puis enregistrez.",
            [new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { all, none, _count } }, list, _error], save);
    }

    private Control Line(Row? row, string title, string amount, string? date = null, string? nature = null, string? payment = null, bool header = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,3*,Auto,Auto,Auto,Auto"), ColumnSpacing = 14, MinHeight = header ? 32 : 64, Margin = new Avalonia.Thickness(4, 4) };
        Control Cell(Control c, int col) { Grid.SetColumn(c, col); grid.Children.Add(c); c.VerticalAlignment = VerticalAlignment.Center; return c; }
        if (row is not null) Cell(row.Selected, 0); else Cell(new TextBlock { Width = 24 }, 0);
        Cell(new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = header ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal, Classes = { header ? "faint" : "plain" } }, 1);
        Cell(new TextBlock { Text = amount, MinWidth = 90, TextAlignment = Avalonia.Media.TextAlignment.Right, Classes = { header ? "faint" : "plain" } }, 2);
        if (row is not null) { Cell(row.Date, 3); Cell(row.Nature, 4); Cell(row.Payment, 5); }
        else { Cell(new TextBlock { Text = date, MinWidth = 120, Classes = { "faint" } }, 3); Cell(new TextBlock { Text = nature, MinWidth = 140, Classes = { "faint" } }, 4); Cell(new TextBlock { Text = payment, MinWidth = 150, Classes = { "faint" } }, 5); }
        return new Border { BorderBrush = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("SubtleBorderBrush"), BorderThickness = new Avalonia.Thickness(0, 0, 0, 1), Child = grid };
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entries = new List<RevenueEntry>();
        foreach (var row in _rows.Where(r => r.Selected.IsChecked == true))
        {
            var data = row.Invoice.Data;
            var label = data.InvoiceNumber!;
            if (!DateOnly.TryParseExact(row.Date.Text?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { FormKit.ShowError(_error, row.Date, $"{label} : saisissez la date au format jj/mm/aaaa."); return; }
            if (date > _today) { FormKit.ShowError(_error, row.Date, $"{label} : la date d’encaissement ne peut pas être dans le futur."); return; }
            if (row.Nature.SelectedIndex < 0) { FormKit.ShowError(_error, row.Nature, $"{label} : choisissez la nature (prestation ou marchandises)."); return; }
            var nature = Natures[row.Nature.SelectedIndex].Nature;
            var amount = data.TotalTtc!.Value;
            entries.Add(new RevenueEntry(Guid.NewGuid(), date, "Facture " + label, amount, nature, nature == RevenueNature.Goods ? amount : 0, nature == RevenueNature.Services ? amount : 0,
                row.Payment.SelectedItem as string ?? "Autre", null, data.ClientName ?? row.Invoice.FolderClient, null, label, row.Invoice.RelativePath, DateTimeOffset.UtcNow));
        }
        if (entries.Count == 0) { FormKit.ShowError(_error, _rows.FirstOrDefault()?.Selected ?? (Control)_count, "Cochez au moins une facture."); return; }
        Result = entries;
        Close(true);
    }
}
