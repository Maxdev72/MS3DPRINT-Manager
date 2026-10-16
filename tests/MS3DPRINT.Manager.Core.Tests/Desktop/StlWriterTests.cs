using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class StlWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-stl-" + Guid.NewGuid().ToString("N"));

    public StlWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static MeshModel Cube(float size)
    {
        Vector3[] corners = [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0), new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new MeshModel(corners, faces);
    }

    [Fact]
    public void A_written_model_reads_back_with_the_same_triangles()
    {
        var path = Path.Combine(_directory, "cube.stl");

        StlWriter.Write(path, Cube(10));
        var back = MeshLoader.Load(path);

        Assert.Equal(12, back.TriangleCount);
        Assert.Equal(10, back.Size.X, 4);
        Assert.Equal(10, back.Size.Z, 4);
    }

    [Fact]
    public void The_file_is_a_binary_stl_of_the_expected_size()
    {
        var path = Path.Combine(_directory, "cube.stl");

        StlWriter.Write(path, Cube(10));

        Assert.Equal(84 + 50 * 12, new FileInfo(path).Length);
    }

    [Fact]
    public void Units_are_converted_to_millimetres_on_the_way_out()
    {
        var path = Path.Combine(_directory, "inch.stl");

        StlWriter.Write(path, Cube(1), 25.4);
        var back = MeshLoader.Load(path);

        Assert.Equal(25.4, back.Size.X, 3);
        Assert.Equal(25.4, back.Size.Y, 3);
    }

    [Fact]
    public void An_existing_file_is_replaced()
    {
        var path = Path.Combine(_directory, "cube.stl");
        File.WriteAllText(path, new string('x', 5000));

        StlWriter.Write(path, Cube(10));

        Assert.Equal(84 + 50 * 12, new FileInfo(path).Length);
    }

    [Fact]
    public void A_scale_that_is_not_positive_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StlWriter.Write(Path.Combine(_directory, "x.stl"), Cube(1), 0));
}
