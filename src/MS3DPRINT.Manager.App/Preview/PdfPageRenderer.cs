using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MS3DPRINT.Manager.App.Preview;

public sealed class PdfPageRenderer
{
    private readonly PdfDocument _document;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private const double MaxDimension = 4096;
    private const double MaxPixels = 12_000_000;

    private PdfPageRenderer(PdfDocument document) => _document = document;
    public int PageCount => checked((int)_document.PageCount);

    public static async Task<PdfPageRenderer> LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        return new PdfPageRenderer(await PdfDocument.LoadFromFileAsync(file));
    }

    public (double Width, double Height) GetPageSize(int index)
    {
        ValidateIndex(index);
        using var page = _document.GetPage((uint)index);
        return (page.Size.Width, page.Size.Height);
    }

    public Task<byte[]> RenderPageAsync(int index)
    {
        var size = GetPageSize(index);
        return RenderPageAsync(index, Math.Max(1, (int)(1200 * size.Width / Math.Max(size.Width, size.Height))));
    }

    public Task<byte[]> RenderPageAsync(int index, int destinationWidth)
    {
        ValidateIndex(index);
        if (destinationWidth <= 0) throw new ArgumentOutOfRangeException(nameof(destinationWidth));
        return RenderSerializedAsync(index, destinationWidth);
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private async Task<byte[]> RenderSerializedAsync(int index, int destinationWidth)
    {
        await _renderGate.WaitAsync();
        try { return await Task.Run(() => RenderPageCoreAsync(index, destinationWidth)); }
        finally { _renderGate.Release(); }
    }

    private async Task<byte[]> RenderPageCoreAsync(int index, int destinationWidth)
    {
        using var page = _document.GetPage((uint)index);
        var width = (double)destinationWidth;
        var height = width * page.Size.Height / page.Size.Width;
        var scale = Math.Min(1, Math.Min(MaxDimension / Math.Max(width, height), Math.Sqrt(MaxPixels / (width * height))));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Floor(width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Floor(height * scale))
        };
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        if (stream.Size > int.MaxValue) throw new InvalidDataException("La page PDF est trop volumineuse pour l’aperçu.");
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var count = checked((uint)stream.Size);
        await reader.LoadAsync(count);
        var bytes = new byte[count];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
