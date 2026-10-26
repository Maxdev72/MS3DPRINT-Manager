using System.Text.RegularExpressions;

namespace MS3DPRINT.Manager.Core.Recognition;

internal sealed record DirectionResult(DocumentDirection Direction, string Reason, double Confidence);

/// <summary>Who issued the document and who it is addressed to: the supplier of a purchase, the client of a revenue, and which of the two the user is.</summary>
internal static partial class PartyReader
{
    private static readonly string[] Merchants =
    [
        "Amazon", "Leroy Merlin", "Castorama", "Bricoman", "Brico Depot", "Mr Bricolage", "Bambu Lab", "Prusa", "Polymaker", "Sunlu", "eSUN", "Creality", "Elegoo", "Anycubic", "AliExpress", "Temu",
        "LDLC", "Boulanger", "Fnac", "Darty", "Cdiscount", "Action", "Gifi", "Lidl", "Carrefour", "Leclerc", "Auchan", "Intermarche", "Super U", "Casino", "Monoprix", "Picard", "Total Energies", "Esso", "Shell",
        "La Poste", "Colissimo", "Mondial Relay", "Chronopost", "UPS", "DHL", "FedEx", "Vistaprint", "Canva", "OVH", "Adobe", "Google", "Microsoft", "Apple", "Orange", "SFR", "Bouygues", "Free Mobile", "Indy",
        "Qonto", "Shine", "Stripe", "PayPal", "Decathlon", "Ikea", "Truffaut", "Point P", "Wurth", "Rexel", "Conrad", "Distrelec", "RS Components", "Farnell", "Mouser", "Digikey", "Gotronic", "Kubii",
        "Botland", "Makerworld", "Printables", "Etsy", "eBay", "Rakuten", "Back Market", "Vinted", "Staples", "Bureau Vallee", "Office Depot", "Cultura", "Maisons du Monde", "Zara", "Kiabi", "Norauto", "Midas",
        "Feu Vert", "Speedy", "Sncf", "Ouigo", "Uber", "Bolt", "Autoroutes", "Vinci", "Sanef", "Ryanair", "Air France", "Booking", "Airbnb", "Ibis", "Mercure", "Sumup", "Zettle", "Lydia", "Alibaba", "Shein", "Wish"
    ];

