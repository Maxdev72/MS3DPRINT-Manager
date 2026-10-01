using System.Diagnostics;
using System.Reflection;
using MS3DPRINT.Manager.App.Preview;
using Xunit.Abstractions;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public sealed class GpuModelLoaderTests
{
    private readonly ITestOutputHelper _output;

    public GpuModelLoaderTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void DenseBinaryStl_BenchmarkAndValidateTriangleCount(int triangleCount)
    {
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-dense-{Guid.NewGuid():N}.stl");
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[80]);
            writer.Write((uint)triangleCount);
            for (var index = 0; index < triangleCount; index++)
            {
                foreach (var value in new float[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
                writer.Write((ushort)0);
            }
        }
        try
        {
            var watch = Stopwatch.StartNew();
            var scene = GpuModelLoader.Load(file);
            watch.Stop();
            _output.WriteLine($"STL binaire {triangleCount:N0} triangles : chargement {watch.Elapsed.TotalMilliseconds:N0} ms");
            Assert.Equal(triangleCount, scene.TriangleCount);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void BinaryStl_ProvidesOneTriangleForPreview()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-{Guid.NewGuid():N}.stl");
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[80]);
            writer.Write(1u);
            foreach (var value in new float[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
            writer.Write((ushort)0);
        }

        try
        {
            var scene = GpuModelLoader.Load(file);
            Assert.Equal(1, scene.TriangleCount);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Obj_ProvidesOneTriangleForPreview()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-{Guid.NewGuid():N}.obj");
        File.WriteAllText(file, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        try
        {
            var scene = GpuModelLoader.Load(file);
            Assert.Equal(1, scene.TriangleCount);
            Assert.Equal(1, scene.Bounds.SizeX);
            Assert.Equal(1, scene.Bounds.SizeY);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void AlreadyCancelledLoad_DoesNotReadTheSourceFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            GpuModelLoader.Load(Path.Combine(Path.GetTempPath(), "missing-preview.stl"), cancellation.Token));
    }

    [Fact]
    public async Task CancelledWhileAnotherImportRuns_DoesNotStartANewImport()
    {
        var gateField = typeof(GpuModelLoader).GetField("ImportGate", BindingFlags.NonPublic | BindingFlags.Static);
        var gate = Assert.IsType<SemaphoreSlim>(gateField?.GetValue(null));
        await gate.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var waiting = Task.Run(() => GpuModelLoader.Load("waiting.stl", cancellation.Token));
            await Task.Delay(30);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public void AsciiStl_ProvidesOneTriangleForPreview()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ms3dprint-{Guid.NewGuid():N}.stl");
        File.WriteAllText(file, "solid triangle\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid triangle\n");

        try
        {
            var scene = GpuModelLoader.Load(file);
            Assert.Equal(1, scene.TriangleCount);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
