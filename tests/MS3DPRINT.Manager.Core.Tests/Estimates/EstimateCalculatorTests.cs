using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class EstimateCalculatorTests
{
    [Fact]
    public void Calculate_applies_failure_margin_then_discount()
    {
        var result = EstimateCalculator.Calculate(new EstimateInputs(MaterialCost: 10m, MachineCost: 5m, MarginPercent: 20m, FailureRatePercent: 10m, DiscountPercent: 5m));
        Assert.Equal(16.67m, result.ExpectedCost);
        Assert.Equal(20m, result.SalePrice);
        Assert.Equal(19m, result.FinalPrice);
    }

    [Fact]
    public void Calculate_adds_the_flat_fee_per_piece_after_the_margin()
    {
        var result = EstimateCalculator.Calculate(new EstimateInputs(MaterialCost: 100m, MarginPercent: 10m, Quantity: 40, MinimumPerPiece: 5m));
        Assert.Equal(200m, result.FlatFee);
        Assert.Equal(310m, result.SalePrice);
        Assert.Equal(310m, result.FinalPrice);
    }

    [Fact]
    public void Calculate_applies_the_discount_to_the_flat_fee_too()
    {
        var result = EstimateCalculator.Calculate(new EstimateInputs(MaterialCost: 10m, DiscountPercent: 10m, Quantity: 2, MinimumPerPiece: 5m));
        Assert.Equal(20m, result.SalePrice);
        Assert.Equal(18m, result.FinalPrice);
    }

    [Fact]
    public void Calculate_rejects_a_quantity_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EstimateCalculator.Calculate(new EstimateInputs(Quantity: 0)));
    }

    [Fact]
    public void Calculate_includes_all_cost_categories()
    {
        var result = EstimateCalculator.Calculate(new EstimateInputs(
            MaterialCost: 1m, MachineCost: 2m, ElectricityCost: 3m, DesignCost: 4m, LaborCost: 5m,
            PostProcessingCost: 6m, ConsumablesCost: 7m, PackagingShippingCost: 8m, MiscCost: 9m));

        Assert.Equal(45m, result.RawCost);
        Assert.Equal(45m, result.ExpectedCost);
        Assert.Equal(45m, result.SalePrice);
        Assert.Equal(45m, result.FinalPrice);
    }

    [Fact]
    public void Calculate_rejects_a_negative_cost()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EstimateCalculator.Calculate(new EstimateInputs(MaterialCost: 10m, MiscCost: -1m)));
}
