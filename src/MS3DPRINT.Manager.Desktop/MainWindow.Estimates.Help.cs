using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MS3DPRINT.Manager.Desktop;

public partial class MainWindow
{
    private void InitializeEstimateHelp()
    {
        foreach (var entry in EstimateFieldHelp.Entries)
            if (this.FindControl<Control>(entry.Key) is { } control) EstimateFieldHelp.Attach(control, entry.Key);
        (Control Control, string Key)[] extra =
        [
            (_estimatePrinter, "_estimatePrinter"), (_estimateCapacity, "_estimateCapacity"), (_estimateExtraGrams, "_estimateExtraGrams"),
            (_estimateWeightIncludesSupports, "_estimateWeightIncludesSupports"), (_estimateSalesShare, "_estimateSalesShare"),
            (_estimateBicShare, "_estimateBicShare"), (_estimateCommercial, "_estimateCommercial"), (_estimateChargeCategory, "_estimateChargeCategory")
        ];
        foreach (var (control, key) in extra) EstimateFieldHelp.Attach(control, key);
    }
    private async void EstimateHelp_Click(object? sender, RoutedEventArgs e) => await new EstimateHelpWindow().ShowDialog<bool>(this);
}
