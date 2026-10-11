namespace MS3DPRINT.Manager.Core.Estimates;

public sealed record EstimateInputs(decimal MaterialCost = 0, decimal MachineCost = 0, decimal ElectricityCost = 0,
    decimal DesignCost = 0, decimal LaborCost = 0, decimal PostProcessingCost = 0, decimal ConsumablesCost = 0,
    decimal PackagingShippingCost = 0, decimal MiscCost = 0, decimal MarginPercent = 0, decimal FailureRatePercent = 0, decimal DiscountPercent = 0);

public sealed record EstimateResult(decimal RawCost, decimal ExpectedCost, decimal SalePrice, decimal FinalPrice);

public static class EstimateCalculator
{
    public static EstimateResult Calculate(EstimateInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.FailureRatePercent is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(input.FailureRatePercent));
        if (input.MarginPercent < 0 || input.DiscountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException();
        var raw = input.MaterialCost + input.MachineCost + input.ElectricityCost + input.DesignCost + input.LaborCost + input.PostProcessingCost + input.ConsumablesCost + input.PackagingShippingCost + input.MiscCost;
        var expected = Math.Round(raw / (1 - input.FailureRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var sale = Math.Round(expected * (1 + input.MarginPercent / 100m), 2, MidpointRounding.AwayFromZero);
        return new(raw, expected, sale, Math.Round(sale * (1 - input.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero));
    }
}
