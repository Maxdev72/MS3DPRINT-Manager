using MS3DPRINT.Manager.App.Preview;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class ModelFileSelectionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "MS3DPRINT-model-" + Guid.NewGuid().ToString("N") + ".stl");

    [Fact]
    public void Select_AcceptsOneExistingStl()
    {
        File.WriteAllText(_path, "solid empty\nendsolid empty");

        Assert.Equal(Path.GetFullPath(_path), ModelFileSelection.Select([_path]));
    }

    [Fact]
    public void Select_RejectsUnsupportedAndMultipleFiles()
    {
        File.WriteAllText(_path, "solid empty\nendsolid empty");

        Assert.Throws<NotSupportedException>(() => ModelFileSelection.Select([Path.ChangeExtension(_path, ".step")]));
        Assert.Throws<ArgumentException>(() => ModelFileSelection.Select([_path, _path]));
    }

    [Fact]
    public void Select_RejectsMissingFile()
        => Assert.Throws<FileNotFoundException>(() => ModelFileSelection.Select([_path]));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
