using Android.Graphics;
using Android.Media;

namespace MS3DPRINT.Manager.Companion.Services;

/// <summary>Turns whatever the camera or the gallery gives into a small upright JPEG (long side 1600 px) that travels well through Nextcloud.</summary>
public static class PhotoTools
{
    public const int MaxSide = 1600;

    public static byte[]? Normalize(byte[] source)
    {
        if (source.Length == 0) return null;
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(source, 0, source.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) return null;
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= MaxSide) sample *= 2;
        using var decoded = BitmapFactory.DecodeByteArray(source, 0, source.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null) return null;
        var degrees = Orientation(source);
        var scale = Math.Min(1f, MaxSide / (float)Math.Max(decoded.Width, decoded.Height));
        using var matrix = new Matrix();
        matrix.PostRotate(degrees); matrix.PostScale(scale, scale);
        using var upright = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
        using var output = new MemoryStream();
        upright!.Compress(Bitmap.CompressFormat.Jpeg!, 85, output);
        return output.ToArray();
    }

    public static byte[]? NormalizeFile(string path) => Normalize(File.ReadAllBytes(path));

    internal static float Orientation(byte[] source)
    {
        try
        {
            using var stream = new MemoryStream(source);
            var exif = new ExifInterface(stream);
            return exif.GetAttributeInt(ExifInterface.TagOrientation, (int)global::Android.Media.Orientation.Normal) switch
            {
                (int)global::Android.Media.Orientation.Rotate90 => 90f,
                (int)global::Android.Media.Orientation.Rotate180 => 180f,
                (int)global::Android.Media.Orientation.Rotate270 => 270f,
                _ => 0f
            };
        }
        catch (Exception exception) when (exception is IOException or Java.IO.IOException or ArgumentException) { return 0f; }
    }
}
