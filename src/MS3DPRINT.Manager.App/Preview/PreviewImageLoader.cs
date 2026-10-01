using System.Windows.Media.Imaging;

namespace MS3DPRINT.Manager.App.Preview;

public static class PreviewImageLoader
{
    public static BitmapImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Decode(stream);
    }

    public static BitmapImage Load(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return Decode(stream);
    }

    private static BitmapImage Decode(Stream stream)
    {
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var originalWidth = frame.PixelWidth;
        var originalHeight = frame.PixelHeight;
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (originalWidth > 2000 || originalHeight > 2000)
        {
            if (originalWidth >= originalHeight) image.DecodePixelWidth = 2000;
            else image.DecodePixelHeight = 2000;
        }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
