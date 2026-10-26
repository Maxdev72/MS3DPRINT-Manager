using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Recognition;

internal sealed record AmountResult(ReadField<decimal>? TotalTtc, ReadField<decimal>? TotalHt, ReadField<decimal>? Vat, IReadOnlyList<decimal> Candidates, IReadOnlyList<string> Warnings);

/// <summary>Finds the amount to pay among all the figures of a document, by weighing the words around each figure and checking that HT + VAT = TTC.</summary>
internal static partial class AmountReader
{
    [GeneratedRegex(@"total\s*(?:ttc|t\.t\.c)|net\s*a\s*payer|montant\s*(?:ttc|a\s*payer|du\b|total|regle)|total\s*a\s*payer|reste\s*a\s*payer|somme\s*due|total\s*general|total\s*due|amount\s*due|total\s*amount|grand\s*total|order\s*total|total\s*facture|total\s*commande|total\s*(?:eur|euros?)\b|total\s*a\s*regler|a\s*regler|ttc\s*a\s*payer|total\s*paye", RegexOptions.CultureInvariant)]
    private static partial Regex StrongTotal();

    [GeneratedRegex(@"\btotal\b|\bmontant\b|\bsomme\b|\bpaiement\b|\breglement\b|\bpaye\b|carte\s*b|\bcb\b|visa|mastercard|\bcarte\b|\bespeces?\b|\bpaypal\b|\bttc\b", RegexOptions.CultureInvariant)]
    private static partial Regex MediumTotal();

    [GeneratedRegex(@"total\s*h\.?\s*t|montant\s*h\.?\s*t|sous.?total\s*h\.?\s*t|base\s*h\.?\s*t|net\s*h\.?\s*t|total\s*hors\s*taxes?|hors\s*taxes?|\bh\.?t\b|total\s*net|subtotal|sub-total|net\s*amount|amount\s*excl", RegexOptions.CultureInvariant)]
    private static partial Regex HtLabel();

    [GeneratedRegex(@"\btva\b|t\.v\.a|\bvat\b|\btax\b", RegexOptions.CultureInvariant)]
    private static partial Regex VatLabel();

