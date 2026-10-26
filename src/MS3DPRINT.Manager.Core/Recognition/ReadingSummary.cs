namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>The sentences shown to the user after a document was read: where it was read from, what to check first, and what the document seems to be.</summary>
public static class ReadingSummary
{
    public static string Describe(DocumentReading reading, DocumentDirection expected)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.IsEmpty)
            return (reading.Warnings.Count > 0 ? reading.Warnings[0] : "Ce document n’a pas pu être lu automatiquement.") + " Saisissez les informations à la main ; le document reste joint.";
        var lines = new List<string> { $"Document lu ({reading.Source}). Vérifiez chaque champ avant d’enregistrer." };
        var check = new List<string>();
        if (reading.TotalTtc is { IsReliable: false }) check.Add("le montant");
        if (reading.Date is { IsReliable: false }) check.Add("la date");
        if (reading.Party is { IsReliable: false }) check.Add(expected == DocumentDirection.Revenue ? "le client" : "le fournisseur");
        if (reading.Number is { IsReliable: false }) check.Add("le numéro");
        if (reading.TotalTtc is null) check.Add("le montant (non trouvé)");
        if (check.Count > 0) lines.Add("À vérifier en priorité : " + string.Join(", ", check) + ".");
        if (reading.Direction != DocumentDirection.Unknown && reading.Direction != expected)
            lines.Add(expected == DocumentDirection.Revenue
                ? "Ce document ressemble à un achat (" + reading.DirectionReason.TrimEnd('.') + ") : il va plutôt dans les achats."
                : "Ce document ressemble à une recette émise par vous (" + reading.DirectionReason.TrimEnd('.') + ") : il va plutôt dans les recettes.");
        lines.AddRange(reading.Warnings.Where(w => !w.StartsWith("Le montant n’a pas pu", StringComparison.Ordinal)));
        return string.Join('\n', lines);
    }
}
