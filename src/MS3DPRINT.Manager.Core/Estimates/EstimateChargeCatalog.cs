using System.Text.Json;

namespace MS3DPRINT.Manager.Core.Estimates;

public sealed record EstimateChargePreset(string Id, string Name, int Year, decimal SalesRate, decimal ServicesBicRate, decimal ServicesBncRate, string SourceUrl)
{
    public override string ToString() => Name;
}
public sealed record EstimateTrainingPreset(string Id, string Name, decimal RatePercent)
{
    public override string ToString() => Name;
}
public sealed record EstimateChargeCatalog(IReadOnlyList<EstimateChargePreset> Presets, IReadOnlyList<EstimateTrainingPreset> Training)
{
    public static EstimateChargeCatalog Load()
    {
        using var stream = typeof(EstimateChargeCatalog).Assembly.GetManifestResourceStream("MS3DPRINT.Manager.Core.Estimates.charge-presets.json")
            ?? throw new InvalidOperationException("Le catalogue des cotisations est introuvable.");
        var catalog = JsonSerializer.Deserialize<EstimateChargeCatalog>(stream) ?? throw new JsonException("Le catalogue des cotisations est vide.");
        foreach (var preset in catalog.Presets)
            EstimateCalculatorV2.ChargeRatePercent(new(preset.SalesRate, preset.ServicesBicRate, preset.ServicesBncRate));
        return catalog;
    }

    /// <summary>Updates social rates only. Turnover shares, CFP and commissions belong to the user's activity.</summary>
    public EstimateCharges Apply(string id, EstimateCharges current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var preset = Presets.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("Ce barème de cotisations est inconnu.", nameof(id));
        return current with { SalesSocialPercent = preset.SalesRate, ServicesBicSocialPercent = preset.ServicesBicRate, ServicesBncSocialPercent = preset.ServicesBncRate, SocialEnabled = true };
    }
}
public sealed record WorkshopEstimateSettings(EstimateCharges Charges, string? PresetId = null,
    decimal ElectricityPricePerKwh = .25m, decimal MarginPercent = 30m, decimal FailureRatePercent = 5m,
    decimal PieceFee = 5m, PieceFeeKind FeeKind = PieceFeeKind.CommercialSupplement,
    EstimatePricingMode PricingMode = EstimatePricingMode.TargetMargin, decimal GeneralFixedCost = 0,
    CostFrequency DesignFrequency = CostFrequency.PerOrder, CostFrequency LaborFrequency = CostFrequency.PerAttempt,
    CostFrequency PostProcessingFrequency = CostFrequency.PerGoodPiece, CostFrequency ConsumablesFrequency = CostFrequency.PerAttempt,
    CostFrequency PackagingFrequency = CostFrequency.PerOrder, CostFrequency MiscFrequency = CostFrequency.PerOrder)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public EstimateV2Options Options => new(Charges: Charges, PricingMode: PricingMode, FeeKind: FeeKind, GeneralFixedCost: GeneralFixedCost,
        DesignFrequency: DesignFrequency, LaborFrequency: LaborFrequency, PostProcessingFrequency: PostProcessingFrequency,
        ConsumablesFrequency: ConsumablesFrequency, PackagingFrequency: PackagingFrequency, MiscFrequency: MiscFrequency, ChargePresetId: PresetId);
}
