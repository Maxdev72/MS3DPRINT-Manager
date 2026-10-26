using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using MS3DPRINT.Manager.Core.Recognition;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>Text recognition by Google ML Kit with the model bundled in the app: no network, no account. Dark screenshots are inverted and low-contrast photos stretched before a second reading.</summary>
public sealed class MlKitOcrEngine : IOcrEngine
{
    private const int EnoughCharacters = 60;
    private const int MaxSide = 3000;
    private readonly ITextRecognizer _recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);

    public async Task<IReadOnlyList<OcrText>> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        var results = new List<OcrText>();
        using var bitmap = Decode(image);
        if (bitmap is null) return results;
        await Add(results, bitmap, "");
        var (mean, low, high) = Luminance(bitmap);
        if (mean < 110)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var inverted = Filter(bitmap, invert: true, low, high);
            await Add(results, inverted, "inversé");
        }
        if (Best(results) < EnoughCharacters || high - low < 150)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stretched = Filter(bitmap, invert: false, low, high);
            await Add(results, stretched, "contraste");
        }
        if (Best(results) < EnoughCharacters)
        {
            foreach (var degrees in new[] { 90f, 270f, 180f })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var matrix = new Matrix(); matrix.PostRotate(degrees);
                using var rotated = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                await Add(results, rotated!, degrees + "°");
                if (Best(results) >= EnoughCharacters) break;
            }
        }
        return results;
    }

    private static int Best(List<OcrText> results) => results.Select(r => r.Text.Count(char.IsLetterOrDigit)).DefaultIfEmpty(0).Max();

    private async Task Add(List<OcrText> results, Bitmap bitmap, string variant)
    {
        using var input = InputImage.FromBitmap(bitmap, 0);
        var task = _recognizer.Process(input);
        var text = (Text?)await Android.Gms.Extensions.TasksExtensions.AsAsync<Text>(task);
        if (text is null) return;
        var words = new List<OcrWord>();
        foreach (var block in text.TextBlocks)
            foreach (var line in block.Lines)
                foreach (var element in line.Elements)
                    if (element.BoundingBox is { } box && !string.IsNullOrWhiteSpace(element.Text)) words.Add(new OcrWord(element.Text, box.Left, box.Top, box.Width(), box.Height()));
        var layout = OcrLayout.ToText(words);
        if (layout.Length > 0) results.Add(new OcrText(layout, variant));
    }

    /// <summary>Decoded upright, at most 3000 px on the long side; small pictures are enlarged (letters should be at least about 20 px tall).</summary>
    private static Bitmap? Decode(byte[] source)
    {
        if (source.Length == 0) return null;
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(source, 0, source.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) return null;
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= MaxSide) sample *= 2;
        using var decoded = BitmapFactory.DecodeByteArray(source, 0, source.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null) return null;
        var longSide = Math.Max(decoded.Width, decoded.Height);
        var scale = longSide < 1500 ? Math.Min(3f, 2400f / longSide) : longSide > MaxSide ? MaxSide / (float)longSide : 1f;
        using var matrix = new Matrix();
        matrix.PostRotate(PhotoTools.Orientation(source)); matrix.PostScale(scale, scale);
        return Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
    }

    private static (double Mean, int Low, int High) Luminance(Bitmap bitmap)
    {
        using var small = Bitmap.CreateScaledBitmap(bitmap, Math.Max(1, Math.Min(96, bitmap.Width)), Math.Max(1, Math.Min(96, bitmap.Height)), true)!;
        var pixels = new int[small.Width * small.Height];
        small.GetPixels(pixels, 0, small.Width, 0, 0, small.Width, small.Height);
        var histogram = new int[256]; long sum = 0;
        foreach (var p in pixels)
        {
            var y = (((p >> 16) & 0xFF) * 299 + ((p >> 8) & 0xFF) * 587 + (p & 0xFF) * 114) / 1000;
            histogram[y]++; sum += y;
        }
        int Percentile(double fraction) { var target = pixels.Length * fraction; long seen = 0; for (var v = 0; v < 256; v++) { seen += histogram[v]; if (seen >= target) return v; } return 255; }
        return ((double)sum / pixels.Length, Percentile(0.01), Percentile(0.99));
    }

    /// <summary>Grey levels spread over the full range (faded paper, shadows), optionally inverted (light text on a dark background).</summary>
    private static Bitmap Filter(Bitmap source, bool invert, int low, int high)
    {
        var range = Math.Max(1, high - low);
        var gain = 255f / range;
        var offset = -low * gain;
        float[] matrix = invert
            ? [-0.299f * gain, -0.587f * gain, -0.114f * gain, 0, 255 - offset, -0.299f * gain, -0.587f * gain, -0.114f * gain, 0, 255 - offset, -0.299f * gain, -0.587f * gain, -0.114f * gain, 0, 255 - offset, 0, 0, 0, 1, 0]
            : [0.299f * gain, 0.587f * gain, 0.114f * gain, 0, offset, 0.299f * gain, 0.587f * gain, 0.114f * gain, 0, offset, 0.299f * gain, 0.587f * gain, 0.114f * gain, 0, offset, 0, 0, 0, 1, 0];
        var output = Bitmap.CreateBitmap(source.Width, source.Height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(output);
        using var paint = new Paint();
        using var filter = new ColorMatrixColorFilter(new ColorMatrix(matrix));
        paint.SetColorFilter(filter);
        canvas.DrawBitmap(source, 0, 0, paint);
        return output;
    }
}

/// <summary>Renders the pages of a PDF with Android's own renderer, for scanned documents.</summary>
public sealed class AndroidPdfRasterizer : IPdfRasterizer
{
    public Task<IReadOnlyList<byte[]>> RenderPagesAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<byte[]>>(() =>
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ocr-" + Guid.NewGuid().ToString("N") + ".pdf");
        var pages = new List<byte[]>();
        try
        {
            File.WriteAllBytes(path, pdf);
            using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly)!;
            using var renderer = new PdfRenderer(descriptor);
            for (var i = 0; i < Math.Min(renderer.PageCount, maxPages); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = renderer.OpenPage(i)!;
                var width = 2200; var height = (int)(width * (double)page.Height / page.Width);
                using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
                bitmap.EraseColor(Color.White);
                page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
                using var stream = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
                pages.Add(stream.ToArray());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
        return pages;
    }, cancellationToken);
}
