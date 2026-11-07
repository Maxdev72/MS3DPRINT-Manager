using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Desktop;

/// <summary>Weight and print time read from the slicer's file instead of being copied by hand.</summary>
public partial class MainWindow
{
    private static readonly string[] SlicerPatterns = ["*.gcode", "*.gco", "*.3mf"];

    private void InitializeEstimateSlicerImport()
    {
        var read = new Button { Name = "EstimateReadSlicerFile", Content = Icons.Label("IconOpen", "Lire le fichier du slicer"), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(read, "G-code (Bambu Studio, OrcaSlicer, PrusaSlicer, Cura) ou 3MF tranché : le poids et la durée sont remplis. Vous pouvez aussi déposer le fichier sur la page.");
        read.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Fichier du slicer", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("G-code ou 3MF tranché") { Patterns = SlicerPatterns }] });
            if (files.Count == 1 && files[0].TryGetLocalPath() is { } path) await ApplySlicerFileAsync(path);
        };
        EstimatePrintInputs.Children.Insert(Math.Min(2, EstimatePrintInputs.Children.Count), read);

        DragDrop.SetAllowDrop(EstimatesWorkspacePanel, true);
        static string? SlicerFileOf(DragEventArgs e)
        {
            var files = e.DataTransfer.TryGetFiles()?.ToArray() ?? [];
            return files.Length == 1 && files[0].TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path)
                && Path.GetExtension(path).ToLowerInvariant() is ".gcode" or ".gco" or ".3mf" ? path : null;
        }
        void Over(object? sender, DragEventArgs e)
        {
            var accepts = _panel is null && SlicerFileOf(e) is not null;
            e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            EstimatesWorkspacePanel.Classes.Set("drag-active", accepts);
        }
        EstimatesWorkspacePanel.AddHandler(DragDrop.DragEnterEvent, Over); EstimatesWorkspacePanel.AddHandler(DragDrop.DragOverEvent, Over);
        EstimatesWorkspacePanel.AddHandler(DragDrop.DragLeaveEvent, (_, _) => EstimatesWorkspacePanel.Classes.Set("drag-active", false));
        EstimatesWorkspacePanel.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true; EstimatesWorkspacePanel.Classes.Set("drag-active", false);
            if (_panel is null && SlicerFileOf(e) is { } path) await ApplySlicerFileAsync(path);
        });
    }

    internal async Task<bool> ApplySlicerFileAsync(string path)
    {
        var estimate = await Task.Run(() => SlicerEstimateReader.Read(path));
        var name = Path.GetFileName(path);
        if (estimate is null)
        {
            Notify(path.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)
                ? $"« {name} » ne contient pas encore de tranchage : tranchez le plateau dans le slicer, enregistrez, puis recommencez."
                : $"« {name} » : poids et durée introuvables dans ce fichier.", error: true);
            return false;
        }
        EstimateWeight.Text = Exact(estimate.Grams);
        _estimateDurationHours.Text = (estimate.Minutes / 60).ToString();
        _estimateDurationMinutes.Text = (estimate.Minutes % 60).ToString();
        // A file holds a whole plate (or several): a single sliced piece is the only case where "per piece" stays right.
        if (estimate.Plates > 1 && EstimatePrintDataMode.SelectedIndex != 1) EstimatePrintDataMode.SelectedIndex = 1;
        UpdateEstimateQuickContext();
        EstimateChanged();
        var plates = estimate.Plates > 1 ? $" pour {estimate.Plates} plateaux" : "";
        Notify($"« {name} » : {Exact(estimate.Grams)} g et {estimate.Minutes / 60} h {estimate.Minutes % 60:00} min{plates}."
            + (estimate.FromLength ? " Poids calculé pour du PLA 1,75 mm : corrigez-le pour une autre matière." : "")
            + (EstimatePrintDataMode.SelectedIndex != 1 ? " Vérifiez s’il s’agit d’une pièce ou d’un plateau." : ""));
        return true;
    }
}
