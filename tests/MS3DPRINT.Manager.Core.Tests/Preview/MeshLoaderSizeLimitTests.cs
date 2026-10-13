using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class MeshLoaderSizeLimitTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-big-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    [Fact]
    public void A_file_above_the_limit_is_refused_with_a_clear_message()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "enorme.stl");
        using (var stream = new FileStream(path, FileMode.Create)) stream.SetLength(MeshLoader.MaxFileBytes + 1);

        var failure = Assert.Throws<InvalidDataException>(() => MeshLoader.Load(path));

        Assert.Contains("trop volumineux", failure.Message);
    }
}
