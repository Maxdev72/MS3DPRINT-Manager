using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Printers;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateTrashAndScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-estimate-trash-" + Guid.NewGuid().ToString("N"));

    private static EstimateProfile Estimate(string filament = "Sunlu Basic", string printer = "P1S") =>
        new(Guid.NewGuid(), "Support", filament, printer, new EstimateInputs(MaterialCost: 4m), new EstimateResult(4m, 4m, 4m, 4m), DateTimeOffset.UtcNow, 100m, 2m, 2m, "AB-2026-001", "ACME");

    [Fact]
    public void Estimate_goes_to_the_trash_and_comes_back_identical()
    {
        var store = new EstimateStore(_root);
        var estimate = Estimate();
        store.Create(estimate);

        var entry = store.Trash(estimate.Id);

        Assert.Empty(store.LoadAll());
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal(estimate, Assert.Single(store.LoadAll()));
    }

    [Fact]
    public void Printer_goes_to_the_trash_and_comes_back_identical()
    {
        var store = new PrinterStore(_root);
        var now = DateTimeOffset.UtcNow;
        var printer = new PrinterProfile(Guid.NewGuid(), "Bambu Lab", "P1S", "P1S Atelier", 2.5m, "256 mm", true, now, now);
        store.Create(printer);

        var entry = store.Trash(printer.Id);

        Assert.Empty(store.LoadAll());
        new ManagedFileService(_root).Restore(entry.Id);
        Assert.Equal(printer, Assert.Single(store.LoadAll()));
    }

    [Fact]
    public void Modelling_only_keeps_no_leftover_filament_printer_or_machine_time()
    {
        var saved = EstimateScopeRules.ForSave(true, "Sunlu Basic", "P1S", 100m, 2m, 2m, null);

        Assert.Equal((EstimateScopeRules.ModellingOnlyLabel, EstimateScopeRules.NoPrinterLabel, 0m, 0m, 0m), saved);
        Assert.True(EstimateScopeRules.IsModellingOnly(Estimate(EstimateScopeRules.ModellingOnlyLabel, EstimateScopeRules.NoPrinterLabel)));
    }

    [Fact]
    public void Printing_estimate_keeps_its_names_when_the_filament_or_printer_left_the_lists()
    {
        var previous = Estimate("Sunlu Basic", "P1S");

        var saved = EstimateScopeRules.ForSave(false, null, null, 120m, 3m, 2m, previous);

        Assert.Equal(("Sunlu Basic", "P1S", 120m, 3m, 2m), saved);
        Assert.False(EstimateScopeRules.IsModellingOnly(previous));
    }

    [Fact]
    public void Printing_estimate_uses_the_current_selection_when_there_is_one()
    {
        var saved = EstimateScopeRules.ForSave(false, "Bambu PLA", "X1C", 50m, 1m, 3m, Estimate());

        Assert.Equal(("Bambu PLA", "X1C", 50m, 1m, 3m), saved);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
