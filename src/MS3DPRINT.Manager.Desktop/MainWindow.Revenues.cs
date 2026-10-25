using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Revenue book page: chronological collections, traceable cancellation, exports.</summary>
public partial class MainWindow
{
    private readonly TextBlock _revenuePageSummary = WorkspaceSummary();
    private bool _populatingRevenueYears;
    private int _revenueYear = DateTime.Today.Year;
    private RevenueYear? _revenueLoaded;

    private const string RevenueRowColumns = "90,1.2*,1.8*,120,110,120,64";

    private async void RefreshRevenues_Click(object? sender, RoutedEventArgs e) => await LoadRevenuesAsync();

    private async void RevenueYear_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingRevenueYears || RevenueYearBox.SelectedItem is not int year || year == _revenueYear) return;
        _revenueYear = year;
        await LoadRevenuesAsync();
    }

    private async void CreateRevenue_Click(object? sender, RoutedEventArgs e) => await OpenRevenueEntryAsync(null);

    private async Task OpenRevenueEntryAsync(string? invoicePdf, RevenueSuggestion? suggestion = null)
    {
        var root = WorkspaceRoot;
        IReadOnlyList<ClientSummary> clientList;
        try { clientList = await Task.Run(() => new ClientCatalog(new ClientProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root))).Load(root)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { clientList = []; }
        var clients = clientList.Select(c => c.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Order().ToArray();
        var dialog = new RevenueEntryWindow(clients, DateOnly.FromDateTime(DateTime.Today), invoicePdf);
        if (suggestion is not null) dialog.Suggest(suggestion);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } entry) return;
        try
        {
            var journal = new RevenueJournal(root);
            if (dialog.SourcePdfPath is { } sourcePdf)
            {
                var clientFolder = clientList.FirstOrDefault(c => string.Equals(c.DisplayName, entry.ClientName, StringComparison.CurrentCultureIgnoreCase))?.ClientPath
                    ?? (suggestion is not null && string.Equals(suggestion.ClientName, entry.ClientName, StringComparison.CurrentCultureIgnoreCase) ? suggestion.ClientFolder : null);
                var stored = await Task.Run(() => RevenueInvoiceArchive.Store(root, sourcePdf, clientFolder, entry.ReceivedOn.Year));
                entry = entry with { InvoicePdfPath = stored };
            }
            var duplicate = await Task.Run(() => journal.IsInvoiceKnown(entry.InvoiceNumber));
            await Task.Run(() => journal.Append(entry));
            _revenueYear = entry.ReceivedOn.Year;
            if (RevenuesWorkspacePanel.IsVisible) await LoadRevenuesAsync();
            Notify(duplicate ? $"Encaissement enregistré. Attention : la facture {entry.InvoiceNumber} figure déjà dans le livre." : "Encaissement enregistré.", error: duplicate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void CorrectRevenue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevenueEntry entry }) return;
        var root = WorkspaceRoot;
        IReadOnlyList<ClientSummary> clientList;
        try { clientList = await Task.Run(() => new ClientCatalog(new ClientProfileStore(new MS3DPRINT.Manager.Core.Workspace.WorkspaceMetadataPaths(root))).Load(root)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { clientList = []; }
        var dialog = new RevenueEntryWindow(clientList.Select(c => c.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).Order().ToArray(), DateOnly.FromDateTime(DateTime.Today), null, entry);
        if (!await dialog.ShowDialog<bool>(this) || dialog.Result is not { } corrected) return;
        try
        {
            var reason = dialog.CorrectionReason;
            if (dialog.SourcePdfPath is { } sourcePdf)
            {
                var clientFolder = clientList.FirstOrDefault(c => string.Equals(c.DisplayName, corrected.ClientName, StringComparison.CurrentCultureIgnoreCase))?.ClientPath;
                var stored = await Task.Run(() => RevenueInvoiceArchive.Store(root, sourcePdf, clientFolder, corrected.ReceivedOn.Year));
                corrected = corrected with { InvoicePdfPath = stored };
            }
            await Task.Run(() => new RevenueJournal(root).Correct(entry, corrected, reason, DateTimeOffset.Now));
            await LoadRevenuesAsync();
            Notify("Correction enregistrée. Les valeurs d’origine restent dans l’historique de la ligne.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void RevenueHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevenueLine line }) return;
        var first = line.FirstRecorded;
        var text = $"Valeurs d’origine : {first.ReceivedOn:dd/MM/yyyy} · {first.Label} · {RevenueExporter.Money(first.Amount)} · pièce {first.InvoiceNumber}\n\n" + string.Join("\n\n", RevenueHistory.Describe(line));
        await new ConfirmHistoryWindow($"Historique de « {line.Entry.Label} »", text).ShowDialog<bool>(this);
    }

    /// <summary>Lists the invoices already filed in the workspace that have no revenue line, and records the ones the user confirms as paid.</summary>
    private async void ImportInvoices_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        Notify("Recherche des factures de l’espace de travail…");
        InvoiceScan scan; IReadOnlyList<RevenueLine> lines;
        try
        {
            (scan, lines) = await Task.Run(() =>
            {
                var found = InvoiceCatalog.Scan(root);
                var journal = new RevenueJournal(root);
                return (found, (IReadOnlyList<RevenueLine>)journal.Years().SelectMany(y => journal.Load(y).Lines).ToArray());
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { Notify("Recherche des factures impossible : " + exception.Message, error: true); return; }
        if (!string.Equals(root, WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) return;
        var missing = InvoiceCatalog.Unrecorded(scan.Candidates, lines);
        if (missing.Count == 0)
        {
            Notify(scan.Candidates.Count == 0 ? "Aucune facture lisible trouvée dans l’espace de travail." : $"Les {scan.Candidates.Count} factures lues ont toutes une recette.");
            return;
        }
        var dialog = new RevenueBatchWindow(missing, scan.Unreadable.Count, DateOnly.FromDateTime(DateTime.Today));
        if (!await dialog.ShowDialog<bool>(this)) return;
        var saved = 0; var failures = new List<string>();
        var entries = dialog.Result;
        await Task.Run(() =>
        {
            var journal = new RevenueJournal(root);
            foreach (var entry in entries)
            {
                try { journal.Append(entry); saved++; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { failures.Add($"{entry.InvoiceNumber} : {exception.Message}"); }
            }
        });
        if (entries.Count > 0) _revenueYear = entries.Max(x => x.ReceivedOn.Year);
        if (RevenuesWorkspacePanel.IsVisible) await LoadRevenuesAsync();
        Notify(failures.Count == 0 ? $"{saved} recette{(saved > 1 ? "s" : "")} enregistrée{(saved > 1 ? "s" : "")}." : $"{saved} enregistrée(s), {failures.Count} en échec : {failures[0]}", error: failures.Count > 0);
    }

    private async void CancelRevenue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevenueEntry entry }) return;
        var dialog = new RevenueCancelWindow(entry);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            var reason = dialog.Reason;
            await Task.Run(() => new RevenueJournal(root).Cancel(entry, reason, DateTimeOffset.Now));
            await LoadRevenuesAsync();
            Notify("Recette annulée. Elle reste visible dans le livre.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private void OpenRevenueInvoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RevenueEntry entry }) return;
        var path = RevenueInvoiceArchive.Resolve(WorkspaceRoot, entry.InvoicePdfPath);
        if (path is null) { Notify("La facture jointe est introuvable dans l’espace de travail.", error: true); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { Notify("Impossible d’ouvrir la facture : " + exception.Message, error: true); }
    }

    private void InitializeRevenueDrop()
    {
        DragDrop.SetAllowDrop(RevenuesWorkspacePanel, true);
        static string? PdfOf(DragEventArgs e)
        {
            var files = e.DataTransfer.TryGetFiles()?.ToArray() ?? [];
            return files.Length == 1 && files[0].TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path)
                && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        void Over(object? sender, DragEventArgs e)
        {
            var accepts = _panel is null && PdfOf(e) is not null;
            e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            RevenuesWorkspacePanel.Classes.Set("drag-active", accepts);
        }
        RevenuesWorkspacePanel.AddHandler(DragDrop.DragEnterEvent, Over); RevenuesWorkspacePanel.AddHandler(DragDrop.DragOverEvent, Over);
        RevenuesWorkspacePanel.AddHandler(DragDrop.DragLeaveEvent, (_, _) => RevenuesWorkspacePanel.Classes.Set("drag-active", false));
        RevenuesWorkspacePanel.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true; RevenuesWorkspacePanel.Classes.Set("drag-active", false);
            if (_panel is null && PdfOf(e) is { } pdf) await OpenRevenueEntryAsync(pdf);
        });
    }

    private async void EditRevenueHeader_Click(object? sender, RoutedEventArgs e)
    {
        var root = WorkspaceRoot;
        try
        {
            var dialog = new RevenueHeaderWindow(new RevenueHeaderStore(root).Load());
            if (!await dialog.ShowDialog<bool>(this)) return;
            new RevenueHeaderStore(root).Save(dialog.Result);
            Notify("Informations de l’entreprise enregistrées.");
            if (SettingsWorkspacePanel.IsVisible) await LoadSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { Notify(exception.Message, error: true); }
    }

    private async void ExportRevenuePdfMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportRevenueAsync("pdf", "Livre PDF", "*.pdf", (year, header) => RevenueExporter.ToPdf(year, header));

    private async void ExportRevenueCsvMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportRevenueAsync("csv", "Fichier CSV", "*.csv", (year, _) => new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(RevenueExporter.ToCsv(year))).ToArray());

    private async void ExportRevenueDeclarationMenu_Click(object? sender, RoutedEventArgs e)
        => await ExportRevenueAsync("txt", "Synthèse de déclaration", "*.txt", (year, header) =>
            new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(RevenueExporter.DeclarationSummary(year.Year, RevenueTotals.Compute(year.Lines), header))).ToArray(),
            "synthese-declaration");

    private async Task ExportRevenueAsync(string extension, string typeName, string pattern, Func<RevenueYear, RevenueHeader, byte[]> build, string stem = "livre-des-recettes")
    {
        var root = WorkspaceRoot;
        var year = _revenueYear;
        try
        {
            var chosen = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exporter le livre des recettes " + year, SuggestedFileName = $"{stem}-{year}.{extension}", DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern] }]
            });
            if (chosen?.TryGetLocalPath() is not { } path) return;
            var bytes = await Task.Run(() => build(new RevenueJournal(root).Load(year), new RevenueHeaderStore(root).Load()));
            await File.WriteAllBytesAsync(path, bytes);
            Notify("Export enregistré : " + Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { Notify("Export impossible : " + exception.Message, error: true); }
    }

    private async Task LoadRevenuesAsync()
    {
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("revenues", root);
        RevenueList.Children.Clear();
        _revenuePageSummary.Text = "Chargement du livre…";
        try
        {
            var selectedYear = _revenueYear;
            var result = await Task.Run(() =>
            {
                var journal = new RevenueJournal(root);
                ActivityThresholds thresholds;
                try { thresholds = new ActivityThresholdStore(root).Load(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { thresholds = new(); }
                int waiting;
                try { waiting = MS3DPRINT.Manager.Core.Mobile.MobileInbox.Scan(root).Revenues.Count; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { waiting = 0; }
                return (Years: journal.Years(), Year: journal.Load(selectedYear), Thresholds: thresholds, Waiting: waiting);
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            var years = result.Years.Append(DateTime.Today.Year).Append(selectedYear).Distinct().OrderByDescending(y => y).ToArray();
            _populatingRevenueYears = true;
            try { RevenueYearBox.ItemsSource = years; RevenueYearBox.SelectedItem = selectedYear; }
            finally { _populatingRevenueYears = false; }
            _revenueLoaded = result.Year;
            var lines = result.Year.Lines;
            var summary = RevenueTotals.Compute(lines);
            ShowRevenueThresholds(result.Thresholds, summary, selectedYear);
            RequestMobileSnapshot();
            var valid = lines.Count(l => !l.IsCancelled);
            _revenuePageSummary.Text = $"{valid} recette{(valid > 1 ? "s" : "")} en {selectedYear} · total encaissé {RevenueExporter.Money(summary.Total)} (marchandises {RevenueExporter.Money(summary.Goods)}, prestations {RevenueExporter.Money(summary.Services)})"
                + (summary.CancelledCount > 0 ? $" · {summary.CancelledCount} annulée{(summary.CancelledCount > 1 ? "s" : "")}" : "")
                + (result.Waiting > 0 ? $" · {result.Waiting} recette{(result.Waiting > 1 ? "s" : "")} du téléphone à importer" : "")
                + (result.Year.Errors.Count > 0 ? $" · {result.Year.Errors.Count} ligne{(result.Year.Errors.Count > 1 ? "s" : "")} illisible{(result.Year.Errors.Count > 1 ? "s" : "")}" : "");
            Control Warning() => CreateReadErrorState("Certaines lignes du livre sont illisibles", "Le fichier est conservé tel quel. Les autres lignes restent affichées. Après correction ou synchronisation, réessayez.", new IOException(string.Join(Environment.NewLine, result.Year.Errors)), LoadRevenuesAsync, compact: true);
            Control Header() => result.Year.Errors.Count == 0 ? CreateCompactTableHeader(RevenueRowColumns, "DATE", "CLIENT", "LIBELLÉ", "NATURE", "MONTANT", "PIÈCE", "ACTIONS")
                : new StackPanel { Children = { Warning(), CreateCompactTableHeader(RevenueRowColumns, "DATE", "CLIENT", "LIBELLÉ", "NATURE", "MONTANT", "PIÈCE", "ACTIONS") } };
            if (lines.Count == 0)
            {
                if (result.Year.Errors.Count > 0) RevenueList.Children.Add(Warning());
                else RevenueList.Children.Add(CreateEmptyState("IconBook", $"Aucune recette en {selectedYear}", "Enregistrez chaque paiement reçu, avec sa date d’encaissement et le numéro de facture.", "Enregistrer un encaissement", CreateRevenue_Click));
                return;
            }
            PagedRows.Render(RevenueList, lines.Reverse().ToArray(), line =>
            {
                var entry = line.Entry;
                Button? actions = null;
                var menu = new List<Button>();
                if (!string.IsNullOrWhiteSpace(entry.InvoicePdfPath))
                {
                    var open = new Button { Content = Icons.Label("IconOpen", "Ouvrir la facture"), Tag = entry };
                    open.Click += OpenRevenueInvoice_Click;
                    menu.Add(open);
                }
                if (line.IsCorrected)
                {
                    var history = new Button { Content = Icons.Label("IconHistory", "Voir l’historique"), Tag = line };
                    history.Click += RevenueHistory_Click;
                    menu.Add(history);
                }
                if (!line.IsCancelled)
                {
                    var correct = new Button { Content = Icons.Label("IconEdit", "Corriger"), Tag = entry };
                    correct.Click += CorrectRevenue_Click;
                    menu.Add(correct);
                    var cancel = new Button { Content = Icons.Label("IconClose", "Annuler la recette"), Tag = entry, Classes = { "menu-destructive" } };
                    cancel.Click += CancelRevenue_Click;
                    menu.Add(cancel);
                }
                if (menu.Count > 0) actions = CreateOverflowActions(menu.ToArray());
                var label = line.IsCancelled ? $"{entry.Label} — annulée : {line.Cancellation!.Reason}" : line.IsCorrected ? $"{entry.Label} (corrigée)" : entry.Label;
                var cells = new[]
                {
                    CreateCompactTableCell(entry.ReceivedOn.ToString("dd/MM/yyyy")), CreateCompactTableCell(entry.ClientName ?? "—"), CreateCompactTableCell(label, prominent: true),
                    CreateCompactTableCell(RevenueExporter.NatureName(entry.Nature)), CreateCompactTableCell(RevenueExporter.Money(entry.Amount)), CreateCompactTableCell(entry.InvoiceNumber)
                };
                if (line.IsCancelled)
                    foreach (var cell in cells) { cell.TextDecorations = TextDecorations.Strikethrough; cell.Opacity = 0.6; }
                return CreateActionRow(RevenueRowColumns, null, actions, cells);
            }, Header, root + "|" + selectedYear);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _revenuePageSummary.Text = "Lecture du livre indisponible";
            RevenueList.Children.Clear();
            RevenueList.Children.Add(CreateReadErrorState("Livre indisponible", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadRevenuesAsync));
        }
    }
}
