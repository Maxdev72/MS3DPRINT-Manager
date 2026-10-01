using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MS3DPRINT.Manager.App.Preview;

public sealed class PdfPageRenderer
{
    private readonly PdfDocument _document;

    private PdfPageRenderer(PdfDocument document) => _document = document;

    public int PageCount => checked((int)_document.PageCount);

    public static async Task<PdfPageRenderer> LoadAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var document = await PdfDocument.LoadFromFileAsync(file);
        return new PdfPageRenderer(document);
    }

    public Task<byte[]> RenderPageAsync(int index)
    {
        if (index < 0 || index >= PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        return Task.Run(() => RenderPageCoreAsync(index));
    }

    private async Task<byte[]> RenderPageCoreAsync(int index)
    {
        using var page = _document.GetPage((uint)index);
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions();
        if (page.Size.Width >= page.Size.Height) options.DestinationWidth = 1200;
        else options.DestinationHeight = 1200;
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
