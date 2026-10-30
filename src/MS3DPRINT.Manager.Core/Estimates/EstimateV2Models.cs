namespace MS3DPRINT.Manager.Core.Estimates;

public enum CostFrequency { PerOrder, PerGoodPiece, PerAttempt }
public enum EstimatePricingMode { TargetMargin, CostMarkup }
public enum PieceFeeKind { CommercialSupplement, RealCost }

public sealed record EstimateCostItem(string Name, decimal Amount, CostFrequency Frequency, bool Enabled = true);

/// <summary>Supplier quote for the complete conforming order; independent of internal printing attempts.</summary>
public sealed record EstimateSubcontracting(decimal PurchaseCost, decimal ShippingCost = 0, string? Supplier = null);

/// <summary>Hours cover the whole order, before retries. Rates and power are snapshots, never live printer references.</summary>
public sealed record EstimateMachineAllocation(string Name, decimal Hours, decimal HourlyRate, decimal MaintenanceHourlyRate = 0, int? PowerWatts = null, Guid? PrinterId = null);

public sealed record EstimateProduction(decimal WeightGrams = 0, decimal ExtraWeightGrams = 0, bool WeightIncludesSupports = true,
    decimal HoursPerRun = 0, decimal MaterialPricePerKg = 0, decimal MachineHourlyRate = 0, decimal MaintenanceHourlyRate = 0,
    int? PowerWatts = null, decimal PricePerKwh = 0, decimal ManualElectricityCost = 0, int PiecesPerPlate = 0,
    IReadOnlyList<EstimateMachineAllocation>? Allocations = null, string? PrinterName = null, Guid? PrinterId = null);

/// <summary>Shares of final turnover: BNC is the remainder after sales and BIC. No regulatory rates are inferred.</summary>
public sealed record EstimateCharges(decimal SalesSocialPercent = 0, decimal ServicesBicSocialPercent = 0, decimal ServicesBncSocialPercent = 0,
    decimal SalesSharePercent = 100, decimal ServicesBicSharePercent = 0, decimal ContributionPercent = 0, decimal CommercialPercent = 0,
    bool SocialEnabled = true, bool ContributionsEnabled = true, bool CommercialEnabled = true);

public sealed record EstimateV2Options(EstimateProduction? Production = null, EstimateCharges? Charges = null,
    EstimatePricingMode PricingMode = EstimatePricingMode.TargetMargin, PieceFeeKind FeeKind = PieceFeeKind.CommercialSupplement,
    decimal? CommercialPrice = null, decimal GeneralFixedCost = 0,
    CostFrequency DesignFrequency = CostFrequency.PerOrder, CostFrequency LaborFrequency = CostFrequency.PerAttempt,
    CostFrequency PostProcessingFrequency = CostFrequency.PerGoodPiece, CostFrequency ConsumablesFrequency = CostFrequency.PerAttempt,
    CostFrequency PackagingFrequency = CostFrequency.PerOrder, CostFrequency MiscFrequency = CostFrequency.PerOrder,
    IReadOnlyList<EstimateCostItem>? ExtraCosts = null, string? ChargePresetId = null,
    bool ComplementaryCostsAreTotals = false, EstimateSubcontracting? Subcontracting = null,
    IReadOnlyList<QuantityPriceTier>? QuantityTiers = null);

public sealed record EstimateProfitability(decimal MaterialCost, decimal MachineCost, decimal ElectricityCost, decimal LaborCost, decimal OtherCost,
    decimal AttemptCost, decimal NonRepeatedCost, decimal FailureProvision, decimal FloorPrice, decimal RecommendedPrice,
    decimal ChargeRatePercent, decimal ProportionalCharges, decimal EstimatedResult, decimal ActualMarginPercent,
    decimal UnitPrice, decimal UnitResult, bool BelowBreakEven, int Runs, decimal TotalWeightGrams, decimal TotalMachineHours,
    decimal ExpectedMachineHours, decimal SubcontractingCost = 0);
