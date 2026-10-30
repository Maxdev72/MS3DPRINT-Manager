using System.Text.Json;
using MS3DPRINT.Manager.Core.Estimates;

namespace MS3DPRINT.Manager.Core.Tests.Estimates;

public sealed class QuantityPricingTests
{
    [Fact]
    public void Defaults_discount_the_reference_price_once_and_recalculate_lot_profit()
    {
        var input=new EstimateInputs(Quantity:1,DesignCost:20,PostProcessingCost:10,DiscountPercent:50,EngineVersion:2,
            V2:new(CommercialPrice:100,Charges:new(CommercialPercent:10),ComplementaryCostsAreTotals:true));
        var rows=QuantityPricing.Calculate(input,QuantityPricing.Defaults());
        Assert.Equal(new[]{0m,5m,10m,13m,25m},rows.Select(r=>r.Tier.DiscountPercent));
        Assert.Equal(75m,rows[^1].UnitPrice);Assert.Equal(3750m,rows[^1].TotalPrice);
        Assert.Equal(520m,rows[^1].ExpectedCost);Assert.Equal(2855m,rows[^1].Profit);
        Assert.Equal(50m,input.DiscountPercent);
    }

    [Fact]
    public void Grid_is_editable_serializable_and_rejects_invalid_discounts()
    {
        var tiers=QuantityPricing.Defaults().Select(t=>t.Quantity==5?t with {DiscountPercent=6.5m}:t).ToArray();
        var input=new EstimateInputs(EngineVersion:2,V2:new(QuantityTiers:tiers));
        var saved=JsonSerializer.Deserialize<EstimateInputs>(JsonSerializer.Serialize(input))!;
        Assert.Equal(6.5m,saved.V2!.QuantityTiers![1].DiscountPercent);
        Assert.Throws<ArgumentException>(()=>QuantityPricing.Calculate(input,[new(5,101)]));
        Assert.Equal(5m,QuantityPricing.Defaults()[1].DiscountPercent);
    }

    [Fact]
    public void Whole_order_projection_scales_printing_and_total_piece_costs_but_keeps_order_setup()
    {
        var input=new EstimateInputs(Quantity:10,PrintDataForWholeOrder:true,DesignCost:100,PostProcessingCost:20,
            EngineVersion:2,V2:new(Production:new(WeightGrams:100,HoursPerRun:5,MaterialPricePerKg:20,MachineHourlyRate:2),ComplementaryCostsAreTotals:true));
        var rows=QuantityPricing.Calculate(input,[new(1,0),new(50,25)]);
        Assert.Equal(103.2m,rows[0].ExpectedCost);Assert.Equal(260m,rows[1].ExpectedCost);
    }

    [Fact]
    public void Supplier_order_labor_stays_fixed_and_fractional_allocations_remain_exact()
    {
        var input=new EstimateInputs(Quantity:10,LaborCost:20,EngineVersion:2,V2:new(Subcontracting:new(100,10),ComplementaryCostsAreTotals:true));
        Assert.Equal(230m,QuantityPricing.Calculate(input,[new(20,0)])[0].ExpectedCost);
        var printing=new EstimateInputs(Quantity:3,PrintDataForWholeOrder:true,EngineVersion:2,
            V2:new(Production:new(HoursPerRun:1,Allocations:[new("Machine",1,5)])));
        Assert.Single(QuantityPricing.Calculate(printing,[new(5,5)]));
    }

    [Fact]
    public void Loss_is_reported_and_plateau_runs_and_machine_allocations_are_consistent()
    {
        var input=new EstimateInputs(Quantity:10,PrintDataForWholeOrder:true,EngineVersion:2,
            V2:new(CommercialPrice:10,Production:new(HoursPerRun:2,PiecesPerPlate:5,Allocations:[new("Machine",4,5)])));
        var row=QuantityPricing.Calculate(input,[new(20,13)]).Single();
        Assert.Equal(40m,row.ExpectedCost);Assert.True(row.BelowFloor);
    }
}
