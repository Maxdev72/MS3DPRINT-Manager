using System.Drawing;
using System.Drawing.Printing;

namespace MS3DPRINT.Manager.Windows;

public sealed record PdfPrintRequest(string PrinterName, int Copies, IReadOnlyList<int> Pages);

/// <summary>Prints PDF pages rendered by <see cref="PdfPageRenderer"/> to an installed Windows printer.</summary>
public static class PdfPrinter
{
    private const int PrintDpi = 200;

    public static IReadOnlyList<string> InstalledPrinters() => PrinterSettings.InstalledPrinters.Cast<string>().ToArray();

    public static string? DefaultPrinter()
    {
        var settings = new PrinterSettings();
        return settings.IsValid ? settings.PrinterName : null;
    }

    /// <summary>Blocks until every page has been spooled; call it from a background thread.</summary>
    public static void Print(PdfPageRenderer pdf, string documentName, PdfPrintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pages.Count == 0) throw new ArgumentException("Aucune page à imprimer.", nameof(request));
        if (request.Copies is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(request), "Le nombre d’exemplaires doit être compris entre 1 et 99.");

        using var document = new PrintDocument { DocumentName = documentName };
        document.PrinterSettings.PrinterName = request.PrinterName;
        if (!document.PrinterSettings.IsValid) throw new InvalidOperationException($"L’imprimante « {request.PrinterName} » est introuvable.");
        document.PrinterSettings.Copies = (short)request.Copies;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        var next = 0;
        document.QueryPageSettings += (_, e) =>
        {
            var size = pdf.GetPageSize(request.Pages[next]);
            e.PageSettings.Landscape = size.Width > size.Height;
        };
        document.PrintPage += (_, e) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageBounds = e.PageBounds; // hundredths of an inch
            var pixelWidth = Math.Clamp(pageBounds.Width * PrintDpi / 100, 200, 4000);
            using var stream = new MemoryStream(pdf.RenderPageAsync(request.Pages[next], pixelWidth).GetAwaiter().GetResult(), writable: false);
            using var image = Image.FromStream(stream);
            var scale = Math.Min((double)pageBounds.Width / image.Width, (double)pageBounds.Height / image.Height);
            var width = (int)(image.Width * scale);
            var height = (int)(image.Height * scale);
            e.Graphics!.DrawImage(image, pageBounds.X + (pageBounds.Width - width) / 2, pageBounds.Y + (pageBounds.Height - height) / 2, width, height);
            next++;
            e.HasMorePages = next < request.Pages.Count;
        };
        document.Print();
    }
}
