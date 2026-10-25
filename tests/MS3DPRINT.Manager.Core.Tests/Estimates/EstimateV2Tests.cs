using System.Text.Json;
using MS3DPRINT.Manager.Core.Estimates;
using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateV2Tests
{
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void Supplier_quote_is_an_order_cost_without_internal_failures_and_keeps_real_piece_fees(int quantity)
    {
        var options = new EstimateV2Options(Charges: new(CommercialPercent: 20), FeeKind: PieceFeeKind.RealCost,
            ComplementaryCostsAreTotals: true, Subcontracting: new(100, 10, "Partenaire"));
        var input = Input(options, quantity) with { LaborCost = 20, MinimumPerPiece = 2, FailureRatePercent = 25, MarginPercent = 20 };
        var result = EstimateCalculator.Calculate(input);
        Assert.Equal(130 + 2 * quantity, result.ExpectedCost);
        Assert.Equal(110, result.V2!.SubcontractingCost);
        Assert.Equal(0, result.V2.FailureProvision); Assert.Equal(0, result.V2.Runs);
        var copy = JsonSerializer.Deserialize<EstimateInputs>(JsonSerializer.Serialize(input))!;
        Assert.Equal(result, EstimateCalculator.Calculate(copy));
        Assert.Throws<ArgumentException>(() => EstimateCalculator.Calculate(input with { V2 = options with { Production = new() } }));
        Assert.Throws<ArgumentException>(() => EstimateCalculator.Calculate(input with { MaterialCost = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => EstimateCalculator.Calculate(input with { V2 = options with { Subcontracting = new(-1) } }));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 10)]
    public void Order_total_costs_do_not_scale_with_runs_and_piece_fees_still_scale(bool whole, int capacity)
    {
        var options = new EstimateV2Options(Production: new(PiecesPerPlate: capacity), FeeKind: PieceFeeKind.RealCost,
            ExtraCosts: [new("Préparation", 30, CostFrequency.PerAttempt), new("Contrôle", 20, CostFrequency.PerGoodPiece)], ComplementaryCostsAreTotals: true);
        var result = EstimateCalculator.Calculate(new(Quantity: 40, PrintDataForWholeOrder: whole, DesignCost: 50, LaborCost: 20,
            PostProcessingCost: 80, ConsumablesCost: 10, MinimumPerPiece: 2, FailureRatePercent: 20, EngineVersion: 2, V2: options));
        Assert.Equal(290, result.RawCost); // 50 + 20 + 80 + 10 + 30 + 20 + 40 * 2
        Assert.Equal(305, result.ExpectedCost); // 230 non répétés + 60 / 0,8
        var saved = JsonSerializer.Deserialize<EstimateInputs>(JsonSerializer.Serialize(new EstimateInputs(Quantity: 40, MinimumPerPiece: 2, EngineVersion: 2, V2: options)))!;
        Assert.True(saved.V2!.ComplementaryCostsAreTotals);
        Assert.Equal(130, EstimateCalculator.Calculate(saved).RawCost); // 30 + 20 + 80
    }

    private static EstimateInputs Input(EstimateV2Options? options = null, int quantity = 1, bool whole = false) =>
        new(Quantity: quantity, PrintDataForWholeOrder: whole, EngineVersion: 2, V2: options ?? new());

    [Fact]
    public void Legacy_forty_piece_example_keeps_all_historical_rounding()
    {
        var r = EstimateCalculator.Calculate(new(MaterialCost: 132.10m, Quantity: 40, FailureRatePercent: 5, MarginPercent: 35, MinimumPerPiece: 5));
        Assert.Equal(132.10m, r.RawCost);
        Assert.Equal(139.05m, r.ExpectedCost);
        Assert.Equal(187.72m, r.SalePrice - r.FlatFee);
        Assert.Equal(387.72m, r.FinalPrice);
    }

    [Theory]
    [InlineData(1, false, 0, 1, 10, 2)]
    [InlineData(40, false, 0, 40, 400, 80)]
    [InlineData(40, true, 0, 1, 10, 2)]
    [InlineData(40, true, 10, 4, 40, 8)]
    [InlineData(41, true, 10, 5, 50, 10)]
    public void Production_counts_runs_without_multiplying_whole_plate_data(int quantity, bool whole, int capacity, int runs, decimal grams, decimal hours)
    {
        var r = EstimateCalculator.Calculate(Input(new(Production: new(WeightGrams: 10, HoursPerRun: 2, MaterialPricePerKg: 20, MachineHourlyRate: 3, PiecesPerPlate: capacity)), quantity, whole));
        Assert.Equal(runs, r.V2!.Runs);
        Assert.Equal(grams, r.V2.TotalWeightGrams);
        Assert.Equal(hours, r.V2.TotalMachineHours);
        Assert.Equal(grams * .02m + hours * 3, r.RawCost);
    }

    [Fact]
    public void Failure_repeats_only_attempt_costs_not_design_shipping_or_good_piece_work()
    {
        var r = EstimateCalculator.Calculate(Input() with { MaterialCost = 100, DesignCost = 30, PackagingShippingCost = 10, PostProcessingCost = 10, FailureRatePercent = 5 });
        Assert.Equal(150, r.RawCost);
        Assert.InRange(r.ExpectedCost, 155.263157m, 155.263158m);
        Assert.InRange(r.V2!.FailureProvision, 5.263157m, 5.263158m);
    }

    [Fact]
    public void Frequencies_keep_setup_once_and_repeat_only_consumables_on_each_plate_attempt()
    {
        var options = new EstimateV2Options(Production: new(PiecesPerPlate: 10), ExtraCosts:
            [new("Scan / préparation", 50, CostFrequency.PerOrder), new("Contrôle", 2, CostFrequency.PerGoodPiece), new("Consommables", 10, CostFrequency.PerAttempt)]);
        var r = EstimateCalculator.Calculate(Input(options, 40, true) with { FailureRatePercent = 20 });
        Assert.Equal(170, r.RawCost);
        Assert.Equal(180, r.ExpectedCost); // 50 + 80 + 40/.8
    }

    [Fact]
    public void Selected_machines_use_their_own_hours_rates_maintenance_and_power()
    {
        var p = new EstimateProduction(HoursPerRun: 5, MachineHourlyRate: 999, PowerWatts: 999,
            Allocations: [new("A", 2, 1, .5m, 100), new("B", 3, 4, 1, 200)], PricePerKwh: .25m);
        var r = EstimateCalculator.Calculate(Input(new(Production: p)));
        Assert.Equal(18, r.V2!.MachineCost);
        Assert.Equal(.2m, r.V2.ElectricityCost);
        Assert.Equal(5, r.V2.TotalMachineHours);
    }

    [Fact]
    public void Allocation_hours_must_cover_exactly_the_planned_occupation()
    {
        var p = new EstimateProduction(HoursPerRun: 5, Allocations: [new("A", 2, 1)]);
        Assert.Throws<ArgumentException>(() => EstimateCalculator.Calculate(Input(new(Production: p))));
    }

    [Fact]
    public void Supports_are_added_only_when_the_slicer_weight_excludes_them()
    {
        var p = new EstimateProduction(WeightGrams: 100, ExtraWeightGrams: 20, MaterialPricePerKg: 10);
        Assert.Equal(1, EstimateCalculator.Calculate(Input(new(Production: p))).RawCost);
        Assert.Equal(1.2m, EstimateCalculator.Calculate(Input(new(Production: p with { WeightIncludesSupports = false }))).RawCost);
    }

    [Fact]
    public void Target_margin_covers_charges_and_uses_unrounded_costs()
    {
        var r = EstimateCalculator.Calculate(Input(new(Charges: new(SalesSocialPercent: 21.2m))) with { MaterialCost = 139.05m, MarginPercent = 20 });
        Assert.Equal(236.48m, r.FinalPrice);
        Assert.Equal(176.46m, r.V2!.FloorPrice);
        Assert.InRange(r.V2.ActualMarginPercent, 20m, 20.001m);
    }

    [Fact]
    public void Mixed_turnover_and_optional_charges_are_weighted_on_final_price()
    {
        var charges = new EstimateCharges(SalesSocialPercent: 10, ServicesBicSocialPercent: 20, ServicesBncSocialPercent: 30,
            SalesSharePercent: 50, ServicesBicSharePercent: 25, ContributionPercent: 1, CommercialPercent: 2);
        var r = EstimateCalculator.Calculate(Input(new(Charges: charges, CommercialPrice: 200)) with { DesignCost = 100 });
        Assert.Equal(20.5m, r.V2!.ChargeRatePercent);
        Assert.Equal(41, r.V2.ProportionalCharges);
        Assert.Equal(59, r.V2.EstimatedResult);
        Assert.Equal(29.5m, r.V2.ActualMarginPercent);
        Assert.Equal(0, EstimateCalculator.Calculate(Input(new(Charges: charges with { SocialEnabled = false, ContributionsEnabled = false, CommercialEnabled = false }, CommercialPrice: 200)) with { DesignCost = 100 }).V2!.ProportionalCharges);
    }

    [Fact]
    public void Real_piece_fee_enters_cost_before_margin_commercial_fee_enters_final_turnover()
    {
        var real = EstimateCalculator.Calculate(Input(new(FeeKind: PieceFeeKind.RealCost), 40) with { MinimumPerPiece = 5, DesignCost = 100, MarginPercent = 20 });
        Assert.Equal(300, real.ExpectedCost);
        Assert.Equal(375, real.FinalPrice);
        var commercial = EstimateCalculator.Calculate(Input(new(Charges: new(SalesSocialPercent: 10)), 40) with { MinimumPerPiece = 5, DesignCost = 100 });
        Assert.Equal(100, commercial.ExpectedCost);
        Assert.Equal(311.12m, commercial.FinalPrice);
        Assert.Equal(31.112m, commercial.V2!.ProportionalCharges);
    }

    [Fact]
    public void Discount_and_manual_price_recalculate_deficit_and_unit_result()
    {
        var r = EstimateCalculator.Calculate(Input(new(Charges: new(SalesSocialPercent: 20), CommercialPrice: 100), 40) with { DesignCost = 100, DiscountPercent = 10 });
        Assert.Equal(90, r.FinalPrice);
        Assert.Equal(18, r.V2!.ProportionalCharges);
        Assert.Equal(-28, r.V2.EstimatedResult);
        Assert.Equal(2.25m, r.V2.UnitPrice);
        Assert.Equal(-.7m, r.V2.UnitResult);
        Assert.True(r.V2.BelowBreakEven);
        var zero = EstimateCalculator.Calculate(Input(new(CommercialPrice: 0)) with { DesignCost = 100 });
        Assert.Equal(0, zero.V2!.ActualMarginPercent);
        Assert.True(zero.V2.BelowBreakEven);
    }

    [Fact]
    public void Markup_mode_remains_available_in_v2()
    {
        var r = EstimateCalculator.Calculate(Input(new(PricingMode: EstimatePricingMode.CostMarkup)) with { DesignCost = 100, MarginPercent = 35 });
        Assert.Equal(135, r.FinalPrice);
    }

    [Fact]
    public void No_intermediate_rounding_and_invoice_rounding_away_from_zero()
    {
        var r = EstimateCalculator.Calculate(Input(new(CommercialPrice: 1.005m), 3) with { MaterialCost = .3333m, MachineCost = .3333m, ElectricityCost = .3334m });
        Assert.Equal(1, r.ExpectedCost);
        Assert.Equal(1.01m, r.FinalPrice);
        Assert.Equal(.01m, r.V2!.EstimatedResult);
    }

    [Theory]
    [InlineData(100, 0, 1)]
    [InlineData(0, 100, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(-1, 0, 1)]
    [InlineData(0, 0, 0)]
    public void Invalid_target_denominator_rates_and_quantities_are_rejected(decimal charges, decimal margin, int quantity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => EstimateCalculator.Calculate(Input(new(Charges: new(SalesSocialPercent: charges)), quantity) with { MarginPercent = margin }));

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(120)]
    public void Invalid_failure_rate_is_rejected(decimal failure)
        => Assert.Throws<ArgumentOutOfRangeException>(() => EstimateCalculator.Calculate(Input() with { FailureRatePercent = failure }));

    [Fact]
    public void Old_json_defaults_to_legacy_and_stored_result_is_preserved()
    {
        var input = JsonSerializer.Deserialize<EstimateInputs>("{\"MaterialCost\":132.10,\"Quantity\":40,\"MinimumPerPiece\":5,\"FailureRatePercent\":5,\"MarginPercent\":35}")!;
        Assert.Equal(1, input.EngineVersion);
        Assert.Equal(387.72m, EstimateCalculator.Calculate(input).FinalPrice);
        var root = Path.Combine(Path.GetTempPath(), "ms3d-v2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EstimateStore(root);
            var p = new EstimateProfile(Guid.NewGuid(), "Archive", "PLA", "A", input, new(1, 2, 3, 4), DateTimeOffset.UtcNow);
            store.Create(p);
            Assert.Equal(4, Assert.Single(store.LoadAll()).Result.FinalPrice);
            var v2 = Input(new(Production: new(HoursPerRun: 2, Allocations: [new("A", 2, .123456m)]), Charges: new(SalesSocialPercent: 10), ExtraCosts: [new("Scan", 40, CostFrequency.PerOrder)]));
            store.Update(p with { Inputs = v2, Result = EstimateCalculator.Calculate(v2) });
            var loaded = Assert.Single(store.LoadAll());
            Assert.Equal(.123456m, loaded.Inputs.V2!.Production!.Allocations![0].HourlyRate);
            Assert.Equal(loaded.Result.FinalPrice, EstimateCalculator.Calculate(loaded.Inputs).FinalPrice);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void V2_does_not_double_count_physical_production_and_direct_print_costs()
        => Assert.Throws<ArgumentException>(() => EstimateCalculator.Calculate(Input(new(Production: new())) with { MachineCost = 10 }));
}
