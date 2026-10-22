using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Core.Tests.Printers;

public sealed class PrinterCostCalculatorTests
{
    [Fact]
    public void Calculates_amortization_and_electricity_from_printer_specs()
    {
        var printer = new PrinterProfile(Guid.NewGuid(), "Bambu Lab", "P1S", "Bambu Lab P1S", 0m, "256 × 256 × 256 mm", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, PurchasePrice: 800m, LifetimeHours: 8000, PowerWatts: 250);

        Assert.Equal(0.10m, PrinterCostCalculator.HourlyRate(printer));
        Assert.Equal(0.20m, PrinterCostCalculator.MachineCost(printer, 2m));
        Assert.Equal(0.13m, PrinterCostCalculator.ElectricityCost(printer, 2m, 0.25m));
    }

    [Fact]
    public void Uses_legacy_hourly_rate_when_purchase_specs_are_missing()
    {
        var printer = new PrinterProfile(Guid.NewGuid(), "Prusa Research", "MK4", "Prusa Research MK4", 2.50m, "250 mm", true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(2.50m, PrinterCostCalculator.HourlyRate(printer));
        Assert.Equal(5m, PrinterCostCalculator.MachineCost(printer, 2m));
    }

    private static PrinterProfile Printer(decimal price, int hours, bool active, int? watts) =>
        new(Guid.NewGuid(), "Marque", "Modèle", "Imprimante", 0m, "250 mm", active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, price, hours, watts);

    [Fact]
    public void Fleet_hourly_rate_adds_the_amortization_of_every_active_printer()
    {
        var fleet = new[] { Printer(800m, 8000, true, 250), Printer(1600m, 8000, true, 350), Printer(5000m, 1000, false, 500) };

        Assert.Equal(0.30m, PrinterCostCalculator.FleetHourlyRate(fleet));
        Assert.Equal(300, PrinterCostCalculator.FleetAveragePowerWatts(fleet));
        Assert.Equal(0m, PrinterCostCalculator.FleetHourlyRate([]));
        Assert.Null(PrinterCostCalculator.FleetAveragePowerWatts([Printer(800m, 8000, true, null)]));
    }
}
