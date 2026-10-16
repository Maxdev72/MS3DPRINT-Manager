using System.Numerics;
using MS3DPRINT.Estimator;

namespace MS3DPRINT.Estimator.Tests;

public sealed class PrintEstimatorTests
{
    private const double PlaDensity = 1.24;

    /// <summary>No calibration: the volumes are the pure geometry, so the expected figures can be computed by hand.</summary>
    private static readonly MachineProfile Exact = new() { WallFlowFactor = 1, SkinFlowFactor = 1, InfillFlowFactor = 1 };

    /// <summary>A closed box, 12 triangles wound counter-clockwise seen from outside.</summary>
    private static (List<Vector3> Positions, List<int> Indices) Box(float x0, float y0, float z0, float x1, float y1, float z1, List<Vector3>? positions = null, List<int>? indices = null, bool inside = false)
    {
        positions ??= [];
        indices ??= [];
        var o = positions.Count;
        positions.AddRange([new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)]);
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        for (var i = 0; i < faces.Length; i += 3)
        {
            indices.Add(faces[i] + o);
            indices.Add(faces[inside ? i + 2 : i + 1] + o);
            indices.Add(faces[inside ? i + 1 : i + 2] + o);
        }
        return (positions, indices);
    }

    private static PrintMesh Cube(float size = 20)
    {
        var (positions, indices) = Box(0, 0, 0, size, size, size);
        return new PrintMesh([.. positions], [.. indices]);
    }

    /// <summary>A pillar with an arm leaving its top sideways: the underside of the arm needs support.</summary>
    private static PrintMesh PillarWithArm()
    {
        var (positions, indices) = Box(0, 0, 0, 10, 10, 20);
        Box(10, 0, 10, 30, 10, 20, positions, indices);
        return new PrintMesh([.. positions], [.. indices]);
    }

    private static EstimateSettings Solid() => new() { InfillPercent = 100 };

    [Fact]
    public void SolidCubeWeighsItsVolumeTimesDensity()
    {
        var result = PrintEstimator.Estimate(Cube(), Solid(), Exact);

        Assert.Equal(8000, result.ModelVolumeMm3, 1);
        Assert.Equal(8 * PlaDensity, result.WeightGrams, 2);
        Assert.Equal(100, result.LayerCount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void InfillWallsAndSkinsFollowTheirGeometry()
    {
        var result = PrintEstimator.Estimate(Cube(), new EstimateSettings { InfillPercent = 15, WallCount = 2, TopBottomThicknessMm = 0.8 }, Exact);

        var wall = 20 * 80 * 2 * new MachineProfile().ExtrusionWidthMm;
        var inner = 400 - 80 * 2 * new MachineProfile().ExtrusionWidthMm;
        var skin = 8 * 0.2 * inner;
        var infill = 92 * 0.2 * inner * 0.15;
        Assert.Equal(wall, result.WallVolumeMm3, 0);
        Assert.Equal(skin, result.SkinVolumeMm3, 0);
        Assert.Equal(infill, result.InfillVolumeMm3, 0);
        Assert.Equal((wall + skin + infill) / 1000 * PlaDensity, result.WeightGrams, 2);
    }

    [Fact]
    public void WallsAreAlsoPrintedAroundACavity()
    {
        var (positions, indices) = Box(0, 0, 0, 30, 30, 30);
        Box(2, 2, 2, 28, 28, 28, positions, indices, inside: true);
        var hollow = new PrintMesh([.. positions], [.. indices]);

        var result = PrintEstimator.Estimate(hollow, new EstimateSettings { InfillPercent = 15 }, Exact);

        // 26 layers hold the cavity (both contours are walled), the 4 mm of floor and roof only the outer one
        var width = Exact.ExtrusionWidthMm;
        var expected = (130 * (120 + 104) + 20 * 120) * 2 * width * 0.2;
        Assert.Equal(30 * 30 * 30 - 26 * 26 * 26, result.ModelVolumeMm3, 0);
        Assert.Equal(expected, result.WallVolumeMm3, 0);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ADefaultProfileStaysCloseToThePureGeometry()
    {
        var calibrated = PrintEstimator.Estimate(Cube(), new EstimateSettings());
        var exact = PrintEstimator.Estimate(Cube(), new EstimateSettings(), Exact);

        Assert.InRange(calibrated.WeightGrams / exact.WeightGrams, 0.85, 1.0);
    }

    [Fact]
    public void AToothedOutlineTakesLongerThanASmoothOne()
    {
        // same footprint and height, but the second one is cut into 24 teeth: the head brakes at every corner
        var smooth = Cube(20);
        var (positions, indices) = (new List<Vector3>(), new List<int>());
        for (var tooth = 0; tooth < 24; tooth++)
        {
            var angle = tooth * Math.PI / 12;
            var x = (float)(10 + 8 * Math.Cos(angle));
            var y = (float)(10 + 8 * Math.Sin(angle));
            Box(x - 1.2f, y - 1.2f, 0, x + 1.2f, y + 1.2f, 20, positions, indices);
        }
        var scattered = new PrintMesh([.. positions], [.. indices]);

        var smoothSeconds = PrintEstimator.Estimate(smooth, new EstimateSettings { InfillPercent = 0 }).TimeSeconds;
        var turningSmooth = PrintEstimator.Estimate(smooth, new EstimateSettings { InfillPercent = 0 }).TurningRadians;
        var turningScattered = PrintEstimator.Estimate(scattered, new EstimateSettings { InfillPercent = 0 }).TurningRadians;

        Assert.True(smoothSeconds > 0);
        Assert.True(turningScattered > 5 * turningSmooth);
    }

    [Fact]
    public void WeightGrowsWithInfillAndWalls()
    {
        double Weight(double infill, int walls) => PrintEstimator.Estimate(Cube(), new EstimateSettings { InfillPercent = infill, WallCount = walls }).WeightGrams;

        Assert.True(Weight(0, 2) < Weight(15, 2));
        Assert.True(Weight(15, 2) < Weight(60, 2));
        Assert.True(Weight(15, 2) < Weight(15, 4));
    }

    [Fact]
    public void TimeGrowsWithFinerLayersAndMoreInfill()
    {
        double Seconds(double layer, double infill) => PrintEstimator.Estimate(Cube(), new EstimateSettings { LayerHeightMm = layer, InfillPercent = infill }).TimeSeconds;

        Assert.True(Seconds(0.1, 15) > Seconds(0.2, 15));
        Assert.True(Seconds(0.2, 15) > Seconds(0.3, 15));
        Assert.True(Seconds(0.2, 80) > Seconds(0.2, 15));
    }

    [Fact]
    public void AFasterHeadShortensTheTimeButNotTheWeight()
    {
        var average = PrintEstimator.Estimate(Cube(), new EstimateSettings());
        var fast = PrintEstimator.Estimate(Cube(), new EstimateSettings(), new MachineProfile { HeadSpeedFactor = 1.35 });
        var slow = PrintEstimator.Estimate(Cube(), new EstimateSettings(), new MachineProfile { HeadSpeedFactor = 0.6 });

        Assert.True(fast.TimeSeconds < average.TimeSeconds);
        Assert.True(slow.TimeSeconds > average.TimeSeconds);
        Assert.Equal(average.WeightGrams, fast.WeightGrams, 6);
    }

    [Fact]
    public void AMaterialThatMeltsSlowerTakesLongerOnTheSameMachine()
    {
        var pla = PrintEstimator.Estimate(Cube(), new EstimateSettings { MaterialMaxFlowMm3S = 21 });
        var petg = PrintEstimator.Estimate(Cube(), new EstimateSettings { MaterialMaxFlowMm3S = 12 });
        var tpu = PrintEstimator.Estimate(Cube(), new EstimateSettings { MaterialMaxFlowMm3S = 3.6 });

        Assert.True(petg.TimeSeconds > pla.TimeSeconds);
        Assert.True(tpu.TimeSeconds > 1.3 * petg.TimeSeconds);
    }

    [Fact]
    public void AFlowLimitedPrintLastsAsLongAsItsVolumeDividedByTheFlow()
    {
        // at 2 mm³/s nothing else matters much: 3 cm³ of plastic cannot be laid down in less than 25 minutes
        var result = PrintEstimator.Estimate(Cube(), new EstimateSettings(), new MachineProfile { HeadSpeedFactor = 2 }, cancellationToken: default);
        var limited = PrintEstimator.Estimate(Cube(), new EstimateSettings { MaterialMaxFlowMm3S = 2 }, new MachineProfile { HeadSpeedFactor = 2 });

        Assert.True(limited.TimeSeconds >= limited.PrintedVolumeMm3 / 2);
        Assert.True(limited.TimeSeconds > 3 * result.TimeSeconds);
    }

    [Fact]
    public void LayerHeightDoesNotChangeTheWeightOfASolidCube()
    {
        var fine = PrintEstimator.Estimate(Cube(), Solid() with { LayerHeightMm = 0.12 });
        var coarse = PrintEstimator.Estimate(Cube(), Solid() with { LayerHeightMm = 0.28 });

        Assert.Equal(fine.WeightGrams, coarse.WeightGrams, 1);
    }

    [Fact]
    public void FilamentLengthMatchesTheMeltedVolume()
    {
        var result = PrintEstimator.Estimate(Cube(), Solid());

        var expected = result.PrintedVolumeMm3 / (Math.PI * 0.875 * 0.875);
        Assert.Equal(expected, result.FilamentLengthMm, 1);
    }

    [Fact]
    public void AnInsideOutMeshGivesTheSameAnswerWithAWarning()
    {
        var cube = Cube();
        var flipped = (int[])cube.Indices.Clone();
        for (var t = 0; t < flipped.Length; t += 3) (flipped[t + 1], flipped[t + 2]) = (flipped[t + 2], flipped[t + 1]);

        var result = PrintEstimator.Estimate(new PrintMesh(cube.Positions, flipped), Solid(), Exact);

        Assert.Equal(8 * PlaDensity, result.WeightGrams, 2);
        Assert.Contains(EstimateWarning.InvertedMesh, result.Warnings);
    }

    [Fact]
    public void AMeshWithAHoleIsFlagged()
    {
        var cube = Cube();
        var withoutTop = cube.Indices.Take(6).Concat(cube.Indices.Skip(12)).ToArray();

        var result = PrintEstimator.Estimate(new PrintMesh(cube.Positions, withoutTop), Solid());

        Assert.Contains(EstimateWarning.OpenMesh, result.Warnings);
    }

    [Fact]
    public void AFlatMeshIsEmpty()
    {
        var flat = new PrintMesh([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [0, 1, 2]);

        var result = PrintEstimator.Estimate(flat, Solid());

        Assert.Equal(0, result.WeightGrams);
        Assert.Contains(EstimateWarning.EmptyModel, result.Warnings);
    }

    [Fact]
    public void SupportsHoldUpTheUndersideOfAnOverhang()
    {
        var mesh = PillarWithArm();
        var without = PrintEstimator.Estimate(mesh, new EstimateSettings { Supports = SupportMode.None });
        var with = PrintEstimator.Estimate(mesh, new EstimateSettings { Supports = SupportMode.Auto });

        // 20 × 10 mm of overhang, 10 mm above the bed, filled at the support density of the profile
        Assert.Equal(0, without.SupportVolumeMm3);
        Assert.Equal(20 * 10 * 10 * new MachineProfile().SupportDensity, with.SupportVolumeMm3, 3);
        Assert.True(with.WeightGrams > without.WeightGrams);
        Assert.True(with.TimeSeconds > without.TimeSeconds);
    }

    [Fact]
    public void ACubeNeedsNoSupport()
    {
        var result = PrintEstimator.Estimate(Cube(), new EstimateSettings { Supports = SupportMode.Auto });

        Assert.Equal(0, result.SupportVolumeMm3);
    }

    [Fact]
    public void AStricterAngleSupportsMore()
    {
        // a pyramid-like roof: a 45° face is supported only when the threshold is below 45°
        var positions = new List<Vector3> { new(0, 0, 0), new(20, 0, 0), new(20, 10, 0), new(0, 10, 0), new(0, 0, 20), new(0, 10, 20) };
        var wedge = new PrintMesh([.. positions], [0, 2, 1, 0, 3, 2, 0, 1, 4, 3, 5, 2, 0, 4, 5, 0, 5, 3, 1, 2, 5, 1, 5, 4]);
        var slope = new EstimateSettings { Supports = SupportMode.Auto, SupportAngleDegrees = 30 };

        var strict = PrintEstimator.Estimate(wedge, slope);
        var lenient = PrintEstimator.Estimate(wedge, slope with { SupportAngleDegrees = 60 });

        Assert.True(strict.SupportVolumeMm3 >= lenient.SupportVolumeMm3);
    }

    [Fact]
    public void ScalingBringsAModelToMillimetres()
    {
        var inches = Cube(1).Scaled(25.4f);

        var result = PrintEstimator.Estimate(inches, Solid());

        Assert.Equal(25.4 * 25.4 * 25.4, result.ModelVolumeMm3, 0);
    }

    [Fact]
    public void SettingsOutOfRangeAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintEstimator.Estimate(Cube(), new EstimateSettings { InfillPercent = 150 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintEstimator.Estimate(Cube(), new EstimateSettings { LayerHeightMm = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintEstimator.Estimate(Cube(), new EstimateSettings { WallCount = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintEstimator.Estimate(Cube(), new EstimateSettings { MaterialMaxFlowMm3S = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrintEstimator.Estimate(Cube(), new EstimateSettings(), new MachineProfile { HeadSpeedFactor = 0 }));
    }

    [Fact]
    public void ACancelledEstimateStops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => PrintEstimator.Estimate(Cube(), Solid(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void AMeshWithBadIndicesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new PrintMesh([new(0, 0, 0)], [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => new PrintMesh([new(0, 0, 0), new(float.NaN, 0, 0), new(0, 1, 0)], [0, 1, 2]));
    }
}
