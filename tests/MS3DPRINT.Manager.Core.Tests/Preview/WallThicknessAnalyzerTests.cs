using System.Numerics;
using MS3DPRINT.Manager.App.Preview;

namespace MS3DPRINT.Manager.Core.Tests.Preview;

public class WallThicknessAnalyzerTests
{
    [Theory]
    [InlineData(0.4f, true)]
    [InlineData(4f, false)]
    public void ClosedBox_DetectsOnlyWallsBelowThreshold(float height, bool thin)
    {
        var result = WallThicknessAnalyzer.Analyze(Box(height), 1, 1);
        Assert.Equal(0, result.BoundaryEdges);
        Assert.Equal(0, result.NonManifoldEdges);
        Assert.Equal(thin, result.ThinSamples.Count > 0);
        if (thin) Assert.Contains(result.ThinSamples, s => Math.Abs(s.ThicknessMm - height) < 0.001);
    }

    [Fact]
    public void ScaleChangesMillimeterThreshold()
    {
        Assert.Empty(WallThicknessAnalyzer.Analyze(Box(0.4f), 10, 1).ThinSamples);
    }

    [Fact]
    public void NarrowAirGapBetweenThickClosedParts_IsNotAThinWall()
    {
        var lower = Box(4);
        var upper = Box(4).Select(t => new WallTriangle(
            t.A + new Vector3(0, 0, 4.4f),
            t.B + new Vector3(0, 0, 4.4f),
            t.C + new Vector3(0, 0, 4.4f)));
        var result = WallThicknessAnalyzer.Analyze(lower.Concat(upper).ToArray(), 1, 1);

        Assert.Equal(0, result.BoundaryEdges);
        Assert.Empty(result.ThinSamples);
    }

    [Fact]
    public void SmallModelUnits_DoNotMergeSeparateEntryAndExitCrossings()
    {
        const float height = 0.000004f; // 4 mm at the explicitly selected scale.
        const float gap = 0.0000004f; // 0.4 mm of air, not a wall.
        var lower = Box(height);
        var upper = Box(height).Select(t => new WallTriangle(
            t.A + new Vector3(0, 0, height + gap),
            t.B + new Vector3(0, 0, height + gap),
            t.C + new Vector3(0, 0, height + gap)));
        var result = WallThicknessAnalyzer.Analyze(lower.Concat(upper).ToArray(), 1_000_000, 1);

        Assert.Equal(0, result.BoundaryEdges);
        Assert.Empty(result.ThinSamples);
    }

    [Fact]
    public void DistantClosedPart_DoesNotChangeNearbyAirGapParity()
    {
        var lower = Box(4);
        var upper = Box(4).Select(t => Translate(t, new Vector3(0, 0, 4.4f)));
        var distant = Box(4).Select(t => Translate(t, new Vector3(0, 0, 10_000_000)));
        var result = WallThicknessAnalyzer.Analyze(lower.Concat(upper).Concat(distant).ToArray(), 1, 1);

        Assert.Equal(0, result.BoundaryEdges);
        Assert.Empty(result.ThinSamples);
    }

    [Fact]
    public void ReversedWindingStillDetectsThinSolid()
    {
        var inward = Box(0.4f).Select(t => new WallTriangle(t.A, t.C, t.B)).ToArray();
        var result = WallThicknessAnalyzer.Analyze(inward, 1, 1);
        Assert.NotEmpty(result.ThinSamples);
    }

    [Fact]
    public void OpenMesh_ReportsBoundary()
    {
        var result = WallThicknessAnalyzer.Analyze(Box(1).Take(10).ToArray(), 1, 1);
        Assert.True(result.BoundaryEdges > 0);
    }

    [Fact]
    public void DuplicatedFaces_ReportNonManifoldEdges()
    {
        var box = Box(1);
        Assert.True(WallThicknessAnalyzer.Analyze(box.Concat(box).ToArray(), 1, 1).NonManifoldEdges > 0);
    }

    [Fact]
    public void CancellationIsHonored()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => WallThicknessAnalyzer.Analyze(Box(1), 1, 1, source.Token));
    }

    [Fact]
    public void CancellationDuringBvhSort_IsReportedAsCancellation()
    {
        var triangles = Enumerable.Range(0, 1000).SelectMany(i => Box(4).Select(t =>
            new WallTriangle(t.A + new Vector3(i * 20, 0, 0), t.B + new Vector3(i * 20, 0, 0), t.C + new Vector3(i * 20, 0, 0)))).ToArray();
        using var source = new CancellationTokenSource();
        var input = new CancelDuringSortList(triangles, source);

        Assert.Throws<OperationCanceledException>(() => WallThicknessAnalyzer.Analyze(input, 1, 1, source.Token));
        Assert.True(input.CancelledDuringSort);
    }

    [Fact]
    public void LargeMesh_UsesBoundedSamplesAndWork()
    {
        var triangles = Enumerable.Range(0, 10000).SelectMany(i => Box(0.4f).Select(t =>
            new WallTriangle(t.A + new Vector3(i * 20, 0, 0), t.B + new Vector3(i * 20, 0, 0), t.C + new Vector3(i * 20, 0, 0)))).ToArray();
        var result = WallThicknessAnalyzer.Analyze(triangles, 1, 1);
        Assert.InRange(result.SampleCount, 1, 2048);
        Assert.InRange(result.IntersectionTests, 1, 2_000_000);
        Assert.NotEmpty(result.ThinSamples);
    }

    internal static WallTriangle[] Box(float h)
    {
        Vector3[] v = [new(0,0,0), new(10,0,0), new(10,10,0), new(0,10,0), new(0,0,h), new(10,0,h), new(10,10,h), new(0,10,h)];
        int[] ix = [0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7];
        return Enumerable.Range(0, 12).Select(i => new WallTriangle(v[ix[i*3]],v[ix[i*3+1]],v[ix[i*3+2]])).ToArray();
    }

    private static WallTriangle Translate(WallTriangle t, Vector3 offset) =>
        new(t.A + offset, t.B + offset, t.C + offset);

    private sealed class CancelDuringSortList(WallTriangle[] triangles, CancellationTokenSource cancellation)
        : IReadOnlyList<WallTriangle>
    {
        private int _reads;
        public int Count => triangles.Length;
        public bool CancelledDuringSort { get; private set; }
        public WallTriangle this[int index]
        {
            get
            {
                if (++_reads == 2 * triangles.Length + 1000)
                {
                    CancelledDuringSort = true;
                    cancellation.Cancel();
                }
                return triangles[index];
            }
        }
        public IEnumerator<WallTriangle> GetEnumerator() => ((IEnumerable<WallTriangle>)triangles).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
