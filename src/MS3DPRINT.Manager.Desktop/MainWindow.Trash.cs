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
        var workspaceRoot = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        TrashList.Children.Clear();
        try
        {
            var entries = await Task.Run(() => new WorkspaceTrashReader().Load(workspaceRoot));
            if (entries.Count == 0) { TrashList.Children.Add(CreateEmptyState("IconTrash", "La corbeille est vide", "Les éléments supprimés apparaissent ici et peuvent être restaurés.")); return; }
            TrashList.Children.Add(CreateCompactTableHeader(TrashRowColumns, "ÉLÉMENT", "SUPPRIMÉ LE", "", ""));
            foreach (var entry in entries)
            {
                var restore = new Button { Content = "Restaurer", Tag = entry.Id };
                restore.Click += RestoreTrash_Click;
                TrashList.Children.Add(CreateActionRow(TrashRowColumns, null, restore,
                    CreateCompactTableCell(entry.Label, prominent: true),
                    CreateCompactTableCell($"{entry.LocalDeletedAt:dd/MM/yyyy HH:mm}")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { TrashList.Children.Add(new TextBlock { Text = "Impossible de lire la corbeille.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void RestoreTrash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        try { await Task.Run(() => new WorkspaceTrashReader().Restore(root, id)); await LoadTrashAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
