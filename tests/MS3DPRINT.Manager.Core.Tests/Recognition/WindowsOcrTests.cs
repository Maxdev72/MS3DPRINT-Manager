using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Windows;

namespace MS3DPRINT.Manager.Core.Tests.Recognition;

/// <summary>Real Windows OCR on pictures drawn the way captures and photos look: light, dark theme, tiny, tilted and shadowed.</summary>
[Collection("DocumentReader statics")]
public sealed class WindowsOcrTests : IDisposable
{
    private static readonly AnalysisContext Me = new() { OwnBusinessName = "MS3DPRINT", Today = new DateOnly(2026, 10, 26) };
    private static readonly string[] Receipt =
    [
        "LEROY MERLIN LE MANS", "ZI Sud 72100 LE MANS", "Ticket n° 004512   24/10/2026 14:32", "",
        "VIS TC 4X40 X200          6,90", "COLLE CYANO 20G           4,50", "EQUERRE ACIER 2X          3,00", "",
        "TOTAL TTC                14,40", "CB SANS CONTACT          14,40", "TVA 20,00%                2,40", "Merci de votre visite"
    ];
    private static readonly string[] Invoice =
    [
        "POLYMAKER SAS", "SIRET 552 081 317 00011", "Facture n° FA-2026-004521", "Date : 17/10/2026", "Echéance : 17/11/2026", "",
        "Facturé à MS3DPRINT", "", "Panchroma PLA 1kg noir  3  19,90  59,70", "Frais de port                 6,00",
        "Total HT                     65,70", "TVA 20 %                     13,14", "Total TTC                    78,84"
    ];

    public WindowsOcrTests() { DocumentReader.Ocr = new WindowsOcrEngine(); DocumentReader.Rasterizer = new WindowsPdfRasterizer(); }
    public void Dispose() { DocumentReader.Ocr = null; DocumentReader.Rasterizer = null; }

    private static byte[] Draw(string[] lines, float size, Color back, Color fore, float tilt = 0, bool shadow = false, string font = "Consolas")
    {
        using var probe = new Bitmap(1, 1);
        using var bitmap = new Bitmap((int)(size * 38), (int)(size * 1.7f * (lines.Length + 3)));
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(back);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.HighQuality;
            if (tilt != 0) g.RotateTransform(tilt);
            using var f = new Font(font, size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(fore);
            for (var i = 0; i < lines.Length; i++) g.DrawString(lines[i], f, brush, size, size + i * size * 1.6f);
            if (shadow)
            {
                g.ResetTransform();
                using var gradient = new LinearGradientBrush(new Rectangle(0, 0, bitmap.Width, bitmap.Height), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 40, 40, 40), 20f);
                g.FillRectangle(gradient, 0, 0, bitmap.Width, bitmap.Height);
            }
        }
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void AssertReceipt(DocumentReading r)
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "ocr-debug.txt"), "=== " + r.Source + "\n" + r.Text + "\n");
        Assert.Equal(14.40m, r.TotalTtc?.Value);
        Assert.Equal(new DateOnly(2026, 10, 24), r.Date?.Value);
        Assert.Equal(DocumentDirection.Purchase, r.Direction);
        Assert.Equal("Leroy Merlin", r.Party?.Value);
    }

    [Fact]
    public async Task Clean_screenshot_of_a_receipt() => AssertReceipt(await DocumentReader.ReadAsync(Draw(Receipt, 20, Color.White, Color.Black), "a.png", Me));

    [Fact]
    public async Task Dark_theme_screenshot_is_inverted_and_read() => AssertReceipt(await DocumentReader.ReadAsync(Draw(Receipt, 20, Color.FromArgb(24, 26, 32), Color.FromArgb(225, 228, 235), font: "Segoe UI"), "dark.png", Me));

    [Fact]
    public async Task Tiny_capture_is_enlarged_before_reading() => AssertReceipt(await DocumentReader.ReadAsync(Draw(Receipt, 11, Color.White, Color.Black, font: "Arial"), "tiny.png", Me));

    [Fact]
    public async Task Tilted_shadowed_photo_still_gives_the_total()
    {
        var r = await DocumentReader.ReadAsync(Draw(Receipt, 22, Color.FromArgb(235, 232, 220), Color.FromArgb(40, 40, 40), tilt: 3f, shadow: true), "photo.png", Me);
        Assert.Equal(14.40m, r.TotalTtc?.Value);
    }

    [Fact]
    public async Task Supplier_invoice_screenshot_gives_totals_number_and_date()
    {
        var r = await DocumentReader.ReadAsync(Draw(Invoice, 20, Color.White, Color.Black, font: "Arial"), "inv.png", Me);
        Assert.Equal(78.84m, r.TotalTtc?.Value);
        Assert.Equal(new DateOnly(2026, 10, 17), r.Date?.Value);
        Assert.Equal("FA-2026-004521", r.Number?.Value);
        Assert.Equal(DocumentDirection.Purchase, r.Direction);
    }

    [Fact]
    public async Task Scanned_pdf_is_rendered_by_windows_and_read()
    {
        var png = Draw(Receipt, 20, Color.White, Color.Black);
        var pdf = ImagePdf(png);
        var r = await DocumentReader.ReadAsync(pdf, "scan.pdf", Me);
        Assert.Equal(14.40m, r.TotalTtc?.Value);
        Assert.StartsWith("PDF numérisé", r.Source);
    }

    /// <summary>A one-page PDF holding only a picture, like a scanner produces.</summary>
    private static byte[] ImagePdf(byte[] png)
    {
        using var image = Image.FromStream(new MemoryStream(png));
        using var jpegStream = new MemoryStream();
        image.Save(jpegStream, ImageFormat.Jpeg);
        var jpeg = jpegStream.ToArray();
        using var pdf = new MemoryStream();
        void Write(string text) { var b = System.Text.Encoding.ASCII.GetBytes(text); pdf.Write(b, 0, b.Length); }
        var offsets = new List<long>();
        Write("%PDF-1.4\n");
        offsets.Add(pdf.Position); Write("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
        offsets.Add(pdf.Position); Write("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
        offsets.Add(pdf.Position); Write($"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</XObject<</Im0 4 0 R>>>>/Contents 5 0 R>>endobj\n");
        offsets.Add(pdf.Position); Write($"4 0 obj<</Type/XObject/Subtype/Image/Width {image.Width}/Height {image.Height}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length {jpeg.Length}>>stream\n");
        pdf.Write(jpeg, 0, jpeg.Length); Write("\nendstream endobj\n");
        var content = $"q 595 0 0 {595.0 * image.Height / image.Width:0} 0 {842 - 595.0 * image.Height / image.Width:0} cm /Im0 Do Q";
        offsets.Add(pdf.Position); Write($"5 0 obj<</Length {content.Length}>>stream\n{content}\nendstream endobj\n");
        var xref = pdf.Position;
        Write($"xref\n0 6\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer<</Size 6/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        return pdf.ToArray();
    }
}
