using MS3DPRINT.Manager.Core.Projects;

namespace MS3DPRINT.Manager.Core.Tests.Projects;

public class ProjectReferenceGeneratorTests
{
    [Fact]
    public void Create_UsesNextSequenceForSameClientAndYear()
    {
        var names = new[] { "MPO-2026-001_ANCIEN", "MPO-2026-007_AUTRE", "MPO-2025-999_OLD", "SDIS72-2026-011_OTHER" };
        var result = ProjectReferenceGenerator.Create("MPO", 2026, names, "Outillage monocuvette");

        Assert.Equal("MPO", result.ClientCode);
        Assert.Equal(2026, result.Year);
        Assert.Equal(8, result.Sequence);
        Assert.Equal("OUTILLAGE_MONOCUVETTE", result.NormalizedProjectName);
        Assert.Equal("MPO-2026-008_OUTILLAGE_MONOCUVETTE", result.FolderName);
    }

    [Fact]
    public void Create_RejectsEmptyNormalizedProjectName()
        => Assert.Throws<ArgumentException>(() => ProjectReferenceGenerator.Create("MPO", 2026, [], "---"));

    [Theory]
    [InlineData("", "Project")]
    [InlineData("---", "Project")]
    public void Create_RejectsEmptyNormalizedClientCode(string code, string projectName)
        => Assert.Throws<ArgumentException>(() => ProjectReferenceGenerator.Create(code, 2026, [], projectName));

    [Fact]
    public void Create_IgnoresNamesWithoutExpectedPrefixAndSuffix()
    {
        var names = new[] { "MPO-2026-099", "XMPO-2026-098_OTHER", "MPO-2026-1000_OTHER", "MPO-2026-002_VALID" };

        var result = ProjectReferenceGenerator.Create("MPO", 2026, names, "New");

        Assert.Equal(3, result.Sequence);
    }

    [Fact]
    public void Create_RejectsSequenceBeyond999()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ProjectReferenceGenerator.Create("MPO", 2026, ["MPO-2026-999_OLD"], "New"));
}
