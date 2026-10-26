using System.Runtime.InteropServices.WindowsRuntime;
using MS3DPRINT.Manager.Core.Recognition;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MS3DPRINT.Manager.Windows;

/// <summary>Text recognition built into Windows (no network, no account). Several preparations of the same picture are read so that dark screenshots, small captures and shadowed photos all give a usable text.</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private const int EnoughCharacters = 60;
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromLanguage(new Language("fr-FR")) ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));

    public bool IsAvailable => _engine is not null;

    /// <summary>"fr-FR" when French recognition is installed; a hint for the user when it is not.</summary>
    public string? LanguageTag => _engine?.RecognizerLanguage.LanguageTag;

    public async Task<IReadOnlyList<OcrText>> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        if (_engine is null || image.Length == 0) return [];
        var results = new List<OcrText>();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(image.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var longSide = Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        var maxSide = (uint)Math.Min(OcrEngine.MaxImageDimension, 4200);

        // A: the picture at a comfortable size (small captures are enlarged: Windows OCR reads text better when letters are about 25 px tall).
        var target = longSide < 1800 ? Math.Min(longSide * 3, 2800u) : Math.Min(longSide, maxSide);
        using var normal = await DecodeAsync(decoder, target, longSide);
        cancellationToken.ThrowIfCancellationRequested();
        await Add(results, normal, "");

        var (pixels, width, height) = Read(normal);
        var luminance = Luminance(pixels);
        if (luminance.Mean < 110)
        {
            // Dark theme screenshot: light text on a dark background is read far better once inverted.
            using var inverted = Create(Invert(pixels), width, height);
            await Add(results, inverted, "inversé");
            // Grey levels only: the coloured fringes of sub-pixel text rendering disappear.
            using var greyInverted = Create(Invert(Stretch(pixels, luminance.Low, luminance.High)), width, height);
            await Add(results, greyInverted, "inversé gris");
        }
        if (Total(results) < EnoughCharacters || luminance.Spread < 150 || luminance.Mean > 110)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var contrasted = Create(Stretch(pixels, luminance.Low, luminance.High), width, height);
            await Add(results, contrasted, "contraste");
        }
        if (Total(results) < EnoughCharacters)
        {
            // Phone photos taken sideways or upside down: try the other orientations.
            foreach (var (rotation, name) in new[] { (BitmapRotation.Clockwise90Degrees, "90°"), (BitmapRotation.Clockwise270Degrees, "270°"), (BitmapRotation.Clockwise180Degrees, "180°") })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rotated = await DecodeAsync(decoder, target, longSide, rotation);
                await Add(results, rotated, name);
                if (Total(results) >= EnoughCharacters) break;
            }
        }
        return results;
    }

    private async Task Add(List<OcrText> results, SoftwareBitmap bitmap, string variant)
    {
        var text = await Recognize(bitmap);
        if (text.Length > 0) results.Add(new OcrText(text, variant));
    }

    private static int Total(IEnumerable<OcrText> results) => results.Select(r => r.Text.Count(char.IsLetterOrDigit)).DefaultIfEmpty(0).Max();

    private async Task<string> Recognize(SoftwareBitmap bitmap)
    {
        var result = await _engine!.RecognizeAsync(bitmap);
        var words = result.Lines.SelectMany(l => l.Words).Select(w => new MS3DPRINT.Manager.Core.Recognition.OcrWord(w.Text, w.BoundingRect.X, w.BoundingRect.Y, w.BoundingRect.Width, w.BoundingRect.Height)).ToList();
        // Windows returns columns as separate blocks: the words are put back on their printed row, straightened by the tilt Windows measured.
        return OcrLayout.ToText(words, result.TextAngle ?? 0);
    }

    private static async Task<SoftwareBitmap> DecodeAsync(BitmapDecoder decoder, uint target, uint longSide, BitmapRotation rotation = BitmapRotation.None)
    {
        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant, Rotation = rotation };
        if (target != longSide)
        {
            var scale = (double)target / longSide;
            transform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        }
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
    }

    private static (byte[] Pixels, int Width, int Height) Read(SoftwareBitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return (pixels, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static SoftwareBitmap Create(byte[] pixels, int width, int height) => SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

    private static (double Mean, double Spread, byte Low, byte High) Luminance(byte[] pixels)
    {
        var histogram = new int[256];
        long count = 0, sum = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4 * 4)
        {
            var y = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
            histogram[y]++; sum += y; count++;
        }
        if (count == 0) return (128, 0, 0, 255);
        byte Percentile(double p) { var target = count * p; long seen = 0; for (var v = 0; v < 256; v++) { seen += histogram[v]; if (seen >= target) return (byte)v; } return 255; }
        var low = Percentile(0.01); var high = Percentile(0.99);
        return ((double)sum / count, high - low, low, high);
    }

    private static byte[] Invert(byte[] pixels)
    {
        var result = new byte[pixels.Length];
        for (var i = 0; i + 3 < pixels.Length; i += 4) { result[i] = (byte)(255 - pixels[i]); result[i + 1] = (byte)(255 - pixels[i + 1]); result[i + 2] = (byte)(255 - pixels[i + 2]); result[i + 3] = 255; }
        return result;
    }

    /// <summary>Spreads the used range of grey levels over 0-255 and turns the picture to grey: shadows and faded thermal paper become readable.</summary>
    private static byte[] Stretch(byte[] pixels, byte low, byte high)
    {
        var result = new byte[pixels.Length];
        var range = Math.Max(1, high - low);
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var y = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
            var v = (byte)Math.Clamp((y - low) * 255 / range, 0, 255);
            result[i] = result[i + 1] = result[i + 2] = v; result[i + 3] = 255;
        }
        return result;
    }
}

/// <summary>Renders PDF pages to pictures with Windows' own PDF renderer.</summary>
public sealed class WindowsPdfRasterizer : IPdfRasterizer
{
    public async Task<IReadOnlyList<byte[]>> RenderPagesAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), "ms3d-ocr-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await File.WriteAllBytesAsync(path, pdf, cancellationToken);
            var renderer = await PdfPageRenderer.LoadAsync(path);
            var pages = new List<byte[]>();
            for (var i = 0; i < Math.Min(renderer.PageCount, maxPages); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages.Add(await renderer.RenderPageAsync(i, 2200));
            }
            return pages;
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
