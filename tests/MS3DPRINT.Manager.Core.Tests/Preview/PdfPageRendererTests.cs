using System.Text;
using System.Buffers.Binary;
using MS3DPRINT.Manager.App.Preview;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class PdfPageRendererTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "MS3DPRINT-pdf-preview-" + Guid.NewGuid().ToString("N") + ".pdf");

    [Fact]
    public async Task LoadAndRender_ProducesPngForOnePagePdf()
    {
        File.WriteAllBytes(_path, CreateBlankPdf());

        var renderer = await PdfPageRenderer.LoadAsync(_path);
        var png = await renderer.RenderPageAsync(0);

        Assert.Equal(1, renderer.PageCount);
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => renderer.RenderPageAsync(1));
    }

    [Fact]
    public async Task RenderPage_BoundsBothDimensionsOfTallPage()
    {
        File.WriteAllBytes(_path, CreateBlankPdf(pageHeight: 600));
        var renderer = await PdfPageRenderer.LoadAsync(_path);

        var png = await renderer.RenderPageAsync(0);

        Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), 1, 1200);
        Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)), 1, 1200);
    }

    [Fact]
    public async Task RenderPage_CanOpenSecondPageOfMultipageDocument()
    {
        File.WriteAllBytes(_path, CreateBlankPdf(pageCount: 2));
        var renderer = await PdfPageRenderer.LoadAsync(_path);

        var secondPage = await renderer.RenderPageAsync(1);

        Assert.Equal(2, renderer.PageCount);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, secondPage[..4]);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(600)]
    public async Task RenderPage_UsesRequestedWidthAndBoundsExcessiveZoom(int pageHeight)
    {
        File.WriteAllBytes(_path, CreateBlankPdf(pageHeight: pageHeight));
        var renderer = await PdfPageRenderer.LoadAsync(_path);
        var method = typeof(PdfPageRenderer).GetMethod("RenderPageAsync", [typeof(int), typeof(int)]);
        Assert.NotNull(method);
        var normal = await (Task<byte[]>)method.Invoke(renderer, [0, 600])!;
        var zoomed = await (Task<byte[]>)method.Invoke(renderer, [0, 1200])!;
        var excessive = await (Task<byte[]>)method.Invoke(renderer, [0, 100_000])!;
        Assert.Equal(600, BinaryPrimitives.ReadInt32BigEndian(normal.AsSpan(16, 4)));
        Assert.Equal(1200, BinaryPrimitives.ReadInt32BigEndian(zoomed.AsSpan(16, 4)));
        var width = BinaryPrimitives.ReadInt32BigEndian(excessive.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(excessive.AsSpan(20, 4));
        Assert.InRange(width, 1, 4096);
        Assert.InRange(height, 1, 4096);
        Assert.True((long)width * height <= 12_000_000);
    }

    internal static byte[] CreateBlankPdf(int pageHeight = 200, int pageCount = 1)
    {
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(index => $"{3 + index * 2} 0 R"));
        AddObject($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        for (var index = 0; index < pageCount; index++)
        {
            AddObject($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 {pageHeight}] /Resources << >> /Contents {4 + index * 2} 0 R >>");
            AddObject("<< /Length 0 >>\nstream\n\nendstream");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(offsets.Count).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(offsets.Count).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());

        void AddObject(string body)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(offsets.Count - 1).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
