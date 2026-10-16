using System.Numerics;
using MS3DPRINT.Estimator;
using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Core.Tests.Desktop;

public sealed class ModelAssessmentTests
{
    private static MeshModel Cube(float size)
    {
        Vector3[] corners = [new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0), new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size)];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new MeshModel(corners, faces);
    }

    [Theory]
    [InlineData("0.20mm Standard @BBL X1C", 0.2)]
    [InlineData("0.12mm Fine @BBL A1", 0.12)]
    [InlineData("0.28mm Extra Draft @BBL P1P", 0.28)]
    [InlineData("Standard", 0.2)]
    [InlineData("", 0.2)]
    [InlineData(null, 0.2)]
    [InlineData("5mm trop épais", 0.2)]
    public void LayerHeightIsReadFromTheQualityName(string? quality, double expected) =>
        Assert.Equal(expected, ModelAssessment.LayerHeightFrom(quality));

    [Theory]
    [InlineData(1.2, 3)]
    [InlineData(0.8, 2)]
    [InlineData(0.4, 1)]
    [InlineData(0.1, 1)]
    [InlineData(0, 1)]
    public void WallThicknessBecomesAtLeastOneLoop(double wallMm, int loops) =>
        Assert.Equal(loops, ModelAssessment.WallLoopsFrom(wallMm));

    [Fact]
    public void AModelInInchesIsBroughtToMillimetres()
    {
        var print = ModelAssessment.ToPrintMesh(Cube(1), 25.4);

        Assert.Equal(25.4, print.Size.X, 3);
        Assert.Equal(25.4, print.Size.Z, 3);
    }

    [Fact]
    public void TheViewerMeshGoesThroughTheEstimatorAndTheFeasibilityCheck()
    {
        var print = ModelAssessment.ToPrintMesh(Cube(20), 1);

        var estimate = PrintEstimator.Estimate(print, new EstimateSettings { InfillPercent = 100 }, new MachineProfile { WallFlowFactor = 1, SkinFlowFactor = 1, InfillFlowFactor = 1 });
        var feasibility = FeasibilityAnalyzer.Analyze(print);

        Assert.Equal(8 * 1.24, estimate.WeightGrams, 2);
        Assert.Equal(FeasibilityVerdict.Printable, feasibility.Verdict);
        Assert.Equal("Imprimable", ModelAssessment.Summarize(feasibility));
    }

    [Fact]
    public void TheSummaryCountsTheFindingsThatAreNotJustNotes()
    {
        var flatPlate = new MeshModel(
            [new(0, 0, 0), new(30, 0, 0), new(30, 30, 0), new(0, 30, 0), new(0, 0, 0.3f), new(30, 0, 0.3f), new(30, 30, 0.3f), new(0, 30, 0.3f)],
            [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5]);

        var report = FeasibilityAnalyzer.Analyze(ModelAssessment.ToPrintMesh(flatPlate, 1));

        Assert.Equal(FeasibilityVerdict.NotPrintable, report.Verdict);
        Assert.Equal("À corriger avant impression (1 point à voir)", ModelAssessment.Summarize(report));
    }
}
