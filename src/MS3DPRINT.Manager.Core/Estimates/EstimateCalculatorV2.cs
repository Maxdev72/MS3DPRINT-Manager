namespace MS3DPRINT.Manager.Core.Estimates;

/// <summary>Expected production cost and profitability. All monetary arithmetic is decimal; only invoice prices are rounded.</summary>
public static class EstimateCalculatorV2
{
    public static decimal Money(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static decimal CeilingCent(decimal amount) => decimal.Ceiling(amount * 100m) / 100m;

    public static int ProductionRuns(int quantity, bool wholeOrder, int piecesPerPlate)
    {
        if (quantity < 1 || piecesPerPlate < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "La quantité doit être positive et la capacité du plateau ne peut pas être négative.");
        return !wholeOrder ? quantity : piecesPerPlate == 0 ? 1 : (int)decimal.Ceiling(quantity / (decimal)piecesPerPlate);
    }

    public static decimal ChargeRatePercent(EstimateCharges charges)
    {
        foreach (var rate in new[] { charges.SalesSocialPercent, charges.ServicesBicSocialPercent, charges.ServicesBncSocialPercent, charges.ContributionPercent, charges.CommercialPercent })
            if (rate is < 0 or >= 100) throw new ArgumentOutOfRangeException(nameof(charges), "Chaque taux de charges doit être compris entre 0 et 100 % (100 exclu).");
        if (charges.SalesSharePercent < 0 || charges.ServicesBicSharePercent < 0 || charges.SalesSharePercent + charges.ServicesBicSharePercent > 100)
            throw new ArgumentOutOfRangeException(nameof(charges), "Les parts de chiffre d’affaires doivent totaliser au plus 100 % ; le solde correspond aux services BNC.");
        var social = (charges.SalesSocialPercent * charges.SalesSharePercent + charges.ServicesBicSocialPercent * charges.ServicesBicSharePercent
            + charges.ServicesBncSocialPercent * (100 - charges.SalesSharePercent - charges.ServicesBicSharePercent)) / 100m;
        var total = (charges.SocialEnabled ? social : 0) + (charges.ContributionsEnabled ? charges.ContributionPercent : 0) + (charges.CommercialEnabled ? charges.CommercialPercent : 0);
        if (total >= 100) throw new ArgumentOutOfRangeException(nameof(charges), "Le total des charges proportionnelles doit être inférieur à 100 %.");
        return total;
    }

    public static EstimateResult Calculate(EstimateInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var options = input.V2 ?? new();
        if (input.Quantity < 1 || input.FailureRatePercent is < 0 or >= 100 || input.MarginPercent < 0 || input.DiscountPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(input), "Quantité ≥ 1, échec entre 0 et 100 % exclu, marge positive et remise entre 0 et 100 % requis.");
        foreach (var cost in new[] { input.MaterialCost, input.MachineCost, input.ElectricityCost, input.DesignCost, input.LaborCost,
            input.PostProcessingCost, input.ConsumablesCost, input.PackagingShippingCost, input.MiscCost, input.MinimumPerPiece, options.GeneralFixedCost }) NonNegative(cost);
        if (options.CommercialPrice is { } commercial) NonNegative(commercial);
        if (!Enum.IsDefined(options.PricingMode) || !Enum.IsDefined(options.FeeKind)) throw new ArgumentException("Mode de tarification inconnu.");
        var charges = ChargeRatePercent(options.Charges ?? new()) / 100m;
        var margin = input.MarginPercent / 100m;
        if (options.PricingMode == EstimatePricingMode.TargetMargin && charges + margin >= 1)
            throw new ArgumentOutOfRangeException(nameof(input), "Charges + marge cible doivent être strictement inférieures à 100 %.");

        var production = options.Production;
        var subcontracting = options.Subcontracting;
        var subcontractingCost = 0m;
        if (subcontracting is not null)
        {
            NonNegative(subcontracting.PurchaseCost); NonNegative(subcontracting.ShippingCost);
            if (production is not null || input.MaterialCost != 0 || input.MachineCost != 0 || input.ElectricityCost != 0)
                throw new ArgumentException("La sous-traitance remplace les coûts d’impression internes : ne les cumulez pas.");
            subcontractingCost = subcontracting.PurchaseCost + subcontracting.ShippingCost;
        }
        var runs = subcontracting is not null ? 1 : ProductionRuns(input.Quantity, input.PrintDataForWholeOrder, production?.PiecesPerPlate ?? 0);
        decimal material = input.MaterialCost, machine = input.MachineCost, electricity = input.ElectricityCost, grams = 0, hours = 0;
        if (production is { } p)
        {
            if (material != 0 || machine != 0 || electricity != 0) throw new ArgumentException("Utilisez les données de production ou les coûts d’impression directs, sans les cumuler.");
            foreach (var value in new[] { p.WeightGrams, p.ExtraWeightGrams, p.HoursPerRun, p.MaterialPricePerKg, p.MachineHourlyRate,
                p.MaintenanceHourlyRate, p.PricePerKwh, p.ManualElectricityCost }) NonNegative(value);
            if (p.PowerWatts < 0) throw new ArgumentOutOfRangeException(nameof(p.PowerWatts));
            grams = (p.WeightGrams + (p.WeightIncludesSupports ? 0 : p.ExtraWeightGrams)) * runs;
            hours = p.HoursPerRun * runs;
            material = grams * p.MaterialPricePerKg / 1000m;
            if (p.Allocations is { Count: > 0 } allocations)
            {
                if (allocations.Sum(a => a.Hours) != hours) throw new ArgumentException("La somme des heures affectées aux machines doit être égale à l’occupation totale prévue.");
                foreach (var a in allocations)
                {
                    NonNegative(a.Hours); NonNegative(a.HourlyRate); NonNegative(a.MaintenanceHourlyRate);
                    if (a.PowerWatts < 0) throw new ArgumentOutOfRangeException(nameof(a.PowerWatts));
                    machine += a.Hours * (a.HourlyRate + a.MaintenanceHourlyRate);
                    // With an unknown power, manual electricity is explicitly supplied for the missing part per run.
                    electricity += a.Hours * (a.PowerWatts ?? 0) / 1000m * p.PricePerKwh;
                }
                if (allocations.Any(a => a.PowerWatts is null or 0)) electricity += p.ManualElectricityCost * runs;
            }
            else
            {
                machine = hours * (p.MachineHourlyRate + p.MaintenanceHourlyRate);
                electricity = p.PowerWatts is > 0 ? hours * p.PowerWatts.Value / 1000m * p.PricePerKwh : p.ManualElectricityCost * runs;
            }
        }

        var attempt = material + machine + electricity;
        decimal nonRepeated = options.GeneralFixedCost + subcontractingCost, labor = 0, other = options.GeneralFixedCost;
        void Add(decimal amount, CostFrequency frequency, bool laborCategory = false, bool pieceFee = false)
        {
            NonNegative(amount);
            if (!Enum.IsDefined(frequency)) throw new ArgumentException("Nature de poste inconnue.");
            var factor = options.ComplementaryCostsAreTotals && !pieceFee ? 1
                : frequency == CostFrequency.PerAttempt ? runs : frequency == CostFrequency.PerGoodPiece ? input.Quantity : 1;
            var total = amount * factor;
            if (frequency == CostFrequency.PerAttempt && subcontracting is null) attempt += total; else nonRepeated += total;
            if (laborCategory) labor += total; else other += total;
        }
        Add(input.DesignCost, options.DesignFrequency, true);
        Add(input.LaborCost, options.LaborFrequency, true);
        Add(input.PostProcessingCost, options.PostProcessingFrequency, true);
        Add(input.ConsumablesCost, options.ConsumablesFrequency);
        Add(input.PackagingShippingCost, options.PackagingFrequency);
        Add(input.MiscCost, options.MiscFrequency);
        foreach (var item in options.ExtraCosts ?? []) if (item.Enabled) Add(item.Amount, item.Frequency);
        var fee = input.MinimumPerPiece * input.Quantity;
        if (options.FeeKind == PieceFeeKind.RealCost) Add(input.MinimumPerPiece, CostFrequency.PerGoodPiece, pieceFee: true);

        var raw = nonRepeated + attempt;
        var expected = nonRepeated + attempt / (1 - input.FailureRatePercent / 100m);
        var floor = CeilingCent(expected / (1 - charges));
        var recommendedBase = options.PricingMode == EstimatePricingMode.TargetMargin ? expected / (1 - charges - margin) : expected * (1 + margin);
        var recommended = CeilingCent(recommendedBase + (options.FeeKind == PieceFeeKind.CommercialSupplement ? fee : 0));
        var final = Money((options.CommercialPrice ?? recommended) * (1 - input.DiscountPercent / 100m));
        var proportional = final * charges;
        var profit = final - expected - proportional;
        return new(raw, expected, recommended, final, fee,
            new(material, machine, electricity, labor, other, attempt, nonRepeated, expected - raw, floor, recommended,
                charges * 100m, proportional, profit, final == 0 ? 0 : profit / final * 100m, final / input.Quantity, profit / input.Quantity,
                profit < 0, subcontracting is null ? runs : 0, grams, hours, hours / (1 - input.FailureRatePercent / 100m), subcontractingCost));
    }

    private static void NonNegative(decimal value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Les coûts, poids et durées doivent être positifs ou nuls.");
    }
}
