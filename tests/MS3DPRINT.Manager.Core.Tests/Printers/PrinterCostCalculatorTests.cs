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
}
