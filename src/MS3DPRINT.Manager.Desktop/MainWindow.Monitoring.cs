using Avalonia.Controls;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Printers;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// Surveillance page: live state and camera of the connected printers. The status links start with the window, so the
/// end of a print or a new alert is announced from any page and from the notification area; cameras run only on this page.
/// </summary>
public partial class MainWindow
{
    private async Task LoadMonitoringAsync()
    {
        var root = WorkspaceRoot;
        try
        {
            var printers = await Task.Run(() => new WorkspaceInventoryReader().LoadPrinters(root));
            RefreshLiveCards(printers);
            _ = BambuHmsTexts.LoadAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            MonitoringEmptyHost.Children.Clear();
            MonitoringEmptyHost.Children.Add(CreateReadErrorState("Parc indisponible", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadMonitoringAsync));
        }
    }

    private readonly Dictionary<Guid, PrinterLiveCard> _liveCards = [];
    private bool _liveCardsHooked;

    /// <summary>One live card per active printer that has a local connection; cards whose connection is unchanged keep running.</summary>
    private void RefreshLiveCards(IReadOnlyList<PrinterProfile> printers)
    {
        if (!_liveCardsHooked)
        {
            _liveCardsHooked = true;
            // Cameras follow the page; the status links keep running for the notifications.
            MonitoringWorkspacePanel.PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                foreach (var live in _liveCards.Values) live.SetCameraActive(CamerasWanted);
            };
            Closed += (_, _) => { _monitoringWindow?.Close(); StopLiveCards(); };
            // Equal columns: about 420 px per card, so every card has the same width and its sections line up.
            PrinterLivePanel.SizeChanged += (_, e) => PrinterLivePanel.Columns = Math.Max(1, (int)(e.NewSize.Width / 420));
        }
        var wanted = printers.Where(p => p.IsActive).Select(p => (Printer: p, Connection: PrinterConnectionStore.Default.Get(p.Id)))
            .Where(x => x.Connection is not null).ToArray();
        foreach (var id in _liveCards.Keys.ToArray())
            if (!wanted.Any(x => x.Printer.Id == id && x.Connection == _liveCards[id].Connection && x.Printer.Name == _liveCards[id].Printer.Name && x.Printer.Model == _liveCards[id].Printer.Model))
            { _liveCards[id].Dispose(); _liveCards.Remove(id); _lastPrinterStatus.Remove(id); }
        foreach (var (printer, connection) in wanted)
            if (!_liveCards.ContainsKey(printer.Id))
            {
                var card = new PrinterLiveCard(printer, connection!);
                card.Changed += () => { RefreshMonitoringSummary(); AnnouncePrinterChange(card); };
                card.SetCameraActive(CamerasWanted);
                _liveCards[printer.Id] = card;
            }
        PrinterLivePanel.Children.Clear();
        foreach (var (printer, _) in wanted) PrinterLivePanel.Children.Add(_liveCards[printer.Id]);
        RefreshMonitoringSummary();
        MonitoringEmptyHost.Children.Clear();
        if (wanted.Length == 0)
            MonitoringEmptyHost.Children.Add(CreateEmptyState("IconCamera", "Aucune imprimante surveillée",
                "Dans Imprimantes, modifiez une Bambu Lab et renseignez son adresse IP, son numéro de série et son code d’accès.", "Ouvrir les imprimantes",
                async (_, _) => await NavigateToWorkspaceAsync("Imprimantes")));
    }

    private void StopLiveCards()
    {
        foreach (var card in _liveCards.Values) card.Dispose();
        _liveCards.Clear();
        PrinterLivePanel.Children.Clear();
        MonitoringSummary.IsVisible = false;
    }

    private readonly Dictionary<Guid, BambuStatus?> _lastPrinterStatus = [];

    /// <summary>
    /// End of print, failure, pause and new alerts: a toast in the window and a Windows notification. The first state
    /// received and a return from offline announce nothing, so opening the software does not replay old events.
    /// </summary>
    private void AnnouncePrinterChange(PrinterLiveCard card)
    {
        var now = card.Status;
        var seen = _lastPrinterStatus.TryGetValue(card.Printer.Id, out var before);
        _lastPrinterStatus[card.Printer.Id] = now;
        if (!seen || before is null || now is null) return;
        var name = card.Printer.Name;
        var job = now.JobName ?? "Impression";
        if (before.IsPrinting && now.State == "FINISH") AnnouncePrinter(card, $"Impression terminée · {name}", job, error: false);
        else if (before.State != "FAILED" && now.State == "FAILED") AnnouncePrinter(card, $"Impression en échec · {name}", job, error: true);
        else if (before.State != "PAUSE" && now.State == "PAUSE") AnnouncePrinter(card, $"Impression en pause · {name}", job, error: true);
        var known = before.Alerts.Select(a => a.Ecode).ToHashSet();
        foreach (var alert in now.Alerts.Where(a => !known.Contains(a.Ecode)))
            AnnouncePrinter(card, $"Alerte · {name}", BambuHmsCatalog.Describe(alert, BambuHmsTexts.Current), error: true);
    }

    /// <summary>The in-window toast at once; the Windows notification follows with a picture of the print when the camera gives one.</summary>
    private async void AnnouncePrinter(PrinterLiveCard card, string title, string body, bool error)
    {
        Notify(title + " : " + body, error);
        if (DesktopApplication.SystemNotification is not { } notify) return;
        notify(title, body, await card.SnapshotAsync());
    }

    private readonly StatStage _watchedTile = new("Imprimantes"), _printingTile = new("En impression", "CyanBrush"), _offlineTile = new("Hors ligne", "WarningBrush"), _nextTile = new("Prochaine fin", "SuccessBrush");

    /// <summary>Four tiles above the cards: printers followed, printing, unreachable, and the next print to finish.</summary>
    private void RefreshMonitoringSummary()
    {
        if (MonitoringSummary.Children.Count == 0)
        {
            // Same counters as the Home page: equal columns, 16 px apart.
            var row = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 16 };
            var stages = new[] { _watchedTile, _printingTile, _offlineTile, _nextTile };
            for (var i = 0; i < stages.Length; i++) { Grid.SetColumn(stages[i], i); row.Children.Add(stages[i]); }
            MonitoringSummary.Children.Add(row);
        }
        var cards = _liveCards.Values.ToArray();
        _watchedTile.Set(cards.Length.ToString(), "Suivies en direct");
        var printing = cards.Count(c => c.Status?.IsPrinting == true);
        _printingTile.Set(printing.ToString(), printing == 0 ? "Aucune impression en cours" : "Avancement sur chaque carte");
        var offline = cards.Count(c => c.Status is null);
        _offlineTile.Set(offline.ToString(), offline > 0 ? "Injoignables ou en connexion" : "Toutes répondent");
        if (cards.Where(c => c.Status is { IsPrinting: true, RemainingMinutes: > 0 }).MinBy(c => c.Status!.RemainingMinutes) is { } next)
        {
            var end = DateTime.Now.AddMinutes(next.Status!.RemainingMinutes!.Value);
            _nextTile.Set(end.ToString("HH:mm"), $"{PrinterLiveCard.FinishDay(end)} · {next.Printer.Name}");
        }
        else _nextTile.Set("—", "Aucune fin prévue");
        MonitoringSummary.IsVisible = cards.Length > 0;
        RefreshMonitoringDetached();
    }
}
