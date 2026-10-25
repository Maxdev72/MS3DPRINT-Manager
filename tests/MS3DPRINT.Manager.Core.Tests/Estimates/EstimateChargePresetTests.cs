using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateChargePresetTests
{
    [Fact]
    public void Standard_2026_rates_apply_without_overwriting_turnover_split_or_other_charges()
    {
        var current = new EstimateCharges(SalesSharePercent: 75, ServicesBicSharePercent: 25, ContributionPercent: .3m, CommercialPercent: 2);
        var catalog = EstimateChargeCatalog.Load();
        var applied = catalog.Apply("fr-micro-2026", current);
        Assert.Equal(12.3m, applied.SalesSocialPercent);
        Assert.Equal(21.2m, applied.ServicesBicSocialPercent);
        Assert.Equal(25.6m, applied.ServicesBncSocialPercent);
        Assert.Equal(75, applied.SalesSharePercent);
        Assert.Equal(25, applied.ServicesBicSharePercent);
        Assert.Equal(.3m, applied.ContributionPercent);
        Assert.Equal(2, applied.CommercialPercent);
        Assert.Equal(16.825m, EstimateCalculatorV2.ChargeRatePercent(applied));
        Assert.Equal(23.2m, catalog.Apply("fr-micro-cipav-2026", current).ServicesBncSocialPercent);
    }

    [Fact]
    public void Training_contribution_is_selected_from_activity_not_inferred_from_sales_or_services()
    {
        var catalog = EstimateChargeCatalog.Load();
        Assert.Equal(.1m, catalog.Training.Single(p => p.Id == "commercial").RatePercent);
        Assert.Equal(.3m, catalog.Training.Single(p => p.Id == "artisan").RatePercent);
        Assert.Equal(.2m, catalog.Training.Single(p => p.Id == "liberal").RatePercent);
        Assert.All(catalog.Presets, p => { Assert.Equal(2026, p.Year); Assert.Contains("https://", p.SourceUrl); });
    }

    [Fact]
    public void Workspace_defaults_are_opt_in_isolated_and_do_not_migrate_existing_estimates()
    {
        var root = Path.Combine(Path.GetTempPath(), "ms3d-charge-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new WorkshopEstimateSettingsStore(Path.Combine(root, "a"));
            var b = new WorkshopEstimateSettingsStore(Path.Combine(root, "b"));
            Assert.Null(a.Load()); Assert.Null(b.Load());
            var settings = new WorkshopEstimateSettings(new(12.3m, 21.2m, 25.6m, 60, 40, .3m), "fr-micro-2026", ElectricityPricePerKwh: .3m, MarginPercent: 25, FailureRatePercent: 8, PieceFee: 2, GeneralFixedCost: 7);
            a.Save(settings);
            Assert.Equal(settings, a.Load());
            Assert.Null(b.Load());
            Assert.False(Directory.Exists(Path.Combine(root, "a", ".ms3dprint-manager", "estimates")));
            Assert.Equal(0, new EstimateCharges().SalesSocialPercent); // The general calculator stays neutral.
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Invalid_defaults_and_unknown_presets_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => EstimateChargeCatalog.Load().Apply("inconnu", new()));
        var root = Path.Combine(Path.GetTempPath(), "ms3d-charge-invalid-" + Guid.NewGuid().ToString("N"));
        try { Assert.Throws<ArgumentOutOfRangeException>(() => new WorkshopEstimateSettingsStore(root).Save(new(new(SalesSocialPercent: 100)))); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
