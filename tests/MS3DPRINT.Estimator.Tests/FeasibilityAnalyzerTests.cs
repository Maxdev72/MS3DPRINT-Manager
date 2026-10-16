using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class FeasibilityAnalyzerTests
{
    /// <summary>A closed box, 12 triangles wound counter-clockwise seen from outside.</summary>
    private static (List<Vector3> Positions, List<int> Indices) Box(float x0, float y0, float z0, float x1, float y1, float z1, List<Vector3>? positions = null, List<int>? indices = null)
    {
        positions ??= [];
        indices ??= [];
        var o = positions.Count;
        positions.AddRange([new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)]);
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        indices.AddRange(faces.Select(face => face + o));
        return (positions, indices);
    }

    private static PrintMesh Block(float x, float y, float z)
    {
        var (positions, indices) = Box(0, 0, 0, x, y, z);
        return new PrintMesh([.. positions], [.. indices]);
    }

    /// <summary>A convex polygon drawn in the X–Z plane (counter-clockwise, X to the right, Z up) pulled along Y.</summary>
    private static PrintMesh Prism((float X, float Z)[] polygon, float y0, float y1)
    {
        var n = polygon.Length;
        var positions = polygon.Select(p => new Vector3(p.X, y0, p.Z)).Concat(polygon.Select(p => new Vector3(p.X, y1, p.Z))).ToArray();
        var indices = new List<int>();
        for (var i = 1; i < n - 1; i++)
        {
            indices.AddRange([0, i, i + 1]);
            indices.AddRange([n, n + i + 1, n + i]);
        }
        for (var i = 0; i < n; i++)
        {
            var next = (i + 1) % n;
            indices.AddRange([i, n + next, next]);
            indices.AddRange([i, n + i, n + next]);
        }
        return new PrintMesh(positions, [.. indices]);
    }

    private static FeasibilityIssue? Issue(FeasibilityReport report, FeasibilityCode code) => report.Issues.FirstOrDefault(issue => issue.Code == code);

    [Fact]
    public void ASolidCubeHasNothingToReport()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(20, 20, 20));

        Assert.Equal(FeasibilityVerdict.Printable, report.Verdict);
        Assert.Empty(report.Issues);
        Assert.Equal(1, report.ShellCount);
        Assert.Equal(20, report.MinWallThicknessMm!.Value, 1);
    }

    [Fact]
    public void AWallBelowTheNozzleWidthCannotBePrinted()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(30, 30, 0.3f));

        var thin = Issue(report, FeasibilityCode.ThinWalls);
        Assert.NotNull(thin);
        Assert.Equal(FeasibilitySeverity.Error, thin.Severity);
        Assert.Equal(FeasibilityVerdict.NotPrintable, report.Verdict);
        Assert.Equal(0.3, report.MinWallThicknessMm!.Value, 2);
        Assert.NotEmpty(thin.Points!);
    }

    [Fact]
    public void AWallBetweenOneAndTwoLinesIsOnlyFragile()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(30, 30, 0.6f));

        var thin = Issue(report, FeasibilityCode.ThinWalls);
        Assert.NotNull(thin);
        Assert.Equal(FeasibilitySeverity.Warning, thin.Severity);
        Assert.Equal(FeasibilityVerdict.PrintableWithWarnings, report.Verdict);
    }

    [Fact]
    public void ASolidWallIsNotThin()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(30, 30, 2));

        Assert.Null(Issue(report, FeasibilityCode.ThinWalls));
    }

    [Fact]
    public void ThinnessFollowsTheNozzle()
    {
        // 0.6 mm is two lines of a 0.3 mm nozzle, so not fragile there
        var report = FeasibilityAnalyzer.Analyze(Block(30, 30, 0.7f), machine: new MachineProfile { NozzleDiameterMm = 0.3 });

        Assert.Null(Issue(report, FeasibilityCode.ThinWalls));
    }

    [Fact]
    public void AMeshWithAHoleIsRefused()
    {
        var cube = Block(20, 20, 20);
        var withoutTop = cube.Indices.Take(6).Concat(cube.Indices.Skip(12)).ToArray();

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh(cube.Positions, withoutTop));

        Assert.Equal(FeasibilityVerdict.NotPrintable, report.Verdict);
        Assert.Equal(4, Issue(report, FeasibilityCode.OpenMesh)!.Value);
        Assert.NotNull(Issue(report, FeasibilityCode.NotAnalysed));
    }

    [Fact]
    public void AHoleIsOnlyAWarningWhenTheVendorRepairsFiles()
    {
        var cube = Block(20, 20, 20);
        var withoutTop = cube.Indices.Take(6).Concat(cube.Indices.Skip(12)).ToArray();

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh(cube.Positions, withoutTop), new FeasibilitySettings { RequireWatertight = false });

        Assert.Equal(FeasibilityVerdict.PrintableWithWarnings, report.Verdict);
    }

    [Fact]
    public void AnInsideOutMeshIsCorrectedAndStillAnalysed()
    {
        var cube = Block(20, 20, 20);
        var flipped = (int[])cube.Indices.Clone();
        for (var t = 0; t < flipped.Length; t += 3) (flipped[t + 1], flipped[t + 2]) = (flipped[t + 2], flipped[t + 1]);

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh(cube.Positions, flipped));

        Assert.Equal(FeasibilityVerdict.Printable, report.Verdict);
        Assert.Equal(FeasibilitySeverity.Info, Issue(report, FeasibilityCode.InvertedMesh)!.Severity);
        Assert.Equal(20, report.MinWallThicknessMm!.Value, 1);
    }

    [Fact]
    public void OneFlippedFaceIsReported()
    {
        var cube = Block(20, 20, 20);
        var indices = (int[])cube.Indices.Clone();
        (indices[1], indices[2]) = (indices[2], indices[1]);

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh(cube.Positions, indices));

        Assert.NotNull(Issue(report, FeasibilityCode.InconsistentWinding));
        Assert.Null(Issue(report, FeasibilityCode.OpenMesh));
    }

    [Fact]
    public void SeparatePiecesAreCounted()
    {
        var (positions, indices) = Box(0, 0, 0, 10, 10, 10);
        Box(30, 0, 0, 40, 10, 10, positions, indices);

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh([.. positions], [.. indices]));

        Assert.Equal(2, report.ShellCount);
        Assert.Equal(2, Issue(report, FeasibilityCode.MultipleShells)!.Value);
    }

    [Fact]
    public void ACavityInsideAPieceIsNotAnotherPiece()
    {
        var (positions, indices) = Box(0, 0, 0, 30, 30, 30);
        var (inner, innerIndices) = Box(2, 2, 2, 28, 28, 28);
        var offset = positions.Count;
        positions.AddRange(inner);
        for (var t = 0; t < innerIndices.Count; t += 3)
        {
            indices.Add(innerIndices[t] + offset);
            indices.Add(innerIndices[t + 2] + offset);
            indices.Add(innerIndices[t + 1] + offset);
        }

        var report = FeasibilityAnalyzer.Analyze(new PrintMesh([.. positions], [.. indices]));

        Assert.Equal(1, report.ShellCount);
        Assert.Null(Issue(report, FeasibilityCode.MultipleShells));
        Assert.Null(Issue(report, FeasibilityCode.OpenMesh));
    }

    [Fact]
    public void AnOverhangNeedsSupportAndItsAreaIsMeasured()
    {
        // wider at the top: each side leans 63° from the vertical, 20 mm wide when seen from above, 20 mm long
        var trapezoid = Prism([(5, 0), (15, 0), (35, 10), (-15, 10)], 0, 20);
        var report = FeasibilityAnalyzer.Analyze(trapezoid);

        var overhang = Issue(report, FeasibilityCode.Overhangs);
        Assert.NotNull(overhang);
        Assert.InRange(report.OverhangAreaMm2, 760, 840);
        Assert.NotEmpty(overhang.Points!);
    }

    [Fact]
    public void AGentleSlopeNeedsNoSupport()
    {
        var gentle = Prism([(0, 0), (30, 0), (20, 10), (10, 10)], 0, 20);

        var report = FeasibilityAnalyzer.Analyze(gentle);

        Assert.Null(Issue(report, FeasibilityCode.Overhangs));
    }

    [Fact]
    public void AModelLargerThanThePrinterIsRefused()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(300, 50, 50), new FeasibilitySettings { BuildVolumeMm = new Vector3(256, 256, 256) });

        Assert.Equal(FeasibilityVerdict.NotPrintable, report.Verdict);
        Assert.NotNull(Issue(report, FeasibilityCode.TooLarge));
    }

    [Fact]
    public void ATinyModelSuggestsWrongUnits()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(1, 1, 1));

        Assert.NotNull(Issue(report, FeasibilityCode.SuspiciousScale));
    }

    [Fact]
    public void ATallPinOnATinyBaseMayComeOff()
    {
        var report = FeasibilityAnalyzer.Analyze(Block(2, 2, 30));

        Assert.Equal(FeasibilitySeverity.Warning, Issue(report, FeasibilityCode.SmallBase)!.Severity);
    }

    [Fact]
    public void AFlatMeshIsEmpty()
    {
        var flat = new PrintMesh([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [0, 1, 2]);

        var report = FeasibilityAnalyzer.Analyze(flat);

        Assert.Equal(FeasibilityVerdict.NotPrintable, report.Verdict);
        Assert.NotNull(Issue(report, FeasibilityCode.EmptyModel));
    }

    [Fact]
    public void ACancelledAnalysisStops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => FeasibilityAnalyzer.Analyze(Block(20, 20, 20), cancellationToken: cancelled.Token));
    }
}
