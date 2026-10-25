using System.Globalization;
using System.Text.Json;
using MS3DPRINT.Manager.Core.Purchases;
using MS3DPRINT.Manager.Core.Revenues;
using MS3DPRINT.Manager.Core.Workspace;

namespace MS3DPRINT.Manager.Core.Mobile;

/// <summary>
/// A purchase noted on the phone. The phone never edits the books: it drops this small file (and the photo of the receipt) in the exchange folder,
/// and the computer imports it after the user's confirmation.
/// </summary>
public sealed record MobilePurchase(Guid Id, DateOnly PurchasedOn, string Supplier, string Label, decimal Amount, string PaymentMethod,
    string? Reference, string? ProjectRef, string? PhotoFileName, DateTimeOffset CapturedAt, int Version = MobileExchange.CurrentVersion);

public static class MobileExchange
{
    public const int CurrentVersion = 1;
    public const string FolderName = "Echanges mobile";
    public const string ProcessedFolderName = "Traites";
    public const string PurchasePrefix = "achat-";
    private const int MaxJsonBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Stable names so that the photo and the data travel together and never collide between devices.</summary>
    public static string BaseName(MobilePurchase purchase) =>
        PurchasePrefix + purchase.PurchasedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-" + purchase.Id.ToString("N")[..8];

    public static byte[] Serialize(MobilePurchase purchase)
    {
        Validate(purchase);
        return JsonSerializer.SerializeToUtf8Bytes(purchase, Options);
    }

    public static MobilePurchase Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaxJsonBytes) throw new JsonException("Fichier d’échange vide ou trop volumineux.");
        var purchase = JsonSerializer.Deserialize<MobilePurchase>(json) ?? throw new JsonException("Fichier d’échange vide.");
        if (purchase.Version > CurrentVersion) throw new JsonException("Ce fichier vient d’une version plus récente de l’application.");
        Validate(purchase);
        return purchase;
    }

    public static void Validate(MobilePurchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        if (purchase.Amount <= 0) throw new JsonException("Le montant doit être positif.");
        if (string.IsNullOrWhiteSpace(purchase.Supplier)) throw new JsonException("Le fournisseur est obligatoire.");
        if (string.IsNullOrWhiteSpace(purchase.Label)) throw new JsonException("Le libellé est obligatoire.");
        if (purchase.PhotoFileName is { } photo && (photo != Path.GetFileName(photo) || photo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new JsonException("Le nom de la photo est invalide.");
    }

    /// <summary>The line to record on the computer. The id is kept so that importing the same file twice is detected.</summary>
    public static PurchaseEntry ToEntry(MobilePurchase purchase, string? documentPath)
    {
        Validate(purchase);
        var reference = string.IsNullOrWhiteSpace(purchase.Reference) ? "Ticket du " + purchase.PurchasedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : purchase.Reference.Trim();
        return new PurchaseEntry(purchase.Id, purchase.PurchasedOn, purchase.Supplier.Trim(), purchase.Label.Trim(), Math.Round(purchase.Amount, 2, MidpointRounding.AwayFromZero),
            string.IsNullOrWhiteSpace(purchase.PaymentMethod) ? "Autre" : purchase.PaymentMethod, reference, documentPath, string.IsNullOrWhiteSpace(purchase.ProjectRef) ? null : purchase.ProjectRef.Trim(), DateTimeOffset.UtcNow);
    }
}

public sealed record MobileInboxItem(string DataPath, MobilePurchase Purchase, string? PhotoPath);

public sealed record MobileInboxScan(IReadOnlyList<MobileInboxItem> Items, IReadOnlyList<string> Errors);

/// <summary>The exchange folder as the computer sees it: files dropped by the phone, waiting for confirmation.</summary>
public static class MobileInbox
{
    public static string FolderFor(string workspaceRoot) => Path.Combine(Path.GetFullPath(workspaceRoot), MobileExchange.FolderName);

    /// <summary>Reads the waiting purchases. Files still being synchronised or malformed are reported, never imported.</summary>
    public static MobileInboxScan Scan(string workspaceRoot)
    {
        var folder = FolderFor(workspaceRoot);
        WorkspacePathSafety.EnsureNoLinks(folder);
        if (!Directory.Exists(folder)) return new([], []);
        var items = new List<MobileInboxItem>(); var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, MobileExchange.PurchasePrefix + "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var purchase = MobileExchange.Parse(File.ReadAllBytes(path));
                string? photo = null;
                if (purchase.PhotoFileName is { } name)
                {
                    var candidate = Path.Combine(folder, name);
                    if (!File.Exists(candidate)) { errors.Add($"{Path.GetFileName(path)} : la photo « {name} » n’est pas encore arrivée (synchronisation en cours ?)."); continue; }
                    photo = candidate;
                }
                items.Add(new(path, purchase, photo));
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            { errors.Add($"{Path.GetFileName(path)} : {exception.Message}"); }
        }
        return new(items, errors);
    }

    /// <summary>Moves the data file and its photo to the processed folder; a name already taken there gets a numeric suffix.</summary>
    public static void MarkProcessed(string workspaceRoot, MobileInboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var folder = FolderFor(workspaceRoot);
        var processed = Path.Combine(folder, MobileExchange.ProcessedFolderName);
        WorkspacePathSafety.EnsureNoLinks(processed);
        Directory.CreateDirectory(processed);
        foreach (var path in new[] { item.DataPath, item.PhotoPath }.Where(p => p is not null).Cast<string>())
        {
            if (!File.Exists(path)) continue;
            var target = Path.Combine(processed, Path.GetFileName(path));
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(processed, Path.GetFileNameWithoutExtension(path) + $" ({n})" + Path.GetExtension(path));
            File.Move(path, target);
        }
    }
}
