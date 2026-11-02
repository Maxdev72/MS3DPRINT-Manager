using Avalonia.Controls;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private int _purchasePointingFilter;
    private int _revenuePointingFilter;
    private readonly HashSet<StackPanel> _journalPointingBusy = [];

    private async Task ChangeJournalPointingAsync(StackPanel host, bool pointed, string[] labels, Func<bool> current, Action save, Func<Task> reload)
    {
        if (labels.Length == 0 || !current() || !_journalPointingBusy.Add(host)) return;
        host.IsEnabled = false;
        try
        {
            if (!pointed && !await new ConfirmRemovalWindow(labels.Length == 1 ? "Pointage de " + labels[0] : $"Pointage de {labels.Length} lignes",
                "Ces lignes reviendront dans « À pointer ». Les achats, les recettes et leurs montants sont conservés.").ShowDialog<bool>(this)) return;
            if (!current()) return;
            await Task.Run(save);
            if (current()) Notify($"{labels.Length} ligne{(labels.Length > 1 ? "s" : "")} {(pointed ? "pointée" : "dépointée")}{(labels.Length > 1 ? "s" : "")}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { if (current()) Notify("Pointage non enregistré : " + exception.Message, error: true); }
        finally
        {
            _journalPointingBusy.Remove(host); host.IsEnabled = true;
            if (current()) await reload();
        }
    }
}
