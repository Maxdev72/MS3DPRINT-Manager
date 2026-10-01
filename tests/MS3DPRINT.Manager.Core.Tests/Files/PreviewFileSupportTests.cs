using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class PreviewFileSupportTests
{
    [Theory]
    [InlineData("photo.JPG", PreviewFileKind.Image)]
    [InlineData("plan.png", PreviewFileKind.Image)]
    [InlineData("scan.bmp", PreviewFileKind.Image)]
    [InlineData("animation.gif", PreviewFileKind.Image)]
    [InlineData("archive.tiff", PreviewFileKind.Image)]
    [InlineData("DEV2026-05.PDF", PreviewFileKind.Pdf)]
    [InlineData("piece.step", PreviewFileKind.None)]
    public void GetKind_ClassifiesSupportedFiles(string path, PreviewFileKind expected)
        => Assert.Equal(expected, PreviewFileSupport.GetKind(path));
}