    [GeneratedRegex(@"destinataire|client\b|factur[ée]e?\s*[àa]\b|adress[ée]e?\s*[àa]|bill\s*to|invoice\s*to|sold\s*to|livr[ée]e?\s*[àa]|ship\s*to|\bpour\s*:|ach[ée]teur|customer", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RecipientMarker();

    [GeneratedRegex(@"(?:siret|n°\s*siret|siret\s*n°)\s*[:.]?\s*((?:\d[\s.]?){14})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Siret();

    [GeneratedRegex(@"\b(SAS|SASU|SARL|EURL|SA|SNC|SCI|SELARL|GmbH|Ltd|LLC|Inc|B\.?V\.?|S\.?R\.?L\.?|S\.?p\.?A\.?|SCOP)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LegalForm();

    [GeneratedRegex(@"\b(rue|avenue|av\.|bd|boulevard|chemin|all[ée]e|impasse|place|route|zone|zi|za|cedex|bp|b\.p\.|lieu-dit|quai|cours|r[ée]sidence|b[âa]timent)\b|\b\d{5}\b|@|www\.|https?:|\.(?:com|fr|net|org|eu)\b|\btel\b|t[ée]l[ée]phone|\bfax\b|siret|siren|\btva\b|\biban\b|\bbic\b|\brcs\b|capital|\bape\b|\bnaf\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NotAName();

    [GeneratedRegex(@"facture|invoice|ticket|re[cç]u|devis|avoir|commande|date|page|merci|bienvenue|caisse|duplicata|original|n°|\bno\b|total|montant|designation|quantit|d[ée]signation|description|prix|article|client|destinataire|paiement|r[èe]glement", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NotNameWord();

    private static readonly string[] Processors = ["PayPal", "Stripe", "Sumup", "Zettle", "Lydia"];

    [GeneratedRegex(@"(?:paiement|payment|virement|transfert|achat|transaction)\s[^
]{0,60}?\s(?:à|a|to|chez|at)\s+(?<n>[\p{Lu}\d][\p{L}\d &.'\-]{2,40}?)(?=\s*[-–,.]\s|\s+(?:le|on|du|the)\s|\s*$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Payee();

    [GeneratedRegex(@"(?:www\.|@|https?://)([a-z0-9\-]+)\.(?:com|fr|net|org|eu|io|co\.uk|de|shop)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Domain();

    public static ReadField<string>? Siren(IReadOnlyList<string> lines, AnalysisContext context)
    {
        var own = context.OwnSiret is null ? "" : TextTools.DigitsOnly(context.OwnSiret);
        foreach (var line in lines)
            foreach (Match m in Siret().Matches(line))
            {
                var digits = TextTools.DigitsOnly(m.Groups[1].Value);
                if (digits.Length == 14 && digits != own && !(own.Length >= 9 && digits.StartsWith(own[..9], StringComparison.Ordinal))) return new(digits, 0.8, TextTools.Collapse(line));
            }
        return null;
    }

    /// <summary>Purchase or revenue: from the user's own name or SIRET, and where it sits (issuer at the top, or recipient under a "Destinataire" marker).</summary>
    public static DirectionResult Direction(IReadOnlyList<string> lines, string folded, AnalysisContext context, DocumentKind kind)
    {
        var ownSiret = context.OwnSiret is null ? "" : TextTools.DigitsOnly(context.OwnSiret);
        var ownNames = new[] { context.OwnBusinessName, context.OwnOwnerName }.Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 3).Select(n => TextTools.Fold(n!).Trim()).Distinct().ToList();
        var foldedLines = lines.Select(TextTools.Fold).ToList();
        var markerIndex = foldedLines.FindIndex(l => RecipientMarker().IsMatch(l));

        bool NearRecipient(Func<string, bool> test)
        {
            for (var i = 0; i < foldedLines.Count; i++)
            {
                if (!RecipientMarker().IsMatch(foldedLines[i])) continue;
                for (var j = i; j < Math.Min(foldedLines.Count, i + 6); j++) if (test(foldedLines[j])) return true;
            }
            return false;
        }

        if (ownSiret.Length == 14)
        {
            var allDigits = TextTools.DigitsOnly(folded);
            if (allDigits.Contains(ownSiret, StringComparison.Ordinal))
            {
                var inRecipient = NearRecipient(l => TextTools.DigitsOnly(l).Contains(ownSiret, StringComparison.Ordinal));
                return inRecipient ? new(DocumentDirection.Purchase, "Votre SIRET figure dans le bloc destinataire.", 0.95) : new(DocumentDirection.Revenue, "Votre SIRET figure sur le document, comme émetteur.", 0.93);
            }
        }
        foreach (var name in ownNames)
        {
            if (!foldedLines.Any(l => l.Contains(name, StringComparison.Ordinal))) continue;
            if (NearRecipient(l => l.Contains(name, StringComparison.Ordinal))) return new(DocumentDirection.Purchase, $"« {name} » figure dans le bloc destinataire.", 0.9);
            var top = foldedLines.Take(8).Any(l => l.Contains(name, StringComparison.Ordinal));
            if (top) return new(DocumentDirection.Revenue, $"« {name} » figure en tête du document, comme émetteur.", 0.85);
            return new(DocumentDirection.Revenue, $"« {name} » figure sur le document.", 0.55);
        }
        if (context.KnownSuppliers.Any(s => s.Length >= 3 && TextTools.ContainsWord(folded, s))) return new(DocumentDirection.Purchase, "Fournisseur déjà connu.", 0.85);
        if (context.KnownClients.Any(s => s.Length >= 3 && TextTools.ContainsWord(folded, s))) return new(DocumentDirection.Revenue, "Client déjà connu.", 0.8);
        if (Merchants.Any(m => TextTools.ContainsWord(folded, m))) return new(DocumentDirection.Purchase, "Enseigne ou fournisseur courant reconnu.", 0.8);
        if (kind == DocumentKind.Receipt) return new(DocumentDirection.Purchase, "Ticket de caisse : un achat.", 0.75);
        if (markerIndex >= 0 && lines.Count > 0 && (ownNames.Count > 0 || ownSiret.Length > 0)) return new(DocumentDirection.Purchase, "Le document est adressé à quelqu’un d’autre que vous.", 0.5);
        return new(DocumentDirection.Unknown, "", 0);
    }

    /// <summary>The name of the other party, with the confidence of the rule that found it.</summary>
    public static ReadField<string>? Name(IReadOnlyList<string> lines, string folded, AnalysisContext context, DocumentDirection direction)
    {
        var ownNames = new[] { context.OwnBusinessName, context.OwnOwnerName }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => TextTools.Fold(n!).Trim()).ToList();
        bool IsOwn(string text) { var f = TextTools.Fold(text); return ownNames.Any(n => n.Length >= 3 && (f.Contains(n, StringComparison.Ordinal) || n.Contains(f.Trim(), StringComparison.Ordinal) && f.Trim().Length >= 5)); }

        if (direction != DocumentDirection.Purchase)
        {
            var client = ClientBlock(lines, IsOwn);
            if (client is not null) return client;
            if (direction == DocumentDirection.Revenue)
            {
                var known = context.KnownClients.Where(k => k.Length >= 3).OrderByDescending(k => k.Length).FirstOrDefault(k => TextTools.ContainsWord(folded, k));
                if (known is not null) return new(known, 0.85, "client déjà connu");
                return null;
            }
        }

        var supplier = context.KnownSuppliers.Where(k => k.Length >= 3).OrderByDescending(k => k.Length).FirstOrDefault(k => TextTools.ContainsWord(folded, k) && !IsOwn(k));
        if (supplier is not null) return new(supplier, 0.92, "fournisseur déjà connu");
        foreach (var line in lines)
            if (Payee().Match(line) is { Success: true } payee && payee.Groups["n"].Value.Trim() is { Length: >= 3 } paid && !IsOwn(paid))
            {
                var canonical = Merchants.Where(m => !Processors.Contains(m)).OrderByDescending(m => m.Length).FirstOrDefault(m => TextTools.ContainsWord(TextTools.Fold(paid), m));
                return canonical is not null ? new(canonical, 0.9, TextTools.Collapse(line)) : new(TextTools.Collapse(paid), 0.7, TextTools.Collapse(line));
            }
        foreach (var merchant in Merchants.Where(m => !Processors.Contains(m)).OrderByDescending(m => m.Length))
            if (TextTools.ContainsWord(folded, merchant) && !IsOwn(merchant)) return new(merchant, 0.85, "enseigne reconnue");

        string? bestLine = null; var bestScore = 0.0; var bestIndex = 0;
        for (var i = 0; i < Math.Min(lines.Count, 14); i++)
        {
            var line = TextTools.Collapse(lines[i]);
            if (line.Length is < 3 or > 50 || IsOwn(line) || line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 6 || line.Contains(',') && !LegalForm().IsMatch(line)) continue;
            if (NotAName().IsMatch(line) || NotNameWord().IsMatch(line)) continue;
            if (line.Count(char.IsDigit) > 3 || !line.Any(char.IsLetter)) continue;
            var score = 20.0 - 1.5 * i;
            if (LegalForm().IsMatch(line)) score += 40;
            if (line == line.ToUpperInvariant()) score += 8;
            if (i + 1 < lines.Count && NotAName().IsMatch(lines[i + 1])) score += 12;
            if (score > bestScore) { bestScore = score; bestLine = line; bestIndex = i; }
        }
        if (bestLine is not null && bestScore >= 12) return new(bestLine, Math.Min(0.85, 0.35 + bestScore / 90.0), $"ligne {bestIndex + 1} du document");
        foreach (var line in lines)
            if (Domain().Match(line) is { Success: true } d && d.Groups[1].Value.Length >= 3 && !IsOwn(d.Groups[1].Value))
                return new(char.ToUpperInvariant(d.Groups[1].Value[0]) + d.Groups[1].Value[1..], 0.5, TextTools.Collapse(line));
        return null;
    }

    private static ReadField<string>? ClientBlock(IReadOnlyList<string> lines, Func<string, bool> isOwn)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var folded = TextTools.Fold(lines[i]);
            if (!RecipientMarker().IsMatch(folded)) continue;
            var marker = RecipientMarker().Match(folded);
            var rest = lines[i][Math.Min(lines[i].Length, marker.Index + marker.Length)..].Trim(' ', ':', '-', '.');
            if (rest.Length >= 3 && rest.Any(char.IsLetter) && !NotAName().IsMatch(rest) && !isOwn(rest)) return new(TextTools.Collapse(rest), 0.8, TextTools.Collapse(lines[i]));
            for (var j = i + 1; j < Math.Min(lines.Count, i + 4); j++)
            {
                var candidate = TextTools.Collapse(lines[j]);
                if (candidate.Length < 3 || candidate.Count(char.IsDigit) > 3 || NotAName().IsMatch(candidate) || NotNameWord().IsMatch(candidate) || !candidate.Any(char.IsLetter)) continue;
                if (isOwn(candidate)) break;
                return new(candidate, 0.8, TextTools.Collapse(lines[i]) + " → " + candidate);
            }
        }
        return null;
    }
}
