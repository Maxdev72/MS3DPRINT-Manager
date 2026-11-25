using SkiaSharp;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>How a rendered image is written: format (png, jpeg, webp, tiff), quality for JPEG and WebP, longest side (0 = as rendered).</summary>
public sealed record OutputOptions(string Format, int Quality = 90, bool MaxCompression = true, int MaxSide = 0)
{
    public string Extension => Format switch { "jpeg" => ".jpg", "webp" => ".webp", "tiff" => ".tif", _ => ".png" };
    public bool KeepsTransparency => Format is "png" or "webp" or "tiff";
}

/// <summary>
/// Writes Blender's PNG in the chosen format, re-encoded (so without Blender's metadata), resized if asked; JPEG gets a white
/// background where the render is transparent. Also lays several variants out on one contact sheet.
/// </summary>
public static class RenderOutput
{
    public static byte[] Encode(string png, OutputOptions options)
    {
        using var source = SKBitmap.Decode(png) ?? throw new InvalidDataException("Image de rendu illisible.");
        using var sized = Resize(source, options.MaxSide);
        return Encode(sized, options);
    }

    public static byte[] Encode(SKBitmap bitmap, OutputOptions options)
    {
        using var flat = options.Format == "jpeg" ? OnWhite(bitmap) : null;
        using var pixmap = (flat ?? bitmap).PeekPixels();
        using SKData? data = options.Format switch
        {
            "jpeg" => pixmap.Encode(new SKJpegEncoderOptions(options.Quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)),
            "webp" => pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, options.Quality)),
            _ => pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, options.MaxCompression ? 9 : 6))
        };
        return data?.ToArray() ?? throw new InvalidOperationException("L’image n’a pas pu être encodée dans ce format.");
    }

    /// <summary>The variants side by side, each with its name under it, on a white sheet.</summary>
    public static SKBitmap ContactSheet(IReadOnlyList<(string Png, string Label)> images, int cell = 640)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(images.Count));
        var rows = (int)Math.Ceiling(images.Count / (double)columns);
        const int caption = 56, gap = 24;
        var sheet = new SKBitmap(columns * (cell + gap) + gap, rows * (cell + caption + gap) + gap);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(SKColors.White);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 28);
        using var ink = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22), IsAntialias = true };
        for (var i = 0; i < images.Count; i++)
        {
            var (x, y) = (gap + i % columns * (cell + gap), gap + i / columns * (cell + caption + gap));
            using var image = SKBitmap.Decode(images[i].Png);
            if (image is null) continue;
            var scale = Math.Min(cell / (float)image.Width, cell / (float)image.Height);
            var (w, h) = (image.Width * scale, image.Height * scale);
            using var picture = SKImage.FromBitmap(image);
            canvas.DrawImage(picture, SKRect.Create(x + (cell - w) / 2, y + (cell - h) / 2, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
            canvas.DrawText(images[i].Label, x + cell / 2f, y + cell + 38, SKTextAlign.Center, font, ink);
        }
        return sheet;
    }

    private static SKBitmap Resize(SKBitmap source, int maxSide)
    {
        if (maxSide <= 0 || Math.Max(source.Width, source.Height) <= maxSide) return source.Copy();
        var scale = maxSide / (double)Math.Max(source.Width, source.Height);
        var info = new SKImageInfo((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale), source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? source.Copy();
    }

    private static SKBitmap OnWhite(SKBitmap bitmap)
    {
        var flat = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(flat);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, 0, 0);
        return flat;
    }
}
