using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Printers;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Surveillance page: live state and camera of the connected printers.</summary>
public partial class MainWindow
{
    private async Task LoadMonitoringAsync()
    {
        var root = WorkspaceRoot;
        try
        {
            var printers = await Task.Run(() => new WorkspaceInventoryReader().LoadPrinters(root));
            if (MonitoringWorkspacePanel.IsVisible) RefreshLiveCards(printers);
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
            // Leaving the page closes the connections and cameras; coming back reopens them.
            MonitoringWorkspacePanel.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !MonitoringWorkspacePanel.IsVisible) StopLiveCards(); };
            Closed += (_, _) => StopLiveCards();
            // Equal columns: about 420 px per card, so every card has the same width and its sections line up.
            PrinterLivePanel.SizeChanged += (_, e) => PrinterLivePanel.Columns = Math.Max(1, (int)(e.NewSize.Width / 420));
        }
        var wanted = printers.Where(p => p.IsActive).Select(p => (Printer: p, Connection: PrinterConnectionStore.Default.Get(p.Id)))
            .Where(x => x.Connection is not null).ToArray();
        foreach (var id in _liveCards.Keys.ToArray())
            if (!wanted.Any(x => x.Printer.Id == id && x.Connection == _liveCards[id].Connection && x.Printer.Name == _liveCards[id].Printer.Name && x.Printer.Model == _liveCards[id].Printer.Model)) { _liveCards[id].Dispose(); _liveCards.Remove(id); }
        foreach (var (printer, connection) in wanted)
            if (!_liveCards.ContainsKey(printer.Id))
            {
                var card = new PrinterLiveCard(printer, connection!);
                card.Changed += RefreshMonitoringSummary;
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

    /// <summary>"3 imprimantes · 2 en impression · 1 hors ligne · prochaine fin 14:32 (Bambu Lab H2D)".</summary>
    private void RefreshMonitoringSummary()
    {
        var cards = _liveCards.Values.ToArray();
        var parts = new List<string> { $"{cards.Length} imprimante{(cards.Length > 1 ? "s" : "")}" };
        var printing = cards.Count(c => c.Status?.IsPrinting == true);
        if (printing > 0) parts.Add($"{printing} en impression");
        var offline = cards.Count(c => c.Status is null);
        if (offline > 0) parts.Add($"{offline} hors ligne ou en connexion");
        if (cards.Where(c => c.Status is { IsPrinting: true, RemainingMinutes: > 0 }).MinBy(c => c.Status!.RemainingMinutes) is { } next)
            parts.Add($"prochaine {PrinterLiveCard.FinishTime(DateTime.Now.AddMinutes(next.Status!.RemainingMinutes!.Value)).ToLowerInvariant()} ({next.Printer.Name})");
        MonitoringSummary.Text = string.Join(" · ", parts);
        MonitoringSummary.IsVisible = cards.Length > 0;
    }
}
