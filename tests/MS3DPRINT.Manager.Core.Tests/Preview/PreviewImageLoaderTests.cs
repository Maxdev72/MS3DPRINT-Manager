using MS3DPRINT.Manager.App.Preview;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class PreviewImageLoaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "MS3DPRINT-image-preview-" + Guid.NewGuid().ToString("N") + ".png");

    [Fact]
    public void Load_DecodesImageWithoutKeepingSourceFileOpen()
    {
        File.WriteAllBytes(_path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/yVMAAAAASUVORK5CYII="));

        var image = PreviewImageLoader.Load(_path);

        Assert.Equal(1, image.PixelWidth);
        Assert.Equal(1, image.PixelHeight);
        Assert.True(image.IsFrozen);
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void Load_BoundsTallImageHeightWithoutUpscalingWidth()
    {
        var bitmap = BitmapSource.Create(1, 5000, 96, 96, PixelFormats.Gray8, null, new byte[5000], 1);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(_path)) encoder.Save(stream);

        var preview = PreviewImageLoader.Load(_path);

        Assert.InRange(preview.PixelHeight, 1, 2000);
        Assert.InRange(preview.PixelWidth, 1, 2000);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
