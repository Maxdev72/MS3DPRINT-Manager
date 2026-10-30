namespace MS3DPRINT.Manager.Core.Estimates;

public sealed record QuantityPriceTier(int Quantity, decimal DiscountPercent);
public sealed record QuantityPriceResult(QuantityPriceTier Tier, decimal ReferenceUnitPrice, decimal UnitPrice,
    decimal TotalPrice, decimal ExpectedCost, decimal Profit, decimal MarginPercent, bool BelowFloor);

/// <summary>Commercial grid relative to one reference unit. Costs are projections, not fresh slicer/supplier quotes.</summary>
public static class QuantityPricing
{
    public static IReadOnlyList<QuantityPriceTier> Defaults() => [new(1,0),new(5,5),new(10,10),new(20,13),new(50,25)];

    public static IReadOnlyList<QuantityPriceResult> Calculate(EstimateInputs source, IReadOnlyList<QuantityPriceTier> tiers)
    {
        if (source.EngineVersion != 2) throw new ArgumentException("Convertissez le devis pour utiliser les tarifs par quantité.");
        EstimateCalculator.Calculate(source);
        if (tiers.Count is < 1 or > 50 || tiers.Any(t=>t is null || t.Quantity is < 1 or > 100000 || t.DiscountPercent is < 0 or > 100)
            || tiers.Select(t=>t.Quantity).Distinct().Count()!=tiers.Count) throw new ArgumentException("Vérifiez les quantités et les remises (0 à 100 %).");
        var reference = source.V2?.CommercialPrice is { } price ? price/source.Quantity : EstimateCalculator.Calculate(Project(source,1)).SalePrice;
        return tiers.Select(t=>
        {
            var unit=EstimateCalculatorV2.Money(reference*(1-t.DiscountPercent/100m));
            var input=Project(source,t.Quantity);
            var result=EstimateCalculator.Calculate(input with { V2=input.V2! with {CommercialPrice=unit*t.Quantity} });
            var p=result.V2!;
            return new QuantityPriceResult(t,reference,unit,result.FinalPrice,result.ExpectedCost,p.EstimatedResult,p.ActualMarginPercent,p.BelowBreakEven);
        }).ToArray();
    }

    private static EstimateInputs Project(EstimateInputs input,int quantity)
    {
        var o=input.V2??new();var p=o.Production;
        var ratio=quantity/(decimal)input.Quantity;
        var originalRuns=EstimateCalculatorV2.ProductionRuns(input.Quantity,input.PrintDataForWholeOrder,p?.PiecesPerPlate??0);
        var whole=input.PrintDataForWholeOrder;
        // Unknown whole-order capacity: project weight/time per piece. Known plate capacity uses complete plate runs.
        var wholeWithoutCapacity=whole && (p?.PiecesPerPlate??0)==0;
        if (p is not null && wholeWithoutCapacity)
        {
            p=p with {WeightGrams=p.WeightGrams/input.Quantity,ExtraWeightGrams=p.ExtraWeightGrams/input.Quantity,
                HoursPerRun=p.HoursPerRun/input.Quantity,ManualElectricityCost=p.ManualElectricityCost/input.Quantity};
            whole=false;
        }
        var runs=EstimateCalculatorV2.ProductionRuns(quantity,whole,p?.PiecesPerPlate??0);
        var attemptRatio=o.Subcontracting is not null?1:wholeWithoutCapacity?ratio:runs/(decimal)originalRuns;
        if (p?.Allocations is {Count:>0} allocations)
        {
            var total=p.HoursPerRun*runs;var originalTotal=allocations.Sum(a=>a.Hours);decimal used=0;
            var last=allocations.ToList().FindLastIndex(a=>a.Hours>0);
            var projected=allocations.Select((a,i)=>
            {
                var hours=i==last?total-used:originalTotal==0?0:a.Hours/originalTotal*total;
                used+=hours;return a with {Hours=hours};
            }).ToArray();
            p=p with {Allocations=projected};
        }
        decimal Cost(decimal amount,CostFrequency frequency) => !o.ComplementaryCostsAreTotals?amount
            : amount*(frequency==CostFrequency.PerOrder?1:frequency==CostFrequency.PerAttempt?attemptRatio:ratio);
        var options=o with {Production=p,CommercialPrice=null,QuantityTiers=null,
            Subcontracting=o.Subcontracting is { } s?s with {PurchaseCost=s.PurchaseCost*ratio}:null,
            ExtraCosts=o.ExtraCosts?.Select(c=>c with {Amount=Cost(c.Amount,c.Frequency)}).ToArray()};
        return input with {Quantity=quantity,PrintDataForWholeOrder=whole,DiscountPercent=0,V2=options,
            MaterialCost=input.MaterialCost*ratio,MachineCost=input.MachineCost*ratio,ElectricityCost=input.ElectricityCost*ratio,
            DesignCost=Cost(input.DesignCost,o.DesignFrequency),LaborCost=Cost(input.LaborCost,o.LaborFrequency),
            PostProcessingCost=Cost(input.PostProcessingCost,o.PostProcessingFrequency),ConsumablesCost=Cost(input.ConsumablesCost,o.ConsumablesFrequency),
            PackagingShippingCost=Cost(input.PackagingShippingCost,o.PackagingFrequency),MiscCost=Cost(input.MiscCost,o.MiscFrequency)};
    }
}
