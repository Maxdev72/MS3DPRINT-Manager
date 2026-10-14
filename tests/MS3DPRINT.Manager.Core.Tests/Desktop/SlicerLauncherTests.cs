using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class SlicerLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-slicer-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void A_file_that_is_not_a_3D_model_is_never_sent_to_the_slicer()
    {
        Directory.CreateDirectory(_directory);
        var document = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(document, "x");
        var program = Path.Combine(_directory, "installer.exe");
        File.WriteAllText(program, "x");

        Assert.Null(SlicerLauncher.TryOpen(document));
        Assert.Null(SlicerLauncher.TryOpen(program));
    }

    [Fact]
    public void A_model_that_does_not_exist_is_not_sent_either()
        => Assert.Null(SlicerLauncher.TryOpen(Path.Combine(_directory, "absent.3mf")));
}
