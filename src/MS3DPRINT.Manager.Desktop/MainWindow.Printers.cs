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

/// <summary>Printers page.</summary>
public partial class MainWindow
{
    private async void RefreshPrinters_Click(object? sender, RoutedEventArgs e) => await LoadPrintersAsync();

    private async void CreatePrinter_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreatePrinterWindow();
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            var now = DateTimeOffset.UtcNow;
            new PrinterStore(root).Create(new PrinterProfile(Guid.NewGuid(), dialog.Brand, dialog.Model, dialog.PrinterName, dialog.HourlyRate, dialog.BuildVolume, dialog.PrinterIsActive, now, now,
                dialog.PurchasePrice, dialog.LifetimeHours, dialog.PowerWatts));
            Notify("Imprimante ajoutée.");
            await LoadPrintersAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void EditPrinter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PrinterProfile original }) return;
        var dialog = new CreatePrinterWindow(original);
        if (!await dialog.ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            new PrinterStore(root).Update(original with { Brand = dialog.Brand, Model = dialog.Model, Name = dialog.PrinterName, HourlyRate = dialog.HourlyRate, BuildVolume = dialog.BuildVolume,
                IsActive = dialog.PrinterIsActive, PurchasePrice = dialog.PurchasePrice, LifetimeHours = dialog.LifetimeHours, PowerWatts = dialog.PowerWatts }, original.UpdatedAt);
            await LoadPrintersAsync();
            Notify("Imprimante enregistrée.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Notify(exception.Message, error: true); }
    }

    private async void TrashPrinter_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PrinterProfile printer }) return;
        if (!await new ConfirmTrashWindow(printer.Name, "Cette imprimante pourra être restaurée depuis la corbeille.").ShowDialog<bool>(this)) return;
        try
        {
            var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
            await Task.Run(() => new PrinterStore(root).Trash(printer.Id));
            await LoadPrintersAsync();
            Notify("Imprimante envoyée dans la corbeille.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadPrintersAsync()
    {
        var root = Environment.GetEnvironmentVariable("MS3DPRINT_WORKSPACE") ?? DefaultWorkspaceRoot;
        PrinterList.Children.Clear();
        try
        {
            var printers = await Task.Run(() => new WorkspaceInventoryReader().LoadPrinters(root));
            if (printers.Count == 0)
            {
                PrinterList.Children.Add(CreateEmptyState("IconPrinters", "Aucune imprimante enregistrée", "Ajoutez vos machines : leur coût horaire alimente vos estimations.", "Ajouter une imprimante", CreatePrinter_Click));
                return;
            }
            PrinterList.Children.Add(CreateCompactTableHeader(ActionRowColumns, "IMPRIMANTE", "COÛT HORAIRE", "PUISSANCE", "ACTIONS"));
            foreach (var printer in printers)
            {
                var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = printer }; edit.Click += EditPrinter_Click;
                var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = printer }; trash.Click += TrashPrinter_Click;
                PrinterList.Children.Add(CreateActionRow(null, CreateOverflowActions(edit, trash),
                    CreateCompactTableCell(printer.DisplayName, prominent: true),
                    CreateCompactTableCell($"{PrinterCostCalculator.HourlyRate(printer):0.00} €/h"),
                    CreateCompactTableCell(printer.PowerWatts is { } watts ? $"{watts} W" : "—")));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { PrinterList.Children.Add(new TextBlock { Text = "Impossible de lire les imprimantes : " + exception.Message, Margin = new Avalonia.Thickness(8) }); }
    }
}
