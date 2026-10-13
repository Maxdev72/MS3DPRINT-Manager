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

/// <summary>Filaments page.</summary>
public partial class MainWindow
{
    private async void RefreshFilaments_Click(object? sender, RoutedEventArgs e) => await LoadFilamentsAsync();

    private async void CreateFilament_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateFilamentWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            new WorkspaceFilamentEditor().Create(root, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Filament ajouté.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void EditFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile original }) return;
        var dialog = new CreateFilamentWindow(original);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            new WorkspaceFilamentEditor().Update(root, original, dialog.Brand, dialog.FilamentName, dialog.Material, dialog.Price, dialog.IsAbrasive, dialog.Color);
            Notify("Fiche filament enregistrée.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadFilamentsAsync()
    {
        var workspaceRoot = WorkspaceRoot;
        FilamentList.Children.Clear();
        try
        {
            var filaments = await Task.Run(() => new WorkspaceInventoryReader().LoadFilaments(workspaceRoot));
            if (filaments.Count == 0) { FilamentList.Children.Add(CreateEmptyState("IconFilaments", "Aucun filament enregistré", "Ajoutez vos bobines pour calculer le coût matière de vos estimations.", "Ajouter un filament", CreateFilament_Click)); return; }
            FilamentList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "FILAMENT", "MATIÈRE", "PRIX", "ACTIONS"));
            foreach (var filament in filaments)
            {
                var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = filament }; edit.Click += EditFilament_Click;
                var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = filament }; trash.Click += TrashFilament_Click;
                var color = string.IsNullOrWhiteSpace(filament.Color) ? string.Empty : $" · {filament.Color}";
                var material = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                material.Children.Add(CreateCompactTableCell($"{filament.Material}{color}"));
                if (filament.IsAbrasive) material.Children.Add(new Border { Classes = { "badge", "warning" }, Child = new TextBlock { Text = "Abrasif" } });
                FilamentList.Children.Add(CreateActionRow(null, CreateOverflowActions(edit, trash),
                    CreateCompactTableCell($"{filament.Brand} · {filament.Name}", prominent: true), material,
                    CreateCompactTableCell($"{filament.PricePerKg:0.00} €/kg")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { FilamentList.Children.Add(new TextBlock { Text = "Impossible de lire les filaments.", Margin = new Avalonia.Thickness(8) }); }
    }

    private async void TrashFilament_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MS3DPRINT.Manager.Core.Filaments.FilamentProfile filament }) return;
        if (!await new ConfirmTrashWindow(filament.Brand + " " + filament.Name, "La fiche pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = WorkspaceRoot;
            await Task.Run(() => new MS3DPRINT.Manager.Core.Filaments.FilamentStore(root).Trash(filament.Id));
            Notify("Filament envoyé dans la corbeille.");
            await LoadFilamentsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }
}
