using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Recognition;

/// <summary>One reading of the text of an image; engines return several when they try different preparations (enlarged, inverted…).</summary>
public sealed record OcrText(string Text, string Variant);

/// <summary>Text recognition supplied by the platform (Windows OCR on the PC, ML Kit on Android). Everything runs on the device.</summary>
public interface IOcrEngine
{
    /// <summary>Reads the text of a picture (PNG, JPEG, BMP…). Returns an empty list when nothing readable is found.</summary>
    Task<IReadOnlyList<OcrText>> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default);
}

/// <summary>Turns the pages of a PDF into pictures, for scanned documents that hold no text.</summary>
public interface IPdfRasterizer
{
    Task<IReadOnlyList<byte[]>> RenderPagesAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken = default);
}

/// <summary>Reads an invoice or a receipt from a PDF, a photo or a screenshot. Nothing leaves the device and nothing is recorded: the result is a proposal.</summary>
public static class DocumentReader
{
    private const int MaxPages = 3;
    private const long MaxBytes = 30 * 1024 * 1024;
    /// <summary>Set once at start-up by the platform; without it only PDFs that hold text can be read.</summary>
    public static IOcrEngine? Ocr { get; set; }
    public static IPdfRasterizer? Rasterizer { get; set; }

    public static bool CanReadImages => Ocr is not null;

    public static async Task<DocumentReading> ReadAsync(string path, AnalysisContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) return Failed("Fichier introuvable.");
        if (info.Length is 0 or > MaxBytes) return Failed("Fichier vide ou trop volumineux (30 Mo au plus).");
        return await ReadAsync(await File.ReadAllBytesAsync(path, cancellationToken), Path.GetFileName(path), context, cancellationToken);
    }

    public static async Task<DocumentReading> ReadAsync(byte[] data, string fileName, AnalysisContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        context ??= new AnalysisContext();
        try
        {
            return IsPdf(data) ? await ReadPdfAsync(data, context, cancellationToken) : await ReadImageAsync(data, context, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return Failed("Ce fichier n’a pas pu être lu (" + exception.GetType().Name + ").");
        }
    }

    public static bool IsPdf(byte[] data) => data.Length > 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';

    private static async Task<DocumentReading> ReadPdfAsync(byte[] pdf, AnalysisContext context, CancellationToken cancellationToken)
    {
        var (indy, text) = IndyInvoiceReader.ReadWithText(pdf);
        DocumentReading? fromText = null;
        if (text.Count(char.IsLetterOrDigit) >= 40) fromText = WithIndy(InvoiceAnalyzer.Analyze(text, context) with { Source = "PDF" }, indy, context);
        if (fromText is { Quality: >= 0.75 }) return fromText;

        // A scanned PDF holds a picture of the page, or a text layer too poor to use: read the pages like photos.
        if (Rasterizer is not null && Ocr is not null)
        {
            var pages = await Rasterizer.RenderPagesAsync(pdf, MaxPages, cancellationToken);
            var scanned = new List<string>();
            foreach (var page in pages)
            {
                var variants = await Ocr.RecognizeAsync(page, cancellationToken);
                if (variants.Count > 0) scanned.Add(variants.OrderByDescending(v => v.Text.Count(char.IsLetterOrDigit)).First().Text);
            }
            if (scanned.Count > 0)
            {
                var combined = string.Join('\n', scanned);
                var fromScan = InvoiceAnalyzer.Analyze(combined, context) with { Source = "PDF numérisé (OCR)" };
                if (fromText is null || fromScan.Quality > fromText.Quality) return WithIndy(fromScan, indy, context);
            }
        }
        if (fromText is not null) return fromText;
        return Failed(Rasterizer is null || Ocr is null ? "Ce PDF ne contient pas de texte (document numérisé) et la lecture d’image n’est pas disponible ici." : "Aucun texte lisible dans ce PDF.");
    }

    private static async Task<DocumentReading> ReadImageAsync(byte[] image, AnalysisContext context, CancellationToken cancellationToken)
    {
        if (Ocr is null) return Failed("La lecture d’image n’est pas disponible sur cet appareil.");
        var variants = await Ocr.RecognizeAsync(image, cancellationToken);
        if (variants.Count == 0 || variants.All(v => v.Text.Count(char.IsLetterOrDigit) < 8)) return Failed("Aucun texte n’a pu être lu sur l’image. Cadrez la facture bien à plat, avec de la lumière, ou utilisez le PDF.");
        DocumentReading? best = null;
        foreach (var variant in variants)
        {
            var reading = InvoiceAnalyzer.Analyze(variant.Text, context) with { Source = "OCR" + (string.IsNullOrEmpty(variant.Variant) ? "" : " (" + variant.Variant + ")") };
            if (best is null || reading.Quality > best.Quality) best = reading;
        }
        return best!;
    }

    /// <summary>Indy invoices (the user's own) follow fixed layouts: their number, date, client and nature are read by dedicated rules and win over the general reading.</summary>
    internal static DocumentReading WithIndy(DocumentReading reading, IndyInvoiceData? indy, AnalysisContext context)
    {
        if (indy is null) return reading;
        var warnings = reading.Warnings.ToList();
        var result = reading;
        if (indy.InvoiceNumber is { } number) result = result with { Number = new(number, 0.96, "modèle de facture Indy") };
        if (indy.IssuedOn is { } issued) result = result with { Date = new(issued, 0.96, "modèle de facture Indy") };
        if (indy.Nature is { } nature) result = result with { Nature = nature };
        if (indy.ClientName is { } client && reading.Direction != DocumentDirection.Purchase) result = result with { Party = new(client, 0.92, "destinataire de la facture") };
        if (indy.TotalTtc is { } total)
        {
            if (reading.Amounts.Contains(total) || reading.TotalTtc is null)
            {
                if (reading.TotalTtc is { } chosen && chosen.Value != total) warnings.Add($"La facture indique aussi {chosen.Value:0.00} € (net à payer, acompte déduit) ; le total TTC {total:0.00} € est proposé.");
                result = result with { TotalTtc = new(total, 0.97, "Total TTC de la facture Indy") };
            }
        }
        if (reading.Direction == DocumentDirection.Unknown && indy.InvoiceNumber is not null)
            result = result with { Direction = DocumentDirection.Revenue, DirectionReason = "Numérotation des factures émises avec Indy." };
        result = result with { Warnings = warnings.Where(w => !w.StartsWith("Le montant n’a pas pu", StringComparison.Ordinal) || result.TotalTtc is not { IsReliable: true }).ToList() };
        return result with { Quality = Math.Round(Math.Max(result.Quality, (result.TotalTtc?.Confidence ?? 0) * 0.45 + 0.96 * 0.4 + (result.Party?.Confidence ?? 0) * 0.15), 3) };
    }

    private static DocumentReading Failed(string reason) => new() { Warnings = [reason], Source = "" };
}
