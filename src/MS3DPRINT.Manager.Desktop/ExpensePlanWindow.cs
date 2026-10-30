using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

public sealed class ExpensePlanWindow : DialogWindow
{
    private readonly TextBox _organization = new() { MaxLength = 250 };
    private readonly TextBox _label = new() { MaxLength = 500 };
    private readonly TextBox _amount = new() { PlaceholderText = "35,08" };
    private readonly TextBox _first = new() { PlaceholderText = "JJ/MM/AAAA" };
    private readonly TextBox _end = new() { PlaceholderText = "JJ/MM/AAAA" };
    private readonly TextBox _last = new() { PlaceholderText = "Même montant si vide" };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Fiche simple", "Tableau bancaire" }, SelectedIndex = 0 };
    private readonly ComboBox _frequency = new() { ItemsSource = new[] { "Mensuelle", "Trimestrielle", "Annuelle" }, SelectedIndex = 0 };
    private readonly TextBox _table = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 160, MaxLength = 500000, PlaceholderText = "Date;Montant;Capital;Intérêts;Assurance;Capital restant dû\n05/11/2026;35,08;30;4,08;1;776,75" };
    private readonly StackPanel _previewRows = new() { Spacing = 4 };
    private readonly TextBlock _error = FormKit.ErrorText();
    private readonly ExpensePlanKind _kind;
    private readonly ExpensePlan? _original;
    private readonly Guid _id;
    private ExpensePlan? _preview;
    private string? _previewInputs;

    public ExpensePlanWindow(ExpensePlanKind kind, Func<ExpensePlan, Task<string?>> save, ExpensePlan? original = null)
    {
        _kind = kind; _original = original; _id = original?.Id ?? Guid.NewGuid();
        Title = original is null ? kind == ExpensePlanKind.Financing ? "Ajouter un financement" : "Ajouter une charge récurrente" : "Modifier l’échéancier";
        Width = 590; Height = 820; MinWidth = 440;
        _organization.Text = original?.Organization; _label.Text = original?.Label;
        _amount.Text = original?.RegularAmount.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
        _first.Text = (original?.FirstDueOn ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd/MM/yyyy");
        _end.Text = original?.EndOn?.ToString("dd/MM/yyyy"); _last.Text = original?.LastAmount?.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"));
        _frequency.SelectedIndex = original?.IntervalMonths switch { 3 => 1, 12 => 2, _ => 0 };
        var manual = new StackPanel { Spacing = 16, Children = {
            FormKit.Field("Montant habituel", _amount, "€"), FormKit.Field("Fréquence", _frequency),
            FormKit.Field("Première échéance restante", _first, hint: "JJ/MM/AAAA. Les paiements antérieurs ne sont pas recréés."),
            FormKit.Field("Dernière échéance", _end, hint: kind == ExpensePlanKind.Financing ? "Obligatoire pour un financement." : "Facultative : vide pour une charge sans fin connue."),
            FormKit.Field("Dernier montant (facultatif)", _last, "€") } };
        var import = new Button { Content = "Importer un CSV…", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        var source = new StackPanel { Spacing = 12, IsVisible = false, Children = {
            new TextBlock { Text = "Collez une sélection Excel ou un CSV UTF-8. Date et Montant/Mensualité sont requis. Capital, Intérêts, Assurance et Capital restant dû sont facultatifs. Conservez uniquement les échéances, dans l’ordre des dates.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
            import, FormKit.Field("Tableau bancaire", _table) } };
        _mode.SelectionChanged += (_, _) => { manual.IsVisible = _mode.SelectedIndex == 0; source.IsVisible = _mode.SelectedIndex == 1; };
        import.Click += async (_, _) =>
        {
            try
            {
                var provider = TopLevel.GetTopLevel(import)?.StorageProvider; if (provider is null) return;
                var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importer le tableau bancaire", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Tableau CSV ou texte") { Patterns = ["*.csv", "*.tsv", "*.txt"] }] });
                if (files.Count == 0) return;
                await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
                var buffer = new char[500001]; var count = await reader.ReadBlockAsync(buffer.AsMemory());
                if (count > 500000) throw new ArgumentException("Le tableau est limité à 500 Ko.");
                _table.Text = new string(buffer, 0, count); Preview();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(e.Message); }
        };
        if (original is { Kind: ExpensePlanKind.Financing })
        {
            _mode.SelectedIndex = 1;
            string N(decimal? n) => n?.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")) ?? "";
            _table.Text = "Date;Montant;Capital;Intérêts;Assurance;Capital restant dû\n" + string.Join("\n", original.Installments.Select(r => $"{r.DueOn:dd/MM/yyyy};{N(r.Amount)};{N(r.Principal)};{N(r.Interest)};{N(r.Insurance)};{N(r.RemainingCapital)}"));
        }
        var preview = new Button { Content = "Prévisualiser", HorizontalAlignment = HorizontalAlignment.Left };
        preview.Click += (_, _) => Preview();
        var confirm = new Button { Content = "Enregistrer la fiche" };
        confirm.Click += async (_, _) =>
        {
            if (_preview is null || _previewInputs != Fingerprint()) { if (Preview()) ShowError("Vérifiez l’aperçu, puis cliquez de nouveau sur Enregistrer la fiche."); return; }
            confirm.IsEnabled = false;
            try { var error = await save(_preview); if (error is null) Close(true); else ShowError(error); }
            finally { confirm.IsEnabled = true; }
        };
        var modeField = FormKit.Field("Mode de saisie", _mode); modeField.IsVisible = kind == ExpensePlanKind.Financing;
        Layout(Title, "Prévisions et paiements confirmés restent séparés du registre des achats.",
            [FormKit.Field("Organisme", _organization), FormKit.Field("Équipement ou libellé", _label), modeField, manual, source, preview, _error, _previewRows], confirm);
    }

    private string Fingerprint() => string.Join("\0", _organization.Text, _label.Text, _amount.Text, _first.Text, _end.Text, _last.Text, _frequency.SelectedIndex, _mode.SelectedIndex, _table.Text);
    private void ShowError(string message)
    {
        _error.Text = message; _error.IsVisible = true;
        Dispatcher.UIThread.Post(() => _error.BringIntoView(), DispatcherPriority.Background);
    }
    private bool Preview()
    {
        _preview = null; _previewRows.Children.Clear(); _error.IsVisible = false;
        try
        {
            ExpensePlan p;
            if (_kind == ExpensePlanKind.Financing && _mode.SelectedIndex == 1)
                p = ExpensePlans.FromTable(_id, _organization.Text ?? "", _label.Text ?? "", ExpenseScheduleImport.Parse(_table.Text ?? "", _id));
            else
            {
                if (!ExpenseScheduleImport.Amount(_amount.Text, out var amount) || amount <= 0) throw new ArgumentException("Saisissez un montant habituel positif, au centime.");
                if (!ExpenseScheduleImport.Date(_first.Text, out var first)) throw new ArgumentException("Vérifiez la première échéance (JJ/MM/AAAA).");
                DateOnly? end = null; decimal? last = null;
                if (!string.IsNullOrWhiteSpace(_end.Text)) { if (!ExpenseScheduleImport.Date(_end.Text, out var date)) throw new ArgumentException("Vérifiez la dernière échéance."); end = date; }
                if (!string.IsNullOrWhiteSpace(_last.Text)) { if (!ExpenseScheduleImport.Amount(_last.Text, out var value) || value <= 0) throw new ArgumentException("Vérifiez le dernier montant."); last = value; }
                if (end is null && last is not null) throw new ArgumentException("Un dernier montant nécessite une date de fin.");
                p = ExpensePlans.Create(_id, _kind, _organization.Text ?? "", _label.Text ?? "", amount, first, _frequency.SelectedIndex switch { 1 => 3, 2 => 12, _ => 1 }, end, last);
            }
            if (_original is not null)
            {
                if (p.Installments.Select(r => r.DueOn).SequenceEqual(_original.Installments.Select(r => r.DueOn))) p = p with { IntervalMonths = _original.IntervalMonths };
                p = p with { Payments = _original.Payments, CreatedAt = _original.CreatedAt, UpdatedAt = _original.UpdatedAt, IsArchived = _original.IsArchived };
            }
            var rows = ExpensePlans.Schedule(p, DateOnly.FromDateTime(DateTime.Today));
            _previewRows.Children.Add(new TextBlock { Text = $"{rows.Count} échéances · {RevenueExporter.Money(rows.Sum(r => r.Amount))}" + (p.EndOn is null ? " · prévision jusqu’au " + rows[^1].DueOn.ToString("dd/MM/yyyy") : ""), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var list = new StackPanel { Spacing = 5 };
            PagedRows.Render(list, rows, r => new TextBlock { Text = r.DueOn.ToString("dd/MM/yyyy") + " · " + RevenueExporter.Money(r.Amount)
                + (r.Principal is { } capital ? " · capital " + RevenueExporter.Money(capital) : "") + (r.Interest is { } interest ? " · intérêts " + RevenueExporter.Money(interest) : "")
                + (r.Insurance is { } insurance ? " · assurance " + RevenueExporter.Money(insurance) : "") + (r.RemainingCapital is { } balance ? " · capital restant dû " + RevenueExporter.Money(balance) : ""), TextWrapping = TextWrapping.Wrap }, () => new TextBlock { Text = "Montants repris sans recalcul bancaire", Classes = { "caption" } });
            _previewRows.Children.Add(new Expander { Header = "Contrôler les échéances", Content = list, Classes = { "estimate-section" } });
            _preview = p; _previewInputs = Fingerprint();
            Dispatcher.UIThread.Post(() => _previewRows.BringIntoView(), DispatcherPriority.Background);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { ShowError(e.Message); return false; }
    }
}
