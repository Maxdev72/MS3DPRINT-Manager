using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class FileOpenRoutingTests
{
    [Theory]
    [InlineData("piece.stl", FileOpenTarget.ThreeD)]
    [InlineData("piece.OBJ", FileOpenTarget.ThreeD)]
    [InlineData("photo.jpeg", FileOpenTarget.Document)]
    [InlineData("DEV2026-05.pdf", FileOpenTarget.Document)]
    [InlineData("modele.step", FileOpenTarget.External)]
    public void Decide_RoutesFilesToTheCorrectViewer(string path, FileOpenTarget expected)
        => Assert.Equal(expected, FileOpenRouting.Decide(path));
}
