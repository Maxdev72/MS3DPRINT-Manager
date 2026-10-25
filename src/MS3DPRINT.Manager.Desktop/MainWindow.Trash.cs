using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Files;
using MS3DPRINT.Manager.Core.Projects;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Desktop.Dashboard;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Trash page.</summary>
public partial class MainWindow
{
    private async void RefreshTrash_Click(object? sender, RoutedEventArgs e) => await LoadTrashAsync();

    private async Task LoadTrashAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        var load = StartWorkspaceLoad("trash", workspaceRoot);
        TrashList.Children.Clear();
        _trashPageSummary.Text = "Chargement des éléments…";
        try
        {
            var result = await Task.Run(() =>
            {
                var reader = new WorkspaceTrashReader();
                var entries = reader.Load(workspaceRoot);
                return (Entries: entries, Errors: reader.ReadErrors);
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            var entries = result.Entries;
            _trashPageSummary.Text = entries.Count == 0 ? "Aucun élément supprimé" : $"{entries.Count} élément{(entries.Count > 1 ? "s" : "")} récupérable{(entries.Count > 1 ? "s" : "")}";
            if (result.Errors.Count > 0)
            {
                _trashPageSummary.Text = $"{entries.Count} récupérable{(entries.Count > 1 ? "s" : "")} · {result.Errors.Count} entrée{(result.Errors.Count > 1 ? "s" : "")} illisible{(result.Errors.Count > 1 ? "s" : "")}";
                TrashList.Children.Add(CreateReadErrorState("Certaines entrées sont illisibles",
                    "Leurs fichiers sont conservés. Les autres éléments restent restaurables. Vérifiez l’accès au dossier et la synchronisation, puis réessayez.",
                    new IOException(string.Join(Environment.NewLine + Environment.NewLine, result.Errors)), LoadTrashAsync));
                if (entries.Count == 0) return;
            }
            if (entries.Count == 0) { TrashList.Children.Add(CreateEmptyState("IconTrash", "La corbeille est vide", "Les éléments supprimés apparaissent ici et peuvent être restaurés.")); return; }
            TrashList.Children.Add(CreateCompactTableHeader(TrashRowColumns, "ÉLÉMENT", "SUPPRIMÉ LE", ""));
            foreach (var entry in entries)
            {
                var pending = _pendingOperations.Contains(RestoreOperationKey(workspaceRoot, entry.Id));
                var restore = new Button { Content = pending ? "Restauration…" : "Restaurer", Tag = entry.Id, IsEnabled = !pending };
                ToolTip.SetTip(restore, entry.Label);
                Avalonia.Automation.AutomationProperties.SetName(restore, "Restaurer " + entry.Label);
                restore.Click += RestoreTrash_Click;
                TrashList.Children.Add(CreateActionRow(TrashRowColumns, null, restore,
                    CreateCompactTableCell(entry.Label, prominent: true),
                    CreateCompactTableCell($"{entry.LocalDeletedAt:dd/MM/yyyy HH:mm}")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _trashPageSummary.Text = "Lecture de la corbeille indisponible";
            TrashList.Children.Clear();
            TrashList.Children.Add(CreateReadErrorState("Corbeille indisponible", "Vérifiez l’accès au dossier de l’atelier pour retrouver les éléments récupérables.", exception, LoadTrashAsync));
        }
    }

    private async void RestoreTrash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } button) return;
        var root = WorkspaceRoot;
        var navigation = CurrentNavigationStop?.Key;
        var label = ToolTip.GetTip(button) as string ?? "élément de la corbeille";
        var operation = TryBeginOperation(RestoreOperationKey(root, id), "Restauration : " + label + "…", button, "Restauration…");
        if (operation is null) return;
        try { await Task.Run(() => new WorkspaceTrashReader().Restore(root, id)); Notify("Élément restauré : " + label); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
        finally { operation.Dispose(); }
        if (MatchesNavigationContext(root, navigation)) await LoadTrashAsync();
    }
}
