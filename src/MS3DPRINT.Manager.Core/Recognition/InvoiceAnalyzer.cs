using System.Text.RegularExpressions;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>Understands the text of any invoice, receipt or order page: total, date, number, other party, direction. Works on text from a PDF or from OCR alike.</summary>
public static partial class InvoiceAnalyzer
{
    [GeneratedRegex(@"\bdevis\b|\bquotation\b|\bquote\b|proposition\s*commerciale", RegexOptions.CultureInvariant)]
    private static partial Regex QuoteWord();
    [GeneratedRegex(@"\bfacture\b|\binvoice\b", RegexOptions.CultureInvariant)]
    private static partial Regex InvoiceWord();
    [GeneratedRegex(@"\bavoir\b|credit\s*note|note\s*de\s*credit", RegexOptions.CultureInvariant)]
    private static partial Regex CreditWord();
    [GeneratedRegex(@"ticket|\brecu\b|caisse|merci\s*de\s*votre\s*visite|a\s*bientot|cb\s*(?:sans\s*contact|contactless)|terminal|\btpe\b|vendeur|hotesse|numero\s*de\s*carte|\bcarte\s*\*+", RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptWord();
    [GeneratedRegex(@"confirmation\s*de\s*commande|order\s*confirmation|bon\s*de\s*commande|accuse\s*de\s*reception\s*de\s*commande|merci\s*pour\s*votre\s*commande|recapitulatif\s*de\s*(?:votre\s*)?commande", RegexOptions.CultureInvariant)]
    private static partial Regex OrderWord();
    [GeneratedRegex(@"bon\s*de\s*livraison|delivery\s*note|packing\s*slip|bordereau", RegexOptions.CultureInvariant)]
    private static partial Regex DeliveryWord();

    [GeneratedRegex(@"(?:paye|paiement|reglement|regle|payment|mode\s*de\s*paiement|paid)\s*(?:par|via|:|by|en)?[^\n]{0,30}?(carte\s*bancaire|carte|cb|visa|mastercard|american\s*express|apple\s*pay|google\s*pay|especes|espece|cash|virement|paypal|cheque)|(?:^|\s)(cb\s*(?:sans\s*contact|contactless)?|carte\s*bancaire|visa|mastercard)\s+\d+[.,]\d{2}|(?:^|\s)(especes|espece)\s+\d+[.,]\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex PaymentHint();

    [GeneratedRegex(@"vente\s*de\s*(?:bien|marchandise)|marchandises?|produits?\s*vendus", RegexOptions.CultureInvariant)]
    private static partial Regex GoodsWord();
    [GeneratedRegex(@"prestations?\s*de\s*service|impression\s*3d|fabrication\s*additive|conception|modelisation|main\s*d'oeuvre|service", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceWord();

    [GeneratedRegex(@"^(?<desc>[\p{L}][\p{L}\d .,'/&()+\-]{3,60}?)\s{2,}(?:\d+\s*(?:x\s*)?)?(?:\d+[.,]\d{2}\s+)?(?<amount>\d{1,5}[.,]\d{2})\s*(?:€|eur)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ItemLine();

    public static DocumentReading Analyze(string text, AnalysisContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        context ??= new AnalysisContext();
        var lines = TextTools.ZipColumns(TextTools.Lines(text));
        var folded = TextTools.Fold(string.Join('\n', lines));
        if (lines.Count == 0) return new DocumentReading { Text = text };

        var kind = Kind(folded, lines);
        var direction = PartyReader.Direction(lines, folded, context, kind);
        var amounts = AmountReader.Read(lines, folded);
        var date = DateReader.Read(lines, context.Today);
        var number = NumberReader.Read(lines, kind);
        var party = PartyReader.Name(lines, folded, context, direction.Direction);
        var siret = PartyReader.Siren(lines, context);
        var payment = Payment(folded);
        var nature = Nature(folded);
        var warnings = new List<string>(amounts.Warnings);

        if (kind == DocumentKind.Quote) warnings.Add("Ce document ressemble à un devis, pas à une facture : rien n’a été payé tant qu’il n’est pas accepté et facturé.");
        if (kind == DocumentKind.OrderConfirmation) warnings.Add("Confirmation de commande : ce n’est pas une facture. Attendez la facture ou le ticket pour noter l’achat.");
        if (kind == DocumentKind.DeliveryNote) warnings.Add("Bon de livraison : il ne comporte pas toujours les prix.");
        if (kind == DocumentKind.CreditNote) warnings.Add("Avoir : ce document diminue une dépense ou une recette. Corrigez la ligne d’origine plutôt que d’en ajouter une.");
        if (date is { } d && d.Value > context.Today.AddDays(1)) warnings.Add("La date lue est dans le futur : vérifiez-la.");
        if (date is { } old && old.Value.Year < context.Today.Year - 1) warnings.Add($"La date lue ({old.Value:dd/MM/yyyy}) n’est pas dans l’année en cours : vérifiez-la.");
        var dollars = Regex.IsMatch(folded, @"\$|usd|dollars?");
        var pounds = folded.Contains('£') || Regex.IsMatch(folded, @"gbp");
        var euros = folded.Contains('€') || Regex.IsMatch(folded, @"eur|euros?");
        if (dollars && !euros) warnings.Add("Le montant semble être en dollars : convertissez-le en euros avant de l’enregistrer (la valeur proposée est celle du document).");
        else if (pounds && !euros) warnings.Add("Le montant semble être en livres sterling : convertissez-le en euros avant de l’enregistrer.");
        if (amounts.TotalTtc is { IsReliable: false }) warnings.Add("Le montant n’a pas pu être identifié avec certitude : vérifiez-le.");

        var label = Label(lines, kind, number, party, direction.Direction);
        var reading = new DocumentReading
        {
            Kind = kind, Direction = direction.Direction, DirectionReason = direction.Reason,
            TotalTtc = amounts.TotalTtc, TotalHt = amounts.TotalHt, Vat = amounts.Vat,
            Date = date, Number = number, Party = party, PartySiret = siret, Payment = payment, Label = label, Nature = nature,
            Warnings = warnings, Text = text, Amounts = amounts.Candidates
        };
        return reading with { Quality = Quality(reading) };
    }

    private static double Quality(DocumentReading r)
    {
        var quality = (r.TotalTtc?.Confidence ?? 0) * 0.45 + (r.Date?.Confidence ?? 0) * 0.25 + (r.Party?.Confidence ?? 0) * 0.15 + (r.Number?.Confidence ?? 0) * 0.15;
        if (r.Kind is DocumentKind.Quote or DocumentKind.OrderConfirmation or DocumentKind.DeliveryNote) quality *= 0.7;
        return Math.Round(quality - 0.03 * r.Warnings.Count, 3);
    }

    private static DocumentKind Kind(string folded, IReadOnlyList<string> lines)
    {
        var head = TextTools.Fold(string.Join('\n', lines.Take(12)));
        if (CreditWord().IsMatch(head)) return DocumentKind.CreditNote;
        if (DeliveryWord().IsMatch(head) && !InvoiceWord().IsMatch(head)) return DocumentKind.DeliveryNote;
        if (QuoteWord().IsMatch(head) && !InvoiceWord().IsMatch(head)) return DocumentKind.Quote;
        if (OrderWord().IsMatch(head) && !InvoiceWord().IsMatch(head)) return DocumentKind.OrderConfirmation;
        var invoice = InvoiceWord().Matches(folded).Count;
        var receipt = ReceiptWord().Matches(folded).Count;
        if (invoice > 0 && invoice >= receipt) return DocumentKind.Invoice;
        if (receipt > 0) return DocumentKind.Receipt;
        if (QuoteWord().IsMatch(folded)) return DocumentKind.Quote;
        return DocumentKind.Unknown;
    }

    private static ReadField<string>? Payment(string folded)
    {
        foreach (Match m in PaymentHint().Matches(folded))
        {
            var word = m.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value;
            if (word is null) continue;
            var name = word switch
            {
                var w when w.StartsWith("carte") || w.StartsWith("cb") || w is "visa" or "mastercard" or "american express" or "apple pay" or "google pay" => "Carte bancaire",
                "especes" or "espece" or "cash" => "Espèces",
                "virement" => "Virement",
                "paypal" => "PayPal",
                "cheque" => "Chèque",
                _ => null
            };
            if (name is not null) return new(name, 0.8, TextTools.Collapse(m.Value));
        }
        return null;
    }

    private static RevenueNature? Nature(string folded)
    {
        var goods = GoodsWord().IsMatch(folded);
        var services = ServiceWord().IsMatch(folded);
        return goods && services ? RevenueNature.Mixed : goods ? RevenueNature.Goods : services ? RevenueNature.Services : null;
    }

    /// <summary>A short label for the entry: the biggest items of the document, or the kind of document with the other party.</summary>
    private static ReadField<string>? Label(IReadOnlyList<string> lines, DocumentKind kind, ReadField<string>? number, ReadField<string>? party, DocumentDirection direction)
    {
        var items = new List<(string Text, decimal Amount)>();
        foreach (var line in lines)
        {
            var match = ItemLine().Match(line);
            if (!match.Success) continue;
            var desc = TextTools.Collapse(match.Groups["desc"].Value).Trim(' ', '.', ',', '-');
            var folded = TextTools.Fold(desc);
            if (desc.Length < 4 || Regex.IsMatch(folded, @"total|tva|\bht\b|ttc|sous|remise|port|livraison|frais|acompte|solde|rendu|monnaie|especes|carte|\bcb\b|paiement|reglement|montant|payer|arrondi|date|siret|iban|capital")) continue;
            if (!TextTools.TryParseMoney(match.Groups["amount"].Value, out var amount) || amount <= 0) continue;
            items.Add((desc, amount));
        }
        if (items.Count > 0)
        {
            var top = items.OrderByDescending(i => i.Amount).Take(2).Select(i => Capitalize(i.Text)).ToList();
            var text = string.Join(", ", top);
            return new(text.Length > 70 ? text[..70].TrimEnd() + "…" : text, 0.6, "lignes de la facture");
        }
        var who = party?.Value;
        var prefix = kind switch { DocumentKind.Receipt => "Ticket", DocumentKind.Quote => "Devis", DocumentKind.CreditNote => "Avoir", _ => "Facture" };
        if (direction == DocumentDirection.Revenue && number is not null) return new($"Facture {number.Value}", 0.7, "numéro de facture");
        if (who is not null) return new($"{prefix} {who}", 0.5, "document et tiers");
        return number is not null ? new($"{prefix} {number.Value}", 0.45, "numéro") : null;
    }

    private static string Capitalize(string text)
    {
        var lower = text.Any(char.IsLower) ? text : text.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }
}
