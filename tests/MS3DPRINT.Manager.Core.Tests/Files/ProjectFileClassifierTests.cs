using MS3DPRINT.Manager.Core.Files;

namespace MS3DPRINT.Manager.Core.Tests.Files;

public sealed class ProjectFileClassifierTests
{
    [Theory]
    [InlineData("DEV2026-05.pdf", "MPO-2026-001_OUTILLAGE", "01_DEVIS_FACTURES", "DEV2026-05__MPO-2026-001.pdf")]
    [InlineData("MPO-STEP-25-GRADE-A.step", "MPO-2026-001_OUTILLAGE", "03_CAO_3D\\02_STEP", "MPO-STEP-25-GRADE-A__MPO-2026-001.step")]
    [InlineData("piece.STL", "MPO-2026-001_OUTILLAGE", "03_CAO_3D\\03_STL", "piece__MPO-2026-001.STL")]
    [InlineData("piece.3mf", "MPO-2026-001_OUTILLAGE", "04_IMPRESSION_3D\\02_3MF", "piece__MPO-2026-001.3mf")]
    public void Suggest_UsesOriginalStemProjectReferenceAndExtension(string source, string project, string folder, string expected)
    {
        var result = ProjectFileClassifier.Suggest(source, project);

        Assert.Equal(folder, result.RelativeDirectory);
        Assert.Equal(expected, result.FileName);
    }

    [Fact]
    public void Suggest_UsesClientFilesForUnknownExtensions()
    {
        var result = ProjectFileClassifier.Suggest("photo.jpg", "MPO-2026-001_OUTILLAGE");

        Assert.Equal("02_FICHIERS_CLIENT", result.RelativeDirectory);
        Assert.Equal("photo__MPO-2026-001.jpg", result.FileName);
    }

    [Fact]
    public void Suggest_RejectsProjectFoldersWithoutAValidReference()
    {
        Assert.Throws<ArgumentException>(() => ProjectFileClassifier.Suggest("DEV2026-05.pdf", "OUTILLAGE"));
    }
}
