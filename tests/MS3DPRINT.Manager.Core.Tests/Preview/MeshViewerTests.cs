using System.Numerics;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class MeshViewerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms3d-mesh-" + Guid.NewGuid().ToString("N"));

    public MeshViewerTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Load_ReadsBinaryStl()
    {
        var path = Path.Combine(_directory, "triangle.stl");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[80]);
            writer.Write(1u);
            foreach (var value in new float[] { 0, 0, 1, 0, 0, 0, 10, 0, 0, 0, 20, 0 }) writer.Write(value);
            writer.Write((ushort)0);
        }

        var mesh = MeshLoader.Load(path);

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(new Vector3(10, 20, 0), mesh.Size);
    }

    [Fact]
    public void Load_ReadsAsciiStl()
    {
        var path = Write("ascii.stl", "solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1.5 0 0\nvertex 0 2 0\nendloop\nendfacet\nendsolid t\n");

        var mesh = MeshLoader.Load(path);

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(1.5f, mesh.Size.X);
        Assert.Equal(2f, mesh.Size.Y);
    }

    [Fact]
    public void Load_TriangulatesObjQuadsAndSupportsNegativeIndices()
    {
        var path = Write("quad.obj", "v 0 0 0\nv 4 0 0\nv 4 3 0\nv 0 3 0\nf 1/1/1 2/2/1 3/3/1 4/4/1\nf -4 -3 -2\n");

        var mesh = MeshLoader.Load(path);

        Assert.Equal(3, mesh.TriangleCount);
        Assert.Equal(new Vector3(4, 3, 0), mesh.Size);
    }

    [Fact]
    public void Load_RejectsFilesWithoutGeometry()
    {
        var path = Write("empty.obj", "# nothing here\n");

        Assert.Throws<InvalidDataException>(() => MeshLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsFacesThatReferenceMissingVertices()
    {
        var path = Write("broken.obj", "v 0 0 0\nv 1 0 0\nf 1 2 9\n");

        Assert.Throws<InvalidDataException>(() => MeshLoader.Load(path));
    }

    [Fact]
    public void Load_RejectsUnsupportedExtensions()
        => Assert.Throws<NotSupportedException>(() => MeshLoader.Load(Write("model.step", "x")));

    private static MeshModel Cube()
    {
        Vector3[] corners =
        [
            new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0),
            new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10)
        ];
        int[] indices =
        [
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
        ];
        return new MeshModel(corners, indices);
    }

    [Fact]
    public void Render_DrawsTheModelOverTheBackground()
    {
        const uint background = 0xFF101923;
        var options = new RenderOptions(160, 120, Wireframe: false, 0xFF58A6FF, background);

        var pixels = SoftwareRenderer.Render(Cube(), new ViewCamera(), options);

        Assert.Equal(160 * 120, pixels.Length);
        Assert.Equal(background, pixels[0]);
        Assert.Contains(pixels, pixel => pixel != background);
        Assert.NotEqual(background, pixels[60 * 160 + 80]);
    }

    [Fact]
    public void Render_WireframeAlsoDrawsOnlyModelPixels()
    {
        const uint background = 0xFF101923;
        var options = new RenderOptions(160, 120, Wireframe: true, 0xFFFFFFFF, background);

        var pixels = SoftwareRenderer.Render(Cube(), new ViewCamera(), options);

        Assert.Equal(background, pixels[0]);
        Assert.Contains(pixels, pixel => pixel == 0xFFFFFFFF);
    }

    [Fact]
    public void Render_StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SoftwareRenderer.Render(Cube(), new ViewCamera(), new RenderOptions(64, 64, false, 0xFFFFFFFF, 0xFF000000), null, cancellation.Token));
    }
}
