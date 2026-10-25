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
            var root = WorkspaceRoot;
            var now = DateTimeOffset.UtcNow;
            new PrinterStore(root).Create(new PrinterProfile(Guid.NewGuid(), dialog.Brand, dialog.Model, dialog.PrinterName, dialog.HourlyRate, dialog.BuildVolume, dialog.PrinterIsActive, now, now,
                dialog.PurchasePrice, dialog.LifetimeHours, dialog.PowerWatts, dialog.AmortizationYears, dialog.ProductiveHoursPerYear, dialog.UseManualHourlyRate, dialog.MaintenanceHourlyRate));
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
            var root = WorkspaceRoot;
            new PrinterStore(root).Update(original with { Brand = dialog.Brand, Model = dialog.Model, Name = dialog.PrinterName, HourlyRate = dialog.HourlyRate, BuildVolume = dialog.BuildVolume,
                IsActive = dialog.PrinterIsActive, PurchasePrice = dialog.PurchasePrice, LifetimeHours = dialog.LifetimeHours, PowerWatts = dialog.PowerWatts,
                AmortizationYears = dialog.AmortizationYears, ProductiveHoursPerYear = dialog.ProductiveHoursPerYear, UseManualHourlyRate = dialog.UseManualHourlyRate, MaintenanceHourlyRate = dialog.MaintenanceHourlyRate }, original.UpdatedAt);
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
            var root = WorkspaceRoot;
            await Task.Run(() => new PrinterStore(root).Trash(printer.Id));
            await LoadPrintersAsync();
            Notify("Imprimante envoyée dans la corbeille.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { Notify(exception.Message, error: true); }
    }

    private async Task LoadPrintersAsync()
    {
        var root = WorkspaceRoot;
        var load = StartWorkspaceLoad("printers", root);
        PrinterList.Children.Clear();
        _printerPageSummary.Text = "Chargement du parc…";
        try
        {
            var result = await Task.Run(() =>
            {
                var reader = new WorkspaceInventoryReader();
                return (Profiles: reader.LoadPrinters(root), Reader: reader);
            });
            if (!IsWorkspaceLoadCurrent(load)) return;
            var printers = result.Profiles;
            var errors = result.Reader.ReadErrors;
            var active = printers.Count(p => p.IsActive);
            _printerPageSummary.Text = $"{printers.Count} imprimante{(printers.Count > 1 ? "s" : "")} · {active} active{(active > 1 ? "s" : "")}";
            if (errors.Count > 0) _printerPageSummary.Text += $" · {errors.Count} fiche{(errors.Count > 1 ? "s" : "")} illisible{(errors.Count > 1 ? "s" : "")}";
            Control Warning() => CreateReadErrorState("Certaines fiches sont illisibles", "Les fichiers concernés sont conservés. Les machines lisibles restent disponibles. Après correction ou synchronisation, réessayez.", new IOException(string.Join(Environment.NewLine, errors)), LoadPrintersAsync, compact: true);
            if (printers.Count == 0 && errors.Count > 0) { PrinterList.Children.Add(Warning()); return; }
            if (printers.Count == 0)
            {
                PrinterList.Children.Add(CreateEmptyState("IconPrinters", "Aucune imprimante enregistrée", "Ajoutez vos machines : leur coût horaire alimente vos estimations.", "Ajouter une imprimante", CreatePrinter_Click));
                return;
            }
            PagedRows.Render(PrinterList, printers, printer =>
            {
                var edit = new Button { Content = Icons.Label("IconEdit", "Modifier"), Tag = printer }; edit.Click += EditPrinter_Click;
                var trash = new Button { Content = Icons.Label("IconTrash", "Corbeille"), Tag = printer, Classes = { "menu-destructive" } }; trash.Click += TrashPrinter_Click;
                return CreateActionRow(null, CreateOverflowActions(edit, trash),
                    CreateCompactTableCell(printer.DisplayName, prominent: true),
                    CreateCompactTableCell($"{PrinterCostCalculator.HourlyRate(printer):0.00} €/h"),
                    CreateCompactTableCell(printer.PowerWatts is { } watts ? $"{watts} W" : "—"));
            }, () => errors.Count == 0 ? CreateCompactTableHeader(ActionRowColumns, "IMPRIMANTE", "COÛT HORAIRE", "PUISSANCE", "ACTIONS")
                : new StackPanel { Children = { Warning(), CreateCompactTableHeader(ActionRowColumns, "IMPRIMANTE", "COÛT HORAIRE", "PUISSANCE", "ACTIONS") } }, root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!IsWorkspaceLoadCurrent(load)) return;
            _printerPageSummary.Text = "Lecture du parc indisponible";
            PrinterList.Children.Clear();
            PrinterList.Children.Add(CreateReadErrorState("Parc indisponible", "Vérifiez l’accès au dossier de l’atelier, puis réessayez.", exception, LoadPrintersAsync));
        }
    }
}