    [GeneratedRegex(@"sous.?total|remise|reduction|promo|economi|ecotaxe|eco.?participation|frais|port\b|livraison|expedition|shipping|arrondi|pourboire|tip\b", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentLabel();

    [GeneratedRegex(@"acompte|deja\s*(?:regle|verse|paye)|verse|solde\s*precedent|avoir\s*utilise|deduit", RegexOptions.CultureInvariant)]
    private static partial Regex DepositLabel();

    [GeneratedRegex(@"rendu|monnaie|change\b|especes\s*(?:remis|donne|recu)|recu\s*(?:en\s*)?especes|a\s*rendre|remis\b", RegexOptions.CultureInvariant)]
    private static partial Regex CashBackLabel();

    [GeneratedRegex(@"prix\s*unit|\bp\.?\s*u\b|pu\s*h|\bqte\b|quantite|\bunit\b|unitaire|\d+\s*x\s*\d|\bx\s*\d+[.,]\d{2}|le\s*kg|/\s*kg|par\s*kg|\bkg\b|points?\b|fidelite|cagnotte|cumul|economie|tva\s*\d|\d+\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex UnitLabel();

    [GeneratedRegex(@"tva\s*non\s*applicable|293\s*b|exoneration\s*de\s*tva|franchise\s*en\s*base|sans\s*tva|tva\s*:?\s*0\s*[.,]?0*\s*%?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex NoVat();

    [GeneratedRegex(@"(\d{1,2}(?:[.,]\d{1,2})?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex Rate();

    public static AmountResult Read(IReadOnlyList<string> lines, string folded)
    {
        var tokens = TextTools.Amounts(lines);
        var warnings = new List<string>();
        if (tokens.Count == 0) return new(null, null, null, [], warnings);

        var scored = tokens.Select(t => (Token: t, Folded: TextTools.Fold(t.Label))).ToList();
        var max = tokens.Max(t => t.Value);

        ReadField<decimal>? ht = null, vat = null;
        var htCandidates = scored.Where(s => HtLabel().IsMatch(s.Folded) && !VatLabel().IsMatch(s.Folded) && !StrongTotal().IsMatch(s.Folded)).OrderByDescending(s => s.Token.Value).ToList();
        if (htCandidates.Count > 0)
        {
            // "Total HT" printed once for the invoice; the largest is the invoice total, smaller ones are lines or sub-totals.
            var labelled = htCandidates.Where(s => Regex.IsMatch(s.Folded, @"total|montant|net")).ToList();
            var chosen = labelled.Count > 0 ? labelled[0] : htCandidates[0];
            ht = new(chosen.Token.Value, 0.8, Evidence(lines, chosen.Token));
        }
        var vatCandidates = scored.Where(s => VatLabel().IsMatch(s.Folded) && !StrongTotal().IsMatch(s.Folded) && !HtLabel().IsMatch(s.Folded)).ToList();
        // On a line "TVA 20 %  2,00" the figure before the percent sign is a rate, not an amount: only the money-shaped figures count and the largest is the total VAT.
        if (vatCandidates.Count > 0)
        {
            var chosen = vatCandidates.OrderByDescending(s => s.Token.Value).First();
            vat = new(chosen.Token.Value, 0.75, Evidence(lines, chosen.Token));
        }

        var candidates = new List<(MoneyToken Token, double Score, string Why)>();
        foreach (var (token, label) in scored)
        {
            var score = 0.0;
            if (StrongTotal().IsMatch(label)) score += 100;
            else if (MediumTotal().IsMatch(label)) score += 50;
            if (label.Contains("net a payer") || label.Contains("reste a payer") || label.Contains("amount due")) score += 12;
            if (HtLabel().IsMatch(label) && !StrongTotal().IsMatch(label)) score -= 120;
            if (VatLabel().IsMatch(label) && !StrongTotal().IsMatch(label)) score -= 120;
            if (ComponentLabel().IsMatch(label) && !StrongTotal().IsMatch(label)) score -= 45;
            if (DepositLabel().IsMatch(label) && !label.Contains("net a payer") && !label.Contains("reste a payer")) score -= 60;
            if (CashBackLabel().IsMatch(label)) score -= 150;
            if (UnitLabel().IsMatch(label) && !StrongTotal().IsMatch(label)) score -= 40;
            if (label.Contains("total") && label.Contains("articles")) score += 5;
            score += 12.0 * token.Line / Math.Max(1, lines.Count - 1);
            if (token.Value == max) score += 15;
            candidates.Add((token, score, label));
        }

        // Consistency: HT + VAT = TTC, or TTC = HT when VAT does not apply.
        decimal? computed = ht is not null && vat is not null ? ht.Value + vat.Value : null;
        if (computed is { } sum)
            for (var i = 0; i < candidates.Count; i++)
                if (Math.Abs(candidates[i].Token.Value - sum) <= 0.02m) candidates[i] = (candidates[i].Token, candidates[i].Score + 45, candidates[i].Why);
        var noVat = NoVat().IsMatch(folded) && vat is null;

        ReadField<decimal>? ttc = null;
        var ranked = candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Token.Value).ToList();
        if (ranked.Count > 0 && ranked[0].Score >= 40)
        {
            var best = ranked[0];
            // The same total printed several times (header, footer, payment line) makes it more certain.
            var repeats = ranked.Count(c => c.Token.Value == best.Token.Value);
            var confidence = Math.Min(0.98, 0.5 + best.Score / 220.0 + (repeats > 1 ? 0.08 : 0) + (computed is { } s2 && Math.Abs(s2 - best.Token.Value) <= 0.02m ? 0.15 : 0));
            ttc = new(best.Token.Value, Math.Round(confidence, 2), Evidence(lines, best.Token));
        }
        else if (computed is { } known)
            ttc = new(known, 0.7, $"HT {ht!.Value:0.00} + TVA {vat!.Value:0.00}");
        else if (ht is not null && noVat)
            ttc = new(ht.Value, 0.75, ht.Evidence + " (TVA non applicable)");
        else if (ranked.Count > 0 && ranked[0].Score > 0)
            ttc = new(ranked[0].Token.Value, 0.35, Evidence(lines, ranked[0].Token));
        else if (tokens.Count > 0)
            ttc = new(max, 0.25, Evidence(lines, tokens.First(t => t.Value == max)));

        if (ttc is not null && ht is not null && vat is not null && Math.Abs(ht.Value + vat.Value - ttc.Value) > 0.05m)
            warnings.Add($"Le total ({ttc.Value:0.00} €) ne correspond pas à HT + TVA ({ht.Value + vat.Value:0.00} €) : vérifiez le montant.");
        if (ttc is not null && ht is not null && vat is null && ttc.Value > ht.Value + 0.005m)
            vat = new(Math.Round(ttc.Value - ht.Value, 2), 0.6, "TTC − HT");
        if (ttc is not null && ht is null && vat is not null && ttc.Value > vat.Value)
            ht = new(Math.Round(ttc.Value - vat.Value, 2), 0.6, "TTC − TVA");
        if (ttc is not null && ht is not null && vat is null && ttc.Value == ht.Value && noVat) vat = new(0m, 0.8, "TVA non applicable");
        if (ttc is not null && ht is not null && ttc.Value < ht.Value - 0.005m) warnings.Add("Le total TTC est inférieur au total HT : vérifiez le montant.");
        return new(ttc, ht, vat, tokens.Select(t => t.Value).Distinct().ToList(), warnings);
    }

    private static string Evidence(IReadOnlyList<string> lines, MoneyToken token)
    {
        var line = TextTools.Collapse(lines[token.Line]);
        if (!line.Any(char.IsLetter) && token.Line > 0) line = TextTools.Collapse(lines[token.Line - 1]) + " " + line;
        return line.Length > 90 ? line[..90] + "…" : line;
    }

    /// <summary>Most frequent VAT rate printed on the document, when there is one.</summary>
    public static decimal? VatRate(IReadOnlyList<string> lines)
    {
        var rates = new List<decimal>();
        foreach (var line in lines)
        {
            if (!VatLabel().IsMatch(TextTools.Fold(line))) continue;
            foreach (Match m in Rate().Matches(line))
                if (decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate is > 0 and <= 30) rates.Add(rate);
        }
        return rates.Count == 0 ? null : rates.GroupBy(r => r).OrderByDescending(g => g.Count()).First().Key;
    }
}
