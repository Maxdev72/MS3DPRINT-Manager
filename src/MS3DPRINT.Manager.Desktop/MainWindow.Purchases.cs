using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Mobile;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Purchase register page: chronological purchases, traceable cancellation, exports.</summary>
public partial class MainWindow
{
    private readonly TextBlock _purchasePageSummary = WorkspaceSummary();
    private bool _populatingPurchaseYears;
    private int _purchaseYear = DateTime.Today.Year;

    private const string PurchaseRowColumns = "90,1.2*,1.8*,110,120,120,64";

    private async void RefreshPurchases_Click(object? sender, RoutedEventArgs e) => await LoadPurchasesAsync();

    private async void PurchaseYear_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingPurchaseYears || PurchaseYearBox.SelectedItem is not int year || year == _purchaseYear) return;
        _purchaseYear = year;
        await LoadPurchasesAsync();
    }

    private async void CreatePurchase_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        string[] suppliers;
        try
        {
            suppliers = await Task.Run(() =>
            {
                var journal = new PurchaseJournal(root);
                return journal.Years().SelectMany(y => journal.Load(y).Lines).Select(l => l.Entry.Supplier).Distinct(StringComparer.CurrentCultureIgnoreCase).Order().ToArray();
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { suppliers = []; }
        RefreshReadingContext(root, null, suppliers);
        var dialog = new PurchaseEntryWindow(suppliers, DateOnly.FromDateTime(DateTime.Today));
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } entry) return;
        try
        {
            var journal = new PurchaseJournal(root);
            if (dialog.SourceDocumentPath is { } source)
            {
                var stored = await Task.Run(() => RevenueInvoiceArchive.StorePurchase(root, source, entry.Supplier, entry.PurchasedOn.Year));
                entry = entry with { DocumentPath = stored };
            }
            var duplicate = await Task.Run(() => journal.IsReferenceKnown(entry.Supplier, entry.Reference));
            await Task.Run(() => journal.Append(entry));
            _purchaseYear = entry.PurchasedOn.Year;
            await LoadPurchasesAsync();
            Notify(duplicate ? $"Achat enregistré. Attention : la pièce {entry.Reference} de {entry.Supplier} figure déjà dans le registre." : "Achat enregistré.", error: duplicate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void CorrectPurchase_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PurchaseEntry entry }) return;
        var root = WorkspaceRoot;
        string[] suppliers;
        try
        {
            suppliers = await Task.Run(() =>
            {
                var journal = new PurchaseJournal(root);
                return journal.Years().SelectMany(y => journal.Load(y).Lines).Select(l => l.Entry.Supplier).Distinct(StringComparer.CurrentCultureIgnoreCase).Order().ToArray();
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { suppliers = []; }
        RefreshReadingContext(root, null, suppliers);
        var dialog = new PurchaseEntryWindow(suppliers, DateOnly.FromDateTime(DateTime.Today), entry);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } corrected) return;
        try
        {
            var reason = dialog.CorrectionReason;
            var source = dialog.SourceDocumentPath ?? RevenueInvoiceArchive.Resolve(root, corrected.DocumentPath);
            if (source is not null)
            {
                var stored = await Task.Run(() => RevenueInvoiceArchive.StorePurchase(root, source, corrected.Supplier, corrected.PurchasedOn.Year));
                corrected = corrected with { DocumentPath = stored };
            }
            await Task.Run(() => new PurchaseJournal(root).Correct(entry, corrected, reason, DateTimeOffset.Now));
            await LoadPurchasesAsync();
            Notify("Correction enregistrée. Les valeurs d’origine restent dans l’historique de la ligne.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void PurchaseHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PurchaseLine line }) return;
        var first = line.FirstRecorded;
        var text = $"Valeurs d’origine : {first.PurchasedOn:dd/MM/yyyy} · {first.Supplier} · {first.Label} · {RevenueExporter.Money(first.Amount)} · pièce {first.Reference}\n\n" + string.Join("\n\n", PurchaseHistory.Describe(line));
        await new ConfirmHistoryWindow($"Historique de « {line.Entry.Label} »", text).ShowDialog<bool>(this);
    }

    private async void ExportPurchaseSummaryMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportPurchaseAsync("txt", "Totaux annuels", "*.txt", (year, header) =>
            new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(PurchaseExporter.AnnualSummary(year.Year, PurchaseTotals.Compute(year.Lines), header))).ToArray());

    /// <summary>Imports the purchases noted on the phone, after confirmation. A file already imported (same id) is only filed away.</summary>
    private async void ImportMobilePurchases_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        MobileInboxScan scan;
        try { scan = await Task.Run(() => MobileInbox.Scan(root)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { Notify("Lecture du dossier d’échange impossible : " + exception.Message, error: true); return; }
        if (scan.Items.Count == 0)
        {
            Notify(scan.Errors.Count > 0 ? "Aucune saisie importable : " + scan.Errors[0] : $"Aucune saisie du téléphone en attente. Dossier d’échange : « {MobileExchange.FolderName} » dans l’espace de travail.");
            return;
        }
        var dialog = new MobileInboxWindow(scan.Items, scan.Errors);
        if (!await dialog.ShowDialog<bool>(this)) return;
        var chosen = dialog.Selected;
        var imported = 0; var already = 0; var failures = new List<string>();
        await Task.Run(() =>
        {
            var journal = new PurchaseJournal(root);
            var known = journal.Years().SelectMany(y => journal.Load(y).Lines).Select(l => l.Entry.Id).ToHashSet();
            foreach (var item in chosen)
            {
                try
                {
                    if (known.Contains(item.Purchase.Id)) { already++; MobileInbox.MarkProcessed(root, item); continue; }
                    var stored = item.PhotoPath is null ? null : RevenueInvoiceArchive.StorePurchase(root, item.PhotoPath, item.Purchase.Supplier, item.Purchase.PurchasedOn.Year);
                    journal.Append(MobileExchange.ToEntry(item.Purchase, stored));
                    MobileInbox.MarkProcessed(root, item);
                    imported++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
                { failures.Add($"{item.Purchase.Supplier} : {exception.Message}"); }
            }
        });
        if (chosen.Count > 0) _purchaseYear = chosen.Max(i => i.Purchase.PurchasedOn.Year);
        if (PurchasesWorkspacePanel.IsVisible) await LoadPurchasesAsync();
        RequestMobileSnapshot(force: true);
        var message = $"{imported} achat{(imported > 1 ? "s" : "")} importé{(imported > 1 ? "s" : "")}" + (already > 0 ? $", {already} déjà présent{(already > 1 ? "s" : "")}" : "") + (failures.Count > 0 ? $", {failures.Count} en échec : {failures[0]}" : ".");
        Notify(message, error: failures.Count > 0);
    }

    private async void CancelPurchase_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PurchaseEntry entry }) return;
        var dialog = new PurchaseCancelWindow(entry);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var reason = dialog.Reason;
            await Task.Run(() => new PurchaseJournal(root).Cancel(entry, reason, DateTimeOffset.Now));
            await LoadPurchasesAsync();
            Notify("Achat annulé. Il reste visible dans le registre.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private void OpenPurchaseDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PurchaseEntry entry }) return;
        var path = RevenueInvoiceArchive.Resolve(WorkspaceRoot, entry.DocumentPath);
        if (path is null) { Notify("Le justificatif joint est introuvable dans l’espace de travail.", error: true); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { Notify("Impossible d’ouvrir le justificatif : " + exception.Message, error: true); }
    }

    private async void ExportPurchasePdfMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportPurchaseAsync("pdf", "Registre PDF", "*.pdf", (year, header) => PurchaseExporter.ToPdf(year, header));

    private async void ExportPurchaseCsvMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportPurchaseAsync("csv", "Fichier CSV", "*.csv", (year, _) => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(PurchaseExporter.ToCsv(year))).ToArray());

    private async Task ExportPurchaseAsync(string extension, string typeName, string pattern, Func<PurchaseYear, RevenueHeader, byte[]> build)
    {
        var root = WorkspaceRoot;
        var year = _purchaseYear;
        try
        {
            var chosen = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exporter le registre des achats " + year, SuggestedFileName = $"registre-des-achats-{year}.{extension}", DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern] }]
            });
            if (chosen?.TryGetLocalPath() is not { } path) return;
            var bytes = await Task.Run(() => build(new PurchaseJournal(root).Load(year), new RevenueHeaderStore(root).Load()));
            await File.WriteAllBytesAsync(path, bytes);
            Notify("Export enregistré : " + Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { Notify("Export impossible : " + exception.Message, error: true); }
    }

    private async Task LoadPurchasesAsync()
    {
        ApplyPurchaseView();
        if (_purchaseScheduleView) { await LoadExpensePlansAsync(); return; }
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("purchases", root);
        PurchaseList.Children.Clear();
        _purchasePageSummary.Text = "Chargement du registre…";
        try
        {
            var selectedYear = _purchaseYear;
            var result = await Task.Run(() =>
            {
                var journal = new PurchaseJournal(root);
                int waiting;
                try { waiting = MobileInbox.Scan(root).Items.Count; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { waiting = 0; }
                return (Years: journal.Years(), Year: journal.Load(selectedYear), Waiting: waiting);
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            var years = result.Years.Append(DateTime.Today.Year).Append(selectedYear).Distinct().OrderByDescending(y => y).ToArray();
            _populatingPurchaseYears = true;
            try { PurchaseYearBox.ItemsSource = years; PurchaseYearBox.SelectedItem = selectedYear; }
            finally { _populatingPurchaseYears = false; }
            var lines = result.Year.Lines;
            var summary = PurchaseTotals.Compute(lines);
            RequestMobileSnapshot();
            var valid = lines.Count(l => !l.IsCancelled);
            _purchasePageSummary.Text = $"{valid} achat{(valid > 1 ? "s" : "")} en {selectedYear} · total payé {RevenueExporter.Money(summary.Total)}"
                + (summary.CancelledCount > 0 ? $" · {summary.CancelledCount} annulé{(summary.CancelledCount > 1 ? "s" : "")}" : "")
                + (result.Waiting > 0 ? $" · {result.Waiting} saisie{(result.Waiting > 1 ? "s" : "")} du téléphone à importer" : "")
                + (result.Year.Errors.Count > 0 ? $" · {result.Year.Errors.Count} ligne{(result.Year.Errors.Count > 1 ? "s" : "")} illisible{(result.Year.Errors.Count > 1 ? "s" : "")}" : "");
            Control Warning() => CreateReadErrorState("Certaines lignes du registre sont illisibles", "Le fichier est conservé tel quel. Les autres lignes restent affichées. Après correction ou synchronisation, réessayez.", new IOException(string.Join(Environment.NewLine, result.Year.Errors)), LoadPurchasesAsync, compact: true);
            Control Header() => result.Year.Errors.Count == 0 ? CreateCompactTableHeader(PurchaseRowColumns, "DATE", "FOURNISSEUR", "LIBELLÉ", "MONTANT", "RÈGLEMENT", "PIÈCE", "ACTIONS")
                : new StackPanel { Children = { Warning(), CreateCompactTableHeader(PurchaseRowColumns, "DATE", "FOURNISSEUR", "LIBELLÉ", "MONTANT", "RÈGLEMENT", "PIÈCE", "ACTIONS") } };
            if (lines.Count == 0)
            {
                if (result.Year.Errors.Count > 0) PurchaseList.Children.Add(Warning());
                else PurchaseList.Children.Add(CreateEmptyState("IconCart", $"Aucun achat en {selectedYear}", "Enregistrez chaque achat de matières ou de fournitures avec la facture du fournisseur.", "Enregistrer un achat", CreatePurchase_Click));
                return;
            }
            PagedRows.Render(PurchaseList, lines.Reverse().ToArray(), line =>
            {
                var entry = line.Entry;
                Button? actions = null;
                var menu = new List<Button>();
                if (!string.IsNullOrWhiteSpace(entry.DocumentPath))
                {
                    var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir le justificatif"), Tag = entry };
                    open.Click += OpenPurchaseDocument_Click;
                    menu.Add(open);
                }
                if (line.IsCorrected)
                {
                    var history = new Button { Content = Icons.Label("IconHistory", "Voir l’historique"), Tag = line };
                    history.Click += PurchaseHistory_Click;
                    menu.Add(history);
                }
                if (!line.IsCancelled)
                {
                    var correct = new Button { Content = Icons.Label("IconEdit", "Corriger"), Tag = entry };
                    correct.Click += CorrectPurchase_Click;
                    menu.Add(correct);
                    var cancel = new Button { Content = Icons.Label("IconClose", "Annuler l’achat"), Tag = entry, Classes = { "menu-destructive" } };
                    cancel.Click += CancelPurchase_Click;
                    menu.Add(cancel);
                }
                if (menu.Count > 0) actions = CreateOverflowActions(menu.ToArray());
                var label = line.IsCancelled ? $"{entry.Label} — annulé : {line.Cancellation!.Reason}" : line.IsCorrected ? $"{entry.Label} (corrigé)" : entry.Label;
                var cells = new[]
                {
                    CreateCompactTableCell(entry.PurchasedOn.ToString("dd/MM/yyyy")), CreateCompactTableCell(entry.Supplier), CreateCompactTableCell(label, prominent: true),
                    CreateCompactTableCell(RevenueExporter.Money(entry.Amount)), CreateCompactTableCell(entry.PaymentMethod), CreateCompactTableCell(entry.Reference)
                };
                if (line.IsCancelled)
                    foreach (var cell in cells) { cell.TextDecorations = TextDecorations.Strikethrough; cell.Opacity = 0.6; }
                return CreateActionRow(PurchaseRowColumns, null, actions, cells);
            }, Header, root + "|purchases|" + selectedYear);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _purchasePageSummary.Text = "Lecture du registre indisponible";
            PurchaseList.Children.Clear();
            PurchaseList.Children.Add(CreateReadErrorState("Registre indisponible", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadPurchasesAsync));
        }
    }
}
