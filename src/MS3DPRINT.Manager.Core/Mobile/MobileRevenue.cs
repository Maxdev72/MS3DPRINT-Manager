using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Mobile;

/// <summary>A collection noted on the phone. Like a purchase, it only travels as a file: the computer records it after confirmation.</summary>
public sealed record MobileRevenue(Guid Id, DateOnly ReceivedOn, string Label, decimal Amount, RevenueNature Nature, decimal GoodsAmount, string PaymentMethod,
    string? ClientName, string? InvoiceNumber, string? ProjectRef, string? PhotoFileName, DateTimeOffset CapturedAt, int Version = MobileExchange.CurrentVersion);

public static class MobileRevenueExchange
{
    public const string Prefix = "recette-";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string BaseName(MobileRevenue revenue) =>
        Prefix + revenue.ReceivedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-" + revenue.Id.ToString("N")[..8];

    public static byte[] Serialize(MobileRevenue revenue)
    {
        Validate(revenue);
        return JsonSerializer.SerializeToUtf8Bytes(revenue, Options);
    }

    public static MobileRevenue Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > 64 * 1024) throw new JsonException("Fichier d’échange vide ou trop volumineux.");
        var revenue = JsonSerializer.Deserialize<MobileRevenue>(json, Options) ?? throw new JsonException("Fichier d’échange vide.");
        if (revenue.Version > MobileExchange.CurrentVersion) throw new JsonException("Ce fichier vient d’une version plus récente de l’application.");
        Validate(revenue);
        return revenue;
    }

    public static void Validate(MobileRevenue revenue)
    {
        ArgumentNullException.ThrowIfNull(revenue);
        if (revenue.Amount <= 0) throw new JsonException("Le montant doit être positif.");
        if (string.IsNullOrWhiteSpace(revenue.Label)) throw new JsonException("Le libellé est obligatoire.");
        if (!Enum.IsDefined(revenue.Nature)) throw new JsonException("La nature de la recette est inconnue.");
        if (revenue.Nature == RevenueNature.Mixed && (revenue.GoodsAmount <= 0 || revenue.GoodsAmount >= revenue.Amount))
            throw new JsonException("La part marchandises d’une recette mixte doit être positive et inférieure au montant.");
        if (revenue.PhotoFileName is { } photo && (photo != Path.GetFileName(photo) || photo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new JsonException("Le nom de la photo est invalide.");
    }

    /// <summary>The line to record on the computer; the id is kept so that importing the same file twice is detected.</summary>
    public static RevenueEntry ToEntry(MobileRevenue revenue, string? documentPath)
    {
        Validate(revenue);
        var amount = Math.Round(revenue.Amount, 2, MidpointRounding.AwayFromZero);
        var goods = revenue.Nature switch { RevenueNature.Goods => amount, RevenueNature.Mixed => Math.Round(revenue.GoodsAmount, 2, MidpointRounding.AwayFromZero), _ => 0m };
        var invoice = string.IsNullOrWhiteSpace(revenue.InvoiceNumber) ? "Sans pièce du " + revenue.ReceivedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : revenue.InvoiceNumber.Trim();
        return new RevenueEntry(revenue.Id, revenue.ReceivedOn, revenue.Label.Trim(), amount, revenue.Nature, goods, amount - goods,
            string.IsNullOrWhiteSpace(revenue.PaymentMethod) ? "Autre" : revenue.PaymentMethod, null, string.IsNullOrWhiteSpace(revenue.ClientName) ? null : revenue.ClientName.Trim(),
            string.IsNullOrWhiteSpace(revenue.ProjectRef) ? null : revenue.ProjectRef.Trim(), invoice, documentPath, DateTimeOffset.UtcNow);
    }
}
